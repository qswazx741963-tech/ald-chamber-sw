namespace Ald.Core;

public enum MachineAction
{
    // PM 側
    RfOn,
    AldStart,
    ChamberVent,
    WaferLiftMove,
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
/// - Loadlock 側（LL-01~08）：Loadlock 由 IPC 經 RS-485 直接控制、SLK 韌體不做互鎖，
///   所以**這裡就是 Loadlock 的實際互鎖**，每個命令送出前由 LoadlockService 呼叫。
/// - PM 側（PM-01~03）：這裡先擋、顯示原因；主控板（firmware/main_board/app/interlock.c）再用 Loadlock 狀態鏡像真正執行。
/// 規則編號見 docs/interlocks.md，改一邊就要改另一邊，並補單元測試。
/// TODO 規則依製程確認
/// </summary>
public static class Interlocks
{
    public static double SvLoadlockMaxTorr { get; set; } = 2.0;
    public static double SvChamberMaxTorr { get; set; } = 1.0;

    public static InterlockResult Check(MachineAction action, MachineSnapshot s)
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

            MachineAction.WaferLiftMove when !s.ArmClearOfSv && s.ArmZ != LoadlockZ.Down
                => InterlockResult.Deny("Loadlock 手臂在 PM 內且 Z 未下降"),

            // ---- LL-01 開 SV / LL-03 傳片
            MachineAction.SlitValveOpen or MachineAction.Transfer when pmBusy
                => InterlockResult.Deny("PM 製程或 RF 進行中"),
            MachineAction.SlitValveOpen or MachineAction.Transfer when s.ChamberVenting
                => InterlockResult.Deny("PM 正在 vent"),
            MachineAction.SlitValveOpen or MachineAction.Transfer when s.LoadlockLidOpen
                => InterlockResult.Deny("Loadlock 艙蓋開啟"),
            MachineAction.SlitValveOpen or MachineAction.Transfer when s.LoadlockRoughOpen || s.LoadlockVentOpen
                => InterlockResult.Deny("Loadlock 粗抽閥或洩壓閥開啟中"),
            MachineAction.SlitValveOpen or MachineAction.Transfer when s.LoadlockPressureTorr > SvLoadlockMaxTorr
                => InterlockResult.Deny($"Loadlock 壓力高於 {SvLoadlockMaxTorr} Torr"),
            MachineAction.SlitValveOpen or MachineAction.Transfer
                when double.IsNaN(s.ChamberPressureTorr) || s.ChamberPressureTorr > SvChamberMaxTorr
                => InterlockResult.Deny($"PM 壓力未知或高於 {SvChamberMaxTorr} Torr"),
            MachineAction.Transfer when !s.PmAtTransfer
                => InterlockResult.Deny("PM Susceptor / Wafer lift 不在交接位置"),
            MachineAction.Transfer when s.LoadlockBusy
                => InterlockResult.Deny("Loadlock 序列執行中"),

            // ---- LL-02 關 SV：手臂必須在外面
            MachineAction.SlitValveClose when !s.ArmClearOfSv
                => InterlockResult.Deny("手臂在 Slit valve 路徑內，關閉會夾到"),

            // ---- LL-04 / LL-05 手臂
            MachineAction.ArmIntoPm when s.SlitValve != LoadlockSv.Open
                => InterlockResult.Deny("Slit valve 未全開"),
            MachineAction.ArmIntoPm or MachineAction.ArmLoadlockSide when s.ArmZ != LoadlockZ.Down
                => InterlockResult.Deny("Z 軸未下降到位"),
            MachineAction.ArmIntoPm when !s.PmAtTransfer
                => InterlockResult.Deny("PM Susceptor / Wafer lift 不在交接位置"),
            MachineAction.ArmIntoPm when pmBusy
                => InterlockResult.Deny("PM 製程或 RF 進行中"),

            // ---- LL-06 Z 軸：手臂在 PM 裡撿放片時，PM 要在交接位置、不能在跑
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
}
