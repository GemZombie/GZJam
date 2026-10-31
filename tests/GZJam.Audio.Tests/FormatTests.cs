using GZJam.Testing;

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
        TrackAnalysis result = new TrackAnalyzer(_engine).Analyze(Sample(file), detectBpm: false, cancellationToken: TestContext.Current.CancellationToken);

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
            var error = Assert.Throws<AudioException>(() => analyzer.Analyze(Sample(file), detectBpm: false, cancellationToken: TestContext.Current.CancellationToken));
            Assert.Equal("it's AAC, which GZJam can't play on Linux (Apple Lossless .m4a files play).", error.Message);
            return;
        }
        Assert.SkipWhen(OperatingSystem.IsWindows() && !AudioException.HasMediaFoundation,
            "This copy of Windows has no Media Foundation (a server or an \"N\" edition).");

        TrackAnalysis result = analyzer.Analyze(Sample(file), detectBpm: false, cancellationToken: TestContext.Current.CancellationToken);

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
            var error = Assert.Throws<AudioException>(() => new TrackAnalyzer(_engine).Analyze(path, detectBpm: false, cancellationToken: TestContext.Current.CancellationToken));
            Assert.Equal("it's a kind of file GZJam can't play.", error.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void An_mp3_whose_audio_is_behind_a_damaged_tag_plays_from_where_its_audio_starts()
    {
        string path = Path.Combine(Path.GetTempPath(), $"gzjam-damaged-{Guid.NewGuid():N}.mp3");
        long audio = DamagedMp3.Write(path, "Bloodest Saxophone", "Bloodest Saxophone", 2000);
        try
        {
            _engine.UseNoSoundDevice();
            Assert.Equal(0, ManagedBass.Bass.CreateStream(path, 0, 0, ManagedBass.BassFlags.Decode));   // BASS alone gives up
            Assert.Equal(audio, SongFile.Mp3AudioStart(path));
            TrackAnalysis result = new TrackAnalyzer(_engine).Analyze(path, detectBpm: false, cancellationToken: TestContext.Current.CancellationToken);
            Assert.InRange(result.Length.TotalSeconds, 1.95, 2.15);
            Assert.InRange(result.AudibleEnd.TotalSeconds, 1.8, 2.15);   // the tone, not the damaged tag as noise

            // Something that isn't audio still says so, after looking.
            File.WriteAllBytes(path, new byte[100_000]);
            Assert.Null(SongFile.Mp3AudioStart(path));
            var error = Assert.Throws<AudioException>(() => new TrackAnalyzer(_engine).Analyze(path, detectBpm: false, cancellationToken: TestContext.Current.CancellationToken));
            Assert.Equal("it's a kind of file GZJam can't play.", error.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void An_m4a_whose_first_box_runs_past_its_end_is_damaged_and_said_so_without_waiting_on_bass()
    {
        byte[] good = File.ReadAllBytes(Sample("tone-alac.m4a"));   // ftyp, free, mdat, then moov
        string path = Path.Combine(Path.GetTempPath(), $"gzjam-damaged-{Guid.NewGuid():N}.m4a");
        try
        {
            // As in the DJ's file: the first box's size damaged, 646 MB in a small file. BASS takes
            // minutes over one, holding up every other sound call (#224).
            byte[] damaged = [.. good];
            damaged[0] = 0x26; damaged[1] = 0x89;
            File.WriteAllBytes(path, damaged);
            Assert.True(SongFile.Mp4Damaged(path));
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var error = Assert.Throws<AudioException>(() => new TrackAnalyzer(_engine).Analyze(path, detectBpm: false, cancellationToken: TestContext.Current.CancellationToken));
            Assert.Equal("the file is damaged, so GZJam can't find its sound.", error.Message);
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"{watch.Elapsed}");

            // Cut short before its index: damaged too.
            File.WriteAllBytes(path, good[..(good.Length / 2)]);
            Assert.True(SongFile.Mp4Damaged(path));

            // Cut short after its index (moov first): BASS plays what's there, as before.
            int moov = 28 + 8 + 34060;
            byte[] indexFirst = [.. good[..36], .. good[moov..], .. good[36..moov]];
            File.WriteAllBytes(path, indexFirst[..(indexFirst.Length * 3 / 4)]);
            Assert.False(SongFile.Mp4Damaged(path));
        }
        finally
        {
            File.Delete(path);
        }

        // Whole files, and files that aren't MP4, aren't damaged here.
        foreach (string file in new[] { "tone-alac.m4a", "tone-aac.m4a", "tone.flac", "tone.mp3", "tone.opus", "tone.aac" })
            Assert.False(SongFile.Mp4Damaged(Sample(file)), file);
    }
}
