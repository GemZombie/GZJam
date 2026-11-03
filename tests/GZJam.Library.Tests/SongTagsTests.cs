using GZJam.Testing;

namespace GZJam.Library.Tests;

/// <summary>
/// The song files' other tags (#119): every one GZJam has no column of its own for, read from
/// each format, shown by the name a DJ knows it by, and searched.
/// </summary>
public sealed class SongTagsTests : IDisposable
{
    private readonly TempLibrary _lib = new();

    public void Dispose() => _lib.Dispose();

    private Track Find(string path) => _lib.Database.Search(null).Single(t => t.Path == path);

    private IEnumerable<string?> Titles(string search) => _lib.Database.Search(search).Select(t => t.Title).Order();

    private static void Id3(string path, Action<TagLib.Id3v2.Tag> change)
    {
        using var file = TagLib.File.Create(path);
        change((TagLib.Id3v2.Tag)file.GetTag(TagLib.TagTypes.Id3v2, create: true));
        file.Save();
    }

    private static void Txxx(TagLib.Id3v2.Tag id3, string name, string value) =>
        TagLib.Id3v2.UserTextInformationFrame.Get(id3, name, create: true).Text = [value];

    private static void Text(TagLib.Id3v2.Tag id3, string frame, string value) =>
        id3.SetTextFrame(TagLib.ByteVector.FromString(frame, TagLib.StringType.Latin1), value);

