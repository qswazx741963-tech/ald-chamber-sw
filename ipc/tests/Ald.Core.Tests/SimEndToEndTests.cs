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
}
