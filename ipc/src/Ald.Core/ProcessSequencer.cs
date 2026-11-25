namespace Ald.Core;

public enum SequencerState { Idle, Running, Done, Aborted, Failed }

public sealed record SequencerProgress(SequencerState State, string Step, string Message, DateTime Time);

/// <summary>
/// 流程引擎：照 ProcessRecipe 一步一步下命令、等狀態。每一步都有逾時；
/// 失敗或中止時把 PM 帶到安全狀態（停 ALD、關前驅物閥與 MFC；加熱維持、真空維持），不自動 vent。
/// 硬體保護不靠這裡：主控板 / 執行板的互鎖一直有效。
/// </summary>
public sealed class ProcessSequencer
{
    private readonly IMachine _m;
    private readonly TimeSpan _poll;
    private CancellationTokenSource? _cts;

    public SequencerState State { get; private set; } = SequencerState.Idle;
    public string Step { get; private set; } = "";
    public event Action<SequencerProgress>? Progress;

    /// <param name="poll">檢查狀態的間隔（測試可以設很短）</param>
    public ProcessSequencer(IMachine machine, TimeSpan? poll = null)
    {
        _m = machine;
        _poll = poll ?? TimeSpan.FromMilliseconds(200);
    }

    public void Abort() => _cts?.Cancel();

    public async Task<SequencerState> RunAsync(ProcessRecipe r, CancellationToken ct = default)
    {
        if (State == SequencerState.Running) throw new InvalidOperationException("流程執行中");
        var errors = r.Validate().ToList();
        if (errors.Count > 0) { Report(SequencerState.Failed, "檢查配方", string.Join("；", errors)); return State; }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = _cts.Token;
        Report(SequencerState.Running, "開始", r.Name);
        try
        {
            Require(_m.Snapshot.AllOnline, "主控板或控制板離線");
            Require(!_m.Snapshot.SafeState, "系統在安全狀態，請先 Reset");
            var mfc = new double[8];
            var opened = new bool[8];
            for (int i = 0; i < r.Steps.Count; i++)
            {
                var st = r.Steps[i];
                string at = r.Steps.Count > 1 ? $"Step {i + 1}/{r.Steps.Count} {st.Name}" : st.Name;
                switch (st.Kind)
                {
                    case StepKind.PumpDown: await PumpDownAsync(st, at, token); break;
                    case StepKind.HeatCheck: await HeatAsync(st, at, token); break;
                    case StepKind.Gas:
                        SetStep(at);
                        ApplyMfc(r, st, mfc, opened);
                        await DelayAsync(st.TimeS, token);
                        break;
                    case StepKind.Ald:
                        ApplyMfc(r, st, mfc, opened);
                        mfc = await AldAsync(st, at, mfc, token);
                        break;
                    case StepKind.Vent:
                        GasOff(mfc, opened);
                        await VentAsync(st, at, token);
                        break;
                }
            }
            EndProcess();
            Report(SequencerState.Done, "完成", r.Name);
        }
        catch (OperationCanceledException)
        {
            SafeStop();
            Report(SequencerState.Aborted, Step, "使用者中止，已停 ALD、關前驅物閥與 MFC");
        }
        catch (SequenceException e)
        {
            SafeStop();
            Report(SequencerState.Failed, Step, e.Message);
        }
        catch (Exception e)   // 通訊中斷、命令被拒（例如超過加熱上限）
        {
            SafeStop();
            Report(SequencerState.Failed, Step, e.Message);
        }
        return State;
    }

    // ------------------------------------------------------------ 步驟
    private async Task PumpDownAsync(ProcessStep st, string at, CancellationToken ct)
    {
        var p = st.Pump ?? new PumpParams();
        double timeout = st.TimeS;
        SetStep($"{at}：Dry pump");
        Require(_m.Snapshot.SlitClosed, "Slit valve 未關");
        Vac(VacuumOutputs.DryPumpOn, on: true);
        Vac(VacuumOutputs.ChamberVent, on: false);
        await WaitAsync(s => s.DryPumpRunning, 60, "Dry pump 沒有啟動", ct);

        if (!(_m.Snapshot.ChamberPressureTorr < p.CrossoverTorr))
        {
            SetStep($"{at}：粗抽");
            Vac(VacuumOutputs.TurboGv, on: false);
            Vac(VacuumOutputs.ChamberRough, on: true);
            await WaitAsync(s => s.ChamberPressureTorr < p.CrossoverTorr, timeout,
                $"粗抽 {timeout:F0} s 內沒有低於 {p.CrossoverTorr} Torr（漏氣？）", ct);
        }

        SetStep($"{at}：Turbo 啟動");
        Vac(VacuumOutputs.TurboRough | VacuumOutputs.TurboOn, on: true);
        await WaitAsync(s => s.TurboAtSpeed, p.TurboSpinUpTimeoutS, "Turbo 沒有到達轉速", ct);

        SetStep($"{at}：開 Turbo GV");
        Vac(VacuumOutputs.ChamberRough, on: false);
        Vac(VacuumOutputs.TurboGv, on: true);
        await WaitAsync(s => s.TurboGateOpened, 30, "Turbo gate valve 沒有打開（互鎖擋下？）", ct);
        await WaitAsync(s => s.ChamberPressureTorr < p.BaseTorr, timeout,
            $"沒有抽到 base pressure {p.BaseTorr:G3} Torr", ct);
    }

