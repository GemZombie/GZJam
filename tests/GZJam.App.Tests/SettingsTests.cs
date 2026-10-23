using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using GZJam.App.Views;

namespace GZJam.App.Tests;

/// <summary>Settings: over the dimmed main screen, sections down the side.</summary>
public sealed class SettingsTests
{
    private static SettingsPanel Panel(AppHarness app) => app.Window.FindControl<SettingsPanel>("SettingsPanel")!;

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [AvaloniaFact]
    public void The_gear_opens_settings_over_the_screen_and_DONE_closes_it()
    {
        using var app = new AppHarness();
        Assert.False(app.Window.IsSettingsOpen);

        Click(app.Window.FindControl<Button>("SettingsButton")!);
        AppHarness.Settle(100);
        Assert.True(app.Window.IsSettingsOpen);
        Assert.Equal(SettingsSection.Sound, Panel(app).Section);
        Assert.True(Panel(app).FindControl<Button>("DoneButton")!.IsFocused);   // keys stay out of the decks

        Click(Panel(app).FindControl<Button>("DoneButton")!);
        Assert.False(app.Window.IsSettingsOpen);
    }

    [AvaloniaFact]
    public void Escape_or_a_tap_on_the_dimmed_screen_closes_it()
    {
        using var app = new AppHarness();
        app.Window.OpenSettings();
        app.Window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Assert.False(app.Window.IsSettingsOpen);

        app.Window.OpenSettings();
        AppHarness.Settle(100);
        app.Window.MouseDown(new Point(6, 6), MouseButton.Left, RawInputModifiers.None);   // a corner, outside the panel
        app.Window.MouseUp(new Point(6, 6), MouseButton.Left, RawInputModifiers.None);
        Assert.False(app.Window.IsSettingsOpen);
    }

    [AvaloniaFact]
    public void It_reopens_where_it_was_left_and_the_output_chips_open_it_on_Sound()
    {
        using var app = new AppHarness();
        app.Window.OpenSettings(SettingsSection.Decks);
        Assert.True(Panel(app).FindControl<Control>("DecksPage")!.IsVisible);
        Assert.False(Panel(app).FindControl<Control>("SoundPage")!.IsVisible);
        app.Window.CloseSettings();

        app.Window.OpenSettings();
        Assert.Equal(SettingsSection.Decks, Panel(app).Section);
        app.Window.CloseSettings();

        var chip = app.Window.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("output-chip"));
        Click(chip);
        Assert.True(app.Window.IsSettingsOpen);
        Assert.Equal(SettingsSection.Sound, Panel(app).Section);
    }

    [AvaloniaFact]
    public void Speakers_for_gzjam_alone_is_on_by_default_shown_on_windows_and_remembered()
    {
        using var app = new AppHarness();
        app.Window.OpenSettings(SettingsSection.Sound);
        var toggle = Panel(app).FindControl<ToggleButton>("ExclusiveSpeakersSwitch")!;
        Assert.Equal(OperatingSystem.IsWindows(), toggle.IsEffectivelyVisible);
        Assert.True(toggle.IsChecked);
        Assert.True(app.ViewModel.Mix!.ExclusiveSpeakers);

        app.ViewModel.Settings.ExclusiveSpeakers = false;
        Assert.False(app.Settings.ExclusiveSpeakers);
        Assert.False(app.ViewModel.Mix.ExclusiveSpeakers);
        Assert.False(toggle.IsChecked);
    }

    [AvaloniaFact]
    public void Each_section_in_pictures()
    {
        using var app = new AppHarness();
        foreach (SettingsSection section in Enum.GetValues<SettingsSection>())
        {
            app.Window.OpenSettings(section);
            app.Screenshot($"settings-{section.ToString().ToLowerInvariant()}");
        }

        app.ViewModel.Settings.TouchMode = true;
        app.Window.OpenSettings(SettingsSection.Sound);
        app.Screenshot("settings-sound-touch");
        app.ViewModel.Settings.TouchMode = false;
    }
}
