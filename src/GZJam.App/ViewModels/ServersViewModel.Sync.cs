using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using GZJam.Library;
using GZJam.Library.Servers;

namespace GZJam.App.ViewModels;

/// <summary>What a server offers to keep on the laptop, for choosing (CHOOSE…): its playlists, what's starred, and what's chosen now.</summary>
public sealed record ServerOffer(IReadOnlyList<ServerPlaylist> Playlists, ServerStarred Starred, IReadOnlyList<ServerChoice> Chosen);

/// <summary>
/// Settings › Servers, part 2 (#283): choosing what to keep on the laptop from each server, and
/// syncing it into the downloads folder, a music folder like any other.
/// </summary>
public sealed partial class ServersViewModel
{
    /// <summary>For songs and lists: a song can take a while, so no overall time limit (a stalled one is given up on, see SubsonicClient.DownloadAsync).</summary>
    private static readonly HttpClient DownloadHttp = new(QuickToConnect())
    {
        Timeout = TimeSpan.FromMinutes(30),
        DefaultRequestHeaders = { { "User-Agent", $"GZJam/{About.Version}" } },
    };

    private readonly AppSettings _settings;
    private readonly SemaphoreSlim _oneSync = new(1, 1);
    private bool _leftoversCleared;

    /// <summary>Talks to a server, signed in. Tests put a pretend server here.</summary>
    public Func<MusicServer, string, SubsonicClient> ClientFor { get; set; } =
        (server, password) => new SubsonicClient(DownloadHttp, server.Address, server.UserName, password);

    /// <summary>Where downloaded songs go unless the DJ chose a folder: "GZJam from servers" in the computer's Music folder.</summary>
    public static string DefaultDownloadsFolder
    {
        get
        {
            string music = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
            if (music.Length == 0)
                music = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Music");
            return Path.Combine(music, "GZJam from servers");
        }
    }

    public string DownloadsFolder => _settings.ServerDownloadsFolder is { Length: > 0 } folder ? folder : DefaultDownloadsFolder;

    // ───────────── The downloads folder (Settings › Servers) ─────────────

    /// <summary>"312 songs, 1.2 GB · 380 GB free", or that nothing's downloaded yet.</summary>
    public string DownloadsText
    {
        get
        {
            (int songs, long bytes) = _database.GetDownloadedTotals();
            string free = FreeSpace() is { } f ? $" · {Format.Size(f)} free on this drive" : "";
            return songs == 0
                ? $"Nothing downloaded yet.{free}"
                : $"{(songs == 1 ? "1 song" : Format.Count(songs) + " songs")}, {Format.Size(bytes)}{free}";
        }
    }

    /// <summary>The folder can be changed (its songs moving with it) unless they're moving already, or a sync is running.</summary>
    public bool CanChangeDownloadsFolder => !_moving && !Items.Any(i => i.IsSyncing);

    public IReadOnlyList<Choice<double?>> LimitChoices { get; } =
    [
        new(null, "As much as the disk has"),
        new(10, "10 GB"),
        new(25, "25 GB"),
        new(50, "50 GB"),
        new(100, "100 GB"),
        new(250, "250 GB"),
        new(500, "500 GB"),
    ];

    /// <summary>Room the downloads may take: past it, a sync stops and says so.</summary>
    public Choice<double?> Limit
    {
        get => LimitChoices.FirstOrDefault(c => c.Value == _settings.ServerDownloadsLimitGb) ?? LimitChoices[0];
        set
        {
            _settings.ServerDownloadsLimitGb = value?.Value;
            OnPropertyChanged();
        }
    }

    /// <summary>Puts downloads in another folder, without moving any (see <see cref="ChangeDownloadsFolderAsync"/>).</summary>
    public void SetDownloadsFolder(string folder)
    {
        _settings.ServerDownloadsFolder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        Log.Info($"Servers: songs will be downloaded to {_settings.ServerDownloadsFolder}");
        RefreshDownloads();
    }

    public void RefreshDownloads()
    {
        OnPropertyChanged(nameof(DownloadsFolder));
        OnPropertyChanged(nameof(DownloadsText));
        OnPropertyChanged(nameof(CanChangeDownloadsFolder));
    }

