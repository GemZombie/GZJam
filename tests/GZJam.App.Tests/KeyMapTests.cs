using Avalonia.Input;

namespace GZJam.App.Tests;

/// <summary>Which key does what (#135): GZJam's own keys, with the DJ's choices over them.</summary>
public sealed class KeyMapTests
{
    private static readonly KeyAction AutoPlay = new(KeyCommand.AutoPlay);
    private static readonly KeyAction FadeOut1 = new(KeyCommand.FadeOut, 1);
    private static readonly KeyAction Play1 = new(KeyCommand.PlayPause, 1);
    private static readonly KeyAction Settings = new(KeyCommand.Settings);

    [Fact]
    public void The_defaults_are_the_keys_gzjam_always_had_and_fade_out_has_none()
    {
        var map = new KeyMap([]);
        Assert.Equal(Play1, map.ActionFor(KeyChord.Command(Key.D1)));
        Assert.Equal(Play1, map.ActionFor(KeyChord.Command(Key.NumPad1)));   // the keypad too
        Assert.Equal(new KeyAction(KeyCommand.Cue, 2), map.ActionFor(KeyChord.Command(Key.D2, ChordModifiers.Shift)));
        Assert.Equal(new KeyAction(KeyCommand.Preview, 4), map.ActionFor(KeyChord.Command(Key.D4, ChordModifiers.Alt | ChordModifiers.Shift)));
        Assert.Equal(AutoPlay, map.ActionFor(KeyChord.Command(Key.P)));
        Assert.Equal(new KeyAction(KeyCommand.FullScreen), map.ActionFor(new KeyChord(Key.F11, ChordModifiers.None)));
        Assert.Equal(Settings, map.ActionFor(KeyChord.Command(Key.O)));
        Assert.Empty(map.KeysFor(FadeOut1));
        Assert.Equal("", map.Tip(FadeOut1));
        Assert.True(map.AllDefaults);
        Assert.All(KeyMap.Actions.SelectMany(KeyMap.Defaults), k => Assert.Null(k.Refusal()));
        // No two actions share a key.
        var all = KeyMap.Actions.SelectMany(KeyMap.Defaults).ToList();
        Assert.Equal(all.Count, all.Distinct().Count());
    }

    [Fact]
    public void A_key_moves_from_the_action_that_had_it_and_only_changes_are_saved()
    {
        var saved = new Dictionary<string, List<string>>();
        var map = new KeyMap(saved);
        int changes = 0;
        map.Changed += (_, _) => changes++;

        KeyAction? from = map.Set(FadeOut1, KeyChord.Command(Key.P));

        Assert.Equal(AutoPlay, from);
        Assert.Equal(FadeOut1, map.ActionFor(KeyChord.Command(Key.P)));
        Assert.Empty(map.KeysFor(AutoPlay));
        Assert.Equal(1, changes);
        Assert.Equal(["Cmd+P"], saved["Deck1.FadeOut"]);
        Assert.Empty(saved["AutoPlay"]);
        Assert.Equal(2, saved.Count);
        Assert.True(map.IsChanged(AutoPlay));

        map.Set(AutoPlay, KeyChord.Command(Key.P));   // back as it was: no longer saved
        Assert.False(saved.ContainsKey("AutoPlay"));
        Assert.Empty(map.KeysFor(FadeOut1));
        Assert.Empty(saved);   // fade out lost it, and no key is GZJam's own for it
    }

    [Fact]
    public void Taking_one_of_two_keys_leaves_the_other()
    {
        var map = new KeyMap([]);
        Assert.Equal(Settings, map.Set(AutoPlay, KeyChord.Command(Key.O)));
        Assert.Equal([KeyChord.Command(Key.OemComma)], map.KeysFor(Settings));
        Assert.Equal(AutoPlay, map.ActionFor(KeyChord.Command(Key.O)));
    }

    [Fact]
    public void Reset_puts_every_key_back()
    {
        var saved = new Dictionary<string, List<string>>();
        var map = new KeyMap(saved);
        map.Set(Play1, new KeyChord(Key.F5, ChordModifiers.None));
        map.Set(AutoPlay, null);
        map.ResetAll();
        Assert.Empty(saved);
        Assert.Equal(Play1, map.ActionFor(KeyChord.Command(Key.D1)));
        Assert.Null(map.ActionFor(new KeyChord(Key.F5, ChordModifiers.None)));
        Assert.Equal(AutoPlay, map.ActionFor(KeyChord.Command(Key.P)));
    }

