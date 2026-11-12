using Ald.Core;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Ald.Hmi;

/// <summary>
/// 總覽圖示點擊操作（2026-11-12 使用者要求：不用切到手動頁）。點閥 / 泵 / MFC / SV / PM / Loadlock 跳出小視窗：
/// 上面是即時狀態，下面是操作按鈕。權限與鎖定跟手動頁相同（Loadlock 部分跟總覽 Loadlock 按鈕相同），
/// 命令一樣走 Cmd / Ll，IPC 與主控板的互鎖都還在。
/// </summary>
public partial class MainWindow
{
    private Flyout? _ovFly;
    private Action<MachineSnapshot>? _ovRefresh;

    private void WireOverviewClick()
    {
        var ov = C<OverviewView>("Ov");
        ov.ItemClicked += id => OpenOvPopup(ov, id);
    }

    private bool LoadlockOpAllowed => Can(Permission.Loadlock) && !XferRunning && _auto?.State != SequencerState.Running && !BatchRunning;

    private string LoadlockLockText() =>
        !Can(Permission.Loadlock) ? (_mode == ControlMode.Online ? "ONLINE 模式：Loadlock 手動操作已鎖住" : "Loadlock 操作需要先登入")
        : XferRunning || BatchRunning || _auto?.State == SequencerState.Running ? "傳片 / 流程執行中，Loadlock 操作已鎖住" : "";

