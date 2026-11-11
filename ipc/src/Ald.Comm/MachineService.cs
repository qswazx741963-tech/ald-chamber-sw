using Ald.Comm.Generated;
using Ald.Core;

namespace Ald.Comm;

/// <summary>
/// IPC 底層的機台服務（人機、流程引擎、記錄都只透過它碰機台）：
///   - 連線 / 心跳（MainBoardLink，連本機 ald-ecat → EtherCAT → 主控板）
///   - 每次連上：把 config（互鎖參數、壓力計、加熱上限）寫進主控板，並讀回目前的命令值
///   - 每輪輪詢：主控板那張表 → MachineSnapshot、警報判斷
///   - 命令：真空閥、加熱、MFC、ALD、馬達、Loadlock、Reset / 進安全狀態
/// 所有命令都是「請求」，主控板與執行板還會再過互鎖。
/// </summary>
public sealed class MachineService : IMachine, IDisposable
{
    private readonly MachineConfig _cfg;
    private readonly MainBoardLink _link;
    private readonly object _cmdLock = new();
    private volatile MachineSnapshot _snap = new() { SafeState = true };
    private VacuumOutputs _vac;
    private ushort _procDo, _heaterEnable, _motDo;

    public MachineConfig Config => _cfg;
    public LoadlockService Loadlock { get; }
    public AlarmManager Alarms { get; }
    public MachineSnapshot Snapshot => _snap;
    public VacuumOutputs VacuumCommand { get { lock (_cmdLock) return _vac; } }

    /// <summary>每輪輪詢後（背景執行緒）。</summary>
    public event Action<MachineSnapshot>? Updated;
    /// <summary>警報發生 / 解除（背景執行緒）。</summary>
    public event Action<AlarmChange>? AlarmChanged;

    public MachineService(MachineConfig cfg)
    {
        _cfg = cfg;
        Interlocks.Settings = cfg.ToInterlockSettings();
        var slots = cfg.Slots.Select(s => new SlotImage
        {
            Slot = s.Slot,
            ExpectedType = Enum.Parse<BoardType>(s.Type, ignoreCase: true),
            InputSize = SizeOf(s.Slot),
        });
        _link = new MainBoardLink(cfg.MainBoard.Host, cfg.MainBoard.Port, cfg.MainBoard.UnitId, slots);
        _link.Connecting += OnConnecting;
        _link.Updated += OnUpdated;
        Loadlock = new LoadlockService(_link, () => _snap) { ArmSafePosSteps = cfg.Interlock.ArmSafePosSteps };
        Alarms = new AlarmManager(cfg, DenyReasons.Describe);
    }

    private static ushort SizeOf(int slot) => slot switch
    {
        0 => MainBoard.Input.Size,
        ProcessBoard.Slot => ProcessBoard.Input.Size,
        HeaterBoard.Slot => HeaterBoard.Input.Size,
        VacuumBoard.Slot => VacuumBoard.Input.Size,
        MotionBoard.Slot => MotionBoard.Input.Size,
        _ => throw new ArgumentOutOfRangeException(nameof(slot)),
    };

    public void Start() => _link.Start(TimeSpan.FromMilliseconds(_cfg.MainBoard.PollMs));

    // ------------------------------------------------------------ 連線與輪詢
    private void OnConnecting(MainBoardLink link)
    {
        WriteConfig(link);
        // 讀回命令值：ald-ecat 重開過的話全部是 0，IPC 這邊的記憶要跟著同步，避免下一次寫入把舊命令又打開
        lock (_cmdLock)
        {
            _vac = (VacuumOutputs)link.ReadHolding(VacuumBoard.Holding.DoCmd, 1)[0];
            _procDo = link.ReadHolding(ProcessBoard.Holding.DoCmd, 1)[0];
            _heaterEnable = link.ReadHolding(HeaterBoard.Holding.HeaterEnable, 1)[0];
            var sp = link.ReadHolding(HeaterBoard.Holding.HeaterSp, 16);
            for (int i = 0; i < 16; i++) _heaterSp[i] = sp[i] * HeaterBoard.Holding.HeaterSpScale;
            HeaterSetpointsKnown = true;
            _motDo = link.ReadHolding(MotionBoard.Holding.DoCmd, 1)[0];
            _tower = null;   // 重新連上後重寫一次燈號
        }
    }

