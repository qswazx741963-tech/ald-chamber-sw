using Ald.Core;
using Ald.Data;
using Xunit;

namespace Ald.Core.Tests;

public class LeakTestTests
{
    private static readonly TimeSpan Fast = TimeSpan.FromMilliseconds(10);
    private static LeakTestConfig Cfg(double limit = 1.0) => new()
    {
        PumpS = 0.05, SettleS = 0.05, MeasureS = 1.0, LimitMtorrPerMin = limit, StartBelowTorr = 5e-5, ChamberVolumeL = 20,
    };

    [Fact]
    public void Fit_ExactLine()
    {
        var pts = Enumerable.Range(0, 10).Select(i => (T: (double)i, P: 1e-6 + 2e-7 * i)).ToList();
        var (slope, r2) = LeakMath.Fit(pts);
        Assert.Equal(2e-7, slope, 12);
        Assert.Equal(1.0, r2, 6);
        Assert.True(double.IsNaN(LeakMath.Fit(pts.Take(1).ToList()).Slope));
    }

    [Fact]
    public async Task SmallLeak_FromAtmosphere_Passes_AndReopensGv()
    {
        var m = new FakeMachine { LeakTorrPerS = 1e-6 };   // 0.06 mTorr/min
        var seq = new LeakTestSequencer(m, Cfg(), Fast);
        var st = await seq.RunAsync();
        Assert.Equal(SequencerState.Done, st);
        var r = seq.Result!;
        Assert.True(r.Pass, r.Text());
        Assert.InRange(r.RorMtorrPerMin, 0.03, 0.09);
        Assert.InRange(r.LeakTorrLps, 1e-5, 3e-5);           // 1e-6 Torr/s × 20 L
        Assert.True(m.VacuumCommand.HasFlag(VacuumOutputs.TurboGv));   // 量完開回 GV
        Assert.Contains(m.Log, x => x.StartsWith("VAC") && !x.Contains("TurboGv"));   // 中間有關過 GV
    }

    [Fact]
    public async Task BigLeak_Fails()
    {
        var m = new FakeMachine { LeakTorrPerS = 1e-4 };   // 6 mTorr/min
        var seq = new LeakTestSequencer(m, Cfg(limit: 1), Fast);
        Assert.Equal(SequencerState.Done, await seq.RunAsync());
        Assert.False(seq.Result!.Pass);
        Assert.InRange(seq.Result.RorMtorrPerMin, 4, 8);
    }

    [Fact]
    public async Task GrossLeak_StopsEarly()
    {
        var m = new FakeMachine { LeakTorrPerS = 0.1 };
        var seq = new LeakTestSequencer(m, Cfg() with { AbortAboveTorr = 1e-2, MeasureS = 30 }, Fast);
        var t0 = DateTime.Now;
        Assert.Equal(SequencerState.Done, await seq.RunAsync());
        Assert.True(DateTime.Now - t0 < TimeSpan.FromSeconds(10));
        Assert.False(seq.Result!.Pass);
        Assert.Contains("大漏", seq.Result.Note);
    }

    [Fact]
    public async Task Abort_DuringMeasure_ReopensGv()
    {
        var m = new FakeMachine { LeakTorrPerS = 1e-6 };
        var seq = new LeakTestSequencer(m, Cfg() with { MeasureS = 30 }, Fast);
        var run = seq.RunAsync();
        while (!seq.Step.StartsWith("量測")) await Task.Delay(10);
        await Task.Delay(100);
        seq.Abort();
        Assert.Equal(SequencerState.Aborted, await run);
        Assert.Null(seq.Result);
        Assert.True(m.VacuumCommand.HasFlag(VacuumOutputs.TurboGv));
    }

    [Fact]
    public async Task Precheck_SlitOpen_Refuses()
    {
        var m = new FakeMachine { Sv = LoadlockSv.Open };
        var seq = new LeakTestSequencer(m, Cfg(), Fast);
        Assert.Equal(SequencerState.Failed, await seq.RunAsync());
        Assert.Contains("Slit", seq.Step + string.Join("", LeakTestSequencer.Precheck(m.Snapshot, Cfg())));
        Assert.DoesNotContain(m.Log, x => x.StartsWith("VAC"));
    }
}

public class MaintenanceTests
{
    private static readonly DateTime T0 = new(2026, 11, 11, 8, 0, 0);
    private static MachineSnapshot S(double sec, Func<MachineSnapshot, MachineSnapshot>? f = null)
    {
        var s = new MachineSnapshot { Connected = true, Time = T0.AddSeconds(sec), LoadlockFresh = true };
        return f is null ? s : f(s);
    }

