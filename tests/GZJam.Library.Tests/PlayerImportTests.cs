using System.Text;

namespace GZJam.Library.Tests;

/// <summary>Bringing playlists (M3U) and star ratings kept in the song files over from other players.</summary>
public sealed class PlayerImportTests : IDisposable
{
    private readonly TempLibrary _lib = new();

    public void Dispose() => _lib.Dispose();

    private string WriteM3u(string name, string text, Encoding? encoding = null)
    {
        string path = Path.Combine(_lib.Directory, "Playlists", name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, (encoding ?? new UTF8Encoding(false)).GetBytes(text));
        return path;
    }

    private Track Find(string path) => _lib.Database.Search(null).Single(t => t.Path == path);

    // ───────────── M3U ─────────────

    [Fact]
    public void An_m3u_lists_its_songs_by_absolute_path_relative_path_or_file_address_skipping_comments_and_streams()
    {
        string a = _lib.AddMp3("Swing/a.mp3");
        string b = _lib.AddMp3("Swing/Track #2 100%.mp3");
        string c = _lib.AddMp3("Blues/c.mp3");
        string m3u = WriteM3u("Friday.m3u8",
            "#EXTM3U\r\n" +
            "#EXTINF:183,Count Basie - A\r\n" +
            a + "\r\n" +
            "\r\n" +
            "../Music/Swing/Track #2 100%.mp3\r\n" +
            new Uri(c).AbsoluteUri + "\r\n" +
            "http://radio.example.com/stream\r\n");

        Assert.Equal([a, b, c], PlaylistFile.Read(m3u));
    }

    [Fact]
    public void A_plain_m3u_that_isnt_utf8_is_read_in_the_computers_code_page()
    {
        string song = _lib.AddMp3("Swing/Café Society.mp3");
        Encoding latin = OperatingSystem.IsWindows()
            ? CodePagesEncoding(System.Globalization.CultureInfo.CurrentCulture.TextInfo.ANSICodePage)
            : Encoding.Latin1;
        string m3u = WriteM3u("Old.m3u", "../Music/Swing/Café Society.mp3\r\n", latin);

        Assert.Equal([song], PlaylistFile.Read(m3u));
    }

