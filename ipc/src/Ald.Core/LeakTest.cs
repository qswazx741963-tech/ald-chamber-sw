namespace Ald.Core;

/// <summary>一次漏率測試的結果。</summary>
/// <param name="RorMtorrPerMin">壓升速率（mTorr/min），最小平方法直線斜率</param>
/// <param name="LeakTorrLps">換算漏氣量 Q = 壓升速率 × 腔體體積（Torr·L/s）；1 Torr·L/s = 1.333 mbar·L/s</param>
/// <param name="R2">直線擬合度：接近 1 = 穩定漏氣；明顯偏低 = 放氣 / 壓力計不穩，結果要打折看</param>
public sealed record LeakTestResult(
    DateTime Time, string Gauge, double StartTorr, double EndTorr, double MeasureS, int Samples,
    double RorMtorrPerMin, double LeakTorrLps, double R2, double LimitMtorrPerMin, bool Pass, string Note)
{
    public double LeakMbarLps => LeakTorrLps * 1.33322;

    public string Text() =>
        $"{(Pass ? "合格" : "不合格")}  壓升 {RorMtorrPerMin:0.000} mTorr/min（上限 {LimitMtorrPerMin:0.###}）  " +
        $"漏氣量 {LeakTorrLps:0.00E+00} Torr·L/s（{LeakMbarLps:0.00E+00} mbar·L/s）  " +
        $"{StartTorr:0.00E+00} → {EndTorr:0.00E+00} Torr / {MeasureS:0} s  R² {R2:0.000}" +
        (Note.Length > 0 ? $"  {Note}" : "");
}

/// <summary>壓升資料的直線擬合。</summary>
public static class LeakMath
{
    /// <summary>最小平方法 p = a + b·t，回傳斜率 b（Torr/s）與 R²。少於 2 點回 NaN。</summary>
    public static (double Slope, double R2) Fit(IReadOnlyList<(double T, double P)> pts)
    {
        int n = pts.Count;
        if (n < 2) return (double.NaN, double.NaN);
        double mt = pts.Average(x => x.T), mp = pts.Average(x => x.P);
        double stt = 0, stp = 0, spp = 0;
        foreach (var (t, p) in pts) { stt += (t - mt) * (t - mt); stp += (t - mt) * (p - mp); spp += (p - mp) * (p - mp); }
        if (stt <= 0) return (double.NaN, double.NaN);
        double b = stp / stt;
        double r2 = spp <= 0 ? 1 : stp * stp / (stt * spp);
        return (b, r2);
    }

    /// <summary>某顆壓力計目前讀值（NaN = 沒有 / 讀不到）。</summary>
    public static double Read(MachineSnapshot s, string gauge) => gauge.ToLowerInvariant() switch
    {
        "chamberiongauge" => s.IonGaugeTorr,
        "chambermanometer" => s.ManometerTorr,
        "chamberconvectron" => s.ChamberPressureTorr,
        _ => double.NaN,
    };
}

/// <summary>
/// 漏率測試（壓升法 rate-of-rise）：關閉氣體 → 抽到 startBelowTorr → 再抽 pumpS →
/// 關 Turbo GV 隔離 PM（Turbo 保持運轉）→ 等 settleS → 記錄 measureS 秒壓力 → 直線擬合算壓升速率 →
/// 重新開 GV 抽回去。量到的是「真漏 + 壁面放氣」的總和，所以每次條件（溫度、抽氣時間）要一樣才能比較。
/// 失敗或中止：關氣體，GV 能開就開回去（主控板互鎖會再擋一次），不會 vent。
/// </summary>
public sealed class LeakTestSequencer
{
    private readonly IMachine _m;
    private readonly LeakTestConfig _cfg;
    private readonly TimeSpan _poll;
    private CancellationTokenSource? _cts;
    private ProcessSequencer? _pump;
    private readonly List<(double T, double P)> _samples = new();

    public SequencerState State { get; private set; } = SequencerState.Idle;
    public string Step { get; private set; } = "";
    public LeakTestResult? Result { get; private set; }
    public event Action<SequencerProgress>? Progress;

    public LeakTestSequencer(IMachine machine, LeakTestConfig cfg, TimeSpan? poll = null)
    {
        _m = machine;
        _cfg = cfg;
        _poll = poll ?? TimeSpan.FromMilliseconds(500);
    }

