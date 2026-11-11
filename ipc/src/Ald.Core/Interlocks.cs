namespace Ald.Core;

public enum MachineAction
{
    // PM 側
    RfOn,
    AldStart,
    ChamberVent,
    WaferLiftMove,
    HeaterMove,        // Heater module 升降
    TurboGateOpen,
    // Loadlock 側
    SlitValveOpen,
    SlitValveClose,
    ArmIntoPm,
    ArmLoadlockSide,
    ZMove,
    Transfer,          // SEQ:LOAD / SEQ:UNLOAD
    LoadlockPump,
    LoadlockVent,
}

public sealed record InterlockResult(bool Allowed, string Reason)
{
    public static readonly InterlockResult Ok = new(true, "");
    public static InterlockResult Deny(string reason) => new(false, reason);
}

/// <summary>
/// IPC 端互鎖。
/// - 實際互鎖全部在主控板（firmware/main_board/app/interlock.c）：Loadlock 經主控板 RS-485 控制、SLK 韌體不做互鎖，
///   所以 LL-01~08 與 PM 側規則都由主控板執行。
/// - 這裡是 IPC 的預檢：命令送出前先擋、立刻在畫面顯示原因（LoadlockService.Execute 會呼叫）。
/// 規則編號見 docs/interlocks.md，改一邊就要改另一邊，並補單元測試。
/// TODO 規則依製程確認
/// </summary>
public static class Interlocks
{
    /// <summary>目前使用的互鎖參數（啟動時由 config/machine.json interlock 區塊設定，同時寫進主控板 HR IL_*）。</summary>
    public static InterlockSettings Settings { get; set; } = new();

    public static InterlockResult Check(MachineAction action, MachineSnapshot s) => Check(action, s, Settings);

