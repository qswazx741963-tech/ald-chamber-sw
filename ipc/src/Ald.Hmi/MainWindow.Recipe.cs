using System.Globalization;
using Ald.Core;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace Ald.Hmi;

/// <summary>
/// Recipe 頁（2026-11-24 改版，參考使用者給的配方表）：每個 Step 一欄橫向並排，參數一列一列直排：
/// 步驟名稱、結束條件、時間、預估時間、Cycles、子步驟（V1~V7 閥序 + ms）、O3、壓力、加熱溫度、MFC 1~8。
/// 選檔 → 編輯 → 檢查 → 存檔（版次 + 修改紀錄）。執行一律從主頁 Sequence。ONLINE 或權限不足（工程師以下）時只能看。
/// </summary>
public partial class MainWindow
{
    private readonly List<string> _runLog = new();
    private string _runName = "";
    private ProcessRecipe _recipe = new() { Name = "新配方" };
    private string? _recipePath;
    private bool _loadingRecipe;

    // 編輯中的內容
    private List<ProcessStep> _steps = new();
    private readonly HashSet<string> _badInput = new();   // 格式錯誤的輸入框（key = 步驟 + 欄位）
    private bool _dirty, _filling, _rEdit = true;
    private Action? _pendingDiscard;
    private string[] _valveNames = new string[7];
    /// <summary>MFC 格子：沒勾「設定」時顯示沿用的值，內容改了要跟著更新。</summary>
    private readonly List<(int Step, int Ch, CheckBox Set, NumericUpDown Num)> _mfcCells = new();
    private readonly List<(int Step, TextBlock Text)> _estCells = new();

    private const double LabelW = 150, ColW = 300;
    private static readonly IBrush HeadBg = new SolidColorBrush(Color.Parse("#D5DDE6"));
    private static readonly IBrush LabelBg = new SolidColorBrush(Color.Parse("#E8EDF2"));
    private static readonly IBrush SectionBg = new SolidColorBrush(Color.Parse("#C9D2DC"));
    private static readonly IBrush CellLine = new SolidColorBrush(Color.Parse("#C9D2DC"));
    private static readonly IBrush DimText = new SolidColorBrush(Color.Parse("#8A96A3"));
    private static readonly IBrush WarnText = new SolidColorBrush(Color.Parse("#A15C00"));
    private static readonly IBrush MarkBg = new SolidColorBrush(Color.Parse("#FFF3CD"));

    private static readonly (StepKind Kind, string Text)[] KindItems =
    [
        (StepKind.PumpDown, "到真空（抽真空）"),
        (StepKind.HeatCheck, "到溫度（確認溫度）"),
        (StepKind.Gas, "依時間（通氣 / Purge）"),
        (StepKind.Ald, "依 Cycle 數（ALD）"),
        (StepKind.Vent, "到大氣（回填）"),
    ];

    private string[] HeaterNames => _m?.Config.Heaters.Channels.Select(h => h.Name).ToArray() ?? Enumerable.Range(1, 16).Select(i => $"#{i}").ToArray();
    private ProcessHwConfig Hw => _m?.Config.Process ?? new ProcessHwConfig();

    private void WireRecipe()
    {
        _valveNames = ValveNames.Load(Hw.ValveNamesPath, Hw.AldValveNames);
        _recipe = NewRecipeTemplate();
        foreach (var tb in new[] { "TxtRName", "TxtRDesc" }) C<TextBox>(tb).TextChanged += (_, _) => Dirty();
        C<ScrollViewer>("SvRTable").ScrollChanged += (_, _) => FreezeHeaders();

        // 新增步驟（加在最後）
        var add = new MenuFlyout();
        foreach (var (kind, text) in KindItems)
        {
            var mi = new MenuItem { Header = text };
            mi.Click += (_, _) =>
            {
                if (_steps.Count >= ProcessRecipe.MaxSteps) { Msg($"步驟最多 {ProcessRecipe.MaxSteps} 個"); return; }
                _steps.Add(DefaultStep(kind));
                RebuildTable(); Dirty();
            };
            add.Items.Add(mi);
        }
        C<Button>("BtnRStepAdd").Flyout = add;

        // 閥管路名稱
        var vg = C<Grid>("GrdValveNames");
        var vbox = new TextBox[7];
        for (int i = 0; i < 7; i++)
        {
            vg.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var t = new TextBlock { Text = $"V{i + 1}", VerticalAlignment = VerticalAlignment.Center };
            vbox[i] = new TextBox { Margin = new(0, 1), Watermark = "例如 TMA、N2 Purge、O3" };
            Grid.SetRow(t, i); Grid.SetRow(vbox[i], i); Grid.SetColumn(vbox[i], 1);
            vg.Children.Add(t); vg.Children.Add(vbox[i]);
        }
        C<Button>("BtnRValveNames").Flyout!.Opened += (_, _) => { for (int i = 0; i < 7; i++) vbox[i].Text = _valveNames[i]; };
        C<Button>("BtnValveNamesSave").Click += (_, _) =>
        {
            try
            {
                _valveNames = vbox.Select(b => (b.Text ?? "").Trim()).ToArray();
                ValveNames.Save(Hw.ValveNamesPath, _valveNames);
                _alarmLog?.Add(OpSource, "Info", "ALD 閥管路名稱：" + string.Join("、", Enumerable.Range(0, 7).Select(i => ValveNames.Label(_valveNames, i))), evt: "OPERATOR");
                C<Button>("BtnRValveNames").Flyout!.Hide();
                RebuildTable();
            }
            catch (Exception e) { Msg($"閥名稱存檔失敗：{e.Message}"); }
        };

        // 檔案
        var cmb = C<ComboBox>("CmbRecipeFile");
        cmb.SelectionChanged += (_, _) =>
        {
            if (_loadingRecipe || cmb.SelectedItem is not string file) return;
            var path = Path.Combine(_recipeDir, file);
            if (_recipePath is not null && Path.GetFullPath(path) == Path.GetFullPath(_recipePath)) return;
            ConfirmDiscard($"開啟 {file}", () => OpenRecipe(path), () => SelectFileQuiet(_recipePath));
        };
        C<Button>("BtnRecipeReload").Click += (_, _) => ReloadRecipeList(null);
        C<Button>("BtnRecipeNew").Click += (_, _) => ConfirmDiscard("新配方", () =>
        {
            _recipe = NewRecipeTemplate();
            _recipePath = null;
            SelectFileQuiet(null);
            FillForm(_recipe);
            Msg("新配方：填好後按存檔（檔名用配方名稱）");
        }, null);
        C<Button>("BtnRecipeSave").Click += (_, _) => SaveRecipe(saveAs: false);
        C<Button>("BtnRecipeSaveAs").Click += (_, _) => SaveRecipe(saveAs: true);
        C<Button>("BtnRecipeCheck").Click += (_, _) =>
        {
            if (!TryReadForm(out var r)) return;
            var e = Check(r);
            var w = Warnings(r);
            Msg(e.Count == 0 && w.Count == 0 ? "檢查通過" : string.Join("\n", e.Concat(w.Select(x => "提醒：" + x))), ok: e.Count == 0);
        };
        C<Button>("BtnRecipeHist").Flyout!.Opened += (_, _) =>
        {
            var log = _recipePath is null ? "" : RecipeHistory.ReadLog(_recipePath);
            C<SelectableTextBlock>("TxtRecipeHist").Text = log.Length > 0 ? log : "這個配方還沒有修改紀錄（2026-11-12 之後在人機存檔才會記錄）";
        };
        C<Button>("BtnRecipeDiscardOk").Click += (_, _) => { C<Border>("BdRecipeDiscard").IsVisible = false; var a = _pendingDiscard; _pendingDiscard = null; _dirty = false; a?.Invoke(); };
        C<Button>("BtnRecipeDiscardNo").Click += (_, _) => { C<Border>("BdRecipeDiscard").IsVisible = false; _pendingDiscard = null; };

        FillForm(_recipe);
        ReloadRecipeList(null);
    }

