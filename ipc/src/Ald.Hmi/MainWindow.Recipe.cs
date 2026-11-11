using System.Globalization;
using Ald.Core;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Ald.Hmi;

/// <summary>
/// Recipe 頁：從配方資料夾選檔 → 表單編輯常用欄位 → 存檔 / 檢查 / 執行（ProcessSequencer）。
/// 表單沒有的欄位（各步逾時等）沿用讀進來的檔案內容，存檔不會被洗掉。
/// </summary>
public partial class MainWindow
{
    private readonly List<string> _runLog = new();
    private int _runLogShown = -1;
    private string _runName = "";
    private ProcessRecipe _recipe = new() { Name = "新配方" };
    private string? _recipePath;
    private readonly NumericUpDown[] _rHeat = new NumericUpDown[16];
    private readonly NumericUpDown[] _rMfc = new NumericUpDown[8];
    private bool _loadingRecipe;

    private void WireRecipe()
    {
        var names = _m?.Config.Heaters.Channels.Select(h => h.Name).ToArray() ?? Enumerable.Range(1, 16).Select(i => $"#{i}").ToArray();
        BuildPairs(C<Grid>("GrdRHeat"), _rHeat, i => $"{i + 1} {names[i]}", i => _m?.Config.Heaters.Channels[i].LimitDegC ?? 650, 5);
        var mfc = _m?.Config.Mfc.Channels;
        BuildPairs(C<Grid>("GrdRMfc"), _rMfc, i => mfc is null ? $"{i + 1}" : $"{i + 1} {mfc[i].Name} ({mfc[i].Unit})", i => mfc?[i].FullScale ?? 2000, 1);

        var cmb = C<ComboBox>("CmbRecipeFile");
        cmb.SelectionChanged += (_, _) =>
        {
            if (_loadingRecipe || cmb.SelectedItem is not string file) return;
            var path = Path.Combine(_recipeDir, file);
            try { _recipe = RecipeFile.Load(path); _recipePath = path; FillForm(_recipe); Msg(""); }
            catch (Exception e) { Msg(e.Message); }
        };
        C<Button>("BtnRecipeReload").Click += (_, _) => ReloadRecipeList(null);
        C<Button>("BtnRecipeNew").Click += (_, _) =>
        {
            _recipe = new ProcessRecipe { Name = "新配方" };
            _recipePath = null;
            _loadingRecipe = true; cmb.SelectedIndex = -1; _loadingRecipe = false;
            FillForm(_recipe);
            Msg("新配方：填好後按存檔（檔名用配方名稱）");
        };
        C<Button>("BtnRecipeSave").Click += (_, _) => SaveRecipe();
        C<Button>("BtnRecipeCheck").Click += (_, _) =>
        {
            if (TryReadForm(out var r)) { var e = Check(r); Msg(e.Count == 0 ? "檢查通過" : string.Join("\n", e)); }
        };
        C<Button>("BtnRecipeRun").Click += (_, _) =>
        {
            if (_m is null || !TryReadForm(out var r)) return;
            var e = Check(r);
            if (e.Count > 0) { Msg("不能執行：\n" + string.Join("\n", e)); return; }
            Msg("");
            RunRecipe(r);
        };
        C<Button>("BtnRecipeAbort").Click += (_, _) => _seq?.Abort();

        FillForm(_recipe);
        ReloadRecipeList(null);
    }

