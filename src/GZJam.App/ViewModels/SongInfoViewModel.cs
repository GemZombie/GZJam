using System.Collections.ObjectModel;
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
/// A song's info (#129), or several songs' at once, from their right-click menu or Ctrl+I: their
/// tags, every other tag the files have, the file, and what GZJam knows. The tags, rating, energy
/// and labels are edited in place, and saved together: kept in GZJam's library, or written into
/// the files too when the DJ chooses (<see cref="SettingsViewModel.WriteTagsToFiles"/>).
/// </summary>
public sealed partial class SongInfoViewModel : ObservableObject
{
    private readonly MainWindowViewModel _main;

    public SongInfoViewModel(Track track, MainWindowViewModel main) : this([track], main)
    {
    }

    public SongInfoViewModel(IReadOnlyList<Track> tracks, MainWindowViewModel main)
    {
        if (tracks.Count == 0)
            throw new ArgumentException("A song's info needs a song.", nameof(tracks));
        _main = main;
        Tracks = [.. tracks.DistinctBy(t => t.Path)];
        Track = Tracks[0];
        ShowAllTags = main.ShowAllTags;
        Location = Track.Path;
        Problem = !IsSingle ? null
            : Track.IsMissing || !System.IO.File.Exists(Track.Path) ? "The file isn't there any more." : null;
        ReadOnlyReason = Problem is not null ? null
            : Tracks.Any(t => t.IsOutsideLibrary)
                ? IsSingle ? "This song isn't in your library, so its tags can't be changed here: add its folder in Settings › Music folders."
                    : "Some of these songs aren't in your library, so their tags can't be changed here."
            : Tracks.Any(t => t.IsMissing) ? "Some of these songs' files aren't there any more, so their tags can't be changed here."
            : null;
        IsEditable = Problem is null && ReadOnlyReason is null;
        Build();
    }

    /// <summary>The songs, each once; the first is the one shown in the header.</summary>
    public IReadOnlyList<Track> Tracks { get; }

    public Track Track { get; }

    public bool IsSingle => Tracks.Count == 1;

    public bool IsSeveral => !IsSingle;

    /// <summary>The tags, rating, energy and labels can be changed: every song is in the library, its file there.</summary>
    public bool IsEditable { get; }

    /// <summary>Why they can't be, if they can't (but the file can be read).</summary>
    public string? ReadOnlyReason { get; }

    /// <summary>The window's title, and the header.</summary>
    public string Heading => IsSingle ? Track.DisplayTitle : $"{Format.Count(Tracks.Count)} songs";

    /// <summary>The artist, under the title; for several songs, the one they share, if they do.</summary>
    public string? Artist => Tracks.Select(t => t.Artist).Distinct().Count() == 1 ? Track.Artist : null;

    /// <summary>Under the title, what a DJ looks for first: "225 BPM · 3:48 · MP3 128 kbps · ♥ · High energy".</summary>
    [ObservableProperty]
    public partial string Summary { get; private set; } = "";

    /// <summary>The cover art's cache, for the header.</summary>
    public AlbumArtCache Art => _main.Art;

    /// <summary>The settings, for writing to the files, which the window can turn on or off.</summary>
    public SettingsViewModel Settings => _main.Settings;

    // ───────────── The tags ─────────────

    /// <summary>GZJam's own fields for the tags every player knows.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<TagField> Tags { get; private set; } = [];

    /// <summary>The files' other tags, as the library keeps them (#119): Composer, Mood, a Dance tag; and ones the DJ adds.</summary>
    public ObservableCollection<TagField> OtherTags { get; } = [];

    public bool HasOtherTags => OtherTags.Count > 0;

    /// <summary>A tag to add: its name ("Dance"), and its value.</summary>
    [ObservableProperty]
    public partial string NewTagName { get; set; } = "";

    [ObservableProperty]
    public partial string NewTagValue { get; set; } = "";

    /// <summary>Why the tag to add can't be one.</summary>
    [ObservableProperty]
    public partial string? NewTagError { get; private set; }

    partial void OnNewTagNameChanged(string value) => NewTagError = null;

