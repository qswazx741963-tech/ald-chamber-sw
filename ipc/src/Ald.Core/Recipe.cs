namespace Ald.Core;

/// <summary>
/// 一段 ALD（2026-11-12 起一個配方可以有多段，例如 Al2O3 ×50 再 TiO2 ×20）。
/// IPC 把這一段的步驟表下載給製程板（槽 1），時序由執行板的 1 kHz 計時器執行；段跟段之間由 IPC 接續。
/// </summary>
public sealed class AldRecipe
{
    public const int MaxSteps = 16;
    /// <summary>製程板一段最多幾組不同的 MFC 切換（暫存器空間限制，見 registers.yaml ALD_MFC_PRESET）。</summary>
    public const int MaxMfcPresets = 4;

    public string Name { get; set; } = "";
    public int Cycles { get; set; } = 1;
    public List<AldStep> Steps { get; set; } = new();

    /// <summary>O3 產生器設定（%，0 = 這段不用 O3）。產生器在這段開始前開好並穩定；打進腔體由 V1~V7 其中一顆閥控制。硬體待確認。</summary>
    public double O3SetPct { get; set; }

    public IEnumerable<string> Validate()
    {
        if (Cycles is < 1 or > 60000) yield return "Cycles 需在 1–60000";
        if (Steps.Count is < 1 or > MaxSteps) yield return $"步驟數需在 1–{MaxSteps}";
        foreach (var (s, i) in Steps.Select((s, i) => (s, i)))
        {
            if (s.DurationMs is < 1 or > 60000) yield return $"第 {i + 1} 步時間需在 1–60000 ms";
            if (s.ValveMask > 0x7F) yield return $"第 {i + 1} 步閥位元超出 ALD_V1–V7";
            if (s.Mfc is { } m)
            {
                if (m.Length != 8) yield return $"第 {i + 1} 步 MFC 需 8 組";
                else if (m.Any(v => v is < 0)) yield return $"第 {i + 1} 步 MFC 不能是負數";
            }
            if (s.Rf1W < 0 || s.Rf2W < 0) yield return $"第 {i + 1} 步 RF 功率不能是負數";
        }
        if (MfcPresets().Count > MaxMfcPresets)
            yield return $"MFC 切換的組合最多 {MaxMfcPresets} 種（相同的切換設定算同一種）";
        if (O3SetPct is < 0 or > 100) yield return "O3 設定需在 0–100 %";
    }

    public double CycleSeconds => Steps.Sum(s => s.DurationMs) / 1000.0;
    public double TotalSeconds => CycleSeconds * Cycles;
    public bool UsesRf => Steps.Any(s => s.Rf1W > 0 || s.Rf2W > 0);
    public bool UsesMfcSwitch => Steps.Any(s => s.HasMfcChange);

    /// <summary>不重複的 MFC 切換設定（依出現順序）。製程板第 n 組 = 這個清單第 n 個。</summary>
    public List<double?[]> MfcPresets()
    {
        var list = new List<double?[]>();
        foreach (var s in Steps.Where(s => s.HasMfcChange))
            if (!list.Any(p => p.SequenceEqual(s.Mfc!))) list.Add(s.Mfc!);
        return list;
    }

    /// <summary>每一步用第幾組（0 = 不變）。</summary>
    public int[] MfcPresetIndex()
    {
        var presets = MfcPresets();
        return Steps.Select(s => s.HasMfcChange ? presets.FindIndex(p => p.SequenceEqual(s.Mfc!)) + 1 : 0).ToArray();
    }

    /// <summary>這段跑完後 MFC 的設定值（start = 段開始時的設定）。切換值是絕對值，所以重複幾個 cycle 結果都一樣。</summary>
    public double[] MfcAfter(double[] start)
    {
        var cur = (double[])start.Clone();
        foreach (var s in Steps.Where(s => s.HasMfcChange))
            for (int c = 0; c < 8 && c < s.Mfc!.Length; c++)
                if (s.Mfc[c] is { } v) cur[c] = v;
        return cur;
    }
}

/// <param name="ValveMask">bit0 = ALD_V1 ... bit6 = ALD_V7</param>
public sealed record AldStep(string Label, ushort ValveMask, int DurationMs)
{
    /// <summary>這一步開始時切換 MFC（config 單位）：null 或某通道 null = 沿用上一步。</summary>
    public double?[]? Mfc { get; init; }
    /// <summary>RF1 / RF2 功率 W，0 = 關。</summary>
    public double Rf1W { get; init; }
    public double Rf2W { get; init; }

    public bool HasMfcChange => Mfc is { } m && m.Any(v => v is not null);

    public bool Equals(AldStep? o) => o is not null && Label == o.Label && ValveMask == o.ValveMask && DurationMs == o.DurationMs
        && Rf1W == o.Rf1W && Rf2W == o.Rf2W && (Mfc ?? []).SequenceEqual(o.Mfc ?? []);
    public override int GetHashCode() => HashCode.Combine(Label, ValveMask, DurationMs, Rf1W, Rf2W);
}
