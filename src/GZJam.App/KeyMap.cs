using Avalonia.Input;

namespace GZJam.App;

/// <summary>What a key can do (#135). The deck ones are for the deck in <see cref="KeyAction.Deck"/>.</summary>
public enum KeyCommand
{
    PlayPause,
    Cue,
    Load,
    Preview,
    FadeOut,
    CrossfadeTo1,
    CrossfadeTo2,
    AutoPlay,
    MainUp,
    MainDown,
    PhonesUp,
    PhonesDown,
    Search,
    SongInfo,
    CompactDecks,
    HideDecks,
    QueueWide,
    FullScreen,
    Settings,
}

/// <summary>A key's action: a command, and for the deck ones, which deck (1 to 4).</summary>
public readonly record struct KeyAction(KeyCommand Command, int Deck = 0)
{
    /// <summary>How it's saved in the settings: "AutoPlay", "Deck1.PlayPause".</summary>
    public string Id => Deck > 0 ? $"Deck{Deck}.{Command}" : Command.ToString();

    public static bool IsDeckCommand(KeyCommand command) => command <= KeyCommand.FadeOut;

    /// <summary>Its name on Settings › Keys, under its group's heading.</summary>
    public string Name => Command switch
    {
        KeyCommand.PlayPause => "Play / pause",
        KeyCommand.Cue => "CUE (headphones) on or off",
        KeyCommand.Load => "Load the selected song",
        KeyCommand.Preview => "Its song in the headphones' preview",
        KeyCommand.FadeOut => "Fade out",
        KeyCommand.CrossfadeTo1 => "Crossfader to Deck 1",
        KeyCommand.CrossfadeTo2 => "Crossfader to Deck 2",
        KeyCommand.AutoPlay => "Auto-play on or off",
        KeyCommand.MainUp => "MAIN volume up",
        KeyCommand.MainDown => "MAIN volume down",
        KeyCommand.PhonesUp => "PHONES volume up",
        KeyCommand.PhonesDown => "PHONES volume down",
        KeyCommand.Search => "Search",
        KeyCommand.SongInfo => "The selected songs' info",
        KeyCommand.CompactDecks => "Compact decks, and back",
        KeyCommand.HideDecks => "Hide the decks, and back",
        KeyCommand.QueueWide => "The queue takes the room in the middle, and back",
        KeyCommand.FullScreen => "Full screen, and back",
        KeyCommand.Settings => "Settings, and closed again",
        _ => Command.ToString(),
    };

    /// <summary>A line under the name, where one is needed.</summary>
    public string? Help => Command switch
    {
        KeyCommand.PlayPause => "On air, two presses if you've chosen two taps, as the button.",
        KeyCommand.Load => "From the queue if you're in it (it leaves the queue), otherwise from the library.",
        KeyCommand.Preview => "From the start, or stop it; the deck stays where it is.",
        KeyCommand.FadeOut => "No key at first: one press ending the song the room hears is easy to hit by mistake. Two presses on air if you've chosen two taps.",
        KeyCommand.MainUp or KeyCommand.MainDown or KeyCommand.PhonesUp or KeyCommand.PhonesDown => "A little each press; held, it repeats.",
        _ => null,
    };
}

/// <summary>Modifiers as GZJam keeps them, the same on every computer: Command is Ctrl, or Cmd on a Mac.</summary>
[Flags]
public enum ChordModifiers
{
    None = 0,
    /// <summary>Ctrl on Windows and Linux, Cmd on a Mac.</summary>
    Command = 1,
    /// <summary>The Mac's Control key, beside Cmd (Ctrl+Cmd+F). Elsewhere Ctrl is <see cref="Command"/>.</summary>
    MacControl = 2,
    Alt = 4,
    Shift = 8,
}

