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
    string[] MfcNames);

/// <summary>
/// 總覽俯視圖：Loadlock（手臂 + 晶圓）─ Slit valve ─ PM（Heater / 晶圓），上方 ALD 閥與 MFC，下方抽氣路（Turbo GV、Turbo、前級閥、粗抽閥、Dry pump）。
/// 自己畫（DrawingContext），依設計座標 760×600 等比縮放置中，視窗變大跟著放大。
/// 顏色：閥開 綠 / 關 灰 / 動作中 橘 / 互鎖擋下 紅框；泵 運轉 綠 / 停 灰 / 警報 紅；
/// 晶圓 無片 白 / 有片 藍 / 製程中 綠 / 不明 橘。
/// 圖示可以點（2026-11-12 使用者要求）：滑鼠移上去出現藍框、游標變手指，點下去發 ItemClicked(代號)，
/// 由 MainWindow 跳出小視窗直接操作（不用切到手動頁）。代號見 <see cref="Hits"/>。
/// </summary>
public sealed class OverviewView : Control
{
    private const double W = 760, H = 600;
    private OverviewModel? _m;

    public void Update(OverviewModel m) { _m = m; InvalidateVisual(); }

    /// <summary>點到圖示：代號（V0~V6、M0~M7、Vac:{VacuumOutputs}、SV、LlRough、LlVent、LL、PM）。</summary>
    public event Action<string>? ItemClicked;

