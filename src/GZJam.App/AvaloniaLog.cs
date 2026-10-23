using Avalonia.Logging;

namespace GZJam.App;

/// <summary>
/// Avalonia's own log (to the console, through Trace), less the input method's. On Linux, when the
/// desktop names Fcitx or IBus as its input method but it doesn't answer over D-Bus, Avalonia logs
/// an error for it, with a stack trace, again and again (issue #21). Typing still works, straight
/// from the keyboard, so that's noted once in GZJam's activity log instead. Input methods aren't
/// turned off: where Fcitx or IBus does answer, typing through it (e.g. Japanese titles) works.
/// Everything else Avalonia logs passes through as before.
/// </summary>
internal sealed class AvaloniaLog(ILogSink inner, Action<string> note) : ILogSink
{
    private int _noted;

    /// <summary>Wraps the log sink Avalonia was given (e.g. by LogToTrace).</summary>
    public static void QuietInputMethods()
    {
        if (Logger.Sink is { } sink and not AvaloniaLog)
            Logger.Sink = new AvaloniaLog(sink, GZJam.App.Log.Info);
    }

    /// <summary>
    /// The input method's messages: Avalonia's "IME" area (DBusTextInputMethodBase), and watching
    /// for Fcitx or IBus on D-Bus (logged under FreeDesktopPlatform, with the service's name).
    /// </summary>
    internal static bool IsInputMethod(string area, string message) =>
        area == "IME" ||
        area == LogArea.FreeDesktopPlatform &&
        (message.Contains("fcitx", StringComparison.OrdinalIgnoreCase) || message.Contains("ibus", StringComparison.OrdinalIgnoreCase));

    public bool IsEnabled(LogEventLevel level, string area) => inner.IsEnabled(level, area);

    public void Log(LogEventLevel level, string area, object? source, string messageTemplate)
    {
        if (IsInputMethod(area, messageTemplate))
            Note(messageTemplate);
        else
            inner.Log(level, area, source, messageTemplate);
    }

    public void Log(LogEventLevel level, string area, object? source, string messageTemplate, params object?[] propertyValues)
    {
        if (IsInputMethod(area, messageTemplate))
            Note(messageTemplate);
        else
            inner.Log(level, area, source, messageTemplate, propertyValues);
    }

    /// <summary>Once: what went wrong, in a line (the message and the exception's first line, no stack trace).</summary>
    private void Note(string message)
    {
        if (Interlocked.Exchange(ref _noted, 1) != 0)
            return;
        string summary = string.Join(" ", message.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).Take(2));
        if (summary.Length > 300)
            summary = summary[..300] + "…";
        note($"The input method (Fcitx or IBus) isn't answering, so typing goes straight from the keyboard. ({summary})");
    }
}
