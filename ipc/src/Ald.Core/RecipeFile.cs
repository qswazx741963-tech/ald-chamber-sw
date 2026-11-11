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
    };

    public static ProcessRecipe Parse(string json)
        => JsonSerializer.Deserialize<ProcessRecipe>(json, Opt) ?? throw new FormatException("配方檔是空的");

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
