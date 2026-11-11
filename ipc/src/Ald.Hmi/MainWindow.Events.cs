using Avalonia.Controls;

namespace Ald.Hmi;

/// <summary>
/// 警報頁下半的事件記錄：時間範圍 / 等級 / 關鍵字篩選、匯出 CSV（存到資料夾 export/）。
/// 勾「自動更新」每 2 秒用目前條件重查；畫面最多列 2000 筆，匯出不限筆數。
/// </summary>
public partial class MainWindow
{
    private static readonly (string Text, TimeSpan Span)[] EvtRanges =
    {
        ("最近 1 小時", TimeSpan.FromHours(1)), ("最近 8 小時", TimeSpan.FromHours(8)), ("最近 24 小時", TimeSpan.FromHours(24)),
        ("最近 7 天", TimeSpan.FromDays(7)), ("最近 30 天", TimeSpan.FromDays(30)),
    };
    private static readonly (string Text, string? Level)[] EvtLevels =
        { ("全部等級", null), ("異常 Alarm", "Alarm"), ("警告 Warning", "Warning"), ("訊息 Info", "Info") };
    private DateTime _evtShownAt = DateTime.MinValue;
    private bool _evtBusy;
    private const int EvtMaxRows = 2000;

    private void WireEvents()
    {
        var r = C<ComboBox>("CmbEvtRange");
        r.ItemsSource = EvtRanges.Select(x => x.Text).ToList();
        r.SelectedIndex = 2;
        var l = C<ComboBox>("CmbEvtLevel");
        l.ItemsSource = EvtLevels.Select(x => x.Text).ToList();
        l.SelectedIndex = 0;
        r.SelectionChanged += (_, _) => QueryEvents();
        l.SelectionChanged += (_, _) => QueryEvents();
        C<Button>("BtnEvtQuery").Click += (_, _) => QueryEvents();
        C<TextBox>("TxtEvtKey").KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) QueryEvents(); };
        C<Button>("BtnEvtCsv").Click += (_, _) => ExportEvents();
    }

    private (DateTime From, DateTime To, string? Level, string Key) EvtFilter()
    {
        var to = DateTime.Now;
        return (to - EvtRanges[Math.Max(0, C<ComboBox>("CmbEvtRange").SelectedIndex)].Span, to,
                EvtLevels[Math.Max(0, C<ComboBox>("CmbEvtLevel").SelectedIndex)].Level,
                C<TextBox>("TxtEvtKey").Text?.Trim() ?? "");
    }

    private void RenderEvents()
    {
        if (C<CheckBox>("ChkEvtLive").IsChecked == true && DateTime.Now - _evtShownAt > TimeSpan.FromSeconds(2)) QueryEvents();
    }

    private async void QueryEvents()
    {
        if (_alarmLog is null || _evtBusy) return;
        _evtBusy = true;
        _evtShownAt = DateTime.Now;
        try
        {
            var (from, to, level, key) = EvtFilter();
            var log = _alarmLog;
            var rows = await Task.Run(() => log.Query(from, to, level, key, EvtMaxRows + 1));
            C<ListBox>("LstLog").ItemsSource = rows.Take(EvtMaxRows).Select(x =>
                $"{x.Time:MM-dd HH:mm:ss} {Lv(x.Level)} {x.Event,-8} {x.Source}：{x.Message}").ToList();
            C<TextBlock>("TxtEvtInfo").Text = rows.Count > EvtMaxRows ? $"超過 {EvtMaxRows} 筆，只列最新的（匯出不限）" : $"{rows.Count} 筆";
        }
        catch (Exception e) { C<TextBlock>("TxtEvtInfo").Text = $"查詢失敗：{e.Message}"; }
        finally { _evtBusy = false; }
    }

    private static string Lv(string level) => level switch { "Alarm" => "異常", "Warning" => "警告", _ => "    " };

    private async void ExportEvents()
    {
        if (_alarmLog is null || _m is null) return;
        var (from, to, level, key) = EvtFilter();
        var path = Path.Combine(ExportDir, $"events_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
        try
        {
            var log = _alarmLog;
            int n = await Task.Run(() => log.ExportCsv(path, from, to, level, key));
            _status = $"事件記錄匯出 {n} 筆：{path}";
            _alarmLog.Add(OpSource, "Info", $"匯出事件記錄 {Path.GetFileName(path)}", evt: "OPERATOR");
        }
        catch (Exception e) { _status = $"匯出失敗：{e.Message}"; }
    }

    /// <summary>匯出資料夾：資料庫旁邊的 export/。</summary>
    private string ExportDir => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(_m!.Config.DataLog.Path)) ?? ".", "export");
}
