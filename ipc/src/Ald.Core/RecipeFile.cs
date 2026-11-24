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
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>讀配方；舊格式（2026-11-24 以前沒有頂層 "steps"）自動轉成步驟清單。</summary>
    public static ProcessRecipe Parse(string json)
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })
                   as System.Text.Json.Nodes.JsonObject ?? throw new FormatException("配方檔是空的");
        bool isNew = node.Any(kv => string.Equals(kv.Key, "steps", StringComparison.OrdinalIgnoreCase));
        if (isNew) return JsonSerializer.Deserialize<ProcessRecipe>(json, Opt) ?? throw new FormatException("配方檔是空的");
        var o = JsonSerializer.Deserialize<LegacyRecipe>(json, Opt) ?? throw new FormatException("配方檔是空的");
        return o.Convert();
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
        sb.AppendLine($"配方 {r.Name}{(r.Revision > 0 ? $"（版次 {r.Revision}）" : "")}");
        for (int i = 0; i < r.Steps.Count; i++)
        {
            var st = r.Steps[i];
            sb.AppendLine($"Step {i + 1} {st.Name}：{StepSummary(st)}");
            if (st.Ald is { } g && st.Kind == StepKind.Ald)
                foreach (var s in g.Steps) sb.AppendLine("  " + Step(s, valveNames));
        }
        if (r.Steps.Count == 0) sb.AppendLine("（沒有步驟）");
        return sb.ToString().TrimEnd();
    }

    /// <summary>一個步驟的一行摘要。</summary>
    public static string StepSummary(ProcessStep st)
    {
        string mfc = st.Mfc is { } m && m.Any(v => v is not null) ? "，" + Mfc(m) : "";
        return st.Kind switch
        {
            StepKind.PumpDown => $"抽真空到 {st.Pump?.BaseTorr:G3} Torr（最多 {st.TimeS:0} s）",
            StepKind.HeatCheck => "確認溫度 " + string.Join("、", (st.Heat?.HeaterSpDegC ?? []).Select((t, ch) => (t, ch)).Where(v => v.t > 0).Select(v => $"#{v.ch + 1} {v.t:0} °C")),
            StepKind.Gas => $"通氣 {st.TimeS:0.#} s{mfc}",
            StepKind.Ald => $"ALD {st.Ald?.Cycles} cycles{(st.Ald?.O3SetPct > 0 ? $"，O3 {st.Ald.O3SetPct:0.#} %" : "")}{mfc}",
            StepKind.Vent => $"回填到 {st.Vent?.TargetTorr:0} Torr",
            _ => "",
        };
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

/// <summary>
/// 2026-11-24 以前的配方格式：固定流程 抽真空 → 確認溫度 → 通氣 → ALD 各段 → 回填。
/// 只用來讀舊檔（RecipeFile.Parse 自動轉換）與測試，存檔一律是新格式（步驟清單）。
/// </summary>
public sealed record LegacyRecipe
{
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public int Revision { get; init; }
    public string ModifiedBy { get; init; } = "";
    public DateTime? ModifiedAt { get; init; }
    public bool PumpDown { get; init; } = true;
    public double CrossoverTorr { get; init; } = 0.5;
    public double BaseTorr { get; init; } = 5e-3;
    public double PumpTimeoutS { get; init; } = 1800;
    public double TurboSpinUpTimeoutS { get; init; } = 900;
    public double[] HeaterSpDegC { get; init; } = new double[16];
    public double HeatToleranceC { get; init; } = 3;
    public double HeatSettleS { get; init; } = 60;
    public double HeatTimeoutS { get; init; } = 3600;
    public double[] Mfc { get; init; } = new double[8];
    public double GasStabilizeS { get; init; } = 10;
    public List<AldRecipe> Segments { get; init; } = new();
    public AldRecipe? Ald { get; init; }
    public bool VentAfter { get; init; }
    public double VentTargetTorr { get; init; } = 740;
    public double VentTimeoutS { get; init; } = 600;
    public double TurboSpinDownTimeoutS { get; init; } = 1200;

    public static implicit operator ProcessRecipe(LegacyRecipe l) => l.Convert();

    public ProcessRecipe Convert()
    {
        var steps = new List<ProcessStep>();
        if (PumpDown)
            steps.Add(ProcessStep.NewPumpDown(new PumpParams { BaseTorr = BaseTorr, CrossoverTorr = CrossoverTorr, TurboSpinUpTimeoutS = TurboSpinUpTimeoutS }, PumpTimeoutS));
        if (HeaterSpDegC.Any(t => t > 0))
            steps.Add(ProcessStep.NewHeatCheck(HeaterSpDegC, HeatToleranceC, HeatSettleS, HeatTimeoutS));
        var segs = Segments.Count > 0 ? Segments : Ald is null ? [] : [Ald];
        bool gasUsed = Mfc.Any(v => v > 0) || segs.Any(g => g.Steps.Any(s => s.HasMfcChange));
        if (gasUsed)
            steps.Add(ProcessStep.NewGas(Mfc.Select(v => (double?)v).ToArray(), GasStabilizeS, "通氣穩定"));
        for (int i = 0; i < segs.Count; i++)
        {
            var g = segs[i];
            if (string.IsNullOrWhiteSpace(g.Name)) g.Name = segs.Count > 1 ? $"ALD {i + 1}" : (Name.Length > 0 ? Name : "ALD");
            steps.Add(ProcessStep.NewAld(g));
        }
        if (VentAfter)
            steps.Add(ProcessStep.NewVent(new VentParams { TargetTorr = VentTargetTorr, TurboSpinDownTimeoutS = TurboSpinDownTimeoutS }, VentTimeoutS));
        return new ProcessRecipe { Name = Name, Description = Description, Revision = Revision, ModifiedBy = ModifiedBy, ModifiedAt = ModifiedAt, Steps = steps };
    }
}
