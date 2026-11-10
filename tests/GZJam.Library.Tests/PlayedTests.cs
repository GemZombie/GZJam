namespace GZJam.Library.Tests;

/// <summary>What the library knows from History: when a song was last played, and played: filters.</summary>
public sealed class PlayedTests : IDisposable
{
    private readonly TempLibrary _lib = new();

    public void Dispose() => _lib.Dispose();

    private Track Find(string path) => _lib.Database.Search(null).Single(t => t.Path == path);

    private void Play(string path, DateTime whenUtc)
    {
        var db = _lib.Database;
        PlaySession session = db.StartOrContinueSession(whenUtc, TimeSpan.FromHours(3));
        db.AddPlay(session.Id, Find(path), whenUtc, "1", 0);
    }

    [Fact]
    public void Tracks_know_when_they_were_last_played_and_how_often()
    {
        string a = _lib.AddMp3("Swing/a.mp3", new GZJam.Testing.TestMp3.Tags(Title: "Jumpin' at the Woodside"));
        string b = _lib.AddMp3("Swing/b.mp3", new GZJam.Testing.TestMp3.Tags(Title: "Splanky"));
        DateTime last = DateTime.UtcNow.AddDays(-3);
        _lib.AddRootAndScan();
        Play(a, DateTime.UtcNow.AddDays(-40));
        Play(a, last);

        Track played = Find(a), never = Find(b);

        Assert.Equal(last, played.LastPlayedUtc);
        Assert.Equal(2, played.PlayCount);
        Assert.Null(never.LastPlayedUtc);
        Assert.Equal(0, never.PlayCount);
    }

    [Fact]
    public void Played_filters_find_recent_and_never_played_songs()
    {
        string recent = _lib.AddMp3("Swing/recent.mp3", new GZJam.Testing.TestMp3.Tags(Title: "Recent"));
        string old = _lib.AddMp3("Swing/old.mp3", new GZJam.Testing.TestMp3.Tags(Title: "Old"));
        string never = _lib.AddMp3("Swing/never.mp3", new GZJam.Testing.TestMp3.Tags(Title: "Never"));
        _lib.AddRootAndScan();
        Play(recent, DateTime.UtcNow.AddDays(-2));
        Play(old, DateTime.UtcNow.AddDays(-30));

        string[] Titles(string search) => _lib.Database.Search(search).Select(t => t.Title!).Order().ToArray();

        Assert.Equal(["Recent"], Titles("played:7"));
        Assert.Equal(["Never", "Old"], Titles("-played:7"));   // last week's set left out
        Assert.Equal(["Old", "Recent"], Titles("played:60"));
        Assert.Equal(["Never"], Titles("played:never"));
        Assert.Equal(["Old", "Recent"], Titles("played:yes"));
        Assert.Equal(["Never", "Old", "Recent"], Titles("played:"));   // half typed: ignored
    }

    [Fact]
    public void A_session_can_be_named_and_unnamed()
    {
        var db = _lib.Database;
        string a = _lib.AddMp3("Swing/a.mp3");
        _lib.AddRootAndScan();
        Play(a, DateTime.UtcNow);
        long id = db.GetSessions()[0].Id;

        db.RenameSession(id, "  Friday Night, Oct 14 ");
        Assert.Equal("Friday Night, Oct 14", db.GetSessions()[0].Name);

        db.RenameSession(id, "");
        Assert.Null(db.GetSessions()[0].Name);
    }

    [Fact]
    public void A_song_looked_up_alone_knows_its_plays_and_labels_as_the_song_list_does()
    {
        // #360: looked up by path, a song's plays and labels come from its own rows, not from all of
        // History grouped for each look-up. The answers must be the same as the list's.
        string a = _lib.AddMp3("Swing/a.mp3", new GZJam.Testing.TestMp3.Tags(Title: "Jumpin' at the Woodside"));
        string b = _lib.AddMp3("Swing/b.mp3", new GZJam.Testing.TestMp3.Tags(Title: "Splanky"));
        _lib.AddRootAndScan();
        var db = _lib.Database;
        DateTime last = DateTime.UtcNow.AddDays(-2);
        Play(a, DateTime.UtcNow.AddDays(-30));
        Play(a, last);
        db.AddLabel([a], "Lindy");
        db.AddLabel([a], "Openers");

        foreach (Track alone in new[] { db.FindTrack(a)!, db.FindTracks([a, b])[a], db.TracksForFiles([a, b])[0] })
        {
            Assert.Equal(last, alone.LastPlayedUtc);
            Assert.Equal(2, alone.PlayCount);
            Assert.Equal(["Lindy", "Openers"], alone.Labels.Order());
        }
        Track never = db.FindTrack(b)!;
        Assert.Null(never.LastPlayedUtc);
        Assert.Equal(0, never.PlayCount);
        Assert.Empty(never.Labels);
        Track listed = Find(a);
        Assert.Equal((listed.LastPlayedUtc, listed.PlayCount), (db.FindTrack(a)!.LastPlayedUtc, db.FindTrack(a)!.PlayCount));
    }
}
