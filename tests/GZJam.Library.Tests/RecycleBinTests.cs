using GZJam.Testing;

namespace GZJam.Library.Tests;

/// <summary>The computer's Recycle Bin, for songs deleted from Files (#385).</summary>
public sealed class RecycleBinTests : IDisposable
{
    private readonly string _work = Directory.CreateTempSubdirectory("gzjam-recycle-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_work, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void A_file_goes_to_the_real_bin_and_is_gone_from_its_folder()
    {
        // Only on CI's throwaway machines: here it would fill the DJ's own Recycle Bin.
        Assert.SkipUnless(TestMachine.IsCi, "Puts a file in the computer's real Recycle Bin: CI only.");
        string song = Path.Combine(_work, $"Reject {Guid.NewGuid():N}.mp3");
        File.WriteAllBytes(song, new byte[4096]);
        Assert.True(RecycleBin.CanRecycle(song));

        RecycleBin.Recycle(song);

        Assert.False(File.Exists(song));
    }

    [Fact]
    public void A_file_that_isnt_there_is_said_so()
    {
        Assert.Throws<FileNotFoundException>(() => RecycleBin.Recycle(Path.Combine(_work, "gone.mp3")));
    }

    [Fact]
    public void On_Linux_the_trash_keeps_where_it_was_and_when_so_it_can_be_put_back()
    {
        string trash = Path.Combine(_work, "Trash");
        string folder = Path.Combine(_work, "New music");
        Directory.CreateDirectory(folder);
        string first = Path.Combine(folder, "Sing, Sing, Sing #2.mp3");
        File.WriteAllText(first, "one");

        RecycleBin.Freedesktop.Recycle(first, trash, first.Replace('\\', '/'));

        Assert.False(File.Exists(first));
        Assert.Equal("one", File.ReadAllText(Path.Combine(trash, "files", "Sing, Sing, Sing #2.mp3")));
        string info = File.ReadAllText(Path.Combine(trash, "info", "Sing, Sing, Sing #2.mp3.trashinfo"));
        Assert.StartsWith("[Trash Info]\nPath=", info);
        Assert.Contains("Sing%2C%20Sing%2C%20Sing%20%232.mp3\n", info);   // escaped, its slashes kept
        Assert.Matches(@"DeletionDate=\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d\n", info);

        // Another of that name: a name of its own in the trash, the first one kept.
        File.WriteAllText(first, "two");
        RecycleBin.Freedesktop.Recycle(first, trash, first.Replace('\\', '/'));
        Assert.Equal("one", File.ReadAllText(Path.Combine(trash, "files", "Sing, Sing, Sing #2.mp3")));
        Assert.Equal("two", File.ReadAllText(Path.Combine(trash, "files", "Sing, Sing, Sing #2.2.mp3")));
        Assert.True(File.Exists(Path.Combine(trash, "info", "Sing, Sing, Sing #2.2.mp3.trashinfo")));
    }
}
