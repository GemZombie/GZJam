using System.Collections.ObjectModel;
using GZJam.Library;
using GZJam.Library.Servers;

namespace GZJam.App.ViewModels;

/// <summary>
/// The Playlists panel as a tree (#354): folders first, then playlists, each sorted by name, a
/// folder's own rows under it while it's open. One list of rows, each indented for its place, as
/// the folder tree is (<see cref="FolderRows"/>): a folder row (<see cref="PlaylistFolderRow"/>)
/// or a playlist (<see cref="PlaylistInfo"/>). Rows are changed in place, so the list doesn't jump.
/// <para>
/// After the DJ's own, each server's playlists (#355): a heading for the server
/// (<see cref="ServerPlaylistsHeading"/>), then its playlists (<see cref="ServerPlaylistRow"/>),
/// in folders made from their names with the server's <i>Playlist folders in names</i> on
/// (<see cref="ServerPlaylistFolderRow"/>). One kept on the computer shows there only.
/// </para>
/// </summary>
public sealed partial class LibraryViewModel
{
    /// <summary>The panel's rows: the DJ's folders and playlists, then each server's heading, folders and playlists.</summary>
    public ObservableCollection<object> PlaylistRows { get; } = [];

    /// <summary>Every folder of playlists, alphabetically, as the library last said.</summary>
    public IReadOnlyList<PlaylistFolder> PlaylistFolders { get; private set; } = [];

    /// <summary>The folders left open: the settings' own list, so it's remembered between runs.</summary>
    public List<long> OpenPlaylistFolders { get; init; } = [];

    /// <summary>The servers whose playlists are folded away in the panel (#355): the settings' own list.</summary>
    public List<long> FoldedServerPlaylists { get; init; } = [];

    /// <summary>The servers' playlist folders left open (#355), by <see cref="ServerPlaylistFolderRow.Key"/>: the settings' own list.</summary>
    public List<string> OpenServerPlaylistFolders { get; init; } = [];

    /// <summary>The servers for the panel, as the app knows them now; none without servers.</summary>
    public Func<IReadOnlyList<PlaylistServer>>? PlaylistServers { get; set; }

    private readonly Dictionary<long, PlaylistFolderRow> _folderRows = [];

    /// <summary>The folders have been read from the library at least once.</summary>
    private bool _playlistFoldersRead;
    private readonly Dictionary<long, ServerPlaylistsHeading> _serverHeadings = [];
    private readonly Dictionary<string, ServerPlaylistFolderRow> _serverFolderRows = [];

    /// <summary>
    /// The folder last chosen in the panel (its row clicked), where a new playlist goes; else the
    /// open playlist's folder; else the top.
    /// </summary>
    public PlaylistFolderRow? ChosenPlaylistFolder { get; set; }

    /// <summary>Where a new playlist goes (#354): the folder chosen, or the open playlist's; null for the top.</summary>
    public long? NewPlaylistFolder =>
        ChosenPlaylistFolder is { } chosen && _folderRows.ContainsKey(chosen.Id) ? chosen.Id : SelectedPlaylist?.FolderId;

    /// <summary>The row the panel shows chosen: the open playlist, the DJ's or a server's. A folder's row only opens or closes it.</summary>
    public object? SelectedPlaylistRow
    {
        get => (object?)SelectedPlaylist ?? SelectedServerPlaylist;
        set
        {
            switch (value)
            {
                case PlaylistInfo playlist:
                    ChosenPlaylistFolder = null;
                    SelectedPlaylist = playlist;
                    break;
                case ServerPlaylistRow serverPlaylist:
                    ChosenPlaylistFolder = null;
                    SelectedServerPlaylist = serverPlaylist;
                    break;
                case PlaylistFolderRow folder:
                    ChosenPlaylistFolder = folder;
                    OnPropertyChanged();   // the open playlist stays chosen
                    break;
                default:
                    OnPropertyChanged();
                    break;
            }
        }
    }

    /// <summary>A folder row's arrow: what's in it comes or goes, and it's remembered.</summary>
    internal void PlaylistFolderToggled(PlaylistFolderRow row)
    {
        OpenPlaylistFolders.Remove(row.Id);
        if (row.IsExpanded)
            OpenPlaylistFolders.Add(row.Id);
        SyncPlaylistRows();
    }

