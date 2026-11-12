namespace Ald.Core;

public enum AlarmLevel { Info = 0, Warning = 1, Alarm = 2 }

/// <param name="Code">固定代碼（記錄 / 查詢用），例 "LINK", "SLOT3_OFFLINE", "TC07_BROKEN"</param>
public sealed record AlarmInfo(string Code, AlarmLevel Level, string Source, string Message);

public sealed record ActiveAlarm(AlarmInfo Info, DateTime Since)
{
    /// <summary>已按「確認 / 消音」。</summary>
    public bool Acked { get; init; }
}

public sealed record AlarmChange(AlarmInfo Info, bool Raised, DateTime Time);

/// <summary>
/// 依 MachineSnapshot 判斷目前有哪些警報，跟上一次比較產生「發生 / 解除」事件（給畫面、記錄、燈號）。
/// 這裡只「判斷與通知」，所有保護動作已經由主控板 / 執行板處理（IPC 掛掉也一樣有效）。
/// 純邏輯，不碰網路與資料庫，方便單元測試。
/// </summary>
public sealed class AlarmManager
{
    private readonly Dictionary<string, ActiveAlarm> _active = new();
    private readonly string[] _heaterNames;
    private readonly bool[] _heaterUsed;
    private readonly Func<uint, string> _denyText;
    private uint _lastDeny;
    private ushort _lastDenyAction;

    /// <param name="denyText">主控板 IL_DENY 位元 → 文字（Ald.Comm.DenyReasons.Describe，用產生檔的位元定義）</param>
    public AlarmManager(MachineConfig cfg, Func<uint, string>? denyText = null)
    {
        _denyText = denyText ?? (b => $"0x{b:X8}");
        _heaterNames = cfg.Heaters.Channels.Select(h => h.Name).ToArray();
        _heaterUsed = cfg.Heaters.Channels.Select(h => h.LimitDegC > 0).ToArray();
    }

    private readonly object _lock = new();
    // 已經自己消失、但還沒人確認過的異常（Alarm 等級）：燈塔維持紅燈閃爍 + 蜂鳴，直到按確認
    private readonly Dictionary<string, ActiveAlarm> _latched = new();
    private DateTime? _connectedSince;
    /// <summary>程式啟動後第一次連上的這段時間內消失的異常不留存（開機時各板陸續上線的暫時警報，不要一開機就叫）。</summary>
    public static readonly TimeSpan StartupGrace = TimeSpan.FromSeconds(10);

    public IReadOnlyCollection<ActiveAlarm> Active { get { lock (_lock) return _active.Values.ToList(); } }
    /// <summary>已消失但沒確認過的異常。</summary>
    public IReadOnlyCollection<ActiveAlarm> Latched { get { lock (_lock) return _latched.Values.ToList(); } }
    public bool AnyAlarm { get { lock (_lock) return _active.Values.Any(a => a.Info.Level == AlarmLevel.Alarm); } }
    public bool AnyWarning { get { lock (_lock) return _active.Values.Any(a => a.Info.Level == AlarmLevel.Warning); } }
    public bool UnackedAlarm { get { lock (_lock) return _latched.Count > 0 || _active.Values.Any(a => !a.Acked && a.Info.Level == AlarmLevel.Alarm); } }
    public bool UnackedWarning { get { lock (_lock) return _active.Values.Any(a => !a.Acked && a.Info.Level == AlarmLevel.Warning); } }

    /// <summary>確認 / 消音：目前所有警報標為已確認，已消失的異常清掉。回傳確認了幾筆。</summary>
    public int Acknowledge()
    {
        lock (_lock)
        {
            int n = _latched.Count;
            _latched.Clear();
            foreach (var k in _active.Keys.ToList())
                if (!_active[k].Acked) { _active[k] = _active[k] with { Acked = true }; n++; }
            return n;
        }
    }

    /// <summary>IPC 這邊的暫存器表版本（Ald.Comm 啟動時設成 RegisterMap.MapVersion；Ald.Core 不參考 Ald.Comm）。</summary>
    public static int ExpectedMapVersion { get; set; }

    private static readonly string[] SlotNames = { "主控板", "製程板", "加熱板", "真空板", "馬達板" };

