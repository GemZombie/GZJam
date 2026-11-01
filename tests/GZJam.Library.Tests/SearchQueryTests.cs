using GZJam.Testing;

namespace GZJam.Library.Tests;

/// <summary>Search filters: bpm:, ~bpm:, year:, artist: and friends, rating:, energy:, and exclusions.</summary>
public sealed class SearchQueryTests : IDisposable
{
    private readonly TempLibrary _lib = new();

    public SearchQueryTests()
    {
        _lib.AddMp3("Lindy/Basie/Jumpin.mp3", new TestMp3.Tags("Jumpin' at the Woodside", "Count Basie", "Decca", "Swing", 1938, Bpm: "236"));
        _lib.AddMp3("Lindy/Basie/Corner.mp3", new TestMp3.Tags("Corner Pocket", "Count Basie Orchestra", "April in Paris", "Swing", 1956, Bpm: "138"));
        _lib.AddMp3("Lindy/Webb/Savoy.mp3", new TestMp3.Tags("Stompin' at the Savoy", "Chick Webb", Year: 1934, Bpm: "150", Comment: "live"));
        _lib.AddMp3("Blues/McShann/Hootie.mp3", new TestMp3.Tags("Hootie Blues", "Jay McShann", Year: 1941, Bpm: "92"));
        string detected = _lib.AddMp3("Blues/Untagged.mp3", new TestMp3.Tags("Basie Blues", "Someone Else"));
        _lib.AddRootAndScan();
        // No BPM tag: the detected one counts.
        _lib.Database.SaveAnalysis(detected, FileVersion.Of(detected)!.Value,
            new StoredAnalysis(1, TimeSpan.FromSeconds(100), TimeSpan.Zero, TimeSpan.FromSeconds(100), 145, null));

        var all = _lib.Database.Search(null).ToDictionary(t => t.Title!, t => t.Path);
        _lib.Database.SetRating([all["Corner Pocket"], all["Hootie Blues"]], Rating.Loved);
        _lib.Database.SetRating([all["Stompin' at the Savoy"]], Rating.Rejected);
        _lib.Database.SetEnergy([all["Jumpin' at the Woodside"]], Energy.High);
        _lib.Database.SetEnergy([all["Corner Pocket"]], Energy.Medium);
        _lib.Database.SetEnergy([all["Hootie Blues"]], Energy.Low);
    }

    public void Dispose() => _lib.Dispose();

    private string[] Titles(string text) => _lib.Database.Search(text).Select(t => t.Title!).Order().ToArray();

    [Theory]
    [InlineData("bpm:138", "Corner Pocket")]
    [InlineData("bpm:140-155", "Basie Blues|Stompin' at the Savoy")]   // a detected BPM counts too
    [InlineData("bpm:155-140", "Basie Blues|Stompin' at the Savoy")]   // either way round
    [InlineData("bpm:>200", "Jumpin' at the Woodside")]
    [InlineData("bpm:<100", "Hootie Blues")]
    [InlineData("~bpm:140", "Basie Blues|Corner Pocket|Stompin' at the Savoy")]   // 128.8 to 151.2
    [InlineData("~bpm:250", "Jumpin' at the Woodside")]
    [InlineData("~bpm:140/2", "Corner Pocket")]   // 137.2 to 142.8
    [InlineData("~bpm:140/12%", "Basie Blues|Corner Pocket|Stompin' at the Savoy")]
    [InlineData("~bpm:236/1", "Jumpin' at the Woodside")]
    public void Bpm_filters(string query, string expected) => Assert.Equal(expected.Split('|'), Titles(query));

    [Fact]
    public void How_near_a_bpm_reaches_is_the_djs_choice_unless_the_search_says()
    {
        string[] Near(string text, double near) => _lib.Database.Search(text, nearBpm: near).Select(t => t.Title!).Order().ToArray();

        Assert.Equal(["Corner Pocket"], Near("~bpm:140", 0.02));
        Assert.Equal(["Basie Blues", "Corner Pocket", "Stompin' at the Savoy"], Near("~bpm:140", 0.08));
        Assert.Equal(["Basie Blues", "Corner Pocket", "Stompin' at the Savoy"], Near("~bpm:140/8", 0.02));   // typed: this search only
        Assert.Equal(["Corner Pocket"], Near("~bpm:140/", 0.02));   // still typing: as near as usual
    }

    [Theory]
    [InlineData("year:1938", "Jumpin' at the Woodside")]
    [InlineData("year:1934-1940", "Jumpin' at the Woodside|Stompin' at the Savoy")]
    [InlineData("year:>=1941", "Corner Pocket|Hootie Blues")]
    public void Year_filters(string query, string expected) => Assert.Equal(expected.Split('|'), Titles(query));

    [Theory]
    [InlineData("artist:basie", "Corner Pocket|Jumpin' at the Woodside")]   // not "Basie Blues": that's the title
    [InlineData("artist:\"count basie orch\"", "Corner Pocket")]
    [InlineData("title:basie", "Basie Blues")]
    [InlineData("album:paris", "Corner Pocket")]
    [InlineData("folder:blues", "Basie Blues|Hootie Blues")]
    [InlineData("comment:live", "Stompin' at the Savoy")]
    [InlineData("artist:basie jump", "Jumpin' at the Woodside")]   // with ordinary words
    public void Field_filters(string query, string expected) => Assert.Equal(expected.Split('|'), Titles(query));

