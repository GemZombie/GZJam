using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using GZJam.App.Views;

namespace GZJam.App.Tests;

/// <summary>MAIN and PHONES volume from the keyboard: Ctrl+↑/↓, with Shift for PHONES (#132).</summary>
public sealed class VolumeKeyTests
{
    private static readonly RawInputModifiers Command = OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control;

    private static void Press(AppHarness app, Key key, RawInputModifiers modifiers) =>
        app.Window.KeyPress(key, modifiers, key == Key.Up ? PhysicalKey.ArrowUp : PhysicalKey.ArrowDown, null);

    [AvaloniaFact]
    public void Ctrl_arrows_turn_MAIN_and_with_Shift_PHONES_a_little_at_a_time()
    {
        using var app = new AppHarness();
        var mixer = app.ViewModel.Mixer!;
        app.Window.FindControl<DataGrid>("TrackGrid")!.Focus();   // the lists would take Ctrl+arrows themselves

        Press(app, Key.Down, Command);
        Press(app, Key.Down, Command);
        Assert.Equal(100 - 2 * MainWindow.VolumeKeyStep, mixer.MainVolume);
        Assert.Equal(100, mixer.CueVolume);

        Press(app, Key.Down, Command | RawInputModifiers.Shift);
        Assert.Equal(100 - MainWindow.VolumeKeyStep, mixer.CueVolume);
        Assert.Equal(100 - 2 * MainWindow.VolumeKeyStep, mixer.MainVolume);

        Press(app, Key.Up, Command);
        Press(app, Key.Up, Command);
        Press(app, Key.Up, Command);   // no further than the top
        Assert.Equal(100, mixer.MainVolume);
        Assert.Equal(100, app.Settings.MainVolume);   // kept, as the slider's are
    }

    [AvaloniaFact]
    public void They_work_from_the_search_box_and_plain_arrows_leave_the_volume_alone()
    {
        using var app = new AppHarness();
        var mixer = app.ViewModel.Mixer!;
        var search = app.Window.FindControl<TextBox>("SearchBox")!;
        search.Focus();
        search.Text = "swing";

        Press(app, Key.Down, Command);
        Assert.Equal(100 - MainWindow.VolumeKeyStep, mixer.MainVolume);
        Assert.Equal("swing", search.Text);

        Press(app, Key.Down, RawInputModifiers.None);   // into the results, as before
        Press(app, Key.Up, RawInputModifiers.Shift);
        Assert.Equal(100 - MainWindow.VolumeKeyStep, mixer.MainVolume);
        Assert.Equal(100, mixer.CueVolume);
    }
}
