using GZJam.Testing;

namespace GZJam.Audio.Tests;

/// <summary>Loudness measurement (EBU R128) and how matching turns tracks up or down.</summary>
[Collection("BASS")]
public sealed class LoudnessTests : IDisposable
{
    private const int Rate = 44100;
    private readonly string _dir = Directory.CreateTempSubdirectory("gzjam-loudness-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    /// <summary>Measures a sine wave, optionally followed by silence.</summary>
    private static LoudnessMeter Measure(double amplitude, double hz, int channels, double seconds = 10, double silence = 0)
    {
        var meter = new LoudnessMeter(channels, Rate);
        int frames = (int)(Rate * (seconds + silence));
        var buffer = new float[4096 * channels];
        for (int done = 0; done < frames;)
        {
            int n = Math.Min(4096, frames - done);
            for (int f = 0; f < n; f++)
            {
                int i = done + f;
                float s = i < Rate * seconds ? (float)(amplitude * Math.Sin(2 * Math.PI * hz * i / Rate)) : 0;
                for (int ch = 0; ch < channels; ch++)
                    buffer[f * channels + ch] = s;
            }
            meter.Push(buffer, n);
            done += n;
        }
        return meter;
    }

    [Fact]
    public void A_full_scale_1_khz_tone_in_stereo_measures_0_lufs()
    {
        // The standard's reference: 997 Hz at 0 dBFS in both channels reads 0 LUFS.
        var meter = Measure(1.0, 997, channels: 2);
        Assert.Equal(0, meter.IntegratedLufs!.Value, 0.1);
        Assert.Equal(0, meter.PeakDb!.Value, 0.05);
    }

    [Fact]
    public void Half_the_level_is_6_lu_quieter()
    {
        Assert.Equal(-6.0, Measure(0.5, 997, channels: 2).IntegratedLufs!.Value, 0.1);
    }

    [Fact]
    public void A_mono_file_counts_as_both_speakers()
    {
        // Played through both speakers, so it's as loud as the same tone in stereo.
        Assert.Equal(-6.0, Measure(0.5, 997, channels: 1).IntegratedLufs!.Value, 0.1);
    }

    [Fact]
    public void Silence_doesnt_make_a_track_seem_quieter()
    {
        // Gating ignores the silence: a long quiet ending doesn't pull the reading down.
        Assert.Equal(-6.0, Measure(0.5, 997, channels: 2, seconds: 10, silence: 20).IntegratedLufs!.Value, 0.1);
    }

    [Fact]
    public void Deep_bass_counts_for_less()
    {
        Assert.True(Measure(0.5, 30, channels: 2).IntegratedLufs < -8);
    }

    [Fact]
    public void Silence_has_no_loudness()
    {
        Assert.Null(Measure(0, 997, channels: 2).IntegratedLufs);
    }

    [Theory]
    [InlineData(-10, -0.5, -6)]     // loud remaster: turned down to the target
    [InlineData(-20, -10, 4)]       // quiet transfer with headroom: turned up all the way
    [InlineData(-20, -1, 2.5)]      // quiet but peaky: only as far as its peaks allow
    [InlineData(-34, -20, 9)]       // very quiet: no more than the maximum boost
    [InlineData(-16, -3, 0)]
    public void Matching_brings_tracks_to_the_target_without_pushing_peaks_into_the_limiter(double lufs, double peak, double gain) =>
        Assert.Equal(gain, LoudnessMatch.GainDb(lufs, peak), 1);

    [Fact]
    public void The_analyzer_measures_loudness_and_peak()
    {
        string tone = TestWav.WriteTone(Path.Combine(_dir, "tone.wav"), TimeSpan.FromSeconds(8), amplitude: 0.25, frequency: 997);
        using var engine = new AudioEngine();

        TrackAnalysis analysis = new TrackAnalyzer(engine).Analyze(tone, detectBpm: false, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(-12.0, analysis.Lufs!.Value, 0.2);
        Assert.Equal(-12.0, analysis.PeakDb!.Value, 0.1);
    }
}
