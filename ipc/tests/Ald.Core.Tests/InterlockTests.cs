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
        TurboGateClosed = true, HeaterDown = true, WaferLiftDown = true,
        ChamberPressureTorr = 0.010,
        LoadlockFresh = true, SlitValve = LoadlockSv.Closed, ArmZ = LoadlockZ.Down,
        ArmPosSteps = 0, LoadlockPressureTorr = 0.040,   // 假設 LL 已加 mTorr 級壓力計
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
    [Fact] public void SvOpen_Denied_WhenLoadlockPressureUnknown() => Assert.False(Allowed(MachineAction.SlitValveOpen, Ready() with { LoadlockPressureTorr = double.NaN }));
    [Fact] public void SvOpen_Denied_WhenChamberPressureUnknown() => Assert.False(Allowed(MachineAction.SlitValveOpen, Ready() with { ChamberPressureTorr = double.NaN }));
    [Fact] public void SvOpen_Denied_WhenNegativePressure() => Assert.False(Allowed(MachineAction.SlitValveOpen, Ready() with { LoadlockPressureTorr = -0.001 }));

    // ---- 開 SV 壓力條件 Below（預設）：LL、PM 都 < 1 Torr
    private static MachineSnapshot SlkUnder() => Ready() with { LoadlockPressureTorr = double.NaN, LoadlockPressureUnderRange = true };
    [Fact] public void SvOpen_Below_Allowed_WhenSlkUnderRangeAndPmBelow1Torr() => Assert.True(Allowed(MachineAction.SlitValveOpen, SlkUnder() with { ChamberPressureTorr = 0.8 }));
    [Fact] public void SvOpen_Below_Denied_WhenPmAt1Torr() => Assert.False(Allowed(MachineAction.SlitValveOpen, SlkUnder() with { ChamberPressureTorr = 1.0 }));
    [Fact] public void SvOpen_Below_Denied_WhenLoadlockAt1_5Torr() => Assert.False(Allowed(MachineAction.SlitValveOpen, Ready() with { LoadlockPressureTorr = 1.5 }));
    [Fact] public void SvOpen_Below_Denied_WhenGaugeSuspect() => Assert.False(Allowed(MachineAction.SlitValveOpen, SlkUnder() with { LoadlockPressureSuspect = true }));
    [Fact] public void SvOpen_Below_Denied_WhenTurboGateOpen() => Assert.False(Allowed(MachineAction.SlitValveOpen, SlkUnder() with { TurboGateClosed = false }));
    [Fact]
    public void SvOpen_Below_SlkUnderRangeNotEnough_WhenThresholdBelow1Torr()
        => Assert.False(Interlocks.Check(MachineAction.SlitValveOpen, SlkUnder(), new InterlockSettings { SvBelowTorr = 0.5 }).Allowed);

    // ---- 壓力條件 Diff：|LL − PM| ≤ 50 mTorr（LL 需 mTorr 級壓力計）
    private static readonly InterlockSettings Diff = new() { SvMode = SvOpenMode.Diff };
    private static bool AllowedDiff(MachineSnapshot s) => Interlocks.Check(MachineAction.SlitValveOpen, s, Diff).Allowed;
    [Fact] public void SvOpen_Diff_Denied_WhenSlkUnderRange() => Assert.False(AllowedDiff(SlkUnder()));
    [Fact] public void SvOpen_Diff_Denied_WhenDiffOver50mTorr() => Assert.False(AllowedDiff(Ready() with { LoadlockPressureTorr = 0.065 }));
    [Fact] public void SvOpen_Diff_Allowed_WhenDiffWithin50mTorr() => Assert.True(AllowedDiff(Ready() with { LoadlockPressureTorr = 0.055 }));
    [Fact] public void SvOpen_Denied_WhenTurboGateOpen() => Assert.False(Allowed(MachineAction.SlitValveOpen, Ready() with { TurboGateClosed = false }));
    [Fact] public void TurboGateOpen_Denied_WhenSvOpen() => Assert.False(Allowed(MachineAction.TurboGateOpen, Ready() with { SlitValve = LoadlockSv.Open }));
    [Fact] public void HeaterMove_Denied_WhenArmInPm() => Assert.False(Allowed(MachineAction.HeaterMove, Ready() with { ArmPosSteps = 9000 }));
    [Fact] public void WaferLift_Allowed_WhenArmStoppedInPm() => Assert.True(Allowed(MachineAction.WaferLiftMove, Ready() with { ArmPosSteps = 9000 }));
    [Fact] public void WaferLift_Denied_WhenArmMoving() => Assert.False(Allowed(MachineAction.WaferLiftMove, Ready() with { ArmMoving = true }));
    [Fact] public void WaferLift_Denied_WhenHeaterUp() => Assert.False(Allowed(MachineAction.WaferLiftMove, Ready() with { HeaterDown = false }));
    [Fact] public void ArmIntoPm_Denied_WhenHeaterUp() => Assert.False(Allowed(MachineAction.ArmIntoPm, Ready() with { SlitValve = LoadlockSv.Open, HeaterDown = false }));
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

