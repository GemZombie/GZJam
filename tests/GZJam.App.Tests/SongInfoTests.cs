using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using GZJam.App.ViewModels;
using GZJam.App.Views;
using GZJam.Library;

namespace GZJam.App.Tests;

/// <summary>A song's info (#129): every tag in the file, the file itself, and what GZJam knows about the song.</summary>
public sealed class SongInfoTests
{
    private static readonly RawInputModifiers Command = OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control;

    /// <summary>A harness whose first song (alphabetically by path) has a Dance tag, a Composer and an encoder.</summary>
    private static AppHarness Tagged()
    {
        var app = new AppHarness(start: false);
        string first = Directory.GetFiles(app.MusicPath, "*.mp3", SearchOption.AllDirectories).Order().First();
        using (var file = TagLib.File.Create(first))
        {
            var id3 = (TagLib.Id3v2.Tag)file.GetTag(TagLib.TagTypes.Id3v2, create: true);
            TagLib.Id3v2.UserTextInformationFrame.Get(id3, "DANCE", create: true).Text = ["Lindy"];
            id3.SetTextFrame("TCOM", "Duke Ellington");
            id3.SetTextFrame("TSSE", "LAME 3.100");
            file.Save();
        }
        app.Start();
        return app;
    }

    private static Track Song(AppHarness app) => app.Library.Results.Single(t => t.Tag("dance") == "Lindy");

    private static List<string?> Texts(Window window, string list) =>
        window.FindControl<ItemsControl>(list)!.GetVisualDescendants().OfType<SelectableTextBlock>().Select(t => t.Text).ToList();

    /// <summary>The values in a list of tags, each in its box to edit (#129).</summary>
    private static List<string?> Fields(Window window, string list) =>
        window.FindControl<ItemsControl>(list)!.GetVisualDescendants().OfType<TextBox>().Select(t => t.Text).ToList();

    private static void Click(MenuItem item) => item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

    [AvaloniaFact]
    public void A_songs_info_shows_its_tags_its_file_and_what_GZJam_knows()
    {
        using var app = Tagged();
        Track song = Song(app);
        app.ViewModel.SetRating([song], Rating.Loved);
        app.ViewModel.SetEnergy([song], Energy.High);
        app.ViewModel.CreatePlaylist("Saturday", [song]);
        var grid = app.Window.FindControl<DataGrid>("TrackGrid")!;
        grid.SelectedItem = song;

        Click(app.Window.FindControl<MenuItem>("TrackInfoItem")!);
        SongInfoWindow window = app.Window.SongInfo!;
        AppHarness.WaitFor(() => window.Vm.IsRead);
        AppHarness.Settle(200);

        Assert.Equal(song.DisplayTitle, window.FindControl<SelectableTextBlock>("HeadingText")!.Text);
        var tags = Fields(window, "TagsList");
        Assert.Contains(song.Artist, tags);
        Assert.Contains(song.Album, tags);
        Assert.Equal(["Duke Ellington", "Lindy"], Fields(window, "OtherTagsList"));
        Assert.Equal(song.Path, window.FindControl<SelectableTextBlock>("LocationText")!.Text);
        Assert.Matches(@"^MP3 · \d+ kbps.* · [\d.,]+ kHz · (mono|stereo)$", Texts(window, "FileList")[0]);
        var gzjam = window.Vm.InGZJam.ToDictionary(r => r.Name, r => r.Value);
        Assert.Equal("I'd play it", window.Vm.RatingChoices[window.Vm.RatingIndex]);
        Assert.Equal("High", window.Vm.EnergyChoices[window.Vm.EnergyIndex]);
        Assert.Equal("Saturday", gzjam["Playlists"]);
        Assert.Equal("Never", gzjam["Played"]);
        Assert.EndsWith("(the tag's)", gzjam["BPM"]);
        Assert.Null(window.Vm.Problem);
        Assert.Matches(@"^\d+ BPM · \d+:\d\d · MP3 \d+ kbps · ♥ · High energy$", window.Vm.Summary);   // the header: what a DJ looks for first
        window.CaptureRenderedFrame()?.Save(Path.Combine(AppHarness.ScreenshotDirectory, "song-info.png"), new PngBitmapEncoderOptions());
        window.Close();
    }