    [Theory]
    [InlineData("rating:yes", "Corner Pocket|Hootie Blues")]
    [InlineData("rating:no", "Stompin' at the Savoy")]
    [InlineData("rating:none", "Basie Blues|Jumpin' at the Woodside")]
    [InlineData("energy:high", "Jumpin' at the Woodside")]
    [InlineData("energy:2-3", "Corner Pocket|Jumpin' at the Woodside")]
    [InlineData("energy:none", "Basie Blues|Stompin' at the Savoy")]
    [InlineData("rating:yes energy:low", "Hootie Blues")]
    [InlineData("energy:<2", "Basie Blues|Hootie Blues|Stompin' at the Savoy")]   // takes in songs never marked
    [InlineData("energy:0-1", "Basie Blues|Hootie Blues|Stompin' at the Savoy")]
    [InlineData("-energy:high", "Basie Blues|Corner Pocket|Hootie Blues|Stompin' at the Savoy")]
    [InlineData("-rating:none", "Corner Pocket|Hootie Blues|Stompin' at the Savoy")]
    public void Rating_and_energy_filters(string query, string expected) => Assert.Equal(expected.Split('|'), Titles(query));

    [Theory]
    [InlineData("basie -blues", "Corner Pocket|Jumpin' at the Woodside")]
    [InlineData("-artist:basie", "Basie Blues|Hootie Blues|Stompin' at the Savoy")]
    [InlineData("-rating:no ~bpm:145", "Basie Blues|Corner Pocket")]
    [InlineData("-year:1938 bpm:>200", "")]
    [InlineData("-year:1956 artist:basie", "Jumpin' at the Woodside")]   // an unknown year isn't 1956 either
    public void A_minus_excludes(string query, string expected) =>
        Assert.Equal(expected.Split('|', StringSplitOptions.RemoveEmptyEntries), Titles(query));

    [Theory]
    [InlineData("basie OR mcshann", "Basie Blues|Corner Pocket|Hootie Blues|Jumpin' at the Woodside")]
    [InlineData("rating:no OR energy:high", "Jumpin' at the Woodside|Stompin' at the Savoy")]
    [InlineData("bpm:<100 OR bpm:>200", "Hootie Blues|Jumpin' at the Woodside")]
    [InlineData("artist:webb OR year:1956", "Corner Pocket|Stompin' at the Savoy")]
    [InlineData("blues rating:yes OR energy:high", "Hootie Blues")]   // blues, and either: OR binds tighter
    [InlineData("hootie OR energy:high OR year:1934", "Hootie Blues|Jumpin' at the Woodside|Stompin' at the Savoy")]
    [InlineData("-rating:no OR energy:low", "Basie Blues|Corner Pocket|Hootie Blues|Jumpin' at the Woodside")]
    [InlineData("savoy OR -live", "Basie Blues|Corner Pocket|Hootie Blues|Jumpin' at the Woodside|Stompin' at the Savoy")]
    public void Or_takes_either(string query, string expected) => Assert.Equal(expected.Split('|'), Titles(query));

    [Theory]
    [InlineData("basie OR")]          // still typing
    [InlineData("OR basie")]
    [InlineData("basie OR OR")]
    [InlineData("basie OR bpm:")]     // the other side half typed
    [InlineData("basie OR \"")]
    public void An_or_with_nothing_on_one_side_is_ignored(string query) =>
        Assert.Equal(["Basie Blues", "Corner Pocket", "Jumpin' at the Woodside"], Titles(query));

    [Fact]
    public void A_lowercase_or_is_a_word()
    {
        Assert.Equal(["Corner Pocket"], Titles("basie or"));   // "or" starts Orchestra
        Assert.Equal(["Corner Pocket"], Titles("basie \"OR\""));
    }

    [Theory]
    [InlineData("bpm:")]           // half typed
    [InlineData("bpm:1")]          // still typing 140: nothing at 1 BPM
    [InlineData("rating:")]
    [InlineData("~bpm:abc")]
    [InlineData("~bpm:140/")]
    [InlineData("~bpm:140/x")]
    [InlineData("~bpm:/5")]
    [InlineData("artist:\"count")]
    [InlineData("year:-")]
    [InlineData("-")]
    [InlineData("energy:>")]
    public void Unfinished_filters_never_break_search(string query)
    {
        var ex = Record.Exception(() => _lib.Database.Search(query));
        Assert.Null(ex);
    }

    [Fact]
    public void A_half_typed_filter_is_ignored_rather_than_emptying_the_list()
    {
        Assert.Equal(5, Titles("bpm:").Length);
        Assert.Equal(3, Titles("basie rating:").Length);   // "basie" still applies
    }

    [Fact]
    public void Unknown_fields_are_searched_as_words()
    {
        Assert.Equal(["Corner Pocket"], Titles("pocket:corner"));
    }
}