    private string CopySample(string name, string? asName = null)
    {
        string path = Path.Combine(_lib.MusicPath, "Formats", asName ?? name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Formats", name), path);
        return path;
    }

    private string Mp3(string title, Action<TagLib.Id3v2.Tag> tags)
    {
        string path = _lib.AddMp3($"Swing/{title}.mp3", new TestMp3.Tags(Title: title));
        Id3(path, tags);
        return path;
    }

    [Fact]
    public void An_mp3s_frames_and_custom_tags_are_read_by_their_names_and_tags_for_programs_left_out()
    {
        string path = Mp3("Song A", id3 =>
        {
            Text(id3, "TCOM", "Duke Ellington");
            Text(id3, "TMOO", "Mellow");
            Text(id3, "TSSE", "LAME 3.100");                     // the encoder: for programs
            Txxx(id3, "DANCE", "Waltz");
            Txxx(id3, "Band_Leader", "Count Basie");
            Txxx(id3, "MusicBrainz Album Id", "0f6a...");         // for programs
            Txxx(id3, "REPLAYGAIN_TRACK_GAIN", "-6.2 dB");
            Txxx(id3, "ARTISTSORT", "Basie, Count");
            Txxx(id3, "Notes", new string('x', 600));             // too long for a column
        });

        _lib.AddRootAndScan();

        Assert.Equal(
            [new SongTag("Band Leader", "Count Basie"), new SongTag("Composer", "Duke Ellington"),
             new SongTag("Dance", "Waltz"), new SongTag("Mood", "Mellow")],
            Find(path).Tags);
        Assert.Equal("Waltz", Find(path).Tag("dance"));
        Assert.Equal("Count Basie", Find(path).Tag("bandleader"));
        Assert.Null(Find(path).Tag("notes"));
    }

    [Fact]
    public void The_same_tag_in_flac_opus_and_m4a_is_one_tag()
    {
        string flac = CopySample("tone.flac");
        string opus = CopySample("tone.opus");
        string m4a = CopySample("tone-aac.m4a");
        foreach (string path in new[] { flac, opus })
        {
            using var file = TagLib.File.Create(path);
            var xiph = (TagLib.Ogg.XiphComment)file.GetTag(TagLib.TagTypes.Xiph, create: true);
            xiph.SetField("COMPOSER", "Duke Ellington");
            xiph.SetField("DANCE", "Waltz", "Foxtrot");
            xiph.SetField("LABEL", "Columbia");
            xiph.SetField("REPLAYGAIN_TRACK_GAIN", "-6.2 dB");
            file.Save();
        }
        using (var file = TagLib.File.Create(m4a))
        {
            var apple = (TagLib.Mpeg4.AppleTag)file.GetTag(TagLib.TagTypes.Apple, create: true);
            apple.Composers = ["Duke Ellington"];                                    // ©wrt
            apple.SetDashBox("com.apple.iTunes", "DANCE", "Waltz");                // a freeform tag
            apple.SetDashBox("com.apple.iTunes", "iTunNORM", " 0000013F 00000156");
            file.Save();
        }

        _lib.AddRootAndScan();

        var expected = new[] { new SongTag("Composer", "Duke Ellington"), new SongTag("Dance", "Waltz; Foxtrot"), new SongTag("Publisher", "Columbia") };
        Assert.Equal(expected, Find(flac).Tags);
        Assert.Equal(expected, Find(opus).Tags);
        Assert.Equal([new SongTag("Composer", "Duke Ellington"), new SongTag("Dance", "Waltz")], Find(m4a).Tags);
        Assert.Equal([new TagName("composer", "Composer", 3), new TagName("dance", "Dance", 3), new TagName("publisher", "Publisher", 2)],
            _lib.Database.GetTagNames());
    }

    [Fact]
    public void A_tag_is_a_filter_by_its_name_and_plain_words_find_its_values()
    {
        Mp3("Song A", id3 => { Txxx(id3, "Dance", "Viennese Waltz"); Text(id3, "TCOM", "Duke Ellington"); });
        Mp3("Song B", id3 => { Txxx(id3, "Dance", "Foxtrot"); Txxx(id3, "Band leader", "Waltz King"); });
        Mp3("Waltz for Debby", _ => { });
        _lib.AddRootAndScan();

        Assert.Equal(["Song A"], Titles("dance:waltz"));
        Assert.Equal(["Song A"], Titles("dance:VIENN"));                          // a word's start, any case
        Assert.Equal(["Song A"], Titles("dance:\"viennese wal\""));               // a phrase
        Assert.Empty(Titles("dance:\"waltz viennese\""));
        Assert.Equal(["Song B"], Titles("bandleader:waltz"));                     // "Band leader", in a filter
        Assert.Equal(["Song B", "Waltz for Debby"], Titles("-dance:waltz"));
        Assert.Equal(["Song A"], Titles("composer:duke dance:waltz"));
        Assert.Equal(["Song A", "Song B"], Titles("dance:waltz OR dance:foxtrot"));
        Assert.Equal(["Song A", "Song B", "Waltz for Debby"], Titles("waltz"));   // plain words: the values too
        Assert.Equal("Waltz for Debby", _lib.Database.Search("waltz")[0].Title);   // the title first
        Assert.Empty(Titles("dance"));                                            // not the tag's name
        Assert.Equal(3, Titles("dance:").Count());                                // being typed: ignored
        Assert.Empty(Titles("tempo:waltz"));                                      // no such tag: words, found nowhere
    }

    [Fact]
    public void Tag_values_are_suggested_and_counted_like_genres()
    {
        Mp3("Song A", id3 => Txxx(id3, "Dance", "Waltz"));
        Mp3("Song B", id3 => Txxx(id3, "Dance", "Waltz; Foxtrot"));
        Mp3("Song C", id3 => Txxx(id3, "dance", "Foxtrot"));
        Mp3("Song D", id3 => Txxx(id3, "DANCE", "Quickstep"));
        _lib.AddRootAndScan();

        Assert.Equal([new ValueCount("Foxtrot", 2), new ValueCount("Waltz", 2), new ValueCount("Quickstep", 1)],
            _lib.Database.GetTagValues("dance"));
        Assert.Equal([new TagName("dance", "Dance", 4)], _lib.Database.GetTagNames());
    }

    [Fact]
    public void Songs_scanned_before_are_read_again_for_their_tags()
    {
        string path = Mp3("Song A", id3 => Txxx(id3, "Dance", "Waltz"));
        _lib.AddRootAndScan();
        using (var c = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_lib.Database.DatabasePath};Pooling=False"))
        {
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "UPDATE tracks SET tags = NULL, tag_text = NULL, tag_version = 4;";   // as GZJam 1.1 left it
            cmd.ExecuteNonQuery();
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Assert.Empty(Find(path).Tags);

        _lib.Scanner.Scan(_lib.Database.GetRoots()[0]);

        Assert.Equal("Waltz", Find(path).Tag("dance"));
        Assert.Equal(["Song A"], Titles("dance:waltz"));
    }

    [Theory]
    [InlineData("DANCE", "Dance")]
    [InlineData("band_leader", "Band leader")]
    [InlineData("Band_Leader", "Band Leader")]
    [InlineData("BandLeader", "BandLeader")]
    [InlineData("composer", "Composer")]
    [InlineData("INITIALKEY", "Key")]
    [InlineData("ORGANIZATION", "Publisher")]
    [InlineData("MUSICBRAINZ_TRACKID", null)]
    [InlineData("Acoustid Id", null)]
    [InlineData("ALBUMARTISTSORT", null)]
    [InlineData("title", null)]
    [InlineData("ENCODED-BY", null)]
    [InlineData("  ", null)]
    [InlineData("123", null)]
    public void Tag_names_are_shown_as_a_DJ_knows_them(string name, string? shown) =>
        Assert.Equal(shown, SongTags.ShownName(name));

    [Theory]
    [InlineData("Café Society", "cafe", false, true)]
    [InlineData("Café Society", "soc caf", false, true)]
    [InlineData("Café Society", "society cafe", true, false)]
    [InlineData("Café Society", "cafe soc", true, true)]
    [InlineData("Café Society", "ociety", false, false)]
    public void A_tag_filter_matches_words_as_the_search_index_does(string value, string typed, bool phrase, bool matches) =>
        Assert.Equal(matches, SongTags.Matches(SongTags.Encode([new SongTag("Dance", value)]), "dance", typed, phrase));
}
