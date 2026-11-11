namespace Ald.Core;

/// <summary>真空計型號，編號 = 主控板 HR PM_GAUGE_MODEL（firmware/common/gauge.h），config 的型號選單用這個。</summary>
public enum GaugeModel
{
    None = 0,
    /// <summary>電容式 0~10 V 線性（Edwards Barocel 7000 系列等），需設定滿刻度。</summary>
    Linear = 1,
    /// <summary>Edwards APGX-H 對流式，P(Torr) = 10^(V − 6.125)。</summary>
    ApgxH = 2,
    /// <summary>Edwards WRG200 標準輸出 2~10 V，P(Torr) = 10^((V − 8.083) / 0.667)。</summary>
    Wrg200X = 3,
    /// <summary>Edwards WRG200 matched 1.4~8.6 V，P(Torr) = 10^((V − 6.875) / 0.6)。</summary>
    Wrg200G = 4,
    /// <summary>Edwards WRG200 matched 1.5~6.9375 V，P(Torr) = 10^((V − 5.5) / 0.5)。</summary>
    Wrg200Q = 5,
}

/// <summary>編號 = 主控板 IR PM_GAUGE_STATUS。</summary>
public enum GaugeStatus { Ok = 0, UnderRange = 1, OverRange = 2, Fault = 3, Offline = 4, NotConfigured = 5 }

/// <summary>
/// 壓力計電壓 → 壓力。公式與故障電壓來自原廠手冊（見 docs/gauges.md），跟 firmware/common/gauge.c 同一張表，改一邊要改另一邊。
/// </summary>
public static class Gauges
{
    private sealed record LogGauge(double A, double B, double VLo, double VHi, double FaultLo, double FaultHi);

    private static readonly Dictionary<GaugeModel, LogGauge> Log = new()
    {
        [GaugeModel.ApgxH] = new(6.125, 1.000, 1.90, 9.125, 1.80, 9.20),
        [GaugeModel.Wrg200X] = new(8.083, 0.667, 2.00, 10.00, 1.50, 10.60),
        [GaugeModel.Wrg200G] = new(6.875, 0.600, 1.40, 8.60, 1.00, 9.50),
        [GaugeModel.Wrg200Q] = new(5.500, 0.500, 1.50, 6.9375, 1.40, 7.50),
    };

    /// <summary>線性型接近 10 V 視為飽和（可能是大氣）。</summary>
    public const double LinearSaturationV = 9.90;

    /// <summary>給畫面下拉選單用的顯示名稱。</summary>
    public static string DisplayName(GaugeModel m) => m switch
    {
        GaugeModel.None => "未使用",
        GaugeModel.Linear => "電容式 0–10 V 線性（Barocel 7000 等）",
        GaugeModel.ApgxH => "Edwards APGX-H 對流式",
        GaugeModel.Wrg200X => "Edwards WRG200（標準 2–10 V）",
        GaugeModel.Wrg200G => "Edwards WRG200（matched 1.4–8.6 V）",
        GaugeModel.Wrg200Q => "Edwards WRG200（matched 1.5–6.94 V）",
        _ => m.ToString(),
    };

    /// <summary>mv：壓力計輸出（mV）；fullScaleTorr 只有 Linear 用。Torr 在 Ok / UnderRange / OverRange 時有效。</summary>
    public static (GaugeStatus Status, double Torr) Convert(GaugeModel model, double mv, double fullScaleTorr = 0)
    {
        double v = mv / 1000.0;
        if (model == GaugeModel.Linear)
        {
            if (!(fullScaleTorr > 0)) return (GaugeStatus.NotConfigured, double.NaN);
            return (v >= LinearSaturationV ? GaugeStatus.OverRange : GaugeStatus.Ok, v / 10.0 * fullScaleTorr);
        }
        if (!Log.TryGetValue(model, out var g)) return (GaugeStatus.NotConfigured, double.NaN);
        if (v < g.FaultLo || v > g.FaultHi) return (GaugeStatus.Fault, double.NaN);
        var st = GaugeStatus.Ok;
        if (v < g.VLo) { v = g.VLo; st = GaugeStatus.UnderRange; }
        if (v > g.VHi) { v = g.VHi; st = GaugeStatus.OverRange; }
        return (st, Math.Pow(10, (v - g.A) / g.B));
    }
}