    [AvaloniaFact]
    public void Show_all_tags_adds_the_ones_for_other_programs_and_is_remembered()
    {
        using var app = Tagged();
        SongInfoWindow window = app.Window.ShowSongInfo(Song(app));
        AppHarness.WaitFor(() => window.Vm.IsRead);
        Assert.False(window.FindControl<ItemsControl>("LeftOutList")!.IsEffectivelyVisible);

        window.FindControl<CheckBox>("ShowAllTagsBox")!.IsChecked = true;
        AppHarness.Settle(200);

        Assert.True(window.FindControl<ItemsControl>("LeftOutList")!.IsEffectivelyVisible);
        Assert.Equal(["Encoder settings", "LAME 3.100"], window.Vm.LeftOutTags.SelectMany(r => new[] { r.Name, r.Value }));
        Assert.True(app.Settings.ShowAllTags);
        window.Close();

        var next = app.Window.ShowSongInfo(app.Library.Results.First(t => t.Tag("dance") is null));
        AppHarness.WaitFor(() => next.Vm.IsRead);
        Assert.True(next.Vm.ShowAllTags);
        Assert.Equal("None: every tag in the file is shown above.", next.Vm.LeftOutNote);
        next.Close();
    }

    [AvaloniaFact]
    public void Ctrl_I_shows_the_selected_songs_info_from_the_song_list_or_the_queue_one_window_at_a_time()
    {
        using var app = Tagged();
        var grid = app.Window.FindControl<DataGrid>("TrackGrid")!;
        Track song = Song(app);
        grid.SelectedItem = song;
        grid.Focus();

        app.Window.KeyPress(Key.I, Command, PhysicalKey.I, null);
        SongInfoWindow first = app.Window.SongInfo!;
        Assert.Same(song, first.Vm.Track);

        Track queued = app.Library.Results.First(t => t != song);
        app.ViewModel.AddToQueue([queued]);
        var queue = app.Window.FindControl<ListBox>("QueueList")!;
        AppHarness.Settle();
        queue.SelectedIndex = 0;
        queue.ContainerFromIndex(0)!.Focus();
        app.Window.KeyPress(Key.I, Command, PhysicalKey.I, null);
        AppHarness.Settle();

        Assert.Equal(queued.Path, app.Window.SongInfo!.Vm.Track.Path);
        Assert.False(first.IsVisible);   // closed: one at a time
        app.Window.SongInfo!.Close();
        AppHarness.Settle();
        Assert.Null(app.Window.SongInfo);
    }

    [AvaloniaFact]
    public void A_songs_info_says_when_its_file_is_gone()
    {
        using var app = Tagged();
        Track song = Song(app);
        File.Delete(song.Path);

        SongInfoWindow window = app.Window.ShowSongInfo(song);
        AppHarness.WaitFor(() => window.Vm.IsRead);

        Assert.Equal("The file isn't there any more.", window.Vm.Problem);
        Assert.False(window.FindControl<Button>("RevealButton")!.IsEnabled);
        Assert.Equal(["Duke Ellington", "Lindy"], window.Vm.OtherTags.Select(r => r.Text));   // what the library knows, still
        Assert.False(window.Vm.IsEditable);
        Assert.False(window.FindControl<Button>("SaveButton")!.IsVisible);
        window.Close();
    }

    [AvaloniaFact]
    public void Song_info_is_offered_for_several_songs_too_to_edit_together()
    {
        using var app = new AppHarness();
        var grid = app.Window.FindControl<DataGrid>("TrackGrid")!;
        grid.SelectedItems.Add(app.Library.Results[0]);
        grid.SelectedItems.Add(app.Library.Results[1]);
        var menu = app.Window.FindControl<ContextMenu>("TrackMenu")!;
        menu.Open(grid);
        AppHarness.Settle(200);

        Assert.True(app.Window.FindControl<MenuItem>("TrackInfoItem")!.IsEnabled);
        menu.Close();
        Click(app.Window.FindControl<MenuItem>("TrackInfoItem")!);
        AppHarness.WaitFor(() => app.Window.SongInfo is not null);
        Assert.Equal(2, app.Window.SongInfo!.Vm.Tracks.Count);
        app.Window.SongInfo.Close();
    }
}
