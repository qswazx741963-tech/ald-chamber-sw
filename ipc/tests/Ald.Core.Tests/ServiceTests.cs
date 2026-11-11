using Ald.Comm;
using Ald.Comm.Generated;
using Ald.Core;
using Xunit;

namespace Ald.Core.Tests;

internal static class Repo
{
    public static string Root
    {
        get
        {
            var d = new DirectoryInfo(AppContext.BaseDirectory);
            while (d is not null && !File.Exists(Path.Combine(d.FullName, "ipc", "AldChamber.sln"))) d = d.Parent;
            return d?.FullName ?? throw new DirectoryNotFoundException("找不到 repo 根目錄");
        }
    }

    public static MachineConfig Config => MachineConfig.Load(Path.Combine(Root, "ipc", "config", "machine.json"));
}

public class ConfigTests
{
    [Fact]
    public void MachineJson_Loads_AndIsValid()
    {
        var c = Repo.Config;
        Assert.Equal(8, c.Mfc.Channels.Count);
        Assert.Equal(16, c.Heaters.Channels.Count);
        Assert.Equal(SvOpenMode.Below, c.Interlock.SvOpenMode);
        Assert.Equal(GaugeModel.ApgxH, c.Gauges.ChamberConvectron.Model);
        Assert.Equal(GaugeModel.None, c.Gauges.LoadlockGauge.Model);
        Assert.Equal(600, c.Heaters.Channels[12].LimitDegC);   // CHUCK1
    }

    [Fact]
    public void PmGauge_Address_IsVacuumAi3()
    {
        var c = Repo.Config;
        Assert.Equal(633, MachineService.GaugeAddress(c.Gauges.Find(c.Gauges.PmInterlockGauge)!));
        Assert.Equal(ProcessBoard.Input.ManometerRaw, MachineService.GaugeAddress(c.Gauges.ChamberManometer));
    }

    [Fact]
    public void BadConfig_IsRejected()
        => Assert.Throws<InvalidDataException>(() => MachineConfig.Parse("""{ "watchdogMs": 50 }"""));
}

public class SnapshotBuilderTests
{
    private static ushort[] Img(int size) => new ushort[size];

    [Fact]
    public void Build_DecodesBoards()
    {
        var cfg = Repo.Config;
        var main = Img(MainBoard.Input.Size);
        main[MainBoard.Input.SysState] = (ushort)SystemState.Idle;
        main[MainBoard.Input.Di] = (ushort)(MainBoard.Input.DiBits.EmoOk | MainBoard.Input.DiBits.SafeLineOk | MainBoard.Input.DiBits.CdaOk);
        main[MainBoard.Input.PmPresMtorr + 1] = 500;   // 0.5 Torr
        main[MainBoard.Input.LlLink] = (ushort)(MainBoard.Input.LlLinkBits.Online | MainBoard.Input.LlLinkBits.TlmFresh);
        main[MainBoard.Input.LlSv] = 1;
        main[MainBoard.Input.LlPres] = 0xFFFF; main[MainBoard.Input.LlPres + 1] = 0xFFFF;   // -1 = 低於下限
        var heat = Img(HeaterBoard.Input.Size);
        heat[30] = 1505;           // TC1 150.5 °C
        heat[31] = 0x8000;         // TC2 斷線
        var proc = Img(ProcessBoard.Input.Size);
        proc[30] = 5000;           // MFC1 50 %
        proc[20] = (1 << 0) | (1 << 7);
        var vac = Img(VacuumBoard.Input.Size);
        vac[10] = (ushort)(VacuumBoard.Input.DiBits.TurboGvClosed | VacuumBoard.Input.DiBits.DrypumpRunning);
        vac[30 + 4] = 3125;        // Foreline APGX-H 3.125 V = 1e-3 Torr
        var mot = Img(MotionBoard.Input.Size);
        mot[10] = (ushort)(MotionBoard.Input.DiBits.HeaterDown | MotionBoard.Input.DiBits.WaferLiftDown);

        var imgs = new[] { main, proc, heat, vac, mot };
        var s = SnapshotBuilder.Build(true, i => imgs[i], cfg);

        Assert.True(s.AllOnline);
        Assert.False(s.SafeState);
        Assert.Equal(0.5, s.ChamberPressureTorr, 6);
        Assert.Equal(150.5, s.TempC[0], 6);
        Assert.True(double.IsNaN(s.TempC[1]));
        Assert.Equal(50, s.MfcPct[0], 6);
        Assert.Equal(1000, s.MfcValue[0], 6);   // Ar 2000 sccm × 50 %
        Assert.Equal(1, s.AldValves);
        Assert.Equal(1, s.MfcUpstreamValves);
        Assert.True(s.TurboGateClosed && s.DryPumpRunning);
        Assert.Equal(1e-3, s.ForelineTorr, 6);
        Assert.True(s.PmAtTransfer);
        Assert.True(s.LoadlockFresh && s.LoadlockPressureUnderRange && s.SlitClosed);
    }