    internal void ServerHeadingToggled(ServerPlaylistsHeading heading)
    {
        FoldedServerPlaylists.Remove(heading.ServerId);
        if (!heading.IsExpanded)
            FoldedServerPlaylists.Add(heading.ServerId);
        SyncPlaylistRows();
    }

    internal void ServerFolderToggled(ServerPlaylistFolderRow folder)
    {
        OpenServerPlaylistFolders.Remove(folder.Key);
        if (folder.IsExpanded)
            OpenServerPlaylistFolders.Add(folder.Key);
        SyncPlaylistRows();
    }

    /// <summary>The servers' playlists may have changed (read again, kept, a server added or gone): the panel follows.</summary>
    public void RefreshServerPlaylists() => SyncPlaylistRows();

    /// <summary>Opens the folders a playlist is in, so its row shows (opened from elsewhere).</summary>
    private void ShowPlaylistRow(PlaylistInfo playlist)
    {
        bool opened = false;
        var parents = PlaylistFolders.ToDictionary(f => f.Id, f => f.ParentId);
        int steps = 0;
        for (long? at = playlist.FolderId; at is { } here && steps++ <= parents.Count; at = parents.GetValueOrDefault(here))
        {
            if (!OpenPlaylistFolders.Contains(here))
            {
                OpenPlaylistFolders.Add(here);
                if (_folderRows.TryGetValue(here, out var row))
                    row.SetExpandedQuietly(true);
                opened = true;
            }
        }
        if (opened)
            SyncPlaylistRows();
    }

    /// <summary>The rows made to match the folders and playlists, changed only where they differ.</summary>
    private void SyncPlaylistRows()
    {
        var servers = PlaylistServers?.Invoke() ?? [];
        // A server's playlist kept on the computer shows under its server, not among the DJ's own too.
        var keptBy = new Dictionary<(long Server, string Playlist), PlaylistInfo>();
        foreach (PlaylistServer server in servers)
        {
            foreach (ServerChoice choice in _database.GetServerChoices(server.Id))
            {
                if (choice is { Kind: ServerChoiceKind.Playlist, PlaylistId: { } id } && Playlists.FirstOrDefault(p => p.Id == id) is { } kept)
                    keptBy[(server.Id, choice.RemoteId)] = kept;
            }
        }
        var keptIds = keptBy.Values.Select(p => p.Id).ToHashSet();

        var folders = PlaylistFolders;
        foreach (long gone in _folderRows.Keys.Where(id => folders.All(f => f.Id != id)).ToList())
            _folderRows.Remove(gone);
        // Folders deleted are forgotten as open, once they've been read: not before (a server's
        // playlists can come first, as GZJam starts).
        if (_playlistFoldersRead)
            OpenPlaylistFolders.RemoveAll(id => folders.All(f => f.Id != id));
        var children = folders.ToLookup(f => f.ParentId);
        var playlists = Playlists.Where(p => !keptIds.Contains(p.Id)).ToLookup(p => p.FolderId);
        foreach (PlaylistFolder folder in folders)
        {
            bool open = OpenPlaylistFolders.Contains(folder.Id);
            if (!_folderRows.TryGetValue(folder.Id, out var row))
                _folderRows[folder.Id] = row = new PlaylistFolderRow(this, folder, open);
            else
            {
                if (row.Folder != folder)
                    row.Folder = folder;
                if (row.IsExpanded != open)
                    row.SetExpandedQuietly(open);   // opened for something put in it
            }
            row.HasChildren = children[folder.Id].Any() || playlists[folder.Id].Any();
        }

        var wanted = new List<object>(folders.Count + Playlists.Count);
        void Add(long? parent)
        {
            foreach (PlaylistFolder folder in children[parent])   // already by name
            {
                PlaylistFolderRow row = _folderRows[folder.Id];
                wanted.Add(row);
                if (row.IsExpanded)
                    Add(folder.Id);
            }
            wanted.AddRange(playlists[parent]);   // by name too
        }
        Add(null);
        foreach (PlaylistServer server in servers)
            AddServer(wanted, server, keptBy);
        foreach (long gone in _serverHeadings.Keys.Where(id => servers.All(s => s.Id != id)).ToList())
            _serverHeadings.Remove(gone);

        for (int i = PlaylistRows.Count - 1; i >= 0; i--)
        {
            if (!wanted.Any(w => SameRow(w, PlaylistRows[i])))
                PlaylistRows.RemoveAt(i);
        }
        for (int i = 0; i < wanted.Count; i++)
        {
            int at = -1;
            for (int j = i; j < PlaylistRows.Count; j++)
            {
                if (SameRow(PlaylistRows[j], wanted[i]))
                {
                    at = j;
                    break;
                }
            }
            if (at < 0)
                PlaylistRows.Insert(i, wanted[i]);
            else
            {
                if (at != i)
                    PlaylistRows.Move(at, i);
                if (!Equals(PlaylistRows[i], wanted[i]))
                    PlaylistRows[i] = wanted[i];
            }
        }
        // The server playlist open is its row as it is now (a new one if it changed).
        if (SelectedServerPlaylist is { } shown && wanted.OfType<ServerPlaylistRow>().FirstOrDefault(r => SameRow(r, shown)) is { } now)
            _selectedServerPlaylist = now;
        OnPropertyChanged(nameof(SelectedPlaylistRow));
    }

