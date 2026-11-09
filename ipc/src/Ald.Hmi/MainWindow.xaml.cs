using System.Windows;

namespace Ald.Hmi;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        // TODO 讀 config/machine.json → 建立 ModbusBus（COM1：ID 1；COM2：ID 2/3/4）→ Start
        //      Updated 事件 → 組成 MachineSnapshot → 更新畫面（Dispatcher.Invoke）
    }
}
