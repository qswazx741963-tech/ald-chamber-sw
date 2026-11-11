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
        Assert.Equal(100, r.Ald!.Cycles);
        Assert.Equal(4, r.Ald.Steps.Count);
        Assert.Equal(new AldStep("Purge", 2, 5000), r.Ald.Steps[1]);
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
                Ald = new AldRecipe { Cycles = 7, Steps = { new("A", 1, 20), new("全關", 0, 100) } },
            };
            RecipeFile.Save(path, r);
            Assert.Contains("測試 配方", File.ReadAllText(path));   // 中文不轉碼
            var b = RecipeFile.Load(path);
            Assert.Equal(r.Name, b.Name);
            Assert.Equal(123, b.PumpTimeoutS);
            Assert.True(b.VentAfter);
            Assert.Equal(200, b.HeaterSpDegC[6]);
            Assert.Equal(r.Ald.Steps, b.Ald!.Steps);
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
}
