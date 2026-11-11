using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Ald.Hmi;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);
        // TODO 讀 config/machine.json → 建立 MainBoardLink（連本機 ald-ecat 主站程式 127.0.0.1:502）→ Start
        //      Updated 事件 → 組成 MachineSnapshot → 更新畫面（Dispatcher.UIThread.Post）
        //      建立 LoadlockService（命令經主控板轉給 Loadlock，RS-485 接在主控板上）；
        //      Loadlock 按鈕一律走 LoadlockService.Execute（預檢互鎖），被擋就顯示原因；結果用 WaitAsync
    }
}
