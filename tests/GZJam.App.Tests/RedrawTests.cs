using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using GZJam.App.Controls;

namespace GZJam.App.Tests;

/// <summary>Drawn again only when what shows changed (#366).</summary>
public sealed class RedrawTests
{
    /// <summary>
    /// The seek bar's place is reported 30 times a second, but on a long song the playhead moves a
    /// pixel or two a second: the whole waveform was drawn again each time. Now only when it
    /// reaches another pixel.
    /// </summary>
    [AvaloniaFact]
    public void The_seek_bar_is_drawn_again_only_when_the_playhead_reaches_another_pixel()
    {
        var bar = new SeekBar { Width = 600, Height = 40, Fill = Avalonia.Media.Brushes.SteelBlue, Progress = 0.5 };
        var window = new Window { Content = bar, Width = 640, Height = 80 };
        window.Show();
        Frame(window);
        int before = bar.Renders;

        // Thirty reports, a second of a 4-minute song: 2.5 px in all.
        for (int i = 1; i <= 30; i++)
        {
            bar.Progress = 0.5 + i / 30.0 / 240;
            Frame(window);
        }
        Assert.InRange(bar.Renders - before, 2, 4);

        bar.Progress = 0.75;   // a seek: drawn there at once
        Frame(window);
        Assert.InRange(bar.Renders - before, 3, 5);
        window.Close();
    }

    private static void Frame(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame()?.Dispose();
    }
}
