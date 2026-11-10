using System.Windows;

namespace Ald.Hmi;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        // TODO 讀 config/machine.json → 建立 MainBoardLink（Modbus TCP 連主控板）→ Start
        //      Updated 事件 → 組成 MachineSnapshot → 更新畫面（Dispatcher.Invoke）
        //      建立 LoadlockLink（RS-485）+ LoadlockService；Loadlock 按鈕一律走 LoadlockService.Execute（內含互鎖），被擋就顯示原因
    }
}