    /// <summary>config → 主控板 HR（每次連上都寫，ald-ecat / 主控板重開後也會恢復）。</summary>
    private void WriteConfig(MainBoardLink link)
    {
        var il = _cfg.Interlock;
        link.Write(MainBoard.Holding.WatchdogMs, (ushort)_cfg.WatchdogMs);
        link.Write(MainBoard.Holding.IlSvDpMaxMtorr, (ushort)il.SvMaxDiffMtorr,
            (ushort)(il.LoadlockZEnabled ? MainBoard.Holding.IlLlZEnableValues.Enabled : MainBoard.Holding.IlLlZEnableValues.Disabled),
            (ushort)il.LoadlockStaleMs,
            (ushort)(il.ArmSafePosSteps >> 16), (ushort)il.ArmSafePosSteps,
            (ushort)il.SvOpenMode,
            (ushort)Math.Round(il.SvBelowTorr * 1000),
            (ushort)Math.Round(il.LoadlockJumpTorr * 1000));
        var pm = _cfg.Gauges.Find(_cfg.Gauges.PmInterlockGauge)!;
        WriteGauge(link, MainBoard.Holding.PmGaugeModel, pm);
        WriteGauge(link, MainBoard.Holding.LlGaugeModel, _cfg.Gauges.LoadlockGauge);
        var lim = _cfg.Heaters.Channels.Select(h => Scaling.ToRaw(h.LimitDegC, HeaterBoard.Holding.HeaterLimitScale,
            HeaterBoard.Holding.HeaterLimitMin, HeaterBoard.Holding.HeaterLimitMax)).ToArray();
        link.Write(HeaterBoard.Holding.HeaterLimit, lim);
    }

    /// <summary>寫 MODEL、AI_ADDR、FS_MTORR（i32）四個連續暫存器。</summary>
    private static void WriteGauge(MainBoardLink link, ushort modelAddr, GaugeConfig g)
    {
        int fs = (int)Math.Round(g.FullScaleTorr * 1000);
        link.Write(modelAddr, (ushort)g.Model, GaugeAddress(g), (ushort)(fs >> 16), (ushort)fs);
    }

    /// <summary>壓力計電壓所在的 IR 絕對位址（主控板用來換算互鎖壓力）。</summary>
    public static ushort GaugeAddress(GaugeConfig g)
    {
        if (g.Model == GaugeModel.None) return 0;
        if (g.Register?.ToUpperInvariant() == "MANOMETER_RAW") return ProcessBoard.Input.ManometerRaw;
        if (g.AiIndex is >= 0 and < 8 && g.Slot == VacuumBoard.Slot) return (ushort)(VacuumBoard.Input.AiRaw + g.AiIndex.Value);
        throw new InvalidDataException($"壓力計位置設定錯誤（slot {g.Slot}, aiIndex {g.AiIndex}, register {g.Register}）");
    }

    private void OnUpdated(MainBoardLink link)
    {
        var imgs = link.Slots.ToDictionary(s => s.Slot);
        ushort[]? Ir(int slot) => imgs.TryGetValue(slot, out var s) && s.Online ? s.Input : null;
        bool pending = Loadlock.LastResult == MainBoard.Input.LlCmdResultValues.Sent;
        var snap = SnapshotBuilder.Build(link.Connected, Ir, _cfg, pending);
        _snap = snap;
        foreach (var c in Alarms.Update(snap)) AlarmChanged?.Invoke(c);
        Updated?.Invoke(snap);
    }

    // ------------------------------------------------------------ 系統
    /// <summary>
    /// 解除安全狀態：先主控板（它會先放開 SAFE 線確認沒有別人在拉），再各執行板。
    /// 原因沒排除（EMO、通訊、SAFE 線）時主控板不會解除。
    /// </summary>
    public async Task ResetAsync(CancellationToken ct = default)
    {
        _link.Write(MainBoard.Holding.Command, (ushort)MainBoard.Holding.CommandValues.ResetAlarm);
        await Task.Delay(200, ct);
        foreach (var slot in new[] { ProcessBoard.Slot, HeaterBoard.Slot, VacuumBoard.Slot, MotionBoard.Slot })
            _link.Write((ushort)(slot * RegisterMap.SlotStride + 1), (ushort)MainBoard.Holding.CommandValues.ResetAlarm);
        await Task.Delay(300, ct);
    }

