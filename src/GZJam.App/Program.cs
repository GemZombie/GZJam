using Avalonia;

namespace GZJam.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // One GZJam at a time on a library (#336): a second one hands its songs to the first, and stops.
        if (!SingleInstance.Claim(AppPaths.DataDirectory, args))
            return;
        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            SingleInstance.Release();
        }
    }

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
