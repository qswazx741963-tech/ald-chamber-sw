using Ald.Comm;
using Ald.Core;
using Ald.Data;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace Ald.Hmi;

/// <summary>
/// 啟動：讀 config/machine.json → MachineService（連本機 ald-ecat）→ 警報 / 趨勢 / 保養計數寫進 SQLite → 主畫面。
/// 參數：--config 路徑（預設程式旁 config/machine.json）、--port（覆寫 ald-ecat 埠，例如模擬用 1502）、
/// --recipes 配方資料夾（預設程式旁 recipes/）
/// </summary>
public partial class App : Application
{
    private MachineService? _machine;
    private AlarmLog? _alarmLog;
    private TrendLog? _trend;
    private UserStore? _users;
    private MaintenanceStore? _maint;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var args = desktop.Args ?? Array.Empty<string>();
            string Arg(string name, string def) { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : def; }

            var cfg = MachineConfig.Load(Arg("--config", Path.Combine(AppContext.BaseDirectory, "config", "machine.json")));
            if (int.TryParse(Arg("--port", ""), out var port)) cfg = cfg with { MainBoard = cfg.MainBoard with { Port = port } };

            _alarmLog = new AlarmLog(cfg.DataLog.Path);
            _trend = new TrendLog(cfg.DataLog.Path, TimeSpan.FromMilliseconds(cfg.DataLog.IntervalMs));
            _users = new UserStore(cfg.DataLog.Path, cfg.Hmi.DefaultAdminPasswordHash);
            _alarmLog.Purge(cfg.DataLog.KeepDays);
            _trend.Purge(cfg.DataLog.KeepDays);

            _machine = new MachineService(cfg);
            _machine.AlarmChanged += c => _alarmLog.Add(c);
            _machine.Updated += s => _trend.Add(s);
            _maint = new MaintenanceStore(cfg.DataLog.Path);
            _maint.BecameDue += r => _alarmLog.Add("保養", "Warning",
                $"{r.Item.Name} 已達保養上限：{Maintenance.Format(r.Value, r.Item.Unit)} / {Maintenance.Format(r.Limit, r.Item.Unit)}", evt: "EVENT");
            _machine.Updated += s => _maint.Track(s, _machine.LastAld);
            _machine.Start();

            var recipes = Arg("--recipes", Path.Combine(AppContext.BaseDirectory, "recipes"));
            desktop.MainWindow = new MainWindow(_machine, _alarmLog, _trend, _users, _maint, recipes);
            desktop.Exit += (_, _) =>
            {
                _machine.Dispose();   // 心跳停止 → 主控板看門狗 → 全機安全狀態
                _trend.Dispose();
                _maint.Dispose();
                _alarmLog.Dispose();
                _users.Dispose();
            };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
