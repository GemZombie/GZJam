using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using GZJam.Library;

namespace GZJam.App.ViewModels;

/// <summary>One line of a song's info: what it is, and its value ("" for none, shown as a dash).</summary>
public sealed record InfoRow(string Name, string Value)
{
    public bool IsEmpty => Value.Length == 0;

    public string Shown => IsEmpty ? "—" : Value;

    /// <summary>An empty one is faint.</summary>
    public double Opacity => IsEmpty ? 0.45 : 1;
}

/// <summary>
/// A song's info (#129), from its right-click menu or Ctrl+I: its tags, every other tag the file
/// has, the file itself, and what GZJam knows about it. Only shown for now; nothing is changed.
/// </summary>
public sealed partial class SongInfoViewModel : ObservableObject
{
    private readonly MainWindowViewModel _main;

    public SongInfoViewModel(Track track, MainWindowViewModel main)
    {
        _main = main;
        Track = track;
        ShowAllTags = main.ShowAllTags;
        Tags =
        [
            new("Title", track.Title ?? ""),
            new("Artist", track.Artist ?? ""),
            new("Album", track.Album ?? ""),
            new("Album artist", track.AlbumArtist ?? ""),
            new("Genre", track.Genre ?? ""),
            new("Year", track.Year?.ToString(CultureInfo.CurrentCulture) ?? ""),
            new("Track", track.TrackNumber?.ToString(CultureInfo.CurrentCulture) ?? ""),
            new("BPM", Format.Bpm(track.TagBpm)),
            new("Grouping", track.Grouping ?? ""),
            new("Comment", track.Comment ?? ""),
        ];
        OtherTags = [.. track.Tags.Select(t => new InfoRow(t.Name, t.Value))];
        Location = track.Path;
        File = FileRows(null);
        InGZJam = GZJamRows();
        Problem = track.IsMissing || !System.IO.File.Exists(track.Path) ? "The file isn't there any more." : null;
        Summary = SummaryText(null);
    }

    public Track Track { get; }

    /// <summary>The window's title.</summary>
    public string Heading => Track.DisplayTitle;

    public string? Artist => Track.Artist;

    /// <summary>Under the title, what a DJ looks for first: "225 BPM · 3:48 · MP3 128 kbps · ♥ · High energy".</summary>
    [ObservableProperty]
    public partial string Summary { get; private set; }

    /// <summary>The cover art's cache, for the header.</summary>
    public AlbumArtCache Art => _main.Art;

    /// <summary>GZJam's own fields for the tags every player knows.</summary>
    public IReadOnlyList<InfoRow> Tags { get; }

    /// <summary>The file's other tags, as the library keeps them (#119): Composer, Mood, a Dance tag.</summary>
    public IReadOnlyList<InfoRow> OtherTags { get; }

    public bool HasOtherTags => OtherTags.Count > 0;

    /// <summary>The file's path, to copy or show in the file manager.</summary>
    public string Location { get; }

    /// <summary>The file: its kind of audio once read, length, size, when it changed.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<InfoRow> File { get; private set; }

    /// <summary>What GZJam knows about the song: never in the file.</summary>
    public IReadOnlyList<InfoRow> InGZJam { get; }

    /// <summary>The tags the library leaves out, read from the file: for other programs, or too long.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<InfoRow> LeftOutTags { get; private set; } = [];

    /// <summary>Shows <see cref="LeftOutTags"/> too. Remembered.</summary>
    [ObservableProperty]
    public partial bool ShowAllTags { get; set; }

    partial void OnShowAllTagsChanged(bool value)
    {
        _main.ShowAllTags = value;
        OnPropertyChanged(nameof(LeftOutNote));
        OnPropertyChanged(nameof(IsLeftOutShown));
    }

    public bool IsLeftOutShown => ShowAllTags && LeftOutTags.Count > 0;