public class LoadlockCommandTests
{
    [Fact]
    public void ToRegister_MapsEveryCommand()
    {
        foreach (var c in Enum.GetValues<LoadlockCommand>())
            Assert.NotEqual(Ald.Comm.Generated.MainBoard.Holding.LlCmdValues.None, Ald.Comm.LoadlockService.ToRegister(c));
        Assert.Equal(Ald.Comm.Generated.MainBoard.Holding.LlCmdValues.SvOpen, Ald.Comm.LoadlockService.ToRegister(LoadlockCommand.SvOpen));
        Assert.Equal(Ald.Comm.Generated.MainBoard.Holding.LlCmdValues.ArmAhome, Ald.Comm.LoadlockService.ToRegister(LoadlockCommand.ArmAutoHome));
    }

    [Fact]
    public void ActionFor_MapsToInterlockRules()
    {
        Assert.Equal(MachineAction.SlitValveOpen, LoadlockCommands.ActionFor(LoadlockCommand.SvOpen, 0, 5894));
        Assert.Equal(MachineAction.ArmIntoPm, LoadlockCommands.ActionFor(LoadlockCommand.ArmGoto, 9000, 5894));
        Assert.Equal(MachineAction.ArmLoadlockSide, LoadlockCommands.ActionFor(LoadlockCommand.ArmGoto, 100, 5894));
        Assert.Equal(MachineAction.SlitValveClose, LoadlockCommands.ActionFor(LoadlockCommand.XferDone, 0, 5894));
        Assert.Null(LoadlockCommands.ActionFor(LoadlockCommand.EStop, 0, 5894));
        Assert.Null(LoadlockCommands.ActionFor(LoadlockCommand.VentOff, 0, 5894));
    }
}

public class GaugeTests
{
    private static bool Near(double a, double b) => Math.Abs(a - b) <= 0.01 * Math.Abs(b);

    [Fact] public void ApgxH_4V_Is_1e2mbar() { var r = Gauges.Convert(GaugeModel.ApgxH, 4000); Assert.Equal(GaugeStatus.Ok, r.Status); Assert.True(Near(r.Torr, 7.5e-3)); }
    [Fact] public void ApgxH_FilamentBroken_IsFault() => Assert.Equal(GaugeStatus.Fault, Gauges.Convert(GaugeModel.ApgxH, 9500).Status);
    [Fact] public void ApgxH_ZeroVolt_IsFault() => Assert.Equal(GaugeStatus.Fault, Gauges.Convert(GaugeModel.ApgxH, 0).Status);
    [Fact] public void Wrg200X_10V_IsAtm() => Assert.True(Near(Gauges.Convert(GaugeModel.Wrg200X, 10000).Torr, 748));
    [Fact] public void Wrg200X_FailToStrike_IsFault() => Assert.Equal(GaugeStatus.Fault, Gauges.Convert(GaugeModel.Wrg200X, 1300).Status);
    [Fact] public void Linear_ManualExample() => Assert.True(Near(Gauges.Convert(GaugeModel.Linear, 6000, 10).Torr, 6));
    [Fact] public void Linear_Saturated_IsOverRange() => Assert.Equal(GaugeStatus.OverRange, Gauges.Convert(GaugeModel.Linear, 9950, 1).Status);
    [Fact] public void Linear_NoFullScale_NotConfigured() => Assert.Equal(GaugeStatus.NotConfigured, Gauges.Convert(GaugeModel.Linear, 5000).Status);
}