    private long? FreeSpace()
    {
        try
        {
            return DriveOf(DownloadsFolder)?.AvailableFreeSpace;
        }
        catch (Exception e) when (e is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // ───────────── Choosing ─────────────

    /// <summary>What a server offers to keep on the laptop, or a <see cref="ServerException"/> saying why it can't be asked.</summary>
    public async Task<ServerOffer> LoadOfferAsync(ServerItemViewModel item, CancellationToken cancel = default)
    {
        string password = TryGetPassword(item.Server.PasswordKey, out string? problem)
            ?? throw new ServerException(problem ?? "The password isn't on this computer: press EDIT to type it.");
        SubsonicClient client = ClientFor(item.Server, password);
        var playlists = await client.GetPlaylistsAsync(cancel);
        var starred = await client.GetStarredAsync(cancel);
        return new ServerOffer(playlists, starred, _database.GetServerChoices(item.Server.Id));
    }

    /// <summary>Keeps these on the laptop from now on; what's no longer chosen stays until it's cleaned up (#286).</summary>
    public void SaveChoices(ServerItemViewModel item, IEnumerable<ServerChoice> choices)
    {
        var chosen = choices.ToList();
        _database.SetServerChoices(item.Server.Id, chosen);
        Log.Info($"Servers: keeping from {item.Name}: {(chosen.Count == 0 ? "nothing" : string.Join(", ", chosen.Select(c => c.Name)))}");
        RefreshKept(item);
    }

    /// <summary>The card's lines about what's kept on the laptop.</summary>
    private void RefreshKept(ServerItemViewModel item)
    {
        var choices = _database.GetServerChoices(item.Server.Id);
        (int songs, long bytes) = _database.GetDownloadedTotals(item.Server.Id);
        item.SetKept(choices, songs, bytes);
    }

    // ───────────── Syncing ─────────────

    /// <summary>The top bar's line while a sync runs; null otherwise.</summary>
    [ObservableProperty]
    public partial string? SyncStatus { get; private set; }

    [ObservableProperty]
    public partial double SyncPercent { get; private set; }

    partial void OnSyncStatusChanged(string? value) => _owner.BackgroundChanged();

    partial void OnSyncPercentChanged(double value) => _owner.BackgroundChanged();

    /// <summary>
    /// Brings what's chosen from <paramref name="item"/> onto the laptop. One server at a time;
    /// another waits its turn. <paramref name="automatic"/> (at startup) stops, between songs, once
    /// the room starts hearing music.
    /// </summary>
    public async Task SyncAsync(ServerItemViewModel item, bool automatic = false)
    {
        if (item.IsSyncing || !item.CanSync)
            return;
        string? password = TryGetPassword(item.Server.PasswordKey, out string? problem);
        if (password is null)
        {
            item.ShowSync(problem ?? "Sign in again: the password isn't on this computer. Press EDIT to type it.", problem: true);
            return;
        }

        var stop = new CancellationTokenSource();
        item.BeginSync(stop);
        item.ShowSync("Waiting for another server's sync…", problem: false);
        await _oneSync.WaitAsync();
        try
        {
            if (stop.IsCancellationRequested)
            {
                item.ShowSync("Stopped.", problem: false);
                return;
            }
            await EnsureDownloadsInLibraryAsync();
            if (!_leftoversCleared)
            {
                _leftoversCleared = true;
                int cleared = await Task.Run(() => ServerSync.ClearLeftovers(DownloadsFolder));
                if (cleared > 0)
                    Log.Info($"Servers: cleared {cleared} half-downloaded files left from before");
            }

            string? limitGb = _settings.ServerDownloadsLimitGb?.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var sync = new ServerSync(_database, ClientFor(item.Server, password), item.Server, DownloadsFolder)
            {
                Limit = _settings.ServerDownloadsLimitGb is { } gb ? (long)(gb * 1_000_000_000) : null,
                StopBefore = automatic
                    ? () => Dispatcher.UIThread.Invoke(() => _owner.WhatTheRoomIsHearing()) is not null
                        ? "Paused: the room is hearing music. Press SYNC to finish."
                        : null
                    : null,
            };
            Log.Info($"Servers: syncing {item.Name}{(automatic ? " (at startup)" : "")} into {sync.ServerFolder}"
                + (limitGb is null ? "" : $", up to {limitGb} GB"));
            item.ShowSync("Finding the songs…", problem: false);
            SyncStatus = $"Syncing {item.Name}…";
            SyncPercent = 0;
            var progress = new Progress<SyncProgress>(p =>
            {
                if (p.Songs == 0 || p.Done >= p.Songs)
                    return;
                item.ShowSync($"Downloading… {Format.Count(p.Done)} of {Format.Count(p.Songs)} songs"
                    + (p.Downloaded > 0 ? $" ({Format.Count(p.Downloaded)} new, {Format.Size(p.Bytes)})" : ""), problem: false);
                SyncStatus = $"Downloading from {item.Name}… {Format.Count(p.Done)} / {Format.Count(p.Songs)}";
                SyncPercent = 100.0 * p.Done / p.Songs;
            });

            SyncResult result = await Task.Run(() => sync.RunAsync(progress, stop.Token));

            foreach (string line in result.Problems)
                Log.Info($"Servers: {item.Name}: {line}");
            Log.Info($"Servers: synced {item.Name}: {result.Songs} songs, {result.Downloaded} new ({Format.Size(result.Bytes)}), "
                + $"{result.AlreadyHere} already here, {result.Failed} failed" + (result.Stopped is { } why ? $"; stopped: {why}" : ""));
            item.ShowSync(Describe(result, automatic), problem: !result.Finished || result.Failed > 0);
            if (result.Downloaded > 0 || result.Songs > 0)
                AfterSync();
        }
        finally
        {
            SyncStatus = null;
            item.EndSync();
            RefreshKept(item);
            RefreshDownloads();
            _oneSync.Release();
        }
    }

    public void StopSync(ServerItemViewModel item) => item.RequestStop();

    /// <summary>A server's node in the library shows its sync.</summary>
    internal void SyncingChanged(ServerItemViewModel item, bool syncing)
    {
        _owner.Library?.SetServerSyncing(item.Server.Id, syncing);
        OnPropertyChanged(nameof(CanChangeDownloadsFolder));
    }

    /// <summary>The library's "Sync now" on a server: what's chosen, or CHOOSE first when nothing is.</summary>
    public ServerItemViewModel? Item(long serverId) => Items.FirstOrDefault(i => i.Server.Id == serverId);

    /// <summary>At startup: each server that has something chosen, quietly; one that doesn't answer is left for later.</summary>
    public async Task SyncAtStartupAsync()
    {
        foreach (ServerItemViewModel item in Items.ToList())
            if (item.CanSync)
                await SyncAsync(item, automatic: true);
    }

    private static string Describe(SyncResult result, bool automatic)
    {
        string news = result.Downloaded == 0 ? "nothing new"
            : $"{Format.Count(result.Downloaded)} new ({Format.Size(result.Bytes)})";
        string failed = result.Failed == 0 ? ""
            : $" {(result.Failed == 1 ? "1 song" : Format.Count(result.Failed) + " songs")} couldn't be downloaded: the activity log says why.";
        if (result.Stopped is { } why)
            return result.Songs == 0 && automatic
                ? $"Not synced at startup: {char.ToLowerInvariant(why[0])}{why[1..]}"
                : $"{why} {Format.Count(result.Downloaded)} new so far.{failed}";
        return $"Synced {DateTime.Now.ToString("t", System.Globalization.CultureInfo.CurrentCulture)}: "
            + $"{Format.Count(result.Songs)} songs here, {news}.{failed}";
    }

    /// <summary>
    /// The downloads folder is in the library: inside one of the DJ's music folders already, or
    /// added as one of its own (a folder in the library's tree).
    /// </summary>
    private async Task EnsureDownloadsInLibraryAsync()
    {
        string folder = DownloadsFolder;
        Directory.CreateDirectory(folder);
        if (RootOf(folder) is null)
        {
            Log.Info($"Servers: adding {folder} to the library");
            await _owner.Library.AddRootAsync(folder);
        }
    }

    private LibraryRoot? RootOf(string folder)
    {
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)) + Path.DirectorySeparatorChar;
        return _database.GetRoots().FirstOrDefault(r =>
            full.StartsWith(Path.TrimEndingDirectorySeparator(r.Path) + Path.DirectorySeparatorChar,
                OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The new songs into the library, and the playlists they filled into the list.</summary>
    private void AfterSync()
    {
        if (RootOf(DownloadsFolder) is { } root)
            _owner.Library.RescanRoot(root.Id);
        _owner.Library.RefreshPlaylists();
    }
}

public sealed partial class ServerItemViewModel
{
    private CancellationTokenSource? _stop;

    /// <summary>"Keeping on this laptop: Friday, Saturday and what's starred", or that nothing is chosen yet.</summary>
    [ObservableProperty]
    public partial string KeptText { get; private set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSync))]
    public partial bool HasChoices { get; private set; }

    /// <summary>The last sync's result, or how the one running is going.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSyncText))]
    public partial string? SyncText { get; private set; }

    [ObservableProperty]
    public partial bool SyncProblem { get; private set; }

    public bool HasSyncText => SyncText is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SyncButtonText), nameof(CanSync))]
    public partial bool IsSyncing { get; private set; }

    public string SyncButtonText => IsSyncing ? "STOP" : "SYNC";

    /// <summary>Something's chosen, and the account may download.</summary>
    public bool CanSync => HasChoices && !IsPreviewOnly;

    /// <summary>The account may download, so there's something to choose.</summary>
    public bool CanChoose => !IsPreviewOnly;

    internal void SetKept(IReadOnlyList<ServerChoice> choices, int songs, long bytes)
    {
        HasChoices = choices.Count > 0;
        if (choices.Count == 0)
        {
            KeptText = IsPreviewOnly ? "" : "Nothing chosen to keep on this laptop yet: press CHOOSE.";
            return;
        }
        var names = choices.Where(c => c.Kind == ServerChoiceKind.Playlist).Select(c => c.Name).ToList();
        if (choices.Any(c => c.Kind == ServerChoiceKind.Starred))
            names.Add("what's starred");
        foreach ((ServerChoiceKind kind, string one, string many) in (ReadOnlySpan<(ServerChoiceKind, string, string)>)
                 [(ServerChoiceKind.Artist, "artist", "artists"), (ServerChoiceKind.Album, "album", "albums"), (ServerChoiceKind.Song, "song", "songs")])
            if (choices.Count(c => c.Kind == kind) is var n and > 0)
                names.Add(n == 1 ? $"1 {one}" : $"{Format.Count(n)} {many}");
        string list = names.Count == 1 ? names[0] : string.Join(", ", names[..^1]) + " and " + names[^1];
        string here = songs == 0 ? "" : $" · {(songs == 1 ? "1 song" : Format.Count(songs) + " songs")} downloaded, {Format.Size(bytes)}";
        KeptText = $"Keeping on this laptop: {list}{here}";
    }

    internal void ShowSync(string text, bool problem)
    {
        SyncText = text;
        SyncProblem = problem;
    }

    internal void BeginSync(CancellationTokenSource stop)
    {
        _stop = stop;
        IsSyncing = true;
    }

    internal void EndSync()
    {
        _stop?.Dispose();
        _stop = null;
        IsSyncing = false;
    }

    partial void OnIsSyncingChanged(bool value) => _owner.SyncingChanged(this, value);

    internal void RequestStop() => _stop?.Cancel();

    public Task SyncOrStopAsync()
    {
        if (IsSyncing)
        {
            _owner.StopSync(this);
            return Task.CompletedTask;
        }
        return _owner.SyncAsync(this);
    }

    partial void OnServerChanged(MusicServer value)
    {
        OnPropertyChanged(nameof(CanSync));
        OnPropertyChanged(nameof(CanChoose));
    }
}