    /// <summary>可點的區域（設計座標）。小的放前面，先比對到小的。</summary>
    public static readonly (string Id, Rect R)[] Hits = BuildHits();
    private static (string, Rect)[] BuildHits()
    {
        var l = new List<(string, Rect)>();
        static Rect Sq(double x, double y, double r) => new(x - r, y - r, 2 * r, 2 * r);
        for (int i = 0; i < 7; i++) l.Add(($"V{i}", new Rect(530 - 120 + i * 40 - 16, 64, 32, 32)));
        for (int i = 0; i < 8; i++) l.Add(($"M{i}", new Rect(530 - 135 + i * 38 - 18, 6, 36, 42)));
        l.Add(($"Vac:{VacuumOutputs.ChamberVent}", Sq(670, 130, 16)));
        l.Add(($"Vac:{VacuumOutputs.TurboGv}", Sq(530, 410, 22)));
        l.Add(($"Vac:{VacuumOutputs.TurboOn}", Sq(530, 465, 26)));
        l.Add(($"Vac:{VacuumOutputs.TurboRough}", Sq(530, 520, 16)));
        l.Add(($"Vac:{VacuumOutputs.ChamberRough}", Sq(410, 505, 16)));
        l.Add(($"Vac:{VacuumOutputs.DryPumpOn}", Sq(220, 555, 26)));
        l.Add(("SV", new Rect(356, 214, 34, 112)));
        l.Add(("LlRough", Sq(120, 370, 16)));
        l.Add(("LlVent", Sq(190, 370, 16)));
        l.Add(("PM", Sq(530, 270, 84)));
        l.Add(("LL", new Rect(60, 190, 290, 160)));
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
            if (_hover is not null && Hits.FirstOrDefault(h => h.Id == _hover) is { Id: not null } hit)
                ctx.DrawRectangle(null, HoverPen, hit.R.Inflate(2), 5, 5);
        }
    }

    private static void Draw(DrawingContext g, OverviewModel m)
    {
        var s = m.S;
        bool conn = s.Connected;
        var vo = s.VacuumOutputs;
        var blk = s.VacuumBlocked;

        // ---------------- 座標
        var pm = new Point(530, 270);           // PM 中心
        const double pmR = 120;
        var ll = new Rect(60, 190, 290, 160);   // Loadlock
        var sv = new Rect(362, 220, 22, 100);   // Slit valve
        double armHome = ll.X + 50, armPm = pm.X;

        // ---------------- 抽氣路（先畫管線，元件蓋在上面）
        double yFore = 555;
        g.DrawLine(PipePen, new Point(pm.X, pm.Y + pmR), new Point(pm.X, yFore));            // PM → GV → Turbo → 前級
        g.DrawLine(PipePen, new Point(pm.X, yFore), new Point(250, yFore));                  // 前級 → Dry pump
        g.DrawLine(PipePen, new Point(pm.X - 95, pm.Y + 75), new Point(410, 470));           // PM → 粗抽閥
        g.DrawLine(PipePen, new Point(410, 470), new Point(410, yFore));
        g.DrawLine(PipePen, new Point(pm.X + 85, pm.Y - 85), new Point(pm.X + 140, pm.Y - 140)); // Vent
        // 氣體：ALD 閥 → PM 上方
        for (int i = 0; i < 7; i++)
        {
            double x = pm.X - 120 + i * 40;
            g.DrawLine(PipePen, new Point(x, 95), new Point(x, 125));
            g.DrawLine(PipePen, new Point(x, 125), new Point(pm.X, pm.Y - pmR + 10));
        }

        // ---------------- Loadlock
        g.DrawRectangle(Body, Outline, ll, 14, 14);
        Text(g, "Loadlock", ll.X + 12, ll.Y + 8, 16, Ink, bold: true);
        string llp = !s.LoadlockFresh ? "未連線" : s.LoadlockPressureUnderRange ? "< 1 Torr"
                   : double.IsNaN(s.LoadlockPressureTorr) ? "---" : $"{s.LoadlockPressureTorr:0.0} Torr";
        Text(g, llp, ll.Right - 12, ll.Y + 8, 15, s.LoadlockPressureSuspect ? Alarm : Ink, align: 1);
        // LL 粗抽 / 洩壓閥（小方塊）
        Valve(g, new Point(ll.X + 60, ll.Bottom + 20), false, s.LoadlockFresh ? (s.LoadlockRoughOpen ? On : Off) : Off, false);
        Text(g, "粗抽", ll.X + 60, ll.Bottom + 34, 12, Dim, align: 0.5);
        Valve(g, new Point(ll.X + 130, ll.Bottom + 20), false, s.LoadlockFresh ? (s.LoadlockVentOpen ? On : Off) : Off, false);
        Text(g, "洩壓", ll.X + 130, ll.Bottom + 34, 12, Dim, align: 0.5);
        if (s.LoadlockLidOpen) Text(g, "上蓋開", ll.Right - 12, ll.Bottom - 24, 13, Moving, align: 1, bold: true);

        // 手臂：位置 step → 畫面長度（PM 位置 = PM 中心）
        double frac = m.ArmPmSteps > 0 ? Math.Clamp((double)s.ArmPosSteps / m.ArmPmSteps, 0, 1) : 0;
        double tip = armHome + 90 + frac * (armPm - armHome - 90);
        var armPen = new Pen(s.ArmMoving ? Moving : Line, 7, lineCap: PenLineCap.Round);
        g.DrawLine(armPen, new Point(armHome, ll.Center.Y), new Point(tip, ll.Center.Y));
        g.DrawEllipse(Steel, Outline, new Point(armHome, ll.Center.Y), 16, 16);
        Text(g, s.ArmZ switch { LoadlockZ.Up => "Z 上", LoadlockZ.Down => "Z 下", LoadlockZ.Raising => "Z 上升中", LoadlockZ.Lowering => "Z 下降中", _ => "Z ?" },
             armHome, ll.Center.Y + 22, 12, Dim, align: 0.5);

        // ---------------- PM
        Octagon(g, pm, pmR, s.ChamberLidClosed || !conn ? Body : new SolidColorBrush(Color.Parse("#FFF3D6")), Outline);
        Text(g, "PM", pm.X - pmR + 22, pm.Y - pmR + 26, 16, Ink, bold: true);
        if (conn && !s.ChamberLidClosed) Text(g, "上蓋開", pm.X + pmR - 22, pm.Y - pmR + 26, 13, Moving, align: 1, bold: true);
        // Heater（chuck）：在 Process 位置畫實心，下位畫淡色
        var heaterBrush = s.HeaterAxis == AxisState.Error ? Alarm : s.HeaterAtProcess ? Steel : new SolidColorBrush(Color.Parse("#E3E8EE"));
        g.DrawEllipse(heaterBrush, Outline, pm, 82, 82);
        // PM 晶圓（Wafer lift 上 = 小一點的外框）
        Wafer(g, pm, 62, m.WaferPm, m.Processing && m.WaferPm == true, s.WaferLiftUp);
        if (m.WaferPm == true && m.PmWaferId.Length > 0) Text(g, m.PmWaferId, pm.X, pm.Y - 9, 14, Brushes.White, align: 0.5, bold: true);
        string pmP = !conn ? "---" : s.ChamberPressureTorr == 0 ? "< 1.0E-03 Torr" : $"{s.ChamberPressureTorr:0.00E+00} Torr";
        Text(g, pmP, pm.X + 28, pm.Y + pmR + 4, 15, Ink, bold: true);
        Text(g, $"Lift {(s.WaferLiftUp ? "上" : s.WaferLiftDown ? "下" : "中")}  Heater {(s.HeaterAtProcess ? "Process" : s.HeaterDown ? "下位" : "移動")}",
             pm.X, pm.Y + 88, 12, Dim, align: 0.5);
        if (s.AldRunning) Text(g, $"ALD cycle {s.AldCycle}", pm.X, pm.Y - pmR + 50, 14, WaferGreen, align: 0.5, bold: true);

        // 晶圓在 Loadlock 手臂上（手臂伸進 PM 時跟著到 PM 上方）
        if (m.WaferLl != false)
        {
            var c = new Point(Math.Max(tip, ll.X + 150), ll.Center.Y);
            Wafer(g, c, 52, m.WaferLl, false, false);
            if (m.WaferLl == true && m.LlWaferId.Length > 0) Text(g, m.LlWaferId, c.X, c.Y - 9, 13, Brushes.White, align: 0.5, bold: true);
        }

        // ---------------- Slit valve（畫在手臂上面）
        var svBrush = !s.LoadlockFresh ? Off : s.SlitValve switch
        {
            LoadlockSv.Open => SvOpen, LoadlockSv.Closed => Line, LoadlockSv.Error => Alarm, _ => Moving,
        };
        if (s.SlitValve == LoadlockSv.Open) g.DrawRectangle(null, new Pen(svBrush, 4), sv);
        else g.DrawRectangle(svBrush, null, sv);
        Text(g, "SV", sv.Center.X, sv.Bottom + 4, 13, Dim, align: 0.5);

        // ---------------- ALD 閥 V1~V7 與 MFC
        Text(g, "ALD 閥", pm.X - 160, 70, 13, Dim, align: 1);
        for (int i = 0; i < 7; i++)
        {
            double x = pm.X - 120 + i * 40;
            bool open = (s.AldValves >> i & 1) != 0;
            var r = new Rect(x - 14, 66, 28, 28);
            g.DrawRectangle(open ? On : Off, Outline, r, 4, 4);
            Text(g, $"V{i + 1}", x, 72, 12, open ? Brushes.White : Ink, align: 0.5, bold: true);
        }
        Text(g, "MFC", pm.X - 160, 12, 13, Dim, align: 1);
        for (int i = 0; i < 8; i++)
        {
            double x = pm.X - 135 + i * 38;
            bool up = (s.MfcUpstreamValves >> i & 1) != 0;
            double v = s.MfcValue.Length > i ? s.MfcValue[i] : 0;
            var r = new Rect(x - 17, 8, 34, 22);
            g.DrawRectangle(up && v > 0.05 ? On : up ? Moving : Off, Outline, r, 3, 3);
            Text(g, $"{i + 1}", x, 11, 12, up ? Brushes.White : Ink, align: 0.5, bold: true);
            if (v > 0.05) Text(g, $"{v:0.#}", x, 34, 11, Ink, align: 0.5);
        }

        // ---------------- Vent
        Valve(g, new Point(pm.X + 140, pm.Y - 140), false, Out(vo, VacuumOutputs.ChamberVent), blk.HasFlag(VacuumOutputs.ChamberVent));
        Text(g, "Vent", pm.X + 160, pm.Y - 150, 13, Dim);

        // ---------------- 抽氣：Turbo GV、Turbo、前級閥、粗抽閥、Dry pump
        var gvBrush = s.TurboGateOpened ? On : s.TurboGateClosed ? Off : Moving;
        Valve(g, new Point(pm.X, 410), true, gvBrush, blk.HasFlag(VacuumOutputs.TurboGv), 20);
        Text(g, "Turbo GV", pm.X - 28, 402, 13, Dim, align: 1);
        Pump(g, new Point(pm.X, 465), "Turbo", s.TurboAlarm ? Alarm : s.TurboAtSpeed ? On : vo.HasFlag(VacuumOutputs.TurboOn) ? Moving : Off);
        Text(g, s.TurboAtSpeed ? "全速" : vo.HasFlag(VacuumOutputs.TurboOn) ? "加速中" : "停", pm.X + 34, 458, 13, Dim);
        Valve(g, new Point(pm.X, 520), true, Out(vo, VacuumOutputs.TurboRough), blk.HasFlag(VacuumOutputs.TurboRough));
        Text(g, "前級閥", pm.X + 22, 512, 12, Dim);
        Valve(g, new Point(410, 505), true, Out(vo, VacuumOutputs.ChamberRough), blk.HasFlag(VacuumOutputs.ChamberRough));
        Text(g, "粗抽閥", 390, 497, 12, Dim, align: 1);
        Pump(g, new Point(220, yFore), "Dry", s.DryPumpAlarm ? Alarm : s.DryPumpRunning ? On : vo.HasFlag(VacuumOutputs.DryPumpOn) ? Moving : Off);
        Text(g, "Dry pump", 220, yFore + 30, 12, Dim, align: 0.5);
        Text(g, $"Foreline {(conn ? s.ForelineTorr.ToString("0.0E+00") : "---")} Torr", 300, yFore + 8, 12, Dim);

        if (!conn) Text(g, "主控板未連線", W / 2, H / 2 - 12, 26, Alarm, align: 0.5, bold: true);
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