    public static InterlockResult Check(MachineAction action, MachineSnapshot s, InterlockSettings cfg)
    {
        if (!s.SlotOnline.All(x => x))
            return InterlockResult.Deny("有控制板離線");
        if (s.SafeState && action != MachineAction.SlitValveClose)
            return InterlockResult.Deny("系統在安全狀態，請先排除原因並 Reset");

        bool llAction = action is >= MachineAction.SlitValveOpen and <= MachineAction.LoadlockVent;
        if (llAction && !s.LoadlockFresh)
            return InterlockResult.Deny("Loadlock 狀態不明（通訊中斷或逾時）");
        if (llAction && s.LoadlockAlarm != 0 && action != MachineAction.SlitValveClose)
            return InterlockResult.Deny($"Loadlock 警報中（代碼 {s.LoadlockAlarm}）");

        bool pmBusy = s.AldRunning || s.RfOn;
        bool zDown = !cfg.LoadlockZEnabled || s.ArmZ == LoadlockZ.Down;
        var svPressure = SvPressureCheck(s, cfg);

        return action switch
        {
            // ---- PM-01~03：PM 製程 / RF / vent 前，SV 必須確定關閉、手臂在外面
            MachineAction.RfOn when !(s.ChamberLidClosed && s.LidCoverClosed)
                => InterlockResult.Deny("Chamber lid / Lid cover 未關"),
            MachineAction.RfOn when !(s.Rf1PowerCableOk && s.Rf1MatchCableOk)
                => InterlockResult.Deny("RF1 電源線或 Match 線未到位"),
            MachineAction.RfOn or MachineAction.AldStart or MachineAction.ChamberVent when !s.LoadlockFresh
                => InterlockResult.Deny("Loadlock 狀態不明，無法確認 Slit valve 已關"),
            MachineAction.RfOn or MachineAction.AldStart or MachineAction.ChamberVent when !s.SlitClosed
                => InterlockResult.Deny("Slit valve 未關"),
            MachineAction.RfOn or MachineAction.AldStart or MachineAction.ChamberVent when !s.ArmClearOfSv
                => InterlockResult.Deny("Loadlock 手臂未退出 Slit valve 路徑"),
            MachineAction.ChamberVent when s.TurboAtSpeed || !s.TurboGateClosed
                => InterlockResult.Deny("Turbo 運轉中或 Gate valve 未關"),

            // ---- PM-04~06：PM 內升降與 Turbo GV
            MachineAction.HeaterMove or MachineAction.WaferLiftMove or MachineAction.TurboGateOpen when !s.LoadlockFresh
                => InterlockResult.Deny("Loadlock 狀態不明，無法確認手臂 / Slit valve 位置"),
            MachineAction.HeaterMove when !s.ArmClearOfSv
                => InterlockResult.Deny("手臂在 PM 內，不能移動 Heater module"),
            MachineAction.WaferLiftMove when s.ArmMoving
                => InterlockResult.Deny("Loadlock 手臂移動中，不能升降 Wafer lift"),
            MachineAction.WaferLiftMove when !s.HeaterDown
                => InterlockResult.Deny("Heater module 不在下位"),
            MachineAction.TurboGateOpen when !s.SlitClosed
                => InterlockResult.Deny("Slit valve 未關，不能開 Turbo gate valve"),

            // ---- LL-01 開 SV / LL-03 傳片
            MachineAction.SlitValveOpen or MachineAction.Transfer when pmBusy
                => InterlockResult.Deny("PM 製程或 RF 進行中"),
            MachineAction.SlitValveOpen or MachineAction.Transfer when s.ChamberVenting
                => InterlockResult.Deny("PM 正在 vent"),
            MachineAction.SlitValveOpen or MachineAction.Transfer when s.LoadlockLidOpen
                => InterlockResult.Deny("Loadlock 艙蓋開啟"),
            MachineAction.SlitValveOpen or MachineAction.Transfer when s.LoadlockRoughOpen || s.LoadlockVentOpen
                => InterlockResult.Deny("Loadlock 粗抽閥或洩壓閥開啟中"),
            MachineAction.SlitValveOpen or MachineAction.Transfer when !s.TurboGateClosed
                => InterlockResult.Deny("Turbo gate valve 未關"),
            MachineAction.SlitValveOpen or MachineAction.Transfer when !svPressure.Allowed
                => svPressure,
            MachineAction.Transfer when !s.PmAtTransfer
                => InterlockResult.Deny("PM 不在交接位置（Heater module 下位、Wafer lift 定位）"),
            MachineAction.Transfer when s.LoadlockBusy
                => InterlockResult.Deny("Loadlock 序列執行中"),

            // ---- LL-02 關 SV：手臂必須在外面
            MachineAction.SlitValveClose when !s.ArmClearOfSv
                => InterlockResult.Deny("手臂在 Slit valve 路徑內，關閉會夾到"),

            // ---- LL-04 / LL-05 手臂
            MachineAction.ArmIntoPm when s.SlitValve != LoadlockSv.Open
                => InterlockResult.Deny("Slit valve 未全開"),
            MachineAction.ArmIntoPm or MachineAction.ArmLoadlockSide when !zDown
                => InterlockResult.Deny("Z 軸未下降到位"),
            MachineAction.ArmIntoPm when !s.PmAtTransfer
                => InterlockResult.Deny("PM 不在交接位置（Heater module 下位、Wafer lift 定位）"),
            MachineAction.ArmIntoPm when pmBusy
                => InterlockResult.Deny("PM 製程或 RF 進行中"),

            // ---- LL-06 Z 軸：手臂在 PM 裡撿放片時，PM 要在交接位置、不能在跑
            MachineAction.ZMove when !cfg.LoadlockZEnabled
                => InterlockResult.Ok,
            MachineAction.ZMove when !s.ArmClearOfSv && !s.PmAtTransfer
                => InterlockResult.Deny("手臂在 PM 內，但 PM 不在交接位置"),
            MachineAction.ZMove when !s.ArmClearOfSv && pmBusy
                => InterlockResult.Deny("手臂在 PM 內，PM 製程或 RF 進行中"),

            // ---- LL-07 / LL-08 Loadlock 抽氣 / 洩壓
            MachineAction.LoadlockPump or MachineAction.LoadlockVent when !s.SlitClosed
                => InterlockResult.Deny("Slit valve 未關"),
            MachineAction.LoadlockPump when s.LoadlockLidOpen
                => InterlockResult.Deny("Loadlock 艙蓋開啟"),
            MachineAction.LoadlockPump when s.LoadlockVentOpen
                => InterlockResult.Deny("Loadlock 洩壓閥開啟中"),
            MachineAction.LoadlockVent when s.LoadlockRoughOpen
                => InterlockResult.Deny("Loadlock 粗抽閥開啟中"),

            _ => InterlockResult.Ok,
        };
    }