    /// <summary>Adds a tag to the songs (saved with the rest), or sets it, if they have it already.</summary>
    public void AddTag()
    {
        string typed = NewTagName.Trim();
        if (typed.Length == 0)
            return;
        if (SongTags.NameFor(typed) is not { } name)
        {
            NewTagError = $"GZJam can't keep a tag called \"{typed}\": it's one of the tags above, or one only other programs use.";
            return;
        }
        string key = SongTags.KeyOf(name);
        if (OtherTags.FirstOrDefault(t => SongTags.KeyOf(SongFields.TagName(t.Field)!) == key) is { } have)
        {
            have.Text = NewTagValue.Trim();
        }
        else
        {
            var field = new TagField(SongFields.ForTag(name), name, [.. Tracks.Select(_ => (string?)null)], edited: false, IsEditable, isNew: true)
            {
                Text = NewTagValue.Trim(),
            };
            field.Changed += Changed;
            OtherTags.Add(field);
            OnPropertyChanged(nameof(HasOtherTags));
        }
        NewTagName = NewTagValue = "";
        Changed();
    }

    /// <summary>Any of the tags shown is GZJam's own edit, over what a file says.</summary>
    public bool HasLibraryEdits => Tags.Concat(OtherTags).Any(t => t.IsLibraryEdit);

    // ───────────── GZJam's own: rating, energy, labels ─────────────

    public static IReadOnlyList<string> RatingNames { get; } = ["—", "I'd play it", "I wouldn't play it"];

    public static IReadOnlyList<string> EnergyNames { get; } = ["—", "Low", "Medium", "High"];

    private static readonly Rating[] RatingOrder = [Rating.Unrated, Rating.Loved, Rating.Rejected];

    private static readonly Energy[] EnergyOrder = [Energy.Unset, Energy.Low, Energy.Medium, Energy.High];

    private const string SeveralChoice = "(several)";

    /// <summary>The choices, with "(several)" last when the songs differ.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<string> RatingChoices { get; private set; } = RatingNames;

    [ObservableProperty]
    public partial IReadOnlyList<string> EnergyChoices { get; private set; } = EnergyNames;

    [ObservableProperty]
    public partial int RatingIndex { get; set; }

    [ObservableProperty]
    public partial int EnergyIndex { get; set; }

    private int _ratingOriginal, _energyOriginal;

    partial void OnRatingIndexChanged(int value) => Changed();

    partial void OnEnergyIndexChanged(int value) => Changed();

    /// <summary>The labels on any of the songs, and any the DJ adds.</summary>
    public ObservableCollection<LabelChoice> Labels { get; } = [];

    [ObservableProperty]
    public partial string NewLabel { get; set; } = "";

    /// <summary>Puts a label on the songs (saved with the rest); the name of one that exists, in any case, is that one.</summary>
    public void AddLabel()
    {
        string typed = NewLabel.Trim();
        if (typed.Length == 0)
            return;
        if (Labels.FirstOrDefault(l => l.Name.Equals(typed, StringComparison.OrdinalIgnoreCase)) is { } have)
        {
            have.IsChecked = true;
        }
        else
        {
            string name = _main.GetLabels().FirstOrDefault(l => l.Name.Equals(typed, StringComparison.OrdinalIgnoreCase))?.Name ?? typed;
            var label = new LabelChoice(name, false, isNew: true) { IsChecked = true };
            label.Changed += Changed;
            Labels.Add(label);
        }
        NewLabel = "";
        Changed();
    }

    // ───────────── The file, and what GZJam knows ─────────────

    public string FileHeading => IsSingle ? "FILE" : "FILES";

    /// <summary>The file's path, to copy or show in the file manager.</summary>
    public string Location { get; }

    /// <summary>The file: its kind of audio once read, length, size, when it changed. For several songs, all of them.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<InfoRow> File { get; private set; } = [];

    /// <summary>What else GZJam knows about the song (one song only): never in the file.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<InfoRow> InGZJam { get; private set; } = [];

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
    public string? LeftOutNote => !ShowAllTags || !IsSingle ? null
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

    private SongFile? _file;

    /// <summary>Reads the file, off the UI thread: its kind of audio and the tags the library leaves out.</summary>
    public async Task LoadAsync()
    {
        if (Problem is null && IsSingle)
        {
            string path = Track.Path;
            SongFile? file = await Task.Run(() => SongFile.Read(path));
            if (file is null)
            {
                Problem = "GZJam can't read this file's details.";
            }
            else
            {
                _file = file;
                File = FileRows(file);
                Summary = SummaryText(file);
                LeftOutTags = [.. file.LeftOut.Select(t => new InfoRow(t.Name, t.Value))];
                OnPropertyChanged(nameof(IsLeftOutShown));
            }
        }
        IsRead = true;
    }

    // ───────────── Saving ─────────────

    /// <summary>Something's been changed and not saved.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    public partial bool HasChanges { get; private set; }