    private ProcessRecipe NewRecipeTemplate()
        => ProcessRecipe.Of("新配方", DefaultStep(StepKind.PumpDown), DefaultStep(StepKind.Gas) with { Name = "通氣穩定" }, DefaultStep(StepKind.Ald));

    /// <summary>新增步驟的預設值。確認溫度預設帶入加熱板目前有開的組。</summary>
    private ProcessStep DefaultStep(StepKind kind) => kind switch
    {
        StepKind.PumpDown => ProcessStep.NewPumpDown(),
        StepKind.HeatCheck => ProcessStep.NewHeatCheck(Enumerable.Range(0, 16).Select(i => _m?.HeaterSetpoint(i) is { } sp && sp > 0 ? Math.Round(sp) : 0).ToArray()),
        StepKind.Gas => ProcessStep.NewGas(null, 10, "Purge"),
        StepKind.Ald => ProcessStep.NewAld(new AldRecipe { Name = "ALD", Cycles = 100, Steps = { new("Precursor A", 1, 20), new("Purge", 2, 5000), new("Precursor B", 4, 20), new("Purge", 2, 5000) } }),
        _ => ProcessStep.NewVent(),
    };

    // ------------------------------------------------------------ 檔案
    private void OpenRecipe(string path)
    {
        try { _recipe = RecipeFile.Load(path); _recipePath = path; FillForm(_recipe); SelectFileQuiet(path); Msg(""); }
        catch (Exception e) { Msg(e.Message); SelectFileQuiet(_recipePath); }
    }

    /// <summary>有未存檔的修改就先問；沒有就直接做。</summary>
    private void ConfirmDiscard(string what, Action go, Action? revert)
    {
        if (_checkDirty) { _checkDirty = false; _dirty = FormDiffers(); }
        if (!_dirty || !_rEdit) { go(); return; }
        revert?.Invoke();
        _pendingDiscard = go;
        C<TextBlock>("TxtRecipeDiscard").Text = $"「{_recipe.Name}」有未存檔的修改，要放棄修改並{what}嗎？";
        C<Border>("BdRecipeDiscard").IsVisible = true;
    }

    private void SelectFileQuiet(string? path)
    {
        _loadingRecipe = true;
        C<ComboBox>("CmbRecipeFile").SelectedItem = path is null ? null : Path.GetFileName(path);
        _loadingRecipe = false;
    }

    private void ReloadRecipeList(string? select)
    {
        var cmb = C<ComboBox>("CmbRecipeFile");
        var files = Directory.Exists(_recipeDir)
            ? Directory.GetFiles(_recipeDir, "*.json").Select(Path.GetFileName).OfType<string>().OrderBy(x => x).ToList()
            : new List<string>();
        _loadingRecipe = true;
        cmb.ItemsSource = files;
        cmb.SelectedItem = select ?? (_recipePath is null ? null : Path.GetFileName(_recipePath));
        _loadingRecipe = false;
        if (files.Count == 0) Msg($"配方資料夾 {_recipeDir} 沒有 .json");
    }

    // ------------------------------------------------------------ 表單 ↔ 配方
    private static AldRecipe Clone(AldRecipe a) => new()
    {
        Name = a.Name, Cycles = a.Cycles, O3SetPct = a.O3SetPct,
        Steps = a.Steps.Select(s => s with { Mfc = s.Mfc is null ? null : (double?[])s.Mfc.Clone() }).ToList(),
    };

    private static ProcessStep Clone(ProcessStep s) => s with
    {
        Mfc = s.Mfc is null ? null : (double?[])s.Mfc.Clone(),
        Heat = s.Heat is null ? null : s.Heat with { HeaterSpDegC = (double[])s.Heat.HeaterSpDegC.Clone() },
        Ald = s.Ald is null ? null : Clone(s.Ald),
    };

    private void FillForm(ProcessRecipe r)
    {
        _filling = true;
        try
        {
            C<TextBox>("TxtRName").Text = r.Name;
            C<TextBox>("TxtRDesc").Text = r.Description;
            _steps = r.Steps.Select(Clone).ToList();
            _badInput.Clear();
            RebuildTable();
        }
        finally { _filling = false; }
        _dirty = false;
        _checkDirty = false;
        C<Border>("BdRecipeDiscard").IsVisible = false;
    }

