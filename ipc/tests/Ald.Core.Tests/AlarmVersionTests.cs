using Ald.Core;
using Xunit;

namespace Ald.Core.Tests;

public class AlarmVersionTests
{
    /// <summary>主控板（或舊版 ald-ecat）回的暫存器表版本跟人機不同：明確告訴人員，不只說「離線」。</summary>
    [Fact]
    public void MainBoardMapVersionMismatch_RaisesClearAlarm()
    {
        AlarmManager.ExpectedMapVersion = 3;
        var m = new AlarmManager(new MachineConfig());
        var s = new MachineSnapshot { Connected = true, SlotOnline = new bool[5], MapVersionSeen = 2 };
        var a = Assert.Single(m.Evaluate(s));
        Assert.Equal("MAP_VERSION", a.Code);
        Assert.Contains("v2", a.Message);
        Assert.Contains("v3", a.Message);

        a = Assert.Single(m.Evaluate(s with { MapVersionSeen = 0 }));
        Assert.Equal("SLOT0_OFFLINE", a.Code);
    }
}
