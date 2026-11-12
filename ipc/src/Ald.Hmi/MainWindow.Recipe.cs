using System.Globalization;
using Ald.Core;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace Ald.Hmi;

/// <summary>
/// Recipe 頁（2026-11-12 改版）：選檔 → 編輯 → 檢查 → 存檔（版次 + 修改紀錄）。執行一律從主頁 Sequence。
/// 左邊是配方整體設定（抽真空、溫度檢查、起始 MFC、結束、進階逾時），右邊是 ALD 各段與步驟表。
/// ONLINE 或權限不足（工程師以下）時只能看。
/// 表單沒有的欄位沿用讀進來的檔案內容，存檔不會被洗掉。
/// </summary>
public partial class MainWindow
{
    private readonly List<string> _runLog = new();
    private string _runName = "";
    private ProcessRecipe _recipe = new() { Name = "新配方" };
    private string? _recipePath;
    private bool _loadingRecipe;

    // 編輯中的內容
    private List<AldRecipe> _segs = new();
    private int _segSel;
    private readonly double[] _heatSp = new double[16];
    private readonly NumericUpDown[] _rMfc = new NumericUpDown[8];
    private readonly Dictionary<string, NumericUpDown> _rAdv = new();
    private bool _dirty, _filling, _rEdit = true;
    private Action? _pendingDiscard;
    private string[] _valveNames = new string[7];

    private static readonly IBrush SegOn = new SolidColorBrush(Color.Parse("#1565C0"));
    private static readonly IBrush SegOff = new SolidColorBrush(Color.Parse("#E3E8EE"));
    private static readonly IBrush RowAlt = new SolidColorBrush(Color.Parse("#F3F6F9"));
    private static readonly IBrush WarnText = new SolidColorBrush(Color.Parse("#A15C00"));

    /// <summary>進階欄位：名稱、最小、最大、遞增、格式。</summary>
    private static readonly (string Key, string Label, double Min, double Max, double Inc, string Fmt)[] AdvFields =
    [
        ("pump", "抽氣逾時 s", 10, 36000, 60, "0"),
        ("turboUp", "Turbo 啟動逾時 s", 10, 7200, 60, "0"),
        ("heat", "等溫度逾時 s", 10, 36000, 60, "0"),
        ("ventTarget", "回填目標 Torr", 100, 800, 10, "0"),
        ("vent", "回填逾時 s", 10, 7200, 60, "0"),
        ("turboDown", "Turbo 停止逾時 s", 10, 7200, 60, "0"),
    ];

    private string[] HeaterNames => _m?.Config.Heaters.Channels.Select(h => h.Name).ToArray() ?? Enumerable.Range(1, 16).Select(i => $"#{i}").ToArray();
    private ProcessHwConfig Hw => _m?.Config.Process ?? new ProcessHwConfig();