    /// <summary>軟體觸發全機安全狀態（等同主控板收到 ENTER_SAFE）。</summary>
    public void EnterSafe() => _link.Write(MainBoard.Holding.Command, (ushort)MainBoard.Holding.CommandValues.EnterSafe);

    private TowerLamp? _tower;

    /// <summary>燈塔 / 蜂鳴器命令（有變化才寫）。主控板安全狀態會強制紅燈、IPC 斷線時只留紅燈。</summary>
    public void SetTower(TowerLamp lamps)
    {
        lock (_cmdLock)
        {
            if (_tower == lamps || !_link.Connected) return;
            _link.Write(MainBoard.Holding.DoCmd, (ushort)lamps);
            _tower = lamps;
        }
    }

    // ------------------------------------------------------------ IMachine
    public void SetVacuum(VacuumOutputs outputs)
    {
        lock (_cmdLock)
        {
            _link.Write(VacuumBoard.Holding.DoCmd, (ushort)outputs);
            _vac = outputs;
        }
    }

    private readonly double[] _heaterSp = new double[16];

    /// <summary>已經從加熱板讀回過設定（連上後才是 true；之前 HeaterSetpoint 全是 null，不能拿來比對常駐設定）。</summary>
    public bool HeaterSetpointsKnown { get; private set; }

    /// <summary>加熱板目前的設定（連上時從加熱板讀回，之後跟著 SetHeater 更新）；null = 該組沒開。</summary>
    public double? HeaterSetpoint(int ch)
    {
        lock (_cmdLock) return (_heaterEnable >> ch & 1) != 0 ? _heaterSp[ch] : null;
    }

    public void SetHeater(int ch, double spDegC, bool enable, double rampDegCPerMin = 0)
    {
        if (ch is < 0 or > 15) throw new ArgumentOutOfRangeException(nameof(ch));
        double limit = _cfg.Heaters.Channels[ch].LimitDegC;
        if (enable && spDegC > limit)
            throw new ArgumentOutOfRangeException(nameof(spDegC), $"{_cfg.Heaters.Channels[ch].Name} 設定 {spDegC} °C 超過上限 {limit} °C");
        var sp = Scaling.ToRaw(spDegC, HeaterBoard.Holding.HeaterSpScale, HeaterBoard.Holding.HeaterSpMin, HeaterBoard.Holding.HeaterSpMax);
        var ramp = Scaling.ToRaw(rampDegCPerMin, HeaterBoard.Holding.HeaterRampScale, HeaterBoard.Holding.HeaterRampMin, HeaterBoard.Holding.HeaterRampMax);
        lock (_cmdLock)
        {
            _link.Write((ushort)(HeaterBoard.Holding.HeaterSp + ch), sp);
            _link.Write((ushort)(HeaterBoard.Holding.HeaterRamp + ch), ramp);
            var en = enable ? (ushort)(_heaterEnable | 1 << ch) : (ushort)(_heaterEnable & ~(1 << ch));
            _link.Write(HeaterBoard.Holding.HeaterEnable, en);
            _heaterEnable = en;
            _heaterSp[ch] = sp * HeaterBoard.Holding.HeaterSpScale;
        }
    }

    public void SetMfc(int ch, double value, bool upstreamValve)
    {
        if (ch is < 0 or > 7) throw new ArgumentOutOfRangeException(nameof(ch));
        double fs = _cfg.Mfc.Channels[ch].FullScale;
        double pct = fs > 0 ? value / fs * 100.0 : 0;
        var raw = Scaling.ToRaw(pct, ProcessBoard.Holding.MfcSpScale, ProcessBoard.Holding.MfcSpMin, ProcessBoard.Holding.MfcSpMax);
        lock (_cmdLock)
        {
            _link.Write((ushort)(ProcessBoard.Holding.MfcSp + ch), raw);
            int bit = 7 + ch;   // MFC_UP_V1 = bit 7
            var d = upstreamValve ? (ushort)(_procDo | 1 << bit) : (ushort)(_procDo & ~(1 << bit));
            _link.Write(ProcessBoard.Holding.DoCmd, d);
            _procDo = d;
        }
    }

    public void SetAldValves(ushort mask)
    {
        if (mask > 0x7F) throw new ArgumentOutOfRangeException(nameof(mask), "只有 ALD_V1~V7");
        lock (_cmdLock)
        {
            var d = (ushort)((_procDo & ~0x7F) | mask);
            _link.Write(ProcessBoard.Holding.DoCmd, d);
            _procDo = d;
        }
    }

