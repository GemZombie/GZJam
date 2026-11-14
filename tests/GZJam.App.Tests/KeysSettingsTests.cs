using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using GZJam.App.ViewModels;
using GZJam.App.Views;

namespace GZJam.App.Tests;

/// <summary>Settings › Keys (#135): click an action's key, press a new one, and it's that action's.</summary>
public sealed class KeysSettingsTests
{
    private static readonly RawInputModifiers Command = OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control;
    private static readonly string CommandName = OperatingSystem.IsMacOS() ? "Cmd" : "Ctrl";

    private static void Press(AppHarness app, Key key, PhysicalKey physical, RawInputModifiers modifiers)
    {
        app.Window.KeyPress(key, modifiers, physical, null);
        app.Window.KeyRelease(key, modifiers, physical, null);
    }

    /// <summary>Opens Settings › Keys and clicks the key of <paramref name="action"/>.</summary>
    private static KeyRowViewModel Choose(AppHarness app, KeyAction action)
    {
        app.Window.OpenSettings(SettingsSection.Keys);
        AppHarness.Settle();
        var panel = app.Window.FindControl<SettingsPanel>("SettingsPanel")!;
        var button = panel.GetVisualDescendants().OfType<Button>()
            .First(b => b.Classes.Contains("key-choice") && b.DataContext is KeyRowViewModel row && row.Action == action);
        button.Focus();
        button.Command!.Execute(button.CommandParameter);
        return (KeyRowViewModel)button.DataContext!;
    }

    [AvaloniaFact]
    public async Task A_new_key_does_its_action_and_the_action_it_was_taken_from_no_longer_has_it()
    {
        using var app = new AppHarness();
        var vm = app.ViewModel;
        await vm.LoadToDeckAsync(vm.Deck1!, app.ToneTrack("One", 60));
        var keys = vm.Settings.Keys;

        KeyRowViewModel row = Choose(app, new(KeyCommand.FadeOut, 1));
        Assert.Equal("PRESS A KEY…", row.ButtonText);
        Assert.True(app.Window.FindControl<SettingsPanel>("SettingsPanel")!.IsChoosingKey);
        app.Screenshot("settings-keys-choosing");

        Press(app, Key.P, PhysicalKey.P, Command);

        Assert.False(keys.IsChoosing);
        Assert.Equal($"{CommandName}+P", row.ButtonText);
        Assert.Equal($"{CommandName}+P was for Auto-play on or off, which has no key now.", keys.Notice);
        Assert.Equal(["Cmd+P"], app.Settings.Keys["Deck1.FadeOut"]);
        Assert.DoesNotContain("key:", vm.AutoPlay!.SwitchTip);
        Assert.EndsWith($"key: {CommandName}+P", vm.Deck1!.FadeOutTip);
        Assert.True(app.Window.IsSettingsOpen);   // the key went to the page, nothing else
        Assert.False(vm.AutoPlay.IsOn);
        app.Screenshot("settings-keys");

        app.Window.CloseSettings();
        app.Settings.OnAirPause = OnAirPause.Tap;
        vm.Deck1.TogglePlayCommand.Execute(null);
        app.Play(0.1);
        app.Window.FindControl<DataGrid>("TrackGrid")!.Focus();
        Press(app, Key.P, PhysicalKey.P, Command);
        Assert.True(vm.Deck1.Deck.IsFading);
        Assert.False(vm.AutoPlay.IsOn);
    }

    [AvaloniaFact]
    public async Task While_a_key_is_wanted_no_other_key_acts_and_Escape_keeps_the_old_one()
    {
        using var app = new AppHarness();
        var vm = app.ViewModel;
        await vm.LoadToDeckAsync(vm.Deck1!, app.ToneTrack("One", 60));
        var keys = vm.Settings.Keys;

        KeyRowViewModel row = Choose(app, new(KeyCommand.AutoPlay));
        Press(app, Key.C, PhysicalKey.C, Command);   // for editing text
        Assert.True(keys.IsChoosing);
        Assert.Equal($"{CommandName}+C is for editing text: choose another.", keys.Notice);
        Press(app, Key.D1, PhysicalKey.Digit1, RawInputModifiers.None);   // no Ctrl: could fire while typing
        Assert.True(keys.IsChoosing);
        Assert.StartsWith("A key needs", keys.Notice);
        Press(app, Key.LeftShift, PhysicalKey.ShiftLeft, RawInputModifiers.Shift);   // a modifier alone waits for the rest
        Assert.True(keys.IsChoosing);

        Press(app, Key.Escape, PhysicalKey.Escape, RawInputModifiers.None);
        Assert.False(keys.IsChoosing);
        Assert.True(app.Window.IsSettingsOpen);   // Escape stopped the choosing, not Settings
        Assert.Equal($"{CommandName}+P", row.ButtonText);
        Assert.Empty(app.Settings.Keys);

        Choose(app, new(KeyCommand.AutoPlay));
        Press(app, Key.D1, PhysicalKey.Digit1, Command);   // Deck 1's play key: taken, not played
        Assert.False(vm.Deck1!.IsPlaying);
        Assert.Equal($"{CommandName}+1", row.ButtonText);
        Assert.Contains("Deck 1 › Play / pause", keys.Notice);

        Choose(app, new(KeyCommand.AutoPlay));
        Press(app, Key.Delete, PhysicalKey.Delete, RawInputModifiers.None);
        Assert.Equal("No key", row.ButtonText);
        Assert.Equal("Auto-play on or off has no key now.", keys.Notice);

        Assert.True(keys.CanReset);
        keys.ResetCommand.Execute(null);
        Assert.Equal($"{CommandName}+P", row.ButtonText);
        Assert.Empty(app.Settings.Keys);
        Assert.False(keys.CanReset);
    }

    [AvaloniaFact]
    public void Decks_3_and_4_are_listed_only_with_four_decks_and_the_menus_show_the_info_key()
    {
        using var app = new AppHarness();
        var keys = app.ViewModel.Settings.Keys;
        Assert.Equal([true, true, false, false, true, true], keys.Groups.Select(g => g.IsShown));
        app.ViewModel.Settings.FourDecks = true;
        Assert.All(keys.Groups, g => Assert.True(g.IsShown));

        var info = app.Window.FindControl<MenuItem>("TrackInfoItem")!;
        Assert.Equal(new KeyGesture(Key.I, OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control), info.InputGesture);
        app.ViewModel.Keys.Set(new(KeyCommand.SongInfo), new KeyChord(Key.F2, ChordModifiers.None));
        Assert.Equal(new KeyGesture(Key.F2), info.InputGesture);
    }
}
