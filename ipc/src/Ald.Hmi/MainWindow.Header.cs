using Ald.Core;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace Ald.Hmi;

/// <summary>
/// 頂部列：執行狀態（登入者小字）、燈塔 + 消音圖示、各模組連線燈號（點一下跳出狀態視窗）、Reset / 安全停止、
/// 右上角下拉選單（各頁面 + 登入 / 登出，取代分頁標籤）；第二列整列顯示最近兩筆事件，可清除（只清畫面）。
/// 時間在左下角狀態列。
/// </summary>
public partial class MainWindow
{
    /// <summary>連線燈號的模組：名稱、警報來源（AlarmInfo.Source）、是否上線。</summary>
    private sealed record LinkModule(string Name, string[] Sources, Func<MachineSnapshot, bool> Online);

    private static readonly LinkModule[] Modules =
    {
        new("主控", new[] { "通訊", "主控板", "系統" }, s => s.Connected),
        new("製程", new[] { "製程板" }, s => s.Connected && s.SlotOnline.Length > 1 && s.SlotOnline[1]),
        new("加熱", new[] { "加熱板", "加熱" }, s => s.Connected && s.SlotOnline.Length > 2 && s.SlotOnline[2]),
        new("真空", new[] { "真空板", "真空" }, s => s.Connected && s.SlotOnline.Length > 3 && s.SlotOnline[3]),
        new("馬達", new[] { "馬達板", "馬達" }, s => s.Connected && s.SlotOnline.Length > 4 && s.SlotOnline[4]),
        new("LL", new[] { "Loadlock" }, s => s.Connected && s.LoadlockFresh),
    };

    private enum Health { Ok, Warning, Alarm, Offline }
    private static readonly IBrush HOk = new SolidColorBrush(Color.Parse("#3CC35A"));
    private static readonly IBrush HWarn = new SolidColorBrush(Color.Parse("#F2C230"));
    private static readonly IBrush HAlarm = new SolidColorBrush(Color.Parse("#E04040"));
    private static readonly IBrush HOff = new SolidColorBrush(Color.Parse("#6B7785"));

    private readonly List<Ellipse> _linkLamps = new();
    private DateTime _recentAt = DateTime.MinValue;
    private DateTime _logClearedAt = DateTime.MinValue;
    private MenuItem? _menuLogin;

