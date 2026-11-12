using GZJam.Library;

namespace GZJam.App.ViewModels;

/// <summary>
/// Folders of playlists (#354): made, renamed, moved and deleted from the Playlists panel, and
/// playlists moved between them. A folder holds folders and playlists, as deep as the DJ likes; a
/// name only has to be unique beside its own.
/// </summary>
public sealed partial class MainWindowViewModel
{
    /// <summary>Asks for a name, then makes a folder of playlists, in <paramref name="parentId"/> or at the top, and shows it.</summary>
    public async Task<PlaylistFolder?> CreatePlaylistFolderAsync(long? parentId = null)
    {
        if (PromptAsync is null
            || await PromptAsync("New folder", "For playlists that go together: setlists, practice, other DJs'.", "Make it", "") is not { } name
            || string.IsNullOrWhiteSpace(name))
            return null;
        PlaylistFolder folder = _database.CreatePlaylistFolder(name, parentId);
        Log.Info($"Playlist folder \"{folder.Name}\" made{(parentId is null ? "" : $" in {FolderPathText(parentId)}")}");
        OpenFolder(parentId);
        OpenFolder(folder.Id);   // made to put things in
        Library.RefreshPlaylists();
        return folder;
    }

    public async Task RenamePlaylistFolderAsync(PlaylistFolderRow folder)
    {
        if (PromptAsync is null || await PromptAsync($"Rename \"{folder.Name}\"", PlaylistsText(folder.Count), "Rename", folder.Name) is not { } name
            || string.IsNullOrWhiteSpace(name))
            return;
        string stored = _database.RenamePlaylistFolder(folder.Id, name);
        Log.Info($"Playlist folder \"{folder.Name}\" renamed \"{stored}\"");
        Library.RefreshPlaylists();
    }

    /// <summary>
    /// Deletes a folder, its folders and their playlists, after asking (saying how many go). The
    /// songs stay in the library, as when a playlist is deleted.
    /// </summary>
    public async Task DeletePlaylistFolderAsync(PlaylistFolderRow folder)
    {
        var inside = FoldersWithin(folder.Id);
        var playlists = Library.Playlists.Where(p => p.FolderId is { } f && inside.Contains(f)).ToList();
        string message = playlists.Count == 0
            ? "There's nothing in it."
            : $"The {PlaylistsText(playlists.Count)} in it {(playlists.Count == 1 ? "goes" : "go")} with it. Their songs stay in your library; only the lists go.";
        if (ConfirmAsync is not null && !await ConfirmAsync($"Delete the folder \"{folder.Name}\"?", message, "Delete"))
            return;
        if (ConfirmAsync is null && playlists.Count > 0)
            return;   // never deleted unasked
        if (Library.SelectedPlaylist is { } open && playlists.Any(p => p.Id == open.Id))
            Library.OpenPlaylist(null);
        _database.DeletePlaylistFolder(folder.Id);
        foreach (PlaylistInfo playlist in playlists)
            _settings.ListSorts.Remove(LibraryViewModel.PlaylistSortKey(playlist.Id));
        if (Library.ChosenPlaylistFolder?.Id is { } chosen && inside.Contains(chosen))
            Library.ChosenPlaylistFolder = null;
        Log.Info($"Playlist folder \"{folder.Name}\" deleted, with {PlaylistsText(playlists.Count)}");
        Library.RefreshPlaylists();
    }

    /// <summary>Moves a playlist into a folder, or to the top (null), and shows it there.</summary>
    public void MovePlaylist(PlaylistInfo playlist, long? folderId)
    {
        if (playlist.FolderId == folderId)
            return;
        string stored = _database.MovePlaylist(playlist.Id, folderId);
        Log.Info($"Playlist \"{playlist.Name}\" moved to {FolderPathText(folderId)}{(stored != playlist.Name ? $", as \"{stored}\" (the name was taken there)" : "")}");
        if (stored != playlist.Name)
            ShowNotice($"There's a \"{playlist.Name}\" in {FolderPathText(folderId)} already, so this one is \"{stored}\".", seconds: 6);
        OpenFolder(folderId);
        Library.RefreshPlaylists();
    }

    /// <summary>Moves a folder into another, or to the top (null). Not into itself, or a folder inside it.</summary>
    public void MovePlaylistFolder(PlaylistFolderRow folder, long? parentId)
    {
        if (folder.Folder.ParentId == parentId)
            return;
        if (!_database.MovePlaylistFolder(folder.Id, parentId))
        {
            ShowNotice($"\"{folder.Name}\" can't go inside itself.", seconds: 5);
            return;
        }
        Log.Info($"Playlist folder \"{folder.Name}\" moved to {FolderPathText(parentId)}");
        OpenFolder(parentId);
        Library.RefreshPlaylists();
    }

    /// <summary>A folder and every folder inside it, by id.</summary>
    internal HashSet<long> FoldersWithin(long folderId)
    {
        var within = new HashSet<long> { folderId };
        var children = Library.PlaylistFolders.ToLookup(f => f.ParentId);
        var stack = new Stack<long>([folderId]);
        while (stack.Count > 0)
        {
            foreach (PlaylistFolder child in children[stack.Pop()])
            {
                if (within.Add(child.Id))
                    stack.Push(child.Id);
            }
        }
        return within;
    }

    /// <summary>"Setlists › 2026", or "the top".</summary>
    internal string FolderPathText(long? folderId) =>
        folderId is null ? "the top" : string.Join(" › ", _database.PlaylistFolderPath(folderId));

    /// <summary>A folder something was put in opens, so it shows.</summary>
    private void OpenFolder(long? folderId)
    {
        if (folderId is { } id && !Library.OpenPlaylistFolders.Contains(id))
            Library.OpenPlaylistFolders.Add(id);
    }

    private static string PlaylistsText(int count) => count == 1 ? "1 playlist" : $"{count} playlists";
}