/// <summary>
/// A key with its modifiers. Saved as text that reads the same on any computer ("Cmd+Shift+D1"),
/// so a backup taken on Windows works on a Mac.
/// </summary>
public readonly record struct KeyChord(Key Key, ChordModifiers Modifiers)
{
    public static KeyChord Command(Key key, ChordModifiers extra = ChordModifiers.None) => new(key, ChordModifiers.Command | extra);

    /// <summary>A key pressed on this computer, or null for one GZJam can't keep (the Windows or Super key).</summary>
    public static KeyChord? From(Key key, KeyModifiers modifiers)
    {
        bool mac = OperatingSystem.IsMacOS();
        if (!mac && modifiers.HasFlag(KeyModifiers.Meta))
            return null;
        var chord = ChordModifiers.None;
        if (modifiers.HasFlag(mac ? KeyModifiers.Meta : KeyModifiers.Control))
            chord |= ChordModifiers.Command;
        if (mac && modifiers.HasFlag(KeyModifiers.Control))
            chord |= ChordModifiers.MacControl;
        if (modifiers.HasFlag(KeyModifiers.Alt))
            chord |= ChordModifiers.Alt;
        if (modifiers.HasFlag(KeyModifiers.Shift))
            chord |= ChordModifiers.Shift;
        return new KeyChord(key, chord);
    }

    /// <summary>Avalonia's modifiers for this chord on this computer (for a menu's key).</summary>
    public KeyModifiers ToKeyModifiers()
    {
        bool mac = OperatingSystem.IsMacOS();
        var result = KeyModifiers.None;
        if (Modifiers.HasFlag(ChordModifiers.Command))
            result |= mac ? KeyModifiers.Meta : KeyModifiers.Control;
        if (Modifiers.HasFlag(ChordModifiers.MacControl))
            result |= KeyModifiers.Control;
        if (Modifiers.HasFlag(ChordModifiers.Alt))
            result |= KeyModifiers.Alt;
        if (Modifiers.HasFlag(ChordModifiers.Shift))
            result |= KeyModifiers.Shift;
        return result;
    }

    public KeyGesture ToGesture() => new(Key, ToKeyModifiers());

    public bool IsModifierKey => Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt
        or Key.RightAlt or Key.LWin or Key.RWin or Key.CapsLock or Key.NumLock or Key.Scroll or Key.None
        or Key.ImeProcessed or Key.DeadCharProcessed;

    public bool IsFunctionKey => Key is >= Key.F1 and <= Key.F24;

    /// <summary>
    /// Whether it may act while the keyboard is in a text box. Not Ctrl+Alt, which is AltGr there
    /// and types a character on many layouts, nor Ctrl with the keys that move or delete by word.
    /// </summary>
    public bool WorksWhileTyping => !(Modifiers.HasFlag(ChordModifiers.Command)
        && (Modifiers.HasFlag(ChordModifiers.Alt) || Key is Key.Left or Key.Right or Key.Home or Key.End or Key.Back or Key.Delete));

    /// <summary>
    /// Why it can't be a key for an action, or null if it can. A key needs Ctrl (Cmd on a Mac) or
    /// to be a function key, so it can never fire while typing, and it mustn't be one that edits
    /// text or belongs to the system.
    /// </summary>
    public string? Refusal()
    {
        bool command = Modifiers.HasFlag(ChordModifiers.Command);
        bool alt = Modifiers.HasFlag(ChordModifiers.Alt);
        if (Key is Key.Escape or Key.Tab)
            return $"{Display} is GZJam's own: choose another.";
        if (!command && !IsFunctionKey)
            return $"A key needs {Shortcuts.Command} with it, or to be a function key (F1 to F12), so it can't fire while you type.";
        if (command && !alt && Key is Key.A or Key.C or Key.V or Key.X or Key.Z or Key.Y)
            return $"{Display} is for editing text: choose another.";
        if (OperatingSystem.IsMacOS() && command && Key is Key.Q or Key.H or Key.M or Key.W)
            return $"{Display} belongs to macOS: choose another.";
        if (!OperatingSystem.IsMacOS() && alt && Key == Key.F4)
            return $"{Display} closes the window: choose another.";
        return null;
    }

    /// <summary>As people read it on this computer: "Ctrl+Shift+1", "Cmd+Option+←".</summary>
    public string Display
    {
        get
        {
            var parts = new List<string>(5);
            if (Modifiers.HasFlag(ChordModifiers.MacControl))
                parts.Add("Ctrl");
            if (Modifiers.HasFlag(ChordModifiers.Command))
                parts.Add(Shortcuts.Command);
            if (Modifiers.HasFlag(ChordModifiers.Alt))
                parts.Add(Shortcuts.Alt);
            if (Modifiers.HasFlag(ChordModifiers.Shift))
                parts.Add("Shift");
            parts.Add(KeyName(Key));
            return string.Join('+', parts);
        }
    }

    public override string ToString() => Display;

    /// <summary>As saved: "Cmd+Shift+D1". The same on every computer.</summary>
    public string Saved
    {
        get
        {
            var parts = new List<string>(5);
            if (Modifiers.HasFlag(ChordModifiers.MacControl))
                parts.Add("MacCtrl");
            if (Modifiers.HasFlag(ChordModifiers.Command))
                parts.Add("Cmd");
            if (Modifiers.HasFlag(ChordModifiers.Alt))
                parts.Add("Alt");
            if (Modifiers.HasFlag(ChordModifiers.Shift))
                parts.Add("Shift");
            parts.Add(Key.ToString());
            return string.Join('+', parts);
        }
    }

    public static KeyChord? Parse(string text)
    {
        string[] parts = text.Split('+', StringSplitOptions.TrimEntries);
        if (parts.Length == 0 || !Enum.TryParse(parts[^1], out Key key) || !Enum.IsDefined(key) || key == Key.None)
            return null;
        var modifiers = ChordModifiers.None;
        foreach (string part in parts[..^1])
        {
            ChordModifiers? modifier = part switch
            {
                "Cmd" => ChordModifiers.Command,
                "MacCtrl" => ChordModifiers.MacControl,
                "Alt" => ChordModifiers.Alt,
                "Shift" => ChordModifiers.Shift,
                _ => null,
            };
            if (modifier is null)
                return null;
            modifiers |= modifier.Value;
        }
        return new KeyChord(key, modifiers);
    }

    /// <summary>A key's name as printed on it, near enough.</summary>
    public static string KeyName(Key key) => key switch
    {
        >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
        >= Key.NumPad0 and <= Key.NumPad9 => $"Num {key - Key.NumPad0}",
        >= Key.A and <= Key.Z => key.ToString(),
        Key.Left => "←",
        Key.Right => "→",
        Key.Up => "↑",
        Key.Down => "↓",
        Key.OemComma => ",",
        Key.OemPeriod => ".",
        Key.OemMinus => "-",
        Key.OemPlus => "=",
        Key.OemQuestion => "/",
        Key.OemSemicolon => ";",
        Key.OemQuotes => "'",
        Key.OemOpenBrackets => "[",
        Key.OemCloseBrackets => "]",
        Key.OemPipe or Key.OemBackslash => "\\",
        Key.OemTilde => "`",
        Key.Add => "Num +",
        Key.Subtract => "Num -",
        Key.Multiply => "Num *",
        Key.Divide => "Num /",
        Key.Decimal => "Num .",
        Key.Back => "Backspace",
        Key.Enter => "Enter",
        Key.PageUp => "Page Up",
        Key.PageDown => "Page Down",
        Key.MediaPlayPause => "Play/Pause",
        Key.MediaNextTrack => "Next track",
        Key.MediaPreviousTrack => "Previous track",
        Key.MediaStop => "Stop",
        _ => key.ToString(),
    };

    /// <summary>A keypad digit as the number row's, so Ctrl+1 works from either.</summary>
    public KeyChord? NumberRowTwin => Key is >= Key.NumPad0 and <= Key.NumPad9
        ? this with { Key = Key.D0 + (Key - Key.NumPad0) }
        : null;
}

