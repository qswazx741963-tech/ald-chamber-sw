using Ald.Core;
using Xunit;

namespace Ald.Core.Tests;

/// <summary>假機台：命令下去，下一次讀狀態就到位（溫度一步到設定值）。用來測流程引擎的步驟與失敗處理。</summary>
internal sealed class FakeMachine : IMachine
{
    private MachineSnapshot _s = new()
    {
        Connected = true, SlotOnline = new[] { true, true, true, true, true }, SysState = SystemState.Idle,
        LoadlockFresh = true, SlitValve = LoadlockSv.Closed, ArmZ = LoadlockZ.Down, ChamberPressureTorr = 760,
        TurboGateClosed = true, ChamberGaugeStatus = GaugeStatus.OverRange, ManometerTorr = 760,
        TempC = Enumerable.Repeat(25.0, 16).ToArray(), ChamberLidClosed = true,
    };
    public readonly double[] HeaterSp = new double[16];
    public readonly bool[] HeaterOn = new bool[16];
    public readonly double[] Mfc = new double[8];
    public AldRecipe? Downloaded;
    public int AldTicks;
    public bool AldRunning;
    public bool BrokenTurbo;
    public List<string> Log = new();
    // 傳片用：PM 機構與 Loadlock（命令下去下一次讀狀態就到位）
    public int HeaterPos;
    public bool LiftUp;
    public bool LiftStuck;
    public LoadlockSv Sv = LoadlockSv.Closed;
    public int ArmPos;
    public LoadlockCommand? FailLl;
    public int HeaterBroken = -1;   // 這一組加熱升不上去
    // 漏率測試用：PM 隔離（GV / 粗抽 / vent 都關）時壓力每秒上升多少 Torr
    public double LeakTorrPerS;
    private DateTime _lastRead = DateTime.Now;

    public MachineSnapshot Snapshot
    {
        get
        {
            // 每讀一次推進一點「物理」
            var s = _s;
            var t = s.TempC.ToArray();
            for (int i = 0; i < 16; i++) t[i] = HeaterOn[i] && i != HeaterBroken ? HeaterSp[i] : 25;
            double p = s.ChamberPressureTorr;
            var o = VacuumCommand;
            var now = DateTime.Now;
            double dt = (now - _lastRead).TotalSeconds;
            _lastRead = now;
            if (o.HasFlag(VacuumOutputs.ChamberVent)) p = Math.Min(760, p * 4 + 10);
            else if (o.HasFlag(VacuumOutputs.TurboGv) && s.TurboAtSpeed) p = Math.Max(1e-6, p / 10);
            else if (o.HasFlag(VacuumOutputs.ChamberRough)) p = Math.Max(0.02, p / 4);
            else p += LeakTorrPerS * dt;
            bool turbo = o.HasFlag(VacuumOutputs.TurboOn) && !BrokenTurbo;
            int cycle = s.AldCycle;
            if (AldRunning && ++AldTicks > 3) { AldRunning = false; cycle = Downloaded!.Cycles; }
            _s = s with
            {
                TempC = t, ChamberPressureTorr = p, ManometerTorr = p, IonGaugeTorr = p, DryPumpRunning = o.HasFlag(VacuumOutputs.DryPumpOn),
                TurboAtSpeed = turbo, TurboGateOpened = o.HasFlag(VacuumOutputs.TurboGv), TurboGateClosed = !o.HasFlag(VacuumOutputs.TurboGv),
                ChamberVenting = o.HasFlag(VacuumOutputs.ChamberVent), AldRunning = AldRunning, AldCycle = cycle,
                ChamberGaugeStatus = p > 700 ? GaugeStatus.OverRange : GaugeStatus.Ok,
                HeaterPosSteps = HeaterPos, HeaterDown = HeaterPos == 0, HeaterAtProcess = HeaterPos == 20000,
                HeaterAxis = AxisState.Idle, WaferLiftUp = LiftUp, WaferLiftDown = !LiftUp,
                SlitValve = Sv, ArmPosSteps = ArmPos,
            };
            return _s;
        }
    }

    public void Inject(Func<MachineSnapshot, MachineSnapshot> f) => _s = f(_s);

