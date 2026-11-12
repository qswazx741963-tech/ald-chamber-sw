using Ald.Comm;
using Ald.Core;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Ald.Hmi;

/// <summary>
/// 手動操作頁：真空閥 / 泵、ALD 閥、MFC、Heater module、Wafer lift（加熱在「加熱」頁，MainWindow.HeaterPage.cs）。
/// 命令一律走 MachineService（IPC 端先檢查上限與互鎖，主控板 / 執行板還會再擋一次）；
/// 流程執行中整頁鎖住，避免手動命令跟流程搶。
/// </summary>
public partial class MainWindow
{
    private static readonly (VacuumOutputs Bit, string Text)[] VacItems =
    {
        (VacuumOutputs.DryPumpOn, "Dry pump"),
        (VacuumOutputs.ChamberRough, "Chamber 粗抽閥"),
        (VacuumOutputs.TurboOn, "Turbo pump"),
        (VacuumOutputs.TurboRough, "Turbo 前級閥（TurboRough）"),
        (VacuumOutputs.TurboGv, "Turbo GV（PM ↔ Turbo）"),
        (VacuumOutputs.ChamberVent, "Chamber Vent"),
    };
    private readonly CheckBox[] _vacChk = new CheckBox[VacItems.Length];
    private readonly CheckBox[] _aldChk = new CheckBox[7];

    /// <summary>同步命令：成功寫狀態列 + 操作記錄；被拒（超上限、互鎖）顯示原因。</summary>
    private void Cmd(string what, Action act)
    {
        try { act(); _status = $"{what}：已送出"; _alarmLog?.Add(OpSource, "Info", what, evt: "OPERATOR"); }
        catch (Exception e) { _status = $"{what}：被拒（{e.Message}）"; _alarmLog?.Add(OpSource, "Warning", _status, evt: "OPERATOR"); }
    }

    private void WireManual()
    {
        var vac = C<StackPanel>("PnlVac");
        for (int i = 0; i < VacItems.Length; i++)
        {
            var (bit, text) = VacItems[i];
            var cb = new CheckBox { Content = text };
            cb.Click += (_, _) =>
            {
                if (_m is null) return;
                bool on = cb.IsChecked == true;
                var cmd = on ? _m.VacuumCommand | bit : _m.VacuumCommand & ~bit;
                Cmd($"{text} {(on ? "開" : "關")}", () => _m.SetVacuum(cmd));
            };
            _vacChk[i] = cb;
            vac.Children.Add(cb);
        }

        var ald = C<WrapPanel>("PnlAld");
        for (int i = 0; i < 7; i++)
        {
            int b = i;
            var cb = new CheckBox { Content = $"V{i + 1}", Margin = new(0, 0, 12, 0) };
            cb.Click += (_, _) =>
            {
                if (_m is null) return;
                // 以實際輸出為基準：安全狀態被板子關掉的閥，不會因為點別顆又被打開
                var cur = _m.Snapshot.AldValves;
                var mask = (ushort)(cb.IsChecked == true ? cur | 1 << b : cur & ~(1 << b));
                Cmd($"ALD V{b + 1} {(cb.IsChecked == true ? "開" : "關")}", () => _m.SetAldValves(mask));
            };
            _aldChk[i] = cb;
            ald.Children.Add(cb);
        }
        C<Button>("BtnAldAllOff").Click += (_, _) => { if (_m is not null) Cmd("ALD 閥全關", () => _m.SetAldValves(0)); };

        if (_m is null) return;
        var cfg = _m.Config;

        C<Button>("BtnGoHeat").Click += (_, _) => C<TabControl>("Tabs").SelectedItem = Tab("加熱");

        // MFC
        var cmbM = C<ComboBox>("CmbMfc");
        cmbM.ItemsSource = cfg.Mfc.Channels.Select((c, i) => $"{i + 1} {c.Name}（{c.Use}）").ToList();
        cmbM.SelectionChanged += (_, _) =>
        {
            int ch = cmbM.SelectedIndex;
            if (ch < 0) return;
            C<NumericUpDown>("NumMfc").Maximum = (decimal)cfg.Mfc.Channels[ch].FullScale;
            C<TextBlock>("TxtMfcUnit").Text = $"設定 {cfg.Mfc.Channels[ch].Unit}";
        };
        cmbM.SelectedIndex = 1;
        C<Button>("BtnMfcApply").Click += (_, _) =>
        {
            int ch = cmbM.SelectedIndex;
            double v = (double)(C<NumericUpDown>("NumMfc").Value ?? 0);
            bool up = C<CheckBox>("ChkMfcUp").IsChecked == true;
            Cmd($"MFC {ch + 1} → {v:0.0} {cfg.Mfc.Channels[ch].Unit}{(up ? "，開上游閥" : "")}", () => _m.SetMfc(ch, v, up));
        };
        C<Button>("BtnMfcOff").Click += (_, _) => { int ch = cmbM.SelectedIndex; Cmd($"MFC {ch + 1} 關", () => _m.SetMfc(ch, 0, false)); };
        C<Button>("BtnMfcAllOff").Click += (_, _) => Cmd("MFC 全部關閉", () => { for (int i = 0; i < 8; i++) _m.SetMfc(i, 0, false); });

        // Heater module / Wafer lift
        ushort Speed() => (ushort)(C<NumericUpDown>("NumAxisSpeed").Value ?? 1000);
        C<Button>("BtnAxisMove").Click += async (_, _) =>
        {
            int t = (int)(C<NumericUpDown>("NumAxisTarget").Value ?? 0);
            await Do($"Heater module 移動到 {t} step", () => _m.HeaterMoveAsync(t, Speed()));
        };
        C<Button>("BtnAxisHome").Click += async (_, _) => await Do("Heater module 原點復歸", () => _m.HeaterHomeAsync(Speed()));
        C<Button>("BtnLiftUp").Click += (_, _) => Cmd("Wafer lift 上升", () => _m.SetWaferLift(true));
        C<Button>("BtnLiftDown").Click += (_, _) => Cmd("Wafer lift 下降", () => _m.SetWaferLift(false));
    }

