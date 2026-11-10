using System.Windows;

namespace Ald.Hmi;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        // TODO 讀 config/machine.json → 建立 MainBoardLink（Modbus TCP 連主控板）→ Start
        //      Updated 事件 → 組成 MachineSnapshot → 更新畫面（Dispatcher.Invoke）
        //      Loadlock 按鈕：先 Interlocks.Check 顯示原因 → SendLoadlockCommand → 看 LL_CMD_RESULT / IL_DENY
    }
}
