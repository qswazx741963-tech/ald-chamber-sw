namespace Ald.Core;

/// <summary>ALD Recipe：IPC 只把步驟表下載給製程板（槽 1），時序由執行板的 1 kHz 計時器執行。</summary>
public sealed class AldRecipe
{
    public string Name { get; set; } = "";
    public int Cycles { get; set; } = 1;
    public List<AldStep> Steps { get; set; } = new();
    /// <summary>MFC ×7 + EL-Press ×1（通道 8）；換成 % 滿刻度下給製程板時用 config 每台的滿刻度。</summary>
    public double[] MfcSccm { get; set; } = new double[8];
    /// <summary>16 組加熱（14 用 + 2 預留），K 型熱電偶。</summary>
    public double[] HeaterSpDegC { get; set; } = new double[16];

    public IEnumerable<string> Validate()
    {
        if (Cycles is < 1 or > 60000) yield return "Cycles 需在 1–60000";
        if (Steps.Count is < 1 or > 16) yield return "步驟數需在 1–16";
        foreach (var (s, i) in Steps.Select((s, i) => (s, i)))
            if (s.DurationMs is < 1 or > 60000) yield return $"第 {i + 1} 步時間需在 1–60000 ms";
        foreach (var (s, i) in Steps.Select((s, i) => (s, i)))
            if (s.ValveMask > 0x7F) yield return $"第 {i + 1} 步閥位元超出 ALD_V1–V7";
        if (MfcSccm.Length != 8) yield return "MFC 設定需 8 組";
        if (HeaterSpDegC.Length != 16) yield return "加熱設定需 16 組";
    }
}

/// <param name="ValveMask">bit0 = ALD_V1 ... bit6 = ALD_V7</param>
public sealed record AldStep(string Label, ushort ValveMask, int DurationMs);
