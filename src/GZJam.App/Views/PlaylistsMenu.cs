using Avalonia.Controls;
using GZJam.App.ViewModels;
using GZJam.Library;

namespace GZJam.App.Views;

/// <summary>
/// The "Add to playlist" submenu for some songs: each playlist, then "New playlist…". Used in
/// the library and queue menus and on the decks (#207), so a song is added without finding it again.
/// </summary>
internal static class PlaylistsMenu
{
    /// <param name="except">A playlist left off (the one open in the song list, which has its own items).</param>
    public static void Fill(MenuItem menu, MainWindowViewModel vm, IReadOnlyList<Track> tracks, long? except = null)
    {
        menu.Items.Clear();
        foreach (PlaylistInfo playlist in vm.Library.Playlists.Where(p => p.Id != except))
        {
            var item = new MenuItem { Header = playlist.Name, IsEnabled = tracks.Count > 0 };
            item.Click += (_, _) => vm.AddToPlaylist(playlist, tracks);
            menu.Items.Add(item);
        }
        if (menu.Items.Count > 0)
            menu.Items.Add(new Separator());
        var create = new MenuItem { Header = "New playlist…", IsEnabled = tracks.Count > 0 };
        create.Click += (_, _) => _ = vm.CreatePlaylistAsync(tracks, open: false);
        menu.Items.Add(create);
    }
}
