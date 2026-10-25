using Avalonia.Headless.XUnit;
using GZJam.Audio;

namespace GZJam.App.Tests;

/// <summary>Headphones unplugged during a night (#72): said in the top bar, and back by themselves.</summary>
public sealed class UnpluggedOutputAppTests
{
    [AvaloniaFact]
    public void Unplugged_headphones_say_so_and_come_back_by_themselves()
    {
        using var app = new AppHarness();
        bool unplugged = false, pluggedIn = false;
        app.Mix.DeviceStopped = bus => unplugged && bus == app.Mix.Cue;
        app.Mix.Reconnect = _ => pluggedIn;
        Assert.True(app.ViewModel.HasCueOutput);

        unplugged = true;
        app.ViewModel.Tick();
        app.Screenshot("headphones-unplugged");
        Assert.Equal("Headphones unplugged. They'll come back by themselves when plugged in.", app.ViewModel.Notice);
        Assert.Equal("Unplugged", app.ViewModel.CueOutputName);
        Assert.False(app.ViewModel.HasCueOutput);   // the amber light
        Assert.True(app.ViewModel.HasMainOutput);   // the room plays on

        pluggedIn = true;
        unplugged = false;
        AppHarness.WaitFor(() => { app.ViewModel.Tick(); return app.ViewModel.HasCueOutput; }, TimeSpan.FromSeconds(5));
        Assert.Equal("Headphones are back.", app.ViewModel.Notice);
        Assert.Equal("Offline", app.ViewModel.CueOutputName);
    }
}
