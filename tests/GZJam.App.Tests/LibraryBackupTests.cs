using System.IO.Compression;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using GZJam.App.Views;
using GZJam.Library;
using Microsoft.Data.Sqlite;

namespace GZJam.App.Tests;

/// <summary>Backing up and restoring the library (#84), and the automatic daily copies.</summary>
public sealed class LibraryBackupTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("gzjam-backup-test-").FullName;

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { /* best effort */ }
    }

    /// <summary>A library with a heart, a label and a playlist on its first song.</summary>
    private static string Mark(AppHarness app)
    {
        string song = app.ViewModel.Library.Results[0].Path;
        app.Database.SetRating([song], Rating.Loved);
        app.Database.AddLabel([song], "Lindy");
        app.Database.AddToPlaylist(app.Database.CreatePlaylist("Saturday").Id, [song]);
        return song;
    }

    [AvaloniaFact]
    public void A_backup_restored_elsewhere_brings_back_the_marks_and_the_settings()
    {
        string backup = Path.Combine(_dir, "GZJam backup.gzjam");
        string song;
        int songs;
        using (var app = new AppHarness())
        {
            song = Mark(app);
            songs = app.Database.GetTrackCount();
            app.Settings.FadeOutSeconds = 9;
            BackupInfo made = LibraryBackup.Create(app.Database, app.ViewModel.SettingsJson(), backup);
            Assert.Equal(songs, made.Songs);
        }

        BackupInfo info = LibraryBackup.Read(backup);
        Assert.Equal((songs, 1, 1), (info.Songs, info.Playlists, info.MarkedSongs));
        Assert.Equal(About.Version, info.GZJamVersion);
        Assert.Single(info.MusicFolders);

        // Another computer's data folder, with a library and settings of its own.
        string data = Path.Combine(_dir, "data");
        Directory.CreateDirectory(data);
        string db = Path.Combine(data, "library.db"), settings = Path.Combine(data, "settings.json");
        new LibraryDatabase(db).CreatePlaylist("Theirs");
        File.WriteAllText(settings, "{}");
        SqliteConnection.ClearAllPools();

        LibraryBackup.Stage(backup, data);
        Assert.True(File.Exists(db));   // nothing changes until the next start
        var (restored, kept) = LibraryBackup.ApplyPending(db, settings)!.Value;

        Assert.Equal(info.Created, restored.Created);
        var library = new LibraryDatabase(db);
        Track t = library.Search(null).Single(x => x.Path == song);
        Assert.Equal(Rating.Loved, t.Rating);
        Assert.Equal(["Lindy"], t.Labels);
        Assert.Equal(["Saturday"], library.GetPlaylists().Select(p => p.Name));
        Assert.Equal(9, AppSettings.Load(settings).FadeOutSeconds);
        Assert.Contains("before restoring a backup", kept);
        Assert.True(File.Exists(Path.Combine(kept, "library.db")));   // the replaced library, kept
        Assert.Equal("{}", File.ReadAllText(Path.Combine(kept, "settings.json")));
        Assert.Null(LibraryBackup.ApplyPending(db, settings));   // once only
    }

    [Fact]
    public void A_file_thats_not_a_backup_is_refused_and_nothing_is_readied()
    {
        string notBackup = Path.Combine(_dir, "song.gzjam");
        File.WriteAllText(notBackup, "not a zip");
        string emptyZip = Path.Combine(_dir, "empty.gzjam");
        using (ZipFile.Open(emptyZip, ZipArchiveMode.Create)) { }

        foreach (string path in new[] { notBackup, emptyZip })
        {
            Assert.Throws<InvalidDataException>(() => LibraryBackup.Read(path));
            Assert.Throws<InvalidDataException>(() => LibraryBackup.Stage(path, _dir));
            Assert.Null(LibraryBackup.ApplyPending(Path.Combine(_dir, "library.db"), Path.Combine(_dir, "settings.json")));
        }
    }

    [AvaloniaFact]
    public void A_backup_from_a_newer_gzjam_is_refused()
    {
        string backup = Path.Combine(_dir, "newer.gzjam");
        using (var app = new AppHarness())
            LibraryBackup.Create(app.Database, "{}", backup);

        // As a newer GZJam would leave it: upgraded past this one, and saying this one can't read it.
        using (var zip = ZipFile.Open(backup, ZipArchiveMode.Update))
        {
            string db = Path.Combine(_dir, "inside.db");
            zip.GetEntry("library.db")!.ExtractToFile(db);
            using (var c = new SqliteConnection($"Data Source={db};Pooling=False"))
            {
                c.Open();
                using var cmd = c.CreateCommand();
                cmd.CommandText = "PRAGMA user_version = 999; UPDATE schema_compat SET oldest_reader = 999;";
                cmd.ExecuteNonQuery();
            }
            zip.GetEntry("library.db")!.Delete();
            zip.CreateEntryFromFile(db, "library.db");
        }

        string data = Path.Combine(_dir, "data");
        Directory.CreateDirectory(data);
        Assert.Throws<LibraryTooNewException>(() => LibraryBackup.Stage(backup, data));
        Assert.Null(LibraryBackup.ApplyPending(Path.Combine(data, "library.db"), Path.Combine(data, "settings.json")));
    }

    [AvaloniaFact]
    public void One_automatic_copy_a_day_and_the_last_five_kept()
    {
        using var app = new AppHarness();
        string folder = Path.Combine(app.WorkDirectory, LibraryBackup.AutomaticFolder);
        var day = new DateTime(2026, 10, 1);
        for (int i = 0; i < 7; i++)
            Assert.NotNull(LibraryBackup.KeepAutomatic(app.Database, "{}", day.AddDays(i)));
        Assert.Null(LibraryBackup.KeepAutomatic(app.Database, "{}", day.AddDays(6).AddHours(5)));   // today's is made

        var kept = Directory.GetFiles(folder).Select(Path.GetFileName).Order().ToList();
        Assert.Equal(LibraryBackup.AutomaticKept, kept.Count);
        Assert.Equal("GZJam automatic backup 2026-10-03.gzjam", kept[0]);
        Assert.Equal("GZJam automatic backup 2026-10-07.gzjam", kept[^1]);
        Assert.Equal(app.Database.GetTrackCount(), LibraryBackup.Read(Path.Combine(folder, kept[^1]!)).Songs);
    }

    [AvaloniaFact]
    public async Task Settings_library_backs_up_and_says_when()
    {
        using var app = new AppHarness();
        var panel = app.Window.FindControl<SettingsPanel>("SettingsPanel")!;
        app.Window.OpenSettings(SettingsSection.Library);
        var last = panel.FindControl<TextBlock>("LastBackupText")!;
        Assert.Equal("Not backed up yet.", last.Text);
        Assert.True(panel.FindControl<Button>("BackUpButton")!.IsVisible);
        Assert.True(panel.FindControl<Button>("RestoreButton")!.IsVisible);

        string backup = Path.Combine(_dir, "mine.gzjam");
        await app.ViewModel.Settings.BackUpAsync(backup);
        app.Screenshot("settings-backup");

        Assert.True(File.Exists(backup));
        Assert.StartsWith("Last backed up ", last.Text);
        Assert.Equal("Library backed up: mine.gzjam", app.ViewModel.Notice);
    }

    [AvaloniaFact]
    public async Task Restoring_asks_first_then_opens_again_with_the_backup()
    {
        using var app = new AppHarness();
        string backup = Path.Combine(_dir, "mine.gzjam");
        await app.ViewModel.Settings.BackUpAsync(backup);
        string? asked = null;
        bool restarting = false;
        app.ViewModel.ConfirmAsync = (_, message, _) => { asked = message; return Task.FromResult(true); };
        app.ViewModel.RestoreRequested += () => restarting = true;

        await app.ViewModel.Settings.RestoreAsync(backup);

        Assert.Contains($"{app.Database.GetTrackCount()} songs", asked);
        Assert.Contains("kept in the \"previous libraries\" folder", asked);
        Assert.True(restarting);
        Assert.True(File.Exists(Path.Combine(app.WorkDirectory, "restore", "library.db")));   // readied for the next start
    }

    [AvaloniaFact]
    public async Task Restoring_waits_while_the_room_hears_music()
    {
        using var app = new AppHarness();
        string backup = Path.Combine(_dir, "mine.gzjam");
        await app.ViewModel.Settings.BackUpAsync(backup);
        var deck = app.ViewModel.Deck1!;
        Assert.True(await app.ViewModel.LoadToDeckAsync(deck, app.ToneTrack("Live")));
        deck.TogglePlayCommand.Execute(null);
        app.Play(0.2);
        bool asked = false;
        app.ViewModel.ConfirmAsync = (_, _, _) => { asked = true; return Task.FromResult(true); };

        await app.ViewModel.Settings.RestoreAsync(backup);

        Assert.False(asked);
        Assert.Equal("Pause the music first: restoring a backup closes GZJam.", app.ViewModel.Notice);
    }
}
