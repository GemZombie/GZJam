using Avalonia.Headless.XUnit;
using GZJam.Library;

namespace GZJam.App.Tests;

/// <summary>A playlist, the queue or a night copied out as files (#101): said first, then copied.</summary>
public sealed class ExportAppTests
{
    [AvaloniaFact]
    public void The_queue_is_copied_out_once_the_DJ_says_so()
    {
        using var app = new AppHarness();
        var vm = app.ViewModel;
        var songs = app.Library.Results.Take(3).ToList();
        vm.AddToQueue(songs);
        string stick = Directory.CreateTempSubdirectory("gzjam-stick-").FullName;
        try
        {
            string? asked = null;
            vm.ConfirmAsync = (heading, message, confirm) =>
            {
                asked = $"{heading} | {message} | {confirm}";
                return Task.FromResult(true);
            };
            var (name, queued) = vm.QueueForExport();
            Assert.StartsWith("Queue, ", name);
            Assert.Equal(songs, queued);

            var copying = vm.ExportAsFilesAsync("Friday", queued, stick);
            AppHarness.WaitFor(() => copying.IsCompleted);
            copying.GetAwaiter().GetResult();

            Assert.Contains("Copy \"Friday\" as files?", asked);
            Assert.Contains("3 songs", asked);
            Assert.Contains("never moved or changed", asked);
            string folder = Path.Combine(stick, "Friday");
            Assert.Equal(4, Directory.GetFiles(folder).Length);   // the songs and the playlist
            Assert.StartsWith("01 - ", Path.GetFileName(Directory.GetFiles(folder, "01 *").Single()));
            Assert.True(File.Exists(Path.Combine(folder, "Friday.m3u8")));
            Assert.Contains(folder, vm.Notice);
            Assert.False(vm.BackgroundStatus?.StartsWith("Copying") == true);   // done, the top bar back as it was

            // Said no to: nothing made.
            vm.ConfirmAsync = (_, _, _) => Task.FromResult(false);
            vm.ExportAsFilesAsync("Saturday", queued, stick).GetAwaiter().GetResult();
            Assert.False(Directory.Exists(Path.Combine(stick, "Saturday")));
        }
        finally
        {
            Directory.Delete(stick, recursive: true);
        }
    }

    [AvaloniaFact]
    public void Songs_whose_files_are_gone_are_named_before_copying()
    {
        using var app = new AppHarness();
        var vm = app.ViewModel;
        var gone = new Track { Path = Path.Combine(Path.GetTempPath(), "gzjam-not-there.mp3"), Folder = "", FileName = "gzjam-not-there.mp3",
            Title = "Not There" };
        string stick = Directory.CreateTempSubdirectory("gzjam-stick-").FullName;
        try
        {
            string? asked = null;
            vm.ConfirmAsync = (_, message, _) => { asked = message; return Task.FromResult(false); };
            vm.ExportAsFilesAsync("Set", [app.Library.Results[0], gone], stick).GetAwaiter().GetResult();
            Assert.Contains("Left out, its file isn't there: Not There.", asked);

            vm.ExportAsFilesAsync("Set", [gone], stick).GetAwaiter().GetResult();
            Assert.Equal("None of these songs' files are there to copy.", vm.Notice);
        }
        finally
        {
            Directory.Delete(stick, recursive: true);
        }
    }
}
