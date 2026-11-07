using GZJam.Library;

namespace GZJam.App.ViewModels;

/// <summary>
/// Downloads removed by themselves (#286): Settings › Servers › Keep downloaded songs, and the room
/// set aside for downloads. Only songs no list kept on the computer needs (its playlists, what's
/// starred, albums, folders, and songs downloaded on purpose), and never a song on a deck, in the
/// queue or played tonight. Removed by GZJam, a song isn't held back from the server: a list that
/// wants it again brings it again, and its hearts, cues and EQ are kept for then.
/// </summary>
public sealed partial class ServersViewModel
{
    /// <summary>When this GZJam started: songs downloaded on the spot before it are the last night's. Tests move it.</summary>
    internal DateTime StartedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>The time now, for the 4 weeks. Tests move it.</summary>
    internal Func<DateTime> UtcNow { get; set; } = () => DateTime.UtcNow;
    private readonly List<ServerDownload> _madeRoom = [];

    public IReadOnlyList<Choice<KeepDownloads>> KeepDownloadsChoices { get; } =
    [
        new(KeepDownloads.UntilRemoved, "Until I remove them"),
        new(KeepDownloads.FourWeeks, "For 4 weeks after they were last played"),
        new(KeepDownloads.NightOnly, "For the night only"),
    ];

    /// <summary>Settings › Servers › Keep downloaded songs.</summary>
    public Choice<KeepDownloads> KeepDownloadsChoice
    {
        get => KeepDownloadsChoices.FirstOrDefault(c => c.Value == _settings.ServerKeepDownloads) ?? KeepDownloadsChoices[0];
        set
        {
            _settings.ServerKeepDownloads = value?.Value ?? KeepDownloads.UntilRemoved;
            Log.Info($"Servers: songs downloaded on their own are kept {KeepDownloadsChoice.Label.ToLowerInvariant()}");
            OnPropertyChanged();
            OnPropertyChanged(nameof(KeepDownloadsHelp));
        }
    }

    public string KeepDownloadsHelp => _settings.ServerKeepDownloads switch
    {
        KeepDownloads.FourWeeks => "Songs downloaded for the queue or a deck, and those no kept list needs any more, are removed when GZJam starts once they haven't been played for 4 weeks. Kept lists' songs stay.",
        KeepDownloads.NightOnly => "Songs downloaded for the queue or a deck are removed the next time GZJam starts. Kept lists' songs stay.",
        _ => "Nothing is removed unless you ask (Remove from this computer, or Free up space… on a server).",
    };

    /// <summary>
    /// At startup, once the queue and decks are back: the downloads that Keep downloaded songs
    /// says have had their time, removed.
    /// </summary>
    internal async Task TidyDownloadsAsync()
    {
        KeepDownloads rule = _settings.ServerKeepDownloads;
        if (rule == KeepDownloads.UntilRemoved || Items.Count == 0 || _moving)
            return;
        var keep = ProtectedPaths();
        var servers = Items.Select(i => i.Server.Id).ToList();
        DateTime cutoff = UtcNow() - NotPlayedFor, started = StartedUtc;
        try
        {
            var gone = await Task.Run(() =>
            {
                var due = servers.SelectMany(id =>
                {
                    var needed = _database.GetServerSongsKeptByChoices(id);
                    return _database.GetServerDownloads(id).Where(d => !needed.Contains(d.SongId) && !keep.Contains(d.Path)
                        && (rule == KeepDownloads.NightOnly
                            ? d.OnTheSpot && d.DownloadedUtc < started
                            : (d.LastPlayedUtc ?? d.DownloadedUtc) < cutoff));
                }).ToList();
                return ForgetDownloads(due);
            });
            if (gone.Count == 0)
                return;
            Log.Info($"Servers: removed {Describe(gone)} of downloads whose time was up (kept {KeepDownloadsChoice.Label.ToLowerInvariant()})");
            _owner.ShowNotice($"Removed {Songs(gone)} {(rule == KeepDownloads.NightOnly ? "downloaded for the last night" : "not played for 4 weeks")}: "
                + "they're still on the server. (Settings › Servers › Keep downloaded songs)", seconds: 8);
            Removed();
        }
        catch (Exception e) when (e is Microsoft.Data.Sqlite.SqliteException or IOException)
        {
            Log.Info($"Servers: couldn't tidy the downloads: {e.Message}");
        }
    }

    /// <summary>
    /// For a sync about to pass the room set aside for downloads: what nothing kept needs, the
    /// least recently played first. The queue and decks are read now, on the window's thread.
    /// </summary>
    private Func<long, IReadOnlySet<string>, IReadOnlyList<(string SongId, long Bytes)>> MakeRoomFor(ServerItemViewModel item)
    {
        long id = item.Server.Id;
        var keep = ProtectedPaths();
        return (bytes, keeping) =>
        {
            var needed = _database.GetServerSongsKeptByChoices(id);
            var oldest = _database.GetServerDownloads(id)
                .Where(d => !keeping.Contains(d.SongId) && !needed.Contains(d.SongId) && !keep.Contains(d.Path))
                .OrderBy(d => d.LastPlayedUtc ?? d.DownloadedUtc);
            var chosen = new List<ServerDownload>();
            long freed = 0;
            foreach (ServerDownload download in oldest)
            {
                if (freed >= bytes)
                    break;
                chosen.Add(download);
                freed += download.Size;
            }
            var gone = ForgetDownloads(chosen);
            lock (_madeRoom)
                _madeRoom.AddRange(gone);
            return gone.Select(d => (d.SongId, d.Size)).ToList();
        };
    }

    /// <summary>After a sync that made room: said, and the library follows.</summary>
    private void RoomMade()
    {
        List<ServerDownload> gone;
        lock (_madeRoom)
        {
            gone = [.. _madeRoom];
            _madeRoom.Clear();
        }
        Log.Info($"Servers: removed {Describe(gone)}, played least recently, to make room for the sync");
        _owner.ShowNotice($"To make room, removed {Songs(gone)} played least recently: they're still on the server.", seconds: 8);
        Removed();
    }

    /// <summary>Deletes the downloads' files and forgets them (not held back from a sync); returns those gone. Off the window's thread.</summary>
    private List<ServerDownload> ForgetDownloads(IReadOnlyList<ServerDownload> downloads)
    {
        var gone = new List<ServerDownload>();
        foreach (ServerDownload download in downloads)
        {
            try
            {
                if (File.Exists(download.Path))
                    File.Delete(download.Path);
                gone.Add(download);
                RemoveEmptyFolders(Path.GetDirectoryName(download.Path));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Log.Info($"Servers: couldn't remove {download.Path}: {e.Message}");
            }
        }
        foreach (var server in gone.GroupBy(d => d.ServerId))
            _database.ForgetServerDownloads(server.Key, server.Select(d => d.SongId));
        return gone;
    }

    /// <summary>"12 songs (310 MB)".</summary>
    private static string Songs(IReadOnlyCollection<ServerDownload> downloads) =>
        $"{SongCount(downloads.Count)} ({Format.Size(downloads.Sum(d => d.Size))})";

    private void Removed()
    {
        AfterSync();
        foreach (ServerItemViewModel item in Items)
            RefreshKept(item);
        RefreshDownloads();
        _owner.Library.RefreshResults();
    }
}
