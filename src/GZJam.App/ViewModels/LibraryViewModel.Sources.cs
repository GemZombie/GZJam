using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using GZJam.Library;

namespace GZJam.App.ViewModels;

/// <summary>Where songs come from, each apart from the others (#318).</summary>
public enum LibrarySource
{
    /// <summary>The DJ's music folders, and every song added to them from another source.</summary>
    MyMusic,

    /// <summary>The music servers, each a node of the tree, with their songs on this computer.</summary>
    Servers,

    /// <summary>The computer's files and any USB stick, as folders: never searched, a folder filtered by name.</summary>
    Files,
}

/// <summary>A source in the library column's list: its name and how many songs it has.</summary>
public sealed partial class SourceViewModel(LibrarySource kind, string name) : ObservableObject
{
    public LibrarySource Kind { get; } = kind;
    public string Name { get; } = name;
    public bool IsMyMusic => Kind == LibrarySource.MyMusic;
    public bool IsServers => Kind == LibrarySource.Servers;
    public bool IsFiles => Kind == LibrarySource.Files;

    [ObservableProperty]
    public partial string CountText { get; set; } = "";

    /// <summary>Its folders are the tree's (a folder in it may be what's showing, rather than the source itself).</summary>
    [ObservableProperty]
    public partial bool IsCurrent { get; set; }
}

/// <summary>
/// The library's sources (#318): My music and Servers, each with its own folders in the tree and
/// its own search. The servers' downloads folder is in neither as a folder: it's where GZJam keeps
/// the servers' songs for playing offline, which show under their servers.
/// </summary>
public sealed partial class LibraryViewModel
{
    /// <summary>My music, Servers once there's a server, and Files.</summary>
    public ObservableCollection<SourceViewModel> Sources { get; } = [new(LibrarySource.MyMusic, "My music"), new(LibrarySource.Files, "Files")];

    /// <summary>The source whose folders the tree shows, and which a search covers.</summary>
    [ObservableProperty]
    public partial LibrarySource Source { get; private set; }

    /// <summary>The servers' downloads folder (#318): in the index, so its songs keep what the DJ gives them, but not My music.</summary>
    public Func<LibraryRoot, bool> IsServersRoot { get; set; } = _ => false;

    /// <summary>My music's folders, by path (not the servers' downloads): as of the tree's last making.</summary>
    public IReadOnlyList<string> MusicFolders { get; private set; } = [];

    /// <summary>The music folders that are the servers' downloads, left out of My music: as of the tree's last making.</summary>
    private HashSet<long> _serversRoots = [];

    /// <summary>
    /// The source chosen in its list: what the song list shows when no folder or playlist is.
    /// Choosing it shows all of it, whatever folder was showing.
    /// </summary>
    public SourceViewModel? SelectedSource
    {
        get => SelectedPlaylist is null && SelectedFolder is null ? Sources.FirstOrDefault(s => s.Kind == Source) : null;
        set
        {
            if (value is not null)
                ChooseSource(value.Kind);
        }
    }

    /// <summary>All of a source: no folder or playlist, the search kept.</summary>
    public void ChooseSource(LibrarySource source)
    {
        bool suppressed = _suppressSearch;
        _suppressSearch = true;
        SelectedPlaylist = null;
        SelectedFolder = null;
        Source = source;
        _suppressSearch = suppressed;
        OnPlaylistShown();
        OnSourceShown();
        if (!_suppressSearch)
            RunSearch();
    }

    partial void OnSourceChanged(LibrarySource value)
    {
        SyncFolderRows();
        OnSourceShown();
        if (!_suppressSearch && SelectedFolder is null && SelectedPlaylist is null)
            RunSearch();   // all of the source showing: the other one's now
    }

    /// <summary>The tree shows the source <paramref name="folder"/> is in, so it has a row (Show in folder tree).</summary>
    internal void ShowSourceOf(FolderNodeViewModel folder) =>
        Source = SourceOf(folder);

    private static LibrarySource SourceOf(FolderNodeViewModel folder) =>
        folder.IsServer ? LibrarySource.Servers : folder.IsFiles ? LibrarySource.Files : LibrarySource.MyMusic;

    private void OnSourceShown()
    {
        foreach (SourceViewModel source in Sources)
            source.IsCurrent = source.Kind == Source;
        OnPropertyChanged(nameof(SelectedSource));
        OnPropertyChanged(nameof(HasNoRoots));
        OnPropertyChanged(nameof(HasNoResults));
        OnPropertyChanged(nameof(IsServerShowing));
        OnPropertyChanged(nameof(SearchPlaceholder));
        OnPropertyChanged(nameof(CanFilter));
        OnPropertyChanged(nameof(ClearScopeTip));
        OnPropertyChanged(nameof(EmptyMessage));
    }

