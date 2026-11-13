using Avalonia.Headless.XUnit;
using GZJam.Testing;

namespace GZJam.App.Tests;

/// <summary>
/// Closing GZJam while it scans a music folder (#350): the scan stops, and nothing is written to
/// the library once it's closed. It used to read on and write every song, into a library that was
/// gone; in the tests, into a data folder being deleted (SQLite error 14).
/// </summary>
public sealed class CloseMidScanTests
{
    private const int Songs = 2000;

    [AvaloniaFact]
    public void Closing_stops_the_scan_and_nothing_is_written_afterward()
    {
        using var app = new AppHarness();
        string big = Path.Combine(app.WorkDirectory, "Big");
        string first = Path.Combine(big, "0000.mp3");
        TestMp3.Write(first, new TestMp3.Tags("Song", "Band", "Album"));
        for (int i = 1; i < Songs; i++)
            File.Copy(first, Path.Combine(big, $"{i:0000}.mp3"));
        int before = app.Database.GetTrackCount();

        Task adding = app.Library.AddRootAsync(big);   // the scan starts on its own thread
        app.Library.Dispose();                          // as GZJam closes
        int atClose = app.Database.GetTrackCount();

        Assert.True(atClose < before + Songs, "the scan was stopped, not left to finish");
        Thread.Sleep(1000);
        AppHarness.SettleIdle();
        Assert.Equal(atClose, app.Database.GetTrackCount());   // nothing written once closed
        Assert.True(adding.IsCompleted);
        Assert.Null(app.Library.ScanStatus);
    }
}
