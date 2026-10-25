namespace GZJam.App;

/// <summary>
/// Starting over with an empty library (Settings › Library). Asked for as GZJam closes, done as
/// it starts again, before the library is opened: the old library and its album art copies are
/// moved into "previous libraries" beside it, never deleted, and GZJam starts with no music
/// folders. Settings stay. The music files themselves are never touched.
/// </summary>
internal static class LibraryReset
{
    private const string Marker = "start-over";

    /// <summary>Where old libraries are kept, under the data folder.</summary>
    public const string PreviousLibraries = "previous libraries";

    /// <summary>Asks the next start to begin with an empty library.</summary>
    public static void Request(string dataDirectory) =>
        File.WriteAllText(Path.Combine(dataDirectory, Marker), DateTime.Now.ToString("O"));

    /// <summary>
    /// If starting over was asked for, moves the old library aside and returns the folder it went
    /// to; otherwise null.
    /// </summary>
    public static string? ApplyPending(string databasePath)
    {
        string dataDirectory = Path.GetDirectoryName(Path.GetFullPath(databasePath))!;
        string marker = Path.Combine(dataDirectory, Marker);
        if (!File.Exists(marker))
            return null;

        string kept = Path.Combine(dataDirectory, PreviousLibraries, $"library {DateTime.Now:yyyy-MM-dd HHmmss}");
        Directory.CreateDirectory(kept);
        // The GZJam that asked may still be closing: give it a few seconds to let go of the files.
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                foreach (string file in new[] { databasePath, databasePath + "-wal", databasePath + "-shm" })
                {
                    if (File.Exists(file))
                        File.Move(file, Path.Combine(kept, Path.GetFileName(file)));
                }
                // The queue kept from last time is the old library's too.
                string night = Path.Combine(dataDirectory, NightState.FileName);
                if (File.Exists(night))
                    File.Move(night, Path.Combine(kept, NightState.FileName));
                string art = Path.Combine(dataDirectory, "art");
                if (Directory.Exists(art))
                    Directory.Move(art, Path.Combine(kept, "art"));
                break;
            }
            catch (IOException) when (attempt < 20)
            {
                Thread.Sleep(250);
            }
        }
        File.Delete(marker);
        return kept;
    }
}
