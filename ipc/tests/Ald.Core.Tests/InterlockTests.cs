using Ald.Core;
using Xunit;

namespace Ald.Core.Tests;

public class InterlockTests
{
    /// <summary>PM 在真空、閒置；Loadlock 真空就緒、SV 關、手臂在 LL、Z 下。</summary>
    private static MachineSnapshot Ready() => new()
    {
        SlotOnline = new[] { true, true, true, true, true },
        ChamberLidClosed = true, LidCoverClosed = true,
        Rf1PowerCableOk = true, Rf1MatchCableOk = true,
        TurboGateClosed = true, WaferLiftHome = true, PmAtTransfer = true,
        ChamberPressureTorr = 0.05,
        LoadlockFresh = true, SlitValve = LoadlockSv.Closed, ArmZ = LoadlockZ.Down,
        ArmPosSteps = 0, LoadlockPressureTorr = -0.001,
    };

    private static bool Allowed(MachineAction a, MachineSnapshot s) => Interlocks.Check(a, s).Allowed;

    [Fact] public void RfOn_Allowed_WhenAllConditionsMet() => Assert.True(Allowed(MachineAction.RfOn, Ready()));
    [Fact] public void RfOn_Denied_WhenLidOpen() => Assert.False(Allowed(MachineAction.RfOn, Ready() with { ChamberLidClosed = false }));
    [Fact] public void Vent_Denied_WhenTurboAtSpeed() => Assert.False(Allowed(MachineAction.ChamberVent, Ready() with { TurboAtSpeed = true }));

    [Fact]
    public void Everything_Denied_WhenSlotOffline()
        => Assert.False(Allowed(MachineAction.SlitValveOpen, Ready() with { SlotOnline = new[] { true, true, false, true, true } }));

    // ---- PM-01：SV 沒關 / 手臂在路徑上，不准開始 ALD
    [Fact] public void AldStart_Denied_WhenSvOpen() => Assert.False(Allowed(MachineAction.AldStart, Ready() with { SlitValve = LoadlockSv.Open }));
    [Fact] public void AldStart_Denied_WhenArmInPm() => Assert.False(Allowed(MachineAction.AldStart, Ready() with { ArmPosSteps = 12000 }));
    [Fact] public void AldStart_Denied_WhenLoadlockUnknown() => Assert.False(Allowed(MachineAction.AldStart, Ready() with { LoadlockFresh = false }));

    // ---- LL-01：開 SV
    [Fact] public void SvOpen_Allowed_WhenReady() => Assert.True(Allowed(MachineAction.SlitValveOpen, Ready()));
    [Fact] public void SvOpen_Denied_WhenAldRunning() => Assert.False(Allowed(MachineAction.SlitValveOpen, Ready() with { AldRunning = true }));
    [Fact] public void SvOpen_Denied_WhenLoadlockAtAtm() => Assert.False(Allowed(MachineAction.SlitValveOpen, Ready() with { LoadlockPressureTorr = 760 }));
    [Fact] public void SvOpen_Denied_WhenChamberPressureUnknown() => Assert.False(Allowed(MachineAction.SlitValveOpen, Ready() with { ChamberPressureTorr = double.NaN }));
    [Fact] public void SvOpen_Denied_WhenLoadlockVentOpen() => Assert.False(Allowed(MachineAction.SlitValveOpen, Ready() with { LoadlockVentOpen = true }));

    // ---- LL-02：關 SV 不能夾到手臂；安全狀態下仍允許
    [Fact] public void SvClose_Denied_WhenArmInPath() => Assert.False(Allowed(MachineAction.SlitValveClose, Ready() with { SlitValve = LoadlockSv.Open, ArmPosSteps = 9000 }));
    [Fact] public void SvClose_Allowed_InSafeState() => Assert.True(Allowed(MachineAction.SlitValveClose, Ready() with { SafeState = true }));

    // ---- LL-04：手臂伸進 PM
    [Fact] public void ArmIntoPm_Denied_WhenSvClosed() => Assert.False(Allowed(MachineAction.ArmIntoPm, Ready()));
    [Fact] public void ArmIntoPm_Allowed_WhenSvOpen() => Assert.True(Allowed(MachineAction.ArmIntoPm, Ready() with { SlitValve = LoadlockSv.Open }));
    [Fact] public void ArmMove_Denied_WhenZUp() => Assert.False(Allowed(MachineAction.ArmLoadlockSide, Ready() with { ArmZ = LoadlockZ.Up }));

    // ---- LL-08：SV 開著不能 vent Loadlock
    [Fact] public void LoadlockVent_Denied_WhenSvOpen() => Assert.False(Allowed(MachineAction.LoadlockVent, Ready() with { SlitValve = LoadlockSv.Open }));
}
