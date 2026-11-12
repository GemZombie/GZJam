using System.Collections.ObjectModel;
using GZJam.Library;

namespace GZJam.App.ViewModels;

/// <summary>
/// The Playlists panel as a tree (#354): folders first, then playlists, each sorted by name, a
/// folder's own rows under it while it's open. One list of rows, each indented for its place, as
/// the folder tree is (<see cref="FolderRows"/>): a folder row (<see cref="PlaylistFolderRow"/>)
/// or a playlist (<see cref="PlaylistInfo"/>). Rows are changed in place, so the list doesn't jump.
/// </summary>
public sealed partial class LibraryViewModel
{
    /// <summary>The panel's rows: <see cref="PlaylistFolderRow"/>s and <see cref="PlaylistInfo"/>s.</summary>
    public ObservableCollection<object> PlaylistRows { get; } = [];

    /// <summary>Every folder of playlists, alphabetically, as the library last said.</summary>
    public IReadOnlyList<PlaylistFolder> PlaylistFolders { get; private set; } = [];

    /// <summary>The folders left open: the settings' own list, so it's remembered between runs.</summary>
    public List<long> OpenPlaylistFolders { get; init; } = [];

    private readonly Dictionary<long, PlaylistFolderRow> _folderRows = [];

    /// <summary>
    /// The folder last chosen in the panel (its row clicked), where a new playlist goes; else the
    /// open playlist's folder; else the top.
    /// </summary>
    public PlaylistFolderRow? ChosenPlaylistFolder { get; set; }

    /// <summary>Where a new playlist goes (#354): the folder chosen, or the open playlist's; null for the top.</summary>
    public long? NewPlaylistFolder =>
        ChosenPlaylistFolder is { } chosen && _folderRows.ContainsKey(chosen.Id) ? chosen.Id : SelectedPlaylist?.FolderId;

    /// <summary>The row the panel shows chosen: the open playlist. A folder's row only opens or closes it.</summary>
    public object? SelectedPlaylistRow
    {
        get => SelectedPlaylist;
        set
        {
            if (value is PlaylistInfo playlist)
            {
                ChosenPlaylistFolder = null;
                SelectedPlaylist = playlist;
            }
            else if (value is PlaylistFolderRow folder)
            {
                ChosenPlaylistFolder = folder;
                OnPropertyChanged();   // the open playlist stays chosen
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
        var folders = PlaylistFolders;
        foreach (long gone in _folderRows.Keys.Where(id => folders.All(f => f.Id != id)).ToList())
            _folderRows.Remove(gone);
        OpenPlaylistFolders.RemoveAll(id => folders.All(f => f.Id != id));
        var children = folders.ToLookup(f => f.ParentId);
        var playlists = Playlists.ToLookup(p => p.FolderId);
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
        OnPropertyChanged(nameof(SelectedPlaylistRow));
    }

    private static bool SameRow(object a, object b) => (a, b) switch
    {
        (PlaylistInfo x, PlaylistInfo y) => x.Id == y.Id,
        (PlaylistFolderRow x, PlaylistFolderRow y) => x.Id == y.Id,
        _ => false,
    };
}
