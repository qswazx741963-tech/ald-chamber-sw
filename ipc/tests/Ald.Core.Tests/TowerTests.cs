using Ald.Core;
using Xunit;

namespace Ald.Core.Tests;

public class TowerTests
{
    private static readonly TowerConfig Cfg = new();
    private static TowerLamp C(TowerInputs i, bool blink = true) => Tower.Compute(i, Cfg, blink);

    [Fact]
    public void Idle_Yellow_Running_Green()
    {
        Assert.Equal(TowerLamp.Yellow, C(new()));
        Assert.Equal(TowerLamp.Green, C(new() { Running = true }));
        Assert.Equal(TowerLamp.None, Tower.Compute(new(), Cfg with { IdleYellow = false }, true));
    }

    [Fact]
    public void UnackedAlarm_RedBlinks_WithBuzzer_AckedSteady()
    {
        var un = new TowerInputs { AnyAlarm = true, UnackedAlarm = true, Running = true };
        Assert.Equal(TowerLamp.Red | TowerLamp.Green | TowerLamp.Buzzer, C(un, blink: true));
        Assert.Equal(TowerLamp.Green, C(un, blink: false));                   // 閃的暗相、斷續蜂鳴
        Assert.Equal(TowerLamp.Buzzer, Tower.Compute(un with { Running = false }, Cfg with { BuzzerIntermittent = false }, false));   // 連續叫
        Assert.Equal(TowerLamp.Red, C(new() { AnyAlarm = true }, blink: false));   // 確認後常亮、不叫
        Assert.Equal(TowerLamp.Red, C(new() { Safe = true }));                      // 安全狀態紅、不叫、不亮待機黃
    }

    [Fact]
    public void Warning_Yellow_NoBuzzerByDefault()
    {
        var w = new TowerInputs { AnyWarning = true, UnackedWarning = true };
        Assert.Equal(TowerLamp.Yellow, C(w, true));
        Assert.Equal(TowerLamp.None, C(w, false));
        Assert.True(Tower.Compute(w, Cfg with { BuzzOnWarning = true }, true).HasFlag(TowerLamp.Buzzer));
        Assert.Equal(TowerLamp.Yellow | TowerLamp.Green, C(new() { AnyWarning = true, Running = true }, false));
    }

    [Fact]
    public void NeedOperator_BlueBlinks()
    {
        Assert.Equal(TowerLamp.Yellow | TowerLamp.Blue, C(new() { NeedOperator = true }, true));
        Assert.Equal(TowerLamp.Yellow, C(new() { NeedOperator = true }, false));
    }

    private static MachineSnapshot Ok(DateTime t) => new()
    {
        Connected = true, Time = t, SlotOnline = new[] { true, true, true, true, true }, EmoOk = true, SafeLineOk = true, CdaOk = true,
        LoadlockFresh = true, ChamberLidClosed = true, ChamberGaugeStatus = GaugeStatus.Ok, TempC = Enumerable.Repeat(25.0, 16).ToArray(),
    };

    [Fact]
    public void AlarmAck_AndLatch()
    {
        var cfg = MachineConfig.Load(Path.Combine(Repo.Root, "ipc", "config", "machine.json"));
        var m = new AlarmManager(cfg);
        var t0 = new DateTime(2026, 11, 11, 8, 0, 0);
        m.Update(Ok(t0));
        // 開機寬限內：CDA 掉一下又回來 → 不留存
        m.Update(Ok(t0.AddSeconds(1)) with { CdaOk = false });
        m.Update(Ok(t0.AddSeconds(2)));
        Assert.False(m.UnackedAlarm);

        // 寬限後：異常發生 → 未確認；消失但沒確認 → 留存；確認後清掉
        m.Update(Ok(t0.AddSeconds(20)) with { CdaOk = false });
        Assert.True(m.UnackedAlarm);
        m.Update(Ok(t0.AddSeconds(21)));
        Assert.False(m.AnyAlarm);
        Assert.True(m.UnackedAlarm);
        Assert.Single(m.Latched);
        Assert.Equal(1, m.Acknowledge());
        Assert.False(m.UnackedAlarm);

        // 確認後還在的異常：紅燈常亮（AnyAlarm）但不再是未確認
        m.Update(Ok(t0.AddSeconds(30)) with { DryPumpAlarm = true });
        m.Acknowledge();
        Assert.True(m.AnyAlarm);
        Assert.False(m.UnackedAlarm);
        Assert.True(m.Active.Single().Acked);
        m.Update(Ok(t0.AddSeconds(31)));   // 已確認的消失 → 不留存
        Assert.Empty(m.Latched);

        // 警告：未確認 → 確認
        m.Update(Ok(t0.AddSeconds(40)) with { ChamberLidClosed = false });
        Assert.True(m.UnackedWarning);
        m.Acknowledge();
        Assert.False(m.UnackedWarning);
    }
}
