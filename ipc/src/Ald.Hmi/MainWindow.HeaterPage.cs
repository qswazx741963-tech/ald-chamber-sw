using Ald.Core;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace Ald.Hmi;

/// <summary>
/// 加熱頁（2026-11-12 使用者要求從手動頁獨立出來）：16 組加熱各一個小框，直接看溫度、改設定、開關，不用下拉選單。
/// 常駐設定：套用 / 關閉成功就寫進 HeaterPreset（斷電後比對、人員按恢復）。權限與鎖定同手動操作頁。
/// </summary>
public partial class MainWindow
{
    private sealed class HeatCard
    {
        public required Border Box;
        public required TextBlock Title, State, Pv, Info;
        public required NumericUpDown Sp;
        public required Button Apply, Off;
        public bool Touched;   // 人員改過設定框（還沒套用）就不要被刷新蓋掉
    }
    private readonly HeatCard[] _heatCards = new HeatCard[16];
    private bool _heatSpLoaded;

    private static readonly IBrush HeatOffBg = new SolidColorBrush(Color.Parse("#E3E8EE"));
    private static readonly IBrush HeatRampBg = new SolidColorBrush(Color.Parse("#F0A030"));
    private static readonly IBrush HeatOkBg = new SolidColorBrush(Color.Parse("#2E9E4F"));
    private static readonly IBrush HeatErrBg = new SolidColorBrush(Color.Parse("#D23C3C"));

