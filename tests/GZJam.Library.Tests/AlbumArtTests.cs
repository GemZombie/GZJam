using GZJam.Testing;

namespace GZJam.Library.Tests;

/// <summary>Finding a song's album art: in the file, or in its folder.</summary>
public sealed class AlbumArtTests : IDisposable
{
    private readonly TempLibrary _lib = new();

    public void Dispose() => _lib.Dispose();

    [Theory]
    [InlineData("folder.jpg|cover.jpg|back.jpg", "cover.jpg")]
    [InlineData("Folder.JPG|front.png", "Folder.JPG")]
    [InlineData("front.png|scan.jpg", "front.png")]
    [InlineData("Album - Front.jpg|Album - Back.jpg", "Album - Front.jpg")]
    [InlineData("scan.jpg|01 Song.mp3|notes.txt", "scan.jpg")]   // the only image
    [InlineData("scan.jpg|scan2.jpg", null)]                    // which? neither
    [InlineData("back.jpg|cd.jpg|AlbumArtSmall.jpg|inlay.jpg", "AlbumArtSmall.jpg")]   // Windows Media Player's
    [InlineData("back.jpg|booklet.jpg|picture.webp", "picture.webp")]
    [InlineData("01 Song.mp3", null)]
    public void The_front_cover_is_chosen_from_a_folders_images(string files, string? expected) =>
        Assert.Equal(expected, AlbumArtFinder.ChooseFolderImage(files.Split('|')));

    [Fact]
    public void The_picture_in_the_file_comes_before_the_folders()
    {
        string a = _lib.AddMp3("Album/a.mp3", new TestMp3.Tags(Title: "A"));
        string b = _lib.AddMp3("Album/b.mp3", new TestMp3.Tags(Title: "B"));
        string c = _lib.AddMp3("Bare/c.mp3", new TestMp3.Tags(Title: "C"));
        File.WriteAllBytes(Path.Combine(_lib.MusicPath, "Album", "cover.jpg"), [1, 2, 3]);
        TestMp3.EmbedPicture(a, [9, 9, 9]);
        byte[] before = File.ReadAllBytes(b);

        Assert.Equal(new byte[] { 9, 9, 9 }, AlbumArtFinder.Find(a)!.Data);
        Assert.False(AlbumArtFinder.Find(a)!.FromFolder);
        Assert.Equal(new byte[] { 1, 2, 3 }, AlbumArtFinder.Find(b)!.Data);
        Assert.True(AlbumArtFinder.Find(b)!.FromFolder);
        Assert.Null(AlbumArtFinder.Find(c));
        Assert.Equal(before, File.ReadAllBytes(b));   // only read
    }

    [Fact]
    public void A_front_cover_wins_over_other_pictures_in_the_file()
    {
        string a = _lib.AddMp3("a.mp3", new TestMp3.Tags(Title: "A"));
        TestMp3.EmbedPicture(a, [1], TagLib.PictureType.BackCover);
        TestMp3.EmbedPicture(a, [2], TagLib.PictureType.FrontCover);
        Assert.Equal(new byte[] { 2 }, AlbumArtFinder.Find(a)!.Data);
    }

    [Fact]
    public void A_saved_picture_is_only_used_while_the_file_and_its_folder_are_unchanged()
    {
        string a = _lib.AddMp3("a.mp3", new TestMp3.Tags(Title: "A"));
        var version = FileVersion.Of(a)!.Value;

        _lib.Database.SaveArt(a, version, folderModifiedTicks: 10, new StoredArt("abc"));
        Assert.Equal(new StoredArt("abc"), _lib.Database.GetArt(a, version, 10));
        Assert.Null(_lib.Database.GetArt(a, version, 11));   // something added to the folder
        Assert.Null(_lib.Database.GetArt(a, version with { Size = version.Size + 1 }, 10));

        _lib.Database.SaveArt(a, version, 11, new StoredArt(null));   // looked: none
        Assert.Equal(new StoredArt(null), _lib.Database.GetArt(a, version, 11));
    }
}