    private void WireRecipe()
    {
        _valveNames = ValveNames.Load(Hw.ValveNamesPath, Hw.AldValveNames);

        // 起始 MFC：8 列
        var g = C<Grid>("GrdRMfc");
        var mfc = _m?.Config.Mfc.Channels;
        for (int i = 0; i < 8; i++)
        {
            g.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var t = new TextBlock { Text = mfc is null ? $"{i + 1}" : $"{i + 1} {mfc[i].Name} ({mfc[i].Unit})", VerticalAlignment = VerticalAlignment.Center, FontSize = 13 };
            var n = new NumericUpDown { Minimum = 0, Maximum = (decimal)(mfc?[i].FullScale ?? 2000), Increment = 1, FormatString = "0.#", Margin = new(0, 1), ShowButtonSpinner = false };
            n.ValueChanged += (_, _) => Dirty();
            Grid.SetRow(t, i); Grid.SetRow(n, i); Grid.SetColumn(n, 1);
            g.Children.Add(t); g.Children.Add(n);
            _rMfc[i] = n;
        }
        // 進階
        var adv = C<Grid>("GrdRAdv");
        for (int i = 0; i < AdvFields.Length; i++)
        {
            var f = AdvFields[i];
            adv.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var t = new TextBlock { Text = f.Label, VerticalAlignment = VerticalAlignment.Center };
            var n = new NumericUpDown { Minimum = (decimal)f.Min, Maximum = (decimal)f.Max, Increment = (decimal)f.Inc, FormatString = f.Fmt, Margin = new(0, 1), ShowButtonSpinner = false };
            n.ValueChanged += (_, _) => Dirty();
            Grid.SetRow(t, i); Grid.SetRow(n, i); Grid.SetColumn(n, 1);
            adv.Children.Add(t); adv.Children.Add(n);
            _rAdv[f.Key] = n;
        }

        foreach (var tb in new[] { "TxtRName", "TxtRDesc", "TxtRBase", "TxtRCross" }) C<TextBox>(tb).TextChanged += (_, _) => Dirty();
        foreach (var cb in new[] { "ChkRPump", "ChkRVent" }) C<CheckBox>(cb).IsCheckedChanged += (_, _) => Dirty();
        foreach (var nu in new[] { "NumRTol", "NumRSettle", "NumRGas" }) C<NumericUpDown>(nu).ValueChanged += (_, _) => Dirty();

        C<ComboBox>("CmbRHeatAdd").SelectionChanged += (_, _) =>
        {
            if (_filling || C<ComboBox>("CmbRHeatAdd").SelectedItem is not ComboBoxItem { Tag: int ch }) return;
            _heatSp[ch] = _m?.HeaterSetpoint(ch) is { } sp && sp > 0 ? Math.Round(sp) : 100;
            RebuildHeat();
            Dirty();
        };

        // 段
        C<TextBox>("TxtRSegName").TextChanged += (_, _) => SegEdit(s => s.Name = (C<TextBox>("TxtRSegName").Text ?? "").Trim());
        C<NumericUpDown>("NumRCycles").ValueChanged += (_, _) => SegEdit(s => s.Cycles = (int)(C<NumericUpDown>("NumRCycles").Value ?? 1));
        C<NumericUpDown>("NumRO3").ValueChanged += (_, _) => SegEdit(s => s.O3SetPct = (double)(C<NumericUpDown>("NumRO3").Value ?? 0));
        C<Button>("BtnRSegUp").Click += (_, _) => MoveSeg(-1);
        C<Button>("BtnRSegDown").Click += (_, _) => MoveSeg(+1);
        C<Button>("BtnRSegCopy").Click += (_, _) =>
        {
            if (_segSel >= _segs.Count) return;
            var c = Clone(_segs[_segSel]);
            c.Name += " 複製";
            _segs.Insert(_segSel + 1, c);
            _segSel++;
            RebuildSegs(); Dirty();
        };
        C<Button>("BtnRSegDel").Click += (_, _) =>
        {
            if (_segSel >= _segs.Count) return;
            _segs.RemoveAt(_segSel);
            _segSel = Math.Max(0, Math.Min(_segSel, _segs.Count - 1));
            RebuildSegs(); Dirty();
        };
        C<Button>("BtnRStepAdd").Click += (_, _) =>
        {
            if (_segSel >= _segs.Count) return;
            var steps = _segs[_segSel].Steps;
            if (steps.Count >= AldRecipe.MaxSteps) { Msg($"每段最多 {AldRecipe.MaxSteps} 步"); return; }
            steps.Add(new AldStep("Purge", 0, 1000));
            RebuildSteps(); Dirty();
        };

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
                RebuildSteps();
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
            _recipe = new ProcessRecipe
            {
                Name = "新配方",
                Segments = [new AldRecipe { Name = "段 1", Cycles = 100, Steps = { new("Precursor A", 1, 20), new("Purge", 2, 5000), new("Precursor B", 4, 20), new("Purge", 2, 5000) } }],
            };
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
        var cmb = C<ComboBox>("CmbRecipeFile");
        cmb.SelectedItem = path is null ? null : Path.GetFileName(path);
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

    private void FillForm(ProcessRecipe r)
    {
        _filling = true;
        try
        {
            C<TextBox>("TxtRName").Text = r.Name;
            C<TextBox>("TxtRDesc").Text = r.Description;
            C<CheckBox>("ChkRPump").IsChecked = r.PumpDown;
            C<CheckBox>("ChkRVent").IsChecked = r.VentAfter;
            C<TextBox>("TxtRBase").Text = r.BaseTorr.ToString("G3", CultureInfo.InvariantCulture);
            C<TextBox>("TxtRCross").Text = r.CrossoverTorr.ToString("G3", CultureInfo.InvariantCulture);
            for (int i = 0; i < 16; i++) _heatSp[i] = i < r.HeaterSpDegC.Length ? r.HeaterSpDegC[i] : 0;
            for (int i = 0; i < 8; i++) _rMfc[i].Value = (decimal)(i < r.Mfc.Length ? r.Mfc[i] : 0);
            C<NumericUpDown>("NumRTol").Value = (decimal)r.HeatToleranceC;
            C<NumericUpDown>("NumRSettle").Value = (decimal)r.HeatSettleS;
            C<NumericUpDown>("NumRGas").Value = (decimal)r.GasStabilizeS;
            _rAdv["pump"].Value = (decimal)r.PumpTimeoutS;
            _rAdv["turboUp"].Value = (decimal)r.TurboSpinUpTimeoutS;
            _rAdv["heat"].Value = (decimal)r.HeatTimeoutS;
            _rAdv["ventTarget"].Value = (decimal)r.VentTargetTorr;
            _rAdv["vent"].Value = (decimal)r.VentTimeoutS;
            _rAdv["turboDown"].Value = (decimal)r.TurboSpinDownTimeoutS;
            _segs = r.AldSegments.Select(Clone).ToList();
            _segSel = 0;
            RebuildHeat();
            RebuildSegs();
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
        return RecipeFile.ToJson(r with { Segments = r.Segments, Ald = null }) != RecipeFile.ToJson(_recipe with { Segments = _recipe.AldSegments.ToList(), Ald = null });
    }

    private bool TryReadForm(out ProcessRecipe r, bool quiet = false)
    {
        r = _recipe;
        try
        {
            string name = (C<TextBox>("TxtRName").Text ?? "").Trim();
            if (name.Length == 0) throw new FormatException("請填配方名稱");
            double Torr(string box, string what)
                => double.TryParse(C<TextBox>(box).Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v > 0
                    ? v : throw new FormatException($"{what} 要填正數（例如 5e-3）");
            double N(NumericUpDown n) => (double)(n.Value ?? 0);
            if (!quiet)
                for (int i = 0; i < _segs.Count; i++)
                    if (_segs[i].Name.Trim().Length == 0) _segs[i].Name = $"段 {i + 1}";
            r = _recipe with
            {
                Name = name,
                Description = (C<TextBox>("TxtRDesc").Text ?? "").Trim(),
                PumpDown = C<CheckBox>("ChkRPump").IsChecked == true,
                VentAfter = C<CheckBox>("ChkRVent").IsChecked == true,
                BaseTorr = Torr("TxtRBase", "Base pressure"),
                CrossoverTorr = Torr("TxtRCross", "Crossover"),
                HeaterSpDegC = (double[])_heatSp.Clone(),
                HeatToleranceC = N(C<NumericUpDown>("NumRTol")),
                HeatSettleS = N(C<NumericUpDown>("NumRSettle")),
                Mfc = _rMfc.Select(N).ToArray(),
                GasStabilizeS = N(C<NumericUpDown>("NumRGas")),
                PumpTimeoutS = N(_rAdv["pump"]),
                TurboSpinUpTimeoutS = N(_rAdv["turboUp"]),
                HeatTimeoutS = N(_rAdv["heat"]),
                VentTargetTorr = N(_rAdv["ventTarget"]),
                VentTimeoutS = N(_rAdv["vent"]),
                TurboSpinDownTimeoutS = N(_rAdv["turboDown"]),
                Segments = _segs.Select(Clone).ToList(),
                Ald = null,
            };
            return true;
        }
        catch (FormatException e) { if (!quiet) Msg(e.Message); return false; }
    }

    /// <summary>配方本身的檢查 + 這台機器的 config（加熱上限、MFC 滿刻度、RF 上限、RF / O3 是否已接上）。有錯就不能執行。</summary>
    private List<string> Check(ProcessRecipe r)
    {
        var e = r.Validate().ToList();
        if (_m is null) return e;
        var cfg = _m.Config;
        for (int i = 0; i < 16; i++)
            if (r.HeaterSpDegC[i] > cfg.Heaters.Channels[i].LimitDegC)
                e.Add($"加熱 #{i + 1} {cfg.Heaters.Channels[i].Name} {r.HeaterSpDegC[i]:0} °C 超過上限 {cfg.Heaters.Channels[i].LimitDegC:0} °C");
        // 配方不改加熱：要的溫度跟加熱板常駐設定對不上，跑了也只會等到逾時
        if (_m.Snapshot.Connected)
            for (int i = 0; i < 16; i++)
                if (r.HeaterSpDegC[i] > 0 && !(Math.Abs((_m.HeaterSetpoint(i) ?? double.NaN) - r.HeaterSpDegC[i]) <= r.HeatToleranceC))
                    e.Add($"加熱 #{i + 1} {cfg.Heaters.Channels[i].Name} 配方需要 {r.HeaterSpDegC[i]:0} °C，" +
                          $"加熱板設定是 {(_m.HeaterSetpoint(i) is { } sp ? $"{sp:0} °C" : "關")}（請先到手動操作頁設定）");
        for (int i = 0; i < 8; i++)
            if (r.Mfc[i] > cfg.Mfc.Channels[i].FullScale)
                e.Add($"起始 MFC {i + 1} {r.Mfc[i]} 超過滿刻度 {cfg.Mfc.Channels[i].FullScale} {cfg.Mfc.Channels[i].Unit}");
        var segs = r.AldSegments;
        for (int g = 0; g < segs.Count; g++)
        {
            string at = $"第 {g + 1} 段";
            for (int k = 0; k < segs[g].Steps.Count; k++)
            {
                var s = segs[g].Steps[k];
                if (s.Mfc is { Length: 8 } m)
                    for (int c = 0; c < 8; c++)
                        if (m[c] > cfg.Mfc.Channels[c].FullScale)
                            e.Add($"{at} 第 {k + 1} 步 MFC {c + 1} {m[c]} 超過滿刻度 {cfg.Mfc.Channels[c].FullScale} {cfg.Mfc.Channels[c].Unit}");
                if (s.Rf1W > cfg.Process.Rf1MaxW) e.Add($"{at} 第 {k + 1} 步 RF1 {s.Rf1W:0} W 超過上限 {cfg.Process.Rf1MaxW:0} W");
                if (s.Rf2W > cfg.Process.Rf2MaxW) e.Add($"{at} 第 {k + 1} 步 RF2 {s.Rf2W:0} W 超過上限 {cfg.Process.Rf2MaxW:0} W");
            }
            if (segs[g].UsesRf && !cfg.Process.RfReady) e.Add($"{at} 有 RF 步驟：RF 控制還沒接上（硬體確認後才能執行，可以先存檔）");
            if (segs[g].O3SetPct > 0 && !cfg.Process.O3Ready) e.Add($"{at} 有用 O3：O3 產生器控制還沒接上（硬體確認後才能執行，可以先存檔）");
        }
        return e;
    }

    /// <summary>不擋執行的提醒。</summary>
    private static List<string> Warnings(ProcessRecipe r)
    {
        var w = new List<string>();
        var segs = r.AldSegments;
        for (int g = 0; g < segs.Count; g++)
            for (int k = 0; k < segs[g].Steps.Count; k++)
            {
                var s = segs[g].Steps[k];
                if (s.HasMfcChange && s.DurationMs < 1000)
                    w.Add($"第 {g + 1} 段 第 {k + 1} 步只有 {s.DurationMs} ms 卻切換 MFC：MFC 反應約 0.5–1 s，流量可能還沒到位就換下一步");
            }
        if (segs.Count == 0 && !r.VentAfter && r.PumpDown) w.Add("沒有 ALD 段：只會抽真空 / 確認溫度 / 通氣");
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
        UpdateTimes();
    }
    private bool _checkDirty;

    // ------------------------------------------------------------ 溫度（只列用到的）
    private void RebuildHeat()
    {
        var p = C<StackPanel>("PnlRHeat");
        p.Children.Clear();
        var names = HeaterNames;
        for (int ch = 0; ch < 16; ch++)
        {
            if (!(_heatSp[ch] > 0)) continue;
            int c = ch;
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,80,Auto") };
            var t = new TextBlock { Text = $"#{c + 1} {names[c]}", VerticalAlignment = VerticalAlignment.Center, FontSize = 13 };
            var n = new NumericUpDown { Minimum = 1, Maximum = (decimal)(_m?.Config.Heaters.Channels[c].LimitDegC ?? 650), Increment = 5, FormatString = "0", Value = (decimal)_heatSp[c], ShowButtonSpinner = false };
            n.ValueChanged += (_, _) => { _heatSp[c] = (double)(n.Value ?? 0); Dirty(); };
            var x = new Button { Content = "✕", Padding = new(6, 2), Margin = new(4, 0, 0, 0) };
            ToolTip.SetTip(x, "不檢查這組");
            x.Click += (_, _) => { _heatSp[c] = 0; RebuildHeat(); Dirty(); };
            Grid.SetColumn(n, 1); Grid.SetColumn(x, 2);
            row.Children.Add(t); row.Children.Add(n); row.Children.Add(x);
            p.Children.Add(row);
        }
        if (p.Children.Count == 0) p.Children.Add(new TextBlock { Text = "（不檢查溫度）", Foreground = Brushes.Gray, FontSize = 13 });
        bool was = _filling;
        _filling = true;
        var cmb = C<ComboBox>("CmbRHeatAdd");
        cmb.ItemsSource = Enumerable.Range(0, 16).Where(ch => !(_heatSp[ch] > 0))
            .Select(ch => new ComboBoxItem { Content = $"#{ch + 1} {names[ch]}", Tag = ch }).ToList();
        cmb.SelectedIndex = -1;
        _filling = was;
    }

    // ------------------------------------------------------------ 段
    private void SegEdit(Action<AldRecipe> edit)
    {
        if (_filling || _segSel >= _segs.Count) return;
        edit(_segs[_segSel]);
        RefreshSegLabels();
        Dirty();
    }

    private void MoveSeg(int d)
    {
        int j = _segSel + d;
        if (_segSel >= _segs.Count || j < 0 || j >= _segs.Count) return;
        (_segs[_segSel], _segs[j]) = (_segs[j], _segs[_segSel]);
        _segSel = j;
        RebuildSegs(); Dirty();
    }

    private static string SegLabel(int i, AldRecipe s) => $"{i + 1}. {(s.Name.Length > 0 ? s.Name : $"段 {i + 1}")} ×{s.Cycles}";

    private void RebuildSegs()
    {
        var p = C<WrapPanel>("PnlRSegs");
        p.Children.Clear();
        for (int i = 0; i < _segs.Count; i++)
        {
            int k = i;
            var b = new Button { Content = SegLabel(i, _segs[i]), Margin = new(0, 0, 6, 4), Padding = new(12, 6), Tag = "seg" };
            b.Click += (_, _) => { _segSel = k; RebuildSegs(); };
            p.Children.Add(b);
        }
        var add = new Button { Content = "＋ 新增段", Margin = new(0, 0, 6, 4), Padding = new(12, 6), IsEnabled = _rEdit };
        add.Click += (_, _) =>
        {
            _segs.Add(new AldRecipe { Name = $"段 {_segs.Count + 1}", Cycles = 100, Steps = { new("Precursor", 1, 20), new("Purge", 2, 5000) } });
            _segSel = _segs.Count - 1;
            RebuildSegs(); Dirty();
        };
        p.Children.Add(add);
        RefreshSegLabels();

        bool was = _filling;
        _filling = true;
        bool has = _segSel < _segs.Count;
        C<StackPanel>("PnlRSegHead").IsVisible = has;
        if (has)
        {
            var s = _segs[_segSel];
            C<TextBox>("TxtRSegName").Text = s.Name;
            C<NumericUpDown>("NumRCycles").Value = s.Cycles;
            C<NumericUpDown>("NumRO3").Value = (decimal)s.O3SetPct;
        }
        _filling = was;
        RebuildSteps();
    }

    private void RefreshSegLabels()
    {
        int i = 0;
        foreach (var b in C<WrapPanel>("PnlRSegs").Children.OfType<Button>().Where(b => (string?)b.Tag == "seg"))
        {
            b.Content = SegLabel(i, _segs[i]);
            b.Background = i == _segSel ? SegOn : SegOff;
            b.Foreground = i == _segSel ? Brushes.White : Brushes.Black;
            b.FontWeight = i == _segSel ? FontWeight.Bold : FontWeight.Normal;
            i++;
        }
        UpdateTimes();
    }

    private void UpdateTimes()
    {
        static string T(double s) => TimeSpan.FromSeconds(Math.Round(s)).ToString(@"hh\:mm\:ss");
        double total = _segs.Sum(s => s.TotalSeconds);
        C<TextBlock>("TxtRTotal").Text = _segs.Count == 0
            ? "沒有 ALD 段（只做抽真空 / 確認溫度 / 回填）。按「＋ 新增段」加入。"
            : $"共 {_segs.Count} 段，ALD 約 {T(total)}（不含抽真空、等溫度、回填）";
        var t = C<TextBlock>("TxtRSegTime");
        if (_segSel >= _segs.Count) { t.Text = ""; return; }
        var g = _segs[_segSel];
        var msg = $"每 cycle {g.CycleSeconds:0.###} s，這段約 {T(g.TotalSeconds)}";
        int presets = g.MfcPresets().Count;
        if (presets > 0) msg += $"；MFC 切換 {presets}/{AldRecipe.MaxMfcPresets} 種";
        var fast = g.Steps.Select((s, i) => (s, i)).Where(x => x.s.HasMfcChange && x.s.DurationMs < 1000).Select(x => x.i + 1).ToList();
        if (fast.Count > 0) msg += $"\n⚠ 第 {string.Join("、", fast)} 步不到 1 s 就切換 MFC，MFC 反應約 0.5–1 s，可能跟不上";
        if (presets > AldRecipe.MaxMfcPresets) msg += $"\n⚠ MFC 切換組合超過 {AldRecipe.MaxMfcPresets} 種（相同設定算同一種）";
        if (g.UsesRf && !Hw.RfReady) msg += "\n⚠ RF 控制還沒接上：可以存檔，但目前不能執行";
        if (g.O3SetPct > 0 && !Hw.O3Ready) msg += "\n⚠ O3 產生器控制還沒接上：可以存檔，但目前不能執行";
        t.Text = msg;
        t.Foreground = msg.Contains('⚠') ? WarnText : new SolidColorBrush(Color.Parse("#5A6B7D"));
    }

    // ------------------------------------------------------------ 步驟表
    private void RebuildSteps()
    {
        var g = C<Grid>("GrdRSteps");
        g.Children.Clear();
        g.RowDefinitions.Clear();
        g.ColumnDefinitions.Clear();
        bool rf1 = Hw.Rf1MaxW > 0, rf2 = Hw.Rf2MaxW > 0;
        // # | 名稱 | V1..V7 | 時間 | MFC | RF1 | RF2 | 操作
        g.ColumnDefinitions.Add(new ColumnDefinition(28, GridUnitType.Pixel));
        g.ColumnDefinitions.Add(new ColumnDefinition(124, GridUnitType.Pixel));
        for (int v = 0; v < 7; v++) g.ColumnDefinitions.Add(new ColumnDefinition(44, GridUnitType.Pixel));
        g.ColumnDefinitions.Add(new ColumnDefinition(80, GridUnitType.Pixel));
        g.ColumnDefinitions.Add(new ColumnDefinition(120, GridUnitType.Pixel));
        g.ColumnDefinitions.Add(new ColumnDefinition(rf1 ? 70 : 0, GridUnitType.Pixel));
        g.ColumnDefinitions.Add(new ColumnDefinition(rf2 ? 70 : 0, GridUnitType.Pixel));
        g.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        if (_segSel >= _segs.Count) return;
        var seg = _segs[_segSel];

        void Add(Control c, int row, int col)
        {
            Grid.SetRow(c, row); Grid.SetColumn(c, col);
            g.Children.Add(c);
        }
        static TextBlock Head(string t) => new()
        {
            Text = t, FontWeight = FontWeight.Bold, FontSize = 12, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center, Margin = new(1, 2),
        };

        g.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        Add(Head("#"), 0, 0);
        Add(Head("步驟名稱"), 0, 1);
        for (int v = 0; v < 7; v++)
            Add(Head(_valveNames[v].Trim().Length > 0 ? $"V{v + 1}\n{_valveNames[v].Trim()}" : $"V{v + 1}"), 0, 2 + v);
        Add(Head("時間 ms"), 0, 9);
        Add(Head("MFC 切換"), 0, 10);
        if (rf1) Add(Head(Hw.RfReady ? "RF1 W" : "RF1 W\n（未接）"), 0, 11);
        if (rf2) Add(Head(Hw.RfReady ? "RF2 W" : "RF2 W\n（未接）"), 0, 12);

        for (int i = 0; i < seg.Steps.Count; i++)
        {
            int k = i, row = i + 1;
            var st = seg.Steps[k];
            g.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            if (i % 2 == 1) Add(new Border { Background = RowAlt, [Grid.ColumnSpanProperty] = 14 }, row, 0);
            void Set(Func<AldStep, AldStep> f) { if (_filling) return; seg.Steps[k] = f(seg.Steps[k]); Dirty(); }

            Add(new TextBlock { Text = $"{k + 1}", VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center }, row, 0);
            var name = new TextBox { Text = st.Label, Margin = new(2) };
            name.TextChanged += (_, _) => Set(s => s with { Label = (name.Text ?? "").Trim() });
            Add(name, row, 1);
            for (int v = 0; v < 7; v++)
            {
                int bit = v;
                var cb = new CheckBox { IsChecked = (st.ValveMask >> bit & 1) != 0, HorizontalAlignment = HorizontalAlignment.Center, MinWidth = 0 };
                ToolTip.SetTip(cb, ValveNames.Label(_valveNames, bit));
                cb.IsCheckedChanged += (_, _) => Set(s => s with { ValveMask = (ushort)(cb.IsChecked == true ? s.ValveMask | 1 << bit : s.ValveMask & ~(1 << bit)) });
                Add(cb, row, 2 + v);
            }
            var ms = new NumericUpDown { Minimum = 1, Maximum = 60000, Increment = 10, FormatString = "0", Value = st.DurationMs, Margin = new(2), ShowButtonSpinner = false };
            ms.ValueChanged += (_, _) => Set(s => s with { DurationMs = (int)(ms.Value ?? 1) });
            Add(ms, row, 9);
            Add(MfcButton(seg, k), row, 10);
            if (rf1) Add(RfBox(st.Rf1W, Hw.Rf1MaxW, v => Set(s => s with { Rf1W = v })), row, 11);
            if (rf2) Add(RfBox(st.Rf2W, Hw.Rf2MaxW, v => Set(s => s with { Rf2W = v })), row, 12);

            var ops = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Margin = new(4, 0) };
            Button Op(string t, string tip, Action a)
            {
                var b = new Button { Content = t, Padding = new(5, 2), MinWidth = 0 };
                ToolTip.SetTip(b, tip);
                b.Click += (_, _) => { a(); RebuildSteps(); Dirty(); };
                ops.Children.Add(b);
                return b;
            }
            Op("↑", "往上移", () => { if (k > 0) (seg.Steps[k - 1], seg.Steps[k]) = (seg.Steps[k], seg.Steps[k - 1]); }).IsEnabled = k > 0;
            Op("↓", "往下移", () => { if (k < seg.Steps.Count - 1) (seg.Steps[k + 1], seg.Steps[k]) = (seg.Steps[k], seg.Steps[k + 1]); }).IsEnabled = k < seg.Steps.Count - 1;
            Op("⧉", "複製這一步", () => { if (seg.Steps.Count < AldRecipe.MaxSteps) seg.Steps.Insert(k + 1, seg.Steps[k] with { Mfc = seg.Steps[k].Mfc?.ToArray() }); })
                .IsEnabled = seg.Steps.Count < AldRecipe.MaxSteps;
            Op("✕", "刪除這一步", () => seg.Steps.RemoveAt(k));
            Add(ops, row, 13);
        }
        if (seg.Steps.Count == 0)
        {
            g.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var t = new TextBlock { Text = "這段還沒有步驟，按下方「＋ 新增步驟」", Foreground = Brushes.Gray, Margin = new(8) };
            Grid.SetColumnSpan(t, 12);
            Add(t, 1, 1);
        }
        UpdateTimes();
    }

    private static NumericUpDown RfBox(double w, double max, Action<double> set)
    {
        var n = new NumericUpDown { Minimum = 0, Maximum = (decimal)max, Increment = 10, FormatString = "0", Value = (decimal)w, Margin = new(2), ShowButtonSpinner = false };
        n.ValueChanged += (_, _) => set((double)(n.Value ?? 0));
        return n;
    }

    /// <summary>MFC 切換按鈕：顯示摘要，點開可設定每個通道「沿用 / 切換到某值」。</summary>
    private Button MfcButton(AldRecipe seg, int k)
    {
        var mfc = _m?.Config.Mfc.Channels;
        string Summary() => seg.Steps[k].HasMfcChange ? RecipeText.Mfc(seg.Steps[k].Mfc!) : "沿用";
        var b = new Button { Content = Summary(), HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new(2), FontSize = 12 };
        ToolTip.SetTip(b, seg.Steps[k].HasMfcChange ? Summary() : "這一步開始時要切換的 MFC（沒勾 = 沿用上一步）");
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,90") };
        var checks = new CheckBox[8];
        var nums = new NumericUpDown[8];
        for (int c = 0; c < 8; c++)
        {
            int ch = c;
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            checks[c] = new CheckBox { Content = mfc is null ? $"MFC {c + 1}" : $"{c + 1} {mfc[c].Name}（{mfc[c].Use}）", Margin = new(0, 0, 8, 0) };
            nums[c] = new NumericUpDown { Minimum = 0, Maximum = (decimal)(mfc?[c].FullScale ?? 2000), Increment = 1, FormatString = "0.#", Margin = new(0, 1), ShowButtonSpinner = false };
            Grid.SetRow(checks[c], c); Grid.SetRow(nums[c], c); Grid.SetColumn(nums[c], 1);
            grid.Children.Add(checks[c]); grid.Children.Add(nums[c]);
            void Changed()
            {
                if (_filling) return;
                nums[ch].IsEnabled = checks[ch].IsChecked == true;
                var m = Enumerable.Range(0, 8).Select(i => checks[i].IsChecked == true ? (double?)(double)(nums[i].Value ?? 0) : null).ToArray();
                seg.Steps[k] = seg.Steps[k] with { Mfc = m.Any(x => x is not null) ? m : null };
                b.Content = Summary();
                ToolTip.SetTip(b, Summary());
                Dirty();
            }
            checks[c].IsCheckedChanged += (_, _) => Changed();
            nums[c].ValueChanged += (_, _) => Changed();
        }
        var panel = new StackPanel { Spacing = 6 };
        panel.Children.Add(new TextBlock { Text = $"第 {k + 1} 步開始時切換 MFC", FontWeight = FontWeight.Bold });
        panel.Children.Add(new TextBlock { Text = "沒勾的通道沿用上一步；切換後一直維持（下一段也維持），直到再被切換", Foreground = Brushes.Gray, FontSize = 12, TextWrapping = TextWrapping.Wrap, MaxWidth = 360 });
        panel.Children.Add(grid);
        var fly = new Flyout { Content = panel, Placement = PlacementMode.Bottom };
        fly.Opened += (_, _) =>
        {
            _filling = true;
            var m = seg.Steps[k].Mfc;
            for (int c = 0; c < 8; c++)
            {
                checks[c].IsChecked = m is { } a && c < a.Length && a[c] is not null;
                nums[c].Value = (decimal)(m is { } a2 && c < a2.Length && a2[c] is { } v ? v : 0);
                nums[c].IsEnabled = checks[c].IsChecked == true && _rEdit;
                checks[c].IsEnabled = _rEdit;
            }
            _filling = false;
        };
        b.Flyout = fly;
        return b;
    }

    // ------------------------------------------------------------ 每 200 ms
    private void RenderRecipe()
    {
        bool edit = Can(Permission.EditRecipe);   // ONLINE 時 Can() 一律 false
        if (edit != _rEdit)
        {
            _rEdit = edit;
            C<StackPanel>("PnlRecipeLeft").IsEnabled = edit;
            C<StackPanel>("PnlRSegHead").IsEnabled = edit;
            C<Grid>("GrdRSteps").IsEnabled = edit;
            C<Button>("BtnRStepAdd").IsEnabled = edit;
            C<Button>("BtnRValveNames").IsEnabled = edit;
            C<Button>("BtnRecipeSave").IsEnabled = edit;
            C<Button>("BtnRecipeSaveAs").IsEnabled = edit;
            C<Button>("BtnRecipeNew").IsEnabled = edit;
            RebuildSegs();   // 「＋ 新增段」跟著鎖
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