    private void WireHeader()
    {
        // 連線燈號
        var links = C<StackPanel>("PnlLinks");
        foreach (var mod in Modules)
        {
            var lamp = new Ellipse { Width = 14, Height = 14, Fill = HOff, VerticalAlignment = VerticalAlignment.Center };
            _linkLamps.Add(lamp);
            links.Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 4,
                Children = { lamp, new TextBlock { Text = mod.Name, Foreground = Brushes.White, FontSize = 13, VerticalAlignment = VerticalAlignment.Center } },
            });
        }
        C<Button>("BtnLinks").Click += (_, _) => { C<Border>("PnlStatus").IsVisible = true; RenderStatusPopup(); };
        C<Button>("BtnStatusClose").Click += (_, _) => C<Border>("PnlStatus").IsVisible = false;
        C<Button>("BtnStatusAlarm").Click += (_, _) =>
        {
            C<Border>("PnlStatus").IsVisible = false;
            C<TabControl>("Tabs").SelectedIndex = 1;   // 警報
        };

        C<Button>("BtnLogClear").Click += (_, _) => { _logClearedAt = DateTime.Now; _recentAt = DateTime.MinValue; };

        // 右上角選單：各頁面 + 登入 / 登出
        var tabs = C<TabControl>("Tabs");
        var menu = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedRight };
        foreach (var t in tabs.Items.OfType<TabItem>())
        {
            var tab = t;
            var item = new MenuItem { Header = t.Header?.ToString(), FontSize = 15, MinWidth = 200 };
            item.Click += (_, _) => tabs.SelectedItem = tab;
            menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());
        _menuLogin = new MenuItem { Header = "登入", FontSize = 15 };
        _menuLogin.Click += (_, _) => LoginClicked();
        menu.Items.Add(_menuLogin);
        menu.Opening += (_, _) =>
        {
            var tabItems = tabs.Items.OfType<TabItem>().ToList();
            int i = 0;
            foreach (var mi in menu.Items.OfType<MenuItem>())
            {
                if (mi == _menuLogin) { mi.Header = _user is null ? "登入" : $"登出（{_user}）"; continue; }
                var tab = tabItems[i++];
                mi.Header = (tab == tabs.SelectedItem ? "▶ " : "    ") + tab.Header;
            }
        };
        C<Button>("BtnMenu").Flyout = menu;
        tabs.SelectionChanged += (_, _) => UpdateMenuButton();
        UpdateMenuButton();
    }

    private void UpdateMenuButton()
        => C<Button>("BtnMenu").Content = $"☰  {(C<TabControl>("Tabs").SelectedItem as TabItem)?.Header}  ▾";

    private Health ModuleHealth(LinkModule mod, MachineSnapshot s, IReadOnlyCollection<ActiveAlarm> active)
    {
        if (!mod.Online(s)) return Health.Offline;
        var mine = active.Where(a => mod.Sources.Contains(a.Info.Source)).ToList();
        if (mine.Any(a => a.Info.Level == AlarmLevel.Alarm)) return Health.Alarm;
        if (mine.Any(a => a.Info.Level == AlarmLevel.Warning)) return Health.Warning;
        if (mod.Name == "加熱" && HeaterMismatch) return Health.Warning;
        return Health.Ok;
    }

    private static IBrush HealthBrush(Health h) => h switch { Health.Ok => HOk, Health.Warning => HWarn, Health.Alarm => HAlarm, _ => HOff };

    private void RenderHeader(MachineSnapshot s)
    {
        var active = _m!.Alarms.Active;
        for (int i = 0; i < Modules.Length; i++)
        {
            var h = ModuleHealth(Modules[i], s, active);
            // 離線：主控板離線顯示紅（整機斷線），其他模組在主控板離線時看不到狀態 → 灰
            _linkLamps[i].Fill = h == Health.Offline ? (i == 0 || s.Connected ? HAlarm : HOff) : HealthBrush(h);
        }
        bool anyProblem = active.Any(a => a.Info.Level >= AlarmLevel.Warning) || _m.Alarms.Latched.Count > 0 || _seqFault;
        C<Button>("BtnLinks").Background = anyProblem && Tower.BlinkOn(DateTime.Now, _m.Config.Tower)
            ? new SolidColorBrush(Color.Parse("#7A2E2E")) : new SolidColorBrush(Color.Parse("#2B3D50"));

        if (_alarmLog is not null && DateTime.Now - _recentAt > TimeSpan.FromSeconds(1))
        {
            _recentAt = DateTime.Now;
            C<TextBlock>("TxtRecent").Text = string.Join("\n", _alarmLog.Recent(2).Where(r => r.Time > _logClearedAt).Select(r =>
                $"{r.Time:HH:mm:ss} {(r.Level == "Alarm" ? "異常" : r.Level == "Warning" ? "警告" : "訊息")} {r.Source}：{r.Message}"));
        }
        if (C<Border>("PnlStatus").IsVisible) RenderStatusPopup();
    }

    /// <summary>狀態視窗：每個模組一列（燈號、上線 / 離線、目前警報），再列流程異常與其他來源的警報。</summary>
    private void RenderStatusPopup()
    {
        if (_m is null) return;
        var s = _m.Snapshot;
        var active = _m.Alarms.Active;
        var latched = _m.Alarms.Latched;
        var rows = C<StackPanel>("PnlStatusRows");
        rows.Children.Clear();

        void Row(IBrush lamp, string name, string state, IEnumerable<string> lines)
        {
            var body = new StackPanel { Spacing = 2, MaxWidth = 560 };
            body.Children.Add(new TextBlock { Text = $"{name}　{state}", FontWeight = FontWeight.Bold, FontSize = 15 });
            foreach (var l in lines) body.Children.Add(new TextBlock { Text = l, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.Parse("#404A55")) });
            rows.Children.Add(new Border
            {
                BorderBrush = new SolidColorBrush(Color.Parse("#D5DDE6")), BorderThickness = new(0, 0, 0, 1), Padding = new(0, 4),
                Child = new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 10,
                    Children = { new Ellipse { Width = 16, Height = 16, Fill = lamp, VerticalAlignment = VerticalAlignment.Top, Margin = new(0, 3, 0, 0) }, body },
                },
            });
        }
        static string Line(ActiveAlarm a, string tag = "") => $"{a.Since:HH:mm:ss} [{(a.Info.Level == AlarmLevel.Alarm ? "異常" : a.Info.Level == AlarmLevel.Warning ? "警告" : "訊息")}{tag}] {a.Info.Message}";

        var used = new HashSet<string>();
        foreach (var mod in Modules)
        {
            var h = ModuleHealth(mod, s, active);
            var mine = active.Where(a => mod.Sources.Contains(a.Info.Source)).OrderByDescending(a => a.Info.Level).Select(a => Line(a))
                .Concat(latched.Where(a => mod.Sources.Contains(a.Info.Source)).Select(a => Line(a, "，已消失未確認"))).ToList();
            foreach (var src in mod.Sources) used.Add(src);
            if (mod.Name == "加熱" && HeaterMismatch) mine.Add("加熱設定跟常駐設定不一樣（畫面上方黃條按「恢復」）：" + DiffText());
            string state = h switch
            {
                Health.Offline => mod.Name == "主控" ? "未連線" : s.Connected ? "離線" : "（主控板未連線，看不到）",
                Health.Alarm => "異常", Health.Warning => "警告", _ => mine.Count > 0 ? "正常（有未確認記錄）" : "正常",
            };
            Row(h == Health.Offline && mod.Name != "主控" && !s.Connected ? HOff : HealthBrush(h == Health.Offline ? Health.Alarm : h),
                mod.Name switch { "主控" => "主控板", "製程" => "製程板", "加熱" => "加熱板", "真空" => "真空板", "馬達" => "馬達板", _ => "Loadlock" },
                state, mine);
        }
        var other = active.Where(a => !used.Contains(a.Info.Source)).Select(a => Line(a))
            .Concat(latched.Where(a => !used.Contains(a.Info.Source)).Select(a => Line(a, "，已消失未確認")))
            .Concat(_seqFault ? new[] { "流程異常停止（未確認，詳見事件記錄）" } : Array.Empty<string>()).ToList();
        if (other.Count > 0) Row(HAlarm, "流程 / 其他", "", other);
    }
}