    /// <summary>A server's heading, and while it's open its folders and playlists.</summary>
    private void AddServer(List<object> wanted, PlaylistServer server, Dictionary<(long Server, string Playlist), PlaylistInfo> keptBy)
    {
        var copies = _database.GetServerPlaylists(server.Id);
        bool open = !FoldedServerPlaylists.Contains(server.Id);
        if (!_serverHeadings.TryGetValue(server.Id, out var heading))
            _serverHeadings[server.Id] = heading = new ServerPlaylistsHeading(this, server, open);
        else
        {
            if (heading.Server != server)
                heading.Server = server;
            if (heading.IsExpanded != open)
                heading.SetExpandedQuietly(open);
        }
        heading.Count = copies.Count;
        wanted.Add(heading);
        if (!open)
            return;

        var split = copies
            .Select(c =>
            {
                var (folders, name) = PlaylistNames.Split(c.Name, server.Separator);
                return (Copy: c, Folders: folders, Name: name);
            })
            .ToList();
        void Add(IReadOnlyList<string> path)
        {
            // Folders directly in this one, by name; then its playlists, by name.
            var inside = split.Where(s => s.Folders.Count > path.Count && s.Folders.Take(path.Count).SequenceEqual(path)).ToList();
            foreach (string name in inside.Select(s => s.Folders[path.Count]).Distinct().Order(StringComparer.CurrentCultureIgnoreCase))
            {
                IReadOnlyList<string> folderPath = [.. path, name];
                string key = ServerPlaylistFolderRow.KeyOf(server.Id, folderPath);
                bool folderOpen = OpenServerPlaylistFolders.Contains(key);
                if (!_serverFolderRows.TryGetValue(key, out var row))
                    _serverFolderRows[key] = row = new ServerPlaylistFolderRow(this, server.Id, folderPath, folderOpen);
                row.Count = inside.Count(s => s.Folders[path.Count] == name);
                wanted.Add(row);
                if (row.IsExpanded)
                    Add(folderPath);
            }
            foreach (var (copy, _, name) in split.Where(s => s.Folders.SequenceEqual(path)).OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                wanted.Add(new ServerPlaylistRow(server.Id, server.Name, copy.Id, name, copy.Name, copy.SongIds.Count, path.Count + 1,
                    keptBy.GetValueOrDefault((server.Id, copy.Id))));
            }
        }
        Add([]);
    }

    private static bool SameRow(object a, object b) => (a, b) switch
    {
        (PlaylistInfo x, PlaylistInfo y) => x.Id == y.Id,
        (PlaylistFolderRow x, PlaylistFolderRow y) => x.Id == y.Id,
        (ServerPlaylistsHeading x, ServerPlaylistsHeading y) => x.ServerId == y.ServerId,
        (ServerPlaylistFolderRow x, ServerPlaylistFolderRow y) => x.Key == y.Key,
        (ServerPlaylistRow x, ServerPlaylistRow y) => x.ServerId == y.ServerId && x.PlaylistId == y.PlaylistId,
        _ => false,
    };
}
