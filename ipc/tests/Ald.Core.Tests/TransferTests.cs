using Ald.Core;
using Xunit;

namespace Ald.Core.Tests;

public class TransferTests
{
    private static readonly TimeSpan Fast = TimeSpan.FromMilliseconds(1);
    private static readonly TransferConfig Cfg = new() { LiftTimeoutS = 0.2, HeaterTimeoutS = 0.2, LoadlockStepTimeoutS = 0.2 };

    /// <summary>PM 抽好真空（GV 開、Turbo 全速）、LL 也在真空。</summary>
    private static FakeMachine Vacuum()
    {
        var m = new FakeMachine();
        m.SetVacuum(VacuumOutputs.DryPumpOn | VacuumOutputs.TurboOn | VacuumOutputs.TurboRough | VacuumOutputs.TurboGv);
        m.Inject(s => s with { ChamberPressureTorr = 1e-4, TurboAtSpeed = true, LoadlockPressureUnderRange = true, ArmZ = LoadlockZ.Down });
        m.Log.Clear();
        return m;
    }

    private static int I(FakeMachine m, string entry) => m.Log.IndexOf(entry);

    [Fact]
    public async Task Load_FullSequence_InSafeOrder()
    {
        var m = Vacuum();
        var map = new WaferMap(); map.Set(true, false, "test");
        var seq = new TransferSequencer(m, map, Cfg, loadlockZEnabled: true, Fast);
        var st = await seq.RunAsync(TransferDirection.LoadToPm);
        Assert.True(st == SequencerState.Done, seq.Step);

        Assert.Equal((false, true), (map.Loadlock, map.Pm));
        Assert.Equal(20000, m.HeaterPos);
        Assert.True(m.LiftUp);
        Assert.Equal(LoadlockSv.Closed, m.Sv);
        Assert.Equal(0, m.ArmPos);
        Assert.True(m.VacuumCommand.HasFlag(VacuumOutputs.TurboGv));                 // GV 恢復

        int gvClose = m.Log.FindIndex(x => x.StartsWith("VAC") && !x.Contains("TurboGv"));
        Assert.True(gvClose >= 0 && gvClose < I(m, "LL SvOpen"));                  // 先關 GV 再開 SV
        Assert.True(I(m, "LL SvOpen") < I(m, "LL ArmToPm"));
        Assert.True(I(m, "LL ArmToPm") < I(m, "LIFT UP"));                         // 手臂到位才接片
        Assert.True(I(m, "LIFT UP") < I(m, "LL ArmToLoadlock"));
        Assert.True(I(m, "LL ArmToLoadlock") < I(m, "LL SvClose"));
        Assert.True(I(m, "LL SvClose") < I(m, "HEATER 20000"));                    // SV 關了才升 Heater
    }

    [Fact]
    public async Task Unload_FromProcessPosition()
    {
        var m = Vacuum();
        m.HeaterPos = 20000; m.LiftUp = true;
        var map = new WaferMap(); map.Set(false, true, "test");
        var seq = new TransferSequencer(m, map, Cfg, loadlockZEnabled: false, Fast);
        Assert.Equal(SequencerState.Done, await seq.RunAsync(TransferDirection.UnloadToLoadlock));
        Assert.Equal((true, false), (map.Loadlock, map.Pm));
        Assert.Equal(0, m.HeaterPos);
        Assert.False(m.LiftUp);
        Assert.True(I(m, "HEATER HOME") < I(m, "LL SvOpen"));
        Assert.True(I(m, "LL ArmToPm") < I(m, "LIFT DOWN"));                       // 手臂在下面才放片
        Assert.True(I(m, "LIFT DOWN") < I(m, "LL ArmToLoadlock"));
    }

    [Theory]
    [InlineData(null, false, "晶圓位置不明")]
    [InlineData(false, false, "手臂上沒有片")]
    [InlineData(true, true, "PM 裡已經有片")]
    public async Task Load_Refused_ByWaferMap(bool? ll, bool? pm, string why)
    {
        var m = Vacuum();
        var map = new WaferMap(); map.Set(ll, pm, "test");
        var seq = new TransferSequencer(m, map, Cfg, true, Fast);
        Assert.Equal(SequencerState.Failed, await seq.RunAsync(TransferDirection.LoadToPm));
        Assert.Empty(m.Log);                                                        // 一個命令都沒下
        string msg = "";
        seq.Progress += p => msg = p.Message;
        await seq.RunAsync(TransferDirection.LoadToPm);
        Assert.Contains(why, msg);
    }

