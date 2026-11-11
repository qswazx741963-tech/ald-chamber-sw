using Ald.Core;
using Ald.Data;
using Avalonia.Controls;
using ScottPlot.Avalonia;

namespace Ald.Hmi;

/// <summary>
/// 保養頁：漏率測試（LeakTestSequencer，壓升法）與保養計數（MaintenanceStore）。
/// 漏率測試跟配方 / 傳片互斥，需要工程師權限；計數歸零、改上限也要工程師，都寫進事件記錄。
/// </summary>
public partial class MainWindow
{
    private readonly MaintenanceStore? _maint;
    private LeakTestSequencer? _leak;
    private readonly List<string> _leakLog = new();
    private DateTime _leakPlotAt = DateTime.MinValue;
    private int _leakPlotN = -1;
    private DateTime _maintShownAt = DateTime.MinValue;
    private string? _maintSel;
    private int _leakHistShown = -1;

    private bool LeakRunning => _leak?.State == SequencerState.Running;

    private void WireMaint()
    {
        var plot = C<AvaPlot>("PlotLeak").Plot;
        plot.Font.Automatic();
        plot.XLabel("s");
        plot.YLabel("Torr");
        if (_m is null) return;
        var cfg = _m.Config.LeakTest;
        C<NumericUpDown>("NumLeakMeasure").Value = (decimal)cfg.MeasureS;
        C<NumericUpDown>("NumLeakLimit").Value = (decimal)cfg.LimitMtorrPerMin;
        C<TextBlock>("TxtLeakCfg").Text =
            $"壓力計 {GaugeName(cfg.Gauge)}、腔體體積 {cfg.ChamberVolumeL:0.#} L。抽到 {cfg.StartBelowTorr:0.0E+00} Torr 以下 → 再抽 {cfg.PumpS:0} s → " +
            $"關 Turbo GV → 等 {cfg.SettleS:0} s → 記錄壓升。量到的是真漏 + 放氣，比較時溫度與抽氣時間要一樣。";

        C<Button>("BtnLeakStart").Click += (_, _) => RunLeakTest();
        C<Button>("BtnLeakAbort").Click += (_, _) => _leak?.Abort();

        var lst = C<ListBox>("LstMaint");
        lst.SelectionChanged += (_, _) =>
        {
            if (lst.SelectedIndex < 0 || _maint is null) return;
            var row = _maint.Rows()[lst.SelectedIndex];
            _maintSel = row.Item.Id;
            C<NumericUpDown>("NumMaintLimit").Value = (decimal)row.Limit;
        };
        C<Button>("BtnMaintLimit").Click += (_, _) => MaintOp(r =>
        {
            double v = (double)(C<NumericUpDown>("NumMaintLimit").Value ?? 0);
            _maint!.SetLimit(r.Item.Id, v);
            return $"保養上限 {r.Item.Name} 設為 {(v > 0 ? Maintenance.Format(v, r.Item.Unit) : "不提醒")}";
        });
        C<Button>("BtnMaintReset").Click += (_, _) => MaintOp(r =>
        {
            _maint!.Reset(r.Item.Id, _user ?? "");
            return $"保養完成歸零：{r.Item.Name}（歸零前 {Maintenance.Format(r.Value, r.Item.Unit)}）";
        });
    }

    private void MaintOp(Func<MaintenanceStore.Row, string> act)
    {
        if (_maint is null || _maintSel is null) { _status = "請先在清單選一個保養項目"; return; }
        try
        {
            var row = _maint.Rows().First(r => r.Item.Id == _maintSel);
            _status = act(row);
            _alarmLog?.Add(OpSource, "Info", _status, evt: "OPERATOR");
            _maintShownAt = DateTime.MinValue;
        }
        catch (ArgumentException e) { _status = $"保養設定失敗：{e.Message}"; }
    }