    /// <summary>表單內容跟最後讀進 / 存檔的版本不一樣（用內容比，不靠事件旗標，避免控制項初始化誤判）。</summary>
    private bool FormDiffers()
    {
        if (!TryReadForm(out var r, quiet: true)) return true;
        return RecipeFile.ToJson(r) != RecipeFile.ToJson(_recipe);
    }

    private bool TryReadForm(out ProcessRecipe r, bool quiet = false)
    {
        r = _recipe;
        try
        {
            string name = (C<TextBox>("TxtRName").Text ?? "").Trim();
            if (name.Length == 0) throw new FormatException("請填配方名稱");
            if (_badInput.Count > 0) throw new FormatException("有欄位格式錯誤：" + string.Join("、", _badInput.OrderBy(x => x)) + "（壓力請填正數，例如 5e-3）");
            var steps = _steps.Select(Clone).ToList();
            for (int i = 0; i < steps.Count; i++)
            {
                var s = steps[i];
                if (s.Name.Trim().Length == 0) s = steps[i] = s with { Name = KindName(s.Kind) };
                if (s.Ald is { } a) a.Name = s.Name;   // ALD 段名稱跟步驟名稱一致
            }
            r = _recipe with { Name = name, Description = (C<TextBox>("TxtRDesc").Text ?? "").Trim(), Steps = steps };
            return true;
        }
        catch (FormatException e) { if (!quiet) Msg(e.Message); return false; }
    }

    private static string KindName(StepKind k) => k switch
    {
        StepKind.PumpDown => "抽真空", StepKind.HeatCheck => "確認溫度", StepKind.Gas => "通氣", StepKind.Ald => "ALD", _ => "回填",
    };

    /// <summary>配方本身的檢查 + 這台機器的 config（加熱上限、MFC 滿刻度、RF 上限、RF / O3 是否已接上）。有錯就不能執行。</summary>
    private List<string> Check(ProcessRecipe r)
    {
        var e = r.Validate().ToList();
        if (_m is null) return e;
        var cfg = _m.Config;
        for (int k = 0; k < r.Steps.Count; k++)
        {
            var st = r.Steps[k];
            string at = $"Step {k + 1} {st.Name}";
            if (st.Heat is { } h && st.Kind == StepKind.HeatCheck)
                for (int i = 0; i < 16 && i < h.HeaterSpDegC.Length; i++)
                {
                    var ch = cfg.Heaters.Channels[i];
                    if (h.HeaterSpDegC[i] > ch.LimitDegC) e.Add($"{at}：加熱 #{i + 1} {ch.Name} {h.HeaterSpDegC[i]:0} °C 超過上限 {ch.LimitDegC:0} °C");
                    // 配方不改加熱：要的溫度跟加熱板常駐設定對不上，跑了也只會等到逾時
                    if (h.HeaterSpDegC[i] > 0 && _m.Snapshot.Connected && !(Math.Abs((_m.HeaterSetpoint(i) ?? double.NaN) - h.HeaterSpDegC[i]) <= h.ToleranceC))
                        e.Add($"{at}：加熱 #{i + 1} {ch.Name} 配方需要 {h.HeaterSpDegC[i]:0} °C，加熱板設定是 {(_m.HeaterSetpoint(i) is { } sp ? $"{sp:0} °C" : "關")}（請先到加熱頁設定）");
                }
            if (st.Mfc is { Length: 8 } m && st.UsesMfc)
                for (int c = 0; c < 8; c++)
                    if (m[c] > cfg.Mfc.Channels[c].FullScale)
                        e.Add($"{at}：MFC {c + 1} {m[c]} 超過滿刻度 {cfg.Mfc.Channels[c].FullScale} {cfg.Mfc.Channels[c].Unit}");
            if (st.Ald is { } a && st.Kind == StepKind.Ald)
            {
                for (int j = 0; j < a.Steps.Count; j++)
                {
                    var s = a.Steps[j];
                    if (s.Mfc is { Length: 8 } sm)
                        for (int c = 0; c < 8; c++)
                            if (sm[c] > cfg.Mfc.Channels[c].FullScale)
                                e.Add($"{at} 子步驟 {j + 1}：MFC {c + 1} {sm[c]} 超過滿刻度 {cfg.Mfc.Channels[c].FullScale} {cfg.Mfc.Channels[c].Unit}");
                    if (s.Rf1W > cfg.Process.Rf1MaxW) e.Add($"{at} 子步驟 {j + 1}：RF1 {s.Rf1W:0} W 超過上限 {cfg.Process.Rf1MaxW:0} W");
                    if (s.Rf2W > cfg.Process.Rf2MaxW) e.Add($"{at} 子步驟 {j + 1}：RF2 {s.Rf2W:0} W 超過上限 {cfg.Process.Rf2MaxW:0} W");
                }
                if (a.UsesRf && !cfg.Process.RfReady) e.Add($"{at} 有 RF：RF 控制還沒接上（硬體確認後才能執行，可以先存檔）");
                if (a.O3SetPct > 0 && !cfg.Process.O3Ready) e.Add($"{at} 有用 O3：O3 產生器控制還沒接上（硬體確認後才能執行，可以先存檔）");
            }
        }
        return e;
    }

    /// <summary>不擋執行的提醒。</summary>
    private static List<string> Warnings(ProcessRecipe r)
    {
        var w = new List<string>();
        for (int k = 0; k < r.Steps.Count; k++)
            if (r.Steps[k].Ald is { } a && r.Steps[k].Kind == StepKind.Ald)
                for (int j = 0; j < a.Steps.Count; j++)
                    if (a.Steps[j].HasMfcChange && a.Steps[j].DurationMs < 1000)
                        w.Add($"Step {k + 1} 子步驟 {j + 1} 只有 {a.Steps[j].DurationMs} ms 卻切換 MFC：MFC 反應約 0.5–1 s，流量可能還沒到位就換下一步");
        if (!r.Steps.Any(s => s.Kind == StepKind.Ald)) w.Add("沒有 ALD 步驟");
        if (r.Steps.Any(s => s.Kind == StepKind.Ald) && !r.Steps.TakeWhile(s => s.Kind != StepKind.Ald).Any(s => s.Kind == StepKind.PumpDown))
            w.Add("ALD 之前沒有抽真空步驟：要確定 PM 已經在真空");
        return w;
    }

