namespace Ald.Core;

public enum MachineAction
{
    RfOn,
    SlitValveOpen,
    WaferLiftMove,
    ChamberVent,
}

public sealed record InterlockResult(bool Allowed, string Reason)
{
    public static readonly InterlockResult Ok = new(true, "");
    public static InterlockResult Deny(string reason) => new(false, reason);
}

/// <summary>
/// 跨板互鎖（IPC 端）。板內互鎖在各 Arduino；安全類另外要用硬體線串接。
/// 每一條規則都要有單元測試（tests/Ald.Core.Tests）。
/// TODO 規則依製程確認
/// </summary>
public static class Interlocks
{
    public static InterlockResult Check(MachineAction action, MachineSnapshot s)
    {
        if (!s.BoardOnline.Skip(1).All(x => x))
            return InterlockResult.Deny("有 Arduino 離線");

        return action switch
        {
            MachineAction.RfOn when !(s.ChamberLidClosed && s.LidCoverClosed)
                => InterlockResult.Deny("Chamber lid / Lid cover 未關"),
            MachineAction.RfOn when !(s.Rf1PowerCableOk && s.Rf1MatchCableOk)
                => InterlockResult.Deny("RF1 電源線或 Match 線未到位"),
            MachineAction.RfOn when !s.SlitClosed
                => InterlockResult.Deny("Slit valve 未關"),

            MachineAction.SlitValveOpen when !s.WaferLiftHome
                => InterlockResult.Deny("Wafer lift 不在原點"),

            MachineAction.WaferLiftMove when !(s.SlitOpened || s.SlitClosed)
                => InterlockResult.Deny("Slit valve 位置不明"),

            MachineAction.ChamberVent when s.TurboAtSpeed || !s.TurboGateClosed
                => InterlockResult.Deny("Turbo 運轉中或 Gate valve 未關"),

            _ => InterlockResult.Ok,
        };
    }
}