    private async void RunLeakTest()
    {
        if (_m is null || Busy) { _status = "已有流程在執行"; return; }
        var cfg = _m.Config.LeakTest with
        {
            MeasureS = (double)(C<NumericUpDown>("NumLeakMeasure").Value ?? 300),
            LimitMtorrPerMin = (double)(C<NumericUpDown>("NumLeakLimit").Value ?? 1),
        };
        _leak = new LeakTestSequencer(_m, cfg);
        string user = _user ?? "";
        lock (_leakLog) _leakLog.Clear();
        _leakPlotN = -1;
        _leak.Progress += p =>
        {
            TowerEvent(p, doneNeedsOperator: false);
            _status = $"漏率測試：{p.Step} {p.Message}".TrimEnd();
            lock (_leakLog) _leakLog.Insert(0, $"{p.Time:HH:mm:ss} {p.Step} {p.Message}".TrimEnd());
            _alarmLog?.Add("漏率測試", p.State == SequencerState.Failed ? "Alarm" : "Info", $"{p.Step} {p.Message}".Trim(), evt: "STEP");
        };
        _alarmLog?.Add(OpSource, "Info", $"漏率測試開始（{cfg.MeasureS:0} s，上限 {cfg.LimitMtorrPerMin:0.###} mTorr/min）", evt: "OPERATOR");
        var seq = _leak;
        await seq.RunAsync();
        if (seq.Result is { } r)
        {
            _maint?.AddLeakTest(r, user);
            _alarmLog?.Add("漏率測試", r.Pass ? "Info" : "Warning", r.Text(), evt: "EVENT");
        }
        _leakHistShown = -1;
    }

    private void RenderMaint(MachineSnapshot s)
    {
        bool can = Can(Permission.Maintenance);
        C<Button>("BtnLeakStart").IsEnabled = _m is not null && can && !Busy;
        C<Button>("BtnLeakAbort").IsEnabled = LeakRunning;
        C<StackPanel>("PnlLeakSet").IsEnabled = can && !LeakRunning;
        C<StackPanel>("PnlMaintEdit").IsEnabled = can;

        if (_m is not null)
        {
            var cfg = _m.Config.LeakTest;
            double p = LeakMath.Read(s, cfg.Gauge);
            string state = _leak is null ? (can ? "待機" : "漏率測試需要工程師以上權限，請先登入")
                : _leak.State switch
                {
                    SequencerState.Running => $"執行中：{_leak.Step}",
                    SequencerState.Done => _leak.Result!.Pass ? "完成：合格" : "完成：不合格",
                    SequencerState.Aborted => "已中止",
                    SequencerState.Failed => $"失敗：{_leak.Step}",
                    _ => "待機",
                };
            C<TextBlock>("TxtLeakState").Text = state;
            var live = $"{GaugeName(cfg.Gauge)} 目前 {P(p)} Torr   Turbo GV {(s.TurboGateOpened ? "開" : s.TurboGateClosed ? "關" : "動作中")}";
            if (_leak is not null)
            {
                var pts = _leak.Samples;
                if (_leak.Result is { } r) live += "\n" + r.Text();
                else if (pts.Count >= 3)
                {
                    var (slope, r2) = LeakMath.Fit(pts);
                    live += $"\n已量 {pts[^1].T:0} s / {pts.Count} 點  目前估計 {slope * 60_000:0.000} mTorr/min  R² {r2:0.000}";
                }
                if (_leak.State != SequencerState.Done)
                {
                    string last;
                    lock (_leakLog) last = string.Join("\n", _leakLog.Take(4));
                    live += "\n" + last;
                }
                if (pts.Count != _leakPlotN && DateTime.Now - _leakPlotAt > TimeSpan.FromSeconds(1)) DrawLeak(pts);
            }
            C<TextBlock>("TxtLeakLive").Text = live;
        }

        if (_maint is null) return;
        if (_leakHistShown < 0)
        {
            C<ListBox>("LstLeakHist").ItemsSource = _maint.LeakTests(50).Select(x =>
                $"{x.Time:yyyy-MM-dd HH:mm} {(x.Result.Pass ? "合格  " : "不合格")} {x.Result.RorMtorrPerMin,8:0.000} mTorr/min " +
                $"{x.Result.LeakTorrLps:0.0E+00} Torr·L/s  {x.User}{(x.Result.Note.Length > 0 ? "  " + x.Result.Note : "")}").ToList();
            _leakHistShown = 1;
        }
        if (DateTime.Now - _maintShownAt > TimeSpan.FromSeconds(2))
        {
            _maintShownAt = DateTime.Now;
            var rows = _maint.Rows();
            var lst = C<ListBox>("LstMaint");
            int sel = lst.SelectedIndex;
            static string V(double v, string unit) => unit == "h" ? $"{v,9:0.00} h" : $"{v,9:0} 次";
            lst.ItemsSource = rows.Select(r =>
                $"{(r.Due ? "⚠" : " ")}{V(r.Value, r.Item.Unit)} / {(r.Limit > 0 ? V(r.Limit, r.Item.Unit) + $" {r.Percent,4:0}%" : "   不提醒        ")}  {r.Item.Name}" +
                $"\n  總累計 {Maintenance.Format(r.Total, r.Item.Unit)}{(r.ResetTime is { } t ? $"   上次保養 {t:yyyy-MM-dd HH:mm} {r.ResetBy}" : "")}").ToList();
            lst.SelectedIndex = sel;
            int due = rows.Count(r => r.Due);
            C<TabItem>("TabMaint").Header = due > 0 ? $"保養 ⚠{due}" : "保養";
            if (_maintSel is not null && rows.FirstOrDefault(r => r.Item.Id == _maintSel) is { } cur)
                C<TextBlock>("TxtMaintSel").Text = $"{cur.Item.Name}：{Maintenance.Format(cur.Value, cur.Item.Unit)}" +
                    (cur.Limit > 0 ? $"，上限 {Maintenance.Format(cur.Limit, cur.Item.Unit)}" : "，不提醒");
            else C<TextBlock>("TxtMaintSel").Text = "（選一個項目）";
        }
    }

