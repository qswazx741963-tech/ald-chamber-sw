namespace Ald.Core;

public enum TransferDirection
{
    /// <summary>Loadlock 手臂上的片送進 PM。</summary>
    LoadToPm,
    /// <summary>PM 的片取回 Loadlock。</summary>
    UnloadToLoadlock,
}

/// <summary>
/// 傳片流程（Loadlock 手臂 ↔ PM Wafer lift），用 SLK 的單步命令組成，每一步等狀態回來再走下一步：
///
/// 送片：PM 到交接位置（Heater module 下位、Wafer lift 下）→ 關 Turbo GV → 開 SV → 手臂進 PM
///       → Wafer lift 上（從手臂接片）→ 手臂退回 → 關 SV → 恢復 Turbo GV → Heater module 到 Process 位
/// 取片：Heater module 下位（片落回 lift pin）→ Wafer lift 上 → 關 Turbo GV → 開 SV → 手臂進 PM
///       → Wafer lift 下（片放到手臂）→ 手臂退回 → 關 SV → 恢復 Turbo GV
///
/// 不用 SLK 的 SEQ:LOAD（它在 PM 內只固定等 2 秒就退手臂，沒有跟 Wafer lift 交握）。
/// 互鎖一樣由主控板把關（LL-01~05、PM-04~06），這裡只負責順序與等待。
/// 失敗 / 中止：停手臂（SLK ABORT），**不自動往回動**（片可能在半路），晶圓位置設為不明，由人確認後處理。
/// </summary>
public sealed class TransferSequencer
{
    private readonly IMachine _m;
    private readonly WaferMap _map;
    private readonly TransferConfig _cfg;
    private readonly bool _zEnabled;
    private readonly TimeSpan _poll;
    private CancellationTokenSource? _cts;
    private bool _gvWasOpen;

    public SequencerState State { get; private set; } = SequencerState.Idle;
    public string Step { get; private set; } = "";
    public event Action<SequencerProgress>? Progress;

    public TransferSequencer(IMachine machine, WaferMap map, TransferConfig cfg, bool loadlockZEnabled, TimeSpan? poll = null)
    {
        _m = machine;
        _map = map;
        _cfg = cfg;
        _zEnabled = loadlockZEnabled;
        _poll = poll ?? TimeSpan.FromMilliseconds(100);
    }

    public void Abort() => _cts?.Cancel();

    /// <summary>執行前檢查（畫面也用來顯示「為什麼不能按」）。空 = 可以執行。</summary>
    public static IEnumerable<string> Precheck(TransferDirection dir, MachineSnapshot s, WaferMap map)
    {
        if (!s.AllOnline) yield return "主控板或控制板離線";
        if (s.SafeState) yield return "系統在安全狀態，請先 Reset";
        if (!s.LoadlockFresh) yield return "Loadlock 狀態不明（通訊）";
        if (s.AldRunning) yield return "ALD 製程執行中";
        if (s.LoadlockBusy) yield return "Loadlock 有其他序列在跑";
        if (map.Loadlock is null || map.Pm is null) yield return "晶圓位置不明，請工程師確認後在畫面設定";
        else if (dir == TransferDirection.LoadToPm)
        {
            if (map.Loadlock != true) yield return "Loadlock 手臂上沒有片";
            if (map.Pm != false) yield return "PM 裡已經有片";
        }
        else
        {
            if (map.Pm != true) yield return "PM 裡沒有片";
            if (map.Loadlock != false) yield return "Loadlock 手臂上已經有片";
        }
        if (!(s.ChamberPressureTorr < 1) && s.ChamberGaugeStatus != GaugeStatus.UnderRange) yield return "PM 不在真空（開 SV 需要 PM < 1 Torr）";
        if (!(s.LoadlockPressureUnderRange || s.LoadlockPressureTorr < 1)) yield return "Loadlock 不在真空（開 SV 需要 LL < 1 Torr，請先 LL 抽氣）";
        if (s.LoadlockVentOpen) yield return "Loadlock 洩壓閥開著";
        if (s.LoadlockLidOpen) yield return "Loadlock 艙蓋開著";
    }

