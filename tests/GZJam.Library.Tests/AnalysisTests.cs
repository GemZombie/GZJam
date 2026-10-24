using GZJam.Testing;

namespace GZJam.Library.Tests;

/// <summary>Saved track analyses, and BPM ranges chosen for tracks.</summary>
public sealed class AnalysisTests : IDisposable
{
    private readonly TempLibrary _lib = new();

    public void Dispose() => _lib.Dispose();

    private static StoredAnalysis Sample(double? bpm = 180, int version = 1) =>
        new(version, TimeSpan.FromSeconds(200), TimeSpan.FromSeconds(0.4), TimeSpan.FromSeconds(196.5), bpm, [1, 0, 2, 7, 7, 8, 8, 9, 9]);

    [Fact]
    public void An_analysis_is_saved_and_found_again()
    {
        string path = _lib.AddMp3("Swing/a.mp3");
        FileVersion version = FileVersion.Of(path)!.Value;

        _lib.Database.SaveAnalysis(path, version, Sample());
        StoredAnalysis found = _lib.Database.GetAnalysis(path, version)!;

        Assert.Equal(TimeSpan.FromSeconds(0.4), found.AudibleStart);
        Assert.Equal(TimeSpan.FromSeconds(196.5), found.AudibleEnd);
        Assert.Equal(180, found.Bpm);
        Assert.Equal(Sample().Waveform, found.Waveform);
    }

    [Fact]
    public void A_songs_jumps_are_remembered_in_order()
    {
        string path = _lib.AddMp3("Swing/long.mp3");
        Assert.Empty(_lib.Database.GetJumps(path));
        SongJump first = new(TimeSpan.FromSeconds(95.5), TimeSpan.FromSeconds(181)), second = new(TimeSpan.FromSeconds(200), TimeSpan.FromSeconds(60));

        _lib.Database.SetJumps(path, [first, second]);
        Assert.Equal([first, second], _lib.Database.GetJumps(path));

        _lib.Database.SetJumps(path, [second]);
        Assert.Equal([second], _lib.Database.GetJumps(path));
        _lib.Database.SetJumps(path, []);
        Assert.Empty(_lib.Database.GetJumps(path));
    }

    [Fact]
    public void Loudness_and_peak_are_saved_with_the_analysis()
    {
        string path = _lib.AddMp3("Swing/loud.mp3");
        FileVersion version = FileVersion.Of(path)!.Value;

        _lib.Database.SaveAnalysis(path, version, Sample() with { Lufs = -19.4, PeakDb = -2.1 });
        StoredAnalysis found = _lib.Database.GetAnalysis(path, version)!;

        Assert.Equal(-19.4, found.Lufs);
        Assert.Equal(-2.1, found.PeakDb);
    }

    [Fact]
    public void A_changed_file_needs_analyzing_again()
    {
        string path = _lib.AddMp3("Swing/a.mp3");
        _lib.Database.SaveAnalysis(path, FileVersion.Of(path)!.Value, Sample());

        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(5));

