using System.Text.Json;
using System.Text.Json.Serialization;
using GZJam.Audio;

namespace GZJam.App;

/// <summary>What happens when a track is loaded onto a deck that's playing.</summary>
public enum PlayingDeckLoad
{
    /// <summary>Refuse; the deck must be paused first. The safe default.</summary>
    Block,
    /// <summary>Ask for confirmation.</summary>
    Ask,
    /// <summary>Load anyway.</summary>
    Allow,
}

/// <summary>How the play button pauses a deck the room is hearing.</summary>
public enum OnAirPause
{
    /// <summary>A tap pauses, like any other deck. The default.</summary>
    Tap,
    /// <summary>
    /// Tap twice (or press and hold), so a stray touch can't stop the music. Works with trackpad
    /// tap-to-click, where holding is awkward.
    /// </summary>
    TapTwice,
}

/// <summary>What a deck's big readout shows; tapping it cycles through these.</summary>
public enum DeckReadout
{
    Remaining,
    Elapsed,
    Bpm,
}

/// <summary>Everything GZJam remembers between sessions. Saved as JSON; unknown or bad files fall back to defaults.</summary>
public sealed class AppSettings
{
    public WindowPlacement? Window { get; set; }

    /// <summary>The folder selected in the library tree, as root path + relative folder.</summary>
    public string? SelectedRootPath { get; set; }
    public string? SelectedFolder { get; set; }

    /// <summary>Speakers. Matched by name and driver ID, since device numbers change between sessions.</summary>
    public AudioDevicePreference? MainDevice { get; set; }

    /// <summary>Headphones.</summary>
    public AudioDevicePreference? CueDevice { get; set; }

    /// <summary>
    /// Speakers and headphones play mono, so the whole floor hears instruments recorded on one
    /// side, and the DJ hears what the room hears, with either ear. On by default.
    /// </summary>
    public bool Mono { get; set; } = true;

    /// <summary>
    /// Windows: the speakers' sound card is GZJam's alone while it's open, so no other program's
    /// sound (a notification, a browser) can reach the room. Only for an output chosen by name,
    /// not "Default"; the headphones stay shared. On by default.
    /// </summary>
    public bool ExclusiveSpeakers { get; set; } = true;

    /// <summary>
    /// Longer buffers on every output, for a computer where the sound crackles or drops out. Play
    /// and the crossfader respond a little later. Off by default.
    /// </summary>
    public bool ExtraBuffering { get; set; }

    /// <summary>
    /// How many decks are on screen: 2, or 4 (half height, 1 and 3 on the left of the mixer, 2 and
    /// 4 on the right).
    /// </summary>
    public int DeckCount { get; set; } = 2;

    /// <summary>
    /// Finger-sized keys, rows, menus and crossfader, for a touchscreen. Off by default; touch
    /// gestures (dragging, long-press for the menu) work either way.
    /// </summary>
    public bool TouchMode { get; set; }

    /// <summary>
    /// Turn each track up or down towards the same loudness (measured when it's analyzed), so a
    /// quiet 1938 transfer and a loud remaster play at about the same level. On by default.
    /// </summary>
    public bool MatchLoudness { get; set; } = true;

    /// <summary>Speaker and headphone volume, 0-100.</summary>
    public double MainVolume { get; set; } = 100;
    public double CueVolume { get; set; } = 100;

    /// <summary>What the headphones hear: 0 = what's cued, 1 = what the room hears, between = both.</summary>
    public double PhonesMix { get; set; }

    /// <summary>How many seconds of the song the decks' close-up waveforms show.</summary>
    public double CloseUpSeconds { get; set; } = 8;

    public PlayingDeckLoad LoadOntoPlayingDeck { get; set; } = PlayingDeckLoad.Block;
    public OnAirPause OnAirPause { get; set; } = OnAirPause.Tap;
    public double FadeOutSeconds { get; set; } = 5;

    /// <summary>How far <c>~bpm:</c> searches reach either side, in percent (the tempo slider's ±8% to start with).</summary>
    public double NearBpmPercent { get; set; } = 8;

    /// <summary>Seconds of silence auto-play leaves between songs.</summary>
    public double GapSeconds { get; set; } = 3;

    /// <summary>
    /// Library columns the DJ has hidden, by header. Genre starts hidden: the folder tree already
    /// sorts music by genre and style, and a laptop can't fit every column.
    /// </summary>
    public List<string> HiddenColumns { get; set; } = ["Genre"];

    /// <summary>Each deck's readout mode, by deck number ("1", "2").</summary>
    public Dictionary<string, DeckReadout> DeckReadouts { get; set; } = [];

    public sealed class WindowPlacement
    {
        public int X { get; set; }
        public int Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public bool Maximized { get; set; }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static AppSettings Load(string path)
    {
        try
        {
            if (File.Exists(path))
                return Migrate(JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions) ?? new());
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            System.Diagnostics.Trace.WriteLine($"Ignoring unreadable settings: {ex.Message}");
        }
        return new();
    }

    /// <summary>Upgrades settings saved by older versions.</summary>
    internal static AppSettings Migrate(AppSettings settings)
    {
        // Decks were named A/B before they became 1/2.
        foreach (var (old, renamed) in new[] { ("A", "1"), ("B", "2") })
        {
            if (settings.DeckReadouts.Remove(old, out DeckReadout readout))
                settings.DeckReadouts.TryAdd(renamed, readout);
        }
        return settings;
    }

    public void Save(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            // Write then swap, so a crash mid-save never leaves a truncated file.
            string temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(this, JsonOptions));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Trace.WriteLine($"Could not save settings: {ex.Message}");
        }
    }
}