    /// <summary>Beside <em>Show all tags</em>: while the file's read, or when it has no others.</summary>
    public string? LeftOutNote => !ShowAllTags ? null
        : !IsRead ? "Reading the file…"
        : Problem is not null ? null
        : LeftOutTags.Count == 0 ? "None: every tag in the file is shown above." : null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LeftOutNote))]
    public partial bool IsRead { get; private set; }

    /// <summary>Why the file couldn't be read, if it couldn't.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LeftOutNote))]
    public partial string? Problem { get; private set; }

    /// <summary>Reads the file, off the UI thread: its kind of audio and the tags the library leaves out.</summary>
    public async Task LoadAsync()
    {
        if (Problem is null)
        {
            string path = Track.Path;
            SongFile? file = await Task.Run(() => SongFile.Read(path));
            if (file is null)
            {
                Problem = "GZJam can't read this file's details.";
            }
            else
            {
                File = FileRows(file);
                Summary = SummaryText(file);
                LeftOutTags = [.. file.LeftOut.Select(t => new InfoRow(t.Name, t.Value))];
                OnPropertyChanged(nameof(IsLeftOutShown));
            }
        }
        IsRead = true;
    }

    private string SummaryText(SongFile? file)
    {
        var parts = new List<string>();
        if (Track.Bpm is { } bpm)
            parts.Add(GZJam.App.Format.Bpm(bpm) + " BPM");
        if (Track.Duration > TimeSpan.Zero)
            parts.Add(GZJam.App.Format.Duration(Track.Duration));
        if (file is not null)
            parts.Add(file.Lossless || file.Kbps == 0 ? file.Format : $"{file.Format} {file.Kbps.ToString(CultureInfo.CurrentCulture)} kbps");
        if (Track.Rating == Rating.Loved)
            parts.Add("♥");
        else if (Track.Rating == Rating.Rejected)
            parts.Add("wouldn't play it");
        if (Track.Energy != Energy.Unset)
            parts.Add(Track.Energy + " energy");
        return string.Join(" · ", parts);
    }

    private IReadOnlyList<InfoRow> FileRows(SongFile? file) =>
    [
        new("Format", file is null ? "" : FormatText(file)),
        new("Length", Track.Duration > TimeSpan.Zero ? GZJam.App.Format.Duration(Track.Duration) : ""),
        new("Size", Track.FileSize > 0 ? GZJam.App.Format.Size(Track.FileSize) : ""),
        new("Changed", Track.ModifiedUtc > DateTime.MinValue ? Track.ModifiedUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) : ""),
    ];

    /// <summary>"MP3 · 320 kbps · 44.1 kHz · stereo", "FLAC · 24-bit · 96 kHz · stereo".</summary>
    internal static string FormatText(SongFile file)
    {
        var parts = new List<string> { file.Format };
        if (file.BitsPerSample > 0)
            parts.Add($"{file.BitsPerSample}-bit");
        else if (file.Lossless)
            parts.Add("lossless");
        else if (file.Kbps > 0)
            parts.Add(file.Kbps.ToString(CultureInfo.CurrentCulture) + " kbps" + (file.Variable ? " (variable)" : ""));
        if (file.SampleRate > 0)
            parts.Add((file.SampleRate / 1000.0).ToString("0.#", CultureInfo.CurrentCulture) + " kHz");
        if (file.Channels > 0)
            parts.Add(file.Channels switch { 1 => "mono", 2 => "stereo", var n => $"{n} channels" });
        return string.Join(" · ", parts);
    }

    private IReadOnlyList<InfoRow> GZJamRows()
    {
        Track t = Track;
        string bpm = t.Bpm is null ? ""
            : GZJam.App.Format.Bpm(t.Bpm) + (t.ManualBpm is not null ? " (typed)"
                : t.BpmRange is { } range && t.DetectedBpm is not null ? $" (detected, in {range})"
                : t.TagBpm is not null ? " (the tag's)"
                : " (detected)");
        string played = t.PlayCount switch
        {
            0 => "Never",
            1 => "Once",
            var n => GZJam.App.Format.Count(n) + " times",
        };
        if (t.LastPlayedUtc is { } last)
            played += ", last " + last.ToLocalTime().ToString("d", CultureInfo.CurrentCulture);
        string cues = "", eq = "", loudness = "", playlists = "";
        if (!t.IsOutsideLibrary || System.IO.File.Exists(t.Path))
        {
            cues = string.Join(", ", _main.Database.GetCues(t.Path).Select(c => $"{c.Number}: {GZJam.App.Format.Duration(c.At)}"));
            EqEditor editor = _main.EqFor(t);
            eq = editor.IsSet ? editor.Describe() : "";
            loudness = editor.LevelText.Replace("Loudness not measured", "Not measured yet");
            playlists = string.Join(", ", _main.Database.GetPlaylistsWith(t.Path));
        }
        return
        [
            new("BPM", bpm),
            new("Rating", t.Rating switch { Rating.Loved => "I'd play it", Rating.Rejected => "I wouldn't play it", _ => "" }),
            new("Energy", t.Energy switch { Energy.High => "High", Energy.Medium => "Medium", Energy.Low => "Low", _ => "" }),
            new("Labels", string.Join(", ", t.Labels)),
            new("Played", played),
            new("Cues", cues),
            new("EQ", eq),
            new("Loudness", loudness),
            new("Playlists", playlists),
        ];
    }
}
