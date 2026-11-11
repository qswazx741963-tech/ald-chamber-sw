using Ald.Core;
using Avalonia.Controls;
using Avalonia.Media;

namespace Ald.Hmi;

/// <summary>
/// 傳片頁：晶圓位置（軟體追蹤）、送片 / 取片（TransferSequencer）、中止、工程師修正位置。
/// 傳片與配方流程互斥；傳片中手動操作頁也鎖住。
/// </summary>
public partial class MainWindow
{
    private WaferMap? _wafers;
    private TransferSequencer? _xfer;
    private readonly List<string> _xferLog = new();
    private int _xferLogShown = -1;
    private static readonly bool?[] WaferChoices = { true, false, null };

    private bool XferRunning => _xfer?.State == SequencerState.Running;
    private bool Busy => SequenceRunning || XferRunning || LeakRunning;

    private void WireTransfer()
    {
        if (_m is null) return;
        _wafers = WaferMap.Load(_m.Config.Transfer.WaferMapPath);
        _wafers.Changed += w => _alarmLog?.Add("晶圓", "Info",
            $"Loadlock {WaferMap.Text(w.Loadlock)}、PM {WaferMap.Text(w.Pm)}（{w.Note}）", evt: "WAFER");

        C<Button>("BtnXferLoad").Click += (_, _) => RunTransfer(TransferDirection.LoadToPm);
        C<Button>("BtnXferUnload").Click += (_, _) => RunTransfer(TransferDirection.UnloadToLoadlock);
        C<Button>("BtnXferAbort").Click += (_, _) => _xfer?.Abort();

        foreach (var n in new[] { "CmbWaferLl", "CmbWaferPm" })
            C<ComboBox>(n).ItemsSource = WaferChoices.Select(WaferMap.Text).ToList();
        C<ComboBox>("CmbWaferLl").SelectedIndex = Array.IndexOf(WaferChoices, _wafers.Loadlock);
        C<ComboBox>("CmbWaferPm").SelectedIndex = Array.IndexOf(WaferChoices, _wafers.Pm);
        C<Button>("BtnWaferSet").Click += (_, _) =>
        {
            var ll = WaferChoices[Math.Max(0, C<ComboBox>("CmbWaferLl").SelectedIndex)];
            var pm = WaferChoices[Math.Max(0, C<ComboBox>("CmbWaferPm").SelectedIndex)];
            Cmd($"晶圓位置設為 Loadlock {WaferMap.Text(ll)}、PM {WaferMap.Text(pm)}", () => _wafers.Set(ll, pm, $"{_user} 手動設定"));
        };
    }

    private async void RunTransfer(TransferDirection dir)
    {
        if (_m is null || _wafers is null || Busy) { _status = "已有流程在執行"; return; }
        _xfer = new TransferSequencer(_m, _wafers, _m.Config.Transfer, _m.Config.Interlock.LoadlockZEnabled);
        lock (_xferLog) _xferLog.Clear();
        _needOp = false;
        _xfer.Progress += p =>
        {
            TowerEvent(p, doneNeedsOperator: dir == TransferDirection.UnloadToLoadlock);
            _status = $"傳片：{p.Step} {p.Message}";
            lock (_xferLog) _xferLog.Insert(0, $"{p.Time:HH:mm:ss} {p.Step} {p.Message}".TrimEnd());
            _alarmLog?.Add("傳片", p.State == SequencerState.Failed ? "Alarm" : "Info", $"{p.Step} {p.Message}".Trim(), evt: "STEP");
        };
        _alarmLog?.Add(OpSource, "Info", dir == TransferDirection.LoadToPm ? "送片 LL→PM" : "取片 PM→LL", evt: "OPERATOR");
        await _xfer.RunAsync(dir);
    }

    private void RenderTransfer(MachineSnapshot s)
    {
        if (_m is null || _wafers is null) return;
        void Box(string border, string text, string where, bool? has)
        {
            C<TextBlock>(text).Text = $"{where}\n{WaferMap.Text(has)}";
            C<Border>(border).Background = new SolidColorBrush(Color.Parse(has switch { true => "#C8E6C9", false => "#F5F5F5", null => "#FFE0B2" }));
        }
        Box("BoxLl", "TxtWaferLl", "Loadlock 手臂", _wafers.Loadlock);
        Box("BoxPm", "TxtWaferPm", "PM", _wafers.Pm);
        C<TextBlock>("TxtWaferSv").Text = s.SlitValve == LoadlockSv.Open ? "⇄ SV 開" : "｜ SV 關";
        C<TextBlock>("TxtWaferNote").Text = _wafers.Updated == default ? _wafers.Note : $"{_wafers.Updated:MM-dd HH:mm:ss} {_wafers.Note}";

        bool canRun = Can(Permission.Transfer) && !Busy;
        var whyLoad = TransferSequencer.Precheck(TransferDirection.LoadToPm, s, _wafers).ToList();
        var whyUnload = TransferSequencer.Precheck(TransferDirection.UnloadToLoadlock, s, _wafers).ToList();
        C<Button>("BtnXferLoad").IsEnabled = canRun && whyLoad.Count == 0;
        C<Button>("BtnXferUnload").IsEnabled = canRun && whyUnload.Count == 0;
        C<Button>("BtnXferAbort").IsEnabled = XferRunning;
        // 兩個方向都不能按時說明原因（PM 有片看取片的原因，否則看送片的）
        var why = _wafers.Pm == true ? whyUnload : whyLoad;
        C<TextBlock>("TxtXferWhy").Text =
            !Can(Permission.Transfer) ? "傳片需要操作員以上權限，請先登入" :
            SequenceRunning ? "配方流程執行中" :
            XferRunning || whyLoad.Count == 0 || whyUnload.Count == 0 ? "" :
            "現在不能傳片：" + string.Join("；", why);
        C<TextBlock>("TxtXferState").Text = _xfer is null ? "" : _xfer.State switch
        {
            SequencerState.Running => $"執行中：{_xfer.Step}",
            SequencerState.Done => "完成",
            SequencerState.Aborted => $"已中止（{_xfer.Step}）",
            SequencerState.Failed => $"失敗（{_xfer.Step}）",
            _ => "",
        };
        C<TextBlock>("TxtXferStatus").Text =
            $"PM {PmP(s)} Torr   LL {(s.LoadlockPressureUnderRange ? "< 1 Torr" : double.IsNaN(s.LoadlockPressureTorr) ? "---" : $"{s.LoadlockPressureTorr:0.0} Torr")}\n" +
            $"Turbo GV {(s.TurboGateOpened ? "開" : s.TurboGateClosed ? "關" : "動作中")}   SV {SvText[(int)s.SlitValve % 6]}\n" +
            $"手臂 {s.ArmPosSteps} step{(s.ArmMoving ? " 移動中" : "")}{(s.ArmClearOfSv ? "（在 SV 外）" : "")}   Z {s.ArmZ}\n" +
            $"Heater module {s.HeaterPosSteps} step {(s.HeaterDown ? "下位" : s.HeaterAtProcess ? "Process 位" : "")}\n" +
            $"Wafer lift {(s.WaferLiftUp ? "上" : s.WaferLiftDown ? "下" : "動作中")}";
        C<StackPanel>("PnlWaferEdit").IsEnabled = Can(Permission.EditWaferMap) && !Busy;

        lock (_xferLog)
            if (_xferLog.Count != _xferLogShown)
            {
                C<ListBox>("LstXferLog").ItemsSource = _xferLog.ToList();
                _xferLogShown = _xferLog.Count;
            }
    }
}
