using Ald.Core;
using Avalonia.Controls;

namespace Ald.Hmi;

/// <summary>
/// 加熱常駐設定的比對 / 恢復（HeaterPreset）：
/// 連上主控板後，加熱板設定跟常駐設定不一樣（斷電復電、ald-ecat 重開會把設定清成 0）→ 頂部黃條 + 藍燈。
/// 不自動恢復（復電不自動重新加熱）：操作員以上按「恢復常駐加熱設定」才寫回；
/// 工程師可按「以目前為準」把常駐設定改成加熱板現在的樣子。兩個都寫事件記錄。
/// </summary>
public partial class MainWindow
{
    private HeaterPreset? _heaters;
    private IReadOnlyList<HeaterMismatch> _heatDiff = Array.Empty<HeaterMismatch>();
    private bool HeaterMismatch => _heatDiff.Count > 0;

    private void WireHeater()
    {
        if (_m is null) return;
        _heaters = HeaterPreset.Load(_m.Config.Heaters.PresetPath);
        if (_heaters.Note.Length > 0 && !_heaters.Defined) _alarmLog?.Add("加熱", "Warning", _heaters.Note, evt: "EVENT");

        C<Button>("BtnHeatRestore").Click += (_, _) => Cmd("恢復常駐加熱設定（" + DiffText() + "）", () =>
        {
            int n = _heaters.Restore(_m, _m.HeaterSetpoint);
            if (n == 0) throw new InvalidOperationException("已經一致，不用恢復");
        });
        C<Button>("BtnHeatAccept").Click += (_, _) => Cmd("加熱常駐設定改為目前加熱板設定（" + DiffText() + "）",
            () => _heaters.TakeCurrent(_m.HeaterSetpoint, $"{_user} 以目前為準"));
    }

    private string DiffText() => string.Join("；", _heatDiff.Select(d => HeaterPreset.Text(d, _m!.Config.Heaters.Channels.Select(c => c.Name).ToList())));

    private void RenderHeater(MachineSnapshot s)
    {
        if (_m is null || _heaters is null) return;
        bool check = s.Connected && s.SlotOnline.Length > 2 && s.SlotOnline[2] && _m.HeaterSetpointsKnown;
        var diff = check ? _heaters.Compare(_m.HeaterSetpoint) : Array.Empty<HeaterMismatch>();
        if (diff.Count > 0 && _heatDiff.Count == 0)
            _alarmLog?.Add("加熱", "Warning", "加熱板設定跟常駐設定不一致（可能斷電或主站重開）：" +
                string.Join("；", diff.Select(d => HeaterPreset.Text(d, _m.Config.Heaters.Channels.Select(c => c.Name).ToList()))), evt: "EVENT");
        _heatDiff = diff;

        C<Border>("BarHeater").IsVisible = diff.Count > 0;
        if (diff.Count == 0) return;
        C<TextBlock>("TxtHeaterBar").Text = "⚠ 加熱設定跟常駐設定不一樣（可能斷電重開過），確認機台狀況後按「恢復」才會重新加熱：\n" + DiffText();
        C<Button>("BtnHeatRestore").IsEnabled = Can(Permission.RunRecipe) && !Busy;
        C<Button>("BtnHeatAccept").IsEnabled = Can(Permission.ManualControl) && !Busy;
    }
}
