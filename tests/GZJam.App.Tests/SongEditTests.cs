using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using GZJam.App.ViewModels;
using GZJam.App.Views;
using GZJam.Library;

namespace GZJam.App.Tests;

/// <summary>
/// Editing songs' tags in their info (#129): one song or many, kept in GZJam's library by
/// default, written into the files when the DJ chooses; with Undo.
/// </summary>
public sealed class SongEditTests
{
    private static Track Song(AppHarness app, string title) => app.Library.Results.Single(t => t.Title == title);

    private static TagField Field(SongInfoWindow window, string field) => window.Vm.Tags.Single(t => t.Field == field);

    private static string? TitleInFile(string path)
    {
        using var file = TagLib.File.Create(path);
        return file.Tag.Title;
    }

    private static void Capture(Window window, string name)
    {
        AppHarness.Settle(200);
        window.CaptureRenderedFrame()?.Save(Path.Combine(AppHarness.ScreenshotDirectory, name + ".png"), new PngBitmapEncoderOptions());
    }

    private static SongInfoWindow Open(AppHarness app, params Track[] tracks)
    {
        SongInfoWindow window = app.Window.ShowSongInfo(tracks);
        AppHarness.WaitFor(() => window.Vm.IsRead);
        AppHarness.Settle(100);
        return window;
    }

    [AvaloniaFact]
    public void An_edit_typed_in_a_songs_info_is_kept_in_the_library_shown_everywhere_and_undone()
    {
        using var app = new AppHarness();
        Track song = Song(app, "Corner Pocket");
        app.ViewModel.AddToQueue([song]);
        SongInfoWindow window = Open(app, song);
        var titleBox = window.FindControl<ItemsControl>("TagsList")!.GetVisualDescendants().OfType<TextBox>().First();

        titleBox.Text = "Corner Pocket (Until I Met You)";
        window.Vm.NewTagName = "dance";
        window.Vm.NewTagValue = "Lindy";
        window.Vm.AddTag();
        Assert.True(window.Vm.HasChanges);
        Assert.True(window.FindControl<Button>("SaveButton")!.IsEnabled);
        AppHarness.Await(window.Vm.SaveAsync());

        Assert.Equal("Corner Pocket (Until I Met You)", song.Title);   // the song on screen changes in place: still selected, where it was
        Assert.Equal("Lindy", song.Tag("dance"));
        Assert.Equal("Corner Pocket (Until I Met You)", app.ViewModel.Queue.Items[0].Track.Title);
        Assert.Equal("Corner Pocket", TitleInFile(song.Path));   // the file isn't touched
        Assert.Equal(song.Path, Assert.Single(app.Database.Search("dance:lindy")).Path);
        Assert.Equal("Saved 1 song, in GZJam's library.", window.Vm.SavedText);
        Assert.False(window.Vm.HasChanges);
        Assert.True(Field(window, SongFields.Title).IsLibraryEdit);   // marked: the library's own, over the file
        Assert.True(window.Vm.HasLibraryEdits);
        Assert.NotNull(app.ViewModel.NoticeUndo);
        Capture(window, "song-info-edited");

        AppHarness.Await(window.Vm.UndoAsync());

        Assert.Equal("Corner Pocket", song.Title);
        Assert.Null(song.Tag("dance"));
        Assert.Empty(app.Database.GetTagEdits(song.Path));
        Assert.False(window.Vm.CanUndo);
        window.Close();
    }

    [AvaloniaFact]
    public void Several_songs_show_what_they_share_change_only_whats_typed_and_ask_first_saying_how_many()
    {
        using var app = new AppHarness();
        Track[] songs = [Song(app, "Corner Pocket"), Song(app, "Liza"), Song(app, "Topsy")];
        app.ViewModel.SetRating([songs[0]], Rating.Loved);
        app.ViewModel.SetLabel([songs[0], songs[1]], "Lindy", true);
        SongInfoWindow window = Open(app, songs);
        string? asked = null;
        window.Vm.ConfirmAsync = (heading, message, _) =>
        {
            asked = heading + " " + message;
            return Task.FromResult(true);
        };

        Assert.Equal("3 songs", window.Vm.Heading);
        TagField artist = Field(window, SongFields.Artist), genre = Field(window, SongFields.Genre);
        Assert.True(artist.IsSeveral);
        Assert.Equal("(several)", artist.Watermark);
        Assert.Equal("Swing", genre.Text);   // shared
        Assert.Equal("(several)", window.Vm.RatingChoices[window.Vm.RatingIndex]);
        LabelChoice lindy = Assert.Single(window.Vm.Labels);
        Assert.Null(lindy.IsChecked);   // on some of them
        Capture(window, "song-info-several");

        genre.Text = "Lindy Hop";
        lindy.IsChecked = true;
        window.Vm.EnergyIndex = 3;
        AppHarness.Await(window.Vm.SaveAsync());

        Assert.Equal("Change 3 songs? Genre, Energy and Labels will change on all 3 songs. Undo puts them back.", asked);
        Assert.All(songs, s => Assert.Equal("Lindy Hop", s.Genre));
        Assert.Equal(["Count Basie Orchestra", "Chick Webb", "Count Basie"], songs.Select(s => s.Artist));   // left as each had it
        Assert.All(songs, s => Assert.Equal(Energy.High, s.Energy));
        Assert.All(songs, s => Assert.Equal(["Lindy"], s.Labels));
        Assert.Equal([Rating.Loved, Rating.Unrated, Rating.Unrated], songs.Select(s => s.Rating));

        app.ViewModel.UndoNotice();   // from the main window's notice, as well as the window's own Undo
        AppHarness.WaitFor(() => songs[0].Genre == "Swing");

        Assert.All(songs, s => Assert.Equal("Swing", s.Genre));
        Assert.All(songs, s => Assert.Equal(Energy.Unset, s.Energy));
        Assert.Equal([["Lindy"], ["Lindy"], []], songs.Select(s => s.Labels.ToArray()));
        window.Close();
    }

