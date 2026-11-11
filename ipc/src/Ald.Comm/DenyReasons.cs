using Ald.Comm.Generated;
using Deny = Ald.Comm.Generated.MainBoard.Input.IlDenyBits;
using Deny2 = Ald.Comm.Generated.MainBoard.Input.IlDeny2Bits;

namespace Ald.Comm;

/// <summary>主控板 IR IL_DENY / IL_DENY2（互鎖擋下原因）→ 中文。位元定義來自產生檔，registers.yaml 改順序也不會錯。</summary>
public static class DenyReasons
{
    private static readonly Dictionary<Deny, string> Text1 = new()
    {
        [Deny.SafeActive] = "系統在安全狀態",
        [Deny.SlotOffline] = "有控制板離線",
        [Deny.LlUnknown] = "Loadlock 狀態不明",
        [Deny.LlAlarm] = "Loadlock 警報中",
        [Deny.SvNotClosed] = "Slit valve 未關",
        [Deny.SvNotOpen] = "Slit valve 未全開",
        [Deny.DpHigh] = "壓力條件未達（LL / PM 未低於門檻或壓差過大）",
        [Deny.PressureUnknown] = "壓力未知（壓力計故障 / 離線 / 疑似斷線）",
        [Deny.PmBusy] = "PM 製程或 RF 進行中",
        [Deny.PmVenting] = "PM 正在 vent",
        [Deny.ArmInSvPath] = "手臂在 Slit valve 路徑內",
        [Deny.ZNotDown] = "Z 軸未下降到位",
        [Deny.LlLidOpen] = "Loadlock 艙蓋開啟",
        [Deny.LlValveConflict] = "Loadlock 粗抽 / 洩壓閥開著",
        [Deny.PmNotAtTransfer] = "PM 不在交接位置",
        [Deny.LlBusy] = "Loadlock 序列執行中",
    };

    private static readonly Dictionary<Deny2, string> Text2 = new()
    {
        [Deny2.TurboGvOpen] = "Turbo gate valve 未關",
        [Deny2.ArmMoving] = "Loadlock 手臂移動中",
        [Deny2.HeaterNotDown] = "Heater module 不在下位",
    };

    public static string Describe(uint bits)
    {
        var parts = new List<string>();
        foreach (var (k, v) in Text1) if ((bits & (uint)k) != 0) parts.Add(v);
        foreach (var (k, v) in Text2) if ((bits >> 16 & (uint)k) != 0) parts.Add(v);
        return parts.Count == 0 ? "（無）" : string.Join("、", parts);
    }
}
