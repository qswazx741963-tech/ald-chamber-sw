using System.Globalization;
using Ald.Core;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Ald.Hmi;

/// <summary>總覽俯視圖要的資料（每次畫面更新整包換掉）。</summary>
public sealed record OverviewModel(
    MachineSnapshot S,
    bool? WaferLl, bool? WaferPm,
    string LlWaferId, string PmWaferId,
    bool Processing,
    int ArmPmSteps,
    string[] MfcNames,
    string[] MfcUnits,
    double[] MfcSetpoints,
    string[] ValveNames,
    double ChuckTempC, double? ChuckSpC);

/// <summary>
/// 總覽俯視圖（2026-11-25 依使用者參考圖加資訊）：Loadlock（手臂 + 晶圓）─ Slit valve ─ PM（Heater / 晶圓），
/// 上方 ALD 閥、右上 MFC 表（實際 / 設定 / 單位），右側 PM 數值（壓力、Chuck 溫度、各壓力計、晶圓 ID、Lift / Heater / 上蓋），下方抽氣路。
/// 數值用黑底綠字框；主要的閥旁邊有「開 / 關」狀態框（亮的那格是目前狀態，動作中兩格都不亮、框變橘，互鎖擋下框變紅）。
/// 自己畫（DrawingContext），依設計座標 1000×640 等比縮放置中，視窗變大跟著放大。
/// 顏色：閥開 綠 / 關 灰 / 動作中 橘 / 互鎖擋下 紅框；泵 運轉 綠 / 停 灰 / 警報 紅；
/// 晶圓 無片 白 / 有片 藍 / 製程中 綠 / 不明 橘。
/// 圖示可以點（2026-11-12 使用者要求）：滑鼠移上去出現藍框、游標變手指，點下去發 ItemClicked(代號)，
/// 由 MainWindow 跳出小視窗直接操作（不用切到手動頁）。代號見 <see cref="Hits"/>。
/// </summary>
public sealed class OverviewView : Control
{
    private const double W = 1000, H = 640;
    private OverviewModel? _m;

    public void Update(OverviewModel m) { _m = m; InvalidateVisual(); }

    /// <summary>點到圖示：代號（V0~V6、M0~M7、Vac:{VacuumOutputs}、SV、LlRough、LlVent、LL、PM）。</summary>
    public event Action<string>? ItemClicked;

    // ---------------- 版面座標（設計座標）
    private static readonly Point Pm = new(520, 300);
    private const double PmR = 125;
    private static readonly Rect Ll = new(30, 215, 300, 170);
    private static readonly Rect SvR = new(342, 250, 22, 100);
    private const double ValveY = 72, MfcX = 735, MfcY = 8, MfcRow = 27, PanelX = 735, PanelY = 262;
    private static double ValveX(int i) => Pm.X - 120 + i * 40;
    private static readonly Point Vent = new(660, 145), Gv = new(520, 462), Turbo = new(520, 522), TurboRough = new(520, 578),
        Rough = new(400, 548), Dry = new(200, 612), LlRoughV = new(80, 410), LlVentV = new(160, 410);