    /// <summary>Saving now (writing files can take a moment).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    public partial bool IsSaving { get; private set; }

    /// <summary>Something's changed, and all of it can be saved (no year that isn't one).</summary>
    public bool CanSave => HasChanges && !IsSaving && IsEditable && !Tags.Concat(OtherTags).Any(t => t.Error is not null);

    /// <summary>What the last save did, said under the tags.</summary>
    [ObservableProperty]
    public partial string? SavedText { get; private set; }

    private Func<Task>? _undo;

    /// <summary>The last save can be undone.</summary>
    [ObservableProperty]
    public partial bool CanUndo { get; private set; }

    /// <summary>
    /// Asks before changing several songs at once: (heading, message, the button). Set by the window;
    /// with none, it's done without asking.
    /// </summary>
    public Func<string, string, string, Task<bool>>? ConfirmAsync { get; set; }

    private void Changed()
    {
        HasChanges = Tags.Concat(OtherTags).Any(t => t.IsChanged) || Labels.Any(l => l.IsChanged)
            || RatingIndex >= 0 && RatingIndex != _ratingOriginal || EnergyIndex >= 0 && EnergyIndex != _energyOriginal;
        OnPropertyChanged(nameof(CanSave));
    }

    /// <summary>The changed tags, by field: their new values (null to take a tag off).</summary>
    internal Dictionary<string, string?> TagChanges() =>
        Tags.Concat(OtherTags).Where(t => t.IsChanged).ToDictionary(t => t.Field, t => t.NewValue, StringComparer.Ordinal);

    /// <summary>
    /// Saves everything changed, on every song: tags, rating, energy, labels. For several songs,
    /// asks first, saying how many. Undo (here, and in the main window's notice) puts it all back.
    /// False if nothing was saved.
    /// </summary>
    public async Task<bool> SaveAsync()
    {
        if (!CanSave)
            return false;
        var changes = TagChanges();
        var labels = Labels.Where(l => l.IsChanged).ToList();
        Rating? rating = RatingIndex != _ratingOriginal && RatingIndex >= 0 && RatingIndex < RatingOrder.Length ? RatingOrder[RatingIndex] : null;
        Energy? energy = EnergyIndex != _energyOriginal && EnergyIndex >= 0 && EnergyIndex < EnergyOrder.Length ? EnergyOrder[EnergyIndex] : null;
        if (IsSeveral && ConfirmAsync is { } confirm)
        {
            var what = changes.Keys.Select(SongFields.Describe).ToList();
            if (rating is not null)
                what.Add("Rating");
            if (energy is not null)
                what.Add("Energy");
            if (labels.Count > 0)
                what.Add("Labels");
            string count = Format.Count(Tracks.Count);
            if (!await confirm($"Change {count} songs?",
                    $"{JoinNames(what)} will change on all {count} songs" +
                    (changes.Count > 0 && Settings.WriteTagsToFiles ? ", in their files too" : "") + ". Undo puts them back.",
                    $"Change {count} songs"))
                return false;
        }

        IsSaving = true;
        try
        {
            // GZJam's own marks: as they were on each song, for Undo.
            var marks = Tracks.Select(t => (Track: t, t.Rating, t.Energy, t.Labels)).ToList();
            if (rating is { } r)
                _main.SetRating(Tracks, r);
            if (energy is { } e)
                _main.SetEnergy(Tracks, e);
            foreach (LabelChoice label in labels.Where(l => l.IsChecked is not null))
                _main.SetLabel(Tracks, label.Name, label.IsChecked == true);

            TagEditOutcome? outcome = changes.Count > 0 ? await _main.EditTagsAsync(Tracks, changes) : null;
            string message = outcome?.Message
                ?? (IsSingle ? $"Saved \"{Track.DisplayTitle}\"." : $"Saved {Format.Count(Tracks.Count)} songs.");

            bool undone = false;
            async Task Undo()
            {
                if (undone)
                    return;
                undone = true;
                if (outcome is not null)
                    await outcome.Undo();
                foreach (var group in marks.Where(_ => rating is not null).GroupBy(m => m.Rating))
                    _main.SetRating([.. group.Select(m => m.Track)], group.Key);
                foreach (var group in marks.Where(_ => energy is not null).GroupBy(m => m.Energy))
                    _main.SetEnergy([.. group.Select(m => m.Track)], group.Key);
                foreach (LabelChoice label in labels.Where(l => l.IsChecked is not null))
                {
                    var had = marks.Where(m => m.Labels.Contains(label.Name, StringComparer.OrdinalIgnoreCase)).Select(m => m.Track).ToList();
                    var hadNot = marks.Select(m => m.Track).Except(had).ToList();
                    if (had.Count > 0)
                        _main.SetLabel(had, label.Name, true);
                    if (hadNot.Count > 0)
                        _main.SetLabel(hadNot, label.Name, false);
                }
            }
            _undo = Undo;
            CanUndo = true;
            SavedText = message;
            _main.ShowNotice(message, seconds: 8, undo: () => _ = UndoAsync());
            Build();
            return true;
        }
        finally
        {
            IsSaving = false;
        }
    }