        Assert.Null(_lib.Database.GetAnalysis(path, FileVersion.Of(path)!.Value));
    }

    [Fact]
    public void Pending_lists_library_tracks_without_a_current_analysis()
    {
        string done = _lib.AddMp3("Swing/done.mp3");
        string old = _lib.AddMp3("Swing/old.mp3");
        _lib.AddMp3("Swing/new.mp3", new TestMp3.Tags(Bpm: "150"));
        _lib.AddRootAndScan();
        _lib.Database.SaveAnalysis(done, FileVersion.Of(done)!.Value, Sample(version: 2));
        _lib.Database.SaveAnalysis(old, FileVersion.Of(old)!.Value, Sample(version: 1));   // an older analyzer

        var pending = _lib.Database.GetPendingAnalysis(version: 2);

        Assert.Equal(["new.mp3", "old.mp3"], pending.Select(p => Path.GetFileName(p.Path)));
        Assert.True(pending[0].HasTagBpm);
    }

    [Fact]
    public void Search_results_carry_the_detected_bpm_but_prefer_the_tag()
    {
        string untagged = _lib.AddMp3("Swing/untagged.mp3", new TestMp3.Tags(Title: "Untagged"));
        string tagged = _lib.AddMp3("Swing/tagged.mp3", new TestMp3.Tags(Title: "Tagged", Bpm: "150"));
        _lib.AddRootAndScan();
        _lib.Database.SaveAnalysis(untagged, FileVersion.Of(untagged)!.Value, Sample(bpm: 180));
        _lib.Database.SaveAnalysis(tagged, FileVersion.Of(tagged)!.Value, Sample(bpm: 75));

        var results = _lib.Database.Search(null).ToDictionary(t => t.Title!);

        Assert.Equal(180, results["Untagged"].DetectedBpm);
        Assert.Equal(150, results["Tagged"].TagBpm);
        Assert.Equal(150, results["Tagged"].Bpm);   // the tag wins
    }

    [Fact]
    public void A_chosen_bpm_range_is_remembered_and_can_be_cleared()
    {
        string path = _lib.AddMp3("Swing/a.mp3");

        _lib.Database.SetBpmRange(path, new BpmRange(140, 220));
        Assert.Equal(new BpmRange(140, 220), _lib.Database.GetBpmRange(path));

        _lib.Database.SetBpmRange(path, new BpmRange(200, 320));
        Assert.Equal(new BpmRange(200, 320), _lib.Database.GetBpmRange(path));

        _lib.Database.SetBpmRange(path, null);
        Assert.Null(_lib.Database.GetBpmRange(path));
    }

    [Fact]
    public void With_a_chosen_range_the_detected_bpm_beats_the_tag()
    {
        string path = _lib.AddMp3("Swing/tagged.mp3", new TestMp3.Tags(Title: "Tagged", Bpm: "103"));
        _lib.AddRootAndScan();
        _lib.Database.SaveAnalysis(path, FileVersion.Of(path)!.Value, Sample(bpm: 206));
        _lib.Database.SetBpmRange(path, new BpmRange(200, 320));

        Track track = Assert.Single(_lib.Database.Search(null));

        Assert.Equal(new BpmRange(200, 320), track.BpmRange);
        Assert.Equal(206, track.Bpm);
        track.BpmRange = null;   // back to automatic: the tag again
        Assert.Equal(103, track.Bpm);
    }

    [Fact]
    public void A_start_and_end_set_by_hand_are_remembered_separately()
    {
        string path = _lib.AddMp3("Live/applause.mp3");
        Assert.True(_lib.Database.GetTrim(path).IsEmpty);

        _lib.Database.SetTrim(path, new ManualTrim(TimeSpan.FromSeconds(6.4), null));
        Assert.Equal(new ManualTrim(TimeSpan.FromSeconds(6.4), null), _lib.Database.GetTrim(path));

        _lib.Database.SetTrim(path, new ManualTrim(TimeSpan.FromSeconds(6.4), TimeSpan.FromSeconds(181.25)));
        Assert.Equal(TimeSpan.FromSeconds(181.25), _lib.Database.GetTrim(path).End);

        _lib.Database.SetTrim(path, default);   // both back to automatic
        Assert.True(_lib.Database.GetTrim(path).IsEmpty);
    }

    [Fact]
    public void A_songs_eq_is_remembered_and_forgotten_when_flat()
    {
        string path = _lib.AddMp3("78s/hissy.mp3");
        Assert.True(_lib.Database.GetEq(path).IsFlat);

        _lib.Database.SetEq(path, new TrackEq(0, -1.5, -6));
        Assert.Equal(new TrackEq(0, -1.5, -6), _lib.Database.GetEq(path));
        _lib.Database.SetEq(path, new TrackEq(2, -1.5, -9));
        Assert.Equal(new TrackEq(2, -1.5, -9), _lib.Database.GetEq(path));

        _lib.Database.SetEq(path, TrackEq.Flat);
        Assert.True(_lib.Database.GetEq(path).IsFlat);

        // The DJ's gain for the song is kept with its EQ; gain alone isn't flat.
        _lib.Database.SetEq(path, new TrackEq(0, 0, 0, Gain: -3.5));
        Assert.Equal(-3.5, _lib.Database.GetEq(path).Gain);

        // Where the mid sits and how much it's evened out are kept with it; either alone isn't flat.
        _lib.Database.SetEq(path, new TrackEq(0, -2, 0, MidHz: 400, Even: 35));
        Assert.Equal(new TrackEq(0, -2, 0, MidHz: 400, Even: 35), _lib.Database.GetEq(path));
        Assert.False(new TrackEq(0, 0, 0, MidHz: 2000).IsFlat);
        Assert.False(new TrackEq(0, 0, 0, Even: 5).IsFlat);
        Assert.True(default(TrackEq).IsFlat);   // a zero frequency means the default
    }
}
