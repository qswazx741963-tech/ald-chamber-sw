using Ald.Data;
using Avalonia.Controls;
using Avalonia.Threading;
using ScottPlot;
using ScottPlot.Avalonia;

namespace Ald.Hmi;

/// <summary>
/// 趨勢頁：讀 SQLite trend 表畫圖。上圖壓力（對數），下圖溫度 / MFC。
/// 「即時更新」每 2 秒重畫（會重設縮放）；關掉即時更新就可以用滑鼠拖曳 / 滾輪看細節。
/// 查詢在背景執行緒做，24 小時的資料最多抽樣到 4000 點，不會卡畫面。
/// </summary>
public partial class MainWindow
{
    private sealed record Signal(string Column, string Label, bool Pressure, CheckBox Box);
    private readonly List<Signal> _signals = new();
    private static readonly (string Text, TimeSpan Span)[] TrendRanges =
    {
        ("最近 10 分鐘", TimeSpan.FromMinutes(10)), ("最近 1 小時", TimeSpan.FromHours(1)),
        ("最近 8 小時", TimeSpan.FromHours(8)), ("最近 24 小時", TimeSpan.FromHours(24)), ("最近 7 天", TimeSpan.FromDays(7)),
    };
    private DateTime _trendLastDraw = DateTime.MinValue;
    private bool _trendBusy;

    private void WireTrend()
    {
        var panel = C<StackPanel>("PnlTrendSignals");
        void Group(string title) => panel.Children.Add(new TextBlock { Text = title, FontWeight = Avalonia.Media.FontWeight.Bold, Margin = new(0, 8, 0, 2) });
        void Add(string col, string label, bool pressure, bool on)
        {
            var cb = new CheckBox { Content = label.Replace("_", "__"), IsChecked = on, Padding = new(4, 0) };   // "_" 會被當成快捷鍵
            cb.Click += (_, _) => DrawTrend();
            panel.Children.Add(cb);
            _signals.Add(new Signal(col, label, pressure, cb));
        }

        Group("壓力（Torr，對數）");
        Add("pm_torr", "PM 互鎖（APGX）", true, true);
        Add("manometer_torr", "Barocel", true, false);
        Add("ion_torr", "Ion gauge", true, true);
        Add("foreline_torr", "Foreline", true, true);
        Add("ll_torr", "Loadlock", true, false);
        Group("溫度（°C）");
        var heaters = _m?.Config.Heaters.Channels;
        for (int i = 0; i < 16; i++) Add($"t{i + 1:00}", heaters is null ? $"T{i + 1}" : $"{i + 1} {heaters[i].Name}", false, i == 6);
        Group("MFC（config 單位）");
        var mfc = _m?.Config.Mfc.Channels;
        for (int i = 0; i < 8; i++) Add($"mfc{i + 1}", mfc is null ? $"MFC{i + 1}" : $"{i + 1} {mfc[i].Name}", false, i == 1);

        var cmb = C<ComboBox>("CmbTrendRange");
        cmb.ItemsSource = TrendRanges.Select(r => r.Text).ToList();
        cmb.SelectedIndex = 0;
        cmb.SelectionChanged += (_, _) => DrawTrend();
        C<Button>("BtnTrendRefresh").Click += (_, _) => DrawTrend();
        C<Button>("BtnTrendCsv").Click += (_, _) => ExportTrend();

        foreach (var name in new[] { "PlotPressure", "PlotTemp" })
        {
            var p = C<AvaPlot>(name).Plot;
            p.Font.Automatic();   // 中文標籤用系統字型
            p.Axes.DateTimeTicksBottom();
        }
    }

    private (DateTime From, DateTime To) TrendWindow()
    {
        var span = TrendRanges[Math.Max(0, C<ComboBox>("CmbTrendRange").SelectedIndex)].Span;
        var to = DateTime.Now;
        return (to - span, to);
    }