/// <summary>
/// Which key does what (#135): GZJam's defaults, with the DJ's own choices over them. Only the
/// actions a DJ changed are saved (in <see cref="AppSettings.Keys"/>), so a later version's new
/// defaults reach everyone who hasn't chosen otherwise.
/// </summary>
public sealed class KeyMap
{
    private readonly Dictionary<string, List<string>> _saved;
    private readonly Dictionary<KeyAction, IReadOnlyList<KeyChord>> _keys = [];
    private readonly Dictionary<KeyChord, KeyAction> _actions = [];

    /// <param name="saved">The settings' choices, kept up to date as keys are changed.</param>
    public KeyMap(Dictionary<string, List<string>> saved)
    {
        _saved = saved;
        Build();
    }

    /// <summary>Every action, in the order Settings › Keys lists them.</summary>
    public static IReadOnlyList<KeyAction> Actions { get; } =
    [
        .. Enumerable.Range(1, 4).SelectMany(deck => new[] { KeyCommand.PlayPause, KeyCommand.Cue, KeyCommand.Load, KeyCommand.Preview, KeyCommand.FadeOut }
            .Select(c => new KeyAction(c, deck))),
        new(KeyCommand.CrossfadeTo1), new(KeyCommand.CrossfadeTo2), new(KeyCommand.AutoPlay),
        new(KeyCommand.MainUp), new(KeyCommand.MainDown), new(KeyCommand.PhonesUp), new(KeyCommand.PhonesDown),
        new(KeyCommand.Search), new(KeyCommand.SongInfo), new(KeyCommand.CompactDecks), new(KeyCommand.HideDecks),
        new(KeyCommand.QueueWide), new(KeyCommand.FullScreen), new(KeyCommand.Settings),
    ];