    [Fact]
    public void Delta_Hours_Edges_AndGap()
    {
        var a = S(0, s => s with { DryPumpRunning = true, TurboAtSpeed = true });
        var b = S(2, s => s with { DryPumpRunning = true, TurboAtSpeed = true, TurboGateOpened = true, ChamberVenting = false,
            SlitValve = LoadlockSv.Open, WaferLiftUp = true });
        var d = Maintenance.Delta(a, b, null);
        Assert.Equal(2.0 / 3600, d["dry_pump_h"], 9);
        Assert.Equal(1, d["turbo_gv"]);
        Assert.Equal(1, d["slit_valve"]);
        Assert.Equal(1, d["wafer_lift"]);
        Assert.False(d.ContainsKey("chamber_vent"));

        var same = Maintenance.Delta(b, b with { Time = b.Time.AddSeconds(1) }, null);   // 狀態沒變 = 只有時數
        Assert.Equal(new[] { "dry_pump_h", "turbo_h" }, same.Keys.OrderBy(k => k));
        var gap = Maintenance.Delta(b, b with { Time = b.Time.AddMinutes(5) }, null);       // 斷線很久：不算時數
        Assert.False(gap.ContainsKey("dry_pump_h"));
        Assert.Empty(Maintenance.Delta(b with { Connected = false }, b, null));
    }

    [Fact]
    public void AldValves_CountedFromRecipe()
    {
        // V1 脈衝、purge、V3 脈衝、purge：每 cycle V1、V3 各開 1 次
        var ald = new AldRecipe { Cycles = 100, Steps = { new("TMA", 0b001, 20), new("P", 0, 100), new("H2O", 0b100, 20), new("P", 0, 100) } };
        Assert.Equal(new[] { 1, 0, 1, 0, 0, 0, 0 }, Maintenance.OpensPerCycle(ald));
        // 整個 cycle 都開著的閥不算
        Assert.Equal(0, Maintenance.OpensPerCycle(new AldRecipe { Steps = { new("a", 2, 1), new("b", 3, 1) } })[1]);

        var a = S(0, s => s with { AldRunning = false, AldCycle = 0 });
        var b = S(1, s => s with { AldRunning = true, AldCycle = 3 });
        var d = Maintenance.Delta(a, b, ald);
        Assert.Equal(1, d["ald_runs"]);
        Assert.Equal(3, d["ald_cycles"]);
        Assert.Equal(3, d["ald_v1"]);
        Assert.Equal(3, d["ald_v3"]);
        Assert.False(d.ContainsKey("ald_v2"));
        // 下一次製程計數器歸零重算
        Assert.Equal(2, Maintenance.Delta(S(0, s => s with { AldCycle = 100 }), S(1, s => s with { AldCycle = 2, AldRunning = true }), ald)["ald_cycles"]);
    }

    [Fact]
    public void Store_Accumulates_Due_Reset_Persists()
    {
        var path = Path.Combine(Path.GetTempPath(), $"maint_{Guid.NewGuid():N}.db");
        try
        {
            var due = new List<string>();
            using (var st = new MaintenanceStore(path, TimeSpan.FromHours(1)))
            {
                st.BecameDue += r => due.Add(r.Item.Id);
                st.SetLimit("turbo_gv", 3);
                for (int i = 0; i < 8; i++) st.Track(S(i, s => s with { TurboGateOpened = i % 2 == 1 }), null);   // 開 4 次
                var row = st.Rows().First(r => r.Item.Id == "turbo_gv");
                Assert.Equal(4, row.Value);
                Assert.True(row.Due);
                Assert.Equal(new[] { "turbo_gv" }, due);   // 只提醒一次

                st.AddLeakTest(new LeakTestResult(T0, "chamberIonGauge", 1e-6, 2e-6, 300, 600, 0.05, 1e-5, 0.99, 1, true, ""), "eng");
            }   // Dispose 會寫回資料庫

            using (var st = new MaintenanceStore(path))
            {
                st.BecameDue += r => due.Add(r.Item.Id);
                var row = st.Rows().First(r => r.Item.Id == "turbo_gv");
                Assert.Equal(4, row.Value);
                Assert.Equal(3, row.Limit);
                st.Track(S(0), null);
                st.Track(S(1, s => s with { TurboGateOpened = true }), null);
                Assert.Single(due);   // 重開程式不重複提醒

                st.Reset("turbo_gv", "eng");
                row = st.Rows().First(r => r.Item.Id == "turbo_gv");
                Assert.Equal(0, row.Value);
                Assert.Equal(5, row.Total);   // 總累計不歸零
                Assert.Equal("eng", row.ResetBy);
                Assert.False(row.Due);

                var lt = Assert.Single(st.LeakTests());
                Assert.Equal("eng", lt.User);
                Assert.Equal(0.05, lt.Result.RorMtorrPerMin, 6);
                Assert.True(lt.Result.Pass);
                Assert.Throws<ArgumentException>(() => st.SetLimit("turbo_gv", -1));
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (var f in new[] { path, path + "-wal", path + "-shm" }) if (File.Exists(f)) File.Delete(f);
        }
    }
}
