using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using GZJam.Library;
using GZJam.Library.Servers;

namespace GZJam.App.ViewModels;

/// <summary>What a row in the server browser is.</summary>
public enum BrowseRowKind
{
    Artist,
    Album,
    Song,
}

/// <summary>A row in the server browser: an artist, an album or a song on the server.</summary>
public sealed partial class BrowseRow : ObservableObject
{
    private BrowseRow(BrowseRowKind kind, string id, string title, string detail)
    {
        Kind = kind;
        Id = id;
        Title = title;
        Detail = detail;
    }

    public BrowseRowKind Kind { get; }
    public string Id { get; }
    public string Title { get; }
    public string Detail { get; }
    public ServerSong? Song { get; private init; }

    /// <summary>"ARTIST", "ALBUM", or nothing for a song.</summary>
    public string KindText => Kind switch
    {
        BrowseRowKind.Artist => "ARTIST",
        BrowseRowKind.Album => "ALBUM",
        _ => "",
    };

    /// <summary>A song that's on this laptop already: downloaded, or the DJ's own copy.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HereText))]
    public partial string? LocalPath { get; set; }

    public bool IsHere => LocalPath is not null;
    public string HereText => IsHere ? "On this laptop" : "";

    public static BrowseRow For(ServerArtist artist) => new(BrowseRowKind.Artist, artist.Id, artist.Name,
        artist.AlbumCount == 1 ? "1 album" : $"{Format.Count(artist.AlbumCount)} albums");

    public static BrowseRow For(ServerAlbum album) => new(BrowseRowKind.Album, album.Id, album.Name, string.Join(" · ",
        new[]
        {
            album.Artist,
            album.Year is { } y and > 0 ? y.ToString(System.Globalization.CultureInfo.InvariantCulture) : null,
            album.SongCount == 1 ? "1 song" : $"{Format.Count(album.SongCount)} songs",
            album.Length > TimeSpan.Zero ? Format.Duration(album.Length) : null,
        }.Where(p => !string.IsNullOrEmpty(p))));

    public static BrowseRow For(ServerSong song) => new(BrowseRowKind.Song, song.Id, song.Title ?? song.Id, string.Join(" · ",
        new[]
        {
            song.Artist,
            song.Album,
            song.Year is { } y and > 0 ? y.ToString(System.Globalization.CultureInfo.InvariantCulture) : null,
            song.Duration > TimeSpan.Zero ? Format.Duration(song.Duration) : null,
        }.Where(p => !string.IsNullOrEmpty(p))))
    {
        Song = song,
    };

    public ServerChoice Choice => new(Kind switch
    {
        BrowseRowKind.Artist => ServerChoiceKind.Artist,
        BrowseRowKind.Album => ServerChoiceKind.Album,
        _ => ServerChoiceKind.Song,
    }, Id, Kind == BrowseRowKind.Song && Song?.Artist is { Length: > 0 } artist ? $"{Title} ({artist})" : Title);
}

/// <summary>
/// Browsing a server (#284): search it, or look through what was added lately, its artists and
/// their albums; preview a song in the headphones; download songs, albums or artists to the
/// laptop. Downloads join what's kept from the server, so a sync keeps them complete.
/// </summary>
public sealed partial class ServerBrowserViewModel : ObservableObject
{
    private readonly ServersViewModel _servers;
    private readonly Stack<(string Heading, IReadOnlyList<BrowseRow> Rows, BrowseRow? Opened)> _back = new();
    private CancellationTokenSource? _typing;
    private CancellationTokenSource? _loading;
    private Dictionary<string, string> _here = [];

    public ServerBrowserViewModel(ServersViewModel servers, ServerItemViewModel server)
    {
        _servers = servers;
        Server = server;
    }

    public ServerItemViewModel Server { get; }

    public string WindowTitle => $"GZJam · {Server.Name}";

    public string SearchPlaceholder => $"Search {Server.Name}: songs, albums, artists";

    public ObservableCollection<BrowseRow> Rows { get; } = [];