    /// <summary>可點的區域（設計座標）。小的放前面，先比對到小的。</summary>
    public static readonly (string Id, Rect R)[] Hits = BuildHits();
    private static (string, Rect)[] BuildHits()
    {
        var l = new List<(string, Rect)>();
        for (int i = 0; i < 7; i++) l.Add(($"V{i}", new Rect(ValveX(i) - 17, ValveY - 4, 34, 48)));
        for (int i = 0; i < 8; i++) l.Add(($"M{i}", new Rect(MfcX - 3, MfcY + 21 + i * MfcRow, 258, MfcRow - 2)));
        l.Add(($"Vac:{VacuumOutputs.ChamberVent}", new Rect(Vent.X - 16, Vent.Y - 34, 82, 52)));
        l.Add(($"Vac:{VacuumOutputs.TurboGv}", new Rect(Gv.X - 80, Gv.Y - 34, 104, 56)));
        l.Add(($"Vac:{VacuumOutputs.TurboOn}", new Rect(Turbo.X - 27, Turbo.Y - 27, 162, 54)));
        l.Add(($"Vac:{VacuumOutputs.TurboRough}", new Rect(TurboRough.X - 16, TurboRough.Y - 17, 124, 34)));
        l.Add(($"Vac:{VacuumOutputs.ChamberRough}", new Rect(Rough.X - 70, Rough.Y - 34, 88, 52)));
        l.Add(($"Vac:{VacuumOutputs.DryPumpOn}", new Rect(Dry.X - 104, Dry.Y - 27, 132, 54)));
        l.Add(("SV", new Rect(SvR.X - 14, SvR.Y - 20, SvR.Width + 28, SvR.Height + 58)));
        l.Add(("LlRough", new Rect(LlRoughV.X - 20, LlRoughV.Y - 14, 64, 62)));
        l.Add(("LlVent", new Rect(LlVentV.X - 20, LlVentV.Y - 14, 64, 62)));
        l.Add(("PM", new Rect(Pm.X - 88, Pm.Y - 88, 176, 176)));
        l.Add(("PM", new Rect(PanelX - 3, PanelY - 3, 266, 222)));
        l.Add(("LL", new Rect(Ll.X, Ll.Y, Ll.Width, Ll.Height)));
        l.Add(("LL", new Rect(207, 392, 126, 47)));
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
        foreach (var (id, r) in Hits) if (r.Contains(d)) return id;
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

    private static readonly IBrush Bg = new SolidColorBrush(Color.Parse("#E4EAF1"));
    private static readonly IBrush Body = new SolidColorBrush(Color.Parse("#F7F9FB"));
    private static readonly IBrush Steel = new SolidColorBrush(Color.Parse("#C9D2DC"));
    private static readonly IBrush Line = new SolidColorBrush(Color.Parse("#5A6B7D"));
    private static readonly IBrush Ink = new SolidColorBrush(Color.Parse("#1F2A36"));
    private static readonly IBrush Dim = new SolidColorBrush(Color.Parse("#6B7785"));
    private static readonly IBrush On = new SolidColorBrush(Color.Parse("#2E9E4F"));
    private static readonly IBrush Off = new SolidColorBrush(Color.Parse("#B8C1CB"));
    private static readonly IBrush Moving = new SolidColorBrush(Color.Parse("#F0A030"));
    private static readonly IBrush Alarm = new SolidColorBrush(Color.Parse("#D23C3C"));
    private static readonly IBrush SvOpen = new SolidColorBrush(Color.Parse("#F2D22E"));
    private static readonly IBrush WaferBlue = new SolidColorBrush(Color.Parse("#2F8FE8"));
    private static readonly IBrush WaferGreen = new SolidColorBrush(Color.Parse("#1F8A3A"));
    private static readonly IBrush WaferUnknown = new SolidColorBrush(Color.Parse("#F0A030"));
    private static readonly Pen PipePen = new(Line, 3);
    private static readonly Pen Outline = new(Line, 1.5);
    private static readonly Pen AlarmPen = new(Alarm, 3);

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
                foreach (var hit in Hits.Where(h => h.Id == _hover))   // PM / LL 有圖跟數值框兩塊，一起框
                    ctx.DrawRectangle(null, HoverPen, hit.R.Inflate(2), 5, 5);
        }
    }

    private static readonly IBrush RoBg = new SolidColorBrush(Color.Parse("#11161C"));
    private static readonly IBrush RoFg = new SolidColorBrush(Color.Parse("#3DF06A"));
    private static readonly IBrush RoWarn = new SolidColorBrush(Color.Parse("#FFC940"));
    private static readonly IBrush ClosedBg = new SolidColorBrush(Color.Parse("#5A6B7D"));
    private static readonly Pen MovingPen = new(Moving, 2.5);
    private static readonly FontFamily Mono = new("Consolas, DejaVu Sans Mono, Menlo, monospace");

    private static string Sci(double t) => double.IsNaN(t) ? "---" : t <= 0 ? "< 1.0E-03" : t.ToString("0.00E+00", CultureInfo.InvariantCulture);
    private static string Short(string t, int n) => t.Length > n ? t[..n] : t;

    private static void Draw(DrawingContext g, OverviewModel m)
    {
        var s = m.S;
        bool conn = s.Connected;
        var vo = s.VacuumOutputs;
        var blk = s.VacuumBlocked;
        double armHome = Ll.X + 50, armPm = Pm.X;
        double yFore = Dry.Y;

        // ---------------- 管線（先畫，元件蓋在上面）
        g.DrawLine(PipePen, new Point(Pm.X, Pm.Y + PmR), new Point(Pm.X, yFore));          // PM → GV → Turbo → 前級
        g.DrawLine(PipePen, new Point(Pm.X, yFore), new Point(Dry.X + 24, yFore));         // 前級 → Dry pump
        g.DrawLine(PipePen, new Point(Pm.X - 95, Pm.Y + 80), new Point(Rough.X, Rough.Y - 70)); // PM → 粗抽閥
        g.DrawLine(PipePen, new Point(Rough.X, Rough.Y - 70), new Point(Rough.X, yFore));
        g.DrawLine(PipePen, new Point(Pm.X + 85, Pm.Y - 85), Vent);                         // Vent
        for (int i = 0; i < 7; i++)
        {
            double x = ValveX(i);
            g.DrawLine(PipePen, new Point(x, ValveY + 44), new Point(x, 128));
            g.DrawLine(PipePen, new Point(x, 128), new Point(Pm.X, Pm.Y - PmR + 10));
        }

        // ---------------- Loadlock
        g.DrawRectangle(Body, Outline, Ll, 14, 14);
        Text(g, "Loadlock", Ll.X + 12, Ll.Y + 10, 16, Ink, bold: true);
        string llp = !s.LoadlockFresh ? "未連線" : s.LoadlockPressureUnderRange ? "< 1 Torr"
                   : double.IsNaN(s.LoadlockPressureTorr) ? "---" : $"{s.LoadlockPressureTorr:0.0} Torr";
        Text(g, "壓力", Ll.Right - 122, Ll.Y + 12, 12, Dim, align: 1);
        Readout(g, new Rect(Ll.Right - 116, Ll.Y + 6, 106, 26), llp, 14, s.LoadlockPressureSuspect || !s.LoadlockFresh ? RoWarn : null);
        Text(g, $"手臂 {s.ArmPosSteps} step", Ll.X + 12, Ll.Bottom - 22, 12, Dim);
        if (s.LoadlockLidOpen) Text(g, "上蓋開", Ll.Right - 12, Ll.Bottom - 22, 13, Moving, align: 1, bold: true);
        if (s.LoadlockAlarm != 0) Text(g, $"⚠ 警報 {s.LoadlockAlarm}", Ll.X + 120, Ll.Bottom - 22, 13, Alarm, bold: true);
        // LL 粗抽 / 洩壓閥 + 開關狀態框
        LlValve(g, LlRoughV, "粗抽", s.LoadlockFresh ? (s.LoadlockRoughOpen ? 0 : 1) : -1);
        LlValve(g, LlVentV, "洩壓", s.LoadlockFresh ? (s.LoadlockVentOpen ? 0 : 1) : -1);
        // LL 晶圓 ID（參考圖 f）
        Text(g, "LL 晶圓 ID", 210, 394, 11, Dim);
        IdBox(g, new Rect(210, 410, 120, 26), m.WaferLl, m.LlWaferId);

        // 手臂：位置 step → 畫面長度（PM 位置 = PM 中心）
        double frac = m.ArmPmSteps > 0 ? Math.Clamp((double)s.ArmPosSteps / m.ArmPmSteps, 0, 1) : 0;
        double tip = armHome + 90 + frac * (armPm - armHome - 90);
        var armPen = new Pen(s.ArmMoving ? Moving : Line, 7, lineCap: PenLineCap.Round);
        g.DrawLine(armPen, new Point(armHome, Ll.Center.Y), new Point(tip, Ll.Center.Y));
        g.DrawEllipse(Steel, Outline, new Point(armHome, Ll.Center.Y), 16, 16);
        Text(g, s.ArmZ switch { LoadlockZ.Up => "Z 上", LoadlockZ.Down => "Z 下", LoadlockZ.Raising => "Z 上升中", LoadlockZ.Lowering => "Z 下降中", _ => "Z ?" },
             armHome, Ll.Center.Y + 22, 12, Dim, align: 0.5);

        // ---------------- PM
        Octagon(g, Pm, PmR, s.ChamberLidClosed || !conn ? Body : new SolidColorBrush(Color.Parse("#FFF3D6")), Outline);
        Text(g, "PM", Pm.X - PmR + 24, Pm.Y - PmR + 26, 16, Ink, bold: true);
        if (conn && !s.ChamberLidClosed) Text(g, "上蓋開", Pm.X + PmR - 24, Pm.Y - PmR + 26, 13, Moving, align: 1, bold: true);
        var heaterBrush = s.HeaterAxis == AxisState.Error ? Alarm : s.HeaterAtProcess ? Steel : new SolidColorBrush(Color.Parse("#E3E8EE"));
        g.DrawEllipse(heaterBrush, Outline, Pm, 86, 86);
        Wafer(g, Pm, 64, m.WaferPm, m.Processing && m.WaferPm == true, s.WaferLiftUp);
        if (m.WaferPm == true && m.PmWaferId.Length > 0) Text(g, m.PmWaferId, Pm.X, Pm.Y - 9, 14, Brushes.White, align: 0.5, bold: true);
        Text(g, $"Lift {(s.WaferLiftUp ? "上" : s.WaferLiftDown ? "下" : "中")}  Heater {(s.HeaterAtProcess ? "Process" : s.HeaterDown ? "下位" : "移動")}",
             Pm.X, Pm.Y + 92, 12, Dim, align: 0.5);
        if (s.AldRunning) Text(g, $"ALD cycle {s.AldCycle}", Pm.X, Pm.Y - PmR + 50, 14, WaferGreen, align: 0.5, bold: true);

        // 晶圓在 Loadlock 手臂上（手臂伸進 PM 時跟著到 PM 上方）
        if (m.WaferLl != false)
        {
            var c = new Point(Math.Max(tip, Ll.X + 150), Ll.Center.Y);
            Wafer(g, c, 52, m.WaferLl, false, false);
            if (m.WaferLl == true && m.LlWaferId.Length > 0) Text(g, m.LlWaferId, c.X, c.Y - 9, 13, Brushes.White, align: 0.5, bold: true);
        }

        // ---------------- Slit valve（畫在手臂上面）+ 壓差 + 開關狀態框（參考圖 d）
        Text(g, "LL − PM 壓差", SvR.Center.X, 176, 11, Dim, align: 0.5);
        bool llOk = s.LoadlockFresh && !double.IsNaN(s.LoadlockPressureTorr);
        string diff = !conn || !s.LoadlockFresh ? "---" : s.LoadlockPressureUnderRange ? "LL < 1 Torr"
                    : llOk ? $"{s.LoadlockPressureTorr - s.ChamberPressureTorr:0.0} Torr" : "---";
        Readout(g, new Rect(SvR.Center.X - 52, 192, 104, 24), diff, 13);
        Text(g, "Slit valve", SvR.Center.X, 232, 11, Dim, align: 0.5);
        var svBrush = !s.LoadlockFresh ? Off : s.SlitValve switch
        {
            LoadlockSv.Open => SvOpen, LoadlockSv.Closed => Line, LoadlockSv.Error => Alarm, _ => Moving,
        };
        if (s.SlitValve == LoadlockSv.Open) g.DrawRectangle(null, new Pen(svBrush, 4), SvR);
        else g.DrawRectangle(svBrush, null, SvR);
        int svAct = !s.LoadlockFresh ? -1 : s.SlitValve switch { LoadlockSv.Open => 0, LoadlockSv.Closed => 1, _ => -1 };
        Indicator(g, new Rect(SvR.Center.X - 22, SvR.Bottom + 6, 44, 34), "開", "關", svAct, On, ClosedBg, s.LoadlockFresh && s.SlitValve == LoadlockSv.Error);

        // ---------------- ALD 閥 V1~V7（下面小字 = 管路名稱）
        Text(g, "ALD 閥", Pm.X - 150, ValveY + 6, 13, Dim, align: 1);
        for (int i = 0; i < 7; i++)
        {
            double x = ValveX(i);
            bool open = (s.AldValves >> i & 1) != 0;
            g.DrawRectangle(open ? On : Off, Outline, new Rect(x - 14, ValveY, 28, 28), 4, 4);
            Text(g, $"V{i + 1}", x, ValveY + 6, 12, open ? Brushes.White : Ink, align: 0.5, bold: true);
            string n = m.ValveNames.Length > i ? m.ValveNames[i] ?? "" : "";
            if (n.Length > 0) Text(g, Short(n, 5), x, ValveY + 30, 10, Dim, align: 0.5);
        }

        // ---------------- MFC 表（參考圖 b）：上游閥燈、名稱、實際流量、設定、單位
        Text(g, "MFC", MfcX, MfcY + 2, 13, Ink, bold: true);
        Text(g, "實際", MfcX + 147, MfcY + 4, 11, Dim, align: 0.5);
        Text(g, "設定", MfcX + 202, MfcY + 4, 11, Dim, align: 0.5);
        for (int i = 0; i < 8; i++)
        {
            double y = MfcY + 22 + i * MfcRow;
            bool up = (s.MfcUpstreamValves >> i & 1) != 0;
            double v = s.MfcValue.Length > i ? s.MfcValue[i] : 0;
            g.DrawRectangle(up ? On : Off, Outline, new Rect(MfcX, y + 6, 11, 11), 2, 2);
            string name = m.MfcNames.Length > i ? m.MfcNames[i] : $"MFC{i + 1}";
            Text(g, $"{i + 1} {Short(name, 13)}", MfcX + 15, y + 4, 12, Ink);
            Readout(g, new Rect(MfcX + 118, y, 58, 23), conn ? $"{v:0.0}" : "---", 13);
            double sp = m.MfcSetpoints.Length > i ? m.MfcSetpoints[i] : 0;
            var sr = new Rect(MfcX + 180, y, 44, 23);
            g.DrawRectangle(Brushes.White, Outline, sr, 3, 3);
            TextIn(g, !conn || s.SafeState ? "—" : $"{sp:0.#}", sr, 12, Ink, mono: true);
            Text(g, m.MfcUnits.Length > i ? Short(m.MfcUnits[i], 4) : "", MfcX + 228, y + 6, 10, Dim);
        }
        Text(g, "■ 綠 = 上游閥開　設定 = 最後下的值", MfcX, MfcY + 22 + 8 * MfcRow + 2, 10, Dim);

        // ---------------- PM 數值面板（壓力、Chuck 溫度、壓力計、晶圓 ID、Lift / Heater / 上蓋）
        var pr = new Rect(PanelX, PanelY, 260, 216);
        g.DrawRectangle(Body, Outline, pr, 8, 8);
        Text(g, "PM 數值", pr.X + 10, pr.Y + 6, 13, Ink, bold: true);
        Text(g, "PM 壓力（Torr）", pr.X + 10, pr.Y + 26, 11, Dim);
        Readout(g, new Rect(pr.X + 8, pr.Y + 42, 124, 30), conn ? Sci(s.ChamberPressureTorr) : "---", 17);
        Text(g, "Chuck 溫度（°C）", pr.X + 138, pr.Y + 26, 11, Dim);
        bool tOk = conn && !double.IsNaN(m.ChuckTempC);
        Readout(g, new Rect(pr.X + 138, pr.Y + 42, 114, 30), tOk ? $"{m.ChuckTempC:0.0}" : "---", 17, tOk ? null : RoWarn);
        Text(g, m.ChuckSpC is { } csp ? $"設定 {csp:0} °C" : "未加熱", pr.X + 252, pr.Y + 74, 10, Dim, align: 1);
        string[] gn = ["Barocel", "Ion", "Foreline"];
        double[] gv = [s.ManometerTorr, s.IonGaugeTorr, s.ForelineTorr];
        for (int i = 0; i < 3; i++)
        {
            double x = pr.X + 8 + i * 82;
            Text(g, gn[i], x, pr.Y + 86, 11, Dim);
            Readout(g, new Rect(x, pr.Y + 101, 78, 22), conn ? Sci(gv[i]) : "---", 12);
        }
        Text(g, "PM 晶圓 ID", pr.X + 10, pr.Y + 134, 11, Dim);
        IdBox(g, new Rect(pr.X + 80, pr.Y + 130, 172, 24), m.WaferPm, m.PmWaferId);
        Text(g, "Wafer lift", pr.X + 10, pr.Y + 160, 11, Dim);
        Indicator(g, new Rect(pr.X + 8, pr.Y + 175, 70, 34), "上", "下", s.WaferLiftUp ? 0 : s.WaferLiftDown ? 1 : -1, On, ClosedBg);
        Text(g, "Heater", pr.X + 92, pr.Y + 160, 11, Dim);
        Indicator(g, new Rect(pr.X + 90, pr.Y + 175, 80, 34), "Process", "下位", s.HeaterAtProcess ? 0 : s.HeaterDown ? 1 : -1, On, ClosedBg, s.HeaterAxis == AxisState.Error);
        Text(g, "上蓋", pr.X + 184, pr.Y + 160, 11, Dim);
        Indicator(g, new Rect(pr.X + 182, pr.Y + 175, 70, 34), "關閉", "開啟", !conn ? -1 : s.ChamberLidClosed ? 0 : 1, On, Moving);
        if (blk != VacuumOutputs.None) Text(g, $"⚠ 互鎖擋下：{blk}", pr.X, pr.Bottom + 6, 12, Alarm, bold: true);

        // ---------------- Vent
        Valve(g, Vent, false, Out(vo, VacuumOutputs.ChamberVent), blk.HasFlag(VacuumOutputs.ChamberVent));
        Text(g, "Vent", Vent.X + 40, Vent.Y - 35, 11, Dim, align: 0.5);
        Indicator(g, new Rect(Vent.X + 18, Vent.Y - 17, 44, 34), "開", "關", vo.HasFlag(VacuumOutputs.ChamberVent) ? 0 : 1, On, ClosedBg, blk.HasFlag(VacuumOutputs.ChamberVent));

        // ---------------- 抽氣：Turbo GV、Turbo（參考圖 e）、前級閥、粗抽閥（參考圖 c）、Dry pump
        Valve(g, Gv, true, s.TurboGateOpened ? On : s.TurboGateClosed ? Off : Moving, blk.HasFlag(VacuumOutputs.TurboGv), 20);
        Text(g, "Turbo GV", Gv.X - 54, Gv.Y - 34, 11, Dim, align: 0.5);
        Indicator(g, new Rect(Gv.X - 76, Gv.Y - 17, 44, 34), "開", "關", s.TurboGateOpened ? 0 : s.TurboGateClosed ? 1 : -1, On, ClosedBg, blk.HasFlag(VacuumOutputs.TurboGv));

        bool tCmd = vo.HasFlag(VacuumOutputs.TurboOn);
        Pump(g, Turbo, "Turbo", s.TurboAlarm ? Alarm : s.TurboAtSpeed ? On : tCmd ? Moving : Off);
        var ts = new Rect(Turbo.X + 32, Turbo.Y - 24, 100, 48);
        Cell(g, new Rect(ts.X, ts.Y, ts.Width, 24), s.TurboAtSpeed ? "全速" : tCmd ? "加速中" : "停止", s.TurboAtSpeed ? On : tCmd ? Moving : Off, s.TurboAtSpeed || tCmd ? Brushes.White : Ink);
        Cell(g, new Rect(ts.X, ts.Y + 24, ts.Width, 24), s.TurboAlarm ? "警報" : "無異常", s.TurboAlarm ? Alarm : On, Brushes.White);
        g.DrawRectangle(null, Outline, ts);

        Valve(g, TurboRough, true, Out(vo, VacuumOutputs.TurboRough), blk.HasFlag(VacuumOutputs.TurboRough));
        Indicator(g, new Rect(TurboRough.X + 18, TurboRough.Y - 17, 44, 34), "開", "關", vo.HasFlag(VacuumOutputs.TurboRough) ? 0 : 1, On, ClosedBg, blk.HasFlag(VacuumOutputs.TurboRough));
        Text(g, "前級閥", TurboRough.X + 66, TurboRough.Y - 8, 12, Dim);

        Valve(g, Rough, true, Out(vo, VacuumOutputs.ChamberRough), blk.HasFlag(VacuumOutputs.ChamberRough));
        Text(g, "粗抽閥", Rough.X - 44, Rough.Y - 34, 11, Dim, align: 0.5);
        Indicator(g, new Rect(Rough.X - 66, Rough.Y - 17, 44, 34), "開", "關", vo.HasFlag(VacuumOutputs.ChamberRough) ? 0 : 1, On, ClosedBg, blk.HasFlag(VacuumOutputs.ChamberRough));

        bool dCmd = vo.HasFlag(VacuumOutputs.DryPumpOn);
        Pump(g, Dry, "Dry", s.DryPumpAlarm ? Alarm : s.DryPumpRunning ? On : dCmd ? Moving : Off);
        Text(g, "Dry pump", Dry.X - 30, Dry.Y - 24, 12, Dim, align: 1);
        Cell(g, new Rect(Dry.X - 100, Dry.Y - 6, 66, 22), s.DryPumpAlarm ? "警報" : s.DryPumpRunning ? "運轉" : dCmd ? "啟動中" : "停止",
             s.DryPumpAlarm ? Alarm : s.DryPumpRunning ? On : dCmd ? Moving : Off, s.DryPumpAlarm || s.DryPumpRunning || dCmd ? Brushes.White : Ink);
        Text(g, "Foreline（Torr）", 260, yFore - 44, 11, Dim);
        Readout(g, new Rect(260, yFore - 28, 90, 22), conn ? Sci(s.ForelineTorr) : "---", 12);

        if (!conn) Text(g, "主控板未連線", W / 2, H / 2 - 12, 26, Alarm, align: 0.5, bold: true);
    }

    /// <summary>LL 閥（水平）+ 名稱 + 開關狀態框。</summary>
    private static void LlValve(DrawingContext g, Point c, string name, int act)
    {
        Valve(g, c, false, act == 0 ? On : Off, false);
        Text(g, name, c.X + 16, c.Y - 8, 12, Dim);
        Indicator(g, new Rect(c.X - 18, c.Y + 16, 44, 34), "開", "關", act, On, ClosedBg);
    }

    /// <summary>黑底綠字數值框。</summary>
    private static void Readout(DrawingContext g, Rect r, string text, double size, IBrush? fg = null)
    {
        g.DrawRectangle(RoBg, null, r, 3, 3);
        TextIn(g, text, r, size, fg ?? RoFg, mono: true, bold: true);
    }

    /// <summary>晶圓 ID 框：有片顯示 ID（沒填就「有片」），無片 / 不明。</summary>
    private static void IdBox(DrawingContext g, Rect r, bool? wafer, string id)
    {
        g.DrawRectangle(wafer is null ? WaferUnknown : Brushes.White, Outline, r, 3, 3);
        string t = wafer switch { true => id.Length > 0 ? id : "有片", false => "無片", null => "不明" };
        TextIn(g, t, r, 13, wafer is null ? Brushes.White : wafer == true ? WaferBlue : Dim, bold: wafer != false);
    }

    /// <summary>上下兩格狀態框（例如 開 / 關）：active 0 = 上格亮、1 = 下格亮、-1 = 動作中（都不亮、橘框）；err = 紅框。</summary>
    private static void Indicator(DrawingContext g, Rect r, string a, string b, int active, IBrush aOn, IBrush bOn, bool err = false)
    {
        double h = r.Height / 2;
        var ra = new Rect(r.X, r.Y, r.Width, h);
        var rb = new Rect(r.X, r.Y + h, r.Width, h);
        g.DrawRectangle(active == 0 ? aOn : Brushes.White, null, ra);
        g.DrawRectangle(active == 1 ? bOn : Brushes.White, null, rb);
        g.DrawLine(new Pen(Off, 1), ra.BottomLeft, ra.BottomRight);
        g.DrawRectangle(null, err ? AlarmPen : active < 0 ? MovingPen : Outline, r);
        TextIn(g, a, ra, 11, active == 0 ? Brushes.White : Dim, bold: active == 0);
        TextIn(g, b, rb, 11, active == 1 ? Brushes.White : Dim, bold: active == 1);
    }

    private static void Cell(DrawingContext g, Rect r, string text, IBrush bg, IBrush fg)
    {
        g.DrawRectangle(bg, null, r);
        TextIn(g, text, r, 12, fg, bold: true);
    }

    /// <summary>文字置中在框裡。</summary>
    private static void TextIn(DrawingContext g, string text, Rect r, double size, IBrush brush, bool mono = false, bool bold = false)
    {
        var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(mono ? Mono : FontFamily.Default, FontStyle.Normal, bold ? FontWeight.Bold : FontWeight.Normal), size, brush);
        g.DrawText(ft, new Point(r.Center.X - ft.Width / 2, r.Center.Y - ft.Height / 2));
    }

    private static IBrush Out(VacuumOutputs vo, VacuumOutputs bit) => vo.HasFlag(bit) ? On : Off;

    /// <summary>閥（蝴蝶結符號）。vertical = 管路垂直。</summary>
    private static void Valve(DrawingContext g, Point c, bool vertical, IBrush fill, bool blocked, double r = 12)
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

    private static void Pump(DrawingContext g, Point c, string label, IBrush fill)
    {
        g.DrawEllipse(fill, Outline, c, 24, 24);
        Text(g, label, c.X, c.Y - 9, 13, fill == Off ? Ink : Brushes.White, align: 0.5, bold: true);
    }

    private static void Wafer(DrawingContext g, Point c, double r, bool? state, bool processing, bool lifted)
    {
        var fill = state switch { true => processing ? WaferGreen : WaferBlue, false => Brushes.White, null => WaferUnknown };
        g.DrawEllipse(fill, new Pen(lifted ? Ink : Line, lifted ? 3 : 1.5, dashStyle: state == false ? DashStyle.Dash : null), c, r, r);
        if (state is null) Text(g, "不明", c.X, c.Y - 10, 15, Brushes.White, align: 0.5, bold: true);
    }

    private static void Octagon(DrawingContext g, Point c, double r, IBrush fill, Pen pen)
    {
        var geo = new StreamGeometry();
        using (var x = geo.Open())
        {
            for (int i = 0; i < 8; i++)
            {
                double a = Math.PI / 8 + i * Math.PI / 4;
                var p = new Point(c.X + r * Math.Cos(a), c.Y + r * Math.Sin(a));
                if (i == 0) x.BeginFigure(p, true); else x.LineTo(p);
            }
            x.EndFigure(true);
        }
        g.DrawGeometry(fill, pen, geo);
    }

    /// <param name="align">0 = 靠左、0.5 = 置中、1 = 靠右（x 是對齊點）</param>
    private static void Text(DrawingContext g, string text, double x, double y, double size, IBrush brush, double align = 0, bool bold = false)
    {
        var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(FontFamily.Default, FontStyle.Normal, bold ? FontWeight.Bold : FontWeight.Normal), size, brush);
        g.DrawText(ft, new Point(x - ft.Width * align, y));
    }
}