    private static Encoding CodePagesEncoding(int codePage)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(codePage);
    }

    [Fact]
    public void Each_m3u_becomes_a_playlist_with_the_songs_gzjam_has_and_the_rest_are_counted()
    {
        string a = _lib.AddMp3("Swing/a.mp3");
        string b = _lib.AddMp3("Swing/b.mp3");
        _lib.AddRootAndScan();
        string elsewhere = Path.Combine(_lib.Directory, "Elsewhere", "lost.mp3");
        string friday = WriteM3u("Friday.m3u", $"{b}\n{elsewhere}\n{a}\n");
        string saturday = WriteM3u("Saturday.m3u8", $"{(OperatingSystem.IsWindows() ? a.ToUpperInvariant() : a)}\n");
        string empty = WriteM3u("Empty.m3u", "#EXTM3U\n");

        PlaylistImport plan = PlaylistImport.Plan(_lib.Database, [friday, saturday, empty]);
        Assert.Equal([("Friday", 2), ("Saturday", 1)], plan.Playlists);
        Assert.Equal(1, plan.MissingSongs);
        Assert.Equal([Path.GetDirectoryName(elsewhere)!], plan.MissingFolders);
        Assert.Equal(["Empty.m3u"], plan.NothingToImport);

        Assert.Equal(2, plan.Apply(_lib.Database));
        PlaylistInfo made = _lib.Database.GetPlaylists().Single(p => p.Name == "Friday");
        Assert.Equal([b, a], _lib.Database.GetPlaylistEntries(made.Id).Select(e => e.Path));
        PlaylistInfo other = _lib.Database.GetPlaylists().Single(p => p.Name == "Saturday");
        Assert.Equal([a], _lib.Database.GetPlaylistEntries(other.Id).Select(e => e.Path));   // as GZJam spells it

        // The same files again: nothing twice.
        PlaylistImport again = PlaylistImport.Plan(_lib.Database, [friday, saturday]);
        Assert.Empty(again.Playlists);
        Assert.Equal(["Friday", "Saturday"], again.AlreadyThere);
    }

    [Fact]
    public void A_playlist_from_another_computer_finds_its_songs_by_file_and_folder_name_and_where_the_music_moved()
    {
        string a = _lib.AddMp3("Swing/A.mp3");
        string top = _lib.AddMp3("top.mp3");
        _lib.AddMp3("Blues/top.mp3");              // the same name elsewhere: found only by the move
        _lib.AddMp3("Lindy/dup.mp3");
        _lib.AddMp3("More/Lindy/dup.mp3");         // two equally good: neither
        _lib.AddMp3("Jazz/solo.mp3");              // the file name alone isn't enough
        _lib.AddRootAndScan();
        string old = WriteM3u("2008.m3u",
            "X:\\Old PC\\Tunes\\swing\\a.mp3\r\n" +
            "X:\\Old PC\\Tunes\\top.mp3\r\n" +
            "Y:\\Else\\Lindy\\dup.mp3\r\n" +
            "Z:\\solo.mp3\r\n" +
            "X:\\Old PC\\Tunes\\Swing\\A.mp3\r\n");

        PlaylistImport plan = PlaylistImport.Plan(_lib.Database, [old]);

        Assert.Equal([("2008", 3)], plan.Playlists);
        Assert.Equal(3, plan.FoundElsewhere);
        Assert.Equal(2, plan.MissingSongs);
        (string was, string now) = Assert.Single(plan.MovedFolders);
        Assert.EndsWith("Swing", was, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(Path.GetDirectoryName(a), now);

        plan.Apply(_lib.Database);
        PlaylistInfo made = _lib.Database.GetPlaylists().Single();
        Assert.Equal([a, top, a], _lib.Database.GetPlaylistEntries(made.Id).Select(e => e.Path));
        Assert.True(File.Exists(old));   // only read
    }

    [Fact]
    public void Once_songs_agree_where_a_folder_went_the_rest_of_it_is_looked_for_only_there()
    {
        string a = _lib.AddMp3("Swing/a.mp3");
        string b = _lib.AddMp3("Swing/b.mp3");
        string c = _lib.AddMp3("Archive/Blues/c.mp3");   // reorganized on the way
        string d = _lib.AddMp3("Archive/Blues/d.mp3");
        _lib.AddMp3("Ellington/Live/CD1/01.mp3");      // another album's, ending the same way
        _lib.AddRootAndScan();
        string old = WriteM3u("Old.m3u",
            "X:\\Old\\Music\\Swing\\a.mp3\n" +
            "X:\\Old\\Music\\Blues\\c.mp3\n" +
            "X:\\Old\\Music\\Basie\\Live\\CD1\\01.mp3\n" +   // deleted since
            "X:\\Old\\Music\\Swing\\b.mp3\n" +
            "X:\\Old\\Music\\Blues\\d.mp3\n");

        PlaylistImport plan = PlaylistImport.Plan(_lib.Database, [old]);

        Assert.Equal(4, plan.FoundElsewhere);
        Assert.Equal(1, plan.MissingSongs);
        Assert.Equal([Path.GetDirectoryName(a), Path.GetDirectoryName(c)], plan.MovedFolders.Select(m => m.Now));
        plan.Apply(_lib.Database);
        Assert.Equal([a, c, b, d], _lib.Database.GetPlaylistEntries(_lib.Database.GetPlaylists().Single().Id).Select(e => e.Path));
    }

    // ───────────── Ratings in the files ─────────────

    [Theory]
    [InlineData(0, null)]
    [InlineData(1, 1)]      // Windows Media Player / MediaMonkey steps
    [InlineData(64, 2)]
    [InlineData(128, 3)]
    [InlineData(196, 4)]
    [InlineData(255, 5)]
    [InlineData(51, 2)]     // evenly spread: 51 / 102 / 153 / 204 / 255
    [InlineData(153, 3)]
    [InlineData(204, 4)]
    public void Popm_ratings_become_stars(int popm, int? stars) => Assert.Equal(stars, FileRatings.StarsFromPopm((byte)popm));

    [Theory]
    [InlineData("RATING", "5", 5)]
    [InlineData("RATING", "1", 1)]
    [InlineData("RATING", "80", 4)]       // MediaMonkey: 0-100
    [InlineData("RATING", "20", 1)]
    [InlineData("RATING", "0.6", 3)]      // 0.0-1.0
    [InlineData("RATING", "1.0", 5)]
    [InlineData("rating", "4", 4)]
    [InlineData("FMPS_RATING", "0.8", 4)]
    [InlineData("FMPS_Rating", "1", 5)]
    [InlineData("RATING", "0", null)]
    [InlineData("RATING", "great", null)]
    [InlineData("RATING", "500", null)]
    [InlineData("MOOD", "5", null)]
    public void Ratings_written_as_text_become_stars(string field, string text, int? stars) =>
        Assert.Equal(stars, FileRatings.StarsFromText(field, text));

    private static void Popm(string path, byte rating)
    {
        using var file = TagLib.File.Create(path);
        var id3 = (TagLib.Id3v2.Tag)file.GetTag(TagLib.TagTypes.Id3v2, create: true);
        TagLib.Id3v2.PopularimeterFrame.Get(id3, "Windows Media Player 9 Series", true)!.Rating = rating;
        file.Save();
    }

    private static void Txxx(string path, string description, string text)
    {
        using var file = TagLib.File.Create(path);
        var id3 = (TagLib.Id3v2.Tag)file.GetTag(TagLib.TagTypes.Id3v2, create: true);
        TagLib.Id3v2.UserTextInformationFrame.Get(id3, description, true)!.Text = [text];
        file.Save();
    }

    [Fact]
    public void Ratings_in_mp3_and_flac_files_give_unrated_songs_their_hearts()
    {
        string five = _lib.AddMp3("Swing/five.mp3", new GZJam.Testing.TestMp3.Tags(Title: "Five"));
        string one = _lib.AddMp3("Swing/one.mp3", new GZJam.Testing.TestMp3.Tags(Title: "One"));
        string three = _lib.AddMp3("Swing/three.mp3", new GZJam.Testing.TestMp3.Tags(Title: "Three"));
        string txxx = _lib.AddMp3("Swing/txxx.mp3", new GZJam.Testing.TestMp3.Tags(Title: "Txxx"));
        string mine = _lib.AddMp3("Swing/mine.mp3", new GZJam.Testing.TestMp3.Tags(Title: "Mine"));
        string none = _lib.AddMp3("Swing/none.mp3", new GZJam.Testing.TestMp3.Tags(Title: "None"));
        string flac = Path.Combine(_lib.MusicPath, "Blues", "tone.flac");
        Directory.CreateDirectory(Path.GetDirectoryName(flac)!);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Formats", "tone.flac"), flac);
        Popm(five, 255);
        Popm(one, 1);
        Popm(three, 128);
        Txxx(txxx, "RATING", "4");
        Popm(mine, 255);
        using (var file = TagLib.File.Create(flac))
        {
            ((TagLib.Ogg.XiphComment)file.GetTag(TagLib.TagTypes.Xiph, create: true)).SetField("RATING", "20");   // MediaMonkey's 1 star
            file.Save();
        }
        _lib.AddRootAndScan();
        _lib.Database.SetRating([mine], Rating.Rejected);   // rated in GZJam: stands
        DateTime written = File.GetLastWriteTimeUtc(five);

        var progress = new List<(int, int)>();
        FileRatings plan = FileRatings.Plan(_lib.Database, new SyncProgress<(int, int)>(progress.Add));
        Assert.Equal(6, plan.SongsRead);   // the unrated ones
        Assert.Equal(2, plan.Loved);
        Assert.Equal(2, plan.Rejected);
        Assert.Equal(1, plan.ThreeStars);
        Assert.Equal((6, 6), progress[^1]);

        Assert.Equal(4, plan.Apply(_lib.Database));
        Assert.Equal(Rating.Loved, Find(five).Rating);
        Assert.Equal(Rating.Loved, Find(txxx).Rating);
        Assert.Equal(Rating.Rejected, Find(one).Rating);
        Assert.Equal(Rating.Rejected, Find(flac).Rating);
        Assert.Equal(Rating.Unrated, Find(three).Rating);
        Assert.Equal(Rating.Unrated, Find(none).Rating);
        Assert.Equal(Rating.Rejected, Find(mine).Rating);

        // Only read: the files are as they were.
        Assert.Equal(written, File.GetLastWriteTimeUtc(five));
        Assert.Equal(0, FileRatings.Plan(_lib.Database).Count);   // nothing new the second time
    }

    /// <summary>Progress that reports straight away, on the thread reporting (Progress&lt;T&gt; posts later).</summary>
    private sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
    {
        private readonly Lock _lock = new();

        public void Report(T value)
        {
            lock (_lock)
                report(value);
        }
    }
}