    [ObservableProperty]
    public partial string Heading { get; private set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    public partial string? Status { get; private set; }

    [ObservableProperty]
    public partial bool StatusProblem { get; private set; }

    public bool HasStatus => Status is not null;

    [ObservableProperty]
    public partial bool CanGoBack { get; private set; }

    /// <summary>The album or artist open now, for DOWNLOAD THE ALBUM / THE ARTIST.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DownloadAllText), nameof(CanDownloadAll))]
    public partial BrowseRow? Opened { get; private set; }

    public string DownloadAllText => Opened?.Kind == BrowseRowKind.Artist ? "DOWNLOAD THE ARTIST" : "DOWNLOAD THE ALBUM";

    public bool CanDownloadAll => Opened is not null && Server.CanChoose;

    [ObservableProperty]
    public partial string SearchText { get; set; } = "";

    /// <summary>How long typing waits before searching.</summary>
    public TimeSpan SearchDelay { get; set; } = TimeSpan.FromMilliseconds(350);

    partial void OnSearchTextChanged(string value)
    {
        _typing?.Cancel();
        var typing = _typing = new CancellationTokenSource();
        Task.Delay(SearchDelay, typing.Token).ContinueWith(_ => Dispatcher.UIThread.Post(async () =>
        {
            if (!typing.IsCancellationRequested)
                await SearchAsync();
        }), CancellationToken.None, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);
    }

    /// <summary>The first page: what was added to the server lately.</summary>
    public Task ShowHomeAsync() => LoadAsync("Recently added", null, async (client, cancel) =>
        (await client.GetAlbumListAsync("newest", 60, cancel: cancel)).Select(BrowseRow.For).ToList(), remember: false);

    public Task SearchAsync()
    {
        string text = SearchText.Trim();
        if (text.Length == 0)
        {
            _back.Clear();
            return ShowHomeAsync();
        }
        return LoadAsync($"Search: {text}", null, async (client, cancel) =>
        {
            ServerSearch found = await client.SearchAsync(text, cancel: cancel);
            return found.Artists.Select(BrowseRow.For).Concat(found.Albums.Select(BrowseRow.For)).Concat(found.Songs.Select(BrowseRow.For)).ToList();
        }, remember: false);
    }

    /// <summary>An artist shows their albums, an album its songs; a song is previewed.</summary>
    public Task OpenAsync(BrowseRow row) => row.Kind switch
    {
        BrowseRowKind.Artist => LoadAsync(row.Title, row, async (client, cancel) =>
            (await client.GetArtistAsync(row.Id, cancel)).Select(BrowseRow.For).ToList()),
        BrowseRowKind.Album => LoadAsync(row.Title, row, async (client, cancel) =>
            (await client.GetAlbumSongsAsync(row.Id, cancel)).Select(BrowseRow.For).ToList()),
        _ => PreviewAsync(row),
    };

    public void GoBack()
    {
        if (!_back.TryPop(out var page))
            return;
        _loading?.Cancel();
        Show(page.Heading, page.Rows, page.Opened);
        CanGoBack = _back.Count > 0;
    }

    private async Task LoadAsync(string heading, BrowseRow? opened,
        Func<SubsonicClient, CancellationToken, Task<List<BrowseRow>>> load, bool remember = true)
    {
        if (_servers.SignedIn(Server) is not { } client)
        {
            Say("The password isn't on this computer: press EDIT on the server in Settings › Servers to type it.", problem: true);
            return;
        }
        _loading?.Cancel();
        var cancel = _loading = new CancellationTokenSource();
        Say("Asking the server…", problem: false);
        try
        {
            List<BrowseRow> rows = await Task.Run(() => load(client, cancel.Token), cancel.Token);
            if (cancel.IsCancellationRequested)
                return;
            if (remember && Rows.Count > 0)
                _back.Push((Heading, Rows.ToList(), Opened));
            else if (!remember)
                _back.Clear();
            CanGoBack = _back.Count > 0;
            Show(heading, rows, opened);
        }
        catch (OperationCanceledException)
        {
        }
        catch (ServerException e)
        {
            Say($"Couldn't ask the server: {e.Message}", problem: true);
        }
    }

    private void Show(string heading, IReadOnlyList<BrowseRow> rows, BrowseRow? opened)
    {
        Heading = heading;
        Opened = opened;
        Rows.Clear();
        foreach (BrowseRow row in rows)
            Rows.Add(row);
        MarkHere();
        int songs = rows.Count(r => r.Kind == BrowseRowKind.Song);
        int here = rows.Count(r => r.IsHere);
        Say(rows.Count == 0 ? "Nothing here."
            : songs > 0 && here > 0 ? $"{(songs == 1 ? "1 song" : Format.Count(songs) + " songs")}, {Format.Count(here)} on this laptop."
            : songs == 0 ? "Double-click an album or artist to open it."
            : "Double-click a song to hear it in the headphones.", problem: false);
    }

    /// <summary>Marks the songs that are on this laptop, as the library knows now.</summary>
    public void MarkHere()
    {
        _here = _servers.LocalCopies(Server);
        foreach (BrowseRow row in Rows)
            row.LocalPath = row.Kind == BrowseRowKind.Song ? _here.GetValueOrDefault(row.Id) : null;
    }

    /// <summary>A song in the headphones: the copy here if there is one, else fetched for the preview only.</summary>
    public async Task PreviewAsync(BrowseRow row)
    {
        if (row.Song is not { } song)
            return;
        Say($"Getting {row.Title} for the headphones…", problem: false);
        string? problem = await _servers.PreviewAsync(Server, song, row.LocalPath);
        Say(problem ?? $"In the headphones: {row.Title}", problem: problem is not null);
    }

    /// <summary>Songs, albums or artists to keep on the laptop: chosen, then synced.</summary>
    public async Task DownloadAsync(IEnumerable<BrowseRow> rows)
    {
        var wanted = rows.Where(r => !r.IsHere).ToList();
        if (wanted.Count == 0)
        {
            Say("Those are on this laptop already.", problem: false);
            return;
        }
        if (!Server.CanChoose)
        {
            Say("This account can't download songs: whoever runs the server can allow it.", problem: true);
            return;
        }
        _servers.AddChoices(Server, wanted.Select(r => r.Choice));
        Say(wanted.Count == 1 ? $"Downloading {wanted[0].Title}…" : $"Downloading {Format.Count(wanted.Count)}…", problem: false);
        await _servers.SyncAsync(Server);
        MarkHere();
        Say(Server.SyncProblem ? Server.SyncText : "Downloaded: in the library, in GZJam from servers.", Server.SyncProblem);
    }

    public Task DownloadAllAsync() => Opened is { } opened ? DownloadAsync([opened]) : Task.CompletedTask;

    private void Say(string? text, bool problem)
    {
        Status = text;
        StatusProblem = problem;
    }
}