    private void WireHeaterPage()
    {
        var grid = C<UniformGrid>("GrdHeaters");
        var names = HeaterNames;
        for (int i = 0; i < 16; i++)
        {
            int ch = i;
            double limit = _m?.Config.Heaters.Channels[i].LimitDegC ?? 650;
            var title = new TextBlock { Text = $"#{i + 1} {names[i]}", FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            var state = new TextBlock { Text = "關", Foreground = Brushes.Black, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            var chip = new Border { Child = state, Background = HeatOffBg, CornerRadius = new(3), Padding = new(6, 1), HorizontalAlignment = HorizontalAlignment.Right };
            var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Background = new SolidColorBrush(Color.Parse("#D5DDE6")) };
            head.Children.Add(new Border { Padding = new(8, 3), Child = title });
            Grid.SetColumn(chip, 1); chip.Margin = new(4, 3, 6, 3);
            head.Children.Add(chip);
            var pv = new TextBlock { Text = "---", FontSize = 24, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center };
            var info = new TextBlock { FontSize = 12, Foreground = new SolidColorBrush(Color.Parse("#5A6B7D")), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
            var sp = new NumericUpDown { Minimum = 0, Maximum = (decimal)Math.Max(0, limit), Increment = 5, FormatString = "0", ShowButtonSpinner = false, Width = 72, Value = 0 };
            var apply = new Button { Content = "套用", Padding = new(10, 4) };
            var off = new Button { Content = "關", Padding = new(10, 4) };
            var card = new HeatCard { Box = new Border(), Title = title, State = state, Pv = pv, Info = info, Sp = sp, Apply = apply, Off = off };
            sp.ValueChanged += (_, _) => card.Touched = true;
            apply.Click += (_, _) =>
            {
                if (_m is null) return;
                double v = (double)(sp.Value ?? 0), ramp = (double)(C<NumericUpDown>("NumHeatRampAll").Value ?? 0);
                if (v <= 0) { _status = $"加熱 #{ch + 1}：設定要大於 0 °C（要關請按「關」）"; return; }
                Cmd($"加熱 #{ch + 1} {names[ch]} → {v:0} °C{(ramp > 0 ? $"（{ramp:0.#} °C/min）" : "")}", () =>
                {
                    _m.SetHeater(ch, v, true, ramp);
                    _heaters?.Set(ch, true, v, ramp, $"{_user} 加熱頁設定 #{ch + 1}");
                });
                card.Touched = false;
            };
            off.Click += (_, _) =>
            {
                if (_m is null) return;
                Cmd($"加熱 #{ch + 1} {names[ch]} 關閉", () =>
                {
                    _m.SetHeater(ch, 0, false);
                    _heaters?.Set(ch, false, 0, 0, $"{_user} 加熱頁關閉 #{ch + 1}");
                });
                card.Touched = false;
            };

            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            row.Children.Add(new TextBlock { Text = "設定", VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(sp);
            row.Children.Add(new TextBlock { Text = "°C", VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 4, 0) });
            row.Children.Add(apply);
            row.Children.Add(off);
            var body = new StackPanel { Margin = new(8, 4, 8, 6), Spacing = 3 };
            var pvRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
            pvRow.Children.Add(pv);
            Grid.SetColumn(info, 1); info.Margin = new(10, 0, 0, 0);
            pvRow.Children.Add(info);
            body.Children.Add(pvRow);
            body.Children.Add(row);
            var stack = new StackPanel();
            stack.Children.Add(head);
            stack.Children.Add(body);
            card.Box.Classes.Add("card");
            card.Box.Margin = new(3);
            card.Box.Child = stack;
            ToolTip.SetTip(card.Box, $"上限 {limit:0} °C");
            grid.Children.Add(card.Box);
            _heatCards[i] = card;
        }
        C<Button>("BtnHeatAllOff2").Click += (_, _) =>
        {
            if (_m is null) return;
            Cmd("加熱全部關閉", () =>
            {
                for (int i = 0; i < 16; i++) _m.SetHeater(i, 0, false);
                _heaters?.TakeCurrent(_ => null, $"{_user} 加熱全部關閉");
            });
            foreach (var c in _heatCards) c.Touched = false;
        };
    }

    private void RenderHeaterPage(MachineSnapshot s)
    {
        if (_m is null) return;
        bool allowed = Can(Permission.ManualControl);
        bool locked = !s.Connected || Busy || !allowed;
        C<TextBlock>("TxtHeatLock").Text =
            _mode == ControlMode.Online ? "ONLINE（生產模式）：加熱設定已鎖住，要修改請先切 OFFLINE（需工程師登入）" :
            !allowed ? "修改加熱設定需要工程師以上權限，請先登入（溫度可以直接看）" :
            !s.Connected ? "主控板未連線" :
            Busy ? "流程執行中，加熱設定已鎖住" :
            s.SafeState ? "系統在安全狀態：加熱板只保溫，請先按 Reset" : "";
        C<Button>("BtnHeatAllOff2").IsEnabled = !locked;
        var limits = _m.Config.Heaters.Channels;
        bool known = _m.HeaterSetpointsKnown;
        for (int i = 0; i < 16; i++)
        {
            var c = _heatCards[i];
            var sp = _m.HeaterSetpoint(i);
            double t = s.TempC.Length > i ? s.TempC[i] : double.NaN;
            bool broken = double.IsNaN(t);
            c.Pv.Text = broken ? "斷線" : $"{t:0.0} °C";
            c.Pv.Foreground = broken ? HeatErrBg : Brushes.Black;
            double duty = s.HeaterDutyPct.Length > i ? s.HeaterDutyPct[i] : 0;
            c.Info.Text = sp is { } v ? $"設定 {v:0} °C\n輸出 {duty:0}%" : $"未加熱\n上限 {limits[i].LimitDegC:0} °C";
            bool near = sp is { } v2 && !broken && Math.Abs(t - v2) <= 3;
            (c.State.Text, ((Border)c.State.Parent!).Background, c.State.Foreground) =
                broken && sp is not null ? ("熱電偶斷線", HeatErrBg, Brushes.White)
                : sp is null ? ("關", HeatOffBg, Brushes.Black)
                : near ? ("到溫", HeatOkBg, Brushes.White)
                : ("升 / 降溫中", HeatRampBg, Brushes.White);
            c.Sp.IsEnabled = c.Apply.IsEnabled = !locked;
            c.Off.IsEnabled = !locked && sp is not null;
            // 設定框：第一次讀到加熱板設定、或人員沒在改時，跟著實際設定
            if (known && (!_heatSpLoaded || !c.Touched) && !c.Sp.IsFocused)
            {
                c.Sp.Value = (decimal)(sp ?? 0);
                c.Touched = false;
            }
        }
        if (known) _heatSpLoaded = true;
    }
}
