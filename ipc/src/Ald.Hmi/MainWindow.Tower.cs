using Ald.Core;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;

namespace Ald.Hmi;

/// <summary>
/// 燈塔 / 蜂鳴器：每次畫面更新依 Ald.Core.Tower 規則算燈號，有變化才寫主控板 HR DO_CMD；
/// 頂部小圓點顯示主控板回報的實際輸出（IR DO_OUT）。
/// 「確認 / 消音」任何人都能按（同安全停止，不鎖），會記在事件記錄。
/// </summary>
public partial class MainWindow
{
    private volatile bool _seqFault;   // 流程異常停止，還沒人確認 → 紅燈閃 + 蜂鳴
    private volatile bool _needOp;     // 流程做完等人員（取片） → 藍燈閃

    private static readonly IBrush Off = new SolidColorBrush(Color.Parse("#3A4654"));
    private static readonly (string Name, TowerLamp Bit, IBrush On)[] Lamps =
    {
        ("LmpRed", TowerLamp.Red, Brushes.Red), ("LmpYellow", TowerLamp.Yellow, Brushes.Gold),
        ("LmpGreen", TowerLamp.Green, Brushes.LimeGreen), ("LmpBlue", TowerLamp.Blue, Brushes.DeepSkyBlue),
    };

    private void WireTower()
    {
        C<Button>("BtnAck").Click += (_, _) =>
        {
            int n = _m?.Alarms.Acknowledge() ?? 0;
            if (_seqFault) n++;
            if (_needOp) n++;
            _seqFault = false;
            _needOp = false;
            _status = n > 0 ? $"已確認 {n} 筆、蜂鳴器消音" : "沒有未確認的警報";
            if (n > 0) _alarmLog?.Add(OpSource, "Info", $"警報確認 / 消音（{n} 筆）", evt: "OPERATOR");
        };
    }

    /// <summary>流程事件 → 燈號狀態：執行中失敗 = 異常待確認；（取片類）完成 = 需要人員。</summary>
    private void TowerEvent(SequencerProgress p, bool doneNeedsOperator)
    {
        // 開始前檢查沒過（Step「檢查…」）只是不能開始，不算機台異常
        if (p.State == SequencerState.Failed && !p.Step.StartsWith("檢查")) _seqFault = true;
        else if (p.State == SequencerState.Done && doneNeedsOperator) _needOp = true;
    }

    private void RenderTower(MachineSnapshot s)
    {
        if (_m is null) return;
        var a = _m.Alarms;
        bool waferUnknown = _wafers is not null && (_wafers.Loadlock is null || _wafers.Pm is null);
        var input = new TowerInputs(
            Safe: s.SafeState || !s.Connected,
            AnyAlarm: a.AnyAlarm,
            UnackedAlarm: a.UnackedAlarm || _seqFault,
            AnyWarning: a.AnyWarning,
            UnackedWarning: a.UnackedWarning,
            Running: Busy && !BatchWaiting,
            NeedOperator: _needOp || waferUnknown || BatchWaiting || HeaterMismatch);
        var cfg = _m.Config.Tower;
        try { _m.SetTower(Tower.Compute(input, cfg, Tower.BlinkOn(DateTime.Now, cfg))); }
        catch (Exception) { /* 斷線時寫不進去；主控板會自己只亮紅燈 */ }

        var fb = s.Connected ? s.Tower : TowerLamp.None;
        foreach (var (name, bit, on) in Lamps) C<Ellipse>(name).Fill = fb.HasFlag(bit) ? on : Off;
        C<Button>("BtnAck").Content = fb.HasFlag(TowerLamp.Buzzer) ? "🔔" : "🔕";
        C<Button>("BtnAck").Background = input.UnackedAlarm || input.UnackedWarning || _needOp
            ? (Tower.BlinkOn(DateTime.Now, cfg) ? Brushes.Orange : Brushes.LightGray) : new SolidColorBrush(Color.Parse("#E0E0E0"));
    }
}