    /// <summary>Puts back what the last save changed, on every song.</summary>
    public async Task UndoAsync()
    {
        if (_undo is not { } undo)
            return;
        _undo = null;
        CanUndo = false;
        await undo();
        SavedText = "Undone: as it was before.";
        Build();
    }

    /// <summary>Puts every field back as it was when opened (or last saved).</summary>
    public void Cancel()
    {
        foreach (TagField field in Tags.Concat(OtherTags.ToList()))
            field.Revert();
        foreach (TagField added in OtherTags.Where(t => t.IsNew).ToList())
            OtherTags.Remove(added);
        foreach (LabelChoice label in Labels.ToList())
        {
            label.Revert();
            if (label.IsNew)
                Labels.Remove(label);
        }
        RatingIndex = _ratingOriginal;
        EnergyIndex = _energyOriginal;
        NewTagName = NewTagValue = NewLabel = "";
        OnPropertyChanged(nameof(HasOtherTags));
        Changed();
    }

    /// <summary>Forgets GZJam's own edits to these songs' tags: they show what their files say again. With Undo.</summary>
    public async Task UseFileTagsAsync()
    {
        Func<Task> undo = await _main.UseFileTagsAsync(Tracks);
        _undo = undo;
        CanUndo = true;
        SavedText = "These songs show what their files say again.";
        _main.ShowNotice(SavedText, seconds: 8, undo: () => _ = UndoAsync());
        Build();
    }