    /// <summary>目前快照該有的警報。</summary>
    public IEnumerable<AlarmInfo> Evaluate(MachineSnapshot s)
    {
        if (!s.Connected)
        {
            yield return new("LINK", AlarmLevel.Alarm, "通訊", "主控板未連線（ald-ecat 主站程式未啟動或 EtherCAT 中斷）");
            yield break;
        }
        if (!s.SlotOnline[0])
        {
            yield return s.MapVersionSeen > 0 && s.MapVersionSeen != ExpectedMapVersion
                ? new("MAP_VERSION", AlarmLevel.Alarm, "主控板",
                      $"暫存器表版本不同：主控板（或 ald-ecat 虛擬機台）是 v{s.MapVersionSeen}，人機是 v{ExpectedMapVersion}。請關掉舊版程式 / 更新主控板韌體後重開")
                : new("SLOT0_OFFLINE", AlarmLevel.Alarm, "主控板", "主控板沒有正常回應（板型 / 版本不符或還在開機）");
            yield break;
        }
        for (int i = 1; i < s.SlotOnline.Length; i++)
            if (!s.SlotOnline[i]) yield return new($"SLOT{i}_OFFLINE", AlarmLevel.Alarm, SlotNames[i], $"{SlotNames[i]}（槽 {i}）離線");
        for (int i = 0; i < s.SlotStatus.Length; i++)
        {
            if (!s.SlotOnline[i]) continue;
            if ((s.SlotStatus[i] & 1 << 3) != 0) yield return new($"SLOT{i}_WDT", AlarmLevel.Alarm, SlotNames[i], $"{SlotNames[i]}看門狗觸發（上層通訊中斷過）");
            if ((s.SlotStatus[i] & 1 << 4) != 0) yield return new($"SLOT{i}_PARAM", AlarmLevel.Warning, SlotNames[i], $"{SlotNames[i]}拒絕參數（設定值超出範圍）");
        }
        if (!s.EmoOk) yield return new("EMO", AlarmLevel.Alarm, "主控板", "緊急停止按下（EMO）");
        else if (!s.SafeLineOk && !s.SafeState) yield return new("SAFE_LINE", AlarmLevel.Alarm, "主控板", "背板 SAFE 線被拉低");
        if (!s.CdaOk) yield return new("CDA", AlarmLevel.Alarm, "主控板", "CDA 壓力不足");
        if (s.SafeState) yield return new("SAFE", AlarmLevel.Warning, "系統", "安全狀態（排除原因後按 Reset）");

        // 真空
        if (s.SlotOnline[3])
        {
            if (s.DryPumpAlarm) yield return new("DRYPUMP", AlarmLevel.Alarm, "真空", "Dry pump 警報");
            if (s.TurboAlarm) yield return new("TURBO", AlarmLevel.Alarm, "真空", "Turbo pump 警報");
            if (s.VacuumBlocked != VacuumOutputs.None)
                yield return new("VAC_BLOCKED", AlarmLevel.Warning, "真空", $"真空板互鎖擋下：{s.VacuumBlocked}");
            if (!s.ChamberLidClosed) yield return new("PM_LID", AlarmLevel.Warning, "真空", "Chamber lid 未關");
            if (s.ChamberGaugeStatus is GaugeStatus.Fault or GaugeStatus.Offline or GaugeStatus.NotConfigured)
                yield return new("PM_GAUGE", AlarmLevel.Alarm, "真空", $"PM 互鎖壓力計異常（{s.ChamberGaugeStatus}）");
        }
        // 加熱
        if (s.SlotOnline[2])
        {
            for (int i = 0; i < s.TempC.Length && i < _heaterUsed.Length; i++)
                if (_heaterUsed[i] && double.IsNaN(s.TempC[i]))
                    yield return new($"TC{i + 1:00}_BROKEN", AlarmLevel.Alarm, "加熱", $"{_heaterNames[i]} 熱電偶斷線");
            if (s.OverTemp != HeaterOverTemp.None)
                yield return new("OVERTEMP", AlarmLevel.Alarm, "加熱", $"過溫開關跳脫：{s.OverTemp}");
        }
        // 馬達
        if (s.SlotOnline[4] && s.HeaterAxis == AxisState.Error)
            yield return new("HEATER_AXIS", AlarmLevel.Alarm, "馬達", "Heater module 驅動器異常（請原點復歸）");
        // Loadlock
        if (!s.LoadlockFresh) yield return new("LL_LINK", AlarmLevel.Alarm, "Loadlock", "Loadlock 通訊中斷（RS-485 / SLK 無回應）");
        else
        {
            if (s.LoadlockAlarm != 0) yield return new("LL_ALARM", AlarmLevel.Alarm, "Loadlock", $"Loadlock 警報（代碼 {s.LoadlockAlarm}）");
            if (s.LoadlockPressureSuspect) yield return new("LL_GAUGE", AlarmLevel.Warning, "Loadlock", "Loadlock 壓力讀值不可信（疑似斷線）");
        }
    }

    /// <summary>更新警報表，回傳這次的變化（發生 / 解除）。互鎖擋下是一次性事件（Info），不會留在警報表。</summary>
    public IReadOnlyList<AlarmChange> Update(MachineSnapshot s)
    {
        var now = s.Time;
        var changes = new List<AlarmChange>();
        var current = Evaluate(s).GroupBy(a => a.Code).ToDictionary(g => g.Key, g => g.First());
        if (s.Connected) _connectedSince ??= now;   // 只看程式啟動後第一次連上
        bool latch = _connectedSince is { } since && now - since >= StartupGrace;
        lock (_lock)
        {
            foreach (var (code, info) in current)
                if (!_active.ContainsKey(code)) { _active[code] = new(info, now); changes.Add(new(info, true, now)); _latched.Remove(code); }
            foreach (var code in _active.Keys.Where(k => !current.ContainsKey(k)).ToList())
            {
                var a = _active[code];
                changes.Add(new(a.Info, false, now));
                _active.Remove(code);
                if (latch && !a.Acked && a.Info.Level == AlarmLevel.Alarm) _latched[code] = a;
            }
        }
        if (s.Connected && (s.LastDenyBits != _lastDeny || s.LastDenyAction != _lastDenyAction))
        {
            if (s.LastDenyBits != 0)
                changes.Add(new(new("IL_DENY", AlarmLevel.Info, "互鎖",
                    $"主控板擋下命令（動作 {s.LastDenyAction}）：{_denyText(s.LastDenyBits)}"), true, now));
            _lastDeny = s.LastDenyBits;
            _lastDenyAction = s.LastDenyAction;
        }
        return changes;
    }
}
