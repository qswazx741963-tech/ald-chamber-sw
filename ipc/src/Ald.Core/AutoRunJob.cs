namespace Ald.Core;

/// <summary>
/// 自動跑片（一片）：準備（LL / PM 抽真空）→ 送片 → 製程 → 取片 →（配方要求時）PM 回填。
/// 每一段用現成的 ProcessSequencer / TransferSequencer，任一段失敗或中止就整個停下，
/// 停在該段的安全處理（製程段：停 ALD、關氣；傳片段：停手臂、不自動往回），不會接著做下一段。
/// 製程段一定不 vent（片還在 PM、要在真空下取片），回填放到取片之後。
/// </summary>
public sealed class AutoRunJob
{
    private readonly IMachine _m;
    private readonly WaferMap _map;
    private readonly TransferConfig _xferCfg;
    private readonly bool _zEnabled;
    private readonly TimeSpan? _poll;
    private CancellationTokenSource? _cts;
    private ProcessSequencer? _proc;
    private TransferSequencer? _xfer;

    public SequencerState State { get; private set; } = SequencerState.Idle;
    public string Phase { get; private set; } = "";
    public string Step { get; private set; } = "";
    public event Action<SequencerProgress>? Progress;

    public AutoRunJob(IMachine machine, WaferMap map, TransferConfig transfer, bool loadlockZEnabled, TimeSpan? poll = null)
    {
        _m = machine;
        _map = map;
        _xferCfg = transfer;
        _zEnabled = loadlockZEnabled;
        _poll = poll;
    }

    public void Abort()
    {
        _cts?.Cancel();
        _proc?.Abort();
        _xfer?.Abort();
    }

    /// <summary>開始前檢查（配方 + 晶圓位置）。</summary>
    public static IEnumerable<string> Precheck(ProcessRecipe r, MachineSnapshot s, WaferMap map)
    {
        foreach (var e in r.Validate()) yield return e;
        if (!s.AllOnline) yield return "主控板或控制板離線";
        if (s.SafeState) yield return "系統在安全狀態，請先 Reset";
        if (map.Loadlock != true || map.Pm != false) yield return "自動跑片需要：Loadlock 手臂上有片、PM 無片";
        if (!r.PumpDown && !(s.ChamberPressureTorr < 1)) yield return "配方沒有勾抽真空，PM 也不在真空";
    }

    public async Task<SequencerState> RunAsync(ProcessRecipe r, CancellationToken ct = default)
    {
        if (State == SequencerState.Running) throw new InvalidOperationException("自動跑片執行中");
        var pre = Precheck(r, _m.Snapshot, _map).ToList();
        if (pre.Count > 0) { Report(SequencerState.Failed, "檢查", "", string.Join("；", pre)); return State; }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = _cts.Token;
        Report(SequencerState.Running, "開始", "", r.Name);
        try
        {
            // 1. 準備：LL 抽到 < 1 Torr、PM 抽到 base pressure
            Phase = "1/5 準備";
            var s = _m.Snapshot;
            if (!(s.LoadlockPressureUnderRange || s.LoadlockPressureTorr < 1))
            {
                Report(SequencerState.Running, Phase, "Loadlock 抽氣", "");
                if (!await _m.LoadlockAsync(LoadlockCommand.Pump, 0, TimeSpan.FromSeconds(_xferCfg.LoadlockStepTimeoutS * 5), token))
                    throw new AutoRunException($"Loadlock 抽氣沒有完成：{_m.LastLoadlockError}");
            }
            if (r.PumpDown)
                await Sub(new ProcessRecipe
                {
                    Name = r.Name + "（抽真空）", PumpDown = true, CrossoverTorr = r.CrossoverTorr, BaseTorr = r.BaseTorr,
                    PumpTimeoutS = r.PumpTimeoutS, TurboSpinUpTimeoutS = r.TurboSpinUpTimeoutS,
                }, token);

            // 2. 送片
            Phase = "2/5 送片";
            await Sub(TransferDirection.LoadToPm, token);

            // 3. 製程（不 vent；已在真空就不再抽）
            Phase = "3/5 製程";
            await Sub(r with { PumpDown = false, VentAfter = false }, token);

            // 4. 取片
            Phase = "4/5 取片";
            await Sub(TransferDirection.UnloadToLoadlock, token);

            // 5. 回填（配方要求時）
            Phase = "5/5 收尾";
            if (r.VentAfter)
                await Sub(new ProcessRecipe
                {
                    Name = r.Name + "（回填）", PumpDown = false, VentAfter = true, VentTargetTorr = r.VentTargetTorr,
                    VentTimeoutS = r.VentTimeoutS, TurboSpinDownTimeoutS = r.TurboSpinDownTimeoutS,
                }, token);
            Phase = "";
            Report(SequencerState.Done, "完成", "", r.Name);
        }
        catch (OperationCanceledException) { Report(SequencerState.Aborted, Step, "", $"{Phase} 使用者中止"); }
        catch (AutoRunException e) { Report(SequencerState.Failed, Step, "", $"{Phase} 停止：{e.Message}"); }
        catch (Exception e) { Report(SequencerState.Failed, Step, "", $"{Phase} 停止：{e.Message}"); }
        finally { _proc = null; _xfer = null; }
        return State;
    }

    private async Task Sub(ProcessRecipe r, CancellationToken ct)
    {
        _proc = new ProcessSequencer(_m, _poll);
        _proc.Progress += Forward;
        var st = await _proc.RunAsync(r, ct);
        ct.ThrowIfCancellationRequested();
        if (st == SequencerState.Aborted) throw new OperationCanceledException();
        if (st != SequencerState.Done) throw new AutoRunException(_proc.Step);
    }

    private async Task Sub(TransferDirection dir, CancellationToken ct)
    {
        _xfer = new TransferSequencer(_m, _map, _xferCfg, _zEnabled, _poll);
        _xfer.Progress += Forward;
        var st = await _xfer.RunAsync(dir, ct);
        ct.ThrowIfCancellationRequested();
        if (st == SequencerState.Aborted) throw new OperationCanceledException();
        if (st != SequencerState.Done) throw new AutoRunException(_xfer.Step);
    }

    private string _lastSubMsg = "";

    /// <summary>子流程的步驟轉發出去（加上段名）；子流程失敗訊息記下來當停止原因。</summary>
    private void Forward(SequencerProgress p)
    {
        if (p.State is SequencerState.Done or SequencerState.Idle) return;
        if (p.State is SequencerState.Failed or SequencerState.Aborted) { _lastSubMsg = $"{p.Step} {p.Message}".Trim(); return; }
        Report(SequencerState.Running, Phase, p.Step, p.Message);
    }

    private void Report(SequencerState st, string phase, string step, string msg)
    {
        State = st;
        Step = string.IsNullOrEmpty(step) ? phase : $"{phase}：{step}";
        if (st is SequencerState.Failed && _lastSubMsg.Length > 0 && !msg.Contains(_lastSubMsg)) msg = $"{msg}（{_lastSubMsg}）";
        Progress?.Invoke(new SequencerProgress(st, Step, msg, DateTime.Now));
    }

    private sealed class AutoRunException(string message) : Exception(message);
}
