using System.Text;
using Ald.Comm;
using Ald.Core;
using Ald.Data;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace Ald.Hmi;

/// <summary>
/// 主畫面（底層版）：畫面只讀 MachineService.Snapshot（每 200 ms 更新一次），按鈕只呼叫 MachineService / ProcessSequencer。
/// 畫面不做任何卡控判斷以外的邏輯；被擋下的命令顯示原因。
/// </summary>
public partial class MainWindow : Window
{
    private readonly MachineService? _m;
    private readonly AlarmLog? _alarmLog;
    private readonly TrendLog? _trend;
    private ProcessSequencer? _seq;
    private string _status = "";
    private readonly string _recipeDir;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private static readonly string[] SvText = { "未知", "關", "開啟中", "開", "關閉中", "錯誤" };

    public MainWindow() : this(null, null, null, null, null, null, "recipes") { }

    public MainWindow(MachineService? machine, AlarmLog? alarmLog, TrendLog? trend, UserStore? users, MaintenanceStore? maint, RunLog? runs, string recipeDir)
    {
        AvaloniaXamlLoader.Load(this);
        _m = machine;
        _alarmLog = alarmLog;
        _trend = trend;
        _recipeDir = recipeDir;
        _users = users;
        _maint = maint;
        _runs = runs;
        Wire();
        WireAccess();
        WireTrend();
        WireTransfer();
        WireManual();
        WireRecipe();
        WireMaint();
        WireTower();
        WireBatch();
        WireEvents();
        WireHeater();
        WireHistory();
        WireHeader();
        _timer.Tick += (_, _) => Render();
        _timer.Start();
        Closed += (_, _) => _timer.Stop();
    }

    private T C<T>(string name) where T : Control => this.FindControl<T>(name) ?? throw new InvalidOperationException(name);

    private void Wire()
    {
        if (_m is null) return;
        C<Button>("BtnReset").Click += async (_, _) => await Do("Reset", () => _m.ResetAsync());
        C<Button>("BtnSafe").Click += (_, _) => _ = Do("安全停止", () => { AbortAll(); _m.EnterSafe(); return Task.CompletedTask; });
        C<Button>("BtnPump").Click += (_, _) => RunRecipe(new ProcessRecipe { Name = "抽真空" });
        C<Button>("BtnVent").Click += (_, _) => RunRecipe(new ProcessRecipe { Name = "回填大氣", PumpDown = false, VentAfter = true });
        C<Button>("BtnAbort").Click += (_, _) => AbortAll();
        C<Button>("BtnLlPump").Click += (_, _) => Ll(LoadlockCommand.Pump);
        C<Button>("BtnLlVent").Click += (_, _) => Ll(LoadlockCommand.Vent);
        C<Button>("BtnSvOpen").Click += (_, _) => Ll(LoadlockCommand.SvOpen);
        C<Button>("BtnSvClose").Click += (_, _) => Ll(LoadlockCommand.SvClose);
    }

    private async Task Do(string what, Func<Task> act)
    {
        try { await act(); _status = $"{what}：已送出"; _alarmLog?.Add(OpSource, "Info", what, evt: "OPERATOR"); }
        catch (Exception e) { _status = $"{what}：失敗（{e.Message}）"; }
    }

    private void Ll(LoadlockCommand cmd)
    {
        if (_m is null) return;
        var r = _m.Loadlock.Execute(cmd);
        _status = r.Allowed ? $"Loadlock {cmd}：已送出" : $"Loadlock {cmd} 被擋：{r.Reason}";
        _alarmLog?.Add(OpSource, "Info", _status, evt: "OPERATOR");
    }

    private AutoRunJob? _auto;
    private bool _runIsAuto;
    private bool SequenceRunning => _seq?.State == SequencerState.Running || _auto?.State == SequencerState.Running;
    private SequencerState? RunState => _runIsAuto ? _auto?.State : _seq?.State;
    private string RunStep => (_runIsAuto ? _auto?.Step : _seq?.Step) ?? "";

    /// <summary>中止所有流程（安全停止、中止按鈕）。</summary>
    private void AbortAll() { _seq?.Abort(); _auto?.Abort(); _xfer?.Abort(); _leak?.Abort(); _batch?.Abort(); }

