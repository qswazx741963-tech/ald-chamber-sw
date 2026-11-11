using Ald.Core;
using Ald.Data;
using Avalonia.Controls;

namespace Ald.Hmi;

/// <summary>
/// 跑片履歷（RunLog）：Recipe 頁「只跑製程 / 自動跑片」、批次的每一片各記一筆。
/// 履歷頁可依時間 / 結果 / 關鍵字（晶圓編號、配方、批次、訊息、使用者）查詢，選一筆看當時的配方內容，匯出 CSV。
/// </summary>
public partial class MainWindow
{
    private readonly RunLog? _runs;
    private IReadOnlyList<RunLog.Row> _histRows = Array.Empty<RunLog.Row>();
    private bool _histDirty = true;
    private bool _histBusy;
    private static readonly (string Text, string? Result)[] HistResults =
        { ("全部結果", null), ("完成 Done", "Done"), ("失敗 Failed", "Failed"), ("中止 Aborted", "Aborted"), ("中斷 Interrupted", RunLog.Interrupted), ("執行中", RunLog.Running) };

    private string RunWaferId() => (C<TextBox>("TxtRunWafer").Text ?? "").Trim();

    private long? RunStart(string kind, string waferId, ProcessRecipe r, string batch = "")
    {
        try { var id = _runs?.Start(kind, waferId, r, _user ?? "", batch); _histDirty = true; return id; }
        catch (Exception e) { _alarmLog?.Add("履歷", "Warning", $"履歷寫入失敗：{e.Message}", evt: "EVENT"); return null; }
    }

    private void RunEnd(long? id, SequencerState state, string message)
    {
        if (id is not { } i || _runs is null) return;
        try { _runs.End(i, state.ToString(), state == SequencerState.Done ? "" : message); _histDirty = true; }
        catch (Exception e) { _alarmLog?.Add("履歷", "Warning", $"履歷寫入失敗：{e.Message}", evt: "EVENT"); }
    }

    private void WireHistory()
    {
        var r = C<ComboBox>("CmbHistRange");
        r.ItemsSource = EvtRanges.Select(x => x.Text).ToList();
        r.SelectedIndex = 3;
        var res = C<ComboBox>("CmbHistResult");
        res.ItemsSource = HistResults.Select(x => x.Text).ToList();
        res.SelectedIndex = 0;
        r.SelectionChanged += (_, _) => QueryHistory();
        res.SelectionChanged += (_, _) => QueryHistory();
        C<Button>("BtnHistQuery").Click += (_, _) => QueryHistory();
        C<TextBox>("TxtHistKey").KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) QueryHistory(); };
        C<Button>("BtnHistCsv").Click += (_, _) => ExportHistory();
        var lst = C<ListBox>("LstHist");
        lst.SelectionChanged += (_, _) => ShowHistDetail(lst.SelectedIndex);
    }

    private (DateTime From, DateTime To, string? Result, string Key) HistFilter()
    {
        var to = DateTime.Now;
        return (to - EvtRanges[Math.Max(0, C<ComboBox>("CmbHistRange").SelectedIndex)].Span, to,
                HistResults[Math.Max(0, C<ComboBox>("CmbHistResult").SelectedIndex)].Result,
                C<TextBox>("TxtHistKey").Text?.Trim() ?? "");
    }

    private void RenderHistory()
    {
        if (_histDirty && C<ListBox>("LstHist").IsEffectivelyVisible) QueryHistory();
    }

    private static string ResultText(string r) => r switch
    {
        "Done" => "完成", "Failed" => "失敗", "Aborted" => "中止", RunLog.Interrupted => "中斷", RunLog.Running => "執行中", _ => r,
    };

    private async void QueryHistory()
    {
        if (_runs is null || _histBusy) return;
        _histBusy = true;
        _histDirty = false;
        try
        {
            var (from, to, result, key) = HistFilter();
            var runs = _runs;
            _histRows = await Task.Run(() => runs.Query(from, to, key, result, 2000));
            C<ListBox>("LstHist").ItemsSource = _histRows.Select(x =>
                $"{x.Start:MM-dd HH:mm:ss}  {ResultText(x.Result),-3}  {x.WaferId,-12} {x.Recipe,-20} {x.Kind,-4} " +
                $"{(x.Duration is { } d ? $"{(int)d.TotalMinutes,3}:{d.Seconds:00}" : "  -  ")}  {x.User}" +
                (x.Batch.Length > 0 ? $"  {x.Batch}" : "") + (x.Message.Length > 0 ? $"\n    {x.Message}" : "")).ToList();
            int done = _histRows.Count(x => x.Result == "Done");
            C<TextBlock>("TxtHistInfo").Text = $"{_histRows.Count} 筆（完成 {done}、其他 {_histRows.Count - done}）";
            C<TextBlock>("TxtHistDetail").Text = "（選一筆看當時的配方）";
        }
        catch (Exception e) { C<TextBlock>("TxtHistInfo").Text = $"查詢失敗：{e.Message}"; }
        finally { _histBusy = false; }
    }

    private void ShowHistDetail(int i)
    {
        if (i < 0 || i >= _histRows.Count) return;
        var x = _histRows[i];
        string recipe;
        try
        {
            var r = RecipeFile.Parse(x.RecipeJson);
            var heat = r.HeaterSpDegC.Select((t, ch) => (t, ch)).Where(v => v.t > 0).Select(v => $"#{v.ch + 1} {v.t:0} °C");
            var gas = r.Mfc.Select((v, ch) => (v, ch)).Where(v => v.v > 0).Select(v => $"MFC{v.ch + 1} {v.v:0.#}");
            recipe = $"配方 {r.Name}\n抽真空 {(r.PumpDown ? "是" : "否")}  結束回填 {(r.VentAfter ? "是" : "否")}\n" +
                     $"溫度 {(heat.Any() ? string.Join("、", heat) : "不檢查")}\n氣體 {(gas.Any() ? string.Join("、", gas) : "無")}\n" +
                     (r.Ald is { } a ? $"ALD {a.Cycles} cycles：\n{AldStepText.Format(a.Steps)}" : "無 ALD");
        }
        catch (Exception e) { recipe = $"配方內容讀不出來：{e.Message}"; }
        C<TextBlock>("TxtHistDetail").Text =
            $"#{x.Id}  {x.Kind}  晶圓 {(x.WaferId.Length > 0 ? x.WaferId : "（未填）")}  {ResultText(x.Result)}\n" +
            $"開始 {x.Start:yyyy-MM-dd HH:mm:ss}  結束 {x.End?.ToString("HH:mm:ss") ?? "-"}  操作 {x.User}{(x.Batch.Length > 0 ? $"  批次 {x.Batch}" : "")}\n" +
            (x.Message.Length > 0 ? $"訊息 {x.Message}\n" : "") + "\n" + recipe;
    }

    private async void ExportHistory()
    {
        if (_runs is null || _m is null) return;
        var (from, to, result, key) = HistFilter();
        var path = Path.Combine(ExportDir, $"runs_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
        try
        {
            var runs = _runs;
            int n = await Task.Run(() => runs.ExportCsv(path, from, to, key, result));
            _status = $"履歷匯出 {n} 筆：{path}";
            _alarmLog?.Add(OpSource, "Info", $"匯出履歷 {Path.GetFileName(path)}", evt: "OPERATOR");
        }
        catch (Exception e) { _status = $"匯出失敗：{e.Message}"; }
    }
}
