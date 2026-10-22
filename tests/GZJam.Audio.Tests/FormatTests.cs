namespace GZJam.Audio.Tests;

/// <summary>
/// Each format beyond MP3, decoded for real (two-second tones made with ffmpeg). The add-ons and
/// the computer's own decoders differ by system, so this runs on Windows, Linux and a Mac in CI.
/// </summary>
[Collection("BASS")]
public sealed class FormatTests : IDisposable
{
    private readonly AudioEngine _engine = new();

    public void Dispose() => _engine.Dispose();

    private static string Sample(string name) => Path.Combine(AppContext.BaseDirectory, "Formats", name);

    [Theory]
    [InlineData("tone.flac")]
    [InlineData("tone.opus")]
    [InlineData("tone-alac.m4a")]
    public void Plays_flac_opus_and_apple_lossless_everywhere(string file)
    {
        TrackAnalysis result = new TrackAnalyzer(_engine).Analyze(Sample(file), detectBpm: false);

        Assert.InRange(result.Length.TotalSeconds, 1.95, 2.1);
        Assert.InRange(result.AudibleEnd.TotalSeconds, 1.8, 2.1);   // heard, not silence
    }

    [Theory]
    [InlineData("tone-aac.m4a")]
    [InlineData("tone.aac")]
    public void Plays_aac_with_the_computers_decoder_and_says_why_not_on_linux(string file)
    {
        var analyzer = new TrackAnalyzer(_engine);
        if (OperatingSystem.IsLinux())
        {
            var error = Assert.Throws<AudioException>(() => analyzer.Analyze(Sample(file), detectBpm: false));
            Assert.Equal("it's AAC, which GZJam can't play on Linux (Apple Lossless .m4a files play).", error.Message);
            return;
        }
        Assert.SkipWhen(OperatingSystem.IsWindows() && !AudioException.HasMediaFoundation,
            "This copy of Windows has no Media Foundation (a server or an \"N\" edition).");

        TrackAnalysis result = analyzer.Analyze(Sample(file), detectBpm: false);

        // Raw AAC doesn't say how long it is, so its length is an estimate (Windows: 2.25 s).
        double longest = file.EndsWith(".aac", StringComparison.Ordinal) ? 2.4 : 2.15;
        Assert.InRange(result.Length.TotalSeconds, 1.95, longest);
        Assert.InRange(result.AudibleEnd.TotalSeconds, 1.8, longest);
    }

    [Fact]
    public void The_add_ons_for_this_system_load()
    {
        string[] expected = OperatingSystem.IsMacOS() ? ["bassflac", "bassopus"] : ["bassflac", "bassopus", "bassalac"];
        Assert.Equal(expected, AudioEngine.LoadedAddOns);
    }

    [Fact]
    public void A_file_that_isnt_audio_says_so()
    {
        string path = Path.Combine(Path.GetTempPath(), $"gzjam-not-audio-{Guid.NewGuid():N}.flac");
        File.WriteAllText(path, "not audio");
        try
        {
            var error = Assert.Throws<AudioException>(() => new TrackAnalyzer(_engine).Analyze(path, detectBpm: false));
            Assert.Equal("it's a kind of file GZJam can't play.", error.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
