using CommunityToolkit.Mvvm.ComponentModel;
using GZJam.Library;

namespace GZJam.App.ViewModels;

/// <summary>
/// Settings › Import from Mixxx: finds Mixxx's library, shows what would come over (and what
/// can't, because GZJam doesn't have those folders), and brings over what the DJ ticks.
/// </summary>
public sealed partial class MixxxImportViewModel(LibraryDatabase database, Action<MixxxImportResult> imported) : ObservableObject
{
    private MixxxImport? _plan;

    /// <summary>The Mixxx database being read, or null before one is found.</summary>
    [ObservableProperty]
    public partial string? Source { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanImport))]
    public partial bool IsBusy { get; private set; }

    /// <summary>What's going on, or why nothing can be imported.</summary>
    [ObservableProperty]
    public partial string Status { get; private set; } = "Looking for Mixxx's library…";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanImport))]
    public partial bool HasPlan { get; private set; }

    [ObservableProperty] public partial string RatingsText { get; private set; } = "";
    [ObservableProperty] public partial string CratesText { get; private set; } = "";
    [ObservableProperty] public partial string PlaylistsText { get; private set; } = "";
    [ObservableProperty] public partial string HistoryText { get; private set; } = "";
    [ObservableProperty] public partial string BpmsText { get; private set; } = "";
    [ObservableProperty] public partial string HotCuesText { get; private set; } = "";

    /// <summary>Mixxx songs GZJam doesn't have, and where they are; empty if it has them all.</summary>
    [ObservableProperty] public partial string MissingText { get; private set; } = "";

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanImport))] public partial bool Ratings { get; set; } = true;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanImport))] public partial bool Crates { get; set; } = true;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanImport))] public partial bool Playlists { get; set; } = true;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanImport))] public partial bool History { get; set; } = true;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanImport))] public partial bool Bpms { get; set; } = true;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanImport))] public partial bool HotCues { get; set; } = true;

    public bool CanImport => HasPlan && !IsBusy && _plan is { } p &&
        (Ratings && p.RatingCount > 0 || Crates && p.CrateCount > 0 || History && p.SessionCount > 0 || Bpms && p.BpmCount > 0 ||
         HotCues && p.CuedSongCount > 0 || Playlists && p.PlaylistCount > 0);

    /// <summary>Reads the Mixxx library at <paramref name="path"/>, or where Mixxx keeps it on this computer.</summary>
    public async Task LoadAsync(string? path = null)
    {
        path ??= MixxxLibrary.FindDefault();
        HasPlan = false;
        _plan = null;
        if (path is null)
        {
            Status = "Mixxx's library isn't where Mixxx keeps it on this computer. Choose its file, mixxxdb.sqlite, from Mixxx's settings folder.";
            return;
        }
        Source = path;
        IsBusy = true;
        Status = "Reading Mixxx's library…";
        try
        {
            _plan = await Task.Run(() =>
            {
                using var mixxx = MixxxLibrary.Open(path);
                return MixxxImport.Plan(database, mixxx);
            });
            Show(_plan);
        }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or IOException or UnauthorizedAccessException)
        {
            Log.Error($"Couldn't read the Mixxx library {path}", ex);
            Status = $"That doesn't look like a Mixxx library ({ex.Message}).";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Show(MixxxImport plan)
    {
        Status = $"{Format.Count(plan.MixxxSongs)} songs in Mixxx; {Format.Count(plan.FoundSongs)} of them are in GZJam's music folders.";
        RatingsText = plan.RatingCount == 0
            ? "Nothing new: no rated songs that are unrated in GZJam."
            : $"{Songs(plan.RatingCount)}: 4 and 5 stars become ♥, 1 and 2 stars a broken heart. 3 stars stay unrated, and songs you've rated in GZJam keep their rating.";
        CratesText = plan.CrateCount == 0
            ? "No crates with songs GZJam has."
            : $"{Format.Count(plan.CrateCount)} {(plan.CrateCount == 1 ? "crate" : "crates")}, {Songs(plan.CrateSongCount)}: each becomes a label.";
        PlaylistsText = plan.PlaylistCount == 0
            ? "Nothing new: no playlists with songs GZJam has (or GZJam has playlists of those names already)."
            : $"{Format.Count(plan.PlaylistCount)} {(plan.PlaylistCount == 1 ? "playlist" : "playlists")}, {Songs(plan.PlaylistSongCount)}, in their order. Songs GZJam doesn't have are left out.";
        HistoryText = plan.SessionCount == 0
            ? "Nothing new: no History nights that aren't in GZJam already."
            : $"{Format.Count(plan.SessionCount)} {(plan.SessionCount == 1 ? "night" : "nights")}, {Songs(plan.SessionPlayCount)} played, with their dates: the Played column and played: searches see them.";
        BpmsText = plan.BpmCount == 0
            ? "None: no songs with a BPM you locked in Mixxx (and none typed in GZJam)."
            : $"{Songs(plan.BpmCount)} with a BPM you locked in Mixxx: used as typed BPMs.";
        HotCuesText = plan.CuedSongCount == 0
            ? "None: no songs with hot cues that have no cues in GZJam."
            : $"{Songs(plan.CuedSongCount)}: hot cues 1 to 4 become cue keys 1 to 4. Songs with cues in GZJam keep theirs." +
              (plan.LeftOutHotCues > 0 ? $" {Format.Count(plan.LeftOutHotCues)} hot {(plan.LeftOutHotCues == 1 ? "cue" : "cues")} numbered above 4 {(plan.LeftOutHotCues == 1 ? "stays" : "stay")} behind." : "");
        int missing = plan.MixxxSongs - plan.FoundSongs;
        MissingText = missing == 0
            ? ""
            : $"{Songs(missing)} in Mixxx aren't in GZJam's music folders, e.g. {string.Join(", ", plan.MissingFolders)}. Add those folders with + Folder, then import again.";
        HasPlan = true;
    }

    /// <summary>Brings over what's ticked. Returns what came over, or null if nothing could.</summary>
    public async Task<MixxxImportResult?> ImportAsync()
    {
        if (!CanImport || _plan is not { } plan)
            return null;
        IsBusy = true;
        Status = "Importing…";
        try
        {
            var options = new MixxxImportOptions(Ratings, Crates, History, Bpms, HotCues, Playlists);
            MixxxImportResult result = await Task.Run(() => plan.Apply(database, options));
            Log.Info($"Imported from Mixxx ({plan.Source}): {result}");
            imported(result);
            return result;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>A one-line summary of an import, for the notice.</summary>
    public static string Summary(MixxxImportResult result)
    {
        var parts = new List<string>();
        if (result.Ratings > 0)
            parts.Add($"{Format.Count(result.Ratings)} {(result.Ratings == 1 ? "rating" : "ratings")}");
        if (result.Labels > 0)
            parts.Add($"{Format.Count(result.Labels)} {(result.Labels == 1 ? "crate" : "crates")} as labels");
        if (result.Playlists > 0)
            parts.Add($"{Format.Count(result.Playlists)} {(result.Playlists == 1 ? "playlist" : "playlists")}");
        if (result.Sessions > 0)
            parts.Add($"{Format.Count(result.Sessions)} {(result.Sessions == 1 ? "night" : "nights")} of History");
        if (result.Bpms > 0)
            parts.Add($"{Format.Count(result.Bpms)} {(result.Bpms == 1 ? "BPM" : "BPMs")}");
        if (result.CuedSongs > 0)
            parts.Add($"hot cues for {Songs(result.CuedSongs)}");
        return parts.Count == 0 ? "Nothing new to import from Mixxx." : "Imported from Mixxx: " + string.Join(", ", parts) + ".";
    }

    private static string Songs(int n) => $"{Format.Count(n)} {(n == 1 ? "song" : "songs")}";
}