/// <summary>Browsing a server (#284): what the browser asks of the servers.</summary>
public sealed partial class ServersViewModel
{
    private string? _previewFolderCleared;

    /// <summary>A client signed in to the server, or null if its password isn't on this computer.</summary>
    public SubsonicClient? SignedIn(ServerItemViewModel item) =>
        TryGetPassword(item.Server.PasswordKey, out _) is { } password ? ClientFor(item.Server, password) : null;

    /// <summary>Each of the server's songs that's on this laptop (by its ID there), and where.</summary>
    public Dictionary<string, string> LocalCopies(ServerItemViewModel item) =>
        _database.GetServerSongs(item.Server.Id).Where(s => File.Exists(s.Path)).ToDictionary(s => s.SongId, s => s.Path);

    /// <summary>Adds songs, albums or artists to what's kept on the laptop from the server.</summary>
    public void AddChoices(ServerItemViewModel item, IEnumerable<ServerChoice> choices)
    {
        var added = choices.ToList();
        _database.AddServerChoices(item.Server.Id, added);
        // Asked for: songs the DJ removed from the laptop before come back with them.
        _database.ForgetServerSongsRemoved(item.Server.Id, added.SelectMany(c => c.Kind switch
        {
            ServerChoiceKind.Song => [c.RemoteId],
            ServerChoiceKind.Album => _database.GetServerCatalogAlbumSongIds(item.Server.Id, c.RemoteId),
            _ => (IReadOnlyList<string>)[],
        }));
        Log.Info($"Servers: keeping from {item.Name}, as well: {string.Join(", ", added.Select(c => $"{c.Kind.ToString().ToLowerInvariant()} {c.Name}"))}");
        RefreshKept(item);
    }

