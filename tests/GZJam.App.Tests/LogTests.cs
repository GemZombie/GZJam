namespace GZJam.App.Tests;

/// <summary>
/// The activity log is written by a thread of its own (#367): logging only queues a line, so a
/// slow disk never holds up the window. Nothing is lost or out of order, and errors go in at once.
/// </summary>
[Collection("Log")]
public sealed class LogTests : IDisposable
{
    private readonly string _work = Directory.CreateTempSubdirectory("gzjam-log-").FullName;

    public void Dispose()
    {
        Log.Stop();
        try
        {
            Directory.Delete(_work, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string Written() => File.ReadAllText(Directory.GetFiles(Path.Combine(_work, "logs"), "gzjam-*.log").Single());

    [Fact]
    public void Lines_from_many_threads_all_arrive_each_threads_in_order()
    {
        Log.Start(_work);
        Parallel.For(0, 4, thread =>
        {
            for (int i = 0; i < 500; i++)
                Log.Info($"thread {thread} line {i:000}");
        });
        Log.Flush();

        string[] lines = Written().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2000, lines.Length);
        for (int thread = 0; thread < 4; thread++)
        {
            var mine = lines.Where(l => l.Contains($"thread {thread} line")).Select(l => l[^3..]).ToList();
            Assert.Equal(Enumerable.Range(0, 500).Select(i => i.ToString("000")), mine);
        }
    }

    [Fact]
    public void A_line_is_written_by_the_logs_own_thread_soon_and_an_error_at_once()
    {
        Log.Start(_work);
        Log.Info("A deck loaded");
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!File.Exists(Path.Combine(_work, "logs", $"gzjam-{DateTime.Now:yyyy-MM-dd}.log")) || !Written().Contains("A deck loaded"))
        {
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), "The log's thread didn't write the line");
            Thread.Sleep(20);
        }

        Log.Error("Something broke", new InvalidOperationException("boom"));
        Assert.Contains("ERROR Something broke: System.InvalidOperationException: boom", Written());   // no wait
    }
}
