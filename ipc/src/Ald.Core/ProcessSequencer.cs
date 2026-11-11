namespace Ald.Core;

/// <summary>
/// 整段製程配方：抽真空 → 確認溫度 → 通氣 → ALD 循環 →（可選）回填大氣。
/// 加熱是常駐設定（手動操作頁設好就不動），配方只檢查溫度到位，不會改加熱設定、製程中也不升降溫。
/// 時間單位都是秒，壓力 Torr，溫度 °C。數值待依實機調整。
/// </summary>
public sealed record ProcessRecipe
{
    public string Name { get; init; } = "";
    public bool PumpDown { get; init; } = true;
    /// <summary>粗抽到這個壓力後切 Turbo。</summary>
    public double CrossoverTorr { get; init; } = 0.5;
    /// <summary>Turbo 抽到這個壓力才開始製程。</summary>
    public double BaseTorr { get; init; } = 5e-3;
    public double PumpTimeoutS { get; init; } = 1800;
    public double TurboSpinUpTimeoutS { get; init; } = 900;

    /// <summary>
    /// 製程需要的溫度（0 = 不檢查）：只檢查、不改加熱設定。開始時已在 ±HeatToleranceC 內就直接往下；
    /// 不在範圍內（例如剛開加熱）就等它進範圍並維持 HeatSettleS，超過 HeatTimeoutS 失敗。
    /// </summary>
    public double[] HeaterSpDegC { get; init; } = new double[16];
    public double HeatToleranceC { get; init; } = 3;
    public double HeatSettleS { get; init; } = 60;
    public double HeatTimeoutS { get; init; } = 3600;

    /// <summary>製程中 MFC（config 單位），0 = 關。</summary>
    public double[] Mfc { get; init; } = new double[8];
    public double GasStabilizeS { get; init; } = 10;

    public AldRecipe? Ald { get; init; }

    public bool VentAfter { get; init; }
    public double VentTargetTorr { get; init; } = 740;
    public double VentTimeoutS { get; init; } = 600;
    public double TurboSpinDownTimeoutS { get; init; } = 1200;