    /// <summary>Where a song fetched only for the headphones goes: a temporary folder, emptied each session.</summary>
    public string PreviewFolder { get; set; } = Path.Combine(Path.GetTempPath(), "GZJam previews");

    /// <summary>
    /// Plays a server's song in the headphones: the copy on this laptop if there is one, else
    /// fetched into <see cref="PreviewFolder"/> for the preview only (it's never played to the room
    /// from there, and isn't in the library). Returns a problem to say, or null.
    /// </summary>
    public async Task<string?> PreviewAsync(ServerItemViewModel item, ServerSong song, string? localPath)
    {
        if (_owner.Preview is null)
            return "The headphone preview isn't available: the sound didn't start.";
        if (!_owner.HasCueOutput)
            return "Choose a headphone output in Settings › Sound to preview songs.";
        string? path = localPath;
        if (path is null)
        {
            if (SignedIn(item) is not { } client)
                return "The password isn't on this computer: press EDIT on the server in Settings › Servers to type it.";
            try
            {
                path = await Task.Run(async () =>
                {
                    ClearPreviewsOnce();
                    Directory.CreateDirectory(PreviewFolder);
                    string extension = Path.GetExtension(ServerPaths.RelativeParts(song)[^1]);
                    string file = Path.Combine(PreviewFolder, ServerPaths.Safe($"{item.Server.Id}-{song.Id}") + extension);
                    if (!File.Exists(file))
                    {
                        string partial = file + ServerSync.PartialExtension;
                        await using (var stream = File.Create(partial))
                            await client.DownloadAsync(song.Id, stream);
                        File.Move(partial, file, overwrite: true);
                    }
                    return file;
                });
            }
            catch (Exception e) when (e is ServerException or IOException or UnauthorizedAccessException)
            {
                return $"Couldn't get the song from the server: {e.Message}";
            }
        }
        var track = new Track
        {
            Path = path,
            Folder = "",
            FileName = Path.GetFileName(path),
            Title = song.Title,
            Artist = song.Artist,
            Album = song.Album,
        };
        if (_owner.Preview.Track?.Path != path)
            _owner.Preview.Toggle(track);
        PreviewStarted(path);
        return null;
    }

    partial void PreviewStarted(string path);

    private void ClearPreviewsOnce()
    {
        if (_previewFolderCleared == PreviewFolder)
            return;
        _previewFolderCleared = PreviewFolder;
        try
        {
            if (Directory.Exists(PreviewFolder))
                Directory.Delete(PreviewFolder, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // one is playing, or in use: left for next time
        }
    }
}
