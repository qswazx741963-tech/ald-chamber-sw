using Avalonia;

namespace Ald.Hmi;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // AldChamber --hash-password 密碼 → 印出 config hmi.defaultAdminPasswordHash 用的字串（不開畫面）
        int i = Array.IndexOf(args, "--hash-password");
        if (i >= 0)
        {
            if (i + 1 >= args.Length) { Console.Error.WriteLine("用法：AldChamber --hash-password 密碼"); Environment.Exit(2); }
            Console.WriteLine(Ald.Data.UserStore.EncodeHash(args[i + 1]));
            return;
        }
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}
