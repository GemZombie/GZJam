using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using GZJam.App.Views;
using GZJam.Library;

namespace GZJam.App.Tests;

/// <summary>Songs dragged in from the file manager: onto a deck, into the queue (#99), or onto a playlist (#229).</summary>
public sealed class FileDropTests
{
    /// <summary>Files and folders as the file manager hands them over.</summary>
    private static DataTransfer Files(AppHarness app, params string[] paths)
    {
        var storage = app.Window.StorageProvider;
        var data = new DataTransfer();
        foreach (string path in paths)
        {
            IStorageItem? item = Directory.Exists(path)
                ? AppHarness.Await(storage.TryGetFolderFromPathAsync(path))
                : AppHarness.Await(storage.TryGetFileFromPathAsync(path));
            data.Add(DataTransferItem.CreateFile(item ?? throw new InvalidOperationException($"No storage item for {path}")));
        }
        return data;
    }

    private static Point Center(AppHarness app, Control control) =>
        control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), app.Window)!.Value;

    private static Point DeckPoint(AppHarness app, string name = "Deck1View") =>
        Center(app, app.Window.FindControl<DeckView>(name)!);

    private static Point QueuePoint(AppHarness app) => Center(app, app.Window.FindControl<Panel>("QueueArea")!);

    private static Point PlaylistPoint(AppHarness app, string name)
    {
        var item = app.Window.FindControl<ListBox>("PlaylistList")!.GetVisualDescendants().OfType<ListBoxItem>()
            .First(i => ((PlaylistInfo)i.DataContext!).Name == name);
        return item.TranslatePoint(new Point(40, item.Bounds.Height / 2), app.Window)!.Value;
    }

    /// <summary>Drags <paramref name="data"/> in from outside the window, over <paramref name="at"/>, as the system does.</summary>
    private static void DragOver(AppHarness app, Point at, DataTransfer data)
    {
        app.Window.DragDrop(at, RawDragEventType.DragEnter, data, DragDropEffects.Copy, RawInputModifiers.None);
        app.Window.DragDrop(at, RawDragEventType.DragOver, data, DragDropEffects.Copy, RawInputModifiers.None);
    }

    /// <summary>The same, then lets go there.</summary>
    private static void Drop(AppHarness app, Point at, DataTransfer data)
    {
        DragOver(app, at, data);
        app.Window.DragDrop(at, RawDragEventType.Drop, data, DragDropEffects.Copy, RawInputModifiers.None);
        AppHarness.Settle(300);
    }

    [AvaloniaFact]
    public void A_song_from_outside_the_library_plays_on_a_deck_without_joining_the_library()
    {
        using var app = new AppHarness();
        string outside = app.ToneTrack("From a USB stick").Path;   // in the harness's work folder, not the music folder
        int songs = app.Database.GetTrackCount();

        Drop(app, DeckPoint(app), Files(app, outside));

        Assert.Equal(outside, app.ViewModel.Deck1!.Track?.Path);
        Assert.Equal("From a USB stick", app.ViewModel.Deck1.Track!.DisplayTitle);
        Assert.Null(app.Database.FindTrack(outside));
        Assert.Equal(songs, app.Database.GetTrackCount());
    }

    [AvaloniaFact]
    public void Files_and_a_folder_go_into_the_queue_in_order_and_library_songs_keep_their_marks()
    {
        using var app = new AppHarness();
        Track library = app.Library.Results[0];
        app.ViewModel.SetRating([library], Rating.Loved);
        string folder = Path.Combine(app.WorkDirectory, "Brought along");
        Directory.CreateDirectory(folder);
        foreach (string name in new[] { "b.wav", "a.wav" })
            File.Copy(app.ToneTrack(name[..1]).Path, Path.Combine(folder, name));
        File.WriteAllText(Path.Combine(folder, "cover.jpg"), "not a song");   // left out quietly

        Drop(app, QueuePoint(app), Files(app, library.Path, folder));

        var queued = app.ViewModel.Queue.Items.Select(i => i.Track).ToList();
        Assert.Equal([library.Path, Path.Combine(folder, "a.wav"), Path.Combine(folder, "b.wav")], queued.Select(t => t.Path));
        Assert.Equal(Rating.Loved, queued[0].Rating);
        Assert.Null(app.ViewModel.Notice);
    }

    [AvaloniaFact]
    public void A_deck_lights_up_for_one_song_but_not_for_several()
    {
        using var app = new AppHarness();
        string one = app.ToneTrack("One").Path, two = app.ToneTrack("Two").Path;
        var deck = app.Window.FindControl<DeckView>("Deck1View")!;
        var frame = deck.FindControl<Border>("Frame")!;

        DragOver(app, DeckPoint(app), Files(app, one));
        Assert.Contains("drop-ok", frame.Classes);
        app.Window.DragDrop(DeckPoint(app), RawDragEventType.DragLeave, Files(app, one), DragDropEffects.Copy, RawInputModifiers.None);
        Assert.DoesNotContain("drop-ok", frame.Classes);

        DragOver(app, DeckPoint(app), Files(app, one, two));
        Assert.Contains("drop-blocked", frame.Classes);
        app.Window.DragDrop(DeckPoint(app), RawDragEventType.Drop, Files(app, one, two), DragDropEffects.Copy, RawInputModifiers.None);
        AppHarness.Settle(300);
        Assert.DoesNotContain("drop-blocked", frame.Classes);
        Assert.Null(app.ViewModel.Deck1!.Track);
        Assert.Equal("A deck takes one song. Drop several on the queue.", app.ViewModel.Notice);
    }

    /// <summary>
    /// Songs from the file manager go onto a playlist's name, lit up as they come over, from
    /// anywhere (#237): one from outside the music folders joins the playlist, not the library.
    /// </summary>
    [AvaloniaFact]
    public void Files_dropped_on_a_playlists_name_are_added_even_from_outside_the_library()
    {
        using var app = new AppHarness();
        var songs = app.Library.Results.Take(2).ToList();
        app.ViewModel.CreatePlaylist("Saturday", []);
        AppHarness.Settle(150);
        string outside = app.ToneTrack("From a USB stick").Path;
        var data = Files(app, songs[1].Path, songs[0].Path, outside);

        DragOver(app, PlaylistPoint(app, "Saturday"), data);
        Assert.Contains(app.Window.FindControl<ListBox>("PlaylistList")!.GetVisualDescendants().OfType<ListBoxItem>(),
            i => i.Classes.Contains("drop-target"));
        app.Window.DragDrop(PlaylistPoint(app, "Saturday"), RawDragEventType.Drop, data, DragDropEffects.Copy, RawInputModifiers.None);
        AppHarness.Settle(300);

        Assert.DoesNotContain(app.Window.FindControl<ListBox>("PlaylistList")!.GetVisualDescendants().OfType<ListBoxItem>(),
            i => i.Classes.Contains("drop-target"));
        var playlist = app.Library.Playlists.Single();
        Assert.Equal([songs[1].Path, songs[0].Path, outside], app.Database.GetPlaylistEntries(playlist.Id).Select(e => e.Path));
        Assert.Equal("3 songs added to Saturday", app.ViewModel.Notice);
        Assert.Null(app.Database.FindTrack(outside));
        Assert.Equal(3, app.Library.Playlists.Single().Count);
    }

    [AvaloniaFact]
    public void Files_dropped_into_an_open_playlist_go_where_they_were_dropped()
    {
        using var app = new AppHarness();
        var songs = app.Library.Results.Take(3).ToList();
        var playlist = app.ViewModel.CreatePlaylist("Saturday", [songs[0], songs[1]])!;
        app.Library.OpenPlaylist(playlist.Id);
        AppHarness.Settle();
        var grid = app.Window.FindControl<DataGrid>("TrackGrid")!;
        var second = grid.GetVisualDescendants().OfType<DataGridRow>().First(r => ((Track)r.DataContext!).PlaylistNumber == 2);
        Point between = second.TranslatePoint(new Point(200, 2), app.Window)!.Value;   // the top of the second row

        Drop(app, between, Files(app, songs[2].Path));

        Assert.Equal([songs[0].Path, songs[2].Path, songs[1].Path], app.Database.GetPlaylistEntries(playlist.Id).Select(e => e.Path));
        Assert.Equal([songs[0].DisplayTitle, songs[2].DisplayTitle, songs[1].DisplayTitle], app.Library.Results.Select(t => t.DisplayTitle));
    }

    [AvaloniaFact]
    public void A_file_that_isnt_a_song_is_refused_with_a_word_why()
    {
        using var app = new AppHarness();
        string notes = Path.Combine(app.WorkDirectory, "set notes.txt");
        File.WriteAllText(notes, "Start slow.");

        Drop(app, DeckPoint(app), Files(app, notes));

        Assert.Null(app.ViewModel.Deck1!.Track);
        Assert.Equal("set notes.txt isn't a song GZJam can play.", app.ViewModel.Notice);
    }
}