    public IEnumerable<string> Validate()
    {
        if (HeaterSpDegC.Length != 16) yield return "HeaterSpDegC 需 16 組";
        if (Mfc.Length != 8) yield return "Mfc 需 8 組";
        if (!(BaseTorr > 0) || !(CrossoverTorr > BaseTorr)) yield return "壓力設定錯誤（需 CrossoverTorr > BaseTorr > 0）";
        if (Ald is not null) foreach (var e in Ald.Validate()) yield return "ALD：" + e;
    }
}

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
            if (r.PumpDown) await PumpDownAsync(r, token);
            await HeatAsync(r, token);
            await GasAsync(r, token);
            if (r.Ald is { } ald) await AldAsync(ald, token);
            await EndProcessAsync(r, token);
            if (r.VentAfter) await VentAsync(r, token);
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
    private async Task PumpDownAsync(ProcessRecipe r, CancellationToken ct)
    {
        SetStep("抽真空：Dry pump");
        Require(_m.Snapshot.SlitClosed, "Slit valve 未關");
        Vac(VacuumOutputs.DryPumpOn, on: true);
        Vac(VacuumOutputs.ChamberVent, on: false);
        await WaitAsync(s => s.DryPumpRunning, 60, "Dry pump 沒有啟動", ct);

        if (!(_m.Snapshot.ChamberPressureTorr < r.CrossoverTorr))
        {
            SetStep("抽真空：粗抽");
            Vac(VacuumOutputs.TurboGv, on: false);
            Vac(VacuumOutputs.ChamberRough, on: true);
            await WaitAsync(s => s.ChamberPressureTorr < r.CrossoverTorr, r.PumpTimeoutS,
                $"粗抽 {r.PumpTimeoutS:F0} s 內沒有低於 {r.CrossoverTorr} Torr（漏氣？）", ct);
        }

        SetStep("抽真空：Turbo 啟動");
        Vac(VacuumOutputs.TurboRough | VacuumOutputs.TurboOn, on: true);
        await WaitAsync(s => s.TurboAtSpeed, r.TurboSpinUpTimeoutS, "Turbo 沒有到達轉速", ct);

        SetStep("抽真空：開 Turbo GV");
        Vac(VacuumOutputs.ChamberRough, on: false);
        Vac(VacuumOutputs.TurboGv, on: true);
        await WaitAsync(s => s.TurboGateOpened, 30, "Turbo gate valve 沒有打開（互鎖擋下？）", ct);
        await WaitAsync(s => s.ChamberPressureTorr < r.BaseTorr, r.PumpTimeoutS,
            $"沒有抽到 base pressure {r.BaseTorr:G3} Torr", ct);
    }

    private async Task HeatAsync(ProcessRecipe r, CancellationToken ct)
    {
        var used = Enumerable.Range(0, 16).Where(i => r.HeaterSpDegC[i] > 0).ToArray();
        if (used.Length == 0) return;
        SetStep("確認溫度");
        bool Ok(MachineSnapshot s, int i) => Math.Abs(s.TempC[i] - r.HeaterSpDegC[i]) <= r.HeatToleranceC;
        if (used.All(i => Ok(_m.Snapshot, i))) return;   // 常駐加熱已經穩定在設定溫度

        SetStep("等待溫度到位");
        var deadline = DateTime.Now.AddSeconds(r.HeatTimeoutS);
        DateTime? inSince = null;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            CheckHealthy();
            var s = _m.Snapshot;
            if (used.All(i => Ok(s, i))) inSince ??= DateTime.Now; else inSince = null;
            if (inSince is { } t && (DateTime.Now - t).TotalSeconds >= r.HeatSettleS) return;
            if (DateTime.Now > deadline)
            {
                var bad = used.Where(i => !Ok(s, i)).Select(i => $"#{i + 1} {s.TempC[i]:F1}/{r.HeaterSpDegC[i]:F0} °C");
                throw new SequenceException($"溫度沒有到位：{string.Join("、", bad)}（加熱在手動操作頁設定，配方不會改加熱）");
            }
            await Task.Delay(_poll, ct);
        }
    }

    private async Task GasAsync(ProcessRecipe r, CancellationToken ct)
    {
        if (r.Mfc.All(v => v <= 0)) return;
        SetStep("通氣穩定");
        for (int i = 0; i < 8; i++) _m.SetMfc(i, r.Mfc[i], r.Mfc[i] > 0);
        await DelayAsync(r.GasStabilizeS, ct);
    }

    private async Task AldAsync(AldRecipe ald, CancellationToken ct)
    {
        SetStep($"ALD：{ald.Cycles} cycles");
        Require(_m.Snapshot.SlitClosed && _m.Snapshot.ArmClearOfSv, "Slit valve 未關或手臂未退出");
        _m.DownloadAld(ald);
        await DelayAsync(0.3, ct);   // 讓步驟表經背板送到製程板
        _m.StartAld();
        await WaitAsync(s => s.AldRunning, 5, "ALD 沒有開始（主控板互鎖擋下或參數被拒）", ct);
        double cycleS = ald.Steps.Sum(x => x.DurationMs) / 1000.0;
        double timeout = ald.Cycles * cycleS * 1.2 + 30;
        var deadline = DateTime.Now.AddSeconds(timeout);
        while (_m.Snapshot.AldRunning)
        {
            ct.ThrowIfCancellationRequested();
            CheckHealthy();
            SetStep($"ALD：cycle {_m.Snapshot.AldCycle}/{ald.Cycles}", quiet: true);
            if (DateTime.Now > deadline) throw new SequenceException("ALD 執行逾時");
            await Task.Delay(_poll, ct);
        }
        if (_m.Snapshot.AldCycle < ald.Cycles)
            throw new SequenceException($"ALD 中斷在 cycle {_m.Snapshot.AldCycle}/{ald.Cycles}");
    }

    private Task EndProcessAsync(ProcessRecipe r, CancellationToken ct)
    {
        SetStep("製程結束");
        _m.SetAldValves(0);
        for (int i = 0; i < 8; i++) _m.SetMfc(i, 0, false);
        return Task.CompletedTask;   // 加熱維持常駐設定
    }

    private async Task VentAsync(ProcessRecipe r, CancellationToken ct)
    {
        SetStep("回填：關 GV、停 Turbo");
        Vac(VacuumOutputs.TurboGv | VacuumOutputs.ChamberRough, on: false);
        await WaitAsync(s => s.TurboGateClosed, 30, "Turbo gate valve 沒有關", ct);
        Vac(VacuumOutputs.TurboOn, on: false);
        await WaitAsync(s => !s.TurboAtSpeed, r.TurboSpinDownTimeoutS, "Turbo 沒有降速", ct);
        SetStep("回填：Vent");
        Vac(VacuumOutputs.ChamberVent, on: true);
        await WaitAsync(s => s.ChamberVenting, 10, "Vent 閥沒有打開（互鎖擋下？）", ct);
        await WaitAsync(s => s.ManometerTorr >= r.VentTargetTorr || s.ChamberGaugeStatus == GaugeStatus.OverRange,
            r.VentTimeoutS, "回填逾時", ct);
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
