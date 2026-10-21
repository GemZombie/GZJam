using GZJam.Testing;

namespace GZJam.Library.Tests;

/// <summary>The DJ's rating and energy marks.</summary>
public sealed class MarksTests : IDisposable
{
    private readonly TempLibrary _lib = new();

    public void Dispose() => _lib.Dispose();

    [Fact]
    public void Ratings_and_energy_are_saved_separately_and_come_back_with_the_tracks()
    {
        string a = _lib.AddMp3("Swing/a.mp3", new TestMp3.Tags(Title: "A"));
        string b = _lib.AddMp3("Swing/b.mp3", new TestMp3.Tags(Title: "B"));
        _lib.AddMp3("Swing/c.mp3", new TestMp3.Tags(Title: "C"));
        _lib.AddRootAndScan();
        byte[] before = File.ReadAllBytes(a);

        _lib.Database.SetRating([a, b], Rating.Loved);
        _lib.Database.SetEnergy([a], Energy.High);
        _lib.Database.SetRating([b], Rating.Rejected);   // changing a rating keeps nothing else

        var tracks = _lib.Database.Search(null).ToDictionary(t => t.Title!);
        Assert.Equal((Rating.Loved, Energy.High), (tracks["A"].Rating, tracks["A"].Energy));
        Assert.Equal((Rating.Rejected, Energy.Unset), (tracks["B"].Rating, tracks["B"].Energy));
        Assert.Equal((Rating.Unrated, Energy.Unset), (tracks["C"].Rating, tracks["C"].Energy));
        Assert.Equal(before, File.ReadAllBytes(a));   // never written to the file
    }

    [Fact]
    public void Clearing_both_marks_forgets_the_file()
    {
        string a = _lib.AddMp3("Swing/a.mp3");
        _lib.Database.SetRating([a], Rating.Loved);
        _lib.Database.SetEnergy([a], Energy.Low);

        _lib.Database.SetRating([a], Rating.Unrated);
        Assert.Equal((Rating.Unrated, Energy.Low), _lib.Database.GetMarks(a));
        _lib.Database.SetEnergy([a], Energy.Unset);

        Assert.Equal((Rating.Unrated, Energy.Unset), _lib.Database.GetMarks(a));
    }

    [Fact]
    public void Marks_survive_a_rescan_after_the_file_changes()
    {
        string a = _lib.AddMp3("Swing/a.mp3", new TestMp3.Tags(Title: "Old title"));
        LibraryRoot root = _lib.AddRootAndScan();
        _lib.Database.SetRating([a], Rating.Loved);

        _lib.AddMp3("Swing/a.mp3", new TestMp3.Tags(Title: "New title"));
        File.SetLastWriteTimeUtc(a, DateTime.UtcNow.AddMinutes(5));
        _lib.Scanner.Scan(root);

        Track track = Assert.Single(_lib.Database.Search(null));
        Assert.Equal("New title", track.Title);
        Assert.Equal(Rating.Loved, track.Rating);
    }
}
