using GZJam.Testing;

namespace GZJam.Library.Tests;

/// <summary>Best matches first, phrases in quotes, and file names (#240).</summary>
public sealed class SearchRankingTests : IDisposable
{
    private readonly TempLibrary _lib = new();

    public SearchRankingTests()
    {
        // In folder order: the comment-only song first, the Ellington song last.
        _lib.AddMp3("A/Sideman.mp3", new TestMp3.Tags("Sideman Stomp", "Barney Bigard", Comment: "Worked with Duke Ellington"));
        _lib.AddMp3("B/Robillard.mp3", new TestMp3.Tags("Ellington Tribute", "Duke Robillard"));
        _lib.AddMp3("C/Ellington Covers/Mood.mp3", new TestMp3.Tags("Mood Indigo", "Someone Else"));
        _lib.AddMp3("D/Caravan 1937 take 2.mp3", new TestMp3.Tags("Caravan", "Duke Ellington"));
        _lib.AddRootAndScan();
    }

    public void Dispose() => _lib.Dispose();

    /// <summary>In the order the search gives them, not sorted.</summary>
    private string[] Titles(string? text) => _lib.Database.Search(text).Select(t => t.DisplayTitle).ToArray();

    [Fact]
    public void Title_or_artist_matches_come_first_and_comment_only_matches_last()
    {
        Assert.Equal(["Ellington Tribute", "Caravan", "Mood Indigo", "Sideman Stomp"], Titles("ellington"));
    }

    [Fact]
    public void Words_split_across_title_and_artist_still_rank_first()
    {
        // Duke in the artist, Ellington in the title: still the song's name, ahead of the comment.
        Assert.Equal(["Ellington Tribute", "Caravan", "Sideman Stomp"], Titles("duke ellington"));
    }

    [Fact]
    public void Without_words_the_list_stays_in_folder_order()
    {
        string[] folderOrder = ["Sideman Stomp", "Ellington Tribute", "Mood Indigo", "Caravan"];
        Assert.Equal(folderOrder, Titles(null));
        Assert.Equal(folderOrder, Titles("-zzz"));   // only an exclusion: no words to rank by
    }

    [Fact]
    public void Words_in_quotes_match_as_a_phrase()
    {
        // Plain words: Duke and Ellington anywhere in the song. A phrase: together, in one field.
        Assert.Equal(["Ellington Tribute", "Caravan", "Sideman Stomp"], Titles("duke ellington"));
        Assert.Equal(["Caravan", "Sideman Stomp"], Titles("\"duke ellington\""));
        Assert.Equal(["Caravan", "Sideman Stomp"], Titles("\"duke ellin"));   // still typing: the last word is a prefix
        Assert.Equal(["Ellington Tribute"], Titles("duke ellington -\"duke ellington\""));
    }

    [Fact]
    public void A_field_filter_in_quotes_is_a_phrase_too()
    {
        Assert.Equal(["Caravan"], Titles("artist:\"duke ellington\""));
        Assert.Empty(Titles("artist:\"ellington duke\""));
    }

    [Fact]
    public void File_names_are_searched_and_have_a_filter_of_their_own()
    {
        Assert.Equal(["Caravan"], Titles("1937 take"));
        Assert.Equal(["Caravan"], Titles("file:\"take 2\""));
        Assert.Empty(Titles("file:indigo"));   // the title, not the file name
        Assert.Equal(["Sideman Stomp", "Ellington Tribute", "Mood Indigo"], Titles("-file:caravan"));   // no words: folder order
    }

    [Theory]
    [InlineData("\"")]
    [InlineData("\"\"")]
    [InlineData("-\"")]
    [InlineData("\"take: 2\"")]
    [InlineData("file:\"")]
    [InlineData("\"a*\" OR")]
    public void Odd_quotes_never_break_search(string input)
    {
        var ex = Record.Exception(() => _lib.Database.Search(input));
        Assert.Null(ex);
    }
}
