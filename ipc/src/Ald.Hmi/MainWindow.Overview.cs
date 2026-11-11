using System.Text;
using Ald.Core;
using Avalonia.Controls;
using Avalonia.Media;

namespace Ald.Hmi;

/// <summary>
/// 總覽（俯視圖 + 兩側資訊卡）。頂部列在 MainWindow.Header.cs。
/// </summary>
public partial class MainWindow
{
    private DateTime? _busySince;

    private string RunningText(out string step)
    {
        step = "";
        if (BatchRunning) { step = _batch!.Step; return $"批次（第 {_batch.Current + 1} 片）"; }
        if (SequenceRunning) { step = RunStep; return _runName; }
        if (XferRunning) { step = _xfer!.Step; return "傳片"; }
        if (LeakRunning) { step = _leak!.Step; return "漏率測試"; }
        return "";
    }

    private void RenderOverview(MachineSnapshot s)
    {
        var m = _m!;
        string run = RunningText(out var step);
        C<TextBlock>("TxtState").Text = !s.Connected ? "未連線" : s.SafeState ? "安全狀態（需 Reset）"
            : run.Length > 0 ? $"執行中：{run}" : $"待機（{s.SysState}）";
        RenderHeader(s);

        // 經過時間
        if (Busy) _busySince ??= DateTime.Now; else _busySince = null;
        string llId = _wafers?.Loadlock == true ? _wafers.LoadlockId : "", pmId = _wafers?.Pm == true ? _wafers.PmId : "";
        string elapsed = _busySince is { } t0 ? (DateTime.Now - t0).ToString(@"hh\:mm\:ss") : "";

        // 作業卡
        C<TextBlock>("TxtRunNow").Text = run.Length == 0
            ? (_status.Length > 0 ? $"待機\n最後：{_status}" : "待機")
            : $"{run}\n{step}\n經過 {elapsed}" + (BatchWaiting ? $"\n⚠ {_batch!.Prompt}" : "");

        // PM 卡
        var pm = new StringBuilder();
        pm.AppendLine($"晶圓   {WaferMap.Text(_wafers?.Pm)}{(pmId.Length > 0 ? " " + pmId : "")}");
        pm.AppendLine($"流程   {(run.Length > 0 ? run : "-")}");
        if (step.Length > 0) pm.AppendLine($"步驟   {step}");
        int cycles = m.LastAld?.Cycles ?? 0;
        pm.AppendLine($"ALD    {(s.AldRunning ? $"執行中 {s.AldCycle}/{(cycles > 0 ? cycles : "?")}" : "停")}");
        if (elapsed.Length > 0) pm.AppendLine($"經過   {elapsed}");
        pm.AppendLine($"Heater {s.HeaterPosSteps} step {(s.HeaterAtProcess ? "Process" : s.HeaterDown ? "下位" : "")} ({s.HeaterAxis})");
        pm.Append($"Lift   {(s.WaferLiftUp ? "上" : s.WaferLiftDown ? "下" : "動作中")}  上蓋 {(s.ChamberLidClosed ? "關" : "開")}");
        C<TextBlock>("TxtMech").Text = pm.ToString();

        var v = new StringBuilder();
        v.AppendLine($"PM 互鎖  {PmP(s)}");
        v.AppendLine($"Barocel  {P(s.ManometerTorr)}");
        v.AppendLine($"Ion      {P(s.IonGaugeTorr)}");
        v.AppendLine($"Foreline {P(s.ForelineTorr)}");
        v.Append("（Torr）");
        if (s.VacuumBlocked != VacuumOutputs.None) v.Append($"\n⚠ 互鎖擋下 {s.VacuumBlocked}");
        C<TextBlock>("TxtVac").Text = v.ToString();

        var l = new StringBuilder();
        string llp = s.LoadlockPressureUnderRange ? "< 1 Torr" : double.IsNaN(s.LoadlockPressureTorr) ? "---" : $"{s.LoadlockPressureTorr:0.0} Torr";
        l.AppendLine($"晶圓 {WaferMap.Text(_wafers?.Loadlock)}{(llId.Length > 0 ? " " + llId : "")}");
        l.AppendLine($"壓力 {llp}{(s.LoadlockPressureSuspect ? "（不可信）" : "")}");
        l.AppendLine($"SV {SvText[(int)s.SlitValve % 6]}  Z {s.ArmZ}");
        l.Append($"手臂 {s.ArmPosSteps} step{(s.LoadlockAlarm != 0 ? $"\n⚠ 警報 {s.LoadlockAlarm}" : "")}");
        C<TextBlock>("TxtLlDetail").Text = l.ToString();

        // 加熱：只列有開的組（常駐設定），沒開的組不佔版面
        var h = new StringBuilder();
        var names = m.Config.Heaters.Channels;
        int off = 0;
        for (int i = 0; i < 16; i++)
        {
            var sp = m.HeaterSetpoint(i);
            if (sp is null) { off++; continue; }
            string t = double.IsNaN(s.TempC[i]) ? " 斷線" : $"{s.TempC[i],5:0.0}";
            string n = names[i].Name.Length > 12 ? names[i].Name[..12] : names[i].Name;
            h.AppendLine($"{n,-12} {t}/{sp,3:0} {s.HeaterDutyPct[i],3:0}%");
        }
        h.Append(off == 16 ? "全部未加熱" : $"（未開 {off} 組；實際/設定 °C 輸出）");
        C<TextBlock>("TxtHeat").Text = h.ToString();

        var g = new StringBuilder();
        var mfc = m.Config.Mfc.Channels;
        for (int i = 0; i < 8; i++)
        {
            string n = mfc[i].Name.Length > 10 ? mfc[i].Name[..10] : mfc[i].Name;
            g.AppendLine($"{i + 1} {n,-10}{s.MfcValue[i],7:0.0}{((s.MfcUpstreamValves >> i & 1) != 0 ? " ●" : "")}");
        }
        g.Append("（● = 上游閥開）");
        C<TextBlock>("TxtMfc").Text = g.ToString();

        C<OverviewView>("Ov").Update(new OverviewModel(s, _wafers?.Loadlock, _wafers?.Pm,
            llId, pmId,
            s.AldRunning || s.SysState == SystemState.Process, m.Config.Transfer.ArmPmSteps,
            mfc.Select(c => c.Name).ToArray()));
    }
}
