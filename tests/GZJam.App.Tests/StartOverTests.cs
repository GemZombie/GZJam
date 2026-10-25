using Avalonia.Headless.XUnit;

namespace GZJam.App.Tests;

/// <summary>Starting over with an empty library (Settings › Library): asked for, then done at the next start.</summary>
public sealed class StartOverTests
{
    [Fact]
    public void The_old_library_and_its_art_are_kept_aside_and_the_next_start_is_empty()
    {
        string data = Directory.CreateTempSubdirectory("gzjam-startover-").FullName;
        try
        {
            string db = Path.Combine(data, "library.db");
            File.WriteAllText(db, "library");
            File.WriteAllText(db + "-wal", "wal");
            Directory.CreateDirectory(Path.Combine(data, "art"));
            File.WriteAllText(Path.Combine(data, "art", "a.jpg"), "art");
            File.WriteAllText(Path.Combine(data, "settings.json"), "{}");

            Assert.Null(LibraryReset.ApplyPending(db));   // not asked for: nothing moves
            Assert.True(File.Exists(db));

            LibraryReset.Request(data);
            string kept = LibraryReset.ApplyPending(db)!;

            Assert.False(File.Exists(db));
            Assert.False(Directory.Exists(Path.Combine(data, "art")));
            Assert.Equal("library", File.ReadAllText(Path.Combine(kept, "library.db")));
            Assert.Equal("wal", File.ReadAllText(Path.Combine(kept, "library.db-wal")));
            Assert.True(File.Exists(Path.Combine(kept, "art", "a.jpg")));
            Assert.True(File.Exists(Path.Combine(data, "settings.json")));   // Settings stay
            Assert.StartsWith(Path.Combine(data, LibraryReset.PreviousLibraries), kept);
            Assert.Null(LibraryReset.ApplyPending(db));   // once only
        }
        finally
        {
            Directory.Delete(data, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task Settings_asks_first_and_never_while_the_room_hears_music()
    {
        using var app = new AppHarness();
        var vm = app.ViewModel;
        bool requested = false;
        vm.StartOverRequested += () => requested = true;

        string? asked = null;
        vm.ConfirmAsync = (heading, _, _) => { asked = heading; return Task.FromResult(false); };
        await vm.Settings.StartOverCommand.ExecuteAsync(null);
        Assert.Equal("Start over with an empty library?", asked);
        Assert.False(requested);   // said no

        vm.ConfirmAsync = (_, _, _) => Task.FromResult(true);
        await vm.LoadToDeckAsync(vm.Deck1!, app.ToneTrack("On air"));
        vm.Deck1!.TogglePlayCommand.Execute(null);
        app.Play(0.2);
        await vm.Settings.StartOverCommand.ExecuteAsync(null);
        Assert.False(requested);
        Assert.Equal("Pause the music first: starting over closes GZJam.", vm.Notice);

        vm.Deck1.TogglePlayCommand.Execute(null);
        await vm.Settings.StartOverCommand.ExecuteAsync(null);
        Assert.True(requested);
    }
}
