using System.Globalization;
using Ald.Core;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Ald.Hmi;

/// <summary>總覽圖要的資料（每次畫面更新整包換掉）。</summary>
public sealed record OverviewModel(
    MachineSnapshot S,
    bool? WaferLl, bool? WaferPm,
    string LlWaferId, string PmWaferId,
    bool Processing,
    bool Transferring,
    string[] MfcNames,
    string[] MfcUnits,
    double[] MfcSetpoints,
    string[] ValveNames,
    int ValveCount,
    double ChuckTempC, double? ChuckSpC);

/// <summary>
/// 總覽：側視管路圖（2026-11-25 依使用者手繪圖 + 示意 v7）。
/// 中間 Loadlock ─ Slit Valve ─ ALD Chamber；上方 ALD 閥組（AV1~AVn，n = config process.aldValveCount），
/// 由 MFC 匯流管經 ALD VALVE PURGE 閥進氣；RF 在閥組右邊（MFC 匯流 → RF → Chamber）；右上 MFC 8 路（上游閥、名稱、實際、設定、單位）；
/// 下方 LL 粗抽 / LL Vent / Chamber Vent / Chamber 粗抽（Vent 共用 VENT N2，粗抽與 Turbo 前級共用同一台 Dry Pump）；
/// 右側 Turbo Iso Valve、Turbo（ON / OFF、轉速、狀態）、前級閥；右下 PM 數值面板（Chuck 溫度、各壓力計、晶圓 ID、Lift / Heater / 上蓋）。
/// 數值框白底黑字；閥旁「開 / 關」兩格狀態框（動作中橘框、互鎖擋下紅框）；有氣體流動或正在抽氣的管路畫綠色（<see cref="FlowSegments"/>）。
/// 自己畫（DrawingContext），依設計座標 984×676 等比縮放置中。
/// 圖示可以點：滑鼠移上去出現藍框，點下去發 ItemClicked(代號)，由 MainWindow 跳出小視窗操作。代號見 <see cref="BuildHits"/>。
/// </summary>
public sealed class OverviewView : Control
{
    private const double W = 984, H = 676;
    private OverviewModel? _m;
    private (string Id, Rect R)[] _hits = BuildHits(6);
    private int _hitsFor = 6;

    public void Update(OverviewModel m)
    {
        _m = m;
        if (m.ValveCount != _hitsFor) { _hitsFor = m.ValveCount; _hits = BuildHits(m.ValveCount); }
        InvalidateVisual();
    }

    /// <summary>點到圖示：代號（V0~V6、M0~M7、Vac:{VacuumOutputs}、Purge、RF、SV、LlRough、LlVent、LL、PM）。</summary>
    public event Action<string>? ItemClicked;

    // ---------------- 版面（設計座標）
    private const double MfcX = 712, HdrX = 692;
    private static double RowY(int i) => 36 + i * 37;
    private static double AvX(int i, int n) => 264 + i * (n <= 6 ? 45 : 38);
    private static readonly Rect LlBox = new(40, 262, 250, 158), SvBox = new(290, 262, 30, 158), PmBox = new(320, 262, 280, 158);
    private static readonly Rect RfBox = new(522, 56, 134, 140), AldBox = new(238, 40, 276, 180);
    private static readonly Rect TurboBox = new(600, 452, 80, 62), TurboSt = new(688, 448, 110, 94), DryBox = new(560, 610, 110, 46);
    private static readonly Rect Panel = new(806, 336, 174, 332);
    private static readonly Point PurgeV = new(400, 20), LlRoughV = new(90, 470), LlVentV = new(200, 470), VentV = new(400, 470),
        RoughV = new(560, 470), GvV = new(640, 410), ForeV = new(640, 575);

