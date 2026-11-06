using System.Diagnostics;

namespace GZJam.Library.Servers;

/// <summary>
/// A file being written no faster than <paramref name="bytesPerSecond"/> (#285: keeping a whole
/// server in the background, with a speed limit, so it doesn't fill the home connection or the
/// NAS). Writes wait as needed; reading, seeking and the length are the file's own.
/// </summary>
public sealed class ThrottledStream(Stream inner, long bytesPerSecond) : Stream
{
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private long _written;

    public override bool CanRead => false;
    public override bool CanSeek => inner.CanSeek;
    public override bool CanWrite => inner.CanWrite;
    public override long Length => inner.Length;

    public override long Position
    {
        get => inner.Position;
        set => inner.Position = value;
    }

    /// <summary>How long to wait before the next bytes, so far: none if writing has kept under the limit.</summary>
    private TimeSpan Ahead()
    {
        var due = TimeSpan.FromSeconds((double)_written / bytesPerSecond);
        return due - _clock.Elapsed;
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        inner.Write(buffer, offset, count);
        _written += count;
        if (Ahead() is { } wait && wait > TimeSpan.Zero)
            Thread.Sleep(wait);
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        await inner.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
        _written += buffer.Length;
        if (Ahead() is { } wait && wait > TimeSpan.Zero)
            await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override void Flush() => inner.Flush();
    public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
    public override void SetLength(long value) => inner.SetLength(value);
}
