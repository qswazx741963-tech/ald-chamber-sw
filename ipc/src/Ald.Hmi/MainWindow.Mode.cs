using Ald.Core;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace Ald.Hmi;

/// <summary>
/// 控制模式 ONLINE / OFFLINE（Ald.Core.ControlModes）與主頁 Sequence 選單。
/// ONLINE 時手動 / 編輯 / 保養類權限鎖住（Can() 會一起判斷），只能從 Sequence 執行流程。
/// 每個 Sequence 先跳確認視窗：說明會做什麼、需要的輸入、開始前檢查結果，按「執行」才開始。
/// 模式切換寫事件記錄；程式重開一律 OFFLINE。
/// </summary>
public partial class MainWindow
{
    private ControlMode _mode = ControlMode.Offline;

    private static readonly IBrush ModeOnline = new SolidColorBrush(Color.Parse("#2E9E4F"));
    private static readonly IBrush ModeOffline = new SolidColorBrush(Color.Parse("#6B7785"));

    /// <summary>目前確認視窗要執行的 Sequence：檢查（回傳不能執行的原因）與執行。</summary>
    private Func<List<string>>? _seqCheck;

    /// <summary>切換被擋下的原因，顯示在控制模式卡上 15 秒（狀態列字太小容易漏看）。</summary>
    private string _modeRefused = "";
    private DateTime _modeRefusedAt;
    private Action? _seqRun;

    private void WireMode()
    {
        C<Button>("BtnOnline").Click += (_, _) => ToggleMode();

        var menu = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedLeft };
        void Add(string text, Action open)
        {
            var mi = new MenuItem { Header = text, FontSize = 15, MinWidth = 240 };
            mi.Click += (_, _) => open();
            menu.Items.Add(mi);
        }
        Add("自動跑片（送片 → 製程 → 取片）", () => OpenSeq("自動跑片",
            "Loadlock 上的片：LL 抽氣 → 送片到 PM → 依配方製程 → 取片回 Loadlock →（配方勾選時）PM 回填。完成後藍燈請人員取片。",
            recipe: true, wafer: true,
            check: r => r is null ? new() { "請選配方" } : Check(r).Concat(AutoRunJob.Precheck(r, _m!.Snapshot, _wafers!)).Distinct().ToList(),
            run: (r, w) => RunAuto(r!, w)));
        Add("只跑製程（片已在 PM）", () => OpenSeq("只跑製程",
            "片已經在 PM（或空跑）：依配方抽真空 → 確認溫度 → 通氣 → ALD →（配方勾選時）回填。不會送片 / 取片。",
            recipe: true, wafer: true,
            check: r => r is null ? new() { "請選配方" } : Check(r),
            run: (r, w) => RunRecipe(r!, w)));
        Add("批次（多片）…", () => { C<TabControl>("Tabs").SelectedItem = Tab("批次"); _status = "在批次頁排好片，按「開始批次」"; });
        menu.Items.Add(new Separator());
        Add("PM 抽真空", () => OpenSeq("PM 抽真空", "Dry pump → 粗抽到 crossover → Turbo 到速 → 開 Turbo GV → 到 base pressure。",
            check: _ => new(), run: (_, _) => RunRecipe(new ProcessRecipe { Name = "抽真空" })));
        Add("PM 回填大氣", () => OpenSeq("PM 回填大氣", "關 Turbo GV → 停 Turbo → 開 Vent 回填到大氣。PM 裡有片也會回填，請確認。",
            check: _ => new(), run: (_, _) => RunRecipe(new ProcessRecipe { Name = "回填大氣", PumpDown = false, VentAfter = true })));
        menu.Items.Add(new Separator());
        Add("送片 LL → PM", () => OpenSeq("送片 LL → PM", "開 SV → 手臂伸入 → Wafer lift 上升接片 → 手臂退回 → 關 SV。",
            check: _ => TransferSequencer.Precheck(TransferDirection.LoadToPm, _m!.Snapshot, _wafers!).ToList(),
            run: (_, _) => RunTransfer(TransferDirection.LoadToPm)));
        Add("取片 PM → LL", () => OpenSeq("取片 PM → LL", "開 SV → 手臂伸入 → Wafer lift 下降放片 → 手臂退回 → 關 SV。完成後藍燈請人員取片。",
            check: _ => TransferSequencer.Precheck(TransferDirection.UnloadToLoadlock, _m!.Snapshot, _wafers!).ToList(),
            run: (_, _) => RunTransfer(TransferDirection.UnloadToLoadlock)));
        Add("Loadlock 抽氣", () => OpenSeq("Loadlock 抽氣", "SLK 抽氣序列：Loadlock 抽到可以開 SV 的壓力。",
            check: _ => new(), run: (_, _) => Ll(LoadlockCommand.Pump)));
        Add("Loadlock 洩壓", () => OpenSeq("Loadlock 洩壓", "SLK 洩壓序列：Loadlock 回到大氣，之後可以開蓋放片 / 取片。",
            check: _ => new(), run: (_, _) => Ll(LoadlockCommand.Vent)));
        C<Button>("BtnSeq").Flyout = menu;

