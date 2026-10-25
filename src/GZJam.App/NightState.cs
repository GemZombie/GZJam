using System.Text.Json;

namespace GZJam.App;

/// <summary>
/// The night so far: the queue and what's on the decks, kept beside the library as they change,
/// so a crash, or a laptop that dies or restarts mid-dance, can bring them back (#96). After a
/// normal close they come back too, the decks at the start of their songs (#117).
/// </summary>
public sealed class NightState
{
    public const string FileName = "night.json";

    /// <summary>GZJam closed as it should; false while it runs, so a crash leaves it false.</summary>
    public bool ClosedNormally { get; set; }

    public DateTime SavedUtc { get; set; }

    /// <summary>The queue's songs, in order, by file.</summary>
    public List<string> Queue { get; set; } = [];

    /// <summary>The decks with a song on them.</summary>
    public List<DeckState> Decks { get; set; } = [];

    /// <summary>Auto-play was on (it comes back off: the DJ turns it on again).</summary>
    public bool AutoPlay { get; set; }

    public sealed class DeckState
    {
        /// <summary>0 for Deck 1, and so on.</summary>
        public int Deck { get; set; }
        public string Path { get; set; } = "";
        public double PositionSeconds { get; set; }
        public double TempoPercent { get; set; }
        public bool Cue { get; set; }
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>The night saved last, or null if there's none (or it can't be read).</summary>
    public static NightState? Load(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<NightState>(File.ReadAllText(path), JsonOptions) : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Log.Error($"Couldn't read the night saved in {path}", ex);
            return null;
        }
    }

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    /// <summary>
    /// Writes <paramref name="json"/> crash-safely: to a temporary file, flushed to the disk, then
    /// swapped in, so the power going mid-write leaves the last night whole.
    /// </summary>
    public static void Write(string path, string json)
    {
        try
        {
            string temp = path + ".tmp";
            using (var file = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(file))
            {
                writer.Write(json);
                writer.Flush();
                file.Flush(flushToDisk: true);
            }
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error($"Couldn't save the night in {path}", ex);
        }
    }
}