    /// <summary>目前已記錄的壓升點（秒, Torr），畫圖用。</summary>
    public IReadOnlyList<(double T, double P)> Samples { get { lock (_samples) return _samples.ToList(); } }

    public void Abort() { _cts?.Cancel(); _pump?.Abort(); }

    public static IEnumerable<string> Precheck(MachineSnapshot s, LeakTestConfig cfg)
    {
        if (!s.AllOnline) yield return "主控板或控制板離線";
        if (s.SafeState) yield return "系統在安全狀態，請先 Reset";
        if (s.AldRunning) yield return "ALD 執行中";
        if (!s.SlitClosed) yield return "Slit valve 未關（或 Loadlock 未連線）";
        if (!s.ChamberLidClosed) yield return "PM 上蓋未關";
        if (double.IsNaN(LeakMath.Read(s, cfg.Gauge))) yield return $"壓力計 {cfg.Gauge} 讀不到";
    }

    public async Task<SequencerState> RunAsync(CancellationToken ct = default)
    {
        if (State == SequencerState.Running) throw new InvalidOperationException("漏率測試執行中");
        Result = null;
        lock (_samples) _samples.Clear();
        var pre = Precheck(_m.Snapshot, _cfg).ToList();
        if (pre.Count > 0) { Report(SequencerState.Failed, "檢查", string.Join("；", pre)); return State; }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = _cts.Token;
        bool isolated = false;
        Report(SequencerState.Running, "開始", $"量測 {_cfg.MeasureS:0} s，上限 {_cfg.LimitMtorrPerMin:0.###} mTorr/min");
        try
        {
            SetStep("關閉 ALD 閥與 MFC");
            _m.SetAldValves(0);
            for (int i = 0; i < 8; i++) _m.SetMfc(i, 0, false);

            var s = _m.Snapshot;
            if (!(Gauge(s) < _cfg.StartBelowTorr) || !s.TurboGateOpened)
            {
                SetStep($"抽真空到 {_cfg.StartBelowTorr:0.0E+00} Torr");
                _pump = new ProcessSequencer(_m, _poll);
                _pump.Progress += p => { if (p.State == SequencerState.Running && p.Step.StartsWith("抽真空")) SetStep(p.Step); };
                var st = await _pump.RunAsync(new ProcessRecipe
                {
                    Name = "漏率測試抽真空", PumpDown = true, CrossoverTorr = _cfg.CrossoverTorr,
                    BaseTorr = _cfg.StartBelowTorr, PumpTimeoutS = _cfg.PumpTimeoutS,
                }, token);
                token.ThrowIfCancellationRequested();
                if (st != SequencerState.Done) throw new LeakTestException($"抽真空沒有完成（{_pump.Step}）");
                await WaitAsync(x => Gauge(x) < _cfg.StartBelowTorr, _cfg.PumpTimeoutS,
                    $"{_cfg.Gauge} 沒有低於 {_cfg.StartBelowTorr:0.0E+00} Torr", token);
            }

            SetStep($"持續抽氣 {_cfg.PumpS:0} s");
            await DelayAsync(_cfg.PumpS, token);

            SetStep("隔離：關 Turbo GV");
            isolated = true;
            Vac(VacuumOutputs.TurboGv | VacuumOutputs.ChamberRough | VacuumOutputs.ChamberVent, on: false);
            await WaitAsync(x => x.TurboGateClosed, 30, "Turbo gate valve 沒有關", token);

            SetStep($"穩定 {_cfg.SettleS:0} s");
            await DelayAsync(_cfg.SettleS, token);

            SetStep($"量測壓升 {_cfg.MeasureS:0} s");
            var t0 = DateTime.Now;
            string note = "";
            while (true)
            {
                token.ThrowIfCancellationRequested();
                CheckHealthy();
                if (!_m.Snapshot.TurboGateClosed) throw new LeakTestException("量測中 Turbo GV 被打開");
                double t = (DateTime.Now - t0).TotalSeconds;
                double p = Gauge(_m.Snapshot);
                if (!double.IsNaN(p) && p > 0) lock (_samples) _samples.Add((t, p));
                if (p > _cfg.AbortAboveTorr) { note = $"壓力超過 {_cfg.AbortAboveTorr:0.0E+00} Torr，提前結束（大漏）"; break; }
                if (t >= _cfg.MeasureS) break;
                await Task.Delay(_poll, token);
            }
            Result = Evaluate(Samples, note);

            SetStep("恢復抽氣：開 Turbo GV");
            await RestoreAsync(token);
            isolated = false;
            Report(SequencerState.Done, "完成", Result.Text());
        }
        catch (OperationCanceledException)
        {
            Cleanup(isolated);
            Report(SequencerState.Aborted, Step, "使用者中止");
        }
        catch (Exception e)
        {
            Cleanup(isolated);
            Report(SequencerState.Failed, Step, e.Message);
        }
        return State;
    }

