using GZJam.Testing;

namespace GZJam.Library.Tests;

/// <summary>Saved track analyses, and the BPM range that detected tempos are fitted into.</summary>
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

    [Theory]
    [InlineData(120, 180, 300, 240)]   // half time, doubled into the range
    [InlineData(66, 100, 260, 132)]
    [InlineData(300, 120, 180, 150)]   // double time, halved
    [InlineData(265, 60, 320, 265)]    // already inside: unchanged
    [InlineData(110, 120, 150, 110)]   // no octave fits: the nearest stays
    [InlineData(90, 100, 400, 180)]    // two fit (180, 360): the fewest steps
    public void Detected_tempos_are_doubled_or_halved_into_the_range(double detected, int min, int max, double shown) =>
        Assert.Equal(shown, BpmRange.Fold(detected, min, max));
}