    public async Task<SequencerState> RunAsync(TransferDirection dir, CancellationToken ct = default)
    {
        if (State == SequencerState.Running) throw new InvalidOperationException("傳片執行中");
        string name = dir == TransferDirection.LoadToPm ? "送片 LL→PM" : "取片 PM→LL";
        var pre = Precheck(dir, _m.Snapshot, _map).ToList();
        if (pre.Count > 0) { Report(SequencerState.Failed, "檢查", string.Join("；", pre)); return State; }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = _cts.Token;
        Report(SequencerState.Running, "開始", name);
        bool waferMoving = false;
        try
        {
            if (dir == TransferDirection.LoadToPm)
            {
                await PmToTransferAsync(liftUp: false, token);
                await OpenPathAsync(token);
                waferMoving = true;
                SetStep("Wafer lift 上（從手臂接片）");
                _m.SetWaferLift(true);
                await WaitAsync(s => s.WaferLiftUp, _cfg.LiftTimeoutS, "Wafer lift 沒有升到上位", token);
                _map.Set(false, true, $"{name}：片已交到 PM");
                waferMoving = false;
                await ClosePathAsync(token);
                SetStep("Heater module 到 Process 位");
                await _m.HeaterMoveAsync(_cfg.HeaterProcessSteps, _cfg.HeaterSpeedSps, token);
                await WaitAsync(s => s.HeaterAtProcess && s.HeaterAxis == AxisState.Idle, _cfg.HeaterTimeoutS, "Heater module 沒有到 Process 位", token);
            }
            else
            {
                await PmToTransferAsync(liftUp: true, token);
                await OpenPathAsync(token);
                waferMoving = true;
                SetStep("Wafer lift 下（片放到手臂）");
                _m.SetWaferLift(false);
                await WaitAsync(s => s.WaferLiftDown, _cfg.LiftTimeoutS, "Wafer lift 沒有降到下位", token);
                _map.Set(true, false, $"{name}：片已交到手臂");
                waferMoving = false;
                await ClosePathAsync(token);
            }
            Report(SequencerState.Done, "完成", name);
        }
        catch (OperationCanceledException)
        {
            Stop(waferMoving, $"{name} 中止");
            Report(SequencerState.Aborted, Step, "使用者中止，手臂已停；不自動往回動，請確認晶圓與手臂位置");
        }
        catch (Exception e)
        {
            Stop(waferMoving, $"{name} 失敗");
            Report(SequencerState.Failed, Step, e.Message);
        }
        return State;
    }

    // ------------------------------------------------------------ 步驟
    private async Task PmToTransferAsync(bool liftUp, CancellationToken ct)
    {
        var s = _m.Snapshot;
        if (!s.HeaterDown || s.HeaterAxis != AxisState.Idle)
        {
            SetStep("Heater module 回下位");
            Require(s.ArmClearOfSv, "手臂不在安全位置，不能移動 Heater module");
            await _m.HeaterHomeAsync(_cfg.HeaterSpeedSps, ct);
            await WaitAsync(x => x.HeaterDown && x.HeaterAxis == AxisState.Idle, _cfg.HeaterTimeoutS, "Heater module 沒有回到下位", ct);
        }
        bool ok = liftUp ? _m.Snapshot.WaferLiftUp : _m.Snapshot.WaferLiftDown;
        if (!ok)
        {
            SetStep(liftUp ? "Wafer lift 上（把片頂離 Heater）" : "Wafer lift 下（準備接片）");
            _m.SetWaferLift(liftUp);
            await WaitAsync(x => liftUp ? x.WaferLiftUp : x.WaferLiftDown, _cfg.LiftTimeoutS, "Wafer lift 沒有到位", ct);
        }
    }

