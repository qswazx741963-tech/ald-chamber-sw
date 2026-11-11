namespace Ald.Core;

/// <summary>
/// 控制模式（參考 SEMI E30 GEM 的 Online / Offline 概念）：
/// OFFLINE = 工程 / 保養，可手動操作、改配方、修正晶圓位置、漏率測試；
/// ONLINE = 生產，只能執行 Sequence（自動跑片、批次、抽真空 / 回填、送取片、LL 抽洩），手動與編輯類鎖住。
/// 安全停止 / 中止 / 警報確認任何模式都能按；主控板硬體互鎖不受模式影響。程式重開一律 OFFLINE。
/// 之後接工廠主機（SECS/GEM）可再加 OnlineRemote。
/// </summary>
public enum ControlMode { Offline, Online }

public static class ControlModes
{
    /// <summary>ONLINE 時鎖住的權限（手動 / 編輯 / 保養類）。</summary>
    public static readonly Permission[] OfflineOnly =
    {
        Permission.ManualControl, Permission.Loadlock, Permission.EditRecipe, Permission.EditWaferMap, Permission.Maintenance,
    };

    public static bool Allowed(ControlMode mode, Permission p) => mode == ControlMode.Offline || !OfflineOnly.Contains(p);

    public static string Text(ControlMode m) => m == ControlMode.Online ? "ONLINE" : "OFFLINE";

    /// <summary>Go Online 前檢查；回傳不能切的原因（空 = 可以）。</summary>
    public static List<string> GoOnlineProblems(UserRole role, MachineSnapshot s, bool anyAlarm, bool? waferLl, bool? waferPm,
                                                bool heaterMismatch, bool busy)
    {
        var e = new List<string>();
        if (!Access.Can(role, Permission.GoOnline)) e.Add($"需要{Access.Text(Access.Required(Permission.GoOnline))}以上登入");
        if (!s.Connected) e.Add("主控板未連線");
        else
        {
            if (!s.AllOnline) e.Add("有控制板離線");
            if (!s.LoadlockFresh) e.Add("Loadlock 通訊中斷");
            if (s.SafeState) e.Add("系統在安全狀態，請先 Reset");
        }
        if (anyAlarm) e.Add("有異常警報未排除（看警報頁）");
        if (waferLl is null || waferPm is null) e.Add("晶圓位置不明（OFFLINE 時在傳片頁確認實物後修正）");
        if (heaterMismatch) e.Add("加熱設定跟常駐設定不一致（先按上方「恢復常駐加熱設定」）");
        if (busy) e.Add("有流程正在執行");
        return e;
    }

    /// <summary>Go Offline 檢查。</summary>
    public static List<string> GoOfflineProblems(UserRole role, bool busy)
    {
        var e = new List<string>();
        if (!Access.Can(role, Permission.GoOffline)) e.Add($"需要{Access.Text(Access.Required(Permission.GoOffline))}以上登入");
        if (busy) e.Add("有流程正在執行，請等它結束或先中止");
        return e;
    }
}