    /// <summary>最後下載到製程板的 ALD 步驟表（保養計數用來算每顆 ALD 閥的動作次數）。</summary>
    public AldRecipe? LastAld { get; private set; }

    public void DownloadAld(AldRecipe r)
    {
        var errors = r.Validate().ToList();
        if (errors.Count > 0) throw new ArgumentException(string.Join("；", errors));
        LastAld = r;
        _link.Write(ProcessBoard.Holding.AldCycles, (ushort)r.Cycles, (ushort)r.Steps.Count);
        _link.Write(ProcessBoard.Holding.AldStepValves, r.Steps.Select(s => s.ValveMask).ToArray());
        _link.Write(ProcessBoard.Holding.AldStepMs, r.Steps.Select(s => (ushort)s.DurationMs).ToArray());
    }

    public void StartAld()
    {
        var check = Interlocks.Check(MachineAction.AldStart, _snap);
        if (!check.Allowed) throw new InvalidOperationException(check.Reason);
        _link.Write(ProcessBoard.Holding.Command, (ushort)ProcessBoard.Holding.CommandValues.Start);
    }

    public void StopAld() => _link.Write(ProcessBoard.Holding.Command, (ushort)ProcessBoard.Holding.CommandValues.Stop);

    public string LastLoadlockError { get; private set; } = "";

    public async Task<bool> LoadlockAsync(LoadlockCommand cmd, int arg, TimeSpan timeout, CancellationToken ct)
    {
        LastLoadlockError = "";
        var r = Loadlock.Execute(cmd, arg);
        if (!r.Allowed) { LastLoadlockError = r.Reason; return false; }
        await Task.Delay(100, ct);
        if (await Loadlock.WaitAsync(timeout)) return true;
        LastLoadlockError = Loadlock.LastResult switch
        {
            MainBoard.Input.LlCmdResultValues.Denied => "主控板擋下：" + DenyReasons.Describe(_snap.LastDenyBits),
            MainBoard.Input.LlCmdResultValues.Busy => "Loadlock 忙碌中（有其他序列在跑）",
            MainBoard.Input.LlCmdResultValues.Error => "SLK 回報錯誤",
            _ => $"{timeout.TotalSeconds:0} 秒內沒有完成",
        };
        return false;
    }

    // ------------------------------------------------------------ 馬達（PM）
    /// <summary>Heater module 原點復歸（回下位）。AXIS_CMD 是持續值，先清回 NONE 再下才算新命令。</summary>
    public async Task HeaterHomeAsync(ushort speedSps, CancellationToken ct = default)
        => await AxisCommandAsync(MotionBoard.Holding.AxisCmdValues.Home, null, speedSps, ct);

    public async Task HeaterMoveAsync(int targetSteps, ushort speedSps, CancellationToken ct = default)
        => await AxisCommandAsync(MotionBoard.Holding.AxisCmdValues.Move, targetSteps, speedSps, ct);

    private async Task AxisCommandAsync(MotionBoard.Holding.AxisCmdValues cmd, int? target, ushort speed, CancellationToken ct)
    {
        var check = Interlocks.Check(MachineAction.HeaterMove, _snap);
        if (!check.Allowed) throw new InvalidOperationException(check.Reason);
        _link.Write(MotionBoard.Holding.AxisCmd, (ushort)MotionBoard.Holding.AxisCmdValues.None);
        await Task.Delay(Math.Max(50, _cfg.MainBoard.PollMs), ct);
        if (target is { } t) _link.Write(MotionBoard.Holding.AxisTarget, (ushort)(t >> 16), (ushort)t);
        _link.Write(MotionBoard.Holding.AxisSpeed, speed);
        _link.Write(MotionBoard.Holding.AxisCmd, (ushort)cmd);
    }

    public void SetWaferLift(bool up)
    {
        var check = Interlocks.Check(MachineAction.WaferLiftMove, _snap);
        if (!check.Allowed) throw new InvalidOperationException(check.Reason);
        lock (_cmdLock)
        {
            _motDo = (ushort)(up ? MotionBoard.Holding.DoCmdBits.WaferLiftUp : MotionBoard.Holding.DoCmdBits.WaferLiftDown);
            _link.Write(MotionBoard.Holding.DoCmd, _motDo);
        }
    }

    public void Dispose() => _link.Dispose();
}
