using GZJam.Testing;
using Microsoft.Data.Sqlite;

namespace GZJam.Library.Tests;

/// <summary>
/// The Grouping tag, as iTunes, Music and other players keep it in the song files (#161): read,
/// shown and searchable, like the comment.
/// </summary>
public sealed class GroupingTests : IDisposable
{
    private readonly TempLibrary _lib = new();

    public void Dispose() => _lib.Dispose();

    private Track Find(string path) => _lib.Database.Search(null).Single(t => t.Path == path);

    private static void Id3(string path, Action<TagLib.Id3v2.Tag> change)
    {
        using var file = TagLib.File.Create(path);
        change((TagLib.Id3v2.Tag)file.GetTag(TagLib.TagTypes.Id3v2, create: true));
        file.Save();
    }

    /// <summary>A GRP1 frame, as iTunes 12.5.2 and later and Music write it.</summary>
    private static void Grp1(TagLib.Id3v2.Tag id3, string text) =>
        id3.AddFrame(new TagLib.Id3v2.TextInformationFrame(TagLib.ByteVector.FromString("GRP1", TagLib.StringType.Latin1)) { Text = [text] });

    private string CopySample(string name)
    {
        string path = Path.Combine(_lib.MusicPath, "Formats", name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Formats", name), path);
        return path;
    }

    [Fact]
    public void Grouping_is_read_from_mp3_m4a_flac_and_opus()
    {
        string itunes = _lib.AddMp3("Swing/itunes.mp3", new TestMp3.Tags(Title: "New iTunes"));
        string older = _lib.AddMp3("Swing/older.mp3", new TestMp3.Tags(Title: "Older iTunes"));
        string both = _lib.AddMp3("Swing/both.mp3", new TestMp3.Tags(Title: "Both"));
        string none = _lib.AddMp3("Swing/none.mp3", new TestMp3.Tags(Title: "None"));
        Id3(itunes, id3 => Grp1(id3, "Balboa, fast, oldie"));
        Id3(older, id3 => id3.Grouping = "Lindy, medium");                       // TIT1
        Id3(both, id3 => { Grp1(id3, "Shag, upbeat"); id3.Grouping = "A Work"; });   // newer iTunes: TIT1 is "Work"
        var others = new[] { CopySample("tone-aac.m4a"), CopySample("tone.flac"), CopySample("tone.opus") };
        foreach (string path in others)
        {
            using var file = TagLib.File.Create(path);
            file.Tag.Grouping = "Blues, slow, mellow";   // ©grp in M4A, GROUPING in FLAC and Opus
            file.Save();
        }

        _lib.AddRootAndScan();

        Assert.Equal("Balboa, fast, oldie", Find(itunes).Grouping);
        Assert.Equal("Lindy, medium", Find(older).Grouping);
        Assert.Equal("Shag, upbeat", Find(both).Grouping);
        Assert.Null(Find(none).Grouping);
        Assert.All(others, path => Assert.Equal("Blues, slow, mellow", Find(path).Grouping));
    }

    [Theory]
    [InlineData(new byte[] { 0, (byte)'B', (byte)'a', (byte)'l' }, "Bal")]                           // Latin-1
    [InlineData(new byte[] { 1, 0xFF, 0xFE, (byte)'O', 0, (byte)'k', 0 }, "Ok")]                     // UTF-16 with a BOM
    [InlineData(new byte[] { 3, (byte)'a', 0, (byte)'b', 0 }, "a; b")]                               // UTF-8, two values
    [InlineData(new byte[] { 3 }, null)]
    public void A_GRP1_frame_TagLib_doesnt_know_is_read_in_its_encoding(byte[] data, string? expected) =>
        Assert.Equal(expected, TagReader.FrameText(new TagLib.ByteVector(data)));

    [Fact]
    public void Grouping_is_searched_by_plain_words_and_by_grouping()
    {
        string balboa = _lib.AddMp3("Swing/a.mp3", new TestMp3.Tags(Title: "Song A"));
        string lindy = _lib.AddMp3("Swing/b.mp3", new TestMp3.Tags(Title: "Balboa Stomp"));
        Id3(balboa, id3 => Grp1(id3, "Balboa, Slowbal"));
        Id3(lindy, id3 => Grp1(id3, "Lindy"));
        _lib.AddRootAndScan();

        Assert.Equal(["Song A"], _lib.Database.Search("slowbal").Select(t => t.Title));
        Assert.Equal(["Balboa Stomp", "Song A"], _lib.Database.Search("balboa").Select(t => t.Title).Order());
        Assert.Equal(["Song A"], _lib.Database.Search("grouping:balboa").Select(t => t.Title));   // not the title
        Assert.Equal(["Song A"], _lib.Database.Search("-grouping:lindy").Select(t => t.Title));
    }

    [Fact]
    public void An_older_GZJam_writing_songs_without_grouping_keeps_the_search_right()
    {
        string path = _lib.AddMp3("Swing/a.mp3", new TestMp3.Tags(Title: "Song A"));
        Id3(path, id3 => Grp1(id3, "Balboa"));
        _lib.AddRootAndScan();

        // GZJam 0.9.3 knows nothing of grouping: it updates the other columns only.
        using (var c = new SqliteConnection($"Data Source={_lib.Database.DatabasePath};Pooling=False"))
        {
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "UPDATE tracks SET title = 'Renamed';";
            cmd.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();

        Assert.Equal(["Renamed"], _lib.Database.Search("grouping:balboa").Select(t => t.Title));
        Assert.Equal(["Renamed"], _lib.Database.Search("renamed").Select(t => t.Title));
        Assert.Empty(_lib.Database.Search("title:song"));
    }
}
