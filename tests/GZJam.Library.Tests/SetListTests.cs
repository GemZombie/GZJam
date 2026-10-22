using System.Globalization;

namespace GZJam.Library.Tests;

/// <summary>Set lists exported from History: readable text and CSV.</summary>
public sealed class SetListTests
{
    private static readonly CultureInfo Us = CultureInfo.GetCultureInfo("en-US");

    /// <summary>A path in this computer's style (a Windows path is one long file name elsewhere).</summary>
    private static string Song(params string[] parts) => Path.Combine([OperatingSystem.IsWindows() ? @"C:\music" : "/music", .. parts]);

    private static DateTime Local(int hour, int minute) => new DateTime(2026, 10, 17, hour, minute, 0, DateTimeKind.Local).ToUniversalTime();

    private static readonly PlayRecord[] Plays =
    [
        new(1, 7, Local(19, 42), Song("Basie", "Woodside.mp3"), "Jumpin' at the Woodside", "Count Basie", "Count Basie Story",
            240, TimeSpan.FromSeconds(190), "1", -1.5),
        new(2, 7, Local(19, 45), Song("Misc", "03 Track.mp3"), null, null, null, null, TimeSpan.FromSeconds(185), "2", 0),
        new(3, 7, Local(19, 48), Song("Webb", "Stompin.mp3"), "Stompin' at the Savoy, \"live\"", "Chick Webb", null,
            188, TimeSpan.FromSeconds(3725), "1", 2),
    ];

    [Fact]
    public void The_text_set_list_has_a_heading_and_a_line_per_song_with_the_tempo_the_room_heard()
    {
        var session = new PlaySession(7, Local(19, 40), Local(19, 48), 3, TimeSpan.Zero) { Name = "Hot Jam, Oct 14" };

        string[] lines = SetList.Text(session, Plays, Us).Split(Environment.NewLine);

        Assert.Equal("Hot Jam, Oct 14", lines[0]);
        Assert.Equal("Saturday, October 17, 2026 · 7:42 PM to 8:50 PM · 3 songs · 1:08:20", lines[1].Replace('\u202F', ' '));
        Assert.Equal("", lines[2]);
        Assert.Equal(" 1  7:42 PM  Jumpin' at the Woodside · Count Basie · 236 BPM · 3:10", lines[3].Replace('\u202F', ' '));
        Assert.Equal(" 2  7:45 PM  03 Track · 3:05", lines[4].Replace('\u202F', ' '));   // untagged: the file name, nothing else
        Assert.Equal(" 3  7:48 PM  Stompin' at the Savoy, \"live\" · Chick Webb · 192 BPM · 1:02:05", lines[5].Replace('\u202F', ' '));
    }

    [Fact]
    public void An_unnamed_session_is_headed_by_its_date()
    {
        var session = new PlaySession(7, Local(19, 40), Local(19, 48), 1, TimeSpan.Zero);
        string text = SetList.Text(session, Plays[..1], Us);
        Assert.StartsWith("Saturday, October 17, 2026" + Environment.NewLine + "7:42", text.Replace('\u202F', ' '));
        Assert.Contains("1 song · 3:10", text);
    }

    [Fact]
    public void The_csv_has_a_row_per_song_quoting_what_needs_it()
    {
        string[] rows = SetList.Csv(Plays).Split(Environment.NewLine);

        Assert.Equal("#,Date,Time,Title,Artist,Album,BPM,Tempo,Length,Deck,File", rows[0]);
        Assert.Equal("1,2026-10-17,19:42:00,Jumpin' at the Woodside,Count Basie,Count Basie Story,236.4,-1.5%,3:10,1," + Song("Basie", "Woodside.mp3"), rows[1]);
        Assert.Equal("2,2026-10-17,19:45:00,03 Track,,,,,3:05,2," + Song("Misc", "03 Track.mp3"), rows[2]);
        Assert.Equal(@"3,2026-10-17,19:48:00,""Stompin' at the Savoy, """"live"""""",Chick Webb,,191.8,+2.0%,1:02:05,1," + Song("Webb", "Stompin.mp3"), rows[3]);
    }

    [Fact]
    public void The_file_name_has_the_session_name_and_date_without_characters_files_cant_have()
    {
        var named = new PlaySession(7, Local(19, 40), null, 0, TimeSpan.Zero) { Name = "Hot Jam: Oct/14?" };
        Assert.Equal("Set list - Hot Jam- Oct-14- - 2026-10-17", SetList.FileName(named, Plays));
        Assert.Equal("Set list - 2026-10-17", SetList.FileName(named with { Name = null }, Plays));
    }
}
