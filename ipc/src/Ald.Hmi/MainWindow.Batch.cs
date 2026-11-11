using System.Text.RegularExpressions;
using Ald.Core;
using Avalonia.Controls;

namespace Ald.Hmi;

/// <summary>
/// 批次頁：排多片（晶圓編號 + 配方）→ BatchJob 連續跑；換片時藍燈 + 提示框，人員放片 / 取片後按確認。
/// 開始、確認需要操作員權限（同傳片）；中止任何人都能按。
/// </summary>
public partial class MainWindow
{
    private BatchJob? _batch;
    private readonly List<BatchItem> _batchItems = new();
    private readonly List<string> _batchLog = new();
    private int _batchLogShown = -1;
    private DateTime _batchListAt = DateTime.MinValue;
    private bool _batchEdited = true;   // 排片清單改過 → 顯示清單，不顯示上一批的結果

    private bool BatchRunning => _batch?.State == SequencerState.Running;
    private bool BatchWaiting => BatchRunning && _batch!.WaitingOperator;

    private void WireBatch()
    {
        var cmb = C<ComboBox>("CmbBatchRecipe");
        void Reload() => cmb.ItemsSource = Directory.Exists(_recipeDir)
            ? Directory.GetFiles(_recipeDir, "*.json").Select(Path.GetFileName).OfType<string>().OrderBy(x => x).ToList()
            : new List<string>();
        Reload();
        cmb.DropDownOpened += (_, _) => Reload();

        C<Button>("BtnBatchAdd").Click += (_, _) =>
        {
            if (cmb.SelectedItem is not string file) { _status = "請先選配方"; return; }
            ProcessRecipe r;
            try { r = RecipeFile.Load(Path.Combine(_recipeDir, file)); }
            catch (Exception e) { _status = $"配方讀取失敗：{e.Message}"; return; }
            var errs = Check(r);
            if (errs.Count > 0) { _status = $"配方 {file} 不能用：{string.Join("；", errs)}"; return; }
            string id = (C<TextBox>("TxtBatchId").Text ?? "").Trim();
            int n = (int)(C<NumericUpDown>("NumBatchCount").Value ?? 1);
            for (int i = 0; i < n; i++)
            {
                if (id.Length == 0) id = $"W{_batchItems.Count + 1:00}";
                _batchItems.Add(new BatchItem(id, r));
                id = NextId(id);
            }
            C<TextBox>("TxtBatchId").Text = id;
            _batchListAt = DateTime.MinValue;
            _batchEdited = true;
        };
        C<Button>("BtnBatchDel").Click += (_, _) =>
        {
            int i = C<ListBox>("LstBatch").SelectedIndex;
            if (i >= 0 && i < _batchItems.Count) _batchItems.RemoveAt(i);
            _batchListAt = DateTime.MinValue;
            _batchEdited = true;
        };
        C<Button>("BtnBatchClear").Click += (_, _) => { _batchItems.Clear(); _batchListAt = DateTime.MinValue; _batchEdited = true; };
        C<Button>("BtnBatchStart").Click += (_, _) => RunBatch();
        C<Button>("BtnBatchAbort").Click += (_, _) => _batch?.Abort();
        C<Button>("BtnBatchConfirm").Click += (_, _) =>
        {
            if (_batch is null || !_batch.WaitingOperator) return;
            _alarmLog?.Add(OpSource, "Info", $"批次人員確認：{_batch.Prompt}", evt: "OPERATOR");
            _batch.Confirm();
        };
    }

    /// <summary>W01 → W02、LOT7-09 → LOT7-10；結尾沒有數字就加 -2。</summary>
    private static string NextId(string id)
    {
        var m = Regex.Match(id, @"^(.*?)(\d+)$");
        if (!m.Success) return id + "-2";
        var num = m.Groups[2].Value;
        return m.Groups[1].Value + (long.Parse(num) + 1).ToString(new string('0', num.Length));
    }

