using GZJam.Library;

namespace GZJam.App.ViewModels;

/// <summary>
/// Songs from the queue sent to a folder of My music (#335). The drop only opens the folder chooser:
/// a drag in the main screen never moves a file without the DJ saying so. Wherever a song came from,
/// it ends up there: moved from the computer, copied from a USB stick or a server.
/// </summary>
public sealed partial class MainWindowViewModel
{
    /// <summary>What happens to each song: moved, copied from a stick, copied from a server, or left on its deck.</summary>
    private sealed record ToFolderPlan(List<Track> Moves, int Joining, List<Track> Sticks, List<Track> FromServers, List<Track> OnDeck)
    {
        public int Count => Moves.Count + Sticks.Count + FromServers.Count;

        public bool IsEmpty => Count == 0;

        public string Action => Moves.Count > 0 ? "Move" : "Add";

        public Track First => Moves.Concat(Sticks).Concat(FromServers).First();
    }

    private ToFolderPlan PlanToFolder(IReadOnlyList<Track> tracks)
    {
        var onDecks = Organize.OnDecks();
        List<Track> moves = [], sticks = [], fromServers = [], onDeck = [];
        int joining = 0;
        foreach (Track track in tracks.DistinctBy(t => t.OnServer is { } remote ? $"{remote.ServerId}/{remote.Song.Id}" : t.Path))
        {
            if (track.OnServer is not null || FileSources.IsIn(track.Path, Servers.DownloadsFolder))
                fromServers.Add(track);
            else if (Files.IsOnStick(track) || FileSources.IsIn(track.Path, Files.ReadInFolder))
                sticks.Add(track);
            else if (onDecks.Contains(track.Path))
                onDeck.Add(track);
            else
            {
                moves.Add(track);
                if (!Library.MusicFolders.Any(f => FileSources.IsIn(track.Path, f)))
                    joining++;
            }
        }
        return new ToFolderPlan(moves, joining, sticks, fromServers, onDeck);
    }

    /// <summary>Whether any of these songs can go to a folder (a song on a deck can't, until it's off).</summary>
    public bool CanSendToFolder(IReadOnlyList<Track> tracks) => tracks.Count > 0 && !PlanToFolder(tracks).IsEmpty;

    /// <summary>The queue menu's item for these songs.</summary>
    public string ToFolderMenuText(IReadOnlyList<Track> tracks) =>
        tracks.Count > 0 && PlanToFolder(tracks) is { IsEmpty: false, Moves.Count: 0 } ? "Add to my music…" : "Move to a folder in My music…";

    /// <summary>What the drag's tag says over a folder, before the drop; null if none of the songs can go.</summary>
    public string? ToFolderDragText(IReadOnlyList<Track> tracks, string folderName) =>
        PlanToFolder(tracks) is { IsEmpty: false } plan ? $"{plan.Action} to {folderName}…" : null;