    /// <summary>手動操作為什麼鎖住（手動頁、總覽點圖示共用）；空字串 = 可操作（安全狀態只是提醒）。</summary>
    private string ManualLockText(MachineSnapshot s) =>
        _mode == ControlMode.Online ? "ONLINE（生產模式）：手動操作已鎖住，請從主頁 Sequence 執行；要手動請先切 OFFLINE（手動需工程師登入）" :
        !Can(Permission.ManualControl) ? "手動操作需要工程師以上權限，請先登入" :
        !s.Connected ? "主控板未連線" :
        SequenceRunning ? $"流程「{_runName}」執行中，手動操作已鎖住（要操作請先中止流程）" :
        XferRunning ? "傳片執行中，手動操作已鎖住" :
        LeakRunning ? "漏率測試執行中，手動操作已鎖住" :
        BatchRunning ? "批次執行中，手動操作已鎖住" :
        s.SafeState ? "系統在安全狀態：執行板會擋下大部分輸出，請先按 Reset" : "";

    private bool ManualLocked(MachineSnapshot s) => !s.Connected || Busy || !Can(Permission.ManualControl);

    private void RenderManual(MachineSnapshot s)
    {
        if (_m is null) return;
        bool allowed = Can(Permission.ManualControl);
        bool locked = !s.Connected || Busy || !allowed;
        C<Grid>("PnlManual").IsEnabled = !locked;
        C<TextBlock>("TxtManualLock").Text = ManualLockText(s);

        var cmd = _m.VacuumCommand;
        var fb = new System.Text.StringBuilder();
        for (int i = 0; i < VacItems.Length; i++)
        {
            var (bit, text) = VacItems[i];
            _vacChk[i].IsChecked = cmd.HasFlag(bit);
            if (cmd.HasFlag(bit) != s.VacuumOutputs.HasFlag(bit) || s.VacuumBlocked.HasFlag(bit))
                fb.AppendLine($"{text}：命令 {(cmd.HasFlag(bit) ? "開" : "關")}，實際 {(s.VacuumOutputs.HasFlag(bit) ? "開" : "關")}"
                              + (s.VacuumBlocked.HasFlag(bit) ? "（真空板互鎖擋下）" : ""));
        }
        fb.AppendLine($"Dry pump {(s.DryPumpRunning ? "運轉" : "停")}  Turbo {(s.TurboAtSpeed ? "全速" : "未到速")}  GV {(s.TurboGateOpened ? "開" : s.TurboGateClosed ? "關" : "動作中")}");
        fb.Append($"PM {PmP(s)} Torr  Foreline {P(s.ForelineTorr)} Torr");
        if (s.LastDenyBits != 0) fb.Append($"\n主控板最後擋下：{DenyReasons.Describe(s.LastDenyBits)}");
        C<TextBlock>("TxtVacFb").Text = fb.ToString();

        for (int i = 0; i < 7; i++) _aldChk[i].IsChecked = (s.AldValves >> i & 1) != 0;

        int m = C<ComboBox>("CmbMfc").SelectedIndex;
        if (m >= 0)
            C<TextBlock>("TxtMfcInfo").Text =
                $"實際 {s.MfcValue[m]:0.0} {_m.Config.Mfc.Channels[m].Unit}（{s.MfcPct[m]:0.0}% FS）\n" +
                $"上游閥 {((s.MfcUpstreamValves >> m & 1) != 0 ? "開" : "關")}";

        C<TextBlock>("TxtAxisInfo").Text = $"位置 {s.HeaterPosSteps} step  狀態 {s.HeaterAxis}\n" +
            $"{(s.HeaterDown ? "下位" : s.HeaterAtProcess ? "Process 位置" : "中間")}";
        C<TextBlock>("TxtLiftInfo").Text = $"{(s.WaferLiftUp ? "上" : s.WaferLiftDown ? "下" : "動作中")}  交接位置 {(s.PmAtTransfer ? "是" : "否")}";
    }
}
