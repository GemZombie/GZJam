using CommunityToolkit.Mvvm.ComponentModel;
using GZJam.Library;

namespace GZJam.App.ViewModels;

/// <summary>A server, as the library's Playlists panel needs it (#355): whether it answers, and how its playlist names make folders.</summary>
public sealed record PlaylistServer(long Id, string Name, bool IsOffline, string? Separator);

/// <summary>
/// A server's heading in the Playlists panel (#355), after the DJ's own playlists: its name and how
/// many playlists it has, folding away like a folder (remembered), and ↻ to read them again.
/// Offline, its playlists show as last read, and the heading says so.
/// </summary>
public sealed partial class ServerPlaylistsHeading : ObservableObject
{
    private readonly LibraryViewModel _owner;
    private bool _isExpanded;

    internal ServerPlaylistsHeading(LibraryViewModel owner, PlaylistServer server, bool isExpanded)
    {
        _owner = owner;
        Server = server;
        _isExpanded = isExpanded;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ServerId), nameof(Name), nameof(IsOffline), nameof(Tip))]
    public partial PlaylistServer Server { get; internal set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Tip))]
    public partial int Count { get; internal set; }

    public long ServerId => Server.Id;
    public string Name => Server.Name;
    public bool IsOffline => Server.IsOffline;

    public string Tip => IsOffline
        ? $"{Name} isn't answering: its playlists as last read"
        : Count == 1 ? $"{Name}'s 1 playlist" : $"{Name}'s {Count} playlists";

    /// <summary>Open, showing its playlists; folded, remembered between runs.</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (SetProperty(ref _isExpanded, value))
                _owner.ServerHeadingToggled(this);
        }
    }

    internal void SetExpandedQuietly(bool value)
    {
        _isExpanded = value;
        OnPropertyChanged(nameof(IsExpanded));
    }
}

/// <summary>
/// A folder in a server's playlist names (#355), "WCS" of "WCS/Setlists/Nov 8": only with the
/// server's <i>Playlist folders in names</i> on. Its rows under it while it's open (remembered).
/// </summary>
public sealed partial class ServerPlaylistFolderRow : ObservableObject
{
    private readonly LibraryViewModel _owner;
    private bool _isExpanded;

    internal ServerPlaylistFolderRow(LibraryViewModel owner, long serverId, IReadOnlyList<string> path, bool isExpanded)
    {
        _owner = owner;
        ServerId = serverId;
        Path = path;
        _isExpanded = isExpanded;
    }

    public long ServerId { get; }

    /// <summary>Its folders from the top, and its own name last.</summary>
    public IReadOnlyList<string> Path { get; }

    /// <summary>Which folder it is among the server's, for remembering it open.</summary>
    public string Key => KeyOf(ServerId, Path);

    internal static string KeyOf(long serverId, IReadOnlyList<string> path) => $"{serverId}:{string.Join('\u001f', path)}";

    public string Name => Path[^1];

    /// <summary>Under the server's heading: one step in, and one more for each folder it's in.</summary>
    public double Indent => Path.Count * 20;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Tip))]
    public partial int Count { get; internal set; }

    public string Tip => Count == 1 ? $"{Name}: 1 playlist" : $"{Name}: {Count} playlists";

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (SetProperty(ref _isExpanded, value))
                _owner.ServerFolderToggled(this);
        }
    }
}

/// <summary>
/// One of a server's playlists in the Playlists panel (#355), under its heading (and its folders,
/// with folders in names on). Opened, its songs show in the song list, those not on the computer
/// marked. Kept on the computer, it's marked so, and shown here only, not among the DJ's own too.
/// </summary>
/// <param name="Name">Its own name: the last part, with folders in names.</param>
/// <param name="FullName">Its name on the server.</param>
/// <param name="Depth">How far in: 1 under the heading, and one more for each folder it's in.</param>
/// <param name="Kept">The DJ's playlist it's kept on the computer as, if it is.</param>
public sealed record ServerPlaylistRow(long ServerId, string ServerName, string PlaylistId, string Name, string FullName, int Songs,
    int Depth, PlaylistInfo? Kept)
{
    public bool IsKept => Kept is not null;
    public bool IsNotKept => Kept is null;

    public double Indent => Depth * 20;

    public string Tip => IsKept
        ? $"{FullName}, on {ServerName}: kept on this computer, and up to date with each sync"
        : $"{FullName}, on {ServerName}";
}