    /// <summary>可點的區域。小的放前面，先比對到小的。</summary>
    private static (string, Rect)[] BuildHits(int n)
    {
        var l = new List<(string, Rect)>();
        for (int i = 0; i < n; i++) l.Add(($"V{i}", new Rect(AvX(i, n) - 21, 111, 42, 57)));
        for (int i = 0; i < 8; i++) l.Add(($"M{i}", new Rect(MfcX - 2, RowY(i) - 14, 270, 28)));
        l.Add(("Purge", new Rect(PurgeV.X - 12, 3, 176, 38)));
        l.Add(("RF", RfBox.Inflate(2)));
        l.Add(($"Vac:{VacuumOutputs.ChamberVent}", new Rect(VentV.X - 14, 444, 72, 54)));
        l.Add(($"Vac:{VacuumOutputs.ChamberRough}", new Rect(502, 444, 72, 54)));
        l.Add(($"Vac:{VacuumOutputs.TurboGv}", new Rect(GvV.X - 18, 390, 84, 52)));
        l.Add(($"Vac:{VacuumOutputs.TurboRough}", new Rect(ForeV.X - 14, 557, 100, 36)));
        l.Add(($"Vac:{VacuumOutputs.TurboOn}", new Rect(TurboBox.X - 2, TurboSt.Y - 2, TurboSt.Right - TurboBox.X + 4, TurboSt.Height + 4)));
        l.Add(($"Vac:{VacuumOutputs.DryPumpOn}", DryBox.Inflate(2)));
        l.Add(("LlRough", new Rect(38, 444, 72, 54)));
        l.Add(("LlVent", new Rect(LlVentV.X - 14, 444, 72, 54)));
        l.Add(("SV", new Rect(SvBox.X - 7, SvBox.Y, SvBox.Width + 14, 200)));
        l.Add(("PM", PmBox));
        l.Add(("PM", Panel));
        l.Add(("LL", LlBox));
        return l.ToArray();
    }

    private string? _hover;

    public OverviewView() { ClipToBounds = true; }

    /// <summary>畫面座標 → 設計座標（跟 Render 用同一個縮放）。</summary>
    private Point ToDesign(Point p)
    {
        var b = Bounds;
        double k = Math.Min(b.Width / W, b.Height / H);
        if (k <= 0) return new Point(-1, -1);
        return new Point((p.X - (b.Width - W * k) / 2) / k, (p.Y - (b.Height - H * k) / 2) / k);
    }

