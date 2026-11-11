namespace Ald.Core;

/// <summary>批次裡的一片：晶圓編號 + 要跑的配方。</summary>
public sealed record BatchItem(string WaferId, ProcessRecipe Recipe);

public enum BatchItemState { Waiting, Running, Done, Failed, Aborted }

public sealed record BatchItemResult(BatchItem Item, BatchItemState State, DateTime? Start, DateTime? End, string Message);

/// <summary>
/// 多片批次（單 Loadlock、手臂一次一片）：
///   每一片：Loadlock 洩壓 → 等人員取出上一片 / 放入這一片並按確認（藍燈）→ 自動跑片（LL 抽氣 → 送片 → 製程 → 取片）。
///   最後一片做完：Loadlock 洩壓 → 等人員取出 →（勾選時）PM 回填。
/// 各片配方的 VentAfter 一律不做（PM 保持真空給下一片），回填只在批次最後。
/// 任一片失敗或中止就整批停下，後面的片標成未跑，不會自動往下做。
/// </summary>
public sealed class BatchJob
{
    private readonly IMachine _m;
    private readonly WaferMap _map;
    private readonly TransferConfig _xferCfg;
    private readonly bool _zEnabled;
    private readonly TimeSpan? _poll;
    private CancellationTokenSource? _cts;
    private AutoRunJob? _auto;
    private TaskCompletionSource<bool>? _confirm;
    private readonly List<BatchItemResult> _results = new();

    public SequencerState State { get; private set; } = SequencerState.Idle;
    public string Step { get; private set; } = "";
    /// <summary>目前在等人員做什麼（空 = 沒在等）。藍燈、確認按鈕看這個。</summary>
    public string Prompt { get; private set; } = "";
    public bool WaitingOperator => Prompt.Length > 0;
    /// <summary>第幾片（0 起算），-1 = 還沒開始。</summary>
    public int Current { get; private set; } = -1;
    public event Action<SequencerProgress>? Progress;
    /// <summary>某一片狀態改變（開始跑、完成、失敗、中止），給跑片履歷記錄用。</summary>
    public event Action<int, BatchItemResult>? ItemChanged;

    public BatchJob(IMachine machine, WaferMap map, TransferConfig transfer, bool loadlockZEnabled, TimeSpan? poll = null)
    {
        _m = machine;
        _map = map;
        _xferCfg = transfer;
        _zEnabled = loadlockZEnabled;
        _poll = poll;
    }

    public IReadOnlyList<BatchItemResult> Results { get { lock (_results) return _results.ToList(); } }

    /// <summary>人員做完（放片 / 取片、蓋子關好）後按確認。</summary>
    public void Confirm() => _confirm?.TrySetResult(true);

    public void Abort()
    {
        _cts?.Cancel();
        _auto?.Abort();
        _confirm?.TrySetCanceled();
    }

    public static IEnumerable<string> Precheck(IReadOnlyList<BatchItem> items, MachineSnapshot s, WaferMap map)
    {
        if (items.Count == 0) yield return "批次是空的";
        foreach (var (it, i) in items.Select((x, i) => (x, i)))
        {
            if (string.IsNullOrWhiteSpace(it.WaferId)) yield return $"第 {i + 1} 片沒有晶圓編號";
            foreach (var e in it.Recipe.Validate()) yield return $"第 {i + 1} 片（{it.WaferId}）配方：{e}";
        }
        foreach (var g in items.GroupBy(x => x.WaferId.Trim()).Where(g => g.Count() > 1 && g.Key.Length > 0))
            yield return $"晶圓編號 {g.Key} 重複";
        if (!s.AllOnline) yield return "主控板或控制板離線";
        if (s.SafeState) yield return "系統在安全狀態，請先 Reset";
        if (map.Pm != false) yield return "PM 要確認無片（傳片頁可修正晶圓位置）";
        if (map.Loadlock is null) yield return "Loadlock 晶圓位置不明（傳片頁可修正）";
    }