    private void SaveRecipe(bool saveAs)
    {
        if (!_rEdit) return;
        if (!TryReadForm(out var r)) return;
        var e = r.Validate().ToList();   // 格式錯誤不准存；機台狀態類（溫度未到等）可以存
        if (e.Count > 0) { Msg("不能存檔：\n" + string.Join("\n", e)); return; }
        bool isNew = saveAs || _recipePath is null;
        var path = isNew ? Path.Combine(_recipeDir, RecipeFile.FileNameFor(r.Name)) : _recipePath!;
        if (isNew && File.Exists(path)) { Msg($"已經有 {Path.GetFileName(path)}，請先把配方名稱改成別的名字"); return; }
        if (isNew) r = r with { Revision = 0 };
        try
        {
            var before = r.Revision;
            var saved = RecipeHistory.Save(path, r, _user ?? "未登入");
            _recipe = saved;
            _recipePath = path;
            _dirty = false;
            ReloadRecipeList(Path.GetFileName(path));
            var w = Warnings(saved);
            Msg((saved.Revision == before && !isNew ? "內容沒有變更，不用存檔" : $"已存檔：{Path.GetFileName(path)}（版次 {saved.Revision}）")
                + (w.Count > 0 ? "\n" + string.Join("\n", w.Select(x => "提醒：" + x)) : ""), ok: true);
            if (isNew || saved.Revision != before)
                _alarmLog?.Add(OpSource, "Info", $"Recipe 存檔 {Path.GetFileName(path)} 版次 {saved.Revision}", evt: "OPERATOR");
        }
        catch (Exception ex) { Msg($"存檔失敗：{ex.Message}"); }
    }

    private static readonly IBrush MsgOk = new SolidColorBrush(Color.Parse("#2E7D32"));
    private static readonly IBrush MsgErr = new SolidColorBrush(Color.Parse("#B03030"));

    private void Msg(string text, bool ok = false)
    {
        var t = C<SelectableTextBlock>("TxtRecipeMsg");
        t.Text = text;
        t.Foreground = ok ? MsgOk : MsgErr;
    }

    private void Dirty()
    {
        if (_filling) return;
        _checkDirty = true;
        RefreshComputed();
    }
    private bool _checkDirty;

    // ------------------------------------------------------------ 表格
    private Grid _tbl = null!;

    /// <summary>整張表重畫（步驟增刪、搬移、換種類、子步驟增刪時）。數值修改不重畫，只更新計算欄。</summary>
    private void RebuildTable()
    {
        bool was = _filling;
        _filling = true;
        try { BuildTable(); }
        finally { _filling = was; }
        FreezeHeaders();
        RefreshComputed();
    }