    private string? HitAt(Point screen)
    {
        if (_m is null) return null;
        var d = ToDesign(screen);
        foreach (var (id, r) in _hits) if (r.Contains(d)) return id;
        return null;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var h = HitAt(e.GetPosition(this));
        if (h == _hover) return;
        _hover = h;
        Cursor = h is null ? Cursor.Default : new Cursor(StandardCursorType.Hand);
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_hover is null) return;
        _hover = null; Cursor = Cursor.Default; InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (HitAt(e.GetPosition(this)) is { } id) { e.Handled = true; ItemClicked?.Invoke(id); }
    }

    private static readonly Pen HoverPen = new(new SolidColorBrush(Color.Parse("#2F8FE8")), 2.5, dashStyle: new DashStyle(new double[] { 3, 2 }, 0));

    private static IBrush B(string c) => new SolidColorBrush(Color.Parse(c));
    private static readonly IBrush Bg = B("#E4EAF1"), Body = B("#F7F9FB"), Line = B("#5A6B7D"), Ink = B("#1F2A36"), Dim = B("#6B7785"),
        On = B("#2E9E4F"), Off = B("#B8C1CB"), Moving = B("#F0A030"), Alarm = B("#D23C3C"), Closed = B("#5A6B7D"), SpBg = B("#E3E8EE"),
        WaferBlue = B("#2F8FE8"), WaferGreen = B("#1F8A3A"), WaferUnknown = B("#F0A030"), LidOpen = B("#FFF3D6");
    private static readonly Pen PipePen = new(Line, 3), FlowPen = new(On, 4), Outline = new(Line, 1.5), Thin = new(Line, 1.2),
        RoPen = new(Ink, 1.4), AlarmPen = new(Alarm, 3), MovingPen = new(Moving, 2.5), DashPen = new(Line, 1.2, dashStyle: new DashStyle(new double[] { 5, 4 }, 0)),
        WaferDash = new(Line, 1.5, dashStyle: new DashStyle(new double[] { 6, 4 }, 0));
    private static readonly FontFamily Mono = new("Consolas, DejaVu Sans Mono, Menlo, monospace");

    public override void Render(DrawingContext ctx)
    {
        var b = Bounds;
        ctx.FillRectangle(Bg, new Rect(b.Size));
        if (_m is null || b.Width < 50 || b.Height < 50) return;
        double k = Math.Min(b.Width / W, b.Height / H);
        var origin = new Point((b.Width - W * k) / 2, (b.Height - H * k) / 2);
        using (ctx.PushTransform(Matrix.CreateScale(k, k) * Matrix.CreateTranslation(origin.X, origin.Y)))
        {
            Draw(ctx, _m);
            if (_hover is not null)
                foreach (var hit in _hits.Where(h => h.Id == _hover))   // PM 有腔體跟數值面板兩塊，一起框
                    ctx.DrawRectangle(null, HoverPen, hit.R.Inflate(2), 5, 5);
        }
    }

    // ================================================================ 管路與流通判斷

    /// <summary>
    /// 管路分段與「有流通」判斷（綠色）。規則：
    /// 通氣 = 有流量的 MFC（上游閥開且 &gt; 0.5 % FS）→ 匯流管 → RF（沒有閥，一定通）→ Chamber；
    /// 匯流管 → ALD VALVE PURGE（開）→ 閥組分配管 → 開著的 AV → 匯流進 Chamber。
    /// 抽氣 = Dry pump 運轉時，開著的 Chamber 粗抽 / LL 粗抽 / 前級閥那一路；Turbo 全速 + 前級通 + Iso Valve 開，Chamber → Turbo 那段也算。
    /// Vent = Vent 閥開時 VENT N2 → 該腔體。
    /// </summary>
    public static List<(Point[] Pts, bool Flow)> FlowSegments(MachineSnapshot s, int n)
    {
        var segs = new List<(Point[], bool)>();
        void Add(bool flow, params (double X, double Y)[] p) => segs.Add((p.Select(q => new Point(q.X, q.Y)).ToArray(), flow));
        bool conn = s.Connected;
        var flowing = Enumerable.Range(0, 8).Select(i => conn && (s.MfcUpstreamValves >> i & 1) != 0 && s.MfcPct.Length > i && s.MfcPct[i] > 0.5).ToArray();
        bool gas = flowing.Any(f => f);
        bool purge = gas && s.AldPurgeOpen;
        var vo = s.VacuumOutputs;
        bool dry = conn && s.DryPumpRunning && !s.DryPumpAlarm;

        // MFC 支線、匯流管（往上到頂、往 RF 的 y = 90）
        for (int i = 0; i < 8; i++) Add(flowing[i], (HdrX, RowY(i)), (MfcX + 28, RowY(i)));
        double yTop = 20, yRf = 90;
        var rows = Enumerable.Range(0, 8).Where(i => flowing[i]).Select(RowY).ToList();
        double gLo = rows.Count > 0 ? Math.Min(rows.Min(), yRf) : 0, gHi = rows.Count > 0 ? Math.Max(rows.Max(), yRf) : 0;
        if (purge) gLo = yTop;
        Add(false, (HdrX, yTop), (HdrX, RowY(7)));
        if (gas) Add(true, (HdrX, gLo), (HdrX, gHi));
        Add(gas, (HdrX, yRf), (RfBox.Right, yRf));
        Add(gas, (570, RfBox.Bottom), (570, PmBox.Y));
        // 匯流管頂 → ALD VALVE PURGE → 分配管
        Add(purge, (HdrX, yTop), (PurgeV.X, yTop));
        Add(purge, (PurgeV.X, yTop), (226, yTop), (226, 100), (AvX(n - 1, n), 100));
        bool anyAv = false;
        for (int i = 0; i < n; i++)
        {
            bool open = (s.AldValves >> i & 1) != 0;
            double x = AvX(i, n);
            Add(purge, (x, 100), (x, 134));
            Add(purge && open, (x, 166), (x, 200));
            anyAv |= purge && open;
        }
        Add(anyAv, (AvX(0, n), 200), (AvX(n - 1, n), 200));
        Add(anyAv, (380, 200), (380, PmBox.Y));

        // 抽氣
        bool fore = dry && vo.HasFlag(VacuumOutputs.TurboRough);
        bool turbo = fore && s.TurboAtSpeed && s.TurboGateOpened;
        Add(turbo, (PmBox.Right, 380), (640, 380), (640, TurboBox.Y));
        Add(fore, (640, TurboBox.Bottom), (640, DryBox.Y));
        Add(dry && vo.HasFlag(VacuumOutputs.ChamberRough), (560, PmBox.Bottom), (560, DryBox.Y));
        Add(dry && s.LoadlockFresh && s.LoadlockRoughOpen, (90, LlBox.Bottom), (90, 640), (DryBox.X, 640));

        // Vent（共用 VENT N2）
        bool pmVent = conn && vo.HasFlag(VacuumOutputs.ChamberVent), llVent = s.LoadlockFresh && s.LoadlockVentOpen;
        Add(llVent, (200, LlBox.Bottom), (200, 535), (300, 535));
        Add(pmVent, (400, PmBox.Bottom), (400, 535), (300, 535));
        Add(llVent || pmVent, (300, 535), (300, 575));
        return segs;
    }

    // ================================================================ 畫圖

    private static string Sci(double t) => double.IsNaN(t) ? "---" : t <= 0 ? "< 1.0E-03" : t.ToString("0.00E+00", CultureInfo.InvariantCulture);
    private static string Short(string t, int n) => t.Length > n ? t[..n] : t;

    private static void Draw(DrawingContext g, OverviewModel m)
    {
        var s = m.S;
        bool conn = s.Connected;
        var vo = s.VacuumOutputs;
        var blk = s.VacuumBlocked;
        int n = Math.Clamp(m.ValveCount, 1, 7);

        // ---------------- 管線：先畫灰色，有流通的再蓋綠色；元件蓋在管線上面
        var segs = FlowSegments(s, n);
        foreach (var (pts, _) in segs) Poly(g, PipePen, pts);
        foreach (var (pts, f) in segs) if (f) Poly(g, FlowPen, pts);

        // ---------------- ALD VALVE PURGE
        Valve(g, PurgeV, false, s.AldPurgeOpen ? On : Off, false, 10);
        Pair(g, new Rect(PurgeV.X + 18, 4, 40, 32), "開", "關", !conn ? -1 : s.AldPurgeOpen ? 0 : 1);
        TextM(g, "ALD VALVE PURGE", PurgeV.X + 64, 31, 11, Dim);

        // ---------------- ALD 閥組
        g.DrawRectangle(null, DashPen, AldBox, 8, 8);
        TextM(g, "ALD 閥組", AldBox.X + 8, 54, 12, Dim);
        TextM(g, s.AldRunning ? $"ALD cycle {s.AldCycle}" : "", AldBox.Right - 8, 54, 11, s.AldRunning ? WaferGreen : Dim, align: 1, bold: s.AldRunning);
        for (int i = 0; i < n; i++)
        {
            double x = AvX(i, n);
            bool open = (s.AldValves >> i & 1) != 0;
            g.DrawRectangle(open ? On : Off, Outline, new Rect(x - 17, 134, 34, 32), 4, 4);
            TextM(g, $"AV{i + 1}", x, 151, 12, open ? Brushes.White : Ink, align: 0.5, bold: true);
            string nm = m.ValveNames.Length > i && !string.IsNullOrWhiteSpace(m.ValveNames[i]) ? m.ValveNames[i].Trim() : $"AV{i + 1}";
            var tag = new Rect(x - 21, 113, 42, 17);
            g.DrawRectangle(Brushes.White, new Pen(Off, 1), tag, 3, 3);
            TextIn(g, Short(nm, 6), tag, 11, Ink);
        }

        // ---------------- RF（功率等通訊手冊，先顯示 ----）
        g.DrawRectangle(Body, Outline, RfBox, 6, 6);
        TextM(g, "RF", RfBox.X + 8, 69, 14, Ink, bold: true);
        bool rfOn = conn && s.RfOn;
        Cell(g, new Rect(528, 82, 61, 22), "ON", rfOn ? On : Brushes.White, rfOn ? Brushes.White : Dim);
        Cell(g, new Rect(589, 82, 61, 22), "OFF", rfOn ? Brushes.White : Closed, rfOn ? Dim : Brushes.White);
        string[] rfl = ["設定", "Fwd", "Ref"];
        for (int i = 0; i < 3; i++)
        {
            double y = 110 + i * 27;
            TextM(g, rfl[i], 530, y + 11, 11, Dim);
            Readout(g, new Rect(566, y, 84, 22), "----", 12);
        }

        // ---------------- MFC
        TextM(g, "實際", MfcX + 152, 14, 11, Dim, align: 0.5);
        TextM(g, "設定", MfcX + 211, 14, 11, Dim, align: 0.5);
        for (int i = 0; i < 8; i++)
        {
            double y = RowY(i);
            bool up = (s.MfcUpstreamValves >> i & 1) != 0;
            Valve(g, new Point(MfcX + 12, y), false, up ? On : Off, false, 9);
            var nr = new Rect(MfcX + 28, y - 12, 92, 24);
            g.DrawRectangle(Brushes.White, Thin, nr, 3, 3);
            TextIn(g, $"{i + 1} {Short(m.MfcNames.Length > i ? m.MfcNames[i] : $"MFC{i + 1}", 13)}", nr, 11, Ink);
            Readout(g, new Rect(MfcX + 124, y - 12, 58, 24), conn && s.MfcValue.Length > i ? $"{s.MfcValue[i]:0.0}" : "---", 13);
            var sr = new Rect(MfcX + 188, y - 12, 46, 24);
            g.DrawRectangle(SpBg, Thin, sr, 3, 3);
            double sp = m.MfcSetpoints.Length > i ? m.MfcSetpoints[i] : 0;
            TextIn(g, !conn || s.SafeState ? "—" : $"{sp:0.#}", sr, 12, Ink, mono: true, bold: true);
            TextM(g, m.MfcUnits.Length > i ? Short(m.MfcUnits[i], 4) : "", MfcX + 239, y, 10, Dim);
        }
        TextM(g, "閥符號 = MFC 上游閥（綠 = 開）　灰底 = 設定值", MfcX, RowY(7) + 24, 10, Dim);

        // ---------------- Loadlock
        g.DrawRectangle(Body, Outline, LlBox, 6, 6);
        TextM(g, "Loadlock", 52, 282, 16, Ink, bold: true);
        TextM(g, "P", 180, 282, 12, Dim, align: 1);
        string llp = !s.LoadlockFresh ? "未連線" : s.LoadlockPressureUnderRange ? "< 1 Torr" : double.IsNaN(s.LoadlockPressureTorr) ? "---" : $"{s.LoadlockPressureTorr:0.0} Torr";
        Readout(g, new Rect(186, 270, 96, 26), llp, 13, s.LoadlockPressureSuspect || !s.LoadlockFresh ? Moving : null);
        Wafer(g, new Point(150, 352), 44, m.WaferLl, false, m.LlWaferId);
        string z = s.ArmZ switch { LoadlockZ.Up => "Z 上", LoadlockZ.Down => "Z 下", LoadlockZ.Raising => "Z 上升中", LoadlockZ.Lowering => "Z 下降中", _ => "Z ?" };
        TextM(g, $"手臂 {s.ArmPosSteps} step　{z}", 52, 408, 11, s.ArmMoving ? Moving : Dim);
        TextM(g, s.LoadlockLidOpen ? "上蓋開" : "上蓋 關", 280, 408, 11, s.LoadlockLidOpen ? Moving : Dim, align: 1, bold: s.LoadlockLidOpen);
        if (s.LoadlockAlarm != 0) TextM(g, $"⚠ 警報 {s.LoadlockAlarm}", 280, 300, 12, Alarm, align: 1, bold: true);

        // ---------------- Slit valve
        var svFill = !s.LoadlockFresh ? Off : s.SlitValve switch { LoadlockSv.Open => SvOpenFill, LoadlockSv.Closed => Closed, LoadlockSv.Error => Alarm, _ => Moving };
        g.DrawRectangle(svFill, null, SvBox);
        if (m.Transferring) TextM(g, "傳片中", SvBox.Center.X, 246, 13, Moving, align: 0.5, bold: true);
        else TextM(g, "Slit Valve", SvBox.Center.X, 246, 12, Dim, align: 0.5);
        int svAct = !s.LoadlockFresh ? -1 : s.SlitValve switch { LoadlockSv.Open => 0, LoadlockSv.Closed => 1, _ => -1 };
        Pair(g, new Rect(285, 428, 40, 32), "開", "關", svAct, err: s.LoadlockFresh && s.SlitValve == LoadlockSv.Error);
        TextM(g, "LL − PM 壓差", 305, 478, 11, Dim, align: 0.5);
        string diff = !conn || !s.LoadlockFresh ? "---" : s.LoadlockPressureUnderRange ? "LL < 1 Torr"
                    : double.IsNaN(s.LoadlockPressureTorr) ? "---" : $"{s.LoadlockPressureTorr - s.ChamberPressureTorr:0.0} Torr";
        Readout(g, new Rect(258, 490, 94, 24), diff, 12);

        // ---------------- ALD Chamber
        g.DrawRectangle(s.ChamberLidClosed || !conn ? Body : LidOpen, Outline, PmBox, 6, 6);
        TextM(g, "ALD Chamber", 332, 282, 16, Ink, bold: true);
        TextM(g, "P", 486, 282, 12, Dim, align: 1);
        Readout(g, new Rect(492, 270, 100, 26), conn ? Sci(s.ChamberPressureTorr) : "---", 14);
        Wafer(g, new Point(450, 352), 46, m.WaferPm, m.Processing && m.WaferPm == true, m.PmWaferId);
        TextM(g, $"Lift {(s.WaferLiftUp ? "上" : s.WaferLiftDown ? "下" : "中")}　Heater {(s.HeaterAtProcess ? "Process" : s.HeaterDown ? "下位" : "移動")}", 332, 408, 11, Dim);
        TextM(g, conn && !s.ChamberLidClosed ? "上蓋開" : "上蓋 關", 592, 408, 11, conn && !s.ChamberLidClosed ? Moving : Dim, align: 1, bold: conn && !s.ChamberLidClosed);

        // ---------------- 下方閥（LL 粗抽、LL Vent、Chamber Vent、Chamber 粗抽）
        int llR = s.LoadlockFresh ? (s.LoadlockRoughOpen ? 0 : 1) : -1, llV = s.LoadlockFresh ? (s.LoadlockVentOpen ? 0 : 1) : -1;
        Valve(g, LlRoughV, true, llR == 0 ? On : Off, false);
        TextM(g, "LL 粗抽 R", 106, 452, 12, Dim);
        Pair(g, new Rect(40, 455, 40, 32), "開", "關", llR);
        Valve(g, LlVentV, true, llV == 0 ? On : Off, false);
        TextM(g, "LL Vent", 216, 452, 12, Dim);
        Pair(g, new Rect(216, 462, 40, 32), "開", "關", llV);
        VacValve(g, VentV, vo, blk, VacuumOutputs.ChamberVent, conn);
        TextM(g, "Vent", 416, 452, 12, Dim);
        VacPair(g, new Rect(416, 462, 40, 32), vo, blk, VacuumOutputs.ChamberVent, conn);
        VacValve(g, RoughV, vo, blk, VacuumOutputs.ChamberRough, conn);
        TextM(g, "粗抽 R", 544, 452, 12, Dim, align: 1);
        VacPair(g, new Rect(504, 462, 40, 32), vo, blk, VacuumOutputs.ChamberRough, conn);
        TextM(g, "VENT N2", 300, 590, 13, Dim, align: 0.5, bold: true);

        // ---------------- Turbo
        Valve(g, GvV, true, s.TurboGateOpened ? On : s.TurboGateClosed ? Off : Moving, blk.HasFlag(VacuumOutputs.TurboGv), 16);
        TextM(g, "Turbo Iso Valve", 662, 398, 12, Dim);
        Pair(g, new Rect(662, 408, 40, 32), "開", "關", !conn ? -1 : s.TurboGateOpened ? 0 : s.TurboGateClosed ? 1 : -1, err: blk.HasFlag(VacuumOutputs.TurboGv));
        bool tCmd = vo.HasFlag(VacuumOutputs.TurboOn);
        g.DrawRectangle(s.TurboAlarm ? Alarm : s.TurboAtSpeed ? On : tCmd ? Moving : Off, Outline, TurboBox, 6, 6);
        TextIn(g, "Turbo", TurboBox, 14, s.TurboAlarm || s.TurboAtSpeed || tCmd ? Brushes.White : Ink, bold: true);
        g.DrawRectangle(Brushes.White, Thin, TurboSt, 4, 4);
        Cell(g, new Rect(692, 452, 51, 22), "ON", tCmd ? On : Brushes.White, tCmd ? Brushes.White : Dim);
        Cell(g, new Rect(743, 452, 51, 22), "OFF", tCmd ? Brushes.White : Closed, tCmd ? Dim : Brushes.White);
        TextM(g, "轉速", 694, 488, 11, Dim);
        Readout(g, new Rect(722, 477, 72, 22), "----", 12);   // 轉速訊號規格待確認
        Cell(g, new Rect(692, 504, 51, 32), s.TurboAtSpeed ? "全速" : tCmd ? "加速中" : "停止", s.TurboAtSpeed ? On : tCmd ? Moving : Off, s.TurboAtSpeed || tCmd ? Brushes.White : Ink);
        Cell(g, new Rect(743, 504, 51, 32), s.TurboAlarm ? "警報" : "無異常", s.TurboAlarm ? Alarm : On, Brushes.White);
        VacValve(g, ForeV, vo, blk, VacuumOutputs.TurboRough, conn);
        VacPair(g, new Rect(658, 559, 40, 32), vo, blk, VacuumOutputs.TurboRough, conn);
        TextM(g, "前級閥", 704, 575, 12, Dim);

        // ---------------- Dry pump
        bool dCmd = vo.HasFlag(VacuumOutputs.DryPumpOn);
        var dFill = s.DryPumpAlarm ? Alarm : s.DryPumpRunning ? On : dCmd ? Moving : Off;
        g.DrawRectangle(dFill, Outline, DryBox, 6, 6);
        var dFg = dFill == Off ? Ink : Brushes.White;
        TextM(g, "Dry Pump", DryBox.Center.X, 627, 14, dFg, align: 0.5, bold: true);
        TextM(g, s.DryPumpAlarm ? "警報" : s.DryPumpRunning ? "運轉" : dCmd ? "啟動中" : "停止", DryBox.Center.X, 645, 11, dFg, align: 0.5);
        TextM(g, "LL 粗抽、Chamber 粗抽、Turbo 前級共用同一台 Dry Pump　｜　綠色管路 = 有氣體流動或正在抽氣", 320, 662, 11, Dim, align: 0.5);

        // ---------------- PM 數值面板
        var p = Panel;
        g.DrawRectangle(Body, Outline, p, 6, 6);
        TextM(g, "PM 數值", p.X + 8, p.Y + 13, 12, Ink, bold: true);
        TextM(g, "Chuck 溫度 °C", p.X + 8, p.Y + 32, 10, Dim);
        bool tOk = conn && !double.IsNaN(m.ChuckTempC);
        Readout(g, new Rect(p.X + 8, p.Y + 40, 90, 26), tOk ? $"{m.ChuckTempC:0.0}" : "---", 15, tOk ? null : Moving);
        TextM(g, m.ChuckSpC is { } csp ? $"設定 {csp:0}" : "設定 未加熱", p.X + 104, p.Y + 53, 10, Dim);
        string[] gn = ["Barocel", "Ion", "Foreline", "PM 互鎖"];
        double[] gv = [s.ManometerTorr, s.IonGaugeTorr, s.ForelineTorr, s.ChamberPressureTorr];
        for (int i = 0; i < 4; i++)
        {
            double y = p.Y + 78 + i * 28;
            TextM(g, gn[i], p.X + 8, y + 11, 11, Dim);
            Readout(g, new Rect(p.X + 70, y, 96, 22), conn ? Sci(gv[i]) : "---", 12);
        }
        TextM(g, "PM 晶圓 ID", p.X + 8, p.Y + 197, 11, Dim);
        IdBox(g, new Rect(p.X + 70, p.Y + 186, 96, 22), m.WaferPm, m.PmWaferId);
        TextM(g, "Wafer lift", p.X + 8, p.Y + 225, 10, Dim);
        TextM(g, "Heater", p.X + 63, p.Y + 225, 10, Dim);
        TextM(g, "上蓋", p.X + 120, p.Y + 225, 10, Dim);
        Pair(g, new Rect(p.X + 6, p.Y + 234, 50, 32), "上", "下", s.WaferLiftUp ? 0 : s.WaferLiftDown ? 1 : -1);
        Pair(g, new Rect(p.X + 60, p.Y + 234, 54, 32), "Process", "下位", s.HeaterAtProcess ? 0 : s.HeaterDown ? 1 : -1, err: s.HeaterAxis == AxisState.Error);
        Pair(g, new Rect(p.X + 118, p.Y + 234, 50, 32), "關閉", "開啟", !conn ? -1 : s.ChamberLidClosed ? 0 : 1, bOn: Moving);
        TextM(g, "LL 晶圓 ID", p.X + 8, p.Y + 284, 11, Dim);
        IdBox(g, new Rect(p.X + 70, p.Y + 273, 96, 22), m.WaferLl, m.LlWaferId);
        TextM(g, blk == VacuumOutputs.None ? "互鎖擋下：無" : $"⚠ 擋下 {Short(blk.ToString(), 18)}", p.X + 8, p.Y + 314, 11, blk == VacuumOutputs.None ? Dim : Alarm, bold: blk != VacuumOutputs.None);

        if (!conn) TextM(g, "主控板未連線", W / 2, H / 2, 26, Alarm, align: 0.5, bold: true);
    }

    private static readonly IBrush SvOpenFill = B("#F2D22E");

    private static void Poly(DrawingContext g, Pen pen, Point[] p)
    {
        for (int i = 1; i < p.Length; i++) g.DrawLine(pen, p[i - 1], p[i]);
    }

    private static void VacValve(DrawingContext g, Point c, VacuumOutputs vo, VacuumOutputs blk, VacuumOutputs bit, bool conn)
        => Valve(g, c, true, conn && vo.HasFlag(bit) ? On : Off, blk.HasFlag(bit));

    private static void VacPair(DrawingContext g, Rect r, VacuumOutputs vo, VacuumOutputs blk, VacuumOutputs bit, bool conn)
        => Pair(g, r, "開", "關", !conn ? -1 : vo.HasFlag(bit) ? 0 : 1, err: blk.HasFlag(bit));

    /// <summary>閥（蝴蝶結）。vertical = 管路垂直（上下兩個三角）。</summary>
    private static void Valve(DrawingContext g, Point c, bool vertical, IBrush fill, bool blocked, double r = 11)
    {
        var geo = new StreamGeometry();
        using (var x = geo.Open())
        {
            if (vertical)
            {
                x.BeginFigure(new Point(c.X - r, c.Y - r), true); x.LineTo(new Point(c.X + r, c.Y - r)); x.LineTo(new Point(c.X - r, c.Y + r)); x.LineTo(new Point(c.X + r, c.Y + r)); x.EndFigure(true);
            }
            else
            {
                x.BeginFigure(new Point(c.X - r, c.Y - r), true); x.LineTo(new Point(c.X - r, c.Y + r)); x.LineTo(new Point(c.X + r, c.Y - r)); x.LineTo(new Point(c.X + r, c.Y + r)); x.EndFigure(true);
            }
        }
        g.DrawGeometry(fill, blocked ? AlarmPen : Outline, geo);
    }

    /// <summary>晶圓圓圈：有片藍（製程中綠）寫 ID、無片白色虛線、不明橘。</summary>
    private static void Wafer(DrawingContext g, Point c, double r, bool? state, bool processing, string id)
    {
        switch (state)
        {
            case true:
                g.DrawEllipse(processing ? WaferGreen : WaferBlue, Outline, c, r, r);
                TextM(g, id.Length > 0 ? Short(id, 10) : "有片", c.X, c.Y - (id.Length > 0 ? 7 : 0), 14, Brushes.White, align: 0.5, bold: true);
                if (id.Length > 0) TextM(g, processing ? "製程中" : "有片", c.X, c.Y + 13, 11, Brushes.White, align: 0.5);
                break;
            case false:
                g.DrawEllipse(Brushes.White, WaferDash, c, r, r);
                TextM(g, "無片", c.X, c.Y, 13, Dim, align: 0.5);
                break;
            default:
                g.DrawEllipse(WaferUnknown, Outline, c, r, r);
                TextM(g, "不明", c.X, c.Y, 14, Brushes.White, align: 0.5, bold: true);
                break;
        }
    }

    /// <summary>實際值框：白底黑字（2026-11-25 使用者要求）。</summary>
    private static void Readout(DrawingContext g, Rect r, string text, double size, IBrush? warn = null)
    {
        g.DrawRectangle(Brushes.White, warn is null ? RoPen : new Pen(warn, 2), r, 3, 3);
        TextIn(g, text, r, size, warn ?? Brushes.Black, mono: true, bold: true);
    }

    private static void IdBox(DrawingContext g, Rect r, bool? wafer, string id)
    {
        g.DrawRectangle(wafer is null ? WaferUnknown : Brushes.White, Thin, r, 3, 3);
        string t = wafer switch { true => id.Length > 0 ? Short(id, 12) : "有片", false => "無片", null => "不明" };
        TextIn(g, t, r, 12, wafer is null ? Brushes.White : wafer == true ? WaferBlue : Dim, bold: wafer != false);
    }

    /// <summary>上下兩格狀態框：act 0 = 上格亮、1 = 下格亮、-1 = 動作中 / 不明（都不亮、橘框）；err = 紅框。</summary>
    private static void Pair(DrawingContext g, Rect r, string a, string b, int act, IBrush? aOn = null, IBrush? bOn = null, bool err = false)
    {
        double h = r.Height / 2;
        var ra = new Rect(r.X, r.Y, r.Width, h);
        var rb = new Rect(r.X, r.Y + h, r.Width, h);
        g.DrawRectangle(act == 0 ? aOn ?? On : Brushes.White, null, ra);
        g.DrawRectangle(act == 1 ? bOn ?? Closed : Brushes.White, null, rb);
        g.DrawRectangle(null, err ? AlarmPen : act < 0 ? MovingPen : Thin, r);
        TextIn(g, a, ra, 11, act == 0 ? Brushes.White : Dim, bold: act == 0);
        TextIn(g, b, rb, 11, act == 1 ? Brushes.White : Dim, bold: act == 1);
    }

    private static void Cell(DrawingContext g, Rect r, string text, IBrush bg, IBrush fg)
    {
        g.DrawRectangle(bg, new Pen(Line, 1), r);
        TextIn(g, text, r, 12, fg, bold: true);
    }

    private static FormattedText Ft(string text, double size, IBrush brush, bool mono, bool bold)
        => new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(mono ? Mono : FontFamily.Default, FontStyle.Normal, bold ? FontWeight.Bold : FontWeight.Normal), size, brush);

    /// <summary>文字置中在框裡。</summary>
    private static void TextIn(DrawingContext g, string text, Rect r, double size, IBrush brush, bool mono = false, bool bold = false)
    {
        var ft = Ft(text, size, brush, mono, bold);
        g.DrawText(ft, new Point(r.Center.X - ft.Width / 2, r.Center.Y - ft.Height / 2));
    }

    /// <summary>文字：y 是文字的垂直中心；align 0 = 靠左、0.5 = 置中、1 = 靠右（x 是對齊點）。</summary>
    private static void TextM(DrawingContext g, string text, double x, double y, double size, IBrush brush, double align = 0, bool bold = false)
    {
        if (text.Length == 0) return;
        var ft = Ft(text, size, brush, false, bold);
        g.DrawText(ft, new Point(x - ft.Width * align, y - ft.Height / 2));
    }
}
