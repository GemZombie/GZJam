namespace GZJam.Library;

/// <summary>One audio file in the library, with the tag data read from it.</summary>
public sealed class Track
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
    public double? Bpm { get; init; }
    public TimeSpan Duration { get; init; }

    /// <summary>Title tag, or the file name when the tag is missing.</summary>
    public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? System.IO.Path.GetFileNameWithoutExtension(FileName) : Title;

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