    private void BuildTable()
    {
        _tbl = C<Grid>("GrdRTable");
        var g = _tbl;
        g.Children.Clear();
        g.RowDefinitions.Clear();
        g.ColumnDefinitions.Clear();
        _frozen.Clear();
        _mfcCells.Clear();
        _estCells.Clear();
        g.ColumnDefinitions.Add(new ColumnDefinition(LabelW, GridUnitType.Pixel));
        for (int k = 0; k < _steps.Count; k++) g.ColumnDefinitions.Add(new ColumnDefinition(ColW, GridUnitType.Pixel));
        var mfc = _m?.Config.Mfc.Channels;

        int row = 0;
        int NewRow() { g.RowDefinitions.Add(new RowDefinition(GridLength.Auto)); return row++; }

        // 表頭
        int r0 = NewRow();
        Put(new TextBlock { Text = _steps.Count == 0 ? "（沒有步驟，按下方「＋ 新增步驟」）" : "", FontWeight = FontWeight.Bold }, r0, 0, HeadBg);
        for (int k = 0; k < _steps.Count; k++) Put(StepHead(k), r0, k + 1, HeadBg);

        void Row(string label, Func<int, ProcessStep, Control?> cell, string? tip = null)
        {
            int r = NewRow();
            var t = new TextBlock { Text = label, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
            if (tip is not null) ToolTip.SetTip(t, tip);
            Put(t, r, 0, LabelBg);
            for (int k = 0; k < _steps.Count; k++) Put(cell(k, _steps[k]) ?? Na(), r, k + 1, null);
        }
        void Section(string label, string colText)
        {
            int r = NewRow();
            Put(new TextBlock { Text = label, FontWeight = FontWeight.Bold }, r, 0, SectionBg);
            for (int k = 0; k < _steps.Count; k++)
                Put(new TextBlock { Text = _steps[k].UsesMfc ? colText : "", FontSize = 12, Foreground = Brushes.Black }, r, k + 1, SectionBg);
        }

        Row("步驟名稱", NameCell);
        Row("結束條件", KindCell, "到真空 / 到溫度 / 到大氣：條件到了就結束；依時間：時間到結束；依 Cycle 數：ALD 跑完結束");
        Row("時間 / 最大時間 (s)", TimeCell, "依時間：這步的時間。其他：最大時間，超過就停止並報警（ALD 填 0 = 自動）");
        Row("預估時間", EstCell);
        Row("Cycles", (k, s) => s.Kind == StepKind.Ald ? Num(s.Ald!.Cycles, 1, 60000, 10, "0", v => { _steps[k].Ald!.Cycles = (int)v; Dirty(); }) : null);
        Row("子步驟（閥序）", SubStepCell, "製程板 1 kHz 執行：每個 cycle 依序跑這些子步驟。勾 = 閥開。⋯ 可以改名稱、切換 MFC、RF");
        Row("O3 (%)", (k, s) => s.Kind == StepKind.Ald ? Num(s.Ald!.O3SetPct, 0, 100, 1, "0.#", v => { _steps[k].Ald!.O3SetPct = v; Dirty(); }) : null,
            "O3 產生器設定（0 = 不用）。產生器在這步開始前開好；打進腔體由 V1~V7 其中一顆閥控制（硬體待確認）");
        Row("壓力", PressureCell, "本機沒有節流閥 / 壓力控制閥：抽真空、回填以外的步驟維持目前抽氣狀態");
        Row("加熱溫度", HeatCell, "加熱是常駐設定（在加熱頁設），配方只確認溫度有沒有到，不會改加熱設定");
        Section("氣體（MFC）", "勾選 = 這步開始時設定；沒勾 = 沿用");
        for (int c = 0; c < 8; c++)
        {
            int ch = c;
            string label = mfc is null ? $"{c + 1}" : $"{c + 1} - {mfc[c].Name}";
            Row(label, (k, s) => MfcCell(k, s, ch));
        }
    }

    private readonly List<(Border Cell, bool Col, bool Row)> _frozen = new();

    private void Put(Control c, int row, int col, IBrush? bg)
    {
        var b = new Border
        {
            Child = c, Background = bg, BorderBrush = CellLine, BorderThickness = new(0, 0, 1, 1), Padding = new(4, 2),
            [Grid.RowProperty] = row, [Grid.ColumnProperty] = col,
        };
        // 左邊參數名稱欄、上面 Step 表頭捲動時固定不動（跟參考的配方表一樣）
        if (col == 0 || row == 0)
        {
            b.ZIndex = (col == 0 ? 1 : 0) + (row == 0 ? 2 : 0);
            b.RenderTransform = new TranslateTransform();
            _frozen.Add((b, col == 0, row == 0));
        }
        _tbl.Children.Add(b);
    }

    private void FreezeHeaders()
    {
        var off = C<ScrollViewer>("SvRTable").Offset;
        foreach (var (b, col, row) in _frozen)
            if (b.RenderTransform is TranslateTransform t) { t.X = col ? off.X : 0; t.Y = row ? off.Y : 0; }
    }

    private static TextBlock Na(string text = "—") => new() { Text = text, Foreground = DimText, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };

    private NumericUpDown Num(double value, double min, double max, double inc, string fmt, Action<double> set, double width = 110)
    {
        var n = new NumericUpDown
        {
            Minimum = (decimal)min, Maximum = (decimal)max, Increment = (decimal)inc, FormatString = fmt, ShowButtonSpinner = false,
            Value = (decimal)Math.Clamp(value, min, max), Width = width, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
        };
        n.ValueChanged += (_, _) => { if (!_filling) set((double)(n.Value ?? (decimal)min)); };
        return n;
    }

    private void SetStep(int k, Func<ProcessStep, ProcessStep> f) { if (_filling) return; _steps[k] = f(_steps[k]); Dirty(); }

    private Control StepHead(int k)
    {
        var p = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        p.Children.Add(new TextBlock { Text = $"Step {k + 1}", FontWeight = FontWeight.Bold, FontSize = 15, VerticalAlignment = VerticalAlignment.Center });
        var ops = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, [Grid.ColumnProperty] = 1 };
        void Op(string t, string tip, bool enabled, Action a)
        {
            var b = new Button { Content = t, Padding = new(6, 1), MinWidth = 0, IsEnabled = enabled && _rEdit };
            ToolTip.SetTip(b, tip);
            b.Click += (_, _) => { a(); RebuildTable(); Dirty(); };
            ops.Children.Add(b);
        }
        Op("◀", "往前移", k > 0, () => (_steps[k - 1], _steps[k]) = (_steps[k], _steps[k - 1]));
        Op("▶", "往後移", k < _steps.Count - 1, () => (_steps[k + 1], _steps[k]) = (_steps[k], _steps[k + 1]));
        Op("⧉", "複製這一步（插在後面）", _steps.Count < ProcessRecipe.MaxSteps, () => _steps.Insert(k + 1, Clone(_steps[k]) with { Name = _steps[k].Name + " 複製" }));
        Op("✕", "刪除這一步", true, () => _steps.RemoveAt(k));
        p.Children.Add(ops);
        return p;
    }

    private Control NameCell(int k, ProcessStep s)
    {
        var t = new TextBox { Text = s.Name };
        t.TextChanged += (_, _) => SetStep(k, x => x with { Name = (t.Text ?? "").Trim() });
        return t;
    }

    private Control KindCell(int k, ProcessStep s)
    {
        var cmb = new ComboBox { ItemsSource = KindItems.Select(x => x.Text).ToList(), SelectedIndex = Array.FindIndex(KindItems, x => x.Kind == s.Kind), HorizontalAlignment = HorizontalAlignment.Stretch };
        cmb.SelectionChanged += (_, _) =>
        {
            if (_filling || cmb.SelectedIndex < 0) return;
            var kind = KindItems[cmb.SelectedIndex].Kind;
            if (kind == _steps[k].Kind) return;
            var old = _steps[k];
            var n = DefaultStep(kind);
            if (n.UsesMfc && old.UsesMfc) n = n with { Mfc = old.Mfc };   // 氣體設定盡量保留
            _steps[k] = n;
            RebuildTable(); Dirty();
        };
        return cmb;
    }

