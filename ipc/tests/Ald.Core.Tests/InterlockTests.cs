using Ald.Core;
using Xunit;

namespace Ald.Core.Tests;

public class InterlockTests
{
    private static MachineSnapshot Ready() => new()
    {
        BoardOnline = new[] { false, true, true, true, true },
        ChamberLidClosed = true, LidCoverClosed = true,
        Rf1PowerCableOk = true, Rf1MatchCableOk = true,
        SlitClosed = true, TurboGateClosed = true, WaferLiftHome = true,
    };

    [Fact]
    public void RfOn_Allowed_WhenAllConditionsMet()
        => Assert.True(Interlocks.Check(MachineAction.RfOn, Ready()).Allowed);

    [Fact]
    public void RfOn_Denied_WhenLidOpen()
        => Assert.False(Interlocks.Check(MachineAction.RfOn, Ready() with { ChamberLidClosed = false }).Allowed);

    [Fact]
    public void Vent_Denied_WhenTurboAtSpeed()
        => Assert.False(Interlocks.Check(MachineAction.ChamberVent, Ready() with { TurboAtSpeed = true }).Allowed);

    [Fact]
    public void Everything_Denied_WhenBoardOffline()
    {
        var s = Ready() with { BoardOnline = new[] { false, true, false, true, true } };
        Assert.False(Interlocks.Check(MachineAction.SlitValveOpen, s).Allowed);
    }
}