    [Fact]
    public void Precheck_NeedsVacuumOnBothSides()
    {
        var map = new WaferMap(); map.Set(true, false, "test");
        var s = new MachineSnapshot
        {
            Connected = true, SlotOnline = new[] { true, true, true, true, true }, SysState = SystemState.Idle, LoadlockFresh = true,
            ChamberPressureTorr = 760, LoadlockPressureTorr = 760,
        };
        var e = TransferSequencer.Precheck(TransferDirection.LoadToPm, s, map).ToList();
        Assert.Contains(e, x => x.Contains("PM 不在真空"));
        Assert.Contains(e, x => x.Contains("Loadlock 不在真空"));
    }

    [Fact]
    public async Task ArmDenied_Fails_BeforeWaferMoves_MapKept()
    {
        var m = Vacuum();
        m.FailLl = LoadlockCommand.ArmToPm;
        var map = new WaferMap(); map.Set(true, false, "test");
        var seq = new TransferSequencer(m, map, Cfg, true, Fast);
        Assert.Equal(SequencerState.Failed, await seq.RunAsync(TransferDirection.LoadToPm));
        string msg = "";
        seq.Progress += p => msg = p.Message;
        Assert.Contains("ArmToPm", seq.Step + string.Join(",", m.Log));
        Assert.Equal((true, false), (map.Loadlock, map.Pm));                       // 片還在手臂上
        Assert.Contains("LL Abort", m.Log);
        Assert.DoesNotContain("LIFT UP", m.Log);
    }

    [Fact]
    public async Task RoughValveOpen_IsClosedBeforeSv_AndDenyReasonShown()
    {
        var m = Vacuum();
        m.Inject(s => s with { LoadlockRoughOpen = true });                         // LL 抽氣完粗抽閥還開著
        var map = new WaferMap(); map.Set(true, false, "test");
        Assert.Equal(SequencerState.Done, await new TransferSequencer(m, map, Cfg, true, Fast).RunAsync(TransferDirection.LoadToPm));
        Assert.True(I(m, "LL RoughOff") >= 0 && I(m, "LL RoughOff") < I(m, "LL SvOpen"));

        var m2 = Vacuum();
        m2.FailLl = LoadlockCommand.SvOpen;
        var map2 = new WaferMap(); map2.Set(true, false, "test");
        var seq = new TransferSequencer(m2, map2, Cfg, true, Fast);
        string msg = "";
        seq.Progress += p => msg = p.Message;
        Assert.Equal(SequencerState.Failed, await seq.RunAsync(TransferDirection.LoadToPm));
        Assert.Contains("主控板擋下：測試", msg);
    }

    [Fact]
    public async Task LiftStuck_DuringHandover_MarksWaferUnknown()
    {
        var m = Vacuum();
        m.LiftStuck = true;                                                        // 交接時 lift 升不上去
        var map = new WaferMap(); map.Set(true, false, "test");
        var seq = new TransferSequencer(m, map, Cfg, true, Fast);
        Assert.Equal(SequencerState.Failed, await seq.RunAsync(TransferDirection.LoadToPm));
        Assert.Null(map.Loadlock);
        Assert.Null(map.Pm);
        Assert.Contains("LL Abort", m.Log);
        Assert.DoesNotContain("LL ArmToLoadlock", m.Log);                          // 不自動往回動
    }

    [Fact]
    public void WaferMap_PersistsAcrossRestart()
    {
        var path = Path.Combine(Path.GetTempPath(), $"wafer-{Guid.NewGuid():N}.json");
        try
        {
            Assert.Null(WaferMap.Load(path).Pm);                                   // 沒有檔案 = 不明
            WaferMap.Load(path).Set(false, true, "送片完成");
            var b = WaferMap.Load(path);
            Assert.Equal((false, true), (b.Loadlock, b.Pm));
            Assert.Equal("送片完成", b.Note);
            File.WriteAllText(path, "{壞掉");
            Assert.Null(WaferMap.Load(path).Pm);
        }
        finally { File.Delete(path); }
    }
}
