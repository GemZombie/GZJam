using Avalonia.Input;
using Avalonia.Interactivity;
using GZJam.Library;

namespace GZJam.App.Views;

/// <summary>
/// The Downloads window (#304): a click on the top bar's progress while it shows songs coming down
/// from a server, or a server's right-click menu › Downloads…. One at a time: asked again, it comes
/// to the front.
/// </summary>
public partial class MainWindow
{
    private DownloadsWindow? _downloads;

    /// <summary>The Downloads window open now, if it is.</summary>
    internal DownloadsWindow? Downloads => _downloads;

    internal DownloadsWindow ShowDownloads()
    {
        if (_downloads is { } open)
        {
            open.Activate();
            return open;
        }
        var window = new DownloadsWindow(Vm.Servers.Downloads);
        window.Closed += (_, _) =>
        {
            if (_downloads == window)
                _downloads = null;
        };
        _downloads = window;
        window.Show(this);
        Log.Info("Downloads window opened");
        return window;
    }

    private void OnBackgroundStatusPressed(object? sender, PointerPressedEventArgs e)
    {
        if (Vm.BackgroundIsDownloads && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            ShowDownloads();
    }

    private void OnDownloadsClick(object? sender, RoutedEventArgs e) => ShowDownloads();
}
