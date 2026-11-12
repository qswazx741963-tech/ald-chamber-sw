using Ald.Core;
using Xunit;

namespace Ald.Core.Tests;

public class RecipeFileTests
{
    [Fact]
    public void ExampleRecipe_Loads()
    {
        var r = RecipeFile.Load(Path.Combine(Repo.Root, "recipes", "example_al2o3.json"));
        Assert.Equal("Example Al2O3", r.Name);
        var seg = Assert.Single(r.Segments);
        Assert.Equal(100, seg.Cycles);
        Assert.Equal(4, seg.Steps.Count);
        Assert.Equal(new AldStep("Purge", 2, 5000), seg.Steps[1]);
        Assert.Equal(20, r.Mfc[1]);
    }

    /// <summary>repo 裡每個配方檔都要能讀、通過檢查，而且不超過 config 的加熱上限 / MFC 滿刻度。</summary>
    [Fact]
    public void AllRepoRecipes_AreValid_ForThisMachine()
    {
        var cfg = Repo.Config;
        var files = Directory.GetFiles(Path.Combine(Repo.Root, "recipes"), "*.json");
        Assert.NotEmpty(files);
        foreach (var f in files)
        {
            var r = RecipeFile.Load(f);
            Assert.Empty(r.Validate());
            for (int i = 0; i < 16; i++) Assert.True(r.HeaterSpDegC[i] <= cfg.Heaters.Channels[i].LimitDegC, $"{f} {cfg.Heaters.Channels[i].Name}");
            for (int i = 0; i < 8; i++) Assert.True(r.Mfc[i] <= cfg.Mfc.Channels[i].FullScale, $"{f} MFC {i + 1}");
        }
    }

