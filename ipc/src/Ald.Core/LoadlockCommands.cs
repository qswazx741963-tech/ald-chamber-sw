namespace Ald.Core;

/// <summary>IPC 可以下給 Loadlock（SLK 模組）的命令。對應的 SLK ASCII 指令見 Ald.Comm/SlkProtocol.cs。</summary>
public enum LoadlockCommand
{
    Status, EStop, Reset, Recover,
    Pump, Vent, Load, Unload, Abort, XferDone,
    SvOpen, SvClose, RoughOn, RoughOff, VentOn, VentOff,
    ArmHome, ArmAutoHome, ArmToLoadlock, ArmToPm, ArmGoto, ZUp, ZDown,
}

public static class LoadlockCommands
{
    /// <summary>
    /// 這個命令要先過哪一條互鎖；回傳 null = 往安全方向的命令，永遠放行
    /// （查狀態、急停、重置、中止、關粗抽 / 洩壓閥）。規則編號見 docs/interlocks.md。
    /// </summary>
    public static MachineAction? ActionFor(LoadlockCommand cmd, int arg, int armSafePosSteps) => cmd switch
    {
        LoadlockCommand.SvOpen => MachineAction.SlitValveOpen,                 // LL-01
        LoadlockCommand.SvClose or LoadlockCommand.XferDone
            => MachineAction.SlitValveClose,                                    // LL-02（SEQ:XFER 會關 SV）
        LoadlockCommand.Load or LoadlockCommand.Unload => MachineAction.Transfer, // LL-03
        LoadlockCommand.ArmToPm => MachineAction.ArmIntoPm,                     // LL-04
        LoadlockCommand.ArmGoto => arg > armSafePosSteps ? MachineAction.ArmIntoPm : MachineAction.ArmLoadlockSide,
        LoadlockCommand.ArmHome or LoadlockCommand.ArmAutoHome or LoadlockCommand.ArmToLoadlock
            or LoadlockCommand.Recover => MachineAction.ArmLoadlockSide,      // LL-05（RCV 會自動 Homing）
        LoadlockCommand.ZUp or LoadlockCommand.ZDown => MachineAction.ZMove,   // LL-06
        LoadlockCommand.Pump or LoadlockCommand.RoughOn => MachineAction.LoadlockPump,   // LL-07
        LoadlockCommand.Vent or LoadlockCommand.VentOn => MachineAction.LoadlockVent,    // LL-08
        _ => null,
    };
}
