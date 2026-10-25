using System.Globalization;

namespace GZJam.App;

/// <summary>
/// A plain-text activity log (one file per day, two weeks kept) so problems at a gig can be
/// traced afterward: what was pressed, what the decks did, and any errors.
/// Lives next to the library database, in a "logs" folder.
/// </summary>
internal static class Log
{
    private static readonly Lock Gate = new();
    private static string? _file;

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
        }
    }

    public static void Info(string message) => Write("INFO ", message);

    public static void Error(string message, Exception? exception = null) =>
        Write("ERROR", exception is null ? message : $"{message}: {exception}");

    private static void Write(string level, string message)
    {
        if (_file is null)
            return;
        string line = $"{DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture)} {level} {message}{Environment.NewLine}";
        lock (Gate)
        {
            try
            {
                File.AppendAllText(_file, line);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Logging must never take the app down.
            }
        }
    }
}
