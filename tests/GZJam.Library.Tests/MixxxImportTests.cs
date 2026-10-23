using GZJam.Testing;
using Microsoft.Data.Sqlite;

namespace GZJam.Library.Tests;

/// <summary>Bringing a Mixxx library over: ratings, crates as labels, History, locked BPMs.</summary>
public sealed class MixxxImportTests : IDisposable
{
    private readonly TempLibrary _lib = new();
    private readonly string _mixxxDb;

    public MixxxImportTests() => _mixxxDb = Path.Combine(Directory.CreateTempSubdirectory("gzjam-fake-mixxx-").FullName, "mixxxdb.sqlite");

    public void Dispose()
    {
        _lib.Dispose();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(Path.GetDirectoryName(_mixxxDb)!, recursive: true); }
        catch (IOException) { }
    }

    private Track Find(string path) => _lib.Database.Search(null).Single(t => t.Path == path);

    private MixxxImport Plan()
    {
        using var mixxx = MixxxLibrary.Open(_mixxxDb);
        return MixxxImport.Plan(_lib.Database, mixxx);
    }

    [Fact]
    public void Ratings_crates_history_and_locked_bpms_come_over()
    {
        string woodside = _lib.AddMp3("Swing/woodside.mp3", new GZJam.Testing.TestMp3.Tags(Title: "Jumpin' at the Woodside"));
        string splanky = _lib.AddMp3("Swing/splanky.mp3", new GZJam.Testing.TestMp3.Tags(Title: "Splanky"));
        string polka = _lib.AddMp3("Other/polka.mp3", new GZJam.Testing.TestMp3.Tags(Title: "Beer Barrel Polka"));
        string meh = _lib.AddMp3("Swing/meh.mp3", new GZJam.Testing.TestMp3.Tags(Title: "Meh"));
        _lib.AddRootAndScan();
        string elsewhere = Path.Combine(Path.GetTempPath(), "not-in-gzjam", "Lost Song.mp3");

        var mixxx = new FakeMixxx(_mixxxDb);
        long w = mixxx.Song(woodside, "Jumpin' at the Woodside", stars: 5, bpm: 236, locked: true);
        long s = mixxx.Song(splanky, "Splanky", stars: 4, bpm: 120.5);   // BPM not locked: GZJam's own stands
        long p = mixxx.Song(polka, "Beer Barrel Polka", stars: 1);
        mixxx.Song(meh, "Meh", stars: 3);
        long lost = mixxx.Song(elsewhere, "Lost Song", stars: 5, size: 1234);
        mixxx.Crate("Lindy", w, s, lost);
        mixxx.Crate("Not for dances", p);
        DateTime night = new(2024, 3, 14, 20, 5, 33, DateTimeKind.Utc);
        mixxx.Night("2024-03-14", (w, night), (lost, night.AddMinutes(3)), (s, night.AddMinutes(6)));
        mixxx.Night("Friday Night", (s, night.AddDays(7)));
        mixxx.Playlist("Not history", w);
        mixxx.Close();

        MixxxImport plan = Plan();
        Assert.Equal(5, plan.MixxxSongs);
        Assert.Equal(4, plan.FoundSongs);
        Assert.Equal([Path.GetDirectoryName(elsewhere)!], plan.MissingFolders);
        Assert.Equal(3, plan.RatingCount);       // 5 and 4 stars: ♥; 1 star: broken heart; 3 stars: nothing
        Assert.Equal(2, plan.CrateCount);
        Assert.Equal(2, plan.SessionCount);
        Assert.Equal(4, plan.SessionPlayCount);  // the song GZJam doesn't have is still part of that night
        Assert.Equal(1, plan.BpmCount);

        MixxxImportResult result = plan.Apply(_lib.Database, new MixxxImportOptions());
        Assert.Equal(new MixxxImportResult(3, 2, 3, 2, 4, 1), result);

        Assert.Equal(Rating.Loved, Find(woodside).Rating);
        Assert.Equal(Rating.Loved, Find(splanky).Rating);
        Assert.Equal(Rating.Rejected, Find(polka).Rating);
        Assert.Equal(Rating.Unrated, Find(meh).Rating);
        Assert.Equal(["Lindy"], Find(woodside).Labels);
        Assert.Equal(["Not for dances"], Find(polka).Labels);
        Assert.Equal(236, Find(woodside).Bpm);
        Assert.Null(Find(splanky).ManualBpm);

        var sessions = _lib.Database.GetSessions().OrderBy(x => x.StartedUtc).ToList();
        Assert.Equal(2, sessions.Count);
        Assert.Null(sessions[0].Name);           // Mixxx's date name: GZJam shows the date itself
        Assert.Equal("Friday Night", sessions[1].Name);
        var plays = _lib.Database.GetPlays(sessions[0].Id);
        Assert.Equal(["Jumpin' at the Woodside", "Lost Song", "Splanky"], plays.Select(x => x.DisplayTitle));
        Assert.Equal(night, plays[0].PlayedUtc);
        Assert.Equal(2, Find(splanky).PlayCount);
    }

    [Fact]
    public void Importing_again_adds_nothing_twice_and_gzjams_own_marks_stand()
    {
        string a = _lib.AddMp3("Swing/a.mp3", new GZJam.Testing.TestMp3.Tags(Title: "A"));
        _lib.AddRootAndScan();
        _lib.Database.SetRating([a], Rating.Rejected);   // the DJ's own call in GZJam
        _lib.Database.SetManualBpm([a], 150);

        var mixxx = new FakeMixxx(_mixxxDb);
        long id = mixxx.Song(a, "A", stars: 5, bpm: 300, locked: true);
        mixxx.Crate("Lindy", id);
        mixxx.Night("2024-03-14", (id, new DateTime(2024, 3, 14, 20, 0, 0, DateTimeKind.Utc)));
        mixxx.Close();

        Plan().Apply(_lib.Database, new MixxxImportOptions());
        MixxxImport again = Plan();
        Assert.Equal(0, again.RatingCount);
        Assert.Equal(0, again.BpmCount);
        Assert.Equal(0, again.SessionCount);
        again.Apply(_lib.Database, new MixxxImportOptions());

        Assert.Equal(Rating.Rejected, Find(a).Rating);
        Assert.Equal(150, Find(a).Bpm);
        Assert.Equal(["Lindy"], Find(a).Labels);
        Assert.Single(_lib.Database.GetSessions(), x => x.PlayCount > 0);
    }

    [Fact]
    public void Moved_music_is_matched_by_file_name_and_size()
    {
        string here = _lib.AddMp3("Swing/Moved Song.mp3", new GZJam.Testing.TestMp3.Tags(Title: "Moved"));
        _lib.AddRootAndScan();
        var mixxx = new FakeMixxx(_mixxxDb);
        mixxx.Song(@"D:\Old Music\Swing\Moved Song.mp3", "Moved", stars: 5, size: new FileInfo(here).Length);
        mixxx.Close();

        MixxxImport plan = Plan();
        Assert.Equal(1, plan.FoundSongs);
        plan.Apply(_lib.Database, new MixxxImportOptions());
        Assert.Equal(Rating.Loved, Find(here).Rating);
    }

    [Fact]
    public void Only_what_was_chosen_comes_over_and_mixxx_is_left_as_it_was()
    {
        string a = _lib.AddMp3("Swing/a.mp3", new GZJam.Testing.TestMp3.Tags(Title: "A"));
        _lib.AddRootAndScan();
        var mixxx = new FakeMixxx(_mixxxDb);
        long id = mixxx.Song(a, "A", stars: 5, bpm: 200, locked: true);
        mixxx.Crate("Lindy", id);
        mixxx.Close();
        byte[] before = File.ReadAllBytes(_mixxxDb);

        Plan().Apply(_lib.Database, new MixxxImportOptions(Ratings: false, Crates: true, History: false, LockedBpms: false));

        Assert.Equal(Rating.Unrated, Find(a).Rating);
        Assert.Equal(["Lindy"], Find(a).Labels);
        Assert.Null(Find(a).ManualBpm);
        Assert.Equal(before, File.ReadAllBytes(_mixxxDb));
        Assert.False(File.Exists(_mixxxDb + "-wal"));
    }

    [Fact]
    public void Hot_cues_one_to_four_become_cues_for_songs_without_any()
    {
        string a = _lib.AddMp3("Swing/a.mp3", new GZJam.Testing.TestMp3.Tags(Title: "A"));
        string b = _lib.AddMp3("Swing/b.mp3", new GZJam.Testing.TestMp3.Tags(Title: "B"));
        string c = _lib.AddMp3("Swing/c.mp3", new GZJam.Testing.TestMp3.Tags(Title: "C"));
        _lib.AddRootAndScan();
        _lib.Database.SetCues(b, [new SongCue(1, TimeSpan.FromSeconds(30))]);   // set in GZJam: stays

        var mixxx = new FakeMixxx(_mixxxDb);
        long ida = mixxx.Song(a, "A");
        mixxx.Cue(ida, 0, 12.5);              // Mixxx's first pad: cue 1
        mixxx.Cue(ida, 2, 95.25);             // third pad: cue 3 (numbers are kept)
        mixxx.Cue(ida, 7, 150);               // eighth pad: GZJam has four cue keys
        mixxx.Cue(ida, -1, 3, type: 2);       // the main cue point: not a hot cue
        mixxx.Cue(ida, 1, 40, type: 4);       // a saved loop on the second pad: not a cue
        mixxx.Cue(ida, -1, 170, type: 7);     // the outro marker: GZJam finds the end itself
        long idb = mixxx.Song(b, "B");
        mixxx.Cue(idb, 0, 60);
        long idc = mixxx.Song(c, "C");
        mixxx.Cue(idc, 3, 5);
        mixxx.Close();

        MixxxImport plan = Plan();
        Assert.Equal(2, plan.CuedSongCount);   // A and C; B has cues in GZJam
        Assert.Equal(1, plan.LeftOutHotCues);

        MixxxImportResult result = plan.Apply(_lib.Database, new MixxxImportOptions());
        Assert.Equal(2, result.CuedSongs);
        Assert.Equal([new SongCue(1, TimeSpan.FromSeconds(12.5)), new SongCue(3, TimeSpan.FromSeconds(95.25))], _lib.Database.GetCues(a));
        Assert.Equal([new SongCue(1, TimeSpan.FromSeconds(30))], _lib.Database.GetCues(b));
        Assert.Equal([new SongCue(4, TimeSpan.FromSeconds(5))], _lib.Database.GetCues(c));

        // Again: nothing new (the songs have cues now), and not chosen: nothing comes over.
        Assert.Equal(0, Plan().CuedSongCount);
        _lib.Database.SetCues(c, []);
        Assert.Equal(0, Plan().Apply(_lib.Database, new MixxxImportOptions(HotCues: false)).CuedSongs);
        Assert.Empty(_lib.Database.GetCues(c));
    }
}
