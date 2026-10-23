using Avalonia.Headless.XUnit;
using Avalonia.Logging;

namespace GZJam.App.Tests;

/// <summary>Avalonia's log: the input method's errors on Linux noted once, everything else as before (issue #21).</summary>
public sealed class AvaloniaLogTests
{
    private sealed class Collect : ILogSink
    {
        public List<string> Lines { get; } = [];
        public bool IsEnabled(LogEventLevel level, string area) => level >= LogEventLevel.Warning;
        public void Log(LogEventLevel level, string area, object? source, string messageTemplate) => Lines.Add($"{area}: {messageTemplate}");
        public void Log(LogEventLevel level, string area, object? source, string messageTemplate, params object?[] propertyValues) =>
            Lines.Add($"{area}: {messageTemplate}");
    }

    // As Avalonia.FreeDesktop's DBusTextInputMethodBase logs them when Fcitx doesn't answer.
    private const string ContextFailed = """
        Unable to create IME input context:
        Tmds.DBus.Protocol.DBusException: org.freedesktop.DBus.Error.ServiceUnknown: The name org.fcitx.Fcitx5 was not provided by any .service files
           at Tmds.DBus.Protocol.DBusConnection.CallMethodAsync[T](MessageBuffer message)
           at Avalonia.FreeDesktop.DBusIme.DBusTextInputMethodBase.Connect(String name)
        """;

    [Fact]
    public void The_input_methods_errors_are_noted_once_and_kept_off_the_console()
    {
        var console = new Collect();
        var notes = new List<string>();
        var log = new AvaloniaLog(console, notes.Add);

        for (int i = 0; i < 3; i++)
            log.Log(LogEventLevel.Error, "IME", null, ContextFailed);
        log.Log(LogEventLevel.Error, "IME", null, "Error:\n" + "System.Exception: gone", []);
        log.Log(LogEventLevel.Error, LogArea.FreeDesktopPlatform, null, "WatchNameOwner for 'org.fcitx.Fcitx' failed: System.Exception: no bus");

        Assert.Empty(console.Lines);
        string note = Assert.Single(notes);
        Assert.Equal("The input method (Fcitx or IBus) isn't answering, so typing goes straight from the keyboard. " +
                     "(Unable to create IME input context: Tmds.DBus.Protocol.DBusException: org.freedesktop.DBus.Error.ServiceUnknown: " +
                     "The name org.fcitx.Fcitx5 was not provided by any .service files)", note);
    }

    [Fact]
    public void Everything_else_avalonia_logs_passes_through()
    {
        var console = new Collect();
        var notes = new List<string>();
        var log = new AvaloniaLog(console, notes.Add);

        log.Log(LogEventLevel.Warning, LogArea.Binding, null, "An error occurred binding {Property}", "Text");
        log.Log(LogEventLevel.Error, LogArea.FreeDesktopPlatform, null, "Unable to get a dbus connection for system tray icons.");
        log.Log(LogEventLevel.Error, LogArea.Layout, null, "Layout cycle detected.");

        Assert.Equal(3, console.Lines.Count);
        Assert.Empty(notes);
        Assert.True(log.IsEnabled(LogEventLevel.Error, "IME"));
        Assert.False(log.IsEnabled(LogEventLevel.Debug, LogArea.Layout));   // as the console's own sink decides
    }

    [AvaloniaFact]   // on the UI thread with the other tests that use Avalonia's (global) log
    public void Wrapping_is_done_once()
    {
        ILogSink? before = Logger.Sink;
        try
        {
            Logger.Sink = new Collect();
            AvaloniaLog.QuietInputMethods();
            ILogSink wrapped = Logger.Sink!;
            AvaloniaLog.QuietInputMethods();
            Assert.IsType<AvaloniaLog>(wrapped);
            Assert.Same(wrapped, Logger.Sink);
        }
        finally
        {
            Logger.Sink = before;
        }
    }
}
