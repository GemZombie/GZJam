namespace GZJam.Library.Servers;

/// <summary>How a song's download ended.</summary>
public enum DownloadEnd
{
    Arrived,
    Failed,
    /// <summary>Stopped before it was whole: the song, or all that was coming down.</summary>
    Stopped,
}

/// <summary>
/// One song coming down, told how it goes (the Downloads window, #304): its bytes as they arrive,
/// then how it ended. Called on the download's own thread.
/// </summary>
public interface IDownloadReport
{
    /// <summary>Stops just this song (the window's Stop on its row); what else is coming goes on.</summary>
    CancellationToken Skip { get; }

    void Started(long? size);

    void Received(long bytes);

    void Ended(DownloadEnd end, string? problem = null);
}
