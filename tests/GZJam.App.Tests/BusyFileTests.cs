namespace GZJam.App.Tests;

/// <summary>
/// A song file another program has open for a moment (the antivirus scanning it, say) is moved
/// once it lets go, rather than the move failing (#346: reading songs ahead made this likelier).
/// </summary>
public sealed class BusyFileTests
{
    [Fact]
    public async Task A_file_open_elsewhere_for_a_moment_is_moved_once_it_lets_go()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows stops a move while a file is open.");
        string dir = Directory.CreateTempSubdirectory("gzjam-busy-").FullName;
        string from = Path.Combine(dir, "song.mp3"), to = Path.Combine(dir, "Lindy", "song.mp3");
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        File.WriteAllBytes(from, new byte[1000]);

        // Open as a scanner might: others may read it, but not move or delete it.
        var held = new FileStream(from, FileMode.Open, FileAccess.Read, FileShare.Read);
        Assert.Throws<IOException>(() => File.Move(from, to));   // as it was
        var letGo = Task.Run(async () =>
        {
            await Task.Delay(500, TestContext.Current.CancellationToken);
            held.Dispose();
        }, TestContext.Current.CancellationToken);

        BusyFile.Move(from, to);
        await letGo;
        Assert.True(File.Exists(to));
        Assert.False(File.Exists(from));
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void A_file_held_too_long_still_fails_with_why()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows stops a move while a file is open.");
        string dir = Directory.CreateTempSubdirectory("gzjam-busy-").FullName;
        string from = Path.Combine(dir, "song.mp3");
        File.WriteAllBytes(from, new byte[1000]);
        using (new FileStream(from, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var e = Assert.Throws<IOException>(() => BusyFile.Delete(from));
            Assert.True(BusyFile.InUse(e));
        }
        Directory.Delete(dir, recursive: true);
    }
}
