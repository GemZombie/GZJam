using Avalonia.Headless.XUnit;
using GZJam.App.ViewModels;
using GZJam.Library;

namespace GZJam.App.Tests;

/// <summary>
/// GZJam as the player for song files (#336): a song double-clicked in the file manager plays on a
/// deck when the room hears nothing, and in the headphones only when it does; a second GZJam hands
/// it to the one open.
/// </summary>
public sealed class OpenWithAppTests
{
    /// <summary>A song file outside the music folders (as one in Downloads would be), with real sound.</summary>
    private static string SongFile(AppHarness app, string title) => app.ToneTrack(title, seconds: 30).Path;

    [AvaloniaFact]
    public void With_nothing_playing_to_the_room_it_plays_on_a_deck_without_joining_the_library()
    {
        using var app = new AppHarness();
        string song = SongFile(app, "From Downloads");
        int songs = app.Database.Search(null).Count;

        Assert.Equal(OpenedTo.Deck, AppHarness.Await(app.ViewModel.OpenFromFileManagerAsync([song])));
        app.Play(0.2);

        DeckViewModel deck = Assert.Single(app.ViewModel.Decks, d => d.Track?.Path == song);
        Assert.True(deck.IsPlaying);
        Assert.True(deck.IsOnAir);   // the room hears it: nothing else was playing
        Assert.Equal(songs, app.Database.Search(null).Count);   // played as it is, not added
    }

    [AvaloniaFact]
    public void With_a_song_on_air_it_plays_in_the_headphones_and_the_room_hears_no_change()
    {
        using var app = new AppHarness();
        var vm = app.ViewModel;
        Assert.Equal(OpenedTo.Deck, AppHarness.Await(vm.OpenFromFileManagerAsync([SongFile(app, "On air")])));
        app.Play(0.2);
        DeckViewModel onAir = Assert.Single(vm.Decks, d => d.IsOnAir);
        var decksBefore = vm.Decks.Select(d => d.Track?.Path).ToList();
        string second = SongFile(app, "Next maybe");

        Assert.Equal(OpenedTo.Headphones, AppHarness.Await(vm.OpenFromFileManagerAsync([second])));

        Assert.Equal(second, vm.Preview!.Track?.Path);
        Assert.Equal(decksBefore, vm.Decks.Select(d => d.Track?.Path));   // no deck touched
        Assert.True(onAir.IsOnAir);
        Assert.Contains("In the headphones", vm.Notice);

        // Opened again: it starts again, rather than stopping as a second tap of 🎧 would.
        Assert.Equal(OpenedTo.Headphones, AppHarness.Await(vm.OpenFromFileManagerAsync([second])));
        Assert.Equal(second, vm.Preview.Track?.Path);
    }

    [AvaloniaFact]
    public void A_file_it_cant_play_or_one_gone_says_so_and_several_play_the_first()
    {
        using var app = new AppHarness();
        var vm = app.ViewModel;
        string text = Path.Combine(app.WorkDirectory, "notes.txt");
        File.WriteAllText(text, "not a song");

        Assert.Equal(OpenedTo.Nothing, AppHarness.Await(vm.OpenFromFileManagerAsync([text])));
        Assert.Contains("can't play notes.txt", vm.Notice);
        Assert.Equal(OpenedTo.Nothing, AppHarness.Await(vm.OpenFromFileManagerAsync([Path.Combine(app.WorkDirectory, "gone.mp3")])));
        Assert.Contains("isn't there any more", vm.Notice);
        Assert.All(vm.Decks, d => Assert.Null(d.Track));

        string first = SongFile(app, "First"), second = SongFile(app, "Second");
        Assert.Equal(OpenedTo.Deck, AppHarness.Await(vm.OpenFromFileManagerAsync([first, second])));
        Assert.Contains(vm.Decks, d => d.Track?.Path == first);
        Assert.DoesNotContain(vm.Decks, d => d.Track?.Path == second);
        Assert.Contains("one at a time", vm.Notice);
    }

    /// <summary>A second GZJam on the same library hands what it was given to the first, over its pipe.</summary>
    [AvaloniaFact]
    public void A_second_GZJam_hands_its_songs_to_the_one_open()
    {
        string folder = Directory.CreateTempSubdirectory("gzjam-one-at-a-time-").FullName;
        try
        {
            Assert.True(SingleInstance.Claim(folder, []));
            var heard = new List<IReadOnlyList<string>>();
            SingleInstance.Listen(args =>
            {
                lock (heard)
                    heard.Add(args);
            });

            Assert.True(SingleInstance.HandOver(SingleInstance.NameFor(folder), [@"C:\Music\One.mp3", "--after-close"]));
            AppHarness.WaitFor(() => { lock (heard) return heard.Count == 1; }, TimeSpan.FromSeconds(10));

            Assert.Equal([@"C:\Music\One.mp3", "--after-close"], heard[0]);
            Assert.Equal([@"C:\Music\One.mp3"], SingleInstance.Files(heard[0]));   // GZJam's own switches aren't songs
            // Another data folder is another GZJam's (GZJAM_DATA_DIR): nobody listens there.
            Assert.False(SingleInstance.HandOver(SingleInstance.NameFor(folder + "-other"), ["x.mp3"]));
        }
        finally
        {
            SingleInstance.Release();
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public void The_same_data_folder_is_the_same_GZJam_whatever_its_spelling()
    {
        string folder = Path.Combine(Path.GetTempPath(), "GZJam Data");
        Assert.Equal(SingleInstance.NameFor(folder), SingleInstance.NameFor(folder + Path.DirectorySeparatorChar));
        Assert.NotEqual(SingleInstance.NameFor(folder), SingleInstance.NameFor(folder + " 2"));
        if (OperatingSystem.IsWindows())
            Assert.Equal(SingleInstance.NameFor(folder), SingleInstance.NameFor(folder.ToUpperInvariant()));
    }
}