    /// <summary>GZJam's own keys for an action, on this computer.</summary>
    public static IReadOnlyList<KeyChord> Defaults(KeyAction action)
    {
        Key digit = Key.D0 + action.Deck;
        return action.Command switch
        {
            KeyCommand.PlayPause => [KeyChord.Command(digit)],
            KeyCommand.Cue => [KeyChord.Command(digit, ChordModifiers.Shift)],
            KeyCommand.Load => [KeyChord.Command(digit, ChordModifiers.Alt)],
            KeyCommand.Preview => [KeyChord.Command(digit, ChordModifiers.Alt | ChordModifiers.Shift)],
            KeyCommand.FadeOut => [],
            KeyCommand.CrossfadeTo1 => [KeyChord.Command(Key.Left)],
            KeyCommand.CrossfadeTo2 => [KeyChord.Command(Key.Right)],
            KeyCommand.AutoPlay => [KeyChord.Command(Key.P)],
            KeyCommand.MainUp => [KeyChord.Command(Key.Up)],
            KeyCommand.MainDown => [KeyChord.Command(Key.Down)],
            KeyCommand.PhonesUp => [KeyChord.Command(Key.Up, ChordModifiers.Shift)],
            KeyCommand.PhonesDown => [KeyChord.Command(Key.Down, ChordModifiers.Shift)],
            KeyCommand.Search => [KeyChord.Command(Key.F)],
            KeyCommand.SongInfo => [KeyChord.Command(Key.I)],
            KeyCommand.CompactDecks => [KeyChord.Command(Key.D)],
            KeyCommand.HideDecks => [KeyChord.Command(Key.D, ChordModifiers.Shift)],
            KeyCommand.QueueWide => [KeyChord.Command(Key.L)],
            // F11, and on a Mac its own Ctrl+Cmd+F too.
            KeyCommand.FullScreen => OperatingSystem.IsMacOS()
                ? [new KeyChord(Key.F11, ChordModifiers.None), KeyChord.Command(Key.F, ChordModifiers.MacControl)]
                : [new KeyChord(Key.F11, ChordModifiers.None)],
            // Ctrl+, (the Mac's own key for Settings) and Ctrl+O, for Options (#166).
            KeyCommand.Settings => [KeyChord.Command(Key.OemComma), KeyChord.Command(Key.O)],
            _ => [],
        };
    }