    /// <summary>
    /// Asks, then sends the songs to a folder of My music.
    /// </summary>
    /// <param name="folder">The folder they were dropped on; null (a drop on My music, or the menu) to open on the usual place.</param>
    public async Task SendToFolderAsync(IReadOnlyList<Track> tracks, string? folder)
    {
        if (Organize.IsBusy)
        {
            ShowNotice("Songs are still being moved: try again once they're done.", 6);
            return;
        }
        ToFolderPlan plan = PlanToFolder(tracks);
        if (plan.IsEmpty)
        {
            ShowNotice(plan.OnDeck.Count == 1 ? $"Take {plan.OnDeck[0].DisplayTitle} off its deck first: its file can't move while it's on a deck."
                : "Take those songs off their decks first: their files can't move while they're on a deck.", 6);
            return;
        }
        if (ChooseAddPlaceAsync is not { } choose)
            return;

        string? keep = null;
        bool choosing = false;
        if (folder is null)
        {
            if (await Servers.KeepFolderToUseAsync() is not var (usualRoot, first))
                return;
            (keep, choosing) = (usualRoot, first);
        }
        string usual = folder ?? Path.Combine(keep!, "Added");
        var asking = new AddPlaceViewModel(ToFolderHeading(plan, folder is not null), ToFolderDetails(plan), usual, Library.MusicFolders, folder, keepFolders: false, canKeepFolders: false,
            Servers.DownloadsFolder, plan.Action, hasUsualPlace: folder is null);
        if (await choose(asking) is not { } chosen)
            return;
        AddPlace place = chosen with { KeepFolders = false };   // straight into the folder chosen
        if (choosing && !Servers.SetKeepFolder(keep!))
            return;

        var moving = plan.Moves.Where(t => !IsInFolder(t, place.Folder)).ToList();
        if (moving.Count + plan.Sticks.Count + plan.FromServers.Count == 0)
        {
            ShowNotice(plan.Moves.Count == 1 ? $"{plan.Moves[0].DisplayTitle} is in that folder already." : "Those songs are in that folder already.", 6);
            return;
        }
        if (moving.Count > 0 && !await Organize.WarnOfFirstMoveAsync())
            return;
        if (Organize.IsBusy)
        {
            ShowNotice("Songs are still being moved: try again once they're done.", 6);
            return;
        }

        if (keep is not null && Servers.RootOf(place.Folder) is null)
        {
            Directory.CreateDirectory(keep);
            await Library.AddRootAsync(keep);
        }
        Log.Info($"To a folder: {moving.Count} to move, {plan.Sticks.Count} from USB sticks and {plan.FromServers.Count} from servers to copy, into {place.Folder}");

        if (plan.Sticks.Count > 0)
        {
            var copied = await Files.CopyIntoMyMusicAsync(plan.Sticks.Select(t => (t, "")).ToList(), place.Folder, keepFolders: false, keep ?? place.Folder);
            if (copied.Count > 0)
                Library.FollowMoves(copied);   // the queue's row is the copy now
        }
        if (moving.Count > 0)
            await Organize.MoveSongsAsync(moving.Select(t => t.Path).ToList(), place.Folder);
        if (plan.FromServers.Count > 0)
            await Servers.KeepTracksAsync(plan.FromServers, place);
    }

    private static bool IsInFolder(Track track, string folder) =>
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetDirectoryName(track.Path) ?? ""), Path.TrimEndingDirectorySeparator(folder), StringComparison.OrdinalIgnoreCase);

    private static string ToFolderHeading(ToFolderPlan plan, bool dropped)
    {
        string what = plan.Count == 1 ? $"\"{plan.First.DisplayTitle}\"" : $"{Format.Count(plan.Count)} songs";
        return plan.Moves.Count == 0 ? $"Add {what} to My music?"
            : dropped ? $"Move {what} here?"
            : $"Move {what} to a folder in My music?";
    }

    private static string ToFolderDetails(ToFolderPlan plan)
    {
        string Who(int n, string one, string many) =>
            plan.Count == 1 ? $"It {one}." : n == 1 ? $"1 song {one}." : $"{Format.Count(n)} songs {many}.";

        var lines = new List<string>();
        int staying = plan.Moves.Count - plan.Joining;
        if (staying > 0)
            lines.Add(Who(staying, "stays in My music, in a new place", "stay in My music, in new places"));
        if (plan.Joining > 0)
            lines.Add(Who(plan.Joining, "joins My music", "join My music"));
        if (plan.Sticks.Count > 0)
            lines.Add(Who(plan.Sticks.Count, "is copied, and the copy joins My music: the USB stick keeps its song",
                "are copied, and the copies join My music: the USB sticks keep their songs"));
        if (plan.FromServers.Count > 0)
            lines.Add(Who(plan.FromServers.Count, "is copied, downloaded first if it isn't here yet, and the copy joins My music: the server's song isn't touched",
                "are copied, downloaded first if they aren't here yet, and the copies join My music: the server's songs aren't touched"));
        if (plan.OnDeck.Count > 0)
            lines.Add(plan.OnDeck.Count == 1 ? $"{plan.OnDeck[0].DisplayTitle} is on a deck, so it stays where it is."
                : $"{Format.Count(plan.OnDeck.Count)} songs are on a deck, so they stay where they are.");
        lines.Add("Hearts, cues, labels and playlists go along, and the place in the queue follows.");
        return string.Join(Environment.NewLine, lines);
    }
}