    private static string JoinNames(IReadOnlyList<string> names) => names.Count switch
    {
        0 => "Nothing",
        1 => names[0],
        _ => string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1],
    };

    // ───────────── Building what's shown ─────────────

    /// <summary>Shows the songs as they are now: every field as saved, nothing changed.</summary>
    private void Build()
    {
        bool editable = IsEditable;
        ILookup<string, string> edited = _main.Database.GetTagEditFields(Tracks.Select(t => t.Path));
        bool IsEdited(string field) => Tracks.Any(t => edited[t.Path].Any(f => SongFields.Same(f, field)));

        foreach (TagField old in Tags.Concat(OtherTags))
            old.Changed -= Changed;
        Tags = [.. SongFields.Usual.Select(f => new TagField(f, SongFields.Describe(f),
            [.. Tracks.Select(t => SongFields.ValueOf(t, f))], IsEdited(f), editable))];

        OtherTags.Clear();
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (SongTag tag in Tracks.SelectMany(t => t.Tags))
            names.TryAdd(tag.Key, tag.Name);
        foreach (var (key, name) in names.OrderBy(n => n.Value, StringComparer.OrdinalIgnoreCase))
        {
            string field = SongFields.ForTag(name);
            OtherTags.Add(new TagField(field, name, [.. Tracks.Select(t => t.Tag(key))], IsEdited(field), editable));
        }
        foreach (TagField field in Tags.Concat(OtherTags))
            field.Changed += Changed;
        OnPropertyChanged(nameof(HasOtherTags));
        OnPropertyChanged(nameof(HasLibraryEdits));

        var ratings = Tracks.Select(t => t.Rating).Distinct().ToList();
        RatingChoices = ratings.Count > 1 ? [.. RatingNames, SeveralChoice] : RatingNames;
        _ratingOriginal = ratings.Count > 1 ? RatingNames.Count : Array.IndexOf(RatingOrder, ratings[0]);
        RatingIndex = _ratingOriginal;
        var energies = Tracks.Select(t => t.Energy).Distinct().ToList();
        EnergyChoices = energies.Count > 1 ? [.. EnergyNames, SeveralChoice] : EnergyNames;
        _energyOriginal = energies.Count > 1 ? EnergyNames.Count : Array.IndexOf(EnergyOrder, energies[0]);
        EnergyIndex = _energyOriginal;

        foreach (LabelChoice old in Labels)
            old.Changed -= Changed;
        Labels.Clear();
        foreach (string label in Tracks.SelectMany(t => t.Labels).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase))
        {
            int on = Tracks.Count(t => t.Labels.Contains(label, StringComparer.OrdinalIgnoreCase));
            var choice = new LabelChoice(label, on == Tracks.Count ? true : null);
            choice.Changed += Changed;
            Labels.Add(choice);
        }
        NewTagName = NewTagValue = NewLabel = "";

        File = IsSingle ? FileRows(_file) : SeveralFilesRows();
        InGZJam = IsSingle ? GZJamRows() : [];
        Summary = SummaryText(_file);
        OnPropertyChanged(nameof(Heading));
        OnPropertyChanged(nameof(Artist));
        Changed();
    }

    private string SummaryText(SongFile? file)
    {
        var parts = new List<string>();
        if (!IsSingle)
        {
            // The heading says how many: how long they play for, and the BPMs they span.
            var length = TimeSpan.FromTicks(Tracks.Sum(t => t.Duration.Ticks));
            if (length > TimeSpan.Zero)
                parts.Add(Format.Duration(length));
            var bpms = Tracks.Select(t => t.Bpm).OfType<double>().ToList();
            if (bpms.Count > 0)
                parts.Add(bpms.Min() == bpms.Max() ? Format.Bpm(bpms[0]) + " BPM" : $"{Format.Bpm(bpms.Min())}–{Format.Bpm(bpms.Max())} BPM");
            return string.Join(" · ", parts);
        }
        if (Track.Bpm is { } bpm)
            parts.Add(Format.Bpm(bpm) + " BPM");
        if (Track.Duration > TimeSpan.Zero)
            parts.Add(Format.Duration(Track.Duration));
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
        new("Length", Track.Duration > TimeSpan.Zero ? Format.Duration(Track.Duration) : ""),
        new("Size", Track.FileSize > 0 ? Format.Size(Track.FileSize) : ""),
        new("Changed", Track.ModifiedUtc > DateTime.MinValue ? Track.ModifiedUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) : ""),
    ];

    /// <summary>Several songs' files: how many, where, how long and how big in all.</summary>
    private IReadOnlyList<InfoRow> SeveralFilesRows()
    {
        var folders = Tracks.Select(t => System.IO.Path.GetDirectoryName(t.Path) ?? "").Distinct(StringComparer.Ordinal).ToList();
        var length = TimeSpan.FromTicks(Tracks.Sum(t => t.Duration.Ticks));
        long size = Tracks.Sum(t => t.FileSize);
        return
        [
            new("Songs", Format.Count(Tracks.Count)),
            new("Folder", folders.Count == 1 ? folders[0] : $"{Format.Count(folders.Count)} folders"),
            new("Length", length > TimeSpan.Zero ? Format.Duration(length) : ""),
            new("Size", size > 0 ? Format.Size(size) : ""),
        ];
    }

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

    /// <summary>What else GZJam knows: the BPM it plays at, plays, cues, EQ, loudness, playlists.</summary>
    private IReadOnlyList<InfoRow> GZJamRows()
    {
        Track t = Track;
        string bpm = t.Bpm is null ? ""
            : Format.Bpm(t.Bpm) + (t.ManualBpm is not null ? " (typed)"
                : t.BpmRange is { } range && t.DetectedBpm is not null ? $" (detected, in {range})"
                : t.TagBpm is not null ? " (the tag's)"
                : " (detected)");
        string played = t.PlayCount switch
        {
            0 => "Never",
            1 => "Once",
            var n => Format.Count(n) + " times",
        };
        if (t.LastPlayedUtc is { } last)
            played += ", last " + last.ToLocalTime().ToString("d", CultureInfo.CurrentCulture);
        string cues = "", eq = "", loudness = "", playlists = "";
        if (!t.IsOutsideLibrary || System.IO.File.Exists(t.Path))
        {
            cues = string.Join(", ", _main.Database.GetCues(t.Path).Select(c => $"{c.Number}: {Format.Duration(c.At)}"));
            EqEditor editor = _main.EqFor(t);
            eq = editor.IsSet ? editor.Describe() : "";
            loudness = editor.LevelText.Replace("Loudness not measured", "Not measured yet");
            playlists = string.Join(", ", _main.Database.GetPlaylistsWith(t.Path));
        }
        return
        [
            new("BPM", bpm),
            new("Played", played),
            new("Cues", cues),
            new("EQ", eq),
            new("Loudness", loudness),
            new("Playlists", playlists),
        ];
    }
}
