using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using GZJam.App.Controls;
using GZJam.App.Views;

namespace GZJam.App.Tests;

/// <summary>The startup screen (issue #49): its sweep, and where it opens.</summary>
public sealed class StartupScreenTests
{
    private const double Frame = 1 / 60.0;

    /// <summary>Runs the sweep frame by frame from <paramref name="from"/> to <paramref name="to"/> seconds; every value it shows.</summary>
    private static List<double> Run(StartupProgress progress, double from, double to)
    {
        var shown = new List<double>();
        for (double t = from; t <= to; t += Frame)
            shown.Add(progress.Update(TimeSpan.FromSeconds(t)));
        return shown;
    }

    private static void NeverGoesBack(List<double> shown)
    {
        for (int i = 1; i < shown.Count; i++)
            Assert.True(shown[i] >= shown[i - 1], $"went back from {shown[i - 1]} to {shown[i]}");
    }

    [Fact]
    public void Within_a_step_the_sweep_creeps_on_but_never_reaches_the_steps_end()
    {
        var progress = new StartupProgress();
        progress.BeginStep(0, 0.2, TimeSpan.Zero);
        var shown = Run(progress, 0, 10);

        NeverGoesBack(shown);
        Assert.True(shown[30] > 0.05, "moving within half a second");
        Assert.InRange(shown[^1], 0.15, 0.2 * 0.9);   // a slow step: still short of its end
        Assert.False(progress.IsComplete);
    }

    [Fact]
    public void Each_step_carries_the_sweep_on_and_it_never_goes_back()
    {
        var progress = new StartupProgress();
        progress.BeginStep(0, 0.2, TimeSpan.Zero);
        var shown = Run(progress, 0, 0.05);
        progress.BeginStep(0.2, 0.6, TimeSpan.FromSeconds(0.05));
        shown.AddRange(Run(progress, 0.05, 0.3));
        progress.BeginStep(0.6, 0.85, TimeSpan.FromSeconds(0.3));
        shown.AddRange(Run(progress, 0.3, 1));

        NeverGoesBack(shown);
        Assert.InRange(shown[^1], 0.6, 0.85);
    }

    [Fact]
    public void Once_ready_the_sweep_reaches_the_end_before_the_startup_screen_goes()
    {
        var progress = new StartupProgress();
        progress.BeginStep(0.6, 0.85, TimeSpan.Zero);
        Run(progress, 0, 0.3);
        progress.Finish();
        var shown = Run(progress, 0.3, 0.3 + 0.2);   // App's StartupFinish

        NeverGoesBack(shown);
        Assert.Equal(1, shown[^1]);
        Assert.True(progress.IsComplete);
    }

    public static TheoryData<AppSettings.WindowPlacement> Placements => new()
    {
        new AppSettings.WindowPlacement { X = 120, Y = 80, Width = 1200, Height = 760 },
        new AppSettings.WindowPlacement { X = 0, Y = 0, Width = 1440, Height = 900, Maximized = true },
    };

    [AvaloniaTheory]
    [MemberData(nameof(Placements))]
    public void It_opens_exactly_where_the_main_window_will(AppSettings.WindowPlacement placement) => OpensWhereTheMainWindowWill(placement);

    /// <summary>The first start: no placement saved, so centred, at the main window's own size.</summary>
    [AvaloniaFact]
    public void On_the_first_start_it_opens_where_the_main_window_will() => OpensWhereTheMainWindowWill(null);

    private static void OpensWhereTheMainWindowWill(AppSettings.WindowPlacement? placement)
    {
        var main = new MainWindow();
        main.RestorePlacement(placement);
        var startup = new StartupWindow(placement);

        Assert.Equal(main.Width, startup.Width);
        Assert.Equal(main.Height, startup.Height);
        Assert.Equal(main.WindowStartupLocation, startup.WindowStartupLocation);
        Assert.Equal(main.Position, startup.Position);
        Assert.Equal(main.WindowState, startup.WindowState);
        Assert.Equal(main.MinWidth, startup.MinWidth);
        Assert.Equal(main.MinHeight, startup.MinHeight);
        main.Close();
    }

    [AvaloniaFact]
    public void It_shows_the_logo_and_only_startup_closes_it()
    {
        var startup = new StartupWindow(null);
        startup.Show();
        startup.BeginStep(0, 0.2);
        AppHarness.Settle(100);
        startup.Finish();
        AppHarness.Settle(300);

        Assert.True(startup.IsVisible);
        Assert.False(startup.ShowInTaskbar);
        Assert.Equal(WindowDecorations.None, startup.WindowDecorations);
        startup.Close();   // Alt+F4, say: GZJam is still starting
        Assert.True(startup.IsVisible);
        startup.Dismiss();
        Assert.False(startup.IsVisible);
    }
}
