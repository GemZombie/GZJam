using GZJam.Testing;

namespace GZJam.Library.Tests;

/// <summary>"Did you mean...?" when a typo finds nothing (#242).</summary>
public sealed class SearchSpellingTests
{
    private static readonly IReadOnlyList<SearchWord> Words =
    [
        .. new SearchWord[]
        {
            new("basie", 40), new("blues", 300), new("caravan", 4), new("count", 41), new("duke", 60),
            new("ella", 80), new("ellington", 120), new("fitzgerald", 70), new("jump", 30), new("jumpin", 12),
            new("savoy", 9), new("stompin", 8), new("webb", 20), new("wells", 3),
        }.OrderBy(w => w.Term, StringComparer.Ordinal),
    ];

    [Theory]
    [InlineData("elington", "ellington")]
    [InlineData("duke elington", "duke ellington")]
    [InlineData("ellingotn", "ellington")]              // two letters swapped
    [InlineData("fitzgerlad", "fitzgerald")]
    [InlineData("elingt", "ellington")]                 // still typing: the word it starts
    [InlineData("artist:basei", "artist:basie")]        // in a field filter
    [InlineData("\"duke elington\"", "\"duke ellington\"")]
    [InlineData("savoyy bpm:140 -live", "savoy bpm:140 -live")]   // filters and left-out words as they were
    [InlineData("webb OR elington", "webb OR ellington")]
    [InlineData("Élington", "ellington")]
    public void A_misspelled_word_is_swapped_for_the_nearest_in_the_library(string typed, string expected) =>
        Assert.Equal(expected, SearchSpelling.Suggest(typed, Words));

    [Theory]
    [InlineData("ellington")]       // nothing wrong
    [InlineData("ellin")]           // the start of a word
    [InlineData("basie savoy")]     // both real, just not together
    [InlineData("zzzzzz")]          // nothing near
    [InlineData("wb")]              // too short to tell
    [InlineData("1938")]
    [InlineData("bpm:1400")]
    [InlineData("-elington")]       // left out: it can't be why nothing's found
    public void Nothing_is_suggested_for(string typed) => Assert.Null(SearchSpelling.Suggest(typed, Words));

    [Fact]
    public void A_short_word_gets_one_letter_off_and_a_tie_goes_to_the_word_more_songs_have()
    {
        Assert.Equal("webb", SearchSpelling.Correct("wbb", Words));
        Assert.Null(SearchSpelling.Correct("wxyb", Words));            // two off in four letters: too far
        Assert.Equal("ella", SearchSpelling.Correct("ellq", Words));   // ella (80 songs), not ellington's start
    }

    [Theory]
    [InlineData("kitten", "sitting", 3)]
    [InlineData("ab", "ba", 1)]
    [InlineData("ellington", "ellington", 0)]
    public void Distance(string a, string b, int expected) => Assert.Equal(expected, SearchSpelling.Distance(a, b, 5));

    [Fact]
    public void The_library_lists_its_words_sorted_with_how_many_songs_have_each()
    {
        using var lib = new TempLibrary();
        lib.AddMp3("Swing/a.mp3", new TestMp3.Tags("Caravan", "Duke Ellington"));
        lib.AddMp3("Swing/b.mp3", new TestMp3.Tags("Cotton Tail", "Duke Ellington"));
        lib.AddRootAndScan();

        var words = lib.Database.GetSearchWords();
        Assert.Equal(words.OrderBy(w => w.Term, StringComparer.Ordinal), words);
        Assert.Equal(2, words.Single(w => w.Term == "ellington").Songs);
        Assert.Equal(1, words.Single(w => w.Term == "caravan").Songs);
        Assert.Contains(words, w => w.Term == "swing");   // folder names too
        Assert.Equal("duke ellington", SearchSpelling.Suggest("duke elington", words));
    }
}
