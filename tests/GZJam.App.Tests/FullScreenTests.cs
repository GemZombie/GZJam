using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using GZJam.App.Views;

namespace GZJam.App.Tests;

/// <summary>F11 for full screen, and Ctrl+Cmd+F on a Mac (#155).</summary>
public sealed class FullScreenTests
{
    private static void Press(AppHarness app, Key key, RawInputModifiers modifiers = RawInputModifiers.None, PhysicalKey physical = PhysicalKey.F11) =>
        app.Window.KeyPress(key, modifiers, physical, null);

    private static Button ExitKey(AppHarness app) => app.Window.FindControl<Button>("ExitFullScreenButton")!;

    [AvaloniaFact]
    public void F11_goes_full_screen_and_back_with_a_key_to_leave_in_the_top_bar()
    {
        using var app = new AppHarness();
        MainWindow window = app.Window;
        window.WindowState = WindowState.Maximized;
        var captions = window.FindControl<StackPanel>("CaptionButtons")!;
        bool hadCaptions = captions.IsVisible;
        Assert.False(ExitKey(app).IsVisible);

        Press(app, Key.F11);

        Assert.Equal(WindowState.FullScreen, window.WindowState);
        Assert.True(ExitKey(app).IsVisible);
        Assert.False(captions.IsVisible);   // Windows: the exit key in place of the window's own buttons

        Press(app, Key.F11);

        Assert.Equal(WindowState.Maximized, window.WindowState);   // back to what it was
        Assert.False(ExitKey(app).IsVisible);
        Assert.Equal(hadCaptions, captions.IsVisible);
    }

    [AvaloniaFact]
    public void F11_works_from_the_search_box_and_Esc_does_not_leave()
    {
        using var app = new AppHarness();
        app.Window.WindowState = WindowState.Normal;
        app.Window.FindControl<TextBox>("SearchBox")!.Focus();

        Press(app, Key.F11);
        Assert.True(app.Window.IsFullScreen);

        Press(app, Key.Escape, physical: PhysicalKey.Escape);
        Assert.True(app.Window.IsFullScreen);

        ExitKey(app).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(WindowState.Normal, app.Window.WindowState);
    }

    [AvaloniaFact]
    public void Full_screen_is_remembered_as_maximized()
    {
        using var app = new AppHarness();
        app.Window.WindowState = WindowState.Normal;
        Press(app, Key.F11);

        Assert.True(app.Window.CapturePlacement().Maximized);
    }

    [AvaloniaFact]
    public void On_a_Mac_Ctrl_Cmd_F_too()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "Ctrl+Cmd+F is the Mac's full-screen key.");
        using var app = new AppHarness();
        Press(app, Key.F, RawInputModifiers.Control | RawInputModifiers.Meta, PhysicalKey.F);
        Assert.True(app.Window.IsFullScreen);
    }
}
