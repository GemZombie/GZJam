using Avalonia.Controls;
using GZJam.App.ViewModels;
using GZJam.Library;

namespace GZJam.App.Views;

/// <summary>
/// The "Add to playlist" submenu for some songs: each playlist, then "New playlist…". Used in
/// the library and queue menus and on the decks (#207), so a song is added without finding it again.
/// Folders of playlists (#354) are submenus, as in the Playlists panel: folders first, then
/// playlists, by name; a folder with no playlist in it, all the way down, is left out.
/// </summary>
internal static class PlaylistsMenu
{
    /// <param name="except">A playlist left off (the one open in the song list, which has its own items).</param>
    public static void Fill(MenuItem menu, MainWindowViewModel vm, IReadOnlyList<Track> tracks, long? except = null)
    {
        menu.Items.Clear();
        var folders = vm.Library.PlaylistFolders.ToLookup(f => f.ParentId);
        var playlists = vm.Library.Playlists.Where(p => p.Id != except).ToLookup(p => p.FolderId);

        bool AddLevel(ItemsControl into, long? parent)
        {
            bool any = false;
            foreach (PlaylistFolder folder in folders[parent])
            {
                var sub = new MenuItem { Header = folder.Name };
                if (AddLevel(sub, folder.Id))
                {
                    into.Items.Add(sub);
                    any = true;
                }
            }
            foreach (PlaylistInfo playlist in playlists[parent])
            {
                var item = new MenuItem { Header = playlist.Name, IsEnabled = tracks.Count > 0 };
                item.Click += (_, _) => vm.AddToPlaylist(playlist, tracks);
                into.Items.Add(item);
                any = true;
            }
            return any;
        }

        if (AddLevel(menu, null))
            menu.Items.Add(new Separator());
        var create = new MenuItem { Header = "New playlist…", IsEnabled = tracks.Count > 0 };
        create.Click += (_, _) => _ = vm.CreatePlaylistAsync(tracks, open: false);
        menu.Items.Add(create);
    }
}
