using Avalonia;

namespace GZJam.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    // Also used by the visual designer.
    public static AppBuilder BuildAvaloniaApp()
    {
        AppBuilder builder = AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
        AvaloniaLog.QuietInputMethods();   // Fcitx or IBus not answering on Linux: a note, not the console (issue #21)
        return builder;
    }
}
