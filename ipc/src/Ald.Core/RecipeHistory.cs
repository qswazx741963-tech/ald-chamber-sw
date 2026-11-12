using System.Globalization;
using System.Text.Json.Nodes;

namespace Ald.Core;

/// <summary>
/// 配方修改紀錄（2026-11-12）：每次存檔
///   1. 舊版備份到 &lt;配方資料夾&gt;/.history/&lt;檔名&gt;.r&lt;版次&gt;.json（要退回舊版時把它複製回去）
///   2. 在 .history/&lt;檔名&gt;.log 加一筆：時間、修改人、版次、改了哪些欄位（舊值 → 新值）
/// 純文字，用記事本就能看；不放資料庫，配方資料夾整個複製就帶著紀錄走。
/// </summary>
public static class RecipeHistory
{
    public static string Dir(string recipePath) => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(recipePath))!, ".history");
    public static string LogPath(string recipePath) => Path.Combine(Dir(recipePath), Path.GetFileNameWithoutExtension(recipePath) + ".log");

    /// <summary>存新版：補上版次 / 修改人 / 時間，備份舊版、寫紀錄、存檔。回傳實際存下的配方。</summary>
    public static ProcessRecipe Save(string path, ProcessRecipe r, string user, DateTime? now = null)
    {
        var t = now ?? DateTime.Now;
        ProcessRecipe? old = null;
        if (File.Exists(path)) { try { old = RecipeFile.Load(path); } catch (FormatException) { /* 舊檔壞掉就當新檔 */ } }
        var saved = r with { Revision = Math.Max(r.Revision, old?.Revision ?? 0) + 1, ModifiedBy = user, ModifiedAt = t };

        var changes = old is null ? new List<string> { "新建" } : Diff(old, saved);
        if (old is not null && changes.Count == 0) return old;   // 沒有改東西：不加版次
        Directory.CreateDirectory(Dir(path));
        if (old is not null && File.Exists(path))
            File.Copy(path, Path.Combine(Dir(path), $"{Path.GetFileNameWithoutExtension(path)}.r{old.Revision}.json"), overwrite: true);
        RecipeFile.Save(path, saved);
        var head = $"{t.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}  {user}  版次 {saved.Revision}";
        File.AppendAllLines(LogPath(path), new[] { head }.Concat(changes.Select(c => "    " + c)));
        return saved;
    }

    /// <summary>讀修改紀錄（新的在前），沒有就回傳空字串。</summary>
    public static string ReadLog(string recipePath)
    {
        var p = LogPath(recipePath);
        if (!File.Exists(p)) return "";
        var blocks = new List<List<string>>();
        foreach (var line in File.ReadAllLines(p))
            if (line.StartsWith("    ") && blocks.Count > 0) blocks[^1].Add(line); else blocks.Add([line]);
        blocks.Reverse();
        return string.Join("\n", blocks.SelectMany(b => b));
    }

    /// <summary>兩版配方的差異（版次、修改人、時間不算）。</summary>
    public static List<string> Diff(ProcessRecipe a, ProcessRecipe b)
    {
        var fa = Flatten(a); var fb = Flatten(b);
        var list = new List<string>();
        foreach (var k in fa.Keys.Union(fb.Keys).OrderBy(Order))
        {
            if (k is "revision" or "modifiedBy" or "modifiedAt") continue;
            fa.TryGetValue(k, out var va); fb.TryGetValue(k, out var vb);
            if (va != vb) list.Add($"{Label(k)}：{va ?? "（無）"} → {vb ?? "（無）"}");
        }
        return list;
    }

    private static Dictionary<string, string> Flatten(ProcessRecipe r)
    {
        var d = new Dictionary<string, string>();
        void Walk(JsonNode? n, string path)
        {
            switch (n)
            {
                case JsonObject o: foreach (var (k, v) in o) Walk(v, path.Length == 0 ? k : $"{path}.{k}"); break;
                case JsonArray a: for (int i = 0; i < a.Count; i++) Walk(a[i], $"{path}[{i}]"); break;
                case null: d[path] = "空白"; break;
                default: d[path] = n.ToJsonString(); break;
            }
        }
        Walk(JsonNode.Parse(RecipeFile.ToJson(r)), "");
        return d;
    }

    /// <summary>依路徑排序，但陣列索引用數字排（steps[10] 排在 steps[9] 後面）。</summary>
    private static string Order(string k) => System.Text.RegularExpressions.Regex.Replace(k, @"\d+", m => m.Value.PadLeft(5, '0'));

    /// <summary>JSON 路徑 → 看得懂的名稱，例如 segments[0].steps[1].durationMs → 第 1 段 第 2 步 時間 ms。</summary>
    public static string Label(string path)
    {
        var s = System.Text.RegularExpressions.Regex.Replace(path, @"segments\[(\d+)\]", m => $"第 {int.Parse(m.Groups[1].Value) + 1} 段");
        s = System.Text.RegularExpressions.Regex.Replace(s, @"steps\[(\d+)\]", m => $"第 {int.Parse(m.Groups[1].Value) + 1} 步");
        s = System.Text.RegularExpressions.Regex.Replace(s, @"(mfc)\[(\d+)\]", m => $"MFC{int.Parse(m.Groups[2].Value) + 1}", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        s = System.Text.RegularExpressions.Regex.Replace(s, @"heaterSpDegC\[(\d+)\]", m => $"溫度 #{int.Parse(m.Groups[1].Value) + 1}");
        foreach (var (k, v) in Names) s = s.Replace(k, v);
        return s.Replace(".", " ");
    }

    private static readonly (string, string)[] Names =
    [
        ("durationMs", "時間 ms"), ("valveMask", "閥"), ("label", "名稱"), ("rf1W", "RF1 W"), ("rf2W", "RF2 W"),
        ("cycles", "Cycles"), ("o3SetPct", "O3 %"), ("name", "名稱"), ("description", "說明"),
        ("pumpDown", "先抽真空"), ("crossoverTorr", "Crossover Torr"), ("baseTorr", "Base Torr"),
        ("pumpTimeoutS", "抽氣逾時 s"), ("turboSpinUpTimeoutS", "Turbo 啟動逾時 s"),
        ("heatToleranceC", "溫度容差 °C"), ("heatSettleS", "溫度穩定 s"), ("heatTimeoutS", "等溫度逾時 s"),
        ("gasStabilizeS", "通氣穩定 s"), ("ventAfter", "結束回填"), ("ventTargetTorr", "回填目標 Torr"),
        ("ventTimeoutS", "回填逾時 s"), ("turboSpinDownTimeoutS", "Turbo 停止逾時 s"),
    ];
}
