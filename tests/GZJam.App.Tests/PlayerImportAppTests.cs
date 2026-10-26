using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using GZJam.App.ViewModels;
using GZJam.App.Views;
using GZJam.Library;

namespace GZJam.App.Tests;

/// <summary>Settings › Library: importing M3U playlists, and the ratings other players keep in the song files.</summary>
public sealed class PlayerImportAppTests
{
    [AvaloniaFact]
    public void Importing_m3u_files_says_what_comes_over_and_what_doesnt_then_makes_the_playlists()
    {
        using var app = new AppHarness();
        Track first = app.Library.Results[0], second = app.Library.Results[1];
        string folder = Path.Combine(app.WorkDirectory, "Playlists");
        Directory.CreateDirectory(folder);
        string friday = Path.Combine(folder, "Friday Lindy.m3u8");
        string lost = Path.Combine(app.WorkDirectory, "Elsewhere", "lost.mp3");
        File.WriteAllLines(friday, ["#EXTM3U", second.Path, lost, first.Path]);

        string? asked = null;
        app.ViewModel.ConfirmImportAsync = (heading, message, _) =>
        {
            asked = heading + "\n" + message;
            return Task.FromResult(true);
        };
        AppHarness.Await(app.ViewModel.ImportPlaylistsAsync([friday]));
        AppHarness.Settle();

        Assert.Equal("Import the playlist \"Friday Lindy\"?\nFriday Lindy (2 songs), in their order.\n\n" +
                     $"1 song isn't in GZJam's music folders and is left out, e.g. from {Path.GetDirectoryName(lost)}. Add those folders with + Folder, then import again.\n\n" +
                     "The playlist files aren't changed.", asked);
        PlaylistInfo made = Assert.Single(app.Library.Playlists, p => p.Name == "Friday Lindy");
        Assert.Equal(2, made.Count);
        Assert.Equal("Imported the playlist \"Friday Lindy\".", app.ViewModel.Notice);

        // Again: nothing twice, and no question.
        asked = null;
        AppHarness.Await(app.ViewModel.ImportPlaylistsAsync([friday]));
        Assert.Null(asked);
        Assert.Equal("GZJam has that playlist already: nothing imported.", app.ViewModel.Notice);
    }

    [AvaloniaFact]
    public void Ratings_in_the_song_files_are_read_shown_and_given_as_hearts()
    {
        using var app = new AppHarness();
        Track loved = app.Library.Results[0], rejected = app.Library.Results[1];
        foreach (var (track, popm) in new[] { (loved, (byte)255), (rejected, (byte)64) })
        {
            using var file = TagLib.File.Create(track.Path);
            var id3 = (TagLib.Id3v2.Tag)file.GetTag(TagLib.TagTypes.Id3v2, create: true);
            TagLib.Id3v2.PopularimeterFrame.Get(id3, "MediaMonkey", true)!.Rating = popm;
            file.Save();
        }

        FileRatingsImportViewModel import = app.ViewModel.CreateFileRatingsImport();
        var dialog = new FileRatingsDialog(import);
        dialog.Show();   // starts reading
        AppHarness.WaitFor(() => import.CanImport);
        Assert.Matches(@"^\d+ unrated songs read\. 1 song with 4 or 5 stars gets ♥; 1 song with 1 or 2 stars gets a broken heart\.$", import.Status);
        Assert.Equal("Songs you've rated in GZJam keep their rating.", import.Mapping);
        Assert.True(dialog.FindControl<Button>("ImportButton")!.IsEnabled);
        AppHarness.Settle(100);
        dialog.CaptureRenderedFrame()?.Save(Path.Combine(AppHarness.ScreenshotDirectory, "file-ratings-dialog.png"), new PngBitmapEncoderOptions());

        int? rated = AppHarness.Await(import.ImportAsync());
        dialog.Close();
        AppHarness.Settle();

        Assert.Equal(2, rated);
        Assert.Equal("Ratings imported from the song files for 2 songs.", FileRatingsImportViewModel.Summary(rated!.Value));
        Assert.Equal(Rating.Loved, app.Library.Results.Single(t => t.Path == loved.Path).Rating);
        Assert.Equal(Rating.Rejected, app.Library.Results.Single(t => t.Path == rejected.Path).Rating);
    }
}
