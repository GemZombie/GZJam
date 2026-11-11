using System.Globalization;
using GZJam.Library;

namespace GZJam.App.ViewModels;

/// <summary>
/// Puts the song list in a column's order (#368), on whatever thread asks: a search sorts its
/// songs on its own worker thread, so a list of 60,000 songs never waits on the window's thread.
/// The grid's own sort did it there, slowly (over 100 ms for 60,000 songs, whatever the column).
/// Stable, as the grid's was: songs alike in the column keep the list's own order, both ways.
/// </summary>
public static class ListOrder
{
    /// <summary>Above this many songs, a list is sorted on a worker thread rather than the window's.</summary>
    public static int SortHereUpTo { get; internal set; } = 3000;

    public static IReadOnlyList<Track> Sort(IReadOnlyList<Track> tracks, AppSettings.ListSort sort, CancellationToken cancel = default)
    {
        if (tracks.Count < 2)
            return tracks;
        Func<Track, object?> value = Value(sort.Column);
        var keys = new object?[tracks.Count];
        for (int i = 0; i < keys.Length; i++)
            keys[i] = value(tracks[i]);
        cancel.ThrowIfCancellationRequested();
        var order = new int[keys.Length];
        for (int i = 0; i < order.Length; i++)
            order[i] = i;
        int direction = sort.Descending ? -1 : 1;
        if (keys.Any(k => k is string))
        {
            // Text, as a person reads it (case aside), compared by sort keys made once a song:
            // several times faster than comparing the text each time.
            CompareInfo culture = CultureInfo.CurrentCulture.CompareInfo;
            var text = new SortKey[keys.Length];
            for (int i = 0; i < text.Length; i++)
                text[i] = culture.GetSortKey(keys[i] as string ?? "", CompareOptions.IgnoreCase);
            cancel.ThrowIfCancellationRequested();
            Array.Sort(order, (a, b) =>
            {
                int c = SortKey.Compare(text[a], text[b]) * direction;
                return c != 0 ? c : a.CompareTo(b);
            });
        }
        else
        {
            // Anything else by its own order, nothing first.
            Array.Sort(order, (a, b) =>
            {
                int c = Comparer<object>.Default.Compare(keys[a], keys[b]) * direction;
                return c != 0 ? c : a.CompareTo(b);
            });
        }
        cancel.ThrowIfCancellationRequested();
        var sorted = new Track[order.Length];
        for (int i = 0; i < order.Length; i++)
            sorted[i] = tracks[order[i]];
        return sorted;
    }

    /// <summary>What a column (a song's property, or "tag:" and a tag's key, #119) sorts by.</summary>
    private static Func<Track, object?> Value(string column)
    {
        if (column.StartsWith("tag:", StringComparison.Ordinal))
        {
            string key = column[4..];
            return t => t.Tag(key);
        }
        return column switch
        {
            nameof(Track.DisplayTitle) => t => t.DisplayTitle,
            nameof(Track.DisplayArtist) => t => t.DisplayArtist,
            nameof(Track.Album) => t => t.Album,
            nameof(Track.Genre) => t => t.Genre,
            nameof(Track.Grouping) => t => t.Grouping,
            nameof(Track.Comment) => t => t.Comment,
            nameof(Track.Year) => t => t.Year,
            nameof(Track.Bpm) => t => t.Bpm,
            nameof(Track.Duration) => t => t.Duration,
            nameof(Track.AddedUtc) => t => t.AddedUtc,
            nameof(Track.LastPlayedUtc) => t => t.LastPlayedUtc,
            nameof(Track.Rating) => t => t.Rating,
            nameof(Track.Energy) => t => t.Energy,
            _ => typeof(Track).GetProperty(column) is { } property ? t => property.GetValue(t) : _ => null,
        };
    }
}
