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
    /// <summary>開 SV 的 Loadlock / PM 壓差上限（config interlock.svMaxDiffMtorr，預設 50 mTorr）。</summary>
    public static double SvMaxDiffTorr { get; set; } = 0.050;
    /// <summary>SLK Z 軸是否啟用（config interlock.loadlockZEnabled）。之後取消 Z 軸時設 false。</summary>
    public static bool LoadlockZEnabled { get; set; } = true;

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
        bool zDown = !LoadlockZEnabled || s.ArmZ == LoadlockZ.Down;
        bool pressureUnknown = double.IsNaN(s.LoadlockPressureTorr) || s.LoadlockPressureTorr < 0
                            || double.IsNaN(s.ChamberPressureTorr);
        double dp = Math.Abs(s.LoadlockPressureTorr - s.ChamberPressureTorr);

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
            MachineAction.SlitValveOpen or MachineAction.Transfer when pressureUnknown
                => InterlockResult.Deny("壓力未知（Loadlock 低於量測下限或 PM 壓力計離線），無法確認壓差"),
            MachineAction.SlitValveOpen or MachineAction.Transfer when dp > SvMaxDiffTorr
                => InterlockResult.Deny($"Loadlock 與 PM 壓差 {dp * 1000:F0} mTorr，超過 {SvMaxDiffTorr * 1000:F0} mTorr"),
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
            MachineAction.ZMove when !LoadlockZEnabled
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
}
