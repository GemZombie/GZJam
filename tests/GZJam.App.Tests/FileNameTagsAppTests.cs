using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using GZJam.App.ViewModels;
using GZJam.App.Views;
using GZJam.Library;
using GZJam.Testing;

namespace GZJam.App.Tests;

/// <summary>
/// Artist and title from file names (#273): a song with no such tags shows what its name says, in
/// italics, everywhere; and Fill tags from file name, in song info, fills them for good, each
/// song its own, with a preview first and Undo.
/// </summary>
public sealed class FileNameTagsAppTests
{
    private static AppHarness WithOldTransfers()
    {
        string music = Path.Combine(Path.GetTempPath(), "gzjam-names-" + Guid.NewGuid().ToString("N"));
        TestMp3.Write(Path.Combine(music, "78s", "1924 Ma Rainey - Lucky Rock Blues.mp3"));
        TestMp3.Write(Path.Combine(music, "78s", "1925 Bessie Smith - St. Louis Blues.mp3"));
        TestMp3.Write(Path.Combine(music, "Rips", "Ella Fitzgerald - Air Mail Special.mp3"), new TestMp3.Tags(Title: "Air Mail Special (live)"));
        return new AppHarness(musicFolder: music);
    }

    private static Track Song(AppHarness app, string fileStart) => app.Library.Results.Single(t => t.FileName.StartsWith(fileStart, StringComparison.Ordinal));

    private static SongInfoWindow Open(AppHarness app, params Track[] tracks)
    {
        SongInfoWindow window = app.Window.ShowSongInfo(tracks);
        AppHarness.WaitFor(() => window.Vm.IsRead);
        AppHarness.Settle(100);
        window.Vm.ConfirmAsync = (_, _, _) => Task.FromResult(true);
        return window;
    }

    [AvaloniaFact]
    public void A_song_without_tags_shows_what_its_name_says_in_italics()
    {
        using var app = WithOldTransfers();
        Track rainey = Song(app, "1924");
        Track ella = Song(app, "Ella");

        Assert.Equal(("Lucky Rock Blues", "Ma Rainey"), (rainey.DisplayTitle, rainey.DisplayArtist));
        Assert.True(rainey.IsTitleFromName && rainey.IsArtistFromName);
        Assert.Equal(("Air Mail Special (live)", "Ella Fitzgerald"), (ella.DisplayTitle, ella.DisplayArtist));   // its title tag stays
        Assert.False(ella.IsTitleFromName);
        Assert.True(ella.IsArtistFromName);

        var grid = app.Window.FindControl<DataGrid>("TrackGrid")!;
        app.Window.UpdateLayout();
        var artist = grid.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "Ma Rainey");
        Assert.Equal(FontStyle.Italic, artist.FontStyle);
        var title = grid.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "Air Mail Special (live)");
        Assert.Equal(FontStyle.Normal, title.FontStyle);

        app.ViewModel.AddToQueue([rainey]);
        Assert.Equal("Ma Rainey", Assert.Single(app.ViewModel.Queue.Items).Artist);
    }

    [AvaloniaFact]
    public void Fill_tags_from_file_name_guesses_the_pattern_previews_and_fills_each_song_its_own()
    {
        using var app = WithOldTransfers();
        Track rainey = Song(app, "1924"), smith = Song(app, "1925");
        SongInfoWindow window = Open(app, rainey, smith);

        window.Vm.OpenFill();
        AppHarness.Settle(100);

        Assert.Equal("{year} {artist} - {title}", window.FindControl<TextBox>("FillPatternBox")!.Text);
        Assert.Equal(["Artist: Ma Rainey  ·  Title: Lucky Rock Blues  ·  Year: 1924", "Artist: Bessie Smith  ·  Title: St. Louis Blues  ·  Year: 1925"],
            window.Vm.FillPreview.Select(r => r.Says));
        Assert.StartsWith("2 of 2 songs' names fit it", window.Vm.FillSummary);
        Assert.True(window.FindControl<Button>("FillButton")!.IsEnabled);

        Assert.True(AppHarness.Await(window.Vm.FillAsync()));

        Assert.Equal(("Lucky Rock Blues", "Ma Rainey", 1924), (rainey.Title, rainey.Artist, rainey.Year));
        Assert.Equal(("St. Louis Blues", "Bessie Smith", 1925), (smith.Title, smith.Artist, smith.Year));
        Assert.False(rainey.IsTitleFromName);
        Assert.Equal("Ma Rainey", app.Database.GetTagEdits(rainey.Path)[SongFields.Artist]);   // kept in the library: the file isn't changed
        Assert.False(window.Vm.IsFillOpen);

        AppHarness.Await(window.Vm.UndoAsync());
        Assert.Null(rainey.Artist);
        Assert.Equal("Ma Rainey", rainey.DisplayArtist);   // what the name says, again
    }

    [AvaloniaFact]
    public void Only_empty_tags_are_filled_unless_asked_and_a_name_that_doesnt_fit_is_left()
    {
        using var app = WithOldTransfers();
        Track ella = Song(app, "Ella"), rainey = Song(app, "1924");
        SongInfoWindow window = Open(app, ella, rainey);
        window.Vm.OpenFill();
        window.Vm.FillPattern = "{artist} - {title}";
        Assert.Equal("Artist: 1924 Ma Rainey  ·  Title: Lucky Rock Blues", window.Vm.FillPreview[1].Says);   // a preview shows it before anything changes
        window.Vm.FillPattern = "{artist} - Air Mail Special";

        Assert.False(window.Vm.FillPreview[1].Fits);
        Assert.True(AppHarness.Await(window.Vm.FillAsync()));

        Assert.Equal(("Air Mail Special (live)", "Ella Fitzgerald"), (ella.Title, ella.Artist));   // the title tag stays
        Assert.Null(rainey.Artist);

        window.Vm.OpenFill();
        window.Vm.FillPattern = "{artist} - {title}";
        window.Vm.FillOnlyEmpty = false;
        Assert.True(AppHarness.Await(window.Vm.FillAsync()));
        Assert.Equal("Air Mail Special", ella.Title);
    }

    [AvaloniaFact]
    public void A_pattern_with_nothing_to_fill_says_so()
    {
        using var app = WithOldTransfers();
        SongInfoWindow window = Open(app, Song(app, "1924"));
        window.Vm.OpenFill();

        window.Vm.FillPattern = "artist - title";

        Assert.False(window.Vm.CanFill);
        Assert.StartsWith("The pattern needs a part to fill", window.Vm.FillSummary);
    }
}
