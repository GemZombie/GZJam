using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using GZJam.App.Views;

namespace GZJam.App.Tests;

/// <summary>The top bar is the window's title bar on Windows and macOS (issue #32).</summary>
public sealed class TitleBarTests
{
    private static WindowDecorationsElementRole Role(Control control) => WindowDecorationProperties.GetElementRole(control);

    [AvaloniaFact]
    public void The_bar_moves_the_window_and_its_keys_stay_keys()
    {
        Assert.SkipUnless(MainWindow.TopBarIsTitleBar, "Linux keeps the system's title bar.");
        using var app = new AppHarness();
        var bar = app.Window.FindControl<Border>("TopBar")!;

        Assert.True(app.Window.ExtendClientAreaToDecorationsHint);
        Assert.Equal(WindowDecorationsElementRole.TitleBar, Role(bar));
        foreach (Button key in bar.GetLogicalDescendants().OfType<Button>().Where(b => !b.Classes.Contains("caption")))
            Assert.Equal(WindowDecorationsElementRole.User, Role(key));
    }

    [AvaloniaFact]
    public void On_windows_gzjam_draws_minimise_maximise_and_close_and_windows_knows_them()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only: macOS draws its own.");
        using var app = new AppHarness();
        Assert.Equal(WindowDecorations.BorderOnly, app.Window.WindowDecorations);   // no title drawn over the bar
        Assert.True(app.Window.FindControl<StackPanel>("CaptionButtons")!.IsVisible);
        Assert.Equal(WindowDecorationsElementRole.MinimizeButton, Role(app.Window.FindControl<Button>("MinimizeButton")!));
        Assert.Equal(WindowDecorationsElementRole.MaximizeButton, Role(app.Window.FindControl<Button>("MaximizeButton")!));
        Assert.Equal(WindowDecorationsElementRole.CloseButton, Role(app.Window.FindControl<Button>("CloseButton")!));

        var maximize = app.Window.FindControl<Button>("MaximizeButton")!;
        Assert.Equal("", maximize.Content);
        app.Window.WindowState = WindowState.Maximized;
        Assert.Equal("", maximize.Content);   // restore
        app.Window.WindowState = WindowState.Normal;
        Assert.Equal("", maximize.Content);
    }

    [AvaloniaFact]
    public void Touch_mode_makes_the_bar_taller()
    {
        using var app = new AppHarness();
        var bar = app.Window.FindControl<Border>("TopBar")!;
        AppHarness.Settle(50);
        double normal = bar.Bounds.Height;
        app.ViewModel.Settings.TouchMode = true;
        AppHarness.Settle(100);
        Assert.True(bar.Bounds.Height > normal, $"{bar.Bounds.Height} should be taller than {normal}");
        app.ViewModel.Settings.TouchMode = false;
    }
}
