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

    public MainWindow() : this(null, null, null, null, null, "recipes") { }

    public MainWindow(MachineService? machine, AlarmLog? alarmLog, TrendLog? trend, UserStore? users, MaintenanceStore? maint, string recipeDir)
    {
        AvaloniaXamlLoader.Load(this);
        _m = machine;
        _alarmLog = alarmLog;
        _trend = trend;
        _recipeDir = recipeDir;
        _users = users;
        _maint = maint;
        Wire();
        WireAccess();
        WireTrend();
        WireTransfer();
        WireManual();
        WireRecipe();
        WireMaint();
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
    private void AbortAll() { _seq?.Abort(); _auto?.Abort(); _xfer?.Abort(); _leak?.Abort(); }

    private void LogRun(string name, SequencerProgress p)
    {
        _status = $"{name}：{p.Step} {p.Message}";
        lock (_runLog) _runLog.Insert(0, $"{p.Time:HH:mm:ss} {p.Step} {p.Message}".TrimEnd());
        _alarmLog?.Add("流程", p.State == SequencerState.Failed ? "Alarm" : "Info", $"{name}：{p.Step} {p.Message}".Trim(), evt: "STEP");
    }

    /// <summary>自動跑片：送片 → 製程 → 取片 →（配方要求）回填。</summary>
    private async void RunAuto(ProcessRecipe r)
    {
        if (_m is null || _wafers is null || Busy) { _status = "已有流程在執行"; return; }
        _auto = new AutoRunJob(_m, _wafers, _m.Config.Transfer, _m.Config.Interlock.LoadlockZEnabled);
        _runIsAuto = true;
        _runName = $"自動跑片 {r.Name}";
        lock (_runLog) _runLog.Clear();
        _auto.Progress += p => LogRun(_runName, p);
        _alarmLog?.Add(OpSource, "Info", _runName, evt: "OPERATOR");
        await _auto.RunAsync(r);
    }

    private async void RunRecipe(ProcessRecipe r)
    {
        if (_m is null || Busy) { _status = "已有流程在執行"; return; }
        _seq = new ProcessSequencer(_m);
        _runIsAuto = false;
        _runName = r.Name;
        lock (_runLog) _runLog.Clear();
        _seq.Progress += p => LogRun($"流程 {r.Name}", p);
        await _seq.RunAsync(r);
    }

    private static string P(double torr) => double.IsNaN(torr) ? "  ---   " : torr.ToString("0.00E+00");

    /// <summary>PM 互鎖壓力暫存器是整數 mTorr，低於 1 mTorr 讀到 0 → 顯示「&lt; 1E-03」而不是 0。</summary>
    private static string PmP(MachineSnapshot s)
        => s.ChamberPressureTorr == 0 ? "< 1.0E-03"
         : s.ChamberGaugeStatus == GaugeStatus.UnderRange ? "<" + P(s.ChamberPressureTorr)
         : P(s.ChamberPressureTorr);

    private void Render()
    {
        if (_m is null) { C<TextBlock>("TxtLink").Text = "主控板：未設定"; return; }
        var s = _m.Snapshot;
        C<TextBlock>("TxtLink").Text = s.Connected ? "主控板：連線" : "主控板：未連線";
        C<TextBlock>("TxtState").Text = $"狀態：{s.SysState}";
        C<TextBlock>("TxtSlots").Text = "控制板：" + string.Join(" ", s.SlotOnline.Select((o, i) => $"{i}{(o ? "●" : "○")}"));
        C<TextBlock>("TxtLl").Text = s.LoadlockFresh ? $"Loadlock：SV {SvText[(int)s.SlitValve % 6]}" : "Loadlock：未連線";

        var v = new StringBuilder();
        v.AppendLine($"PM 互鎖壓力  {PmP(s)} Torr  ({s.ChamberGaugeStatus})");
        v.AppendLine($"Barocel      {P(s.ManometerTorr)} Torr");
        v.AppendLine($"Ion gauge    {P(s.IonGaugeTorr)} Torr");
        v.AppendLine($"Foreline     {P(s.ForelineTorr)} Torr");
        v.AppendLine($"Dry pump {(s.DryPumpRunning ? "運轉" : "停")}  Turbo {(s.TurboAtSpeed ? "全速" : "未到速")}");
        v.AppendLine($"Turbo GV {(s.TurboGateOpened ? "開" : s.TurboGateClosed ? "關" : "動作中")}  輸出 {s.VacuumOutputs}");
        C<TextBlock>("TxtVac").Text = v.ToString();

        var l = new StringBuilder();
        string llp = s.LoadlockPressureUnderRange ? "< 1 Torr" : double.IsNaN(s.LoadlockPressureTorr) ? "---" : $"{s.LoadlockPressureTorr:0.0} Torr";
        l.AppendLine($"壓力 {llp}{(s.LoadlockPressureSuspect ? "（不可信）" : "")}");
        l.AppendLine($"SV {SvText[(int)s.SlitValve % 6]}  手臂 {s.ArmPosSteps} step  Z {s.ArmZ}");
        l.AppendLine($"粗抽 {(s.LoadlockRoughOpen ? "開" : "關")}  洩壓 {(s.LoadlockVentOpen ? "開" : "關")}  警報 {s.LoadlockAlarm}");
        C<TextBlock>("TxtLlDetail").Text = l.ToString();

        var h = new StringBuilder();
        var names = _m.Config.Heaters.Channels;
        for (int i = 0; i < 16; i++)
        {
            string t = double.IsNaN(s.TempC[i]) ? "  斷線" : $"{s.TempC[i],6:0.0}";
            h.AppendLine($"{names[i].Name,-17}{t} °C {s.HeaterDutyPct[i],5:0}%");
        }
        C<TextBlock>("TxtHeat").Text = h.ToString();

        var g = new StringBuilder();
        var mfc = _m.Config.Mfc.Channels;
        for (int i = 0; i < 8; i++)
            g.AppendLine($"{i + 1} {mfc[i].Name,-18}{s.MfcValue[i],8:0.0} {mfc[i].Unit}{((s.MfcUpstreamValves >> i & 1) != 0 ? " ●" : "")}");
        C<TextBlock>("TxtMfc").Text = g.ToString();

        C<TextBlock>("TxtMech").Text =
            $"Heater module {s.HeaterPosSteps} step ({s.HeaterAxis}){(s.HeaterDown ? " 下位" : s.HeaterAtProcess ? " Process" : "")}\n" +
            $"Wafer lift {(s.WaferLiftUp ? "上" : s.WaferLiftDown ? "下" : "動作中")}  交接位置 {(s.PmAtTransfer ? "是" : "否")}\n" +
            $"ALD {(s.AldRunning ? "執行中" : "停")}  cycle {s.AldCycle}  閥 0x{s.AldValves:X2}";

        C<ListBox>("LstActive").ItemsSource = _m.Alarms.Active.OrderByDescending(a => a.Info.Level)
            .Select(a => $"[{a.Info.Level}] {a.Since:HH:mm:ss} {a.Info.Source}：{a.Info.Message}").ToList();
        if (_alarmLog is not null && DateTime.Now.Second % 2 == 0)
            C<ListBox>("LstLog").ItemsSource = _alarmLog.Recent(200).Select(r => $"{r.Time:MM-dd HH:mm:ss} {r.Event,-8} {r.Source}：{r.Message}").ToList();
        C<TextBlock>("TxtStatusBar").Text = _status;
        RenderManual(s);
        RenderRecipe();
        RenderAccess();
        RenderTrend();
        RenderTransfer(s);
        RenderMaint(s);
    }
}
