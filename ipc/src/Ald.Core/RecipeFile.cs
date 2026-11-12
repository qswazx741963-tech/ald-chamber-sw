using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Ald.Core;

/// <summary>
/// 製程配方檔（JSON，一個檔 = 一個 ProcessRecipe）。欄位名稱用 camelCase，可以有 // 註解與 "_comment"（讀檔時忽略）。
/// 人機的 Recipe 頁只編常用欄位；逾時等進階欄位直接改 JSON，讀進來再存回去會保留。
/// </summary>
public static class RecipeFile
{
    private static readonly JsonSerializerOptions Opt = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,   // 中文直接存，不轉 \uXXXX
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>讀配方；舊格式（單一 "ald"）自動轉成一段 "segments"。</summary>
    public static ProcessRecipe Parse(string json)
    {
        var r = JsonSerializer.Deserialize<ProcessRecipe>(json, Opt) ?? throw new FormatException("配方檔是空的");
        if (r.Segments.Count == 0 && r.Ald is { } a)
        {
            if (string.IsNullOrWhiteSpace(a.Name)) a.Name = r.Name;
            r = r with { Segments = [a], Ald = null };
        }
        return r;
    }

    public static string ToJson(ProcessRecipe r) => JsonSerializer.Serialize(r, Opt);

    public static ProcessRecipe Load(string path)
    {
        try { return Parse(File.ReadAllText(path)); }
        catch (JsonException e) { throw new FormatException($"{Path.GetFileName(path)} 格式錯誤：{e.Message}", e); }
    }

    /// <summary>先寫暫存檔再取代，存到一半斷電不會把原檔弄壞。</summary>
    public static void Save(string path, ProcessRecipe r)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(dir);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, ToJson(r));
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>檔名用的安全名稱（去掉不能當檔名的字元）。</summary>
    public static string FileNameFor(string recipeName)
    {
        var n = string.Concat(recipeName.Trim().Select(c => Path.GetInvalidFileNameChars().Contains(c) || c == ' ' ? '_' : c));
        return (n.Length == 0 ? "recipe" : n) + ".json";
    }
}

/// <summary>
/// ALD 步驟表的文字格式（人機編輯用），一行一步：<c>名稱, 閥, 時間ms</c>。
/// 閥寫法：<c>V1+V3</c>、<c>-</c>（全關）或數字遮罩（<c>5</c>、<c>0x05</c>）。# 之後是註解。
/// </summary>
public static class AldStepText
{
    public static string Format(IEnumerable<AldStep> steps)
        => string.Join("\n", steps.Select(s => $"{s.Label}, {Valves(s.ValveMask)}, {s.DurationMs}"));

    public static string Valves(ushort mask)
        => mask == 0 ? "-" : string.Join("+", Enumerable.Range(0, 16).Where(b => (mask >> b & 1) != 0).Select(b => $"V{b + 1}"));

    public static List<AldStep> Parse(string text)
    {
        var list = new List<AldStep>();
        var lines = text.Replace("\r", "").Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            int hash = line.IndexOf('#');
            if (hash >= 0) line = line[..hash];
            if (string.IsNullOrWhiteSpace(line)) continue;
            var f = line.Split(',', '，').Select(x => x.Trim()).ToArray();
            if (f.Length != 3) throw new FormatException($"第 {i + 1} 行要寫成「名稱, 閥, 時間ms」");
            if (!int.TryParse(f[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var ms))
                throw new FormatException($"第 {i + 1} 行時間「{f[2]}」不是整數 ms");
            list.Add(new AldStep(f[0], ParseValves(f[1], i + 1), ms));
        }
        return list;
    }

    private static ushort ParseValves(string s, int line)
    {
        s = s.Replace(" ", "");
        if (s is "-" or "" or "0") return 0;
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            && ushort.TryParse(s[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex)) return hex;
        if (ushort.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var dec)) return dec;
        ushort mask = 0;
        foreach (var part in s.Split('+'))
        {
            var m = Regex.Match(part, @"^[Vv](\d{1,2})$");
            if (!m.Success || int.Parse(m.Groups[1].Value) is < 1 or > 16)
                throw new FormatException($"第 {line} 行閥「{part}」看不懂（寫 V1+V3、- 或數字）");
            mask |= (ushort)(1 << (int.Parse(m.Groups[1].Value) - 1));
        }
        return mask;
    }
}

/// <summary>配方的文字摘要（履歷頁、確認視窗用）。</summary>
public static class RecipeText
{
    public static string Describe(ProcessRecipe r, IReadOnlyList<string>? valveNames = null)
    {
        var sb = new System.Text.StringBuilder();
        var heat = r.HeaterSpDegC.Select((t, ch) => (t, ch)).Where(v => v.t > 0).Select(v => $"#{v.ch + 1} {v.t:0} °C");
        var gas = r.Mfc.Select((v, ch) => (v, ch)).Where(v => v.v > 0).Select(v => $"MFC{v.ch + 1} {v.v:0.#}");
        sb.AppendLine($"配方 {r.Name}{(r.Revision > 0 ? $"（版次 {r.Revision}）" : "")}");
        sb.AppendLine($"抽真空 {(r.PumpDown ? "是" : "否")}  結束回填 {(r.VentAfter ? "是" : "否")}");
        sb.AppendLine($"溫度 {(heat.Any() ? string.Join("、", heat) : "不檢查")}");
        sb.AppendLine($"起始氣體 {(gas.Any() ? string.Join("、", gas) : "無")}");
        var segs = r.AldSegments;
        if (segs.Count == 0) sb.AppendLine("無 ALD");
        for (int i = 0; i < segs.Count; i++)
        {
            var g = segs[i];
            sb.AppendLine($"ALD 第 {i + 1} 段 {g.Name}：{g.Cycles} cycles{(g.O3SetPct > 0 ? $"，O3 {g.O3SetPct:0.#} %" : "")}");
            foreach (var s in g.Steps) sb.AppendLine("  " + Step(s, valveNames));
        }
        return sb.ToString().TrimEnd();
    }

    public static string Step(AldStep s, IReadOnlyList<string>? valveNames = null)
    {
        string v = s.ValveMask == 0 ? "閥全關"
            : string.Join("+", Enumerable.Range(0, 7).Where(b => (s.ValveMask >> b & 1) != 0).Select(b => valveNames is null ? $"V{b + 1}" : ValveNames.Label(valveNames, b)));
        var extra = new List<string>();
        if (s.HasMfcChange) extra.Add("MFC " + Mfc(s.Mfc!));
        if (s.Rf1W > 0) extra.Add($"RF1 {s.Rf1W:0} W");
        if (s.Rf2W > 0) extra.Add($"RF2 {s.Rf2W:0} W");
        return $"{s.Label}, {v}, {s.DurationMs} ms" + (extra.Count > 0 ? "（" + string.Join("，", extra) + "）" : "");
    }

    /// <summary>MFC 切換摘要，例如 "2→150、5→0"。</summary>
    public static string Mfc(double?[] m)
        => string.Join("、", m.Select((v, c) => (v, c)).Where(x => x.v is not null).Select(x => $"MFC{x.c + 1}→{x.v:0.#}"));
}