    [Fact]
    public void A_saved_choice_keeps_its_key_from_a_default_and_bad_entries_are_ignored()
    {
        var saved = new Dictionary<string, List<string>>
        {
            ["Deck1.FadeOut"] = ["Cmd+P"],          // as if a later GZJam made Ctrl+P a default elsewhere
            ["Deck2.PlayPause"] = ["Cmd+C", "Cmd+F9"],   // Ctrl+C edits text: dropped, the other kept
            ["Search"] = ["Cmd+Nonsense"],
            ["SomethingNewer"] = ["Cmd+K"],          // from a later version: left alone
        };
        var map = new KeyMap(saved);
        Assert.Equal(FadeOut1, map.ActionFor(KeyChord.Command(Key.P)));
        Assert.Empty(map.KeysFor(AutoPlay));
        Assert.Equal([KeyChord.Command(Key.F9)], map.KeysFor(new(KeyCommand.PlayPause, 2)));
        Assert.Empty(map.KeysFor(new(KeyCommand.Search)));
        Assert.True(saved.ContainsKey("SomethingNewer"));
    }

    [Fact]
    public void Keys_are_saved_the_same_on_every_computer_and_read_back()
    {
        foreach (KeyChord chord in KeyMap.Actions.SelectMany(KeyMap.Defaults))
            Assert.Equal(chord, KeyChord.Parse(chord.Saved));
        Assert.Equal("Cmd+Alt+Shift+D3", KeyChord.Command(Key.D3, ChordModifiers.Alt | ChordModifiers.Shift).Saved);
        Assert.Null(KeyChord.Parse("Hyper+P"));
        Assert.Null(KeyChord.Parse(""));
    }

    [Fact]
    public void Keys_read_as_printed_on_the_keyboard()
    {
        string command = OperatingSystem.IsMacOS() ? "Cmd" : "Ctrl";
        string alt = OperatingSystem.IsMacOS() ? "Option" : "Alt";
        Assert.Equal($"{command}+{alt}+Shift+1", KeyChord.Command(Key.D1, ChordModifiers.Alt | ChordModifiers.Shift).Display);
        Assert.Equal($"{command}+←", KeyChord.Command(Key.Left).Display);
        Assert.Equal($"{command}+,", KeyChord.Command(Key.OemComma).Display);
        Assert.Equal("F11", new KeyChord(Key.F11, ChordModifiers.None).Display);
        var map = new KeyMap([]);
        Assert.Equal($" · keys: {command}+, or {command}+O", map.Tip(Settings));
        Assert.Equal($" · key: {command}+P", map.Tip(AutoPlay));
    }

    [Fact]
    public void Keys_that_could_fire_while_typing_or_belong_to_text_or_the_system_are_refused()
    {
        Assert.NotNull(new KeyChord(Key.P, ChordModifiers.None).Refusal());
        Assert.NotNull(new KeyChord(Key.P, ChordModifiers.Shift).Refusal());
        Assert.NotNull(KeyChord.Command(Key.C).Refusal());
        Assert.NotNull(KeyChord.Command(Key.Z, ChordModifiers.Shift).Refusal());
        Assert.NotNull(KeyChord.Command(Key.Escape).Refusal());
        Assert.Null(new KeyChord(Key.F5, ChordModifiers.None).Refusal());
        Assert.Null(KeyChord.Command(Key.C, ChordModifiers.Alt).Refusal());
        Assert.Throws<ArgumentException>(() => new KeyMap([]).Set(AutoPlay, KeyChord.Command(Key.V)));
        if (!OperatingSystem.IsMacOS())
            Assert.Null(KeyChord.From(Key.P, KeyModifiers.Control | KeyModifiers.Meta));   // with the Windows or Super key: the system's
    }

    [Fact]
    public void In_a_text_box_Ctrl_Alt_and_the_word_keys_are_left_to_it()
    {
        Assert.False(KeyChord.Command(Key.D1, ChordModifiers.Alt).WorksWhileTyping);   // AltGr types characters
        Assert.False(KeyChord.Command(Key.Left).WorksWhileTyping);
        Assert.True(KeyChord.Command(Key.Up).WorksWhileTyping);
        Assert.True(KeyChord.Command(Key.D1, ChordModifiers.Shift).WorksWhileTyping);
        Assert.True(new KeyChord(Key.F11, ChordModifiers.None).WorksWhileTyping);
    }
}
