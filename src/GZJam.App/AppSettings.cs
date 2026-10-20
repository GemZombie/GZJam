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

    public PlayingDeckLoad LoadOntoPlayingDeck { get; set; } = PlayingDeckLoad.Block;
    public OnAirPause OnAirPause { get; set; } = OnAirPause.Tap;
    public double FadeOutSeconds { get; set; } = 5;

    /// <summary>Each deck's readout mode, by deck letter.</summary>
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
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions) ?? new();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            System.Diagnostics.Trace.WriteLine($"Ignoring unreadable settings: {ex.Message}");
        }
        return new();
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