    private Control TimeCell(int k, ProcessStep s)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        double max = s.Kind == StepKind.Ald ? 864000 : 36000;
        p.Children.Add(Num(s.TimeS, 0, max, s.Kind == StepKind.Gas ? 1 : 60, "0.#", v => SetStep(k, x => x with { TimeS = v })));
        p.Children.Add(new TextBlock
        {
            Text = s.Kind switch { StepKind.Gas => "時間", StepKind.Ald => "最大（0 = 自動）", _ => "最大時間" },
            Foreground = DimText, FontSize = 12, VerticalAlignment = VerticalAlignment.Center,
        });
        return p;
    }

    private Control EstCell(int k, ProcessStep s)
    {
        var t = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        _estCells.Add((k, t));
        return t;
    }

    private static string Hms(double s) => TimeSpan.FromSeconds(Math.Round(s)).ToString(@"hh\:mm\:ss");

    private string EstText(ProcessStep s) => s.Kind switch
    {
        StepKind.Gas => $"{s.TimeS:0.#} s",
        StepKind.Ald when s.Ald is { } a => $"{Hms(a.TotalSeconds)}（每 cycle {a.CycleSeconds:0.###} s）"
            + (a.MfcPresets().Count > 0 ? $"\nMFC 切換 {a.MfcPresets().Count}/{AldRecipe.MaxMfcPresets} 種" : ""),
        StepKind.PumpDown => "看實際抽氣",
        StepKind.HeatCheck => "已到溫就直接下一步",
        _ => "看實際回填",
    };

    private Control? PressureCell(int k, ProcessStep s)
    {
        TextBox Torr(double v, string key, Func<ProcessStep, double, ProcessStep> set)
        {
            var t = new TextBox { Text = v.ToString("G3", CultureInfo.InvariantCulture), Width = 80 };
            t.TextChanged += (_, _) =>
            {
                if (_filling) return;
                string id = $"Step {k + 1} {key}";
                if (double.TryParse(t.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var x) && x > 0 && double.IsFinite(x))
                { _badInput.Remove(id); SetStep(k, st => set(st, x)); }
                else { _badInput.Add(id); Dirty(); }
            };
            return t;
        }
        Control Line(string label, Control box, string unit)
        {
            var p = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new(0, 1) };
            p.Children.Add(new TextBlock { Text = label, Width = 66, VerticalAlignment = VerticalAlignment.Center });
            p.Children.Add(box);
            p.Children.Add(new TextBlock { Text = unit, VerticalAlignment = VerticalAlignment.Center });
            return p;
        }
        switch (s.Kind)
        {
            case StepKind.PumpDown:
            {
                var p = s.Pump ?? new PumpParams();
                var sp = new StackPanel();
                sp.Children.Add(Line("抽到 Base", Torr(p.BaseTorr, "Base", (st, x) => st with { Pump = (st.Pump ?? new()) with { BaseTorr = x } }), "Torr"));
                sp.Children.Add(Line("Crossover", Torr(p.CrossoverTorr, "Crossover", (st, x) => st with { Pump = (st.Pump ?? new()) with { CrossoverTorr = x } }), "Torr（切 Turbo）"));
                return sp;
            }
            case StepKind.Vent:
            {
                var v = s.Vent ?? new VentParams();
                return Line("回填到", Num(v.TargetTorr, 100, 800, 10, "0", x => SetStep(k, st => st with { Vent = (st.Vent ?? new()) with { TargetTorr = x } }), 90), "Torr");
            }
            default: return Na("維持（不控壓）");
        }
    }

    private Control? HeatCell(int k, ProcessStep s)
    {
        if (s.Kind != StepKind.HeatCheck) return Na("維持常駐設定");
        var h = s.Heat ?? new HeatParams();
        var names = HeaterNames;
        var p = new StackPanel { Spacing = 2 };
        for (int ch = 0; ch < 16; ch++)
        {
            if (!(ch < h.HeaterSpDegC.Length && h.HeaterSpDegC[ch] > 0)) continue;
            int c = ch;
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,80,Auto,Auto") };
            row.Children.Add(new TextBlock { Text = $"#{c + 1} {names[c]}", VerticalAlignment = VerticalAlignment.Center, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis });
            var n = Num(h.HeaterSpDegC[c], 1, _m?.Config.Heaters.Channels[c].LimitDegC ?? 650, 5, "0", v => { _steps[k].Heat!.HeaterSpDegC[c] = v; Dirty(); }, 76);
            Grid.SetColumn(n, 1);
            row.Children.Add(n);
            row.Children.Add(new TextBlock { Text = "°C", VerticalAlignment = VerticalAlignment.Center, Margin = new(4, 0), [Grid.ColumnProperty] = 2 });
            var x = new Button { Content = "✕", Padding = new(6, 1), MinWidth = 0, [Grid.ColumnProperty] = 3 };
            ToolTip.SetTip(x, "不確認這組");
            x.Click += (_, _) => { _steps[k].Heat!.HeaterSpDegC[c] = 0; RebuildTable(); Dirty(); };
            row.Children.Add(x);
            p.Children.Add(row);
        }
        var add = new ComboBox
        {
            PlaceholderText = "＋ 加入要確認的加熱組", HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = Enumerable.Range(0, 16).Where(ch => !(h.HeaterSpDegC[ch] > 0)).Select(ch => new ComboBoxItem { Content = $"#{ch + 1} {names[ch]}", Tag = ch }).ToList(),
        };
        add.SelectionChanged += (_, _) =>
        {
            if (_filling || add.SelectedItem is not ComboBoxItem { Tag: int ch }) return;
            _steps[k].Heat!.HeaterSpDegC[ch] = _m?.HeaterSetpoint(ch) is { } sp && sp > 0 ? Math.Round(sp) : 100;
            RebuildTable(); Dirty();
        };
        p.Children.Add(add);
        var tol = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new(0, 2, 0, 0) };
        tol.Children.Add(new TextBlock { Text = "容差 ±", VerticalAlignment = VerticalAlignment.Center });
        tol.Children.Add(Num(h.ToleranceC, 0.5, 50, 0.5, "0.0", v => SetStep(k, st => st with { Heat = st.Heat! with { ToleranceC = v } }), 60));
        tol.Children.Add(new TextBlock { Text = "°C　穩定", VerticalAlignment = VerticalAlignment.Center });
        tol.Children.Add(Num(h.SettleS, 0, 36000, 10, "0", v => SetStep(k, st => st with { Heat = st.Heat! with { SettleS = v } }), 60));
        tol.Children.Add(new TextBlock { Text = "s", VerticalAlignment = VerticalAlignment.Center });
        p.Children.Add(tol);
        return p;
    }

    private Control? MfcCell(int k, ProcessStep s, int ch)
    {
        if (!s.UsesMfc) return s.Kind == StepKind.Vent ? Na("關") : Na();
        var fs = _m?.Config.Mfc.Channels[ch].FullScale ?? 2000;
        var m = s.Mfc;
        bool set = m is { } a && ch < a.Length && a[ch] is not null;
        var p = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        var cb = new CheckBox { IsChecked = set, MinWidth = 0, Content = "設定", VerticalAlignment = VerticalAlignment.Center };
        var n = new NumericUpDown { Minimum = 0, Maximum = (decimal)fs, Increment = 1, FormatString = "0.#", ShowButtonSpinner = false, Width = 90, IsEnabled = set, Value = set ? (decimal)Math.Clamp(m![ch]!.Value, 0, fs) : 0 };
        void Write()
        {
            if (_filling) return;
            var arr = _steps[k].Mfc is { Length: 8 } cur ? (double?[])cur.Clone() : new double?[8];
            arr[ch] = cb.IsChecked == true ? (double)(n.Value ?? 0) : null;
            _steps[k] = _steps[k] with { Mfc = arr.Any(x => x is not null) ? arr : null };
            Dirty();
        }
        cb.IsCheckedChanged += (_, _) => { n.IsEnabled = cb.IsChecked == true; Write(); };
        n.ValueChanged += (_, _) => { if (cb.IsChecked == true) Write(); };
        p.Children.Add(cb);
        p.Children.Add(n);
        _mfcCells.Add((k, ch, cb, n));
        return p;
    }

    // ------------------------------------------------------------ 子步驟
    private Control? SubStepCell(int k, ProcessStep s)
    {
        if (s.Kind != StepKind.Ald || s.Ald is not { } ald) return null;
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition(20, GridUnitType.Pixel));
        for (int v = 0; v < 7; v++) g.ColumnDefinitions.Add(new ColumnDefinition(26, GridUnitType.Pixel));
        g.ColumnDefinitions.Add(new ColumnDefinition(60, GridUnitType.Pixel));
        g.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        void Add(Control c, int r, int col) { Grid.SetRow(c, r); Grid.SetColumn(c, col); g.Children.Add(c); }
        static TextBlock H(string t) => new() { Text = t, FontSize = 11, FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center };

        g.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        Add(H("#"), 0, 0);
        for (int v = 0; v < 7; v++)
        {
            var h = H($"V{v + 1}");
            ToolTip.SetTip(h, ValveNames.Label(_valveNames, v));
            Add(h, 0, 1 + v);
        }
        Add(H("ms"), 0, 8);
        for (int i = 0; i < ald.Steps.Count; i++)
        {
            int j = i, r = i + 1;
            var st = ald.Steps[j];
            g.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            if (st.HasMfcChange || st.Rf1W > 0 || st.Rf2W > 0)
                Add(new Border { Background = MarkBg, [Grid.ColumnSpanProperty] = 10 }, r, 0);
            var idx = new TextBlock { Text = $"{j + 1}", VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            ToolTip.SetTip(idx, RecipeText.Step(st, _valveNames));
            Add(idx, r, 0);
            for (int v = 0; v < 7; v++)
            {
                int bit = v;
                var cb = new CheckBox { IsChecked = (st.ValveMask >> bit & 1) != 0, HorizontalAlignment = HorizontalAlignment.Center, MinWidth = 0, Padding = new(0) };
                ToolTip.SetTip(cb, ValveNames.Label(_valveNames, bit));
                cb.IsCheckedChanged += (_, _) =>
                {
                    if (_filling) return;
                    ald.Steps[j] = ald.Steps[j] with { ValveMask = (ushort)(cb.IsChecked == true ? ald.Steps[j].ValveMask | 1 << bit : ald.Steps[j].ValveMask & ~(1 << bit)) };
                    Dirty();
                };
                Add(cb, r, 1 + v);
            }
            Add(Num(st.DurationMs, 1, 60000, 10, "0", x => { ald.Steps[j] = ald.Steps[j] with { DurationMs = (int)x }; Dirty(); }, 56), r, 8);
            Add(SubStepMore(k, ald, j), r, 9);
        }
        var add = new Button { Content = "＋ 子步驟", Padding = new(8, 2), Margin = new(0, 4, 0, 0), IsEnabled = ald.Steps.Count < AldRecipe.MaxSteps };
        ToolTip.SetTip(add, $"每段最多 {AldRecipe.MaxSteps} 個子步驟");
        add.Click += (_, _) => { ald.Steps.Add(new AldStep("Purge", 0, 1000)); RebuildTable(); Dirty(); };
        g.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        Grid.SetColumnSpan(add, 9);
        Add(add, ald.Steps.Count + 1, 0);
        return g;
    }

    /// <summary>子步驟的「⋯」：名稱、MFC 切換、RF、上下移、複製、刪除。</summary>
    private Button SubStepMore(int k, AldRecipe seg, int j)
    {
        var st = seg.Steps[j];
        bool mark = st.HasMfcChange || st.Rf1W > 0 || st.Rf2W > 0;
        var b = new Button { Content = mark ? "⋯ *" : "⋯", Padding = new(6, 1), MinWidth = 0, Margin = new(4, 1, 0, 1) };
        ToolTip.SetTip(b, RecipeText.Step(st, _valveNames));
        var mfc = _m?.Config.Mfc.Channels;
        var panel = new StackPanel { Spacing = 6, Width = 360 };
        panel.Children.Add(new TextBlock { Text = $"Step {k + 1} 子步驟 {j + 1}", FontWeight = FontWeight.Bold });
        var name = new TextBox { Text = st.Label, Watermark = "名稱，例如 TMA pulse" };
        panel.Children.Add(name);
        panel.Children.Add(new TextBlock { Text = "這個子步驟開始時切換 MFC（沒勾 = 沿用；切換後一直維持到下次切換）", Foreground = Brushes.Gray, FontSize = 12, TextWrapping = TextWrapping.Wrap });
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,90") };
        var checks = new CheckBox[8];
        var nums = new NumericUpDown[8];
        for (int c = 0; c < 8; c++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            bool on = st.Mfc is { } m && c < m.Length && m[c] is not null;
            checks[c] = new CheckBox { Content = mfc is null ? $"MFC {c + 1}" : $"{c + 1} {mfc[c].Name}（{mfc[c].Unit}）", IsChecked = on };
            nums[c] = new NumericUpDown { Minimum = 0, Maximum = (decimal)(mfc?[c].FullScale ?? 2000), Increment = 1, FormatString = "0.#", ShowButtonSpinner = false, IsEnabled = on, Value = on ? (decimal)st.Mfc![c]!.Value : 0 };
            int ch = c;
            checks[c].IsCheckedChanged += (_, _) => nums[ch].IsEnabled = checks[ch].IsChecked == true;
            Grid.SetRow(checks[c], c); Grid.SetRow(nums[c], c); Grid.SetColumn(nums[c], 1);
            grid.Children.Add(checks[c]); grid.Children.Add(nums[c]);
        }
        panel.Children.Add(grid);
        NumericUpDown? rf1 = null, rf2 = null;
        if (Hw.Rf1MaxW > 0 || Hw.Rf2MaxW > 0)
        {
            var rf = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            if (Hw.Rf1MaxW > 0) { rf.Children.Add(new TextBlock { Text = "RF1 W", VerticalAlignment = VerticalAlignment.Center }); rf.Children.Add(rf1 = new NumericUpDown { Minimum = 0, Maximum = (decimal)Hw.Rf1MaxW, Increment = 10, FormatString = "0", ShowButtonSpinner = false, Width = 80, Value = (decimal)st.Rf1W }); }
            if (Hw.Rf2MaxW > 0) { rf.Children.Add(new TextBlock { Text = "RF2 W", VerticalAlignment = VerticalAlignment.Center }); rf.Children.Add(rf2 = new NumericUpDown { Minimum = 0, Maximum = (decimal)Hw.Rf2MaxW, Increment = 10, FormatString = "0", ShowButtonSpinner = false, Width = 80, Value = (decimal)st.Rf2W }); }
            if (!Hw.RfReady) rf.Children.Add(new TextBlock { Text = "（RF 未接上）", Foreground = WarnText, VerticalAlignment = VerticalAlignment.Center });
            panel.Children.Add(rf);
        }
        var fly = new Flyout { Content = panel, Placement = PlacementMode.Bottom };
        var ops = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        void Op(string t, bool enabled, Action a)
        {
            var x = new Button { Content = t, IsEnabled = enabled && _rEdit };
            x.Click += (_, _) => { fly.Hide(); a(); RebuildTable(); Dirty(); };
            ops.Children.Add(x);
        }
        Op("套用", true, () =>
        {
            var m = Enumerable.Range(0, 8).Select(i => checks[i].IsChecked == true ? (double?)(double)(nums[i].Value ?? 0) : null).ToArray();
            seg.Steps[j] = seg.Steps[j] with
            {
                Label = (name.Text ?? "").Trim(), Mfc = m.Any(x => x is not null) ? m : null,
                Rf1W = (double)(rf1?.Value ?? 0), Rf2W = (double)(rf2?.Value ?? 0),
            };
        });
        Op("↑", j > 0, () => (seg.Steps[j - 1], seg.Steps[j]) = (seg.Steps[j], seg.Steps[j - 1]));
        Op("↓", j < seg.Steps.Count - 1, () => (seg.Steps[j + 1], seg.Steps[j]) = (seg.Steps[j], seg.Steps[j + 1]));
        Op("複製", seg.Steps.Count < AldRecipe.MaxSteps, () => seg.Steps.Insert(j + 1, seg.Steps[j] with { Mfc = seg.Steps[j].Mfc?.ToArray() }));
        Op("刪除", true, () => seg.Steps.RemoveAt(j));
        panel.Children.Add(ops);
        panel.IsEnabled = true;
        name.IsEnabled = grid.IsEnabled = _rEdit;
        b.Flyout = fly;
        return b;
    }

    /// <summary>更新計算欄：預估時間、沿用的 MFC 值、總時間。</summary>
    private void RefreshComputed()
    {
        bool was = _filling;
        _filling = true;
        try
        {
            var cur = MfcAtStepsOf(_steps);
            foreach (var (k, ch, cb, n) in _mfcCells)
                if (cb.IsChecked != true && k < cur.Count) n.Value = (decimal)Math.Min((double)n.Maximum, cur[k][ch]);
            foreach (var (k, t) in _estCells)
                if (k < _steps.Count) t.Text = EstText(_steps[k]);
        }
        finally { _filling = was; }
        double ald = _steps.Where(s => s.Kind == StepKind.Ald && s.Ald is not null).Sum(s => s.Ald!.TotalSeconds);
        double gas = _steps.Where(s => s.Kind == StepKind.Gas).Sum(s => s.TimeS);
        C<TextBlock>("TxtRTotal").Text = _steps.Count == 0 ? "" :
            $"共 {_steps.Count} 步，ALD 約 {Hms(ald)}、通氣 {Hms(gas)}（不含抽真空、等溫度、回填）";
    }

    private static List<double[]> MfcAtStepsOf(List<ProcessStep> steps) => new ProcessRecipe { Steps = steps }.MfcAtSteps();

    // ------------------------------------------------------------ 每 200 ms
    private void RenderRecipe()
    {
        bool edit = Can(Permission.EditRecipe);   // ONLINE 時 Can() 一律 false
        if (edit != _rEdit)
        {
            _rEdit = edit;
            C<Grid>("PnlRecipeHead").IsEnabled = edit;
            C<Grid>("GrdRTable").IsEnabled = edit;
            C<Button>("BtnRStepAdd").IsEnabled = edit;
            C<Button>("BtnRValveNames").IsEnabled = edit;
            C<Button>("BtnRecipeSave").IsEnabled = edit;
            C<Button>("BtnRecipeSaveAs").IsEnabled = edit;
            C<Button>("BtnRecipeNew").IsEnabled = edit;
            RebuildTable();   // 表頭的搬移 / 刪除鍵跟著鎖
        }
        var lockBox = C<Border>("BdRecipeLock");
        lockBox.IsVisible = !edit;
        if (!edit)
            C<TextBlock>("TxtRecipeLock").Text = _mode == ControlMode.Online
                ? "ONLINE 中：配方只能看。要修改請先回主頁按 Go Offline。"
                : "目前權限只能看配方，要修改請用工程師以上帳號登入（右上角選單）。";
        if (_checkDirty) { _checkDirty = false; _dirty = FormDiffers(); }
        var r = _recipe;
        C<TextBlock>("TxtRecipeRev").Text = (_recipePath is null ? "（未存檔的新配方）" : r.Revision == 0 ? "版次 -（還沒在人機存檔過）" : $"版次 {r.Revision}" +
            (r.ModifiedAt is { } t ? $"，{r.ModifiedBy} 修改於 {t:yyyy-MM-dd HH:mm}" : "")) + (_dirty ? "　● 有未存檔的修改" : "");
    }
}
