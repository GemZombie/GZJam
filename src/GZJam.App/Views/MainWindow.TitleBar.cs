using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Input;
using Avalonia.LogicalTree;

namespace GZJam.App.Views;

/// <summary>
/// GZJam's top bar is also the window's title bar on Windows and macOS (issue #32): one bar instead
/// of the system's plus GZJam's, giving the decks and library the difference. Its empty space moves
/// the window (a double-click maximises), its keys stay keys, and the system's own buttons sit at its
/// end. Linux keeps the system's title bar where there is one; tiling desktops like Hyprland have none.
/// </summary>
public partial class MainWindow
{
    /// <summary>macOS's close, minimise and zoom buttons, on the left.</summary>
    private const double MacTrafficLights = 80;

    /// <summary>Whether the top bar is the window's title bar here.</summary>
    public static bool TopBarIsTitleBar => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();

    private void UseTopBarAsTitleBar()
    {
        if (!TopBarIsTitleBar)
            return;

        ExtendClientAreaToDecorationsHint = true;
        TopBar.SizeChanged += (_, e) => ExtendClientAreaTitleBarHeightHint = e.NewSize.Height;   // follows touch mode
        if (OperatingSystem.IsMacOS())
        {
            TopBarContent.Margin = new Thickness(MacTrafficLights, 0, 16, 0);   // macOS draws its own buttons
        }
        else
        {
            // Avalonia would draw Windows' title bar itself, the window's title included, over the
            // left of the bar: GZJam draws the three buttons instead, and Windows is told which is which.
            WindowDecorations = WindowDecorations.BorderOnly;
            CaptionButtons.IsVisible = true;
            TopBarContent.Margin = new Thickness(20, 0, 12, 0);
            ShowMaximizeOrRestore();
        }

        // The bar moves the window; anything in it you can press stays pressable.
        WindowDecorationProperties.SetElementRole(TopBar, WindowDecorationsElementRole.TitleBar);
        foreach (Button button in TopBar.GetLogicalDescendants().OfType<Button>())   // toggle buttons too
            WindowDecorationProperties.SetElementRole(button, WindowDecorationsElementRole.User);

        // Maximised on Windows, the window's edge sits off screen: keep everything on it.
        PropertyChanged += (_, e) =>
        {
            if (e.Property == OffScreenMarginProperty)
                WindowRoot.Margin = OffScreenMargin;
            else if (e.Property == WindowStateProperty)
                ShowMaximizeOrRestore();
        };

        // Set last: the bar's buttons are pressable, these three are Windows' own. Windows then
        // minimises, maximises (with snap layouts) and closes by itself, by mouse, touch or pen.
        WindowDecorationProperties.SetElementRole(MinimizeButton, WindowDecorationsElementRole.MinimizeButton);
        WindowDecorationProperties.SetElementRole(MaximizeButton, WindowDecorationsElementRole.MaximizeButton);
        WindowDecorationProperties.SetElementRole(CloseButton, WindowDecorationsElementRole.CloseButton);
    }

    /// <summary>The middle button shows maximise, or restore when the window is maximised.</summary>
    private void ShowMaximizeOrRestore()
    {
        bool maximized = WindowState == WindowState.Maximized;
        MaximizeButton.Content = maximized ? "\uE923" : "\uE922";
        Avalonia.Automation.AutomationProperties.SetName(MaximizeButton, maximized ? "Restore" : "Maximise");
    }
}