    [Fact]
    public void Build_Disconnected_IsSafe()
    {
        var s = SnapshotBuilder.Build(false, _ => null, Repo.Config);
        Assert.False(s.Connected);
        Assert.True(s.SafeState);
        Assert.False(s.AllOnline);
    }
}

public class AlarmManagerTests
{
    private static MachineSnapshot Healthy() => new()
    {
        Connected = true, SlotOnline = new[] { true, true, true, true, true }, SysState = SystemState.Idle,
        EmoOk = true, SafeLineOk = true, CdaOk = true, ChamberLidClosed = true, ChamberGaugeStatus = GaugeStatus.Ok,
        LoadlockFresh = true, TempC = Enumerable.Repeat(25.0, 16).ToArray(),
    };

    [Fact]
    public void Healthy_NoAlarm()
    {
        var m = new AlarmManager(Repo.Config);
        Assert.Empty(m.Update(Healthy()));
        Assert.Empty(m.Active);
    }

    [Fact]
    public void RaiseAndClear()
    {
        var m = new AlarmManager(Repo.Config);
        var t = Healthy().TempC.ToArray(); t[6] = double.NaN;
        var c = m.Update(Healthy() with { TempC = t, DryPumpAlarm = true });
        Assert.Contains(c, x => x.Raised && x.Info.Code == "TC07_BROKEN");
        Assert.Contains(c, x => x.Raised && x.Info.Code == "DRYPUMP");
        Assert.True(m.AnyAlarm);
        Assert.Empty(m.Update(Healthy() with { TempC = t, DryPumpAlarm = true }));   // 沒變化不重複通知
        var c2 = m.Update(Healthy());
        Assert.Equal(2, c2.Count(x => !x.Raised));
        Assert.Empty(m.Active);
    }

    [Fact]
    public void SpareHeater_BrokenTc_IsIgnored()   // SPARE1/2 上限 0 = 沒用
    {
        var m = new AlarmManager(Repo.Config);
        var t = Healthy().TempC.ToArray(); t[14] = double.NaN; t[15] = double.NaN;
        Assert.Empty(m.Update(Healthy() with { TempC = t }));
    }

    [Fact]
    public void Disconnected_OnlyLinkAlarm()
    {
        var m = new AlarmManager(Repo.Config);
        var c = m.Update(new MachineSnapshot { Connected = false });
        Assert.Single(c);
        Assert.Equal("LINK", c[0].Info.Code);
    }

    [Fact]
    public void Deny_IsReportedOnce_WithChineseReason()
    {
        var m = new AlarmManager(Repo.Config, DenyReasons.Describe);
        var bits = (uint)MainBoard.Input.IlDenyBits.LlValveConflict | (uint)MainBoard.Input.IlDeny2Bits.TurboGvOpen << 16;
        var c = m.Update(Healthy() with { LastDenyBits = bits, LastDenyAction = 1 });
        var d = Assert.Single(c);
        Assert.Contains("粗抽", d.Info.Message);
        Assert.Contains("Turbo gate valve", d.Info.Message);
        Assert.Empty(m.Update(Healthy() with { LastDenyBits = bits, LastDenyAction = 1 }));
    }
}
