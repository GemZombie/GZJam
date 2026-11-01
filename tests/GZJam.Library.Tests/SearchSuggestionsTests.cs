using GZJam.Testing;

namespace GZJam.Library.Tests;

/// <summary>What the search box suggests as the DJ types (#241).</summary>
public sealed class SearchSuggestionsTests
{
    private static readonly Dictionary<string, IReadOnlyList<ValueCount>> Library = new()
    {
        ["label"] = [new("Balboa", 412), new("Female vocals", 518), new("Lindy", 2104), new("Lindy Hop classics", 9)],
        ["artist"] = [new("Count Basie", 214), new("Benny Goodman", 180), new("Basie All Stars", 3)],
        ["genre"] = [new("Swing", 900), new("Blues", 230)],
    };

    private static IReadOnlyList<ValueCount> Values(string field) => Library.GetValueOrDefault(field) ?? [];

    private static SearchSuggestionList? At(string text, int? caret = null) => SearchSuggestions.For(text, caret ?? text.Length, Values);

    private static string[] Texts(string text) => At(text)?.Items.Select(i => i.Text).ToArray() ?? [];

    [Theory]
    [InlineData("ar", "artist:")]
    [InlineData("jump al", "album:")]
    [InlineData("fi", "file:")]
    [InlineData("fo", "folder:")]
    [InlineData("LA", "label:")]
    [InlineData("-pl", "played:")]
    public void Filter_names_are_offered_from_two_letters(string typed, string offered) =>
        Assert.Equal([offered], Texts(typed));

    [Theory]
    [InlineData("a")]          // one letter: nearly every word would pop the list up
    [InlineData("basie")]      // no filter starts with it
    [InlineData("artist:basie ")]   // a space after: nothing being typed
    [InlineData("\"duke ell")]      // a phrase
    [InlineData("")]
    [InlineData("-")]
    public void Nothing_is_offered_for(string typed) => Assert.Null(At(typed));

    [Fact]
    public void Only_at_the_end_of_a_word()
    {
        Assert.Null(At("artist basie", caret: 2));
        Assert.NotNull(At("ar basie", caret: 2));
    }

    [Fact]
    public void After_label_come_the_labels_with_their_counts()
    {
        var list = At("label:")!;
        Assert.Equal(["Balboa", "Female vocals", "Lindy", "Lindy Hop classics", "none"], list.Items.Select(i => i.Text));
        Assert.Equal(412, list.Items[0].Count);
        Assert.Equal("label:\"Female vocals\" ", list.Items[1].Insert);
    }

    [Fact]
    public void Values_that_start_with_the_typing_come_before_those_with_a_later_word_that_does()
    {
        Assert.Equal(["Basie All Stars", "Count Basie"], Texts("artist:bas"));
        Assert.Equal(["Female vocals"], Texts("label:voc"));
        Assert.Equal(["Lindy Hop classics"], Texts("label:lindy"));   // typed in full: Lindy itself isn't offered
    }

    [Fact]
    public void Choices_in_words_for_energy_rating_and_played()
    {
        Assert.Equal(["high", "medium", "low", "none"], Texts("energy:"));
        Assert.Equal(["no", "none"], Texts("rating:n"));
        Assert.Equal("-played:7 ", At("-played:")!.Items[0].Insert);
    }

    [Fact]
    public void A_bpm_or_year_gets_a_hint_not_a_choice()
    {
        var hint = Assert.Single(At("bpm:")!.Items);
        Assert.Null(hint.Insert);
        Assert.Null(At("bpm:14"));
        Assert.Contains("near", Assert.Single(At("~bpm:")!.Items).Text);
        Assert.Equal(["~bpm:"], Texts("~b"));
    }

    [Fact]
    public void Choosing_replaces_the_word_being_typed()
    {
        string text = "jump ar swing";
        var list = SearchSuggestions.For(text, 7, Values)!;
        Assert.Equal(("jump artist: swing", 12), SearchSuggestions.Apply(text, list, list.Items[0]));

        text = "basie label:fem";
        list = At(text)!;
        Assert.Equal(("basie label:\"Female vocals\" ", 28), SearchSuggestions.Apply(text, list, list.Items[0]));
    }

    [Fact]
    public void A_quoted_value_still_being_typed_gets_suggestions()
    {
        Assert.Equal(["Female vocals"], Texts("label:\"fem"));
        Assert.Empty(Texts("label:\"Female vocals\""));
    }

    [Fact]
    public void The_library_gives_its_values_and_decades()
    {
        using var lib = new TempLibrary();
        lib.AddMp3("a.mp3", new TestMp3.Tags("A", "Count Basie; Joe Williams", Genre: "Swing", Year: 1938));
        lib.AddMp3("b.mp3", new TestMp3.Tags("B", "Count Basie", Genre: "Swing", Year: 1955));
        lib.AddMp3("c.mp3", new TestMp3.Tags("C", "Chick Webb", Year: 1934));
        lib.AddRootAndScan();

        Assert.Equal([new("Count Basie", 2), new("Chick Webb", 1), new("Joe Williams", 1)], lib.Database.GetFieldValues("artist"));
        Assert.Equal([new("Swing", 2)], lib.Database.GetFieldValues("genre"));
        Assert.Equal([new("1930", 2), new("1950", 1)], lib.Database.GetDecades());
        Assert.Throws<ArgumentException>(() => lib.Database.GetFieldValues("path; DROP TABLE tracks"));
    }
}
