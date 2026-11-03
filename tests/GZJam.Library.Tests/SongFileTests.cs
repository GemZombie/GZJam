using GZJam.Testing;

namespace GZJam.Library.Tests;

/// <summary>
/// What a song's info (#129) reads from the file when it's opened: the kind of audio, and the
/// tags the library leaves out.
/// </summary>
public sealed class SongFileTests : IDisposable
{
    private readonly TempLibrary _lib = new();

    public void Dispose() => _lib.Dispose();

    private string CopySample(string name)
    {
        string path = Path.Combine(_lib.MusicPath, "Formats", name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Formats", name), path);
        return path;
    }

    private static void Txxx(TagLib.Id3v2.Tag id3, string name, string value) =>
        TagLib.Id3v2.UserTextInformationFrame.Get(id3, name, create: true).Text = [value];

    [Fact]
    public void An_mp3s_tags_for_other_programs_and_long_ones_are_read_by_the_names_the_file_gives_them()
    {
        string path = _lib.AddMp3("Swing/Song A.mp3", new TestMp3.Tags(Title: "Song A", Artist: "Count Basie"));
        using (var file = TagLib.File.Create(path))
        {
            var id3 = (TagLib.Id3v2.Tag)file.GetTag(TagLib.TagTypes.Id3v2, create: true);
            id3.SetTextFrame("TCOM", "Duke Ellington");                    // one of the song's other tags: shown with them
            id3.SetTextFrame("TSSE", "LAME 3.100");
            id3.SetTextFrame("TSOP", "Basie, Count");
            Txxx(id3, "DANCE", "Waltz");
            Txxx(id3, "MusicBrainz Album Id", "0f6a");
            Txxx(id3, "REPLAYGAIN_TRACK_GAIN", "-6.2 dB");
            Txxx(id3, "Notes", new string('x', 600));
            file.Save();
        }

        SongFile info = SongFile.Read(path)!;

        Assert.Equal(
            [new SongTag("Artist sort", "Basie, Count"), new SongTag("Encoder settings", "LAME 3.100"),
             new SongTag("MusicBrainz Album Id", "0f6a"), new SongTag("Notes", new string('x', SongTags.MaxOtherValueLength) + "…"),
             new SongTag("REPLAYGAIN_TRACK_GAIN", "-6.2 dB")],
            info.LeftOut);   // not the title or artist (GZJam's own fields), not Composer or Dance (shown already)
        Assert.Equal("MP3", info.Format);
        Assert.True(info.Kbps > 0);
        Assert.True(info.SampleRate > 0);
        Assert.InRange(info.Channels, 1, 2);
        Assert.Equal(0, info.BitsPerSample);
    }

    [Theory]
    [InlineData("tone.flac", "FLAC", true)]
    [InlineData("tone.opus", "Opus", false)]
    [InlineData("tone-aac.m4a", "AAC", false)]
    [InlineData("tone-alac.m4a", "Apple Lossless", true)]
    [InlineData("tone.wma", "WMA", false)]
    public void The_kind_of_audio_is_named_as_a_DJ_knows_it(string sample, string format, bool lossless)
    {
        SongFile info = SongFile.Read(CopySample(sample))!;

        Assert.Equal(format, info.Format);
        Assert.True(info.SampleRate > 0);
        Assert.Equal(lossless, info.Lossless);
        if (!lossless)
            Assert.Equal(0, info.BitsPerSample);
    }

    [Fact]
    public void A_wma_files_own_names_for_the_usual_tags_arent_other_tags()
    {
        string path = CopySample("tone.wma");
        using (var file = TagLib.File.Create(path))
        {
            var asf = (TagLib.Asf.Tag)file.GetTag(TagLib.TagTypes.Asf, create: true);
            asf.Album = "April in Paris";                                   // WM/AlbumTitle
            asf.Grouping = "Lindy";                                         // WM/ContentGroupDescription
            asf.BeatsPerMinute = 120;                                       // WM/BeatsPerMinute
            asf.Composers = ["Duke Ellington"];                             // WM/Composer
            asf.Disc = 1;                                                   // WM/PartOfSet
            asf.SetDescriptorString("Basie, Count", "WM/ArtistSortOrder");
            file.Save();
        }

        _lib.AddRootAndScan();

        Track track = _lib.Database.Search(null).Single(t => t.Path == path);
        Assert.Equal([new SongTag("Composer", "Duke Ellington"), new SongTag("Disc", "1")], track.Tags);
        Assert.Contains(new SongTag("ArtistSortOrder", "Basie, Count"), SongFile.Read(path)!.LeftOut);
        Assert.DoesNotContain(SongFile.Read(path)!.LeftOut, t => t.Name is "AlbumTitle" or "ContentGroupDescription" or "BeatsPerMinute");
    }

    [Fact]
    public void A_file_that_isnt_there_has_no_details()
    {
        Assert.Null(SongFile.Read(Path.Combine(_lib.MusicPath, "gone.mp3")));
    }

    [Fact]
    public void The_playlists_a_song_is_in_are_found()
    {
        string a = _lib.AddMp3("Swing/Song A.mp3", new TestMp3.Tags(Title: "Song A"));
        string b = _lib.AddMp3("Swing/Song B.mp3", new TestMp3.Tags(Title: "Song B"));
        _lib.AddRootAndScan();
        var saturday = _lib.Database.CreatePlaylist("Saturday");
        var lateNight = _lib.Database.CreatePlaylist("late night");
        _lib.Database.AddToPlaylist(saturday.Id, [a, b, a]);
        _lib.Database.AddToPlaylist(lateNight.Id, [a]);

        Assert.Equal(["late night", "Saturday"], _lib.Database.GetPlaylistsWith(a));
        Assert.Equal(["Saturday"], _lib.Database.GetPlaylistsWith(b));
    }
}
