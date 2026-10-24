using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.VisualTree;
using GZJam.App.Controls;
using GZJam.Library;
using GZJam.Testing;

namespace GZJam.App.Tests;

/// <summary>Album art on the decks, the queue and the library.</summary>
public sealed class AlbumArtAppTests
{
    /// <summary>A real picture, as a PNG file's bytes.</summary>
    private static byte[] Picture(int width, int height, Color color)
    {
        using var bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using (var buffer = bitmap.Lock())
        {
            var pixels = new int[width * height];
            Array.Fill(pixels, color.A << 24 | color.R << 16 | color.G << 8 | color.B);
            System.Runtime.InteropServices.Marshal.Copy(pixels, 0, buffer.Address, pixels.Length);
        }
        using var stream = new MemoryStream();
        bitmap.Save(stream, new PngBitmapEncoderOptions());
        return stream.ToArray();
    }

    private static string AlbumFolder(AppHarness app, string album) =>
        Path.GetDirectoryName(app.Library.Results.First(t => t.Album == album).Path)!;

    private static AlbumArt DeckArt(AppHarness app, int deck) =>
        app.Window.FindControl<Views.DeckView>($"Deck{deck}View")!.FindControl<AlbumArt>("Art")!;

    [AvaloniaFact]
    public void A_deck_shows_the_songs_picture_and_a_record_without_one()
    {
        using var app = new AppHarness();
        Track withCover = app.Library.Results.First(t => t.Album == "April in Paris");
        Track inFile = app.Library.Results.First(t => t.Album == "Stompin' at the Savoy");
        Track without = app.Library.Results.First(t => t.Album == "Live at Carnegie Hall");
        File.WriteAllBytes(Path.Combine(AlbumFolder(app, "April in Paris"), "cover.jpg"), Picture(600, 600, Colors.SteelBlue));
        TestMp3.EmbedPicture(inFile.Path, Picture(500, 400, Colors.IndianRed));   // not square: cropped
        var deck1 = app.ViewModel.Decks[0];
        var deck2 = app.ViewModel.Decks[1];
        double height = app.Window.FindControl<Views.DeckView>("Deck1View")!.Bounds.Height;

        Assert.False(DeckArt(app, 1).HasPicture);   // no song: the record
        Assert.True(AppHarness.Await(app.ViewModel.LoadToDeckAsync(deck1, withCover)));
        Assert.True(AppHarness.Await(app.ViewModel.LoadToDeckAsync(deck2, without)));
        AppHarness.WaitFor(() => DeckArt(app, 1).HasPicture);
        AppHarness.Settle(300);
        Assert.False(DeckArt(app, 2).HasPicture);
        Assert.Equal(height, app.Window.FindControl<Views.DeckView>("Deck1View")!.Bounds.Height);   // nothing moved
        deck1.TogglePlayCommand.Execute(null);
        app.ViewModel.Mixer!.Crossfader = 0;   // on air
        app.Play(0.1);
        AppHarness.Settle(150);
        app.Screenshot("album-art-decks");
        deck1.TogglePlayCommand.Execute(null);
        app.Play(0.1);

        Assert.True(AppHarness.Await(app.ViewModel.LoadToDeckAsync(deck1, inFile)));
        AppHarness.WaitFor(() => DeckArt(app, 1).HasPicture);

        // One small copy per picture, 128 px across, in the art folder next to the library.
        string[] copies = Directory.GetFiles(Path.Combine(app.WorkDirectory, "art"), "*.png");
        Assert.Equal(2, copies.Length);
        foreach (string copy in copies)
        {
            using var bitmap = new Bitmap(copy);
            Assert.Equal(AlbumArtCache.Size, bitmap.PixelSize.Width);
        }
    }

    [AvaloniaFact]
    public void Four_decks_show_small_pictures()
    {
        using var app = new AppHarness();
        File.WriteAllBytes(Path.Combine(AlbumFolder(app, "April in Paris"), "cover.jpg"), Picture(400, 400, Colors.SteelBlue));
        File.WriteAllBytes(Path.Combine(AlbumFolder(app, "The Complete Decca Recordings"), "cover.jpg"), Picture(400, 400, Colors.Goldenrod));
        var vm = app.ViewModel;
        vm.Settings.FourDecks = true;
        AppHarness.Await(vm.LoadToDeckAsync(vm.Decks[0], app.Library.Results.First(t => t.Album == "April in Paris")));
        AppHarness.Await(vm.LoadToDeckAsync(vm.Decks[1], app.Library.Results.First(t => t.Album == "Live at Carnegie Hall")));
        AppHarness.Await(vm.LoadToDeckAsync(vm.Decks[2], app.Library.Results.First(t => t.Album == "The Complete Decca Recordings")));

        var compact = Enumerable.Range(1, 4)
            .Select(n => app.Window.FindControl<Views.DeckView>($"Deck{n}View")!.FindControl<AlbumArt>("CompactArt")!).ToList();
        AppHarness.WaitFor(() => compact[0].HasPicture && compact[2].HasPicture);
        AppHarness.Settle(300);
        Assert.False(compact[1].HasPicture);
        Assert.False(compact[3].HasPicture);
        vm.Decks[0].TogglePlayCommand.Execute(null);
        vm.Mixer!.Crossfader = 0;   // on air
        app.Play(0.1);
        AppHarness.Settle(150);
        app.Screenshot("album-art-four-decks");
    }

