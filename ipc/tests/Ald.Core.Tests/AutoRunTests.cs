using Ald.Core;
using Xunit;

namespace Ald.Core.Tests;

public class AutoRunTests
{
    private static readonly TimeSpan Fast = TimeSpan.FromMilliseconds(1);
    private static readonly TransferConfig Cfg = new() { LiftTimeoutS = 0.2, HeaterTimeoutS = 0.2, LoadlockStepTimeoutS = 0.2 };

    private static ProcessRecipe Recipe(bool vent)
    {
        var sp = new double[16]; sp[6] = 150;
        var mfc = new double[8]; mfc[1] = 20;
        return new ProcessRecipe
        {
            Name = "auto", HeaterSpDegC = sp, HeatSettleS = 0.01, Mfc = mfc, GasStabilizeS = 0.01, VentAfter = vent,
            Ald = new AldRecipe { Cycles = 3, Steps = { new("A", 1, 20), new("P", 2, 50) } },
        };
    }

    [Fact]
    public async Task FullRun_FromAtmosphere_LoadProcessUnloadVent()
    {
        var m = new FakeMachine();               // PM、LL 都在大氣
        var map = new WaferMap(); map.Set(true, false, "test");
        var job = new AutoRunJob(m, map, Cfg, true, Fast);
        var phases = new List<string>();
        job.Progress += p => phases.Add(p.Step);
        var st = await job.RunAsync(Recipe(vent: true));
        Assert.True(st == SequencerState.Done, string.Join("\n", phases));

        Assert.Equal((true, false), (map.Loadlock, map.Pm));                          // 片回到 LL
        int I(string x) => m.Log.FindIndex(l => l.StartsWith(x));
        Assert.True(I("LL Pump") < I("LL SvOpen"));                                 // 先抽 LL
        Assert.True(I("LIFT UP") < I("START"));                                     // 送片後才跑 ALD
        Assert.True(I("START") < I("LIFT DOWN"));                                   // 製程完才取片
        int vent = m.Log.FindLastIndex(l => l.StartsWith("VAC") && l.Contains("ChamberVent"));
        Assert.True(vent > m.Log.LastIndexOf("LL SvClose"));                        // 取完片、SV 關了才 vent
        Assert.Contains(phases, x => x.StartsWith("3/5 製程"));
        Assert.Equal(LoadlockSv.Closed, m.Sv);
    }

    [Fact]
    public async Task ProcessFails_StopsBeforeUnload_WaferStaysInPm()
    {
        var m = new FakeMachine();
        var map = new WaferMap(); map.Set(true, false, "test");
        var r = Recipe(vent: true);
        var sp = new double[16]; sp[0] = 150;
        r = r with { HeaterSpDegC = sp, HeatSettleS = 5, HeatTimeoutS = 0.05 };
        m.HeaterBroken = 0;                                                          // #1 加熱壞了 → 升溫逾時
        var job = new AutoRunJob(m, map, Cfg, true, Fast);
        string msg = "";
        job.Progress += p => msg = p.Message;
        Assert.Equal(SequencerState.Failed, await job.RunAsync(r));
        Assert.Contains("3/5 製程", job.Step);
        Assert.Contains("升溫逾時", msg);
        Assert.Equal((false, true), (map.Loadlock, map.Pm));                         // 片在 PM，沒有自動取片
        Assert.DoesNotContain("LIFT DOWN", m.Log);
        Assert.DoesNotContain(m.Log, l => l.Contains("ChamberVent"));               // 不自動 vent
    }

    [Fact]
    public async Task Refused_WhenWaferMapWrong()
    {
        var m = new FakeMachine();
        var map = new WaferMap(); map.Set(false, true, "test");
        var job = new AutoRunJob(m, map, Cfg, true, Fast);
        Assert.Equal(SequencerState.Failed, await job.RunAsync(Recipe(false)));
        Assert.Empty(m.Log);
    }

    [Fact]
    public async Task Abort_DuringProcess_StopsAld_NoUnload()
    {
        var m = new FakeMachine();
        var map = new WaferMap(); map.Set(true, false, "test");
        var job = new AutoRunJob(m, map, Cfg, true, Fast);
        var task = job.RunAsync(Recipe(false) with { HeatSettleS = 5 });
        await Task.Delay(300);
        Assert.Contains("製程", job.Step);
        job.Abort();
        Assert.Equal(SequencerState.Aborted, await task);
        Assert.Contains("STOP", m.Log);
        Assert.Equal((false, true), (map.Loadlock, map.Pm));
    }
}