    /// <summary>Whether a root of the tree is in the source the tree shows.</summary>
    private bool InSource(FolderNodeViewModel root) => SourceOf(root) == Source;

    /// <summary>The search box's hint: what it searches.</summary>
    public string SearchPlaceholder => SelectedPlaylist is not null ? "Search, or filter: bpm:130-150  artist:basie  rating:yes"
        : Source == LibrarySource.Servers ? "Search your servers"
        : Source == LibrarySource.Files ? "Filter this folder's songs by name"
        : "Search, or filter: bpm:130-150  artist:basie  rating:yes";

    /// <summary>The search's filters (BPM, Label, Filter, the search help): for every source but Files, whose box filters by name only.</summary>
    public bool CanFilter => SelectedPlaylist is not null || Source != LibrarySource.Files;

    public string ClearScopeTip => Source switch
    {
        LibrarySource.Servers => "Search every server",
        LibrarySource.Files => "Back to the computer and its USB sticks",
        _ => "Search all of My music",
    };

    /// <summary>The sources' counts, and Servers in the list once there's a server: after the tree is made.</summary>
    private void RefreshSources()
    {
        var servers = Roots.Where(r => r.IsServerItself).ToList();
        int mine = Roots.Where(r => r.IsFolder).Sum(r => r.Node.TrackCount);
        Sources[0].CountText = Format.Count(mine);
        SourceViewModel? listed = Sources.FirstOrDefault(s => s.IsServers);
        if (servers.Count > 0)
        {
            if (listed is null)
                Sources.Insert(1, listed = new SourceViewModel(LibrarySource.Servers, "Servers") { IsCurrent = Source == LibrarySource.Servers });
            listed.CountText = servers.Sum(s => s.Node.TrackCount) is var n and > 0 ? Format.Count(n) : "";
        }
        else if (listed is not null)
        {
            Sources.Remove(listed);
            if (Source == LibrarySource.Servers)
                ChooseSource(LibrarySource.MyMusic);
        }
        OnPropertyChanged(nameof(SelectedSource));
    }

    // ───────────── Search across every server ─────────────

    /// <summary>Every server's songs for the song list (Servers, no server chosen): one after another, in the tree's order.</summary>
    private async Task RunServersSearchAsync(string text, int version, object key, CancellationToken cancel)
    {
        if (BrowseServer is null)
            return;
        var rows = new List<Track>();
        string? problem = null, note = null;
        foreach (long serverId in Roots.Where(r => r.IsServerItself).Select(r => r.Node.ServerId!.Value).ToList())
        {
            ServerBrowse found;
            try
            {
                found = await BrowseServer(serverId, "", text, cancel);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            if (version != _searchVersion)
                return;
            rows.AddRange(found.Tracks);
            problem ??= found.Problem;
            note ??= found.Note;
        }
        DidYouMean = null;
        DidYouMeanText = null;
        ServerProblem = rows.Count == 0 ? problem : null;
        ServerNote = note;
        ShowResults(OnlyHere ? rows.Where(t => !t.IsOnServerOnly).ToList() : rows, key);
        IsSearching = false;
        OnPropertyChanged(nameof(EmptyMessage));
        if (text.Trim().Length > 1)
            _ = FindAlsoInMyMusicAsync(text, version, cancel);
    }

    /// <summary>Under a search of the servers: how many songs of My music it finds, a tap away (#318).</summary>
    private async Task FindAlsoInMyMusicAsync(string text, int version, CancellationToken cancel)
    {
        var notIn = _serversRoots;
        double near = NearBpm;
        int count;
        try
        {
            count = await Task.Run(() => _database.Search(text, null, cancel, near, notInRoots: notIn).Count, cancel);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        if (version != _searchVersion || cancel.IsCancellationRequested)
            return;
        _alsoOnServer = null;
        _alsoInMyMusic = count > 0;
        AlsoOnText = count > 0 ? $"Also: {Format.Count(count)} in My music" : null;
    }

    /// <summary>The "Also:" line points to My music, not a server.</summary>
    private bool _alsoInMyMusic;

    /// <summary>
    /// A song of the servers' downloads folder shown in the tree (#318): its folder on its server,
    /// as the server lays it out. Null for any other song.
    /// </summary>
    public Func<Track, (long ServerId, string Folder)?> ServerFolderOf { get; set; } = _ => null;

    /// <summary>
    /// The servers' downloads folder isn't in My music: a remembered folder of it (from 1.4) is
    /// forgotten, and its songs shown under their servers.
    /// </summary>
    private bool IsServersRootId(long rootId) => _serversRoots.Contains(rootId);
}