    private async Task OpenPathAsync(CancellationToken ct)
    {
        if (_zEnabled && _m.Snapshot.ArmZ != LoadlockZ.Down)
        {
            SetStep("Loadlock Z 下降");
            await Ll(LoadlockCommand.ZDown, 0, ct);
            await WaitAsync(s => s.ArmZ == LoadlockZ.Down, _cfg.LoadlockStepTimeoutS, "Loadlock Z 沒有降到底", ct);
        }
        SetStep("關 Turbo GV");
        _gvWasOpen = _m.VacuumCommand.HasFlag(VacuumOutputs.TurboGv);
        _m.SetVacuum(_m.VacuumCommand & ~VacuumOutputs.TurboGv);
        await WaitAsync(s => s.TurboGateClosed, 30, "Turbo GV 沒有關", ct);

        if (_m.Snapshot.LoadlockRoughOpen)
        {
            SetStep("關 Loadlock 粗抽閥");   // 粗抽閥開著不准開 SV（LL_VALVE_CONFLICT）
            await Ll(LoadlockCommand.RoughOff, 0, ct);
            await WaitAsync(s => !s.LoadlockRoughOpen, _cfg.LoadlockStepTimeoutS, "Loadlock 粗抽閥沒有關", ct);
        }

        SetStep("開 SV");
        await Ll(LoadlockCommand.SvOpen, 0, ct);
        await WaitAsync(s => s.SlitValve == LoadlockSv.Open, _cfg.LoadlockStepTimeoutS, "SV 沒有全開", ct);

        SetStep("手臂進 PM");
        await Ll(LoadlockCommand.ArmToPm, 0, ct);
        await WaitAsync(s => !s.ArmMoving && s.ArmPosSteps > s.ArmSafePosSteps, _cfg.LoadlockStepTimeoutS, "手臂沒有到 PM 交接位置", ct);
    }

    private async Task ClosePathAsync(CancellationToken ct)
    {
        SetStep("手臂退回 Loadlock");
        await Ll(LoadlockCommand.ArmToLoadlock, 0, ct);
        await WaitAsync(s => s.ArmClearOfSv, _cfg.LoadlockStepTimeoutS, "手臂沒有退出 SV", ct);

        SetStep("關 SV");
        await Ll(LoadlockCommand.SvClose, 0, ct);
        await WaitAsync(s => s.SlitClosed, _cfg.LoadlockStepTimeoutS, "SV 沒有關到位", ct);

        if (_gvWasOpen && _m.Snapshot.TurboAtSpeed)
        {
            SetStep("恢復 Turbo GV");
            _m.SetVacuum(_m.VacuumCommand | VacuumOutputs.TurboGv);
            await WaitAsync(s => s.TurboGateOpened, 30, "Turbo GV 沒有打開", ct);
        }
    }

    // ------------------------------------------------------------ 工具
    private async Task Ll(LoadlockCommand cmd, int arg, CancellationToken ct)
    {
        if (!await _m.LoadlockAsync(cmd, arg, TimeSpan.FromSeconds(_cfg.LoadlockStepTimeoutS), ct))
            throw new SequenceException($"Loadlock {cmd} 沒有完成：{_m.LastLoadlockError}");
    }

    private void Stop(bool waferMoving, string why)
    {
        try { _ = _m.LoadlockAsync(LoadlockCommand.Abort, 0, TimeSpan.FromSeconds(2), CancellationToken.None); }
        catch (Exception) { /* 通訊中斷時主控板看門狗會讓全機進安全狀態 */ }
        if (waferMoving) _map.Set(null, null, $"{why}：交接途中停止，晶圓位置不明");
    }

    private void CheckHealthy()
    {
        var s = _m.Snapshot;
        if (!s.AllOnline) throw new SequenceException("主控板或控制板離線");
        if (s.SafeState) throw new SequenceException("系統進入安全狀態");
        if (!s.LoadlockFresh) throw new SequenceException("Loadlock 通訊中斷");
    }

    private static void Require(bool ok, string why) { if (!ok) throw new SequenceException(why); }

    private async Task WaitAsync(Func<MachineSnapshot, bool> cond, double timeoutS, string failMsg, CancellationToken ct)
    {
        var deadline = DateTime.Now.AddSeconds(timeoutS);
        while (!cond(_m.Snapshot))
        {
            ct.ThrowIfCancellationRequested();
            CheckHealthy();
            if (DateTime.Now > deadline) throw new SequenceException(failMsg);
            await Task.Delay(_poll, ct);
        }
    }

    private void SetStep(string step)
    {
        if (step == Step) return;
        Report(SequencerState.Running, step, "");
    }

    private void Report(SequencerState st, string step, string msg)
    {
        State = st;
        Step = step;
        Progress?.Invoke(new SequencerProgress(st, step, msg, DateTime.Now));
    }

    private sealed class SequenceException(string message) : Exception(message);
}
