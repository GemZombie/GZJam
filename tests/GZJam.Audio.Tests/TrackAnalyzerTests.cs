using GZJam.Testing;

namespace GZJam.Audio.Tests;

/// <summary>Analysis of generated audio: silence trimming, waveform bands and tempo.</summary>
[Collection("BASS")]
public sealed class TrackAnalyzerTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("gzjam-analyze-").FullName;
    private readonly AudioEngine _engine = new();
    private readonly TrackAnalyzer _analyzer;

    public TrackAnalyzerTests() => _analyzer = new TrackAnalyzer(_engine);

    public void Dispose()
    {
        _engine.Dispose();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private string File(string name) => Path.Combine(_dir, name);

    [Fact]
    public void Finds_where_the_music_starts_and_ends()
    {
        string path = TestWav.WriteTone(File("padded.wav"), TimeSpan.FromSeconds(5),
            silenceBefore: TimeSpan.FromSeconds(2), silenceAfter: TimeSpan.FromSeconds(3));

        TrackAnalysis result = _analyzer.Analyze(path, detectBpm: false);

        Assert.Equal(10, result.Length.TotalSeconds, 0.01);
        // A little is kept either side, so the attack and the last ring-out aren't shaved.
        Assert.InRange(result.AudibleStart.TotalSeconds, 1.9, 2.0);
        Assert.InRange(result.AudibleEnd.TotalSeconds, 7.0, 7.35);
    }

    [Fact]
    public void A_quiet_ending_is_kept()
    {
        // The last two seconds are 30 dB down: soft, but music.
        string loud = TestWav.WriteTone(File("loud.wav"), TimeSpan.FromSeconds(4), amplitude: 0.5);
        string path = File("quiet-ending.wav");
        Concatenate(path, loud, TestWav.WriteTone(File("soft.wav"), TimeSpan.FromSeconds(2), amplitude: 0.5 / 31.6));

        TrackAnalysis result = _analyzer.Analyze(path, detectBpm: false);

        Assert.Equal(result.Length, result.AudibleEnd);
    }

    [Fact]
    public void Silence_throughout_is_left_alone()
    {
        string path = TestWav.WriteTone(File("silent.wav"), TimeSpan.FromSeconds(3), amplitude: 0);

        TrackAnalysis result = _analyzer.Analyze(path);

        Assert.Equal(TimeSpan.Zero, result.AudibleStart);
        Assert.Equal(result.Length, result.AudibleEnd);
        Assert.Null(result.Bpm);
    }

    [Theory]
    [InlineData(60, nameof(Waveform.Low))]
    [InlineData(1000, nameof(Waveform.Mid))]
    [InlineData(8000, nameof(Waveform.High))]
    public void The_waveform_shows_which_band_a_sound_is_in(double frequency, string band)
    {
        string path = TestWav.WriteTone(File($"{frequency}.wav"), TimeSpan.FromSeconds(3), frequency: frequency);

        Waveform w = _analyzer.Analyze(path, detectBpm: false).Waveform;

        double Average(byte[] values) => values.Average(v => (double)v);
        var levels = new Dictionary<string, double>
        {
            [nameof(Waveform.Low)] = Average(w.Low),
            [nameof(Waveform.Mid)] = Average(w.Mid),
            [nameof(Waveform.High)] = Average(w.High),
        };
        Assert.Equal(band, levels.MaxBy(l => l.Value).Key);
    }

    [Fact]
    public void Waveforms_survive_saving()
    {
        string path = TestWav.WriteTone(File("tone.wav"), TimeSpan.FromSeconds(2));
        Waveform w = _analyzer.Analyze(path, detectBpm: false).Waveform;

        Waveform copy = Waveform.FromBytes(w.ToBytes())!;

        Assert.Equal(w.Low, copy.Low);
        Assert.Equal(w.Mid, copy.Mid);
        Assert.Equal(w.High, copy.High);
        Assert.Equal(w.Loudness, copy.Loudness);
        Assert.Null(Waveform.FromBytes([1, 0, 1, 2, 3, 4]));   // an older format: re-analyze
    }

    // A very fast tune with a heavy backbeat can still read as half time; the DJ's BPM range
    // (see BpmRangeTests) settles those.
    [Theory]
    [InlineData(130)]
    [InlineData(180)]
    public void Finds_the_beat_not_the_backbeat(double bpm)
    {
        // A loud snare on 2 and 4 makes half the tempo look strong; dancers count the beat.
        string path = TestWav.WriteSwingBeat(File($"swing-{bpm}.wav"), bpm, TimeSpan.FromSeconds(40));

        double? found = _analyzer.Analyze(path).Bpm;

        Assert.NotNull(found);
        Assert.InRange(found.Value, bpm * 0.98, bpm * 1.02);
    }

    [Fact]
    public void A_slow_swing_is_not_doubled()
    {
        // Swung eighths fall two thirds through the beat, not halfway: still one beat.
        string path = TestWav.WriteSwingBeat(File("slow.wav"), 72, TimeSpan.FromSeconds(40), backbeat: 0.3);

        double? found = _analyzer.Analyze(path).Bpm;

        Assert.NotNull(found);
        Assert.InRange(found.Value, 70, 74);
    }

    [Theory]
    [InlineData(120, 180, 300, 240)]   // only half time found: doubled into the range
    [InlineData(300, 120, 180, 150)]   // double time: halved
    [InlineData(110, 120, 150, 110)]   // no octave fits: as near as it gets
    public void A_chosen_range_folds_the_detected_tempo_into_it(double detected, int min, int max, double chosen) =>
        Assert.Equal(chosen, TrackAnalyzer.ChooseBpm([(detected, 1.0)], (min, max)));

    [Fact]
    public void A_chosen_range_picks_the_best_tempo_inside_it()
    {
        // Not just half or double: a three-to-two mistake is fixed too.
        (double, double)[] candidates = [(72, 0.5), (216, 0.4), (144, 0.3)];

        Assert.Equal(72, TrackAnalyzer.ChooseBpm(candidates, null));
        Assert.Equal(216, TrackAnalyzer.ChooseBpm(candidates, (200, 320)));
        Assert.Equal(144, TrackAnalyzer.ChooseBpm(candidates, (100, 160)));
    }

    [Fact]
    public void A_fast_tune_read_as_half_time_is_found_in_a_fast_range()
    {
        // A heavy backbeat on a very fast tune: automatic detection may say half time.
        string path = TestWav.WriteSwingBeat(File("fast.wav"), 240, TimeSpan.FromSeconds(40));

        double? found = _analyzer.Analyze(path, bpmRange: (200, 320)).Bpm;

        Assert.NotNull(found);
        Assert.InRange(found.Value, 235, 245);
    }

    /// <summary>Joins WAVs written by <see cref="TestWav"/> (same format) into one.</summary>
    private static void Concatenate(string path, params string[] parts)
    {
        const int header = 44;
        var data = parts.SelectMany(p => System.IO.File.ReadAllBytes(p).Skip(header)).ToArray();
        var bytes = System.IO.File.ReadAllBytes(parts[0])[..header];
        BitConverter.GetBytes(36 + data.Length).CopyTo(bytes, 4);
        BitConverter.GetBytes(data.Length).CopyTo(bytes, 40);
        System.IO.File.WriteAllBytes(path, [.. bytes, .. data]);
    }
}