    private async void RunBatch()
    {
        if (_m is null || _wafers is null || Busy) { _status = "已有流程在執行"; return; }
        var items = _batchItems.ToList();
        _needOp = false;
        _batchEdited = false;
        _batch = new BatchJob(_m, _wafers, _m.Config.Transfer, _m.Config.Interlock.LoadlockZEnabled);
        lock (_batchLog) _batchLog.Clear();
        _batch.Progress += p =>
        {
            _status = $"批次：{p.Step} {p.Message}".TrimEnd();
            lock (_batchLog) _batchLog.Insert(0, $"{p.Time:HH:mm:ss} {p.Step} {p.Message}".TrimEnd());
            if (!p.Step.Contains("ALD：cycle"))
                _alarmLog?.Add("批次", p.State == SequencerState.Failed ? "Alarm" : "Info", $"{p.Step} {p.Message}".Trim(), evt: "STEP");
            TowerEvent(p, doneNeedsOperator: false);
            _batchListAt = DateTime.MinValue;
        };
        string batchId = $"B{DateTime.Now:yyyyMMdd-HHmmss}";
        var ids = new long?[items.Count];
        _batch.ItemChanged += (i, x) =>
        {
            if (x.State == BatchItemState.Running) ids[i] = RunStart("批次", x.Item.WaferId, x.Item.Recipe, batchId);
            else if (x.State is BatchItemState.Done or BatchItemState.Failed or BatchItemState.Aborted)
                RunEnd(ids[i], x.State switch { BatchItemState.Done => SequencerState.Done, BatchItemState.Failed => SequencerState.Failed, _ => SequencerState.Aborted }, x.Message);
        };
        bool vent = C<CheckBox>("ChkBatchVent").IsChecked == true;
        _alarmLog?.Add(OpSource, "Info", $"批次 {batchId} 開始：{items.Count} 片（{string.Join("、", items.Select(x => x.WaferId))}）{(vent ? "，結束後 PM 回填" : "")}", evt: "OPERATOR");
        await _batch.RunAsync(items, vent);
        _batchListAt = DateTime.MinValue;
    }

    private void RenderBatch()
    {
        bool can = Can(Permission.RunRecipe) && Can(Permission.Transfer);
        bool running = BatchRunning;
        C<StackPanel>("PnlBatchEdit").IsEnabled = can && !running;
        C<Button>("BtnBatchStart").IsEnabled = _m is not null && can && !Busy && _batchItems.Count > 0;
        C<Button>("BtnBatchAbort").IsEnabled = running;
        C<Button>("BtnBatchConfirm").IsEnabled = Can(Permission.Transfer);

        if (_m is not null && _wafers is not null && !running)
        {
            var e = BatchJob.Precheck(_batchItems, _m.Snapshot, _wafers).Where(x => x != "批次是空的").ToList();
            C<TextBlock>("TxtBatchCheck").Text = !can ? "批次需要操作員以上權限，請先登入"
                : e.Count == 0 ? "" : "現在不能開始：" + string.Join("；", e);
        }
        else C<TextBlock>("TxtBatchCheck").Text = "";

        bool wait = BatchWaiting;
        C<Border>("PnlBatchPrompt").IsVisible = wait;
        if (wait) C<TextBlock>("TxtBatchPrompt").Text = _batch!.Prompt;
        C<TextBlock>("TxtBatchState").Text = _batch is null ? "待機" : _batch.State switch
        {
            SequencerState.Running => $"執行中：{_batch.Step}",
            SequencerState.Done => "完成：全部片子做完",
            SequencerState.Aborted => $"已中止：{_batch.Step}",
            SequencerState.Failed => $"失敗：{_batch.Step}",
            _ => "待機",
        };

        if (DateTime.Now - _batchListAt > TimeSpan.FromSeconds(1))
        {
            _batchListAt = DateTime.Now;
            var res = _batch is not null && (running || !_batchEdited) ? _batch.Results : null;
            static string St(BatchItemState s) => s switch
            {
                BatchItemState.Running => "▶ 執行中", BatchItemState.Done => "✓ 完成  ", BatchItemState.Failed => "✗ 失敗  ",
                BatchItemState.Aborted => "■ 中止  ", _ => "  等待  ",
            };
            var rows = res is not null
                ? res.Select((r, i) => $"{i + 1,2}. {St(r.State)} {r.Item.WaferId,-10} {r.Item.Recipe.Name}" +
                    (r.End is { } end && r.Start is { } st ? $"  {(end - st):mm\\:ss}" : "") + (r.Message.Length > 0 ? $"  {r.Message}" : ""))
                : _batchItems.Select((x, i) => $"{i + 1,2}.          {x.WaferId,-10} {x.Recipe.Name}");
            var lst = C<ListBox>("LstBatch");
            int sel = lst.SelectedIndex;
            lst.ItemsSource = rows.ToList();
            lst.SelectedIndex = sel;
        }
        lock (_batchLog)
            if (_batchLog.Count != _batchLogShown)
            {
                C<ListBox>("LstBatchLog").ItemsSource = _batchLog.Take(300).ToList();
                _batchLogShown = _batchLog.Count;
            }
    }
}