    private void LogRun(string name, SequencerProgress p)
    {
        _status = $"{name}：{p.Step} {p.Message}";
        lock (_runLog) _runLog.Insert(0, $"{p.Time:HH:mm:ss} {p.Step} {p.Message}".TrimEnd());
        _alarmLog?.Add("流程", p.State == SequencerState.Failed ? "Alarm" : "Info", $"{name}：{p.Step} {p.Message}".Trim(), evt: "STEP");
    }

    /// <summary>自動跑片：送片 → 製程 → 取片 →（配方要求）回填。</summary>
    private async void RunAuto(ProcessRecipe r, string waferId = "")
    {
        if (_m is null || _wafers is null || Busy) { _status = "已有流程在執行"; return; }
        _needOp = false;
        _auto = new AutoRunJob(_m, _wafers, _m.Config.Transfer, _m.Config.Interlock.LoadlockZEnabled);
        _runIsAuto = true;
        _runName = $"自動跑片 {r.Name}";
        lock (_runLog) _runLog.Clear();
        string fail = "";
        _auto.Progress += p => { LogRun(_runName, p); TowerEvent(p, doneNeedsOperator: true); if (p.State == SequencerState.Failed) fail = $"{p.Step} {p.Message}".Trim(); };
        _alarmLog?.Add(OpSource, "Info", _runName, evt: "OPERATOR");
        var job = _auto;
        long? id = RunStart("自動跑片", waferId, r);
        if (waferId.Length > 0) _wafers.SetIds(waferId, null);
        await job.RunAsync(r);
        RunEnd(id, job.State, fail);
    }

    /// <param name="waferId">null = 不記履歷（總覽的抽真空 / 回填）</param>
    private async void RunRecipe(ProcessRecipe r, string? waferId = null)
    {
        if (_m is null || Busy) { _status = "已有流程在執行"; return; }
        _needOp = false;
        _seq = new ProcessSequencer(_m);
        _runIsAuto = false;
        _runName = r.Name;
        lock (_runLog) _runLog.Clear();
        string fail = "";
        _seq.Progress += p => { LogRun($"流程 {r.Name}", p); TowerEvent(p, doneNeedsOperator: false); if (p.State == SequencerState.Failed) fail = $"{p.Step} {p.Message}".Trim(); };
        var seq = _seq;
        long? id = waferId is null ? null : RunStart("製程", waferId, r);
        if (waferId is { Length: > 0 }) _wafers?.SetIds(null, waferId);
        await seq.RunAsync(r);
        RunEnd(id, seq.State, fail);
    }

    private static string P(double torr) => double.IsNaN(torr) ? "  ---   " : torr.ToString("0.00E+00");

    /// <summary>PM 互鎖壓力暫存器是整數 mTorr，低於 1 mTorr 讀到 0 → 顯示「&lt; 1E-03」而不是 0。</summary>
    private static string PmP(MachineSnapshot s)
        => s.ChamberPressureTorr == 0 ? "< 1.0E-03"
         : s.ChamberGaugeStatus == GaugeStatus.UnderRange ? "<" + P(s.ChamberPressureTorr)
         : P(s.ChamberPressureTorr);

    private void Render()
    {
        C<TextBlock>("TxtClock").Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        if (_m is null) { C<TextBlock>("TxtState").Text = "未設定主控板"; return; }
        var s = _m.Snapshot;
        RenderOverview(s);

        C<ListBox>("LstActive").ItemsSource = _m.Alarms.Active.OrderByDescending(a => a.Info.Level)
            .Select(a => $"{(a.Acked ? "  " : "● ")}[{a.Info.Level}] {a.Since:HH:mm:ss} {a.Info.Source}：{a.Info.Message}{(a.Acked ? "" : "（未確認）")}")
            .Concat(_m.Alarms.Latched.Select(a => $"● [已消失] {a.Since:HH:mm:ss} {a.Info.Source}：{a.Info.Message}（未確認）"))
            .Concat(_seqFault ? new[] { "● [流程] 流程異常停止（未確認，詳見事件記錄）" } : Array.Empty<string>())
            .ToList();
        RenderEvents();
        C<TextBlock>("TxtStatusBar").Text = _status;
        RenderManual(s);
        RenderRecipe();
        RenderAccess();
        RenderTrend();
        RenderTransfer(s);
        RenderMaint(s);
        RenderBatch();
        RenderHeater(s);
        RenderHistory();
        RenderTower(s);
    }
}
