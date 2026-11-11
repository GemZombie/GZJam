using GZJam.Library;

namespace GZJam.App.ViewModels;

/// <summary>
/// The song list's rows by file, to find a song's row when it changes (a rating, a download, a
/// scan). Made on the thread that read the list (#368), and small: one entry a song, with a list
/// only for a song a playlist has more than once.
/// </summary>
public sealed class ResultIndex
{
    public static ResultIndex Empty { get; } = new([]);

    private readonly Dictionary<string, Track> _first;
    private readonly Dictionary<string, List<Track>>? _more;

    private ResultIndex(IReadOnlyList<Track> tracks)
    {
        _first = new Dictionary<string, Track>(tracks.Count, StringComparer.Ordinal);
        foreach (Track track in tracks)
        {
            if (_first.TryAdd(track.Path, track))
                continue;
            _more ??= new Dictionary<string, List<Track>>(StringComparer.Ordinal);   // in a playlist twice
            if (_more.TryGetValue(track.Path, out var rows))
                rows.Add(track);
            else
                _more[track.Path] = [_first[track.Path], track];
        }
    }

    public static ResultIndex Of(IReadOnlyList<Track> tracks) => tracks.Count == 0 ? Empty : new ResultIndex(tracks);

    public Track? First(string path) => _first.GetValueOrDefault(path);

    public IReadOnlyList<Track> All(string path) =>
        _more?.GetValueOrDefault(path) is { } rows ? rows : _first.TryGetValue(path, out Track? track) ? [track] : [];
}
