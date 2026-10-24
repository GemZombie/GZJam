using Microsoft.Data.Sqlite;

namespace GZJam.Library.Tests;

/// <summary>A library a newer GZJam has upgraded: opened if it says this one can read it, else refused, untouched.</summary>
public sealed class NewerLibraryTests : IDisposable
{
    private readonly TempLibrary _lib = new();

    public void Dispose() => _lib.Dispose();

    /// <summary>A library as a newer GZJam would leave it: upgrade <paramref name="version"/>, readable from <paramref name="oldestReader"/>.</summary>
    private string NewerLibrary(long version, long? oldestReader)
    {
        string path = Path.Combine(_lib.Directory, $"newer-{version}-{oldestReader}.db");
        _ = new LibraryDatabase(path);   // as this GZJam makes it
        using (var c = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = oldestReader is { } oldest
                ? $"PRAGMA user_version = {version}; UPDATE schema_compat SET oldest_reader = {oldest}; CREATE TABLE from_the_future (x);"
                : $"PRAGMA user_version = {version}; DROP TABLE schema_compat;";
            cmd.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();
        return path;
    }

    private static long UserVersion(string path)
    {
        using var c = new SqliteConnection($"Data Source={path};Pooling=False");
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "PRAGMA user_version;";
        return (long)cmd.ExecuteScalar()!;
    }

    [Fact]
    public void One_whose_newer_upgrades_only_added_things_opens_as_it_is()
    {
        string path = NewerLibrary(version: 25, oldestReader: 19);
        Assert.Null(LibraryDatabase.CheckCanOpen(path));

        var db = new LibraryDatabase(path);
        db.AddRoot(_lib.MusicPath);
        Assert.Single(db.GetRoots());           // and it works
        SqliteConnection.ClearAllPools();
        Assert.Equal(25, UserVersion(path));    // not "upgraded" back down
    }

    [Theory]
    [InlineData(30L)]    // the newer GZJam said only it, or newer, can read it
    [InlineData(null)]   // or said nothing
    public void One_this_version_cant_read_is_refused_and_left_alone(long? oldestReader)
    {
        string path = NewerLibrary(version: 30, oldestReader);
        byte[] before = File.ReadAllBytes(path);

        var reason = LibraryDatabase.CheckCanOpen(path);
        Assert.NotNull(reason);
        Assert.Equal(30, reason.Version);
        var thrown = Assert.Throws<LibraryTooNewException>(() => new LibraryDatabase(path));
        Assert.Equal(reason.Known, thrown.Known);
        SqliteConnection.ClearAllPools();
        Assert.Equal(30, UserVersion(path));
    }

    [Fact]
    public void A_library_that_isnt_there_yet_or_is_current_can_be_opened()
    {
        Assert.Null(LibraryDatabase.CheckCanOpen(Path.Combine(_lib.Directory, "none.db")));
        Assert.Null(LibraryDatabase.CheckCanOpen(_lib.Database.DatabasePath));
    }
}
