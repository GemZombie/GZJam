using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;

namespace GZJam.App;

/// <summary>
/// One GZJam at a time on a library (#336): a song double-clicked in the file manager, with GZJam
/// already open, goes to the GZJam that's open, not a second one on the same library and sound
/// devices. Held per data folder, so a test copy with its own (GZJAM_DATA_DIR) runs beside the
/// real one. A second GZJam started hands what it was given (songs, or nothing: then the open one
/// just comes forward) to the first over a local pipe, and stops.
/// </summary>
internal static class SingleInstance
{
    /// <summary>Started by GZJam itself to take over once it has closed (a backup restored, starting over): it waits for the lock.</summary>
    public const string AfterCloseArgument = "--after-close";

    private static Mutex? s_held;
    private static string? s_pipe;

    /// <summary>
    /// What a second GZJam handed over: its arguments (songs' paths, or none). Raised on a worker
    /// thread; queued until something listens.
    /// </summary>
    public static event Action<IReadOnlyList<string>>? Received;

    private static readonly List<IReadOnlyList<string>> s_unheard = [];

    /// <summary>The songs among a GZJam's arguments: what isn't one of its own switches.</summary>
    public static IReadOnlyList<string> Files(IEnumerable<string> args) =>
        args.Where(a => !a.StartsWith("--", StringComparison.Ordinal) && !a.StartsWith("-psn_", StringComparison.Ordinal)).ToList();

    /// <summary>
    /// Takes the lock for this data folder, or hands <paramref name="args"/> to the GZJam that has
    /// it. True: this GZJam runs. False: another one has it, and has what this one was given.
    /// </summary>
    public static bool Claim(string dataFolder, string[] args)
    {
        string name = NameFor(dataFolder);
        s_pipe = name;
        var mutex = new Mutex(initiallyOwned: false, OperatingSystem.IsWindows() ? @"Local\" + name : name);
        bool waitForIt = args.Contains(AfterCloseArgument);
        try
        {
            if (!mutex.WaitOne(waitForIt ? TimeSpan.FromSeconds(30) : TimeSpan.Zero))
            {
                if (!waitForIt && HandOver(name, args))
                {
                    mutex.Dispose();
                    return false;
                }
                // The other one isn't answering (closing, or stuck): this one runs anyway, rather
                // than nothing opening at all.
            }
        }
        catch (AbandonedMutexException)
        {
            // The one before closed without letting go (it crashed): this one has it now.
        }
        s_held = mutex;
        _ = Task.Run(ListenAsync);
        return true;
    }

    /// <summary>Lets go of the lock as GZJam closes, so one started next doesn't wait for this process to end.</summary>
    public static void Release()
    {
        try
        {
            s_held?.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Not held by this thread (taken on another): it goes with the process.
        }
        s_held = null;
    }

    /// <summary>Hears what's handed to it, or the ones handed before anything listened.</summary>
    public static void Listen(Action<IReadOnlyList<string>> heard)
    {
        lock (s_unheard)
        {
            Received += heard;
            foreach (var args in s_unheard)
                heard(args);
            s_unheard.Clear();
        }
    }

    /// <summary>The lock's and the pipe's name for a data folder: the same folder, the same name.</summary>
    internal static string NameFor(string dataFolder)
    {
        string folder = Path.GetFullPath(dataFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
            folder = folder.ToUpperInvariant();   // folders whose names differ only in case are one there
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(folder));
        return "GZJam-" + Convert.ToHexString(hash, 0, 8);
    }

    /// <summary>Sends <paramref name="args"/> to the GZJam listening; false if none answered in time.</summary>
    internal static bool HandOver(string name, IReadOnlyList<string> args)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", name, PipeDirection.Out);
            pipe.Connect(3000);
            using var writer = new StreamWriter(pipe, new UTF8Encoding(false));
            foreach (string arg in args)
                writer.WriteLine(arg);
            writer.Flush();
            return true;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static async Task ListenAsync()
    {
        while (s_pipe is { } name)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(name, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await pipe.WaitForConnectionAsync();
                using var reader = new StreamReader(pipe, Encoding.UTF8);
                var args = new List<string>();
                while (await reader.ReadLineAsync() is { } line)
                {
                    if (line.Length > 0)
                        args.Add(line);
                }
                lock (s_unheard)
                {
                    if (Received is { } heard)
                        heard(args);
                    else
                        s_unheard.Add(args);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                await Task.Delay(500);   // a pipe that couldn't be made or read: try again
            }
        }
    }
}
