namespace GZJam.App;

/// <summary>
/// Moving or deleting a song file that another program has open for a moment. On Windows, the
/// antivirus scans a file when it's read (GZJam reads the next songs ahead of time, #346), and an
/// indexer or a backup may open one too: a move at that instant fails with "being used by another
/// process". Waiting a moment and trying again is all it needs. Never call it from the window's
/// thread: it can wait up to <see cref="MaxWait"/>.
/// </summary>
internal static class BusyFile
{
    private const int Tries = 10;
    private static readonly TimeSpan Pause = TimeSpan.FromMilliseconds(200);

    /// <summary>The longest it waits for a file to be free: <see cref="Tries"/> × <see cref="Pause"/>.</summary>
    public static TimeSpan MaxWait => Pause * Tries;

    /// <summary><see cref="File.Move(string, string)"/>, trying again while the file is in use.</summary>
    public static void Move(string from, string to) => Retry(() => File.Move(from, to));

    /// <summary><see cref="File.Delete"/>, trying again while the file is in use.</summary>
    public static void Delete(string path) => Retry(() => File.Delete(path));

    private static void Retry(Action action)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                action();
                return;
            }
            catch (IOException e) when (attempt < Tries && InUse(e))
            {
                Thread.Sleep(Pause);
            }
        }
    }

    /// <summary>Windows' "being used by another process" (sharing violation) and "locked" (lock violation).</summary>
    internal static bool InUse(IOException e) => (e.HResult & 0xFFFF) is 32 or 33;
}
