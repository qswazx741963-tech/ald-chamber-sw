namespace Ald.Core;

/// <summary>ALD Recipe：IPC 只把步驟表下載給製程板（槽 1），時序由執行板的 1 kHz 計時器執行。</summary>
public sealed class AldRecipe
{
    public string Name { get; set; } = "";
    public int Cycles { get; set; } = 1;
    public List<AldStep> Steps { get; set; } = new();
    public double[] MfcSccm { get; set; } = new double[6];
    public double[] HeaterSpDegC { get; set; } = new double[12];

    public IEnumerable<string> Validate()
    {
        if (Cycles is < 1 or > 60000) yield return "Cycles 需在 1–60000";
        if (Steps.Count is < 1 or > 16) yield return "步驟數需在 1–16";
        foreach (var (s, i) in Steps.Select((s, i) => (s, i)))
            if (s.DurationMs is < 1 or > 60000) yield return $"第 {i + 1} 步時間需在 1–60000 ms";
    }
}

/// <param name="ValveMask">bit0 = ALD_V1 ... bit5 = ALD_V6</param>
public sealed record AldStep(string Label, ushort ValveMask, int DurationMs);
