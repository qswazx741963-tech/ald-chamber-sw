using Ald.Core;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace Ald.Hmi;

/// <summary>
/// MFC 規格設定（2026-11-25 使用者要求）：Recipe 頁下方「MFC 規格…」，8 組各填 名稱 / 氣體、用途、滿刻度、單位。
/// 存到 config mfc.specPath（預設 data/mfc.json），立即生效；權限同配方編輯（工程師以上、OFFLINE），流程執行中不能改。
/// </summary>
public partial class MainWindow
{
    private static readonly string[] MfcUnits = ["sccm", "slm", "mg/h", "g/h", "psi", "Torr", "bar"];

    private void WireMfcSpecs()
    {
        var btn = C<Button>("BtnRMfcSpecs");
        var name = new TextBox[8]; var use = new TextBox[8]; var fs = new NumericUpDown[8]; var unit = new ComboBox[8];
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("28,150,130,100,90") };
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        string[] heads = ["#", "名稱 / 氣體", "用途", "滿刻度", "單位"];
        for (int c = 0; c < heads.Length; c++)
            grid.Children.Add(new TextBlock { Text = heads[c], FontWeight = FontWeight.Bold, Margin = new(2), [Grid.ColumnProperty] = c });
        for (int i = 0; i < 8; i++)
        {
            int r = i + 1;
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.Children.Add(new TextBlock { Text = $"{i + 1}", VerticalAlignment = VerticalAlignment.Center, [Grid.RowProperty] = r });
            name[i] = new TextBox { Margin = new(2), Watermark = "例如 Ar 200 sccm", [Grid.RowProperty] = r, [Grid.ColumnProperty] = 1 };
            use[i] = new TextBox { Margin = new(2), Watermark = "例如 Purge", [Grid.RowProperty] = r, [Grid.ColumnProperty] = 2 };
            fs[i] = new NumericUpDown { Margin = new(2), Minimum = 0, Maximum = 1_000_000, Increment = 10, FormatString = "0.###", ShowButtonSpinner = false, [Grid.RowProperty] = r, [Grid.ColumnProperty] = 3 };
            unit[i] = new ComboBox { Margin = new(2), ItemsSource = MfcUnits, HorizontalAlignment = HorizontalAlignment.Stretch, [Grid.RowProperty] = r, [Grid.ColumnProperty] = 4 };
            grid.Children.Add(name[i]); grid.Children.Add(use[i]); grid.Children.Add(fs[i]); grid.Children.Add(unit[i]);
        }
        var msg = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = MsgErr, MaxWidth = 500 };
        var save = new Button { Content = "儲存", HorizontalAlignment = HorizontalAlignment.Right, Background = new SolidColorBrush(Color.Parse("#1565C0")), Foreground = Brushes.White };
        var panel = new StackPanel { Spacing = 6 };
        panel.Children.Add(new TextBlock { Text = "MFC 規格（整台機器共用）", FontWeight = FontWeight.Bold });
        panel.Children.Add(new TextBlock
        {
            Text = "製程板只認 % 滿刻度，流量換算在 IPC：改滿刻度後，之後下的設定值和顯示的讀值都照新滿刻度換算。請填 MFC 本體標示（或校正報告）的滿刻度與氣體。",
            Foreground = Brushes.Gray, FontSize = 12, TextWrapping = TextWrapping.Wrap, MaxWidth = 500,
        });
        panel.Children.Add(grid);
        panel.Children.Add(msg);
        panel.Children.Add(save);
        var fly = new Flyout { Content = panel, Placement = PlacementMode.TopEdgeAlignedLeft };
        fly.FlyoutPresenterClasses.Add("wide");
        btn.Flyout = fly;

        fly.Opened += (_, _) =>
        {
            msg.Text = "";
            var ch = _m?.Config.Mfc.Channels ?? new();
            for (int i = 0; i < 8 && i < ch.Count; i++)
            {
                name[i].Text = ch[i].Name; use[i].Text = ch[i].Use; fs[i].Value = (decimal)ch[i].FullScale;
                unit[i].SelectedItem = MfcUnits.Contains(ch[i].Unit) ? ch[i].Unit : null;
                if (unit[i].SelectedItem is null) { unit[i].ItemsSource = MfcUnits.Append(ch[i].Unit).ToArray(); unit[i].SelectedItem = ch[i].Unit; }
            }
            bool ok = Can(Permission.EditRecipe) && !Busy;
            grid.IsEnabled = save.IsEnabled = ok;
            if (!ok) msg.Text = Busy ? "流程執行中不能改 MFC 規格" : "修改 MFC 規格需要工程師以上權限，且在 OFFLINE";
        };
        save.Click += (_, _) =>
        {
            if (_m is null) return;
            if (!Can(Permission.EditRecipe) || Busy) { msg.Text = "目前不能修改（權限或流程執行中）"; return; }
            var list = Enumerable.Range(0, 8).Select(i => new MfcChannelConfig
            {
                Name = (name[i].Text ?? "").Trim(), Use = (use[i].Text ?? "").Trim(),
                FullScale = (double)(fs[i].Value ?? 0), Unit = (unit[i].SelectedItem as string ?? "").Trim(),
            }).ToList();
            var err = MfcSpecs.Validate(list).ToList();
            if (err.Count > 0) { msg.Text = string.Join("\n", err); return; }
            var old = _m.Config.Mfc.Channels;
            var diff = MfcSpecs.Diff(old, list);
            if (diff.Count == 0) { fly.Hide(); return; }
            try
            {
                MfcSpecs.Save(_m.Config.Mfc.SpecPath, list);
                _m.UpdateMfcSpecs(list);
            }
            catch (Exception e) { msg.Text = $"存檔失敗：{e.Message}"; return; }
            _alarmLog?.Add(OpSource, "Info", "MFC 規格：" + string.Join("、", diff), evt: "OPERATOR");
            fly.Hide();
            RefreshManualMfcList();
            RebuildTable();
            Msg("MFC 規格已更新：" + string.Join("、", diff) + "\n提醒：改了滿刻度的話，請按「檢查」確認配方流量沒有超過新的滿刻度", ok: true);
        };
    }
}
