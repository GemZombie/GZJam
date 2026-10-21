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
