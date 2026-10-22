namespace GZJam.Library.Tests;

/// <summary>The library finds and reads tags from each format beyond MP3 (two-second tones made with ffmpeg).</summary>
public sealed class FormatTests : IDisposable
{
    private readonly TempLibrary _lib = new();

    public void Dispose() => _lib.Dispose();

    [Fact]
    public void Flac_opus_and_m4a_are_scanned_with_their_tags()
    {
        foreach (string sample in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Formats")))
            File.Copy(sample, Path.Combine(_lib.MusicPath, Path.GetFileName(sample)));

        _lib.AddRootAndScan();
        var tracks = _lib.Database.Search(null).ToDictionary(t => t.FileName);

        Assert.Equal(["tone-aac.m4a", "tone-alac.m4a", "tone.aac", "tone.flac", "tone.opus"], tracks.Keys.Order());
        foreach (var (file, title) in new[] { ("tone.flac", "Tone in FLAC"), ("tone.opus", "Tone in Opus"),
                     ("tone-aac.m4a", "Tone in AAC"), ("tone-alac.m4a", "Tone in ALAC") })
        {
            Track t = tracks[file];
            Assert.Equal(title, t.Title);
            Assert.Equal("Test Band", t.Artist);
            Assert.Equal("Formats", t.Album);
            Assert.Equal("Swing", t.Genre);
            Assert.Equal(1938, t.Year);
            Assert.InRange(t.Duration.TotalSeconds, 1.9, 2.2);
        }
        // Raw AAC has nowhere to keep tags: it's listed by its file name.
        Assert.Null(tracks["tone.aac"].Title);
    }
}