    private void OpenOvPopup(OverviewView ov, string id)
    {
        if (_m is null) return;
        _ovFly?.Hide();
        var m = _m;
        var cfg = m.Config;
        var title = new TextBlock { FontWeight = FontWeight.Bold, FontSize = 15 };
        var state = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13 };
        var btns = new WrapPanel();
        var extra = new StackPanel { Spacing = 4 };
        var lockTxt = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = HeatErrBg };
        var panel = new StackPanel { Spacing = 6, MinWidth = 230, MaxWidth = 330 };
        panel.Children.Add(title);
        panel.Children.Add(state);
        panel.Children.Add(extra);
        panel.Children.Add(btns);
        panel.Children.Add(lockTxt);
        var fly = new Flyout { Content = panel };

        var ops = new List<Control>();          // 會被鎖的控制項
        Func<MachineSnapshot, string> stateFn = _ => "";
        bool llItem = false;

        Button Op(string text, Action act, bool lockable = true)
        {
            var b = new Button { Content = text, Margin = new(0, 0, 6, 6), MinWidth = 64, HorizontalContentAlignment = HorizontalAlignment.Center };
            b.Click += (_, _) => { act(); fly.Hide(); };
            btns.Children.Add(b);
            if (lockable) ops.Add(b);
            return b;
        }

        if (id[0] == 'V' && id.Length == 2)            // ALD 閥
        {
            int i = id[1] - '0';
            string name = _valveNames.Length > i && !string.IsNullOrWhiteSpace(_valveNames[i]) ? $"（{_valveNames[i]}）" : "";
            title.Text = $"ALD 閥 V{i + 1}{name}";
            stateFn = s => $"目前：{((s.AldValves >> i & 1) != 0 ? "開" : "關")}{(s.AldRunning ? "（ALD 執行中，由製程板控制）" : "")}";
            Op("開", () => Cmd($"ALD V{i + 1} 開", () => m.SetAldValves((ushort)(m.Snapshot.AldValves | 1 << i))));
            Op("關", () => Cmd($"ALD V{i + 1} 關", () => m.SetAldValves((ushort)(m.Snapshot.AldValves & ~(1 << i)))));
            Op("ALD 閥全關", () => Cmd("ALD 閥全關", () => m.SetAldValves(0)));
        }
        else if (id[0] == 'M')                           // MFC
        {
            int ch = id[1] - '0';
            var c = cfg.Mfc.Channels[ch];
            title.Text = $"MFC {ch + 1} {c.Name}";
            stateFn = s => $"實際 {s.MfcValue[ch]:0.0} {c.Unit}（{s.MfcPct[ch]:0.0}% FS）\n上游閥 {((s.MfcUpstreamValves >> ch & 1) != 0 ? "開" : "關")}　滿刻度 {c.FullScale:0} {c.Unit}";
            var num = new NumericUpDown { Minimum = 0, Maximum = (decimal)c.FullScale, Increment = 1, FormatString = "0.0", ShowButtonSpinner = false, Width = 90, Value = (decimal)Math.Round(m.Snapshot.MfcValue[ch], 1) };
            var up = new CheckBox { Content = "同時開上游閥", IsChecked = true };
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            row.Children.Add(new TextBlock { Text = "設定", VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(num);
            row.Children.Add(new TextBlock { Text = c.Unit, VerticalAlignment = VerticalAlignment.Center });
            extra.Children.Add(row);
            extra.Children.Add(up);
            ops.Add(num); ops.Add(up);
            Op("套用", () =>
            {
                double v = (double)(num.Value ?? 0);
                bool u = up.IsChecked == true;
                Cmd($"MFC {ch + 1} → {v:0.0} {c.Unit}{(u ? "，開上游閥" : "")}", () => m.SetMfc(ch, v, u));
            });
            Op("關", () => Cmd($"MFC {ch + 1} 關", () => m.SetMfc(ch, 0, false)));
        }
        else if (id.StartsWith("Vac:"))                  // 真空閥 / 泵
        {
            var bit = Enum.Parse<VacuumOutputs>(id[4..]);
            string text = VacItems.First(x => x.Bit == bit).Text;
            bool pump = bit is VacuumOutputs.DryPumpOn or VacuumOutputs.TurboOn;
            title.Text = text;
            stateFn = s =>
            {
                string fb = bit switch
                {
                    VacuumOutputs.DryPumpOn => $"Dry pump {(s.DryPumpAlarm ? "警報" : s.DryPumpRunning ? "運轉中" : "停")}",
                    VacuumOutputs.TurboOn => $"Turbo {(s.TurboAlarm ? "警報" : s.TurboAtSpeed ? "全速" : s.VacuumOutputs.HasFlag(bit) ? "加速中" : "停")}",
                    VacuumOutputs.TurboGv => $"GV {(s.TurboGateOpened ? "開" : s.TurboGateClosed ? "關" : "動作中")}",
                    _ => $"實際 {(s.VacuumOutputs.HasFlag(bit) ? "開" : "關")}",
                };
                return $"命令 {(m.VacuumCommand.HasFlag(bit) ? "開" : "關")}　{fb}"
                    + (s.VacuumBlocked.HasFlag(bit) ? "\n⚠ 真空板互鎖擋下" : "")
                    + $"\nPM {PmP(s)} Torr　Foreline {P(s.ForelineTorr)} Torr";
            };
            Op(pump ? "啟動" : "開", () => Cmd($"{text} {(pump ? "啟動" : "開")}", () => m.SetVacuum(m.VacuumCommand | bit)));
            Op(pump ? "停止" : "關", () => Cmd($"{text} {(pump ? "停止" : "關")}", () => m.SetVacuum(m.VacuumCommand & ~bit)));
        }
        else if (id == "PM")
        {
            title.Text = "PM（製程腔體）";
            stateFn = s => $"晶圓 {WaferMap.Text(_wafers?.Pm)}{(_wafers?.Pm == true && _wafers.PmId.Length > 0 ? " " + _wafers.PmId : "")}\n"
                + $"Wafer lift {(s.WaferLiftUp ? "上" : s.WaferLiftDown ? "下" : "動作中")}　Heater module {(s.HeaterAtProcess ? "Process 位置" : s.HeaterDown ? "下位" : "中間")}（{s.HeaterPosSteps} step）\n"
                + $"壓力 {PmP(s)} Torr";
            Op("Lift 上升", () => Cmd("Wafer lift 上升", () => m.SetWaferLift(true)));
            Op("Lift 下降", () => Cmd("Wafer lift 下降", () => m.SetWaferLift(false)));
            Op("Heater 到 Process", () => _ = Do($"Heater module 移動到 Process（{cfg.Transfer.HeaterProcessSteps} step）", () => m.HeaterMoveAsync(cfg.Transfer.HeaterProcessSteps, 1000)));
            Op("Heater 原點復歸", () => _ = Do("Heater module 原點復歸", () => m.HeaterHomeAsync(1000)));
            Op("加熱設定 →", () => C<TabControl>("Tabs").SelectedItem = Tab("加熱"), lockable: false);
            Op("手動頁 →", () => C<TabControl>("Tabs").SelectedItem = Tab("手動操作"), lockable: false);
        }
        else                                              // Loadlock：LL、SV、LL 粗抽 / 洩壓閥
        {
            llItem = true;
            string llp(MachineSnapshot s) => !s.LoadlockFresh ? "未連線" : s.LoadlockPressureUnderRange ? "< 1 Torr" : double.IsNaN(s.LoadlockPressureTorr) ? "---" : $"{s.LoadlockPressureTorr:0.0} Torr";
            switch (id)
            {
                case "SV":
                    title.Text = "Slit valve（Loadlock ↔ PM）";
                    stateFn = s => $"SV {SvText[(int)s.SlitValve % 6]}\nLoadlock {llp(s)}　PM {PmP(s)} Torr\n開 SV 條件：兩邊都 < 1 Torr 且 Turbo GV 已關";
                    Op("開 SV", () => Ll(LoadlockCommand.SvOpen));
                    Op("關 SV", () => Ll(LoadlockCommand.SvClose));
                    break;
                case "LlRough":
                    title.Text = "Loadlock 粗抽閥";
                    stateFn = s => $"目前 {(s.LoadlockRoughOpen ? "開" : "關")}　Loadlock {llp(s)}";
                    Op("開", () => Ll(LoadlockCommand.RoughOn));
                    Op("關", () => Ll(LoadlockCommand.RoughOff));
                    break;
                case "LlVent":
                    title.Text = "Loadlock 洩壓閥";
                    stateFn = s => $"目前 {(s.LoadlockVentOpen ? "開" : "關")}　Loadlock {llp(s)}";
                    Op("開", () => Ll(LoadlockCommand.VentOn));
                    Op("關", () => Ll(LoadlockCommand.VentOff));
                    break;
                default:
                    title.Text = "Loadlock";
                    stateFn = s => $"晶圓 {WaferMap.Text(_wafers?.Loadlock)}{(_wafers?.Loadlock == true && _wafers.LoadlockId.Length > 0 ? " " + _wafers.LoadlockId : "")}\n"
                        + $"壓力 {llp(s)}{(s.LoadlockPressureSuspect ? "（不可信）" : "")}　SV {SvText[(int)s.SlitValve % 6]}\n"
                        + $"手臂 {s.ArmPosSteps} step　Z {s.ArmZ}{(s.LoadlockLidOpen ? "\n⚠ 上蓋開" : "")}";
                    Op("LL 抽氣", () => Ll(LoadlockCommand.Pump));
                    Op("LL 洩壓", () => Ll(LoadlockCommand.Vent));
                    Op("開 SV", () => Ll(LoadlockCommand.SvOpen));
                    Op("關 SV", () => Ll(LoadlockCommand.SvClose));
                    Op("傳片頁 →", () => C<TabControl>("Tabs").SelectedItem = Tab("傳片"), lockable: false);
                    break;
            }
        }

        _ovRefresh = s =>
        {
            state.Text = stateFn(s);
            bool locked = llItem ? !LoadlockOpAllowed : ManualLocked(s);
            foreach (var c in ops) c.IsEnabled = !locked;
            lockTxt.Text = llItem ? LoadlockLockText() : ManualLockText(s);
            lockTxt.IsVisible = lockTxt.Text.Length > 0;
        };
        _ovRefresh(m.Snapshot);
        fly.Closed += (_, _) => { if (_ovFly == fly) { _ovFly = null; _ovRefresh = null; } };
        _ovFly = fly;
        fly.ShowAt(ov, true);
    }
}
