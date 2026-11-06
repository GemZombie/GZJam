using System.Diagnostics;
using GZJam.Library.Servers;

namespace GZJam.Library.Tests;

/// <summary>The speed limit for keeping a whole server (#285).</summary>
public sealed class ThrottledStreamTests
{
    [Fact]
    public async Task Writes_go_no_faster_than_the_limit_and_arrive_whole()
    {
        using var file = new MemoryStream();
        var limited = new ThrottledStream(file, bytesPerSecond: 400_000);
        byte[] chunk = new byte[20_000];
        new Random(7).NextBytes(chunk);
        var clock = Stopwatch.StartNew();

        for (int i = 0; i < 10; i++)
            await limited.WriteAsync(chunk, TestContext.Current.CancellationToken);

        Assert.True(clock.Elapsed >= TimeSpan.FromMilliseconds(450), $"took {clock.Elapsed.TotalMilliseconds} ms");   // 200 KB at 400 KB/s: half a second
        Assert.Equal(200_000, file.Length);
        Assert.Equal(chunk, file.ToArray()[180_000..]);
    }
}
