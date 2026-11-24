using System.Text.Json.Serialization;

namespace Ald.Core;

/// <summary>
/// 整段製程配方（2026-11-24 改版）：一串「步驟」（Step 1、Step 2 …）依序執行，人機的 Recipe 頁每個步驟一欄。
/// 步驟種類（= 結束條件）：抽真空（到 base pressure）、確認溫度（到溫）、通氣（依時間）、ALD（依 cycle 數）、回填（到大氣）。
/// 加熱是常駐設定（加熱頁設好就不動），「確認溫度」只檢查、不改加熱設定，製程中也不升降溫。
/// 時間單位秒、壓力 Torr、溫度 °C。舊格式（2026-11-24 以前：pumpDown / heaterSpDegC / mfc / segments / ventAfter）讀檔時自動轉換。
/// </summary>
public sealed record ProcessRecipe
{
    public string Name { get; init; } = "";
    /// <summary>說明（膜種、用途等），選填。</summary>
    public string Description { get; init; } = "";
    /// <summary>版次：每次在人機存檔 +1；ModifiedBy / ModifiedAt 記最後修改人與時間（修改內容另記在配方資料夾 .history/）。</summary>
    public int Revision { get; init; }
    public string ModifiedBy { get; init; } = "";
    public DateTime? ModifiedAt { get; init; }

    /// <summary>步驟，依序執行。</summary>
    public List<ProcessStep> Steps { get; init; } = new();

    public const int MaxSteps = 40;

    // ------------------------------------------------------------ 常用的單一動作配方
    public static ProcessRecipe Of(string name, params ProcessStep[] steps) => new() { Name = name, Steps = steps.ToList() };
    public static ProcessRecipe PumpOnly(string name, PumpParams? p = null, double timeoutS = 1800)
        => Of(name, ProcessStep.NewPumpDown(p, timeoutS));
    public static ProcessRecipe VentOnly(string name, VentParams? p = null, double timeoutS = 600)
        => Of(name, ProcessStep.NewVent(p, timeoutS));

    // ------------------------------------------------------------ 查詢
    [JsonIgnore] public IEnumerable<AldRecipe> AldSegments => Steps.Where(s => s.Kind == StepKind.Ald && s.Ald is not null).Select(s => s.Ald!);
    /// <summary>ALD 總時間（秒，不含抽真空 / 等溫度 / 回填）。</summary>
    [JsonIgnore] public double AldSeconds => AldSegments.Sum(g => g.TotalSeconds);
    [JsonIgnore] public bool PumpsFirst => Steps.Count > 0 && Steps[0].Kind == StepKind.PumpDown;
    [JsonIgnore] public bool VentsLast => Steps.Count > 0 && Steps[^1].Kind == StepKind.Vent;
    /// <summary>開頭連續幾步是抽真空（自動跑片在送片前做）。</summary>
    [JsonIgnore] public int LeadingPumpSteps => Steps.TakeWhile(s => s.Kind == StepKind.PumpDown).Count();
    /// <summary>結尾連續幾步是回填（自動跑片在取片後做）。</summary>
    [JsonIgnore] public int TrailingVentSteps => Steps.AsEnumerable().Reverse().TakeWhile(s => s.Kind == StepKind.Vent).Count();

    /// <summary>取其中幾步組成新配方（名稱加後綴）。</summary>
    public ProcessRecipe Slice(int start, int count, string suffix = "")
        => this with { Name = Name + suffix, Steps = Steps.Skip(start).Take(count).ToList() };

    /// <summary>MFC 通道 c 在整個製程中有沒有用到（任何一步設定 &gt; 0，含 ALD 子步驟切換）：用到的通道開上游閥。</summary>
    public bool MfcUsed(int c) => Steps.Any(s =>
        s.Mfc is { } m && c < m.Length && m[c] > 0
        || s.Ald is { } a && a.Steps.Any(x => x.Mfc is { } sm && c < sm.Length && sm[c] > 0));

    /// <summary>每一步開始時（套用這步 MFC 設定後）的 MFC 設定值；ALD 子步驟切換後的值帶到下一步。</summary>
    public List<double[]> MfcAtSteps()
    {
        var cur = new double[8];
        var list = new List<double[]>();
        foreach (var s in Steps)
        {
            if (s.Kind is StepKind.Vent) cur = new double[8];
            if (s.UsesMfc && s.Mfc is { } m)
                for (int c = 0; c < 8 && c < m.Length; c++) if (m[c] is { } v) cur[c] = v;
            list.Add((double[])cur.Clone());
            if (s.Kind == StepKind.Ald && s.Ald is { } a) cur = a.MfcAfter(cur);
        }
        return list;
    }

    public IEnumerable<string> Validate()
    {
        if (Steps.Count == 0) yield return "配方沒有任何步驟";
        if (Steps.Count > MaxSteps) yield return $"步驟最多 {MaxSteps} 個";
        for (int i = 0; i < Steps.Count; i++)
            foreach (var e in Steps[i].Validate()) yield return $"Step {i + 1} {Steps[i].Name}：{e}";
    }
}

/// <summary>步驟種類，同時就是結束條件。</summary>
public enum StepKind
{
    /// <summary>抽真空：Dry pump → 粗抽 → Turbo → 開 GV，到 base pressure 結束。</summary>
    PumpDown,
    /// <summary>確認溫度：要求的加熱組都在容差內並維持穩定時間（不改加熱設定）。</summary>
    HeatCheck,
    /// <summary>通氣：設定 MFC，等固定時間（通氣穩定、Purge）。</summary>
    Gas,
    /// <summary>ALD：子步驟閥序重複 N 個 cycle（製程板 1 kHz 執行）。</summary>
    Ald,
    /// <summary>回填：關 GV、停 Turbo、Vent 到大氣。</summary>
    Vent,
}