    private static string GaugeName(string g) => g.ToLowerInvariant() switch
    {
        "chamberiongauge" => "Ion gauge（WRG）",
        "chambermanometer" => "Barocel",
        "chamberconvectron" => "PM 互鎖（APGX）",
        _ => g,
    };

    private void DrawLeak(IReadOnlyList<(double T, double P)> pts)
    {
        _leakPlotAt = DateTime.Now;
        _leakPlotN = pts.Count;
        var view = C<AvaPlot>("PlotLeak");
        var plot = view.Plot;
        plot.Clear();
        if (pts.Count > 0)
        {
            var xs = pts.Select(x => x.T).ToArray();
            var ys = pts.Select(x => x.P).ToArray();
            var sc = plot.Add.Scatter(xs, ys);
            sc.LineWidth = 0;
            sc.MarkerSize = 4;
            sc.LegendText = "壓力";
            if (pts.Count >= 3)
            {
                var (slope, _) = LeakMath.Fit(pts);
                double mt = xs.Average(), mp = ys.Average();
                var fit = plot.Add.Line(xs[0], mp + slope * (xs[0] - mt), xs[^1], mp + slope * (xs[^1] - mt));
                fit.LineWidth = 2;
                fit.LegendText = $"擬合 {slope * 60_000:0.000} mTorr/min";
            }
            plot.ShowLegend(ScottPlot.Alignment.UpperLeft);
        }
        plot.Axes.Left.TickGenerator = new ScottPlot.TickGenerators.NumericAutomatic { LabelFormatter = y => y.ToString("0.0E+00") };
        plot.Axes.AutoScale();
        view.Refresh();
    }
}
