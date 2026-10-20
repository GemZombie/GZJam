using GZJam.Testing;

namespace GZJam.Library.Tests;

/// <summary>A throwaway music folder plus library database, deleted after the test.</summary>
public sealed class TempLibrary : IDisposable
{
    public TempLibrary()
    {
        Directory = System.IO.Directory.CreateTempSubdirectory("gzjam-test-").FullName;
        MusicPath = Path.Combine(Directory, "Music");
        System.IO.Directory.CreateDirectory(MusicPath);
        Database = new LibraryDatabase(Path.Combine(Directory, "library.db"));
        Scanner = new LibraryScanner(Database);
    }

    public string Directory { get; }
    public string MusicPath { get; }
    public LibraryDatabase Database { get; }
    public LibraryScanner Scanner { get; }

    /// <summary>Writes an MP3 at a path relative to the music folder ('/'-separated).</summary>
    public string AddMp3(string relativePath, TestMp3.Tags? tags = null)
    {
        string path = Path.Combine(MusicPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
        TestMp3.Write(path, tags);
        return path;
    }

    public LibraryRoot AddRootAndScan()
    {
        LibraryRoot root = Database.AddRoot(MusicPath);
        Scanner.Scan(root);
        return root;
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { System.IO.Directory.Delete(Directory, recursive: true); }
        catch (IOException) { /* best effort */ }
    }
}
