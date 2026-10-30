using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using GZJam.App.ViewModels;
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
    public void Ctrl_comma_or_Ctrl_O_opens_settings_from_anywhere_and_closes_it_again()
    {
        using var app = new AppHarness();
        var command = OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control;
        var search = app.Window.FindControl<TextBox>("SearchBox")!;
        search.Focus();

        app.Window.KeyPress(Key.OemComma, command, PhysicalKey.Comma, null);
        Assert.True(app.Window.IsSettingsOpen);
        Assert.Equal("", search.Text ?? "");   // nothing typed into the search
        app.Window.KeyPress(Key.OemComma, command, PhysicalKey.Comma, null);
        Assert.False(app.Window.IsSettingsOpen);

        app.Window.KeyPress(Key.O, command, PhysicalKey.O, null);
        Assert.True(app.Window.IsSettingsOpen);
        app.Window.KeyPress(Key.O, command, PhysicalKey.O, null);
        Assert.False(app.Window.IsSettingsOpen);

        app.Window.KeyPress(Key.O, command | RawInputModifiers.Shift, PhysicalKey.O, null);   // only the plain key
        Assert.False(app.Window.IsSettingsOpen);
        Assert.Contains("+,", ToolTip.GetTip(app.Window.FindControl<Button>("SettingsButton")!) as string);
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
    public void Speakers_for_gzjam_alone_is_off_by_default_shown_on_windows_and_remembered()
    {
        using var app = new AppHarness();
        app.Window.OpenSettings(SettingsSection.Sound);
        var toggle = Panel(app).FindControl<ToggleButton>("ExclusiveSpeakersSwitch")!;
        Assert.Equal(OperatingSystem.IsWindows(), toggle.IsEffectivelyVisible);
        Assert.False(toggle.IsChecked);
        Assert.False(app.ViewModel.Mix!.ExclusiveSpeakers);

        app.ViewModel.Settings.ExclusiveSpeakers = true;
        Assert.True(app.Settings.ExclusiveSpeakers);
        Assert.True(app.ViewModel.Mix.ExclusiveSpeakers);
        Assert.True(toggle.IsChecked);
    }

    /// <summary>
    /// #209: sharing the speakers is the default only where nothing was saved (a new install). A
    /// settings file from before holds the old default, on, and keeps it.
    /// </summary>
    [Fact]
    public void Shared_speakers_is_the_default_only_where_no_choice_was_saved()
    {
        string folder = Directory.CreateTempSubdirectory("gzjam-settings-").FullName;
        try
        {
            string fresh = Path.Combine(folder, "fresh.json"), saved = Path.Combine(folder, "saved.json");
            File.WriteAllText(fresh, "{ \"FadeOutSeconds\": 5 }");
            File.WriteAllText(saved, "{ \"ExclusiveSpeakers\": true }");
            Assert.False(AppSettings.Load(fresh).ExclusiveSpeakers);
            Assert.True(AppSettings.Load(saved).ExclusiveSpeakers);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [AvaloniaFact]
    public void Extra_protection_against_crackles_is_off_by_default_and_remembered()
    {
        using var app = new AppHarness();
        app.Window.OpenSettings(SettingsSection.Sound);
        var toggle = Panel(app).FindControl<ToggleButton>("ExtraBufferingSwitch")!;
        Assert.True(toggle.IsEffectivelyVisible);
        Assert.False(toggle.IsChecked);

        app.ViewModel.Settings.ExtraBuffering = true;
        Assert.True(app.Settings.ExtraBuffering);
        Assert.True(toggle.IsChecked);
        Assert.False(Panel(app).FindControl<TextBlock>("WindowsDefaultHint")!.IsEffectivelyVisible);   // no speakers chosen
    }

    [AvaloniaFact]
    public void The_booth_is_off_by_default_and_its_volume_and_mono_reach_it()
    {
        using var app = new AppHarness();
        app.Window.OpenSettings(SettingsSection.Sound);
        Assert.True(Panel(app).FindControl<ComboBox>("BoothDeviceBox")!.IsEffectivelyVisible);
        Assert.False(Panel(app).FindControl<Button>("BoothLouderButton")!.IsEffectivelyVisible);   // no booth: no volume
        Assert.False(app.Window.FindControl<Button>("BoothChip")!.IsVisible);                      // nor a chip in the top bar
        Assert.Null(app.Settings.BoothDevice);

        var settings = app.ViewModel.Settings;
        settings.BoothQuieterCommand.Execute(null);
        settings.BoothQuieterCommand.Execute(null);
        Assert.Equal(90, app.Settings.BoothVolume);
        Assert.Equal(0.81f, app.ViewModel.Mix!.Booth.Volume, 0.001f);   // the same audio taper as the faders
        Assert.Equal("−1.8 dB", settings.BoothVolumeText);
        settings.BoothVolume = 3;   // steps of 5
        Assert.Equal(5, app.Settings.BoothVolume);

        settings.Mono = false;
        Assert.False(app.ViewModel.Mix.Booth.Mono);
        settings.Mono = true;
        Assert.True(app.ViewModel.Mix.Booth.Mono);
    }

    [AvaloniaFact]
    public void One_output_for_both_is_its_own_switch_off_by_default()
    {
        using var app = new AppHarness();
        app.Window.OpenSettings(SettingsSection.Sound);
        var settings = app.ViewModel.Settings;
        var toggle = Panel(app).FindControl<ToggleButton>("SplitOutputSwitch")!;
        Assert.True(toggle.IsEffectivelyVisible);
        Assert.False(toggle.IsChecked);
        Assert.False(settings.SplitOutput);
        Assert.True(Panel(app).FindControl<ComboBox>("CueDeviceBox")!.IsEffectivelyVisible);
        Assert.False(Panel(app).FindControl<TextBlock>("CueSplitText")!.IsEffectivelyVisible);
        Assert.False(Panel(app).FindControl<TextBlock>("SpeakersSplitHelp")!.IsEffectivelyVisible);
        Assert.Equal(SettingsViewModel.NoDevice, settings.CueDevices[0]);
        Assert.All(settings.CueDevices.Skip(1), d => Assert.NotEqual(-2, d.Index));   // no split entry in the list

        settings.SplitOutput = true;   // offline: there's nothing to split
        Assert.False(settings.SplitOutput);
    }

    [AvaloniaFact]
    public void One_output_for_both_says_left_and_right_in_settings_and_the_top_bar()
    {
        // Shared, not GZJam's alone: another program (a GZJam being tried, say) may be using one.
        using var app = new AppHarness(settings: new AppSettings { MatchLoudness = false, ExclusiveSpeakers = false }, realOutputs: true);
        foreach (var device in app.Audio.GetOutputDevices().Where(d => !d.FollowsSystemDefault))
        {
            if (app.Mix.Main.Device is not null)
                break;
            app.ViewModel.SetOutputs(device, null, null, remember: false, quiet: true);
        }
        Assert.SkipWhen(app.Mix.Main.Device is null, "Needs a sound device that opens.");
        app.Window.OpenSettings(SettingsSection.Sound);
        var settings = app.ViewModel.Settings;
        string name = app.Mix.Main.Device!.Name;

        settings.SplitOutput = true;
        AppHarness.Settle(100);
        Assert.True(app.Mix.IsSplit);
        Assert.True(Panel(app).FindControl<ToggleButton>("SplitOutputSwitch")!.IsChecked);
        Assert.True(Panel(app).FindControl<TextBlock>("SpeakersSplitHelp")!.IsEffectivelyVisible);
        Assert.True(Panel(app).FindControl<TextBlock>("CueSplitText")!.IsEffectivelyVisible);
        Assert.False(Panel(app).FindControl<ComboBox>("CueDeviceBox")!.IsEffectivelyVisible);
        Assert.Equal($"Left · {name}", app.ViewModel.MainOutputName);   // the side first: a long name is trimmed at its end
        Assert.Equal($"Right · {name}", app.ViewModel.CueOutputName);
        Assert.Equal("Split: the room on the left plug, your headphones on the right.", app.ViewModel.Notice);
        app.Screenshot("settings-sound-split");

        settings.SplitOutput = false;
        AppHarness.Settle(100);
        Assert.False(app.Mix.IsSplit);
        Assert.Null(app.Mix.Cue.Device);   // the headphones are off until chosen again
        Assert.Equal(name, app.ViewModel.MainOutputName);
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