    /// <param name="ventPmAtEnd">整批做完後 PM 回填大氣</param>
    public async Task<SequencerState> RunAsync(IReadOnlyList<BatchItem> items, bool ventPmAtEnd, CancellationToken ct = default)
    {
        if (State == SequencerState.Running) throw new InvalidOperationException("批次執行中");
        lock (_results)
        {
            _results.Clear();
            _results.AddRange(items.Select(x => new BatchItemResult(x, BatchItemState.Waiting, null, null, "")));
        }
        Current = -1;
        var pre = Precheck(items, _m.Snapshot, _map).ToList();
        if (pre.Count > 0) { Report(SequencerState.Failed, "檢查", string.Join("；", pre)); return State; }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = _cts.Token;
        Report(SequencerState.Running, "開始", $"{items.Count} 片");
        try
        {
            string? prev = _map.Loadlock == true ? "Loadlock 上原有的片" : null;
            for (int i = 0; i < items.Count; i++)
            {
                Current = i;
                var it = items[i];
                string tag = $"{i + 1}/{items.Count} {it.WaferId}";

                // 換片：洩壓 → 人員取出上一片、放入這一片
                await VentLoadlockAsync(tag, token);
                await OperatorAsync(tag, prev is null
                    ? $"請打開 Loadlock，放入晶圓 {it.WaferId}，關好上蓋後按「確認」"
                    : $"請打開 Loadlock，取出 {prev}，放入晶圓 {it.WaferId}，關好上蓋後按「確認」", token);
                _map.Set(true, false, $"批次放入 {it.WaferId}", it.WaferId);

                // 自動跑片
                Set(i, BatchItemState.Running, DateTime.Now, null, "");
                _auto = new AutoRunJob(_m, _map, _xferCfg, _zEnabled, _poll);
                string last = "";
                _auto.Progress += p =>
                {
                    if (p.State == SequencerState.Running) Report(SequencerState.Running, $"{tag}：{p.Step}", p.Message, quiet: true);
                    else if (p.State == SequencerState.Failed) last = $"{p.Step} {p.Message}".Trim();
                };
                var st = await _auto.RunAsync(it.Recipe with { VentAfter = false }, token);
                _auto = null;
                token.ThrowIfCancellationRequested();
                if (st == SequencerState.Aborted) throw new OperationCanceledException();
                if (st != SequencerState.Done)
                {
                    Set(i, BatchItemState.Failed, null, DateTime.Now, last);
                    throw new BatchException($"{tag} 失敗：{last}");
                }
                Set(i, BatchItemState.Done, null, DateTime.Now, "");
                Report(SequencerState.Running, $"{tag} 完成", "");
                prev = it.WaferId;
            }

            Current = items.Count;
            await VentLoadlockAsync("收尾", token);
            await OperatorAsync("收尾", $"請打開 Loadlock，取出 {prev}，關好上蓋後按「確認」", token);
            _map.Set(false, false, $"批次結束，取出 {prev}");

            if (ventPmAtEnd)
            {
                SetStep("收尾：PM 回填");
                var seq = new ProcessSequencer(_m, _poll ?? TimeSpan.FromMilliseconds(200));
                if (await seq.RunAsync(new ProcessRecipe { Name = "批次結束 PM 回填", PumpDown = false, VentAfter = true }, token) != SequencerState.Done)
                    throw new BatchException($"PM 回填沒有完成：{seq.Step}");
            }
            Report(SequencerState.Done, "完成", $"{items.Count} 片全部完成");
        }
        catch (OperationCanceledException)
        {
            MarkStopped(BatchItemState.Aborted, "使用者中止");
            Report(SequencerState.Aborted, Step, "使用者中止，停在目前狀態（晶圓位置看傳片頁）");
        }
        catch (Exception e)
        {
            MarkStopped(BatchItemState.Failed, e.Message);
            Report(SequencerState.Failed, Step, $"{e.Message}；整批停止，後面的片沒有跑");
        }
        finally { Prompt = ""; _confirm = null; _auto = null; }
        return State;
    }

    private async Task VentLoadlockAsync(string tag, CancellationToken ct)
    {
        SetStep($"{tag}：Loadlock 洩壓");
        double timeout = _xferCfg.LoadlockStepTimeoutS * 5;
        if (!await _m.LoadlockAsync(LoadlockCommand.Vent, 0, TimeSpan.FromSeconds(timeout), ct))
            throw new BatchException($"Loadlock 洩壓沒有完成：{_m.LastLoadlockError}");
        // SLK 回「完成」只代表收到命令，洩壓序列還在跑：等它跑完、壓力回到量測範圍（≥ 1 Torr）
        var end = DateTime.Now.AddSeconds(timeout);
        while (_m.Snapshot is var s && (s.LoadlockBusy || s.LoadlockPressureUnderRange || !s.LoadlockFresh))
        {
            if (DateTime.Now > end) throw new BatchException("Loadlock 洩壓逾時（序列沒有結束或壓力沒有回升）");
            await Task.Delay(_poll ?? TimeSpan.FromMilliseconds(200), ct);
        }
    }

    /// <summary>等人員確認；確認時蓋子還開著就再等一次。</summary>
    private async Task OperatorAsync(string tag, string prompt, CancellationToken ct)
    {
        while (true)
        {
            _confirm = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Prompt = prompt;
            Report(SequencerState.Running, $"{tag}：等待人員", prompt);
            using (ct.Register(() => _confirm.TrySetCanceled()))
                await _confirm.Task;
            ct.ThrowIfCancellationRequested();
            if (!_m.Snapshot.LoadlockLidOpen) break;
            prompt = "Loadlock 上蓋還開著，請關好後再按「確認」";
        }
        Prompt = "";
        Report(SequencerState.Running, $"{tag}：人員已確認", "");
    }

    private void Set(int i, BatchItemState st, DateTime? start, DateTime? end, string msg)
    {
        BatchItemResult n;
        lock (_results)
        {
            var r = _results[i];
            _results[i] = n = r with { State = st, Start = start ?? r.Start, End = end ?? r.End, Message = msg };
        }
        ItemChanged?.Invoke(i, n);
    }

    private void MarkStopped(BatchItemState st, string msg)
    {
        var changed = new List<(int, BatchItemResult)>();
        lock (_results)
            for (int i = 0; i < _results.Count; i++)
                if (_results[i].State == BatchItemState.Running)
                {
                    _results[i] = _results[i] with { State = st, End = DateTime.Now, Message = msg };
                    changed.Add((i, _results[i]));
                }
        foreach (var (i, r) in changed) ItemChanged?.Invoke(i, r);
    }

    private void SetStep(string step) => Report(SequencerState.Running, step, "");

    private void Report(SequencerState st, string step, string msg, bool quiet = false)
    {
        State = st;
        if (quiet && step == Step) return;
        Step = step;
        Progress?.Invoke(new SequencerProgress(st, step, msg, DateTime.Now));
    }

    private sealed class BatchException(string message) : Exception(message);
}
