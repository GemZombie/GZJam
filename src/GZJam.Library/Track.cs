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

    /// <summary>The BPM to show: the tag's, unless the DJ asked for detection in a range.</summary>
    public double? Bpm => BpmRange is not null ? DetectedBpm ?? TagBpm : TagBpm ?? DetectedBpm;

    public TimeSpan Duration { get; init; }

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
