using Ald.Core;
using Xunit;

namespace Ald.Core.Tests;

public sealed class MfcSpecTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"ald-mfc-{Guid.NewGuid():N}.json");
    public void Dispose() { File.Delete(_path); File.Delete(_path + ".tmp"); }

    private static List<MfcChannelConfig> Eight() =>
        Enumerable.Range(1, 8).Select(i => new MfcChannelConfig { Name = $"Ar {i}", Use = "Purge", FullScale = 200, Unit = "sccm" }).ToList();

    [Fact]
    public void MissingFile_UsesConfigDefault()
    {
        var d = Eight();
        Assert.Equal(d, MfcSpecs.Load(_path, d));
    }

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        var a = Eight();
        a[3] = a[3] with { Name = "N2 500 sccm", FullScale = 500 };
        MfcSpecs.Save(_path, a);
        var b = MfcSpecs.Load(_path, Eight());
        Assert.Equal(500, b[3].FullScale);
        Assert.Equal("N2 500 sccm", b[3].Name);
        Assert.Equal(["MFC 4 名稱 Ar 4 → N2 500 sccm", "MFC 4 滿刻度 200 → 500"], MfcSpecs.Diff(Eight(), b));
    }

    [Fact]
    public void BadFile_FallsBackAndValidateCatchesErrors()
    {
        var bad = Eight();
        bad[1] = bad[1] with { FullScale = 0 };
        bad[2] = bad[2] with { Name = " " };
        Assert.Equal(2, MfcSpecs.Validate(bad).Count());
        MfcSpecs.Save(_path, bad);
        Assert.Equal(200, MfcSpecs.Load(_path, Eight())[1].FullScale);   // 不合格的檔不採用
        File.WriteAllText(_path, "not json");
        Assert.Equal(8, MfcSpecs.Load(_path, Eight()).Count);
    }
}