    /// <summary>名稱 + 數字框兩兩一排。</summary>
    private static void BuildPairs(Grid g, NumericUpDown[] boxes, Func<int, string> label, Func<int, double> max, double inc)
    {
        int rows = (boxes.Length + 1) / 2;
        for (int r = 0; r < rows; r++) g.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        for (int i = 0; i < boxes.Length; i++)
        {
            int row = i % rows, col = i / rows * 2;
            var t = new TextBlock { Text = label(i), VerticalAlignment = VerticalAlignment.Center, Margin = new(col == 0 ? 0 : 16, 0, 8, 0) };
            var n = new NumericUpDown { Minimum = 0, Maximum = (decimal)Math.Max(0, max(i)), Increment = (decimal)inc, FormatString = "0.#", Margin = new(0, 1) };
            Grid.SetRow(t, row); Grid.SetColumn(t, col);
            Grid.SetRow(n, row); Grid.SetColumn(n, col + 1);
            g.Children.Add(t); g.Children.Add(n);
            boxes[i] = n;
        }
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

    private void FillForm(ProcessRecipe r)
    {
        C<TextBox>("TxtRName").Text = r.Name;
        C<CheckBox>("ChkRPump").IsChecked = r.PumpDown;
        C<CheckBox>("ChkRVent").IsChecked = r.VentAfter;
        C<TextBox>("TxtRBase").Text = r.BaseTorr.ToString("G3", CultureInfo.InvariantCulture);
        C<TextBox>("TxtRCross").Text = r.CrossoverTorr.ToString("G3", CultureInfo.InvariantCulture);
        for (int i = 0; i < 16; i++) _rHeat[i].Value = (decimal)(i < r.HeaterSpDegC.Length ? r.HeaterSpDegC[i] : 0);
        for (int i = 0; i < 8; i++) _rMfc[i].Value = (decimal)(i < r.Mfc.Length ? r.Mfc[i] : 0);
        C<NumericUpDown>("NumRTol").Value = (decimal)r.HeatToleranceC;
        C<NumericUpDown>("NumRSettle").Value = (decimal)r.HeatSettleS;
        C<NumericUpDown>("NumRGas").Value = (decimal)r.GasStabilizeS;
        C<NumericUpDown>("NumRCycles").Value = r.Ald?.Cycles ?? 1;
        C<TextBox>("TxtRSteps").Text = r.Ald is null ? "" : AldStepText.Format(r.Ald.Steps);
    }

    private bool TryReadForm(out ProcessRecipe r)
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
            var steps = AldStepText.Parse(C<TextBox>("TxtRSteps").Text ?? "");
            r = _recipe with
            {
                Name = name,
                PumpDown = C<CheckBox>("ChkRPump").IsChecked == true,
                VentAfter = C<CheckBox>("ChkRVent").IsChecked == true,
                BaseTorr = Torr("TxtRBase", "Base pressure"),
                CrossoverTorr = Torr("TxtRCross", "Crossover"),
                HeaterSpDegC = _rHeat.Select(N).ToArray(),
                HeatToleranceC = N(C<NumericUpDown>("NumRTol")),
                HeatSettleS = N(C<NumericUpDown>("NumRSettle")),
                Mfc = _rMfc.Select(N).ToArray(),
                GasStabilizeS = N(C<NumericUpDown>("NumRGas")),
                Ald = steps.Count == 0 ? null
                    : new AldRecipe { Name = name, Cycles = (int)N(C<NumericUpDown>("NumRCycles")), Steps = steps },
            };
            return true;
        }
        catch (FormatException e) { Msg(e.Message); return false; }
    }

    /// <summary>配方本身的檢查 + 這台機器的 config（加熱上限、MFC 滿刻度）。</summary>
    private List<string> Check(ProcessRecipe r)
    {
        var e = r.Validate().ToList();
        if (_m is null) return e;
        var cfg = _m.Config;
        for (int i = 0; i < 16; i++)
            if (r.HeaterSpDegC[i] > cfg.Heaters.Channels[i].LimitDegC)
                e.Add($"加熱 #{i + 1} {cfg.Heaters.Channels[i].Name} {r.HeaterSpDegC[i]:0} °C 超過上限 {cfg.Heaters.Channels[i].LimitDegC:0} °C");
        for (int i = 0; i < 8; i++)
            if (r.Mfc[i] > cfg.Mfc.Channels[i].FullScale)
                e.Add($"MFC {i + 1} {r.Mfc[i]} 超過滿刻度 {cfg.Mfc.Channels[i].FullScale} {cfg.Mfc.Channels[i].Unit}");
        return e;
    }

    private void SaveRecipe()
    {
        if (!TryReadForm(out var r)) return;
        var path = _recipePath ?? Path.Combine(_recipeDir, RecipeFile.FileNameFor(r.Name));
        if (_recipePath is null && File.Exists(path)) { Msg($"已經有 {Path.GetFileName(path)}，請換一個配方名稱"); return; }
        try
        {
            RecipeFile.Save(path, r);
            _recipe = r;
            _recipePath = path;
            ReloadRecipeList(Path.GetFileName(path));
            Msg($"已存檔：{Path.GetFileName(path)}");
            _alarmLog?.Add("操作", "Info", $"Recipe 存檔 {Path.GetFileName(path)}", evt: "OPERATOR");
        }
        catch (Exception e) { Msg($"存檔失敗：{e.Message}"); }
    }

    private void Msg(string text) => C<TextBlock>("TxtRecipeMsg").Text = text;

    private void RenderRecipe()
    {
        bool running = SequenceRunning;
        C<Button>("BtnRecipeRun").IsEnabled = _m is not null && !running;
        C<Button>("BtnRecipeAbort").IsEnabled = running;
        C<TextBlock>("TxtRunState").Text = _seq is null ? "待機" : _seq.State switch
        {
            SequencerState.Running => $"執行中：{_runName}",
            SequencerState.Done => $"完成：{_runName}",
            SequencerState.Aborted => $"已中止：{_runName}",
            SequencerState.Failed => $"失敗：{_runName}",
            _ => "待機",
        };
        C<TextBlock>("TxtRunStep").Text = _seq?.Step ?? "";

        try
        {
            var steps = AldStepText.Parse(C<TextBox>("TxtRSteps").Text ?? "");
            double cycleS = steps.Sum(s => s.DurationMs) / 1000.0;
            double total = cycleS * (double)(C<NumericUpDown>("NumRCycles").Value ?? 0);
            C<TextBlock>("TxtRAldTime").Text = steps.Count == 0 ? "" : $"每 cycle {cycleS:0.###} s，共約 {TimeSpan.FromSeconds(total):hh\\:mm\\:ss}";
        }
        catch (FormatException e) { C<TextBlock>("TxtRAldTime").Text = e.Message; }

        lock (_runLog)
            if (_runLog.Count != _runLogShown)
            {
                C<ListBox>("LstRunLog").ItemsSource = _runLog.ToList();
                _runLogShown = _runLog.Count;
            }
    }
}