    private void RenderTrend()
    {
        if (C<CheckBox>("ChkTrendLive").IsChecked == true && C<AvaPlot>("PlotPressure").IsEffectivelyVisible
            && DateTime.Now - _trendLastDraw > TimeSpan.FromSeconds(2))
            DrawTrend();
    }

    private async void DrawTrend()
    {
        if (_trend is null || _trendBusy) return;
        _trendBusy = true;
        _trendLastDraw = DateTime.Now;
        try
        {
            var sel = _signals.Where(s => s.Box.IsChecked == true).ToList();
            var (from, to) = TrendWindow();
            var (time, data) = await Task.Run(() => _trend.QueryMany(sel.Select(s => s.Column).ToList(), from, to));
            var xs = time.Select(t => t.ToOADate()).ToArray();
            Draw(C<AvaPlot>("PlotPressure"), sel.Where(s => s.Pressure), xs, data, log: true, from, to);
            Draw(C<AvaPlot>("PlotTemp"), sel.Where(s => !s.Pressure), xs, data, log: false, from, to);
            C<TextBlock>("TxtTrendInfo").Text = $"{time.Length} 點  {from:HH:mm:ss} – {to:HH:mm:ss}";
        }
        catch (Exception e) { C<TextBlock>("TxtTrendInfo").Text = $"讀取失敗：{e.Message}"; }
        finally { _trendBusy = false; }
    }

    private static void Draw(AvaPlot view, IEnumerable<Signal> signals, double[] xs, Dictionary<string, double[]> data, bool log, DateTime from, DateTime to)
    {
        var plot = view.Plot;
        plot.Clear();
        int n = 0;
        foreach (var s in signals)
        {
            var ys = data[s.Column];
            // 斷線（NaN）與對數圖的 ≤ 0 不畫
            var keep = Enumerable.Range(0, xs.Length).Where(i => !double.IsNaN(ys[i]) && (!log || ys[i] > 0)).ToArray();
            if (keep.Length == 0) continue;
            var sc = plot.Add.Scatter(keep.Select(i => xs[i]).ToArray(), keep.Select(i => log ? Math.Log10(ys[i]) : ys[i]).ToArray());
            sc.MarkerSize = 0;
            sc.LineWidth = 2;
            sc.LegendText = s.Label;
            n++;
        }
        var x = plot.Axes.DateTimeTicksBottom();
        if (x.TickGenerator is ScottPlot.TickGenerators.DateTimeAutomatic dt)
            dt.LabelFormatter = d => (to - from).TotalDays > 1.01 ? d.ToString("MM-dd HH:mm") : d.ToString("HH:mm:ss");
        if (log)
        {
            plot.Axes.Left.TickGenerator = new ScottPlot.TickGenerators.NumericAutomatic
            {
                LabelFormatter = y => $"1E{y:0}",
                IntegerTicksOnly = true,
                MinorTickGenerator = new ScottPlot.TickGenerators.LogMinorTickGenerator(),
            };
            plot.YLabel("Torr");
        }
        else plot.YLabel("°C / sccm");   // 軸標題用英文：ScottPlot 旋轉文字不會自動換中文字型
        plot.Axes.AutoScale();
        plot.Axes.SetLimitsX(from.ToOADate(), to.ToOADate());
        if (n > 0) plot.ShowLegend(Alignment.UpperLeft); else plot.HideLegend();
        view.Refresh();
    }

    private async void ExportTrend()
    {
        if (_trend is null || _m is null) return;
        var (from, to) = TrendWindow();
        var dir = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(_m.Config.DataLog.Path)) ?? ".", "export");
        var path = Path.Combine(dir, $"trend_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
        try
        {
            int n = await Task.Run(() => _trend.ExportCsv(path, from, to));
            _status = $"趨勢匯出 {n} 筆：{path}";
            _alarmLog?.Add(OpSource, "Info", $"匯出趨勢 {Path.GetFileName(path)}", evt: "OPERATOR");
        }
        catch (Exception e) { _status = $"匯出失敗：{e.Message}"; }
    }
}
