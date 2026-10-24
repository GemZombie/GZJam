using System.ComponentModel;

namespace GZJam.Library;

/// <summary>One audio file in the library, with the tag data read from it.</summary>
public sealed class Track : INotifyPropertyChanged
{
    public long Id { get; init; }
    public long RootId { get; init; }

    /// <summary>Full path on disk.</summary>
    public required string Path { get; init; }

    /// <summary>Folder relative to its library root, '/'-separated; empty for files directly in the root.</summary>
    public required string Folder { get; init; }

    public required string FileName { get; init; }
    public long FileSize { get; init; }
    public DateTime ModifiedUtc { get; init; }

    public string? Title { get; init; }
    public string? Artist { get; init; }
    public string? Album { get; init; }
    public string? AlbumArtist { get; init; }
    public string? Genre { get; init; }
    public string? Comment { get; init; }
    public int? Year { get; init; }
    public int? TrackNumber { get; init; }

    /// <summary>BPM from the file's tag. Always preferred to a detected one.</summary>
    public double? TagBpm { get; init; }

    /// <summary>
    /// BPM found by analysis, as detected. Set when analysis finishes, which may be after the
    /// track is on screen.
    /// </summary>
    public double? DetectedBpm
    {
        get;
        set
        {
            if (field == value)
                return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DetectedBpm)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Bpm)));
        }
    }

    /// <summary>
    /// The tempo range the DJ chose for this track because detection got it wrong; null for
    /// automatic. Choosing one also means the detected BPM is preferred to the tag's.
    /// </summary>
    public BpmRange? BpmRange
    {
        get;
        set
        {
            if (field == value)
                return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BpmRange)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Bpm)));
        }
    }

    /// <summary>A BPM the DJ typed (or locked in Mixxx); it wins over the tag and detection.</summary>
    public double? ManualBpm
    {
        get;
        set
        {
            if (field == value)
                return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ManualBpm)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Bpm)));
        }
    }

    /// <summary>The BPM to show: one typed by the DJ, else the tag's, unless the DJ asked for detection in a range.</summary>
    public double? Bpm => ManualBpm ?? (BpmRange is not null ? DetectedBpm ?? TagBpm : TagBpm ?? DetectedBpm);

    public TimeSpan Duration { get; init; }

    /// <summary>The DJ's rating. Kept in the library database, never written to the file.</summary>
    public Rating Rating
    {
        get;
        set
        {
            if (field == value)
                return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Rating)));
        }
    }

    /// <summary>The DJ's energy mark. Kept in the library database, never written to the file.</summary>
    public Energy Energy
    {
        get;
        set
        {
            if (field == value)
                return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Energy)));
        }
    }

    /// <summary>The DJ's labels on this song ("Lindy", "Female vocals"), alphabetically. Kept in the library database.</summary>
    public IReadOnlyList<string> Labels
    {
        get;
        set
        {
            if (field.SequenceEqual(value))
                return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Labels)));
        }
    } = [];

    /// <summary>When the room last heard this file (from History), or null if never.</summary>
    public DateTime? LastPlayedUtc
    {
        get;
        set
        {
            if (field == value)
                return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LastPlayedUtc)));
        }
    }

    /// <summary>How many times the room has heard it, across all sessions.</summary>
    public int PlayCount
    {
        get;
        set
        {
            if (field == value)
                return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PlayCount)));
        }
    }

    /// <summary>Played in the session going on now (tonight's gig): shown dimmed so it isn't repeated.</summary>
    public bool IsPlayedTonight
    {
        get;
        set
        {
            if (field == value)
                return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsPlayedTonight)));
        }
    }

    /// <summary>
    /// Shown as part of a playlist: which entry this row is (a song can be in a playlist twice,
    /// each its own row), and its number in the playlist, from 1. Null in the rest of the library.
    /// </summary>
    public long? PlaylistEntryId { get; private set; }

    public int? PlaylistNumber { get; private set; }

    internal Track InPlaylist(long entryId, int number)
    {
        (PlaylistEntryId, PlaylistNumber) = (entryId, number);
        return this;
    }

    /// <summary>Title tag, or the file name when the tag is missing.</summary>
    public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? System.IO.Path.GetFileNameWithoutExtension(FileName) : Title;

    public event PropertyChangedEventHandler? PropertyChanged;

    public override string ToString() => $"{DisplayTitle} - {Artist}";
}

/// <summary>A folder added to the library by the user.</summary>
public sealed record LibraryRoot(long Id, string Path)
{
    public string Name => System.IO.Path.GetFileName(Path.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar)) is { Length: > 0 } name
        ? name
        : Path;
}

/// <summary>Limits a search to a folder and everything beneath it.</summary>
/// <param name="Folder">Relative to the root, '/'-separated; empty means the whole root.</param>
public sealed record FolderScope(long RootId, string Folder);