    [AvaloniaFact]
    public void The_songs_of_an_album_share_one_small_copy_and_the_next_session_reads_it()
    {
        using var app = new AppHarness();
        string folder = AlbumFolder(app, "The Complete Decca Recordings");
        File.WriteAllBytes(Path.Combine(folder, "folder.png"), Picture(300, 300, Colors.Goldenrod));
        var songs = app.Library.Results.Where(t => t.Album == "The Complete Decca Recordings").ToList();
        var cache = app.ViewModel.Art;

        foreach (Track song in songs)
            cache.Get(song.Path);
        AppHarness.WaitFor(() => songs.All(s => cache.Get(s.Path) is not null));
        Assert.Single(Directory.GetFiles(Path.Combine(app.WorkDirectory, "art"), "*.png"));
        Assert.All(songs, s => Assert.Same(cache.Get(songs[0].Path), cache.Get(s.Path)));

        // Another session: the database and the small copy are enough.
        using var again = new AlbumArtCache(app.Database, Path.Combine(app.WorkDirectory, "art"));
        string? found = null;
        again.Found += path => found = path;
        Assert.Null(again.Get(songs[0].Path));
        AppHarness.WaitFor(() => found == songs[0].Path);
        Assert.NotNull(again.Get(songs[0].Path));
    }

    [AvaloniaFact]
    public void A_cover_added_to_a_folder_later_is_found()
    {
        using var app = new AppHarness();
        Track song = app.Library.Results.First(t => t.Album == "Confessin' the Blues");
        var cache = app.ViewModel.Art;
        string? found = null;
        cache.Found += path => found = path;
        cache.Get(song.Path);
        AppHarness.WaitFor(() => found == song.Path);
        Assert.Null(cache.Get(song.Path));   // none

        File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(song.Path)!, "cover.png"), Picture(200, 200, Colors.Teal));
        using var later = new AlbumArtCache(app.Database, Path.Combine(app.WorkDirectory, "art"));   // next session
        later.Get(song.Path);
        AppHarness.WaitFor(() => later.Get(song.Path) is not null);
    }

    [AvaloniaFact]
    public void The_queue_shows_pictures_and_the_library_column_starts_hidden()
    {
        using var app = new AppHarness();
        File.WriteAllBytes(Path.Combine(AlbumFolder(app, "April in Paris"), "cover.jpg"), Picture(400, 400, Colors.SteelBlue));
        app.ViewModel.Queue.Add(app.Library.Results.Where(t => t.Album is "April in Paris" or "Live at Carnegie Hall").ToList());
        AppHarness.Settle();

        var queueArt = app.Window.FindControl<ListBox>("QueueList")!.GetVisualDescendants().OfType<AlbumArt>().ToList();
        Assert.Equal(5, queueArt.Count);
        AppHarness.WaitFor(() => queueArt.Count(a => a.HasPicture) == 3);

        var column = app.Window.FindControl<DataGrid>("TrackGrid")!.Columns.Single(c => (string?)c.Header == "Art");
        Assert.False(column.IsVisible);
        app.Window.SetColumnShown("Art", true);
        AppHarness.WaitFor(() => app.Window.FindControl<DataGrid>("TrackGrid")!.GetVisualDescendants().OfType<AlbumArt>().Any(a => a.HasPicture));
        app.Screenshot("album-art-queue-and-library");
    }

    [Fact]
    public void Settings_from_before_album_art_start_with_its_column_hidden_once()
    {
        var old = AppSettings.Migrate(new AppSettings { HiddenColumns = ["Year"], SeenColumns = null });
        Assert.Equal(["Year", "Art"], old.HiddenColumns);

        old.HiddenColumns.Remove("Art");   // the DJ shows it
        Assert.Equal(["Year"], AppSettings.Migrate(old).HiddenColumns);   // and it stays shown

        string file = Path.Combine(Directory.CreateTempSubdirectory("gzjam-settings-").FullName, "settings.json");
        var fresh = new AppSettings();
        fresh.HiddenColumns.Remove("Art");
        fresh.Save(file);
        Assert.DoesNotContain("Art", AppSettings.Load(file).HiddenColumns);
    }
}