    /// <summary>A key was changed, or all were reset: tooltips naming keys should be read again.</summary>
    public event EventHandler? Changed;

    /// <summary>The keys for an action, none if it has none.</summary>
    public IReadOnlyList<KeyChord> KeysFor(KeyAction action) => _keys.GetValueOrDefault(action, []);

    /// <summary>The action a key does, if any. A keypad digit does what its number-row twin does unless it has its own.</summary>
    public KeyAction? ActionFor(KeyChord chord) =>
        _actions.TryGetValue(chord, out KeyAction action) ? action
        : chord.NumberRowTwin is { } twin && _actions.TryGetValue(twin, out action) ? action
        : null;

    /// <summary>"Ctrl+P", "Ctrl+, or Ctrl+O", or null with no key.</summary>
    public string? Text(KeyAction action) => KeysFor(action) is { Count: > 0 } keys ? string.Join(" or ", keys.Select(k => k.Display)) : null;

    /// <summary>For the end of a tooltip: " · key: Ctrl+P", or nothing with no key.</summary>
    public string Tip(KeyAction action) => Text(action) is { } text
        ? $" · {(KeysFor(action).Count > 1 ? "keys" : "key")}: {text}"
        : "";

    /// <summary>The DJ chose this action's keys (they aren't GZJam's own).</summary>
    public bool IsChanged(KeyAction action) => _saved.ContainsKey(action.Id);

    /// <summary>Whether no action's keys were changed.</summary>
    public bool AllDefaults => !Actions.Any(IsChanged);

    /// <summary>
    /// Gives an action <paramref name="chord"/> as its only key, or no key (null). The key is taken
    /// from the action that had it; that action is returned.
    /// </summary>
    public KeyAction? Set(KeyAction action, KeyChord? chord)
    {
        if (chord?.Refusal() is { } refusal)
            throw new ArgumentException(refusal, nameof(chord));
        KeyAction? from = null;
        if (chord is { } key && _actions.TryGetValue(key, out KeyAction owner) && owner != action)
        {
            from = owner;
            Save(owner, KeysFor(owner).Where(k => k != key));
        }
        Save(action, chord is { } c ? [c] : []);
        Build();
        Changed?.Invoke(this, EventArgs.Empty);
        return from;
    }

    /// <summary>Every action back to GZJam's own keys.</summary>
    public void ResetAll()
    {
        _saved.Clear();
        Build();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Save(KeyAction action, IEnumerable<KeyChord> keys)
    {
        var list = keys.ToList();
        if (list.SequenceEqual(Defaults(action)))
            _saved.Remove(action.Id);
        else
            _saved[action.Id] = list.Select(k => k.Saved).ToList();
    }

    /// <summary>
    /// The DJ's choices first, then GZJam's defaults for the rest, minus any key a choice already
    /// took (a later version's new default never steals a key the DJ chose).
    /// </summary>
    private void Build()
    {
        _keys.Clear();
        _actions.Clear();
        foreach (KeyAction action in Actions)
        {
            if (!_saved.TryGetValue(action.Id, out List<string>? texts))
                continue;
            var keys = new List<KeyChord>();
            foreach (string text in texts)
            {
                if (KeyChord.Parse(text) is { } chord && chord.Refusal() is null && _actions.TryAdd(chord, action))
                    keys.Add(chord);
            }
            _keys[action] = keys;
        }
        foreach (KeyAction action in Actions.Where(a => !_keys.ContainsKey(a)))
        {
            var keys = new List<KeyChord>();
            foreach (KeyChord chord in Defaults(action))
            {
                if (_actions.TryAdd(chord, action))
                    keys.Add(chord);
            }
            _keys[action] = keys;
        }
    }
}