    public VacuumOutputs VacuumCommand { get; private set; }
    public void SetVacuum(VacuumOutputs o) { VacuumCommand = o; Log.Add($"VAC {o}"); }
    public void SetHeater(int ch, double sp, bool en, double ramp = 0) { HeaterSp[ch] = sp; HeaterOn[ch] = en; }
    public void SetMfc(int ch, double v, bool up) => Mfc[ch] = up ? v : 0;
    public void SetAldValves(ushort mask) => Log.Add($"ALDV {mask}");
    public void DownloadAld(AldRecipe r) => Downloaded = r;
    public void StartAld() { AldRunning = true; AldTicks = 0; Log.Add("START"); }
    public void StopAld() { AldRunning = false; Log.Add("STOP"); }
    public Task<bool> LoadlockAsync(LoadlockCommand c, int a, TimeSpan t, CancellationToken ct)
    {
        Log.Add($"LL {c}");
        LastLoadlockError = "";
        if (c == FailLl) { LastLoadlockError = "主控板擋下：測試"; return Task.FromResult(false); }
        switch (c)
        {
            case LoadlockCommand.SvOpen: Sv = LoadlockSv.Open; break;
            case LoadlockCommand.SvClose: Sv = LoadlockSv.Closed; break;
            case LoadlockCommand.ArmToPm: ArmPos = 18420; break;
            case LoadlockCommand.ArmToLoadlock: ArmPos = 0; break;
            case LoadlockCommand.RoughOff: _s = _s with { LoadlockRoughOpen = false }; break;
            case LoadlockCommand.Pump: _s = _s with { LoadlockPressureUnderRange = true, LoadlockRoughOpen = true }; break;
        }
        return Task.FromResult(true);
    }
    public string LastLoadlockError { get; private set; } = "";
    public Task HeaterHomeAsync(ushort sp, CancellationToken ct = default) { HeaterPos = 0; Log.Add("HEATER HOME"); return Task.CompletedTask; }
    public Task HeaterMoveAsync(int t, ushort sp, CancellationToken ct = default) { HeaterPos = t; Log.Add($"HEATER {t}"); return Task.CompletedTask; }
    public void SetWaferLift(bool up) { if (!LiftStuck) LiftUp = up; Log.Add(up ? "LIFT UP" : "LIFT DOWN"); }
}

public class SequencerTests
{
    private static readonly TimeSpan Fast = TimeSpan.FromMilliseconds(1);

    private static ProcessRecipe Recipe(bool vent = true)
    {
        var sp = new double[16]; sp[6] = 150; sp[7] = 150;
        var mfc = new double[8]; mfc[1] = 20;
        return new ProcessRecipe
        {
            Name = "test", HeaterSpDegC = sp, HeatSettleS = 0.01, Mfc = mfc, GasStabilizeS = 0.01, VentAfter = vent,
            Ald = new AldRecipe { Cycles = 5, Steps = { new("A", 1, 20), new("P", 2, 50) } },
        };
    }

    [Fact]
    public async Task FullRecipe_Completes_InOrder()
    {
        var m = new FakeMachine();
        var seq = new ProcessSequencer(m, Fast);
        var steps = new List<string>();
        seq.Progress += p => steps.Add(p.Step);
        var st = await seq.RunAsync(Recipe());
        Assert.Equal(SequencerState.Done, st);
        Assert.Equal(5, m.Downloaded!.Cycles);
        Assert.Contains("START", m.Log);
        Assert.All(m.Mfc, v => Assert.Equal(0, v));                       // 製程結束關氣
        Assert.False(m.VacuumCommand.HasFlag(VacuumOutputs.ChamberVent)); // 回填完關 vent
        Assert.False(m.VacuumCommand.HasFlag(VacuumOutputs.TurboGv));
        int iPump = steps.FindIndex(x => x.StartsWith("抽真空")), iHeat = steps.IndexOf("升溫"),
            iAld = steps.FindIndex(x => x.StartsWith("ALD")), iVent = steps.FindIndex(x => x.StartsWith("回填"));
        Assert.True(iPump < iHeat && iHeat < iAld && iAld < iVent);
    }

    [Fact]
    public async Task TurboFailure_Fails_AndStopsGas()
    {
        var m = new FakeMachine { BrokenTurbo = true };
        var r = Recipe() with { TurboSpinUpTimeoutS = 0.05 };
        var seq = new ProcessSequencer(m, Fast);
        Assert.Equal(SequencerState.Failed, await seq.RunAsync(r));
        Assert.Contains("Turbo", seq.Step);
        Assert.Contains("STOP", m.Log);
    }

    [Fact]
    public async Task SafeState_DuringRun_Fails()
    {
        var m = new FakeMachine();
        var sp = new double[16]; sp[0] = 100;
        var r = Recipe() with { PumpDown = false, HeaterSpDegC = sp, HeatSettleS = 5 };
        var seq = new ProcessSequencer(m, Fast);
        var task = seq.RunAsync(r);
        await Task.Delay(50);
        m.Inject(s => s with { SysState = SystemState.Safe, SafeState = true });
        Assert.Equal(SequencerState.Failed, await task);
    }

    [Fact]
    public async Task Abort_StopsAld()
    {
        var m = new FakeMachine();
        var r = Recipe() with { PumpDown = false, HeatSettleS = 5 };
        var seq = new ProcessSequencer(m, Fast);
        var task = seq.RunAsync(r);
        await Task.Delay(50);
        seq.Abort();
        Assert.Equal(SequencerState.Aborted, await task);
        Assert.Contains("STOP", m.Log);
    }

    [Fact]
    public async Task NotStarted_WhenSafe()
    {
        var m = new FakeMachine();
        m.Inject(s => s with { SafeState = true });
        Assert.Equal(SequencerState.Failed, await new ProcessSequencer(m, Fast).RunAsync(Recipe()));
        Assert.DoesNotContain(m.Log, x => x.StartsWith("VAC"));
    }
}