    private LeakTestResult Evaluate(IReadOnlyList<(double T, double P)> pts, string note)
    {
        if (pts.Count < 3)
            return new LeakTestResult(DateTime.Now, _cfg.Gauge, double.NaN, double.NaN, 0, pts.Count, double.NaN, double.NaN, double.NaN,
                _cfg.LimitMtorrPerMin, false, (note + " 有效點數不足，無法判定").Trim());
        var (slope, r2) = LeakMath.Fit(pts);
        double ror = slope * 60_000;   // Torr/s → mTorr/min
        bool gross = note.Length > 0;
        return new LeakTestResult(DateTime.Now, _cfg.Gauge, pts[0].P, pts[^1].P, pts[^1].T, pts.Count,
            ror, slope * _cfg.ChamberVolumeL, r2, _cfg.LimitMtorrPerMin, !gross && ror <= _cfg.LimitMtorrPerMin, note);
    }

    /// <summary>Turbo 還在全速就開回 GV（主控板會檢查壓差 / 互鎖）。</summary>
    private async Task RestoreAsync(CancellationToken ct)
    {
        if (!_m.Snapshot.TurboAtSpeed) return;
        Vac(VacuumOutputs.TurboGv, on: true);
        await WaitAsync(x => x.TurboGateOpened, 30, "Turbo gate valve 沒有打開（互鎖擋下？）", ct);
    }

    private void Cleanup(bool isolated)
    {
        try
        {
            _m.SetAldValves(0);
            for (int i = 0; i < 8; i++) _m.SetMfc(i, 0, false);
            if (isolated && _m.Snapshot.TurboAtSpeed && !_m.Snapshot.SafeState) Vac(VacuumOutputs.TurboGv, on: true);
        }
        catch (Exception) { /* 通訊中斷時主控板看門狗會讓全機進安全狀態 */ }
    }

    private double Gauge(MachineSnapshot s) => LeakMath.Read(s, _cfg.Gauge);

    private void Vac(VacuumOutputs bits, bool on)
        => _m.SetVacuum(on ? _m.VacuumCommand | bits : _m.VacuumCommand & ~bits);

    private void CheckHealthy()
    {
        var s = _m.Snapshot;
        if (!s.AllOnline) throw new LeakTestException("主控板或控制板離線");
        if (s.SafeState) throw new LeakTestException("系統進入安全狀態");
    }

    private async Task WaitAsync(Func<MachineSnapshot, bool> cond, double timeoutS, string failMsg, CancellationToken ct)
    {
        var deadline = DateTime.Now.AddSeconds(timeoutS);
        while (!cond(_m.Snapshot))
        {
            ct.ThrowIfCancellationRequested();
            CheckHealthy();
            if (DateTime.Now > deadline) throw new LeakTestException(failMsg);
            await Task.Delay(_poll, ct);
        }
    }

    private async Task DelayAsync(double seconds, CancellationToken ct)
    {
        var end = DateTime.Now.AddSeconds(seconds);
        while (DateTime.Now < end) { CheckHealthy(); await Task.Delay(_poll, ct); }
    }

    private void SetStep(string step)
    {
        if (step == Step) return;
        Report(SequencerState.Running, step, "");
    }

    private void Report(SequencerState st, string step, string msg)
    {
        State = st;
        Step = step;
        Progress?.Invoke(new SequencerProgress(st, step, msg, DateTime.Now));
    }

    private sealed class LeakTestException(string message) : Exception(message);
}
