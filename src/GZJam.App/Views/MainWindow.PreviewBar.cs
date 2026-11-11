using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using GZJam.App.ViewModels;
using Track = GZJam.Library.Track;

namespace GZJam.App.Views;

/// <summary>
/// The song in the headphones, acted on from the preview bar under the song list (#386): a DJ
/// previews a song, moves on to another folder or search while it plays, then wants it in the
/// queue. QUEUE adds it; its grip (the headphones and title) drags it onto the queue, a deck or a
/// playlist; right-click or ⋯ opens its menu. The preview keeps playing throughout.
/// </summary>
public partial class MainWindow
{
    /// <summary>How far (px) a press on the bar's grip moves before it's a drag.</summary>
    private const double PreviewDragDistance = 4;

    private readonly ContextMenu _previewMenu = new();
    private (IPointer Pointer, Point At)? _previewPress;

    private void InitializePreviewBar()
    {
        LibraryPreview.ShowsSongActions = true;
        LibraryPreview.QueueKey.Click += (_, _) => QueuePreviewSong();
        LibraryPreview.MenuKey.Click += (_, _) => OpenPreviewMenu(LibraryPreview.MenuKey);
        LibraryPreview.AddHandler(ContextRequestedEvent, (_, e) =>
        {
            e.Handled = true;
            OpenPreviewMenu(LibraryPreview.Grip);
        });
        LibraryPreview.Grip.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                return;
            _previewPress = (e.Pointer, e.GetPosition(this));
            e.Pointer.Capture(LibraryPreview.Grip);   // moved off the grip quickly, it's still this press
        };
        LibraryPreview.Grip.PointerMoved += (_, e) =>
        {
            if (_previewPress is not { } press || press.Pointer != e.Pointer || PreviewSong is not { } song)
                return;
            Point at = e.GetPosition(this);
            if (((Vector)(at - press.At)).Length < PreviewDragDistance)
                return;
            _previewPress = null;
            StartTrackDrag(new TrackDrag([song], null), e.Pointer, at);
        };
        LibraryPreview.Grip.PointerReleased += (_, _) => _previewPress = null;
        LibraryPreview.Grip.PointerCaptureLost += (_, _) => _previewPress = null;
    }

    /// <summary>The song in the headphones, as its list has it; null when nothing is previewing.</summary>
    private Track? PreviewSong => Vm.Preview?.Song;

    /// <summary>QUEUE: the song in the headphones goes to the end of the queue, and keeps playing.</summary>
    internal void QueuePreviewSong()
    {
        if (PreviewSong is not { } song)
            return;
        bool already = Vm.Queue.Items.Any(i => i.Track.Path == song.Path);
        Vm.AddToQueue([song]);
        if (!already)   // a song there already has its own notice, with Undo
            Vm.ShowNotice($"In the queue: {song.DisplayTitle}", seconds: 3);
        Log.Info($"Preview bar: queued \"{song.DisplayTitle}\"");
    }

    /// <summary>The song in the headphones' menu, as its row in the song list has.</summary>
    internal ContextMenu PreviewMenu()
    {
        _previewMenu.Items.Clear();
        if (PreviewSong is not { } song)
            return _previewMenu;
        MenuItem Item(string header, Action click, string? gesture = null)
        {
            var item = new MenuItem { Header = header };
            if (gesture is not null)
                item.InputGesture = KeyGesture.Parse(gesture);
            item.Click += (_, _) => click();
            _previewMenu.Items.Add(item);
            return item;
        }
        Item("Add to queue", QueuePreviewSong);
        Item("Play next", () => Vm.AddToQueue([song], 0));
        _previewMenu.Items.Add(new Separator());
        for (int index = 0; index < Vm.ActiveDecks.Count; index++)
        {
            if (DeckAt(index) is not { } deck)
                continue;
            bool canLoad = Vm.CanLoad(deck);
            var load = Item(canLoad ? $"Load to Deck {index + 1}" : $"Load to Deck {deck.Label}  (on air)", () => _ = Vm.LoadToDeckAsync(deck, song));
            load.IsEnabled = canLoad;
        }
        _previewMenu.Items.Add(new Separator());
        var playlists = new MenuItem { Header = "Add to playlist" };
        PlaylistsMenu.Fill(playlists, Vm, [song]);
        _previewMenu.Items.Add(playlists);
        _previewMenu.Items.Add(new Separator());
        // As the song list's menu: a server's song isn't marked or looked into until it's here, and
        // one outside the music folders isn't in the tree.
        if (!song.IsOnServerOnly)
        {
            Item("Song info…", () => _ = OpenSongInfoAsync([song]));
            Item("Show in folder tree", () => ShowInTree(song)).IsEnabled = !song.IsOutsideLibrary;
        }
        return _previewMenu;
    }

    private void OpenPreviewMenu(Control target)
    {
        if (PreviewSong is null)
            return;
        PreviewMenu().Open(target);
    }
}
