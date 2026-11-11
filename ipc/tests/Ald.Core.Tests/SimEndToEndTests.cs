using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Ald.Comm;
using Ald.Core;
using Xunit;
using Xunit.Abstractions;

namespace Ald.Core.Tests;

/// <summary>
/// 端到端：C# MachineService ─Modbus TCP→ ald-ecat --sim（虛擬機台：正式主控板 + 4 塊執行板韌體 + 物理模型）。
/// 模擬 20 倍速。ald-ecat 沒編（make -C ipc/ecat-master）時略過。
/// </summary>
public sealed class SimEndToEndTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly Process? _ecat;
    private readonly int _port;

    public SimEndToEndTests(ITestOutputHelper output)
    {
        _out = output;
        var exe = Path.Combine(Repo.Root, "ipc", "ecat-master", "ald-ecat");
        if (!File.Exists(exe)) return;
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        _port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        _ecat = Process.Start(new ProcessStartInfo(exe, $"--sim --speed 20 --port {_port}")
        { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false });
        Thread.Sleep(300);
    }

    public void Dispose()
    {
        if (_ecat is { HasExited: false }) _ecat.Kill();
        _ecat?.Dispose();
    }

    private MachineService NewService()
    {
        var cfg = Repo.Config with
        {
            MainBoard = new MainBoardConfig { Host = "127.0.0.1", Port = _port, PollMs = 50 },
            WatchdogMs = 10000,   // 20 倍速：IPC 心跳 50 ms（真實）= 1 s（模擬）
        };
        return new MachineService(cfg);
    }

    private static async Task<bool> Until(Func<bool> cond, double seconds)
    {
        var end = DateTime.Now.AddSeconds(seconds);
        while (DateTime.Now < end) { if (cond()) return true; await Task.Delay(50); }
        return cond();
    }

    [Fact]
    public async Task ConnectResetAndRunRecipe_OnVirtualMachine()
    {
        if (_ecat is null) { _out.WriteLine("略過：ald-ecat 沒有編譯"); return; }
        using var m = NewService();
        var alarms = new List<AlarmChange>();
        m.AlarmChanged += c => { lock (alarms) alarms.Add(c); };
        m.Start();

        Assert.True(await Until(() => m.Snapshot.AllOnline && m.Snapshot.LoadlockFresh, 10), "連線 / 各槽上線");
        Assert.True(m.Snapshot.SafeState);                 // 開機一律安全狀態
        await m.ResetAsync();
        Assert.True(await Until(() => !m.Snapshot.SafeState, 5), "Reset 後離開安全狀態");
        Assert.True(await Until(() => m.Snapshot.SlotStatus.Skip(1).All(st => (st & 4) == 0), 5), "各執行板離開安全狀態");

        // Loadlock 先抽好（LL-07），關粗抽，手臂原點
        Assert.True(await m.LoadlockAsync(LoadlockCommand.Pump, 0, TimeSpan.FromSeconds(30), default), "LL 抽氣");
        Assert.True(await m.LoadlockAsync(LoadlockCommand.RoughOff, 0, TimeSpan.FromSeconds(5), default));
        Assert.True(await m.LoadlockAsync(LoadlockCommand.ArmHome, 0, TimeSpan.FromSeconds(10), default));

        var sp = new double[16]; sp[6] = 120;              // Chamber upper
        var recipe = new ProcessRecipe
        {
            Name = "e2e", HeaterSpDegC = sp, HeatToleranceC = 5, HeatSettleS = 1, HeatTimeoutS = 60,
            BaseTorr = 5e-3, PumpTimeoutS = 30, TurboSpinUpTimeoutS = 30,
            Mfc = new double[] { 0, 20, 0, 0, 0, 0, 0, 0 }, GasStabilizeS = 0.5,
            Ald = new AldRecipe { Cycles = 20, Steps = { new("A", 1, 20), new("Purge", 2, 100), new("B", 4, 20), new("Purge", 2, 100) } },
            VentAfter = true, VentTimeoutS = 30, TurboSpinDownTimeoutS = 30,
        };
        var seq = new ProcessSequencer(m, TimeSpan.FromMilliseconds(50));
        seq.Progress += p => _out.WriteLine($"{p.Time:HH:mm:ss.fff} {p.State} {p.Step} {p.Message}");
        var st = await seq.RunAsync(recipe);
        Assert.Equal(SequencerState.Done, st);
        Assert.True(m.Snapshot.ManometerTorr > 700, $"回填後 {m.Snapshot.ManometerTorr} Torr");
        Assert.Equal(20, m.Snapshot.AldCycle);

        // 互鎖：PM 在大氣、LL 在真空 → 開 SV 要被主控板擋（IPC 預檢也擋）
        var r = m.Loadlock.Execute(LoadlockCommand.SvOpen);
        Assert.False(r.Allowed);
        _out.WriteLine("開 SV 被擋：" + r.Reason);

        lock (alarms) foreach (var a in alarms) _out.WriteLine($"{(a.Raised ? "發生" : "解除")} {a.Info.Code} {a.Info.Message}");
        Assert.DoesNotContain(m.Alarms.Active, a => a.Info.Level == AlarmLevel.Alarm);
    }

    [Fact]
    public async Task HeartbeatLoss_PutsMachineSafe_AndConfigIsRewrittenOnReconnect()
    {
        if (_ecat is null) { _out.WriteLine("略過：ald-ecat 沒有編譯"); return; }
        var m = NewService();
        m.Start();
        Assert.True(await Until(() => m.Snapshot.AllOnline, 10));
        await m.ResetAsync();
        Assert.True(await Until(() => !m.Snapshot.SafeState, 5));
        m.Dispose();                                   // 人機當掉 → 心跳停止
        await Task.Delay(1500);                        // 模擬 30 s > 看門狗 10 s

        using var m2 = NewService();
        m2.Start();
        Assert.True(await Until(() => m2.Snapshot.AllOnline, 10));
        Assert.True(m2.Snapshot.SafeState, "心跳中斷後應在安全狀態");
        Assert.Contains(m2.Alarms.Active, a => a.Info.Code == "SAFE");
    }

    [Fact]
    public async Task WaferTransfer_LoadThenUnload_OnVirtualMachine()
    {
        if (_ecat is null) { _out.WriteLine("略過：ald-ecat 沒有編譯"); return; }
        using var m = NewService();
        m.Start();
        Assert.True(await Until(() => m.Snapshot.AllOnline && m.Snapshot.LoadlockFresh, 10), "連線 / 各槽上線");
        await m.ResetAsync();
        Assert.True(await Until(() => !m.Snapshot.SafeState && m.Snapshot.SlotStatus.Skip(1).All(st => (st & 4) == 0), 5), "Reset");

        Assert.True(await m.LoadlockAsync(LoadlockCommand.Pump, 0, TimeSpan.FromSeconds(30), default), "LL 抽氣");
        Assert.True(await m.LoadlockAsync(LoadlockCommand.RoughOff, 0, TimeSpan.FromSeconds(5), default));
        Assert.True(await m.LoadlockAsync(LoadlockCommand.ArmHome, 0, TimeSpan.FromSeconds(10), default));
        var pump = new ProcessRecipe { Name = "抽真空", BaseTorr = 5e-3, PumpTimeoutS = 30, TurboSpinUpTimeoutS = 30 };
        Assert.Equal(SequencerState.Done, await new ProcessSequencer(m, TimeSpan.FromMilliseconds(50)).RunAsync(pump));

        var map = new WaferMap();
        map.Set(true, false, "測試：手臂上放一片");
        var cfg = m.Config.Transfer with { LoadlockStepTimeoutS = 20, HeaterTimeoutS = 20 };
        foreach (var dir in new[] { TransferDirection.LoadToPm, TransferDirection.UnloadToLoadlock })
        {
            var x = new TransferSequencer(m, map, cfg, m.Config.Interlock.LoadlockZEnabled, TimeSpan.FromMilliseconds(50));
            x.Progress += p => _out.WriteLine($"{p.Time:HH:mm:ss.fff} {p.State} {p.Step} {p.Message}");
            Assert.Equal(SequencerState.Done, await x.RunAsync(dir));
            Assert.True(m.Snapshot.SlitClosed && m.Snapshot.ArmClearOfSv);
            Assert.True(await Until(() => m.Snapshot.TurboGateOpened, 5), "GV 恢復");
        }
        Assert.Equal((true, false), (map.Loadlock, map.Pm));
        Assert.True(m.Snapshot.HeaterDown);
        Assert.DoesNotContain(m.Alarms.Active, a => a.Info.Level == AlarmLevel.Alarm);
    }

    [Fact]
    public async Task AutoRun_FromAtmosphere_OnVirtualMachine()
    {
        if (_ecat is null) { _out.WriteLine("略過：ald-ecat 沒有編譯"); return; }
        using var m = NewService();
        m.Start();
        Assert.True(await Until(() => m.Snapshot.AllOnline && m.Snapshot.LoadlockFresh, 10), "連線 / 各槽上線");
        await m.ResetAsync();
        Assert.True(await Until(() => !m.Snapshot.SafeState && m.Snapshot.SlotStatus.Skip(1).All(st => (st & 4) == 0), 5), "Reset");
        Assert.True(await m.LoadlockAsync(LoadlockCommand.ArmHome, 0, TimeSpan.FromSeconds(10), default));

        var r = RecipeFile.Load(Path.Combine(Repo.Root, "recipes", "sim_quick_test.json"));
        var map = new WaferMap();
        map.Set(true, false, "測試");
        var job = new AutoRunJob(m, map, m.Config.Transfer with { LoadlockStepTimeoutS = 20, HeaterTimeoutS = 20 },
                                 m.Config.Interlock.LoadlockZEnabled, TimeSpan.FromMilliseconds(50));
        job.Progress += p => _out.WriteLine($"{p.Time:HH:mm:ss.fff} {p.State} {p.Step} {p.Message}");
        Assert.Equal(SequencerState.Done, await job.RunAsync(r));
        Assert.Equal((true, false), (map.Loadlock, map.Pm));
        Assert.Equal(20, m.Snapshot.AldCycle);
        Assert.True(m.Snapshot.ManometerTorr > 700, "回填");
        Assert.True(m.Snapshot.SlitClosed);
        Assert.DoesNotContain(m.Alarms.Active, a => a.Info.Level == AlarmLevel.Alarm);
    }

    [Fact]
    public async Task LeakTest_OnVirtualMachine()
    {
        if (_ecat is null) { _out.WriteLine("略過：ald-ecat 沒有編譯"); return; }
        using var m = NewService();
        m.Start();
        Assert.True(await Until(() => m.Snapshot.AllOnline && m.Snapshot.LoadlockFresh, 10), "連線 / 各槽上線");
        await m.ResetAsync();
        Assert.True(await Until(() => !m.Snapshot.SafeState && m.Snapshot.SlotStatus.Skip(1).All(st => (st & 4) == 0), 5), "Reset");

        // 虛擬機台預設漏氣 1e-5 Torr·L/s ÷ 20 L = 0.03 mTorr/min（模擬時間）；模擬加速，實際量到的是倍數
        var seq = new LeakTestSequencer(m, m.Config.LeakTest with { PumpS = 1, SettleS = 1, MeasureS = 6 }, TimeSpan.FromMilliseconds(100));
        seq.Progress += p => _out.WriteLine($"{p.Time:HH:mm:ss.fff} {p.State} {p.Step} {p.Message}");
        Assert.Equal(SequencerState.Done, await seq.RunAsync());
        var r = seq.Result!;
        _out.WriteLine(r.Text());
        Assert.True(r.Pass, r.Text());
        Assert.True(r.RorMtorrPerMin > 0);
        Assert.True(r.Samples > 20);
        Assert.True(await Until(() => m.Snapshot.TurboGateOpened, 10), "量完 GV 開回");
        Assert.DoesNotContain(m.Alarms.Active, a => a.Info.Level == AlarmLevel.Alarm);
    }

    [Fact]
    public async Task TowerLamp_FollowsIpc_SafeForcesRed()
    {
        if (_ecat is null) { _out.WriteLine("略過：ald-ecat 沒有編譯"); return; }
        using var m = NewService();
        m.Start();
        Assert.True(await Until(() => m.Snapshot.AllOnline, 10), "連線");
        // 安全狀態：IPC 只要綠燈，主控板仍強制紅燈
        m.SetTower(TowerLamp.Green);
        Assert.True(await Until(() => m.Snapshot.Tower == (TowerLamp.Green | TowerLamp.Red), 5), $"安全狀態紅燈 {m.Snapshot.Tower}");
        await m.ResetAsync();
        Assert.True(await Until(() => !m.Snapshot.SafeState, 5), "Reset");
        Assert.True(await Until(() => m.Snapshot.Tower == TowerLamp.Green, 5), $"解除後照 IPC {m.Snapshot.Tower}");
        m.SetTower(TowerLamp.Blue | TowerLamp.Buzzer | TowerLamp.Yellow);
        Assert.True(await Until(() => m.Snapshot.Tower == (TowerLamp.Blue | TowerLamp.Buzzer | TowerLamp.Yellow), 5), $"{m.Snapshot.Tower}");
    }

    [Fact]
    public async Task Batch_TwoWafers_OnVirtualMachine()
    {
        if (_ecat is null) { _out.WriteLine("略過：ald-ecat 沒有編譯"); return; }
        using var m = NewService();
        m.Start();
        Assert.True(await Until(() => m.Snapshot.AllOnline && m.Snapshot.LoadlockFresh, 10), "連線 / 各槽上線");
        await m.ResetAsync();
        Assert.True(await Until(() => !m.Snapshot.SafeState && m.Snapshot.SlotStatus.Skip(1).All(st => (st & 4) == 0), 5), "Reset");
        Assert.True(await m.LoadlockAsync(LoadlockCommand.ArmHome, 0, TimeSpan.FromSeconds(10), default));

        var r = RecipeFile.Load(Path.Combine(Repo.Root, "recipes", "sim_quick_test.json"));
        var map = new WaferMap();
        map.Set(false, false, "測試");
        var job = new BatchJob(m, map, m.Config.Transfer with { LoadlockStepTimeoutS = 20, HeaterTimeoutS = 20 },
                               m.Config.Interlock.LoadlockZEnabled, TimeSpan.FromMilliseconds(50));
        int prompts = 0;
        job.Progress += p =>
        {
            if (!p.Step.Contains("ALD：cycle")) _out.WriteLine($"{p.Time:HH:mm:ss.fff} {p.State} {p.Step} {p.Message}");
            if (p.Step.EndsWith("等待人員")) { prompts++; _ = Task.Run(async () => { await Task.Delay(100); job.Confirm(); }); }
        };
        var st = await job.RunAsync(new[] { new BatchItem("W01", r), new BatchItem("W02", r) }, ventPmAtEnd: true);
        Assert.Equal(SequencerState.Done, st);
        Assert.Equal(3, prompts);
        Assert.All(job.Results, x => Assert.Equal(BatchItemState.Done, x.State));
        Assert.Equal((false, false), (map.Loadlock, map.Pm));
        Assert.True(m.Snapshot.ManometerTorr > 700, "最後 PM 回填");
        Assert.True(m.Snapshot.SlitClosed);
        Assert.DoesNotContain(m.Alarms.Active, a => a.Info.Level == AlarmLevel.Alarm);
    }
}