    /// <summary>
    /// LL-01 的壓力條件，跟主控板 interlock.c sv_open_conditions 同規則：
    /// Below（預設）：LL 與 PM 都 &lt; 門檻（SLK 低於下限 1 Torr 只在門檻 ≥ 1 Torr 時算數）；Diff：|LL − PM| ≤ 上限。
    /// 壓力未知（PM 壓力計離線 / 故障 / 超出上限、LL 壓力計疑似斷線）一律不准。
    /// </summary>
    private static InterlockResult SvPressureCheck(MachineSnapshot s, InterlockSettings cfg)
    {
        double ll = s.LoadlockPressureTorr, pm = s.ChamberPressureTorr;
        if (s.LoadlockPressureSuspect)
            return InterlockResult.Deny("Loadlock 壓力計讀值不可信（疑似斷線，請確認後讓 LL 回到 1 Torr 以上再抽）");
        if (double.IsNaN(pm))
            return InterlockResult.Deny("PM 壓力未知（壓力計離線、故障或超出量測範圍）");
        if (!s.LoadlockPressureUnderRange && !(ll >= 0))
            return InterlockResult.Deny("Loadlock 壓力未知");
        if (cfg.SvMode == SvOpenMode.Below)
        {
            bool llOk = s.LoadlockPressureUnderRange ? cfg.SvBelowTorr >= SlkGaugeMinTorr : ll < cfg.SvBelowTorr;
            if (!llOk) return InterlockResult.Deny($"Loadlock 壓力未低於 {cfg.SvBelowTorr * 1000:F0} mTorr");
            if (!(pm < cfg.SvBelowTorr)) return InterlockResult.Deny($"PM 壓力未低於 {cfg.SvBelowTorr * 1000:F0} mTorr");
            return InterlockResult.Ok;
        }
        if (s.LoadlockPressureUnderRange)
            return InterlockResult.Deny("Loadlock 低於 SLK 壓力計下限，量不到壓差（壓差模式需 mTorr 級壓力計）");
        double dp = Math.Abs(ll - pm);
        return dp > cfg.SvMaxDiffTorr
            ? InterlockResult.Deny($"Loadlock 與 PM 壓差 {dp * 1000:F0} mTorr，超過 {cfg.SvMaxDiffTorr * 1000:F0} mTorr")
            : InterlockResult.Ok;
    }

    /// <summary>SLK 壓力計量測下限。</summary>
    public const double SlkGaugeMinTorr = 1.0;
}

/// <summary>開 SV 的壓力條件，對應主控板 HR IL_SV_MODE。</summary>
public enum SvOpenMode { Below = 0, Diff = 1 }

/// <summary>互鎖參數（config/machine.json interlock 區塊）。</summary>
public sealed record InterlockSettings
{
    /// <summary>Below = LL、PM 都低於 SvBelowTorr（預設，2026-11-11）；Diff = 壓差 ≤ SvMaxDiffTorr。</summary>
    public SvOpenMode SvMode { get; init; } = SvOpenMode.Below;
    public double SvBelowTorr { get; init; } = 1.0;
    public double SvMaxDiffTorr { get; init; } = 0.050;
    /// <summary>SLK Z 軸是否啟用。之後取消 Z 軸時設 false。</summary>
    public bool LoadlockZEnabled { get; init; } = true;

}
