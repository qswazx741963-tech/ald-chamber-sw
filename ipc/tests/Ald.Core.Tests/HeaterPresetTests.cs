using Ald.Core;
using Xunit;

namespace Ald.Core.Tests;

public sealed class HeaterPresetTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"ald-heat-{Guid.NewGuid():N}.json");
    public void Dispose() { File.Delete(_path); File.Delete(_path + ".tmp"); }

    private static Func<int, double?> Board(FakeMachine m) => ch => m.HeaterOn[ch] ? m.HeaterSp[ch] : null;

    [Fact]
    public void NeverSet_NoCompare()
    {
        var p = HeaterPreset.Load(_path);
        Assert.False(p.Defined);
        Assert.Empty(p.Compare(_ => 150));
    }

    [Fact]
    public void PowerLoss_DetectsMismatch_RestoreOnlyDiffs()
    {
        var p = HeaterPreset.Load(_path);
        p.Set(6, true, 150, 5, "test");
        p.Set(7, true, 120.5, 0, "test");

        var m = new FakeMachine();
        m.SetHeater(6, 150, true);
        m.SetHeater(7, 120.5, true);
        Assert.Empty(p.Compare(Board(m)));

        // 斷電重開：加熱板全部變 0 / 關，常駐設定從檔案讀回
        var m2 = new FakeMachine();
        var p2 = HeaterPreset.Load(_path);
        Assert.True(p2.Defined);
        Assert.Equal(new HeaterPresetChannel(true, 120.5, 0), p2[7]);
        var diff = p2.Compare(Board(m2));
        Assert.Equal(new[] { 6, 7 }, diff.Select(d => d.Channel));
        Assert.Equal(150, diff[0].Want);
        Assert.Null(diff[0].Have);
        Assert.Empty(m2.Log);                                   // 只比對，不會自己去加熱

        m2.SetHeater(7, 120.5, true);                           // 有一組已經手動開好
        m2.Log.Clear();
        Assert.Equal(1, p2.Restore(m2, Board(m2)));
        Assert.Equal(new[] { "HEAT 6 150 True" }, m2.Log);
        Assert.Empty(p2.Compare(Board(m2)));
    }

    [Fact]
    public void ExtraHeaterOn_OrDifferentSp_IsMismatch_TakeCurrentAccepts()
    {
        var p = new HeaterPreset(_path);
        p.Set(6, true, 150, 0, "");
        var board = new double?[16];
        board[6] = 150.04;                                      // 解析度內算一樣
        Assert.Empty(p.Compare(i => board[i]));
        board[6] = 151;
        board[3] = 80;                                          // 常駐是關，板子上開著
        var diff = p.Compare(i => board[i]);
        Assert.Equal(new[] { 3, 6 }, diff.Select(d => d.Channel));
        Assert.Contains("常駐 關，目前 80.0 °C", HeaterPreset.Text(diff[0], new[] { "a", "b", "c", "d" }));

        p.TakeCurrent(i => board[i], "accept");
        Assert.Empty(p.Compare(i => board[i]));
        Assert.Empty(HeaterPreset.Load(_path).Compare(i => board[i]));
    }

    [Fact]
    public void BadFile_IsUndefined_WithNote()
    {
        File.WriteAllText(_path, "{ not json");
        var p = HeaterPreset.Load(_path);
        Assert.False(p.Defined);
        Assert.NotEmpty(p.Note);
    }
}

public sealed class WaferIdTests
{
    [Fact]
    public void Id_FollowsWafer_LlToPmAndBack_ClearedWhenRemoved()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ald-wafer-{Guid.NewGuid():N}.json");
        try
        {
            var m = new WaferMap(path);
            m.Set(true, false, "放入", "W01");
            Assert.Equal(("W01", ""), (m.LoadlockId, m.PmId));
            m.Set(false, true, "送片");
            Assert.Equal(("", "W01"), (m.LoadlockId, m.PmId));
            var r = WaferMap.Load(path);                           // 重開機讀回
            Assert.Equal("W01", r.PmId);
            m.Set(true, false, "取片");
            Assert.Equal(("W01", ""), (m.LoadlockId, m.PmId));
            m.SetIds(null, "X");                                    // PM 無片，不記
            Assert.Equal("", m.PmId);
            m.Set(false, false, "取出");
            Assert.Equal(("", ""), (m.LoadlockId, m.PmId));
            m.Set(null, null, "不明");
            Assert.Equal(("", ""), (m.LoadlockId, m.PmId));
        }
        finally { File.Delete(path); }
    }
}
