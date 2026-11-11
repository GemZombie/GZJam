using GZJam.App.ViewModels;
using GZJam.Library;

namespace GZJam.App.Tests;

/// <summary>The song list's order by a column (#368), made off the window's thread: as the grid's own sort was.</summary>
public sealed class ListOrderTests
{
    private static Track Song(string title, string? artist = null, double? bpm = null, string? style = null) => new()
    {
        Path = $"/music/{title}.mp3", Folder = "", FileName = title + ".mp3", Title = title, Artist = artist, TagBpm = bpm,
        Tags = style is null ? [] : [new SongTag("STYLE", style)],
    };

    private static List<string> Titles(IReadOnlyList<Track> tracks) => tracks.Select(t => t.Title!).ToList();

    [Fact]
    public void Text_goes_as_a_person_reads_it_case_aside_and_songs_alike_keep_the_lists_own_order_both_ways()
    {
        Track[] songs =
        [
            Song("1", "basie"), Song("2", "Ellington"), Song("3", "Basie"), Song("4", "Élan"), Song("5", "Calloway"), Song("6", "basie"),
        ];

        var up = ListOrder.Sort(songs, new AppSettings.ListSort(nameof(Track.DisplayArtist), false));
        Assert.Equal(["1", "3", "6", "5", "4", "2"], Titles(up));   // É with the E's, not after Z

        var down = ListOrder.Sort(songs, new AppSettings.ListSort(nameof(Track.DisplayArtist), true));
        Assert.Equal(["2", "4", "5", "1", "3", "6"], Titles(down));   // the Basies still in the list's order
    }

    [Fact]
    public void Numbers_go_by_value_songs_without_one_first()
    {
        Track[] songs = [Song("fast", bpm: 180), Song("none"), Song("slow", bpm: 95.5), Song("mid", bpm: 140)];
        Assert.Equal(["none", "slow", "mid", "fast"], Titles(ListOrder.Sort(songs, new AppSettings.ListSort(nameof(Track.Bpm), false))));
        Assert.Equal(["fast", "mid", "slow", "none"], Titles(ListOrder.Sort(songs, new AppSettings.ListSort(nameof(Track.Bpm), true))));
    }

    [Fact]
    public void A_tags_column_goes_by_that_tag()
    {
        Track[] songs = [Song("a", style: "Swing"), Song("b"), Song("c", style: "blues"), Song("d", style: "Lindy")];
        Assert.Equal(["b", "c", "d", "a"], Titles(ListOrder.Sort(songs, new AppSettings.ListSort("tag:" + SongTags.KeyOf("STYLE"), false))));
    }

    [Fact]
    public void The_list_given_is_left_as_it_was()
    {
        Track[] songs = [Song("b"), Song("a")];
        var sorted = ListOrder.Sort(songs, new AppSettings.ListSort(nameof(Track.DisplayTitle), false));
        Assert.Equal(["a", "b"], Titles(sorted));
        Assert.Equal(["b", "a"], Titles(songs));
    }
}
