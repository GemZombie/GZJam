using System.Globalization;
using System.Text;

namespace GZJam.App;

/// <summary>
/// A plain-text activity log (one file per day, two weeks kept) so problems at a gig can be
/// traced afterward: what was pressed, what the decks did, and any errors.
/// Lives next to the library database, in a "logs" folder.
/// <para>
/// A line is only queued where it's logged (most come from the window's thread); a writer thread
/// of its own puts them in the file, so a slow disk, or an antivirus watching the folder, never
/// holds up the window (#367). An error is written at once, and so is everything on closing.
/// </para>
/// </summary>
internal static class Log
{
    /// <summary>Guards <see cref="Waiting"/>.</summary>
    private static readonly Lock Gate = new();

    /// <summary>Guards the file: one batch written at a time, in order.</summary>
    private static readonly Lock FileGate = new();

    private static readonly StringBuilder Waiting = new();
    private static readonly AutoResetEvent Wake = new(false);
    private static string? _file;
    private static Thread? _writer;

    public static string? Directory { get; private set; }

    public static void Start(string folder)
    {
        try
        {
            Directory = Path.Combine(folder, "logs");
            System.IO.Directory.CreateDirectory(Directory);
            _file = Path.Combine(Directory, $"gzjam-{DateTime.Now:yyyy-MM-dd}.log");
            foreach (var old in new DirectoryInfo(Directory).GetFiles("gzjam-*.log").Where(f => f.LastWriteTime < DateTime.Now.AddDays(-14)))
                old.Delete();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _file = null;
            return;
        }
        if (_writer is null)
        {
            _writer = new Thread(WriteLoop) { IsBackground = true, Name = "GZJam log", Priority = ThreadPriority.BelowNormal };
            _writer.Start();
            AppDomain.CurrentDomain.ProcessExit += (_, _) => Flush();
        }
    }

    /// <summary>Writes what's waiting, then stops: lines after this are dropped (tests).</summary>
    internal static void Stop()
    {
        Flush();
        _file = null;
    }

    public static void Info(string message) => Write("INFO ", message);

    public static void Error(string message, Exception? exception = null)
    {
        Write("ERROR", exception is null ? message : $"{message}: {exception}");
        Flush();   // an error may be the last thing GZJam does
    }

    private static void Write(string level, string message)
    {
        if (_file is null)
            return;
        string line = $"{DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture)} {level} {message}{Environment.NewLine}";
        lock (Gate)
            Waiting.Append(line);
        Wake.Set();
    }

    /// <summary>Puts every line logged so far in the file, before returning.</summary>
    public static void Flush()
    {
        lock (FileGate)
        {
            string lines;
            lock (Gate)
            {
                if (Waiting.Length == 0)
                    return;
                lines = Waiting.ToString();
                Waiting.Clear();
            }
            if (_file is not { } file)
                return;
            try
            {
                File.AppendAllText(file, lines);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Logging must never take the app down.
            }
        }
    }

    private static void WriteLoop()
    {
        while (true)
        {
            Wake.WaitOne();
            Thread.Sleep(50);   // a burst of lines (a deck loading) goes in one write
            Flush();
        }
    }
}