        C<Button>("BtnSeqCancel").Click += (_, _) => C<Border>("PnlSeq").IsVisible = false;
        C<Button>("BtnSeqOk").Click += (_, _) =>
        {
            var errs = _seqCheck?.Invoke() ?? new();
            if (errs.Count > 0) { C<TextBlock>("TxtSeqErr").Text = "不能執行：\n" + string.Join("\n", errs); return; }
            C<Border>("PnlSeq").IsVisible = false;
            _seqRun?.Invoke();
        };
        C<ComboBox>("CmbSeqRecipe").SelectionChanged += (_, _) => C<TextBlock>("TxtSeqErr").Text = "";
    }

    private TabItem? Tab(string header) => C<TabControl>("Tabs").Items.OfType<TabItem>().FirstOrDefault(t => t.Header?.ToString()?.StartsWith(header) == true);

    /// <param name="check">開始前檢查（參數 = 選的配方，沒選配方欄位時是 null）</param>
    private void OpenSeq(string title, string desc, Func<ProcessRecipe?, List<string>> check, Action<ProcessRecipe?, string> run,
                         bool recipe = false, bool wafer = false)
    {
        if (_m is null || _wafers is null) return;
        C<TextBlock>("TxtSeqTitle").Text = title;
        C<TextBlock>("TxtSeqDesc").Text = desc;
        C<TextBlock>("TxtSeqErr").Text = "";
        C<StackPanel>("PnlSeqRecipe").IsVisible = recipe;
        C<StackPanel>("PnlSeqWafer").IsVisible = wafer;
        var cmb = C<ComboBox>("CmbSeqRecipe");
        if (recipe)
        {
            var files = Directory.Exists(_recipeDir)
                ? Directory.GetFiles(_recipeDir, "*.json").Select(Path.GetFileName).OfType<string>().OrderBy(x => x).ToList() : new();
            var keep = cmb.SelectedItem as string;
            cmb.ItemsSource = files;
            cmb.SelectedItem = keep is not null && files.Contains(keep) ? keep : null;
        }
        if (wafer) C<TextBox>("TxtSeqWafer").Text = _wafers.Loadlock == true ? _wafers.LoadlockId : _wafers.Pm == true ? _wafers.PmId : "";

        ProcessRecipe? Load()
        {
            if (!recipe || cmb.SelectedItem is not string f) return null;
            try { return RecipeFile.Load(Path.Combine(_recipeDir, f)); }
            catch (Exception e) { C<TextBlock>("TxtSeqErr").Text = $"配方讀取失敗：{e.Message}"; return null; }
        }
        _seqCheck = () =>
        {
            var e = new List<string>();
            if (!Can(Permission.RunRecipe)) e.Add("需要操作員以上登入");
            if (Busy) e.Add("已有流程在執行");
            var r = Load();
            e.AddRange(check(r));
            return e.Distinct().ToList();
        };
        _seqRun = () =>
        {
            var r = Load();
            string w = (C<TextBox>("TxtSeqWafer").Text ?? "").Trim();
            _alarmLog?.Add(OpSource, "Info", $"Sequence：{title}{(r is null ? "" : $"（配方 {r.Name}）")}{(w.Length > 0 ? $" 晶圓 {w}" : "")}", evt: "OPERATOR");
            run(r, w);
        };
        if (!recipe) { var e = _seqCheck(); if (e.Count > 0) C<TextBlock>("TxtSeqErr").Text = "目前不能執行：\n" + string.Join("\n", e); }
        C<Border>("PnlSeq").IsVisible = true;
    }

    private void ToggleMode()
    {
        if (_m is null) return;
        if (_mode == ControlMode.Offline)
        {
            var e = ControlModes.GoOnlineProblems(_role, _m.Snapshot, _m.Alarms.AnyAlarm, _wafers?.Loadlock, _wafers?.Pm, HeaterMismatch, Busy);
            if (e.Count > 0) { Refuse("不能切 ONLINE", e); return; }
            _mode = ControlMode.Online;
        }
        else
        {
            var e = ControlModes.GoOfflineProblems(_role, Busy);
            if (e.Count > 0) { Refuse("不能切 OFFLINE", e); return; }
            _mode = ControlMode.Offline;
        }
        _modeRefused = "";
        _status = $"控制模式切到 {ControlModes.Text(_mode)}";
        _alarmLog?.Add(OpSource, "Info", _status, evt: "OPERATOR");
    }

    private void Refuse(string head, List<string> e)
    {
        _status = head + "：" + string.Join("；", e);
        _modeRefused = head + "：\n" + string.Join("\n", e.Select(x => "・" + x));
        _modeRefusedAt = DateTime.Now;
    }

    private void RenderMode()
    {
        bool on = _mode == ControlMode.Online;
        foreach (var (bd, tx) in new[] { ("BdMode", "TxtMode"), ("BdModeTop", "TxtModeTop") })
        {
            C<Border>(bd).Background = on ? ModeOnline : ModeOffline;
            C<TextBlock>(tx).Text = ControlModes.Text(_mode);
        }
        bool refused = _modeRefused.Length > 0 && DateTime.Now - _modeRefusedAt < TimeSpan.FromSeconds(15);
        var hint = C<TextBlock>("TxtModeHint");
        hint.Foreground = refused ? HAlarm : new SolidColorBrush(Color.Parse("#5A6B7D"));
        hint.Text = refused ? _modeRefused : on
            ? "生產模式：只能從 Sequence 執行流程，手動 / 改配方 / 晶圓位置 / 保養已鎖住。"
            : "工程模式：可手動操作與編輯。確認機台正常後按 Go Online。";
        var btn = C<Button>("BtnOnline");
        btn.Content = on ? "Go Offline" : "Go Online";
        btn.Background = on ? new SolidColorBrush(Color.Parse("#E0E0E0")) : ModeOnline;
        btn.Foreground = on ? Brushes.Black : Brushes.White;
        btn.IsEnabled = _m is not null && !Busy && Can(on ? Permission.GoOffline : Permission.GoOnline);
        C<Button>("BtnSeq").IsEnabled = _m is not null && !Busy && Can(Permission.RunRecipe);
        C<Button>("BtnAbort").IsEnabled = Busy;
    }
}