public sealed record ProcessStep
{
    public string Name { get; init; } = "";
    public StepKind Kind { get; init; }
    /// <summary>通氣：這步的時間。抽真空 / 確認溫度 / 回填：最大時間（逾時就失敗）。ALD：最大時間，0 = 自動（預估 ×1.2 + 30 s）。</summary>
    public double TimeS { get; init; }
    /// <summary>這步開始時設定的 MFC（config 單位）。null 或某通道 null = 沿用上一步。只有通氣、ALD 用。</summary>
    public double?[]? Mfc { get; init; }
    public PumpParams? Pump { get; init; }
    public HeatParams? Heat { get; init; }
    public AldRecipe? Ald { get; init; }
    public VentParams? Vent { get; init; }

    [JsonIgnore] public bool UsesMfc => Kind is StepKind.Gas or StepKind.Ald;

    public static ProcessStep NewPumpDown(PumpParams? p = null, double timeoutS = 1800, string name = "抽真空")
        => new() { Name = name, Kind = StepKind.PumpDown, TimeS = timeoutS, Pump = p ?? new PumpParams() };
    public static ProcessStep NewHeatCheck(double[] sp, double tol = 3, double settleS = 60, double timeoutS = 3600, string name = "確認溫度")
        => new() { Name = name, Kind = StepKind.HeatCheck, TimeS = timeoutS, Heat = new HeatParams { HeaterSpDegC = sp, ToleranceC = tol, SettleS = settleS } };
    public static ProcessStep NewGas(double?[]? mfc, double timeS, string name = "通氣")
        => new() { Name = name, Kind = StepKind.Gas, TimeS = timeS, Mfc = mfc };
    public static ProcessStep NewAld(AldRecipe ald, double?[]? mfc = null, string? name = null)
        => new() { Name = name ?? ald.Name, Kind = StepKind.Ald, Ald = ald, Mfc = mfc };
    public static ProcessStep NewVent(VentParams? p = null, double timeoutS = 600, string name = "回填")
        => new() { Name = name, Kind = StepKind.Vent, TimeS = timeoutS, Vent = p ?? new VentParams() };

    /// <summary>預估時間（秒）；抽真空 / 等溫度 / 回填看實際狀況，回傳 null。</summary>
    [JsonIgnore]
    public double? EstimatedS => Kind switch
    {
        StepKind.Gas => TimeS,
        StepKind.Ald => Ald?.TotalSeconds,
        _ => null,
    };

    public IEnumerable<string> Validate()
    {
        if (Mfc is { } m)
        {
            if (!UsesMfc && m.Any(v => v is not null)) yield return "這種步驟不能設定 MFC";
            if (m.Length != 8) yield return "MFC 需 8 組";
            else if (m.Any(v => v is < 0)) yield return "MFC 不能是負數";
        }
        switch (Kind)
        {
            case StepKind.PumpDown:
                if (Pump is not { } p) { yield return "缺少抽真空設定"; break; }
                if (!(p.BaseTorr > 0) || !(p.CrossoverTorr > p.BaseTorr)) yield return "壓力設定錯誤（需 Crossover > Base > 0）";
                if (TimeS is <= 0 or > 36000) yield return "最大時間需在 0–36000 s";
                break;
            case StepKind.HeatCheck:
                if (Heat is not { } h) { yield return "缺少溫度設定"; break; }
                if (h.HeaterSpDegC.Length != 16) yield return "溫度需 16 組";
                else if (!h.HeaterSpDegC.Any(t => t > 0)) yield return "沒有選要確認的加熱組";
                if (h.ToleranceC is <= 0 or > 50) yield return "容差需在 0–50 °C";
                if (h.SettleS is < 0 or > 36000) yield return "穩定時間需在 0–36000 s";
                if (TimeS is <= 0 or > 36000) yield return "最大時間需在 0–36000 s";
                break;
            case StepKind.Gas:
                if (TimeS is < 0 or > 36000) yield return "時間需在 0–36000 s";
                break;
            case StepKind.Ald:
                if (Ald is not { } a) { yield return "缺少 ALD 子步驟"; break; }
                foreach (var e in a.Validate()) yield return e;
                if (TimeS is < 0 or > 864000) yield return "最大時間需在 0–864000 s（0 = 自動）";
                else if (TimeS > 0 && TimeS < a.TotalSeconds) yield return $"最大時間 {TimeS:0} s 比預估 {a.TotalSeconds:0} s 短";
                break;
            case StepKind.Vent:
                if (Vent is not { } v) { yield return "缺少回填設定"; break; }
                if (v.TargetTorr is < 100 or > 800) yield return "回填目標需在 100–800 Torr";
                if (TimeS is <= 0 or > 36000) yield return "最大時間需在 0–36000 s";
                break;
        }
    }
}

public sealed record PumpParams
{
    /// <summary>Turbo 抽到這個壓力結束這步。</summary>
    public double BaseTorr { get; init; } = 5e-3;
    /// <summary>粗抽到這個壓力後切 Turbo。</summary>
    public double CrossoverTorr { get; init; } = 0.5;
    public double TurboSpinUpTimeoutS { get; init; } = 900;
}

public sealed record HeatParams
{
    /// <summary>16 組要的溫度（0 = 不檢查這組）。</summary>
    public double[] HeaterSpDegC { get; init; } = new double[16];
    public double ToleranceC { get; init; } = 3;
    /// <summary>開始時不在範圍內，就要進範圍後維持這麼久才算到溫。</summary>
    public double SettleS { get; init; } = 60;
}

public sealed record VentParams
{
    public double TargetTorr { get; init; } = 740;
    public double TurboSpinDownTimeoutS { get; init; } = 1200;
}
