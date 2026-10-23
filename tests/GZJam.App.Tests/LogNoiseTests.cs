using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Logging;

namespace GZJam.App.Tests;

/// <summary>The console stays quiet in normal use, so a real problem isn't lost among harmless messages.</summary>
public sealed class LogNoiseTests
{
    /// <summary>Collects what Avalonia logs about bindings while it's in place.</summary>
    private sealed class BindingLog : ILogSink, IDisposable
    {
        private readonly ILogSink? _previous = Logger.Sink;
        public List<string> Messages { get; } = [];

        public BindingLog() => Logger.Sink = this;

        public void Dispose() => Logger.Sink = _previous;

        public bool IsEnabled(LogEventLevel level, string area) => level >= LogEventLevel.Warning && area == LogArea.Binding;

        public void Log(LogEventLevel level, string area, object? source, string messageTemplate) =>
            Log(level, area, source, messageTemplate, []);

        public void Log(LogEventLevel level, string area, object? source, string messageTemplate, params object?[] propertyValues)
        {
            if (IsEnabled(level, area))
                Messages.Add($"{level} {source?.GetType().Name}: {messageTemplate} [{string.Join(", ", propertyValues)}]");
        }
    }

    [AvaloniaFact]
    public void The_library_list_logs_no_binding_errors()
    {
        using var log = new BindingLog();
        using var app = new AppHarness();
        var grid = app.Window.FindControl<DataGrid>("TrackGrid")!;

        // Rows shown, sorted by the converted columns, selected and scrolled.
        foreach (DataGridColumn column in grid.Columns.Where(c => c.Header is "BPM" or "Played" or "Time"))
        {
            column.Sort();
            AppHarness.Settle(100);
        }
        grid.SelectedIndex = 1;
        grid.ScrollIntoView(grid.SelectedItem, null);
        AppHarness.Settle(200);

        Assert.True(log.Messages.Count == 0, string.Join(Environment.NewLine, log.Messages.Distinct().Take(10)));
    }

    [AvaloniaFact]
    public void A_short_set_logs_no_binding_errors()
    {
        using var log = new BindingLog();
        using var app = new AppHarness();
        var vm = app.ViewModel;

        vm.Queue.Add(app.Library.Results.Take(3));
        vm.LoadToDeckAsync(vm.Deck1!, app.ToneTrack("Tone A", seconds: 40)).GetAwaiter().GetResult();
        vm.LoadToDeckAsync(vm.Deck2!, app.ToneTrack("Tone B", seconds: 40)).GetAwaiter().GetResult();
        vm.Deck1!.AddCue(TimeSpan.FromSeconds(20));
        vm.Deck1.TogglePlayCommand.Execute(null);
        app.Play(35);   // long enough to count as played: History has a row
        vm.ShowHistory = true;
        vm.SetDeckCount(4);
        app.Play(0.5);
        vm.Deck2!.EjectCommand.Execute(null);
        vm.SetDeckCount(2);
        vm.Settings.TouchMode = true;
        app.Play(0.5);
        AppHarness.Settle(200);

        Assert.True(log.Messages.Count == 0, string.Join(Environment.NewLine, log.Messages.Distinct().Take(10)));
    }
}
