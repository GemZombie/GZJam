using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace GZJam.App.Views;

/// <summary>
/// Full screen (#155): F11 on and off, and on a Mac Ctrl+Cmd+F too (and its green button, as
/// macOS does by itself). The top bar stays, with a key to leave full screen in place of the
/// window's own buttons, for a touch screen with no keyboard. Esc doesn't leave it: Esc already
/// does things in GZJam. Next time GZJam opens it's maximized, not full screen.
/// </summary>
public partial class MainWindow
{
    /// <summary>What the window was before full screen, to go back to.</summary>
    private WindowState _beforeFullScreen = WindowState.Maximized;

    public bool IsFullScreen => WindowState == WindowState.FullScreen;

    public void ToggleFullScreen()
    {
        if (IsFullScreen)
        {
            WindowState = _beforeFullScreen;
            Log.Info("Full screen off");
        }
        else
        {
            _beforeFullScreen = WindowState is WindowState.Maximized or WindowState.Normal ? WindowState : WindowState.Maximized;
            WindowState = WindowState.FullScreen;
            Log.Info("Full screen on");
        }
    }

    /// <summary>F11, or Ctrl+Cmd+F on a Mac: from anywhere, the search box too, as they type nothing.</summary>
    private void OnFullScreenKeyDown(object? sender, KeyEventArgs e)
    {
        bool f11 = e.Key == Key.F11 && e.KeyModifiers == KeyModifiers.None;
        bool macKeys = OperatingSystem.IsMacOS() && e.Key == Key.F && e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Meta);
        if (!f11 && !macKeys)
            return;
        ToggleFullScreen();
        e.Handled = true;
    }

    private void OnExitFullScreenClick(object? sender, RoutedEventArgs e)
    {
        if (IsFullScreen)
            ToggleFullScreen();
    }

    /// <summary>
    /// Full screen came or went (by key, by the exit key, or by macOS's own button): the exit key
    /// shows instead of the window's buttons, and the bar no longer moves a window that fills the screen.
    /// </summary>
    private void ShowFullScreen()
    {
        bool full = IsFullScreen;
        ExitFullScreenButton.IsVisible = full;
        if (!TopBarIsTitleBar)
            return;
        WindowDecorationProperties.SetElementRole(TopBar, full ? WindowDecorationsElementRole.None : WindowDecorationsElementRole.TitleBar);
        if (OperatingSystem.IsMacOS())
            TopBarContent.Margin = new Thickness(full ? 20 : MacTrafficLights, 0, 16, 0);   // macOS hides its buttons in full screen
        else
            CaptionButtons.IsVisible = !full;
    }
}
