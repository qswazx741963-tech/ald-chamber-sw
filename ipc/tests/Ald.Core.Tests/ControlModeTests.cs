using Ald.Core;
using Xunit;

namespace Ald.Core.Tests;

public class ControlModeTests
{
    private static MachineSnapshot Ok() => new()
    {
        Connected = true, SlotOnline = new[] { true, true, true, true, true }, LoadlockFresh = true, SysState = SystemState.Idle,
    };

    [Fact]
    public void Online_LocksManualAndEditing_KeepsSequencesAndStop()
    {
        foreach (var p in new[] { Permission.ManualControl, Permission.Loadlock, Permission.EditRecipe, Permission.EditWaferMap, Permission.Maintenance })
        {
            Assert.False(ControlModes.Allowed(ControlMode.Online, p));
            Assert.True(ControlModes.Allowed(ControlMode.Offline, p));
        }
        foreach (var p in new[] { Permission.Stop, Permission.Reset, Permission.RunRecipe, Permission.Transfer, Permission.GoOffline })
            Assert.True(ControlModes.Allowed(ControlMode.Online, p));
    }

    [Fact]
    public void GoOnline_AllGood_NoProblems()
        => Assert.Empty(ControlModes.GoOnlineProblems(UserRole.Operator, Ok(), false, false, false, false, false));

    [Fact]
    public void GoOnline_ListsEveryProblem()
    {
        var s = Ok() with { SafeState = true, SlotOnline = new[] { true, true, false, true, true }, LoadlockFresh = false };
        var e = ControlModes.GoOnlineProblems(UserRole.Viewer, s, anyAlarm: true, waferLl: null, waferPm: false, heaterMismatch: true, busy: true);
        Assert.Equal(8, e.Count);
        Assert.Contains(e, x => x.Contains("操作員"));
        Assert.Contains(e, x => x.Contains("Reset"));
        Assert.Contains(e, x => x.Contains("晶圓位置不明"));
        Assert.Contains(e, x => x.Contains("加熱"));
        Assert.Single(ControlModes.GoOnlineProblems(UserRole.Operator, new MachineSnapshot(), false, false, false, false, false));   // 未連線只報一條
    }

    [Fact]
    public void GoOffline_NeedsOperator_AndIdle()
    {
        Assert.Single(ControlModes.GoOfflineProblems(UserRole.Viewer, false));
        Assert.Empty(ControlModes.GoOfflineProblems(UserRole.Operator, false));
        Assert.Empty(ControlModes.GoOfflineProblems(UserRole.Engineer, false));
        Assert.Single(ControlModes.GoOfflineProblems(UserRole.Admin, true));
    }
}
