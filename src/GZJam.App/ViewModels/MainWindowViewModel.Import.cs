using GZJam.Library;

namespace GZJam.App.ViewModels;

/// <summary>Bringing playlists and ratings over from other players (Settings › Library).</summary>
public partial class MainWindowViewModel
{
    /// <summary>
    /// Asks before importing (heading, message, confirm button), with a confirm button that doesn't
    /// look like a warning: an import loses nothing. Null in tests that don't need it: no asking.
    /// </summary>
    public Func<string, string, string, Task<bool>>? ConfirmImportAsync { get; set; }

    /// <summary>
    /// Settings › Import playlists: reads the M3U files the DJ picked, says what would come over,
    /// and makes the playlists once they confirm.
    /// </summary>
    public async Task ImportPlaylistsAsync(IReadOnlyList<string> files)
    {
        if (files.Count == 0)
            return;
        PlaylistImport plan = await Task.Run(() => PlaylistImport.Plan(_database, files));
        if (plan.Playlists.Count == 0)
        {
            ShowNotice(NothingToImport(plan), seconds: 8);
            return;
        }
        string heading = plan.Playlists.Count == 1 ? $"Import the playlist \"{plan.Playlists[0].Name}\"?" : $"Import {plan.Playlists.Count} playlists?";
        if (ConfirmImportAsync is not null && !await ConfirmImportAsync(heading, PlaylistsMessage(plan), "Import"))
            return;
        int made = plan.Apply(_database);
        Log.Info($"Imported {made} playlists from {files.Count} M3U files ({plan.SongCount} songs, {plan.MissingSongs} not found)");
        Library.RefreshPlaylists();
        ShowNotice(made == 1 ? $"Imported the playlist \"{plan.Playlists[0].Name}\"." : $"Imported {made} playlists.");
    }

    /// <summary>What an M3U import would bring over, and what it leaves out.</summary>
    internal static string PlaylistsMessage(PlaylistImport plan)
    {
        var lines = new List<string>
        {
            string.Join(", ", plan.Playlists.Select(p => $"{p.Name} ({Songs(p.Songs)})")) + ", in their order.",
        };
        if (plan.MissingSongs > 0)
            lines.Add($"{Songs(plan.MissingSongs)} {(plan.MissingSongs == 1 ? "isn't" : "aren't")} in GZJam's music folders and {(plan.MissingSongs == 1 ? "is" : "are")} left out, e.g. from {string.Join(", ", plan.MissingFolders)}. Add those folders with + Folder, then import again.");
        if (plan.AlreadyThere.Count > 0)
            lines.Add($"GZJam has {(plan.AlreadyThere.Count == 1 ? "a playlist" : "playlists")} called {string.Join(", ", plan.AlreadyThere)} already: left as {(plan.AlreadyThere.Count == 1 ? "it is" : "they are")}.");
        if (plan.NothingToImport.Count > 0)
            lines.Add($"Nothing to import from {string.Join(", ", plan.NothingToImport)}: no songs GZJam has.");
        lines.Add("The playlist files aren't changed.");
        return string.Join("\n\n", lines);
    }

    private static string NothingToImport(PlaylistImport plan) =>
        plan.AlreadyThere.Count > 0 && plan.NothingToImport.Count == 0
            ? $"GZJam has {(plan.AlreadyThere.Count == 1 ? "that playlist" : "those playlists")} already: nothing imported."
            : plan.MissingSongs > 0
                ? $"No songs to import: the {Songs(plan.MissingSongs)} listed {(plan.MissingSongs == 1 ? "isn't" : "aren't")} in GZJam's music folders (e.g. {string.Join(", ", plan.MissingFolders)})."
                : "No songs to import: the playlist is empty, or couldn't be read.";

    /// <summary>Settings › Ratings in the song files; afterward, the library shows the hearts.</summary>
    public FileRatingsImportViewModel CreateFileRatingsImport() => new(_database, () => Library.RefreshResults());

    private static string Songs(int n) => $"{Format.Count(n)} {(n == 1 ? "song" : "songs")}";
}