    private async Task HeatAsync(ProcessStep st, string at, CancellationToken ct)
    {
        var h = st.Heat ?? new HeatParams();
        var used = Enumerable.Range(0, 16).Where(i => i < h.HeaterSpDegC.Length && h.HeaterSpDegC[i] > 0).ToArray();
        if (used.Length == 0) return;
        SetStep(at);
        bool Ok(MachineSnapshot s, int i) => Math.Abs(s.TempC[i] - h.HeaterSpDegC[i]) <= h.ToleranceC;
        if (used.All(i => Ok(_m.Snapshot, i))) return;   // 常駐加熱已經穩定在設定溫度

        SetStep($"{at}：等待溫度到位");
        var deadline = DateTime.Now.AddSeconds(st.TimeS);
        DateTime? inSince = null;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            CheckHealthy();
            var s = _m.Snapshot;
            if (used.All(i => Ok(s, i))) inSince ??= DateTime.Now; else inSince = null;
            if (inSince is { } t && (DateTime.Now - t).TotalSeconds >= h.SettleS) return;
            if (DateTime.Now > deadline)
            {
                var bad = used.Where(i => !Ok(s, i)).Select(i => $"#{i + 1} {s.TempC[i]:F1}/{h.HeaterSpDegC[i]:F0} °C");
                throw new SequenceException($"溫度沒有到位：{string.Join("、", bad)}（加熱在加熱頁設定，配方不會改加熱）");
            }
            await Task.Delay(_poll, ct);
        }
    }

    /// <summary>
    /// 套用這步的 MFC 設定。第一次用到氣體時，整個配方會用到的通道一起開上游閥
    /// （ALD 子步驟才切換的通道也先開，製程板切換時只改流量、不動閥）。
    /// </summary>
    private void ApplyMfc(ProcessRecipe r, ProcessStep st, double[] cur, bool[] opened)
    {
        var set = st.Mfc;
        for (int i = 0; i < 8; i++)
        {
            bool change = set is { } m && i < m.Length && m[i] is not null;
            if (change) cur[i] = set![i]!.Value;
            bool open = r.MfcUsed(i);
            if (change || (open && !opened[i])) _m.SetMfc(i, cur[i], open);
            if (open) opened[i] = true;
        }
    }

    private void GasOff(double[] cur, bool[] opened)
    {
        _m.SetAldValves(0);
        _m.SetAldPurge(false);
        for (int i = 0; i < 8; i++) { _m.SetMfc(i, 0, false); cur[i] = 0; opened[i] = false; }
    }

    /// <returns>這段跑完後的 MFC 設定（下一步的起點）</returns>
    private async Task<double[]> AldAsync(ProcessStep st, string at, double[] mfcStart, CancellationToken ct)
    {
        var ald = st.Ald!;
        if (ald.Name != st.Name) ald.Name = st.Name;
        SetStep($"{at}：{ald.Cycles} cycles");
        Require(_m.Snapshot.SlitClosed && _m.Snapshot.ArmClearOfSv, "Slit valve 未關或手臂未退出");
        if (ald.UsesRf) throw new SequenceException("配方有 RF 步驟，但 RF 控制還沒接上（硬體待確認）");
        if (ald.O3SetPct > 0) throw new SequenceException("配方有使用 O3，但 O3 產生器控制還沒接上（硬體待確認）");
        _m.SetAldPurge(true);        // MFC 匯流 → ALD 閥組的進氣閥，到 GasOff / 結束才關
        _m.DownloadAld(ald);
        await DelayAsync(0.3, ct);   // 讓步驟表經背板送到製程板
        _m.StartAld();
        await WaitAsync(s => s.AldRunning, 5, "ALD 沒有開始（主控板互鎖擋下或參數被拒）", ct);
        double timeout = st.TimeS > 0 ? st.TimeS : ald.TotalSeconds * 1.2 + 30;
        var deadline = DateTime.Now.AddSeconds(timeout);
        while (_m.Snapshot.AldRunning)
        {
            ct.ThrowIfCancellationRequested();
            CheckHealthy();
            SetStep($"{at}：cycle {_m.Snapshot.AldCycle}/{ald.Cycles}", quiet: true);
            if (DateTime.Now > deadline) throw new SequenceException($"{at} 執行逾時");
            await Task.Delay(_poll, ct);
        }
        if (_m.Snapshot.AldCycle < ald.Cycles)
            throw new SequenceException($"{at} 中斷在 cycle {_m.Snapshot.AldCycle}/{ald.Cycles}");

        // 子步驟切換過的 MFC：先把設定值改成最後的值，再下 STOP 解除製程板的覆寫，流量不會跳動
        var after = ald.MfcAfter(mfcStart);
        if (ald.UsesMfcSwitch)
            for (int i = 0; i < 8; i++)
                if (after[i] != mfcStart[i]) _m.SetMfc(i, after[i], true);
        _m.StopAld();
        return after;
    }

    private void EndProcess()
    {
        SetStep("製程結束");
        _m.SetAldValves(0);
        _m.SetAldPurge(false);
        for (int i = 0; i < 8; i++) _m.SetMfc(i, 0, false);   // 加熱維持常駐設定
    }

    private async Task VentAsync(ProcessStep st, string at, CancellationToken ct)
    {
        var v = st.Vent ?? new VentParams();
        SetStep($"{at}：關 GV、停 Turbo");
        Vac(VacuumOutputs.TurboGv | VacuumOutputs.ChamberRough, on: false);
        await WaitAsync(s => s.TurboGateClosed, 30, "Turbo gate valve 沒有關", ct);
        Vac(VacuumOutputs.TurboOn, on: false);
        await WaitAsync(s => !s.TurboAtSpeed, v.TurboSpinDownTimeoutS, "Turbo 沒有降速", ct);
        SetStep($"{at}：Vent");
        Vac(VacuumOutputs.ChamberVent, on: true);
        await WaitAsync(s => s.ChamberVenting, 10, "Vent 閥沒有打開（互鎖擋下？）", ct);
        await WaitAsync(s => s.ManometerTorr >= v.TargetTorr || s.ChamberGaugeStatus == GaugeStatus.OverRange,
            st.TimeS, "回填逾時", ct);
        Vac(VacuumOutputs.ChamberVent, on: false);
    }

    // ------------------------------------------------------------ 工具
    private void Vac(VacuumOutputs bits, bool on)
        => _m.SetVacuum(on ? _m.VacuumCommand | bits : _m.VacuumCommand & ~bits);

    private void SafeStop()
    {
        try
        {
            _m.StopAld();
            _m.SetAldValves(0);
            _m.SetAldPurge(false);
            for (int i = 0; i < 8; i++) _m.SetMfc(i, 0, false);
        }
        catch (Exception) { /* 通訊中斷時主控板看門狗會讓全機進安全狀態 */ }
    }

    private void CheckHealthy()
    {
        var s = _m.Snapshot;
        if (!s.AllOnline) throw new SequenceException("主控板或控制板離線");
        if (s.SafeState) throw new SequenceException("系統進入安全狀態");
    }

    private static void Require(bool ok, string why) { if (!ok) throw new SequenceException(why); }

    private async Task WaitAsync(Func<MachineSnapshot, bool> cond, double timeoutS, string failMsg, CancellationToken ct)
    {
        var deadline = DateTime.Now.AddSeconds(timeoutS);
        while (!cond(_m.Snapshot))
        {
            ct.ThrowIfCancellationRequested();
            CheckHealthy();
            if (DateTime.Now > deadline) throw new SequenceException(failMsg);
            await Task.Delay(_poll, ct);
        }
    }

    private async Task DelayAsync(double seconds, CancellationToken ct)
    {
        var end = DateTime.Now.AddSeconds(seconds);
        while (DateTime.Now < end) { CheckHealthy(); await Task.Delay(_poll, ct); }
    }

    private void SetStep(string step, bool quiet = false)
    {
        if (step == Step) return;
        Step = step;
        if (!quiet) Report(SequencerState.Running, step, "");
    }

    private void Report(SequencerState st, string step, string msg)
    {
        State = st;
        Step = step;
        Progress?.Invoke(new SequencerProgress(st, step, msg, DateTime.Now));
    }

    private sealed class SequenceException(string message) : Exception(message);
}
