using CommunityToolkit.Mvvm.ComponentModel;
using GZJam.Library;

namespace GZJam.App.ViewModels;

/// <summary>A song in Fill tags from file name's preview: its file name, and what the pattern makes of it.</summary>
public sealed record FillRow(string FileName, string Says, bool Fits);

/// <summary>
/// Fill tags from file name (#273), in song info: a pattern such as <c>{year} {artist} - {title}</c>,
/// guessed from the songs' names, with a preview of what each song's name gives, then the tags
/// filled (only the empty ones, unless the DJ says), each song its own, saved as any edit is:
/// in GZJam's library, or written into the files, with Undo.
/// </summary>
public sealed partial class SongInfoViewModel
{
    /// <summary>How many songs' names the preview shows.</summary>
    private const int FillShown = 6;

    [ObservableProperty]
    public partial bool IsFillOpen { get; set; }

    [ObservableProperty]
    public partial string FillPattern { get; set; } = "";

    /// <summary>Only tags the songs don't have; off: the name's over what's there.</summary>
    [ObservableProperty]
    public partial bool FillOnlyEmpty { get; set; } = true;

    [ObservableProperty]
    public partial IReadOnlyList<FillRow> FillPreview { get; private set; } = [];

    /// <summary>"38 of 40 songs fit it: their artist and title get filled." Or what's wrong.</summary>
    [ObservableProperty]
    public partial string FillSummary { get; private set; } = "";

    [ObservableProperty]
    public partial bool CanFill { get; private set; }

    /// <summary>The parts a pattern can have, for its tip.</summary>
    public string FillHelp =>
        "Write how the names are laid out, with {artist}, {title}, {year}, {track} and {album} where they are, "
        + "and {skip} for anything to leave out: \"{year} {artist} - {title}\" for \"1924 Ma Rainey - Lucky Rock Blues\".";

    /// <summary>Opens Fill tags from file name, with the pattern most of the songs' names fit.</summary>
    public void OpenFill()
    {
        if (FillPattern.Length == 0)
            FillPattern = FileNameTags.BestPattern(Tracks.Select(t => t.FileName));
        IsFillOpen = true;
        PreviewFill();
    }

    partial void OnFillPatternChanged(string value) => PreviewFill();

    partial void OnFillOnlyEmptyChanged(bool value) => PreviewFill();

    partial void OnHasChangesChanged(bool value) => PreviewFill();

    /// <summary>Each song's changes by the pattern: those its name fits, only the tags it lacks unless told.</summary>
    private List<(Track Track, IReadOnlyDictionary<string, string?> Changes)> FillChanges()
    {
        var edits = new List<(Track, IReadOnlyDictionary<string, string?>)>();
        foreach (Track track in Tracks)
        {
            if (FileNameTags.Match(FillPattern, track.FileName) is not { } says)
                continue;
            var changes = new Dictionary<string, string?>(StringComparer.Ordinal);
            void Fill(string field, string? value)
            {
                if (value is null || (FillOnlyEmpty && !string.IsNullOrWhiteSpace(SongFields.ValueOf(track, field))))
                    return;
                if (SongFields.ValueOf(track, field) != value)
                    changes[field] = value;
            }
            Fill(SongFields.Artist, says.Artist);
            Fill(SongFields.Title, says.Title);
            Fill(SongFields.Album, says.Album);
            Fill(SongFields.Year, says.Year?.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Fill(SongFields.Track, says.Track?.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (changes.Count > 0)
                edits.Add((track, changes));
        }
        return edits;
    }

    private void PreviewFill()
    {
        if (!IsFillOpen)
            return;
        if (!FileNameTags.IsValid(FillPattern))
        {
            FillPreview = [];
            FillSummary = "The pattern needs a part to fill, each once: " + FillHelp;
            CanFill = false;
            return;
        }
        FillPreview = [.. Tracks.Take(FillShown).Select(t => FileNameTags.Match(FillPattern, t.FileName) is { } says
            ? new FillRow(t.FileName, Describe(says), true)
            : new FillRow(t.FileName, "doesn't fit the pattern", false))];
        int fit = Tracks.Count(t => FileNameTags.Match(FillPattern, t.FileName) is not null);
        int changing = FillChanges().Count;
        string songs = Tracks.Count == 1 ? "The song's name" : $"{Format.Count(fit)} of {Format.Count(Tracks.Count)} songs' names";
        FillSummary = fit == 0 ? (Tracks.Count == 1 ? "The song's name doesn't fit the pattern." : "None of the songs' names fit the pattern.")
            : changing == 0 ? $"{songs} fit it, but their tags already say so{(FillOnlyEmpty ? " (only empty tags are filled)" : "")}."
            : HasChanges ? "Save or cancel the changes above first."
            : $"{songs} fit it: {(changing == 1 ? "1 song" : $"{Format.Count(changing)} songs")} get{(changing == 1 ? "s" : "")} {(FillOnlyEmpty ? "the tags it's missing" : "its tags")} filled.";
        CanFill = IsEditable && changing > 0 && !HasChanges && !IsSaving;
    }

    private static string Describe(NameTags says) => string.Join("  ·  ", new[]
    {
        says.Artist is { } artist ? $"Artist: {artist}" : null,
        says.Title is { } title ? $"Title: {title}" : null,
        says.Album is { } album ? $"Album: {album}" : null,
        says.Year is { } year ? $"Year: {year}" : null,
        says.Track is { } track ? $"Track: {track}" : null,
    }.OfType<string>());

    /// <summary>Fills the tags from the names, asking first for several songs; Undo puts them back.</summary>
    public async Task<bool> FillAsync()
    {
        if (!CanFill)
            return false;
        var edits = FillChanges();
        if (edits.Count > 1 && ConfirmAsync is { } confirm)
        {
            string count = Format.Count(edits.Count);
            if (!await confirm($"Fill {count} songs' tags from their file names?",
                    $"Each song gets what its own name says{(FillOnlyEmpty ? ", where its tag is empty" : "")}"
                    + (Settings.WriteTagsToFiles ? ", in its file too" : "") + ". Undo puts them back.",
                    $"Fill {count} songs"))
                return false;
        }
        IsSaving = true;
        try
        {
            TagEditOutcome? outcome = await _main.EditTagsAsync(edits);
            if (outcome is null)
                return false;
            _undo = outcome.Undo;
            CanUndo = true;
            SavedText = $"Filled from the file names. {outcome.Message}";
            _main.ShowNotice(SavedText, seconds: 8, undo: () => _ = UndoAsync());
            Build();
            IsFillOpen = false;
            return true;
        }
        finally
        {
            IsSaving = false;
        }
    }
}
