using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using GZJam.App.Controls;

namespace GZJam.App.Views;

/// <summary>
/// The startup screen (issue #49): the logo, on the main window's background and exactly where the
/// main window will open, shown while GZJam gets ready. The main window opens underneath, and when
/// it's drawn this closes, so to a DJ it's the one window, its decks appearing. Built in code, not
/// XAML, so there's as little as possible to do before it's on screen.
/// </summary>
public sealed class StartupWindow : Window
{
    private readonly StartupLogo _logo = new();
    private bool _done;

    /// <summary>
    /// Windows and macOS. On Linux, a tiling desktop (Hyprland, for one) would give this a tile of
    /// its own beside the main window, so Linux starts as before until that can be tried there.
    /// </summary>
    public static bool IsShown => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();

    public StartupWindow(AppSettings.WindowPlacement? placement)
    {
        Title = "GZJam";
        WindowDecorations = WindowDecorations.None;
        ShowInTaskbar = false;
        Topmost = true;   // the main window opens underneath, out of sight until it's drawn
        CanResize = false;

        // As the main window: its size, and where it opens (MainWindow.axaml).
        Width = 1440;
        Height = 900;
        MinWidth = 1024;
        MinHeight = 680;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        MainWindow.RestorePlacement(this, placement);

        // Alt+F4 here would close GZJam's only window, halfway through starting: only startup closes it.
        Closing += (_, e) => e.Cancel = !_done;

        var app = Application.Current!;
        Background = (IBrush)app.FindResource("BgBrush")!;
        Content = new Grid
        {
            Children =
            {
                _logo,
                new TextBlock
                {
                    Text = About.Version,
                    FontFamily = (FontFamily)app.FindResource("MonoFont")!,
                    FontSize = 12,
                    Foreground = (IBrush)app.FindResource("DimBrush")!,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, StartupLogoHandler.MarkSize + 56, 0, 0),
                },
            },
        };
    }

    /// <summary>A step of getting ready begins, covering <paramref name="from"/> to <paramref name="to"/> of the sweep.</summary>
    public void BeginStep(double from, double to) => _logo.BeginStep(from, to);

    /// <summary>Everything is ready: the sweep runs to the end.</summary>
    public void Finish() => _logo.Finish();

    /// <summary>Closes the startup screen: the main window is ready (or starting failed).</summary>
    public void Dismiss()
    {
        _done = true;
        Close();
    }
}
