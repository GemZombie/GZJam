using GZJam.Testing;

namespace GZJam.Library.Tests;

/// <summary>Labels (crates): many per song, kept by path, searchable with label:.</summary>
public sealed class LabelsTests : IDisposable
{
    private readonly TempLibrary _lib = new();
    private readonly string _woodside, _splanky, _tain;

    public LabelsTests()
    {
        _woodside = _lib.AddMp3("Swing/woodside.mp3", new TestMp3.Tags(Title: "Jumpin' at the Woodside"));
        _splanky = _lib.AddMp3("Swing/splanky.mp3", new TestMp3.Tags(Title: "Splanky"));
        _tain = _lib.AddMp3("Swing/tain.mp3", new TestMp3.Tags(Title: "Tain't What You Do"));
        _lib.AddRootAndScan();
    }

    public void Dispose() => _lib.Dispose();

    private string[] Titles(string search) => _lib.Database.Search(search).Select(t => t.Title!).Order().ToArray();

    [Fact]
    public void Songs_carry_several_labels_and_the_library_shows_them()
    {
        var db = _lib.Database;
        db.AddLabel([_woodside, _splanky], "Lindy");
        db.AddLabel([_tain], "Female vocals");
        db.AddLabel([_woodside], "Balboa");

        Track woodside = db.Search("woodside").Single();
        Assert.Equal(["Balboa", "Lindy"], woodside.Labels);
        Assert.Equal(["Balboa", "Female vocals", "Lindy"], db.GetLabels().Select(l => l.Name));
        Assert.Equal(2, db.GetLabels().Single(l => l.Name == "Lindy").Count);
    }

    [Fact]
    public void A_labels_first_spelling_wins_and_it_goes_when_no_song_has_it()
    {
        var db = _lib.Database;
        Assert.Equal("Lindy", db.AddLabel([_woodside], "Lindy"));
        Assert.Equal("Lindy", db.AddLabel([_splanky], " lindy "));   // the same label
        Assert.Single(db.GetLabels());

        db.RemoveLabel([_woodside], "Lindy");
        Assert.Equal(1, db.GetLabels().Single().Count);
        db.RemoveLabel([_splanky], "lindy");
        Assert.Empty(db.GetLabels());
    }

    [Fact]
    public void Label_filters_find_songs_by_label()
    {
        var db = _lib.Database;
        db.AddLabel([_woodside, _splanky], "Lindy");
        db.AddLabel([_tain], "Female vocals");

        Assert.Equal(["Jumpin' at the Woodside", "Splanky"], Titles("label:lindy"));
        Assert.Equal(["Jumpin' at the Woodside", "Splanky"], Titles("label:LIN"));   // a prefix, any case
        Assert.Equal(["Tain't What You Do"], Titles("label:\"female vocals\""));
        Assert.Equal(["Tain't What You Do"], Titles("-label:lindy"));
        Assert.Equal(["Splanky"], Titles("label:lindy splanky"));
        Assert.Empty(Titles("label:none"));

        db.RemoveLabel([_splanky], "Lindy");
        Assert.Equal(["Splanky"], Titles("label:none"));
        Assert.Equal(3, Titles("label:").Length);   // half typed: ignored
        Assert.Empty(Titles("label:100%"));          // no LIKE wildcards from what's typed
    }
}