    [Fact]
    public void SaveThenLoad_RoundTrips_IncludingAdvancedFields()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ald-recipe-{Guid.NewGuid():N}.json");
        try
        {
            var sp = new double[16]; sp[6] = 200;
            var r = new ProcessRecipe
            {
                Name = "測試 配方", HeaterSpDegC = sp, PumpTimeoutS = 123, VentAfter = true,
                Segments =
                [
                    new AldRecipe { Name = "Al2O3", Cycles = 7, Steps = { new("A", 1, 20), new("全關", 0, 100) } },
                    new AldRecipe { Name = "TiO2", Cycles = 3, Steps = { new("B", 4, 20) { Mfc = [null, 50, null, null, null, null, null, null], Rf1W = 300 } } },
                ],
            };
            RecipeFile.Save(path, r);
            Assert.Contains("測試 配方", File.ReadAllText(path));   // 中文不轉碼
            var b = RecipeFile.Load(path);
            Assert.Equal(r.Name, b.Name);
            Assert.Equal(123, b.PumpTimeoutS);
            Assert.True(b.VentAfter);
            Assert.Equal(200, b.HeaterSpDegC[6]);
            Assert.Null(b.Ald);
            Assert.Equal(2, b.Segments.Count);
            Assert.Equal(r.Segments[0].Steps, b.Segments[0].Steps);
            Assert.Equal(r.Segments[1].Steps, b.Segments[1].Steps);
            Assert.Equal(50, b.Segments[1].Steps[0].Mfc![1]);
            Assert.Null(b.Segments[1].Steps[0].Mfc![0]);
            Assert.DoesNotContain("\"ald\"", File.ReadAllText(path));   // 不再寫舊欄位
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void BrokenJson_GivesReadableError()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ald-recipe-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "{ \"name\": ");
        try { Assert.Contains("格式錯誤", Assert.Throws<FormatException>(() => RecipeFile.Load(path)).Message); }
        finally { File.Delete(path); }
    }

    [Fact]
    public void FileName_IsSafe() => Assert.Equal("Al2O3_100c.json", RecipeFile.FileNameFor(" Al2O3 100c "));

    [Fact]
    public void StepText_RoundTrips()
    {
        var steps = new List<AldStep> { new("A pulse", 1, 20), new("Purge", 2, 5000), new("A+B", 5, 30), new("Wait", 0, 100) };
        var text = AldStepText.Format(steps);
        Assert.Equal("A pulse, V1, 20\nPurge, V2, 5000\nA+B, V1+V3, 30\nWait, -, 100", text);
        Assert.Equal(steps, AldStepText.Parse(text));
    }

    [Fact]
    public void StepText_AcceptsMasksCommentsAndChineseComma()
    {
        var s = AldStepText.Parse("# 註解\nA, 0x05, 20\n\nB，3，40   # 行尾註解\r\nC, v7, 1");
        Assert.Equal(new[] { new AldStep("A", 5, 20), new AldStep("B", 3, 40), new AldStep("C", 64, 1) }, s);
    }

    [Theory]
    [InlineData("A, V1, abc", "不是整數")]
    [InlineData("A, V1", "名稱, 閥, 時間ms")]
    [InlineData("A, X3, 20", "看不懂")]
    [InlineData("A, V17, 20", "看不懂")]
    public void StepText_BadLines_SayWhichLine(string text, string expect)
    {
        var e = Assert.Throws<FormatException>(() => AldStepText.Parse("ok, V1, 1\n" + text));
        Assert.Contains("第 2 行", e.Message);
        Assert.Contains(expect, e.Message);
    }

    [Fact]
    public void OldSingleAldFormat_BecomesOneSegment()
    {
        var r = RecipeFile.Parse("""{ "name": "舊配方", "ald": { "cycles": 5, "steps": [ { "label": "A", "valveMask": 1, "durationMs": 20 } ] } }""");
        var seg = Assert.Single(r.Segments);
        Assert.Equal("舊配方", seg.Name);
        Assert.Equal(5, seg.Cycles);
        Assert.Null(r.Ald);
    }

    [Fact]
    public void MfcSwitch_Presets_Index_And_After()
    {
        double?[] M(params double?[] v) => v.Concat(Enumerable.Repeat((double?)null, 8 - v.Length)).ToArray();
        var seg = new AldRecipe
        {
            Cycles = 10,
            Steps =
            {
                new("A", 1, 20),
                new("P", 2, 2000) { Mfc = M(null, 100) },
                new("B", 4, 20) { Mfc = M(50) },
                new("P", 2, 2000) { Mfc = M(null, 100) },          // 跟第 2 步一樣 = 同一組
                new("x", 0, 10) { Mfc = M() },                     // 全部 null = 不切換
            },
        };
        Assert.Empty(seg.Validate());
        Assert.Equal(2, seg.MfcPresets().Count);
        Assert.Equal(new[] { 0, 1, 2, 1, 0 }, seg.MfcPresetIndex());
        Assert.Equal(new double[] { 50, 100, 7, 0, 0, 0, 0, 0 }, seg.MfcAfter([20, 10, 7, 0, 0, 0, 0, 0]));

        for (int i = 0; i < 4; i++) seg.Steps.Add(new("s", 0, 10) { Mfc = M(i + 1) });
        Assert.Contains(seg.Validate(), e => e.Contains("MFC 切換的組合最多"));
    }

    [Fact]
    public void History_BacksUpOldVersion_AndLogsChanges()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"ald-rh-{Guid.NewGuid():N}");
        var path = Path.Combine(dir, "r.json");
        try
        {
            var r = new ProcessRecipe { Name = "r", Segments = [new AldRecipe { Name = "A", Cycles = 5, Steps = { new("A", 1, 20) } }] };
            var s1 = RecipeHistory.Save(path, r, "eng1", new DateTime(2026, 11, 12, 10, 0, 0));
            Assert.Equal(1, s1.Revision);
            Assert.Equal("eng1", s1.ModifiedBy);
            Assert.Equal(1, RecipeHistory.Save(path, s1, "eng1").Revision);      // 沒改東西：版次不變

            var r2 = s1 with { Segments = [new AldRecipe { Name = "A", Cycles = 8, Steps = { new("A", 1, 25) } }] };
            var s2 = RecipeHistory.Save(path, r2, "eng2", new DateTime(2026, 11, 12, 11, 0, 0));
            Assert.Equal(2, s2.Revision);
            Assert.Equal(5, RecipeFile.Load(Path.Combine(dir, ".history", "r.r1.json")).Segments[0].Cycles);   // 舊版備份
            var log = RecipeHistory.ReadLog(path);
            Assert.StartsWith("2026-11-12 11:00:00  eng2  版次 2", log);                // 新的在前
            Assert.Contains("第 1 段 Cycles：5 → 8", log);
            Assert.Contains("第 1 段 第 1 步 時間 ms：20 → 25", log);
            Assert.Contains("新建", log);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