    [AvaloniaFact]
    public void Clearing_a_field_the_songs_differ_in_takes_it_off_all_of_them()
    {
        using var app = new AppHarness();
        Track[] songs = [Song(app, "Corner Pocket"), Song(app, "Liza")];
        SongInfoWindow window = Open(app, songs);
        window.Vm.ConfirmAsync = (_, _, _) => Task.FromResult(true);
        TagField year = Field(window, SongFields.Year);
        Assert.True(year.IsSeveral);
        Assert.True(year.CanClearAll);

        year.ClearAllCommand.Execute(null);
        Assert.Equal("(none, on every song)", year.Watermark);
        AppHarness.Await(window.Vm.SaveAsync());

        Assert.All(songs, s => Assert.Null(s.Year));
        window.Close();
    }

    [AvaloniaFact]
    public void With_writing_on_edits_go_into_the_files_but_a_song_on_a_deck_is_kept_in_the_library()
    {
        using var app = new AppHarness();
        app.ViewModel.Settings.WriteTagsToFiles = true;
        Track onDeck = Song(app, "Liza"), song = Song(app, "Topsy");
        Assert.True(AppHarness.Await(app.ViewModel.LoadToDeckAsync(app.ViewModel.Deck1!, onDeck)));
        SongInfoWindow window = Open(app, song, onDeck);
        window.Vm.ConfirmAsync = (_, _, _) => Task.FromResult(true);
        Assert.True(window.FindControl<CheckBox>("WriteTagsBox")!.IsChecked);

        Field(window, SongFields.Comment).Text = "Great for a jam";
        Field(window, SongFields.Bpm).Text = "156.5";
        AppHarness.Await(window.Vm.SaveAsync());

        using (var file = TagLib.File.Create(song.Path))
            Assert.Equal("Great for a jam", file.Tag.Comment);
        Assert.Equal(156.5, song.TagBpm);
        Assert.Empty(app.Database.GetTagEdits(song.Path));   // the file says it now
        Assert.Equal("Great for a jam", onDeck.Comment);
        using (var file = TagLib.File.Create(onDeck.Path))
            Assert.Null(file.Tag.Comment);
        Assert.Equal("Saved into 1 file; kept in GZJam's library only (\"Liza\": it's on a deck).", window.Vm.SavedText);
        Assert.Equal(onDeck.Path, app.ViewModel.Deck1!.Track!.Path);

        AppHarness.Await(window.Vm.UndoAsync());

        using (var file = TagLib.File.Create(song.Path))
            Assert.Null(file.Tag.Comment);
        Assert.Null(song.Comment);
        Assert.Null(onDeck.Comment);
        window.Close();
    }

    [AvaloniaFact]
    public void A_year_that_isnt_one_cant_be_saved()
    {
        using var app = new AppHarness();
        SongInfoWindow window = Open(app, Song(app, "Liza"));

        Field(window, SongFields.Year).Text = "the forties";

        Assert.Equal("A year, like 1957", Field(window, SongFields.Year).Error);
        Assert.False(window.Vm.CanSave);
        Field(window, SongFields.Year).Text = "1938";
        Assert.True(window.Vm.CanSave);
        window.Vm.Cancel();
        Assert.False(window.Vm.HasChanges);
        window.Close();
    }

    [AvaloniaFact]
    public void Another_songs_info_asks_before_throwing_away_changes_not_saved()
    {
        using var app = new AppHarness();
        Track liza = Song(app, "Liza"), topsy = Song(app, "Topsy");
        SongInfoWindow window = Open(app, liza);
        int asked = 0;
        bool discard = false;
        window.ConfirmDiscardAsync = () =>
        {
            asked++;
            return Task.FromResult(discard);
        };
        Field(window, SongFields.Title).Text = "Liza (All the Clouds'll Roll Away)";

        Assert.Null(AppHarness.Await(app.Window.OpenSongInfoAsync([topsy])));
        Assert.Same(window, app.Window.SongInfo);
        Assert.True(window.Vm.HasChanges);

        window.Close();   // Escape, or the window's close button: asks too
        AppHarness.Settle(100);
        Assert.Equal(2, asked);
        Assert.True(window.IsVisible);

        discard = true;
        SongInfoWindow next = AppHarness.Await(app.Window.OpenSongInfoAsync([topsy]))!;
        Assert.Same(topsy, next.Vm.Track);
        Assert.False(window.IsVisible);
        Assert.Equal("Liza", liza.Title);
        next.Close();
    }

    [AvaloniaFact]
    public void Use_the_files_tags_forgets_the_librarys_edits()
    {
        using var app = new AppHarness();
        Track song = Song(app, "Liza");
        SongInfoWindow window = Open(app, song);
        Field(window, SongFields.Artist).Text = "Chick Webb and His Orchestra";
        AppHarness.Await(window.Vm.SaveAsync());
        Assert.True(window.Vm.HasLibraryEdits);

        AppHarness.Await(window.Vm.UseFileTagsAsync());

        Assert.Equal("Chick Webb", song.Artist);
        Assert.False(window.Vm.HasLibraryEdits);
        Assert.Equal("Chick Webb", Field(window, SongFields.Artist).Text);
        window.Close();
    }
}
