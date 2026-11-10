using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GZJam.Library;

namespace GZJam.App.Views;

/// <summary>
/// The song list keeps its place when GZJam reads the same list again by itself: after a scan,
/// or with each song a sync brings down (a new file in the downloads folder is scanned). Before,
/// it went back to the top each time, every few seconds during a sync, so a song far down a
/// server's long folder couldn't be reached. The rows in view and the songs selected stay; a new
/// folder, playlist or search starts at the top as before. The grid keeps one collection and its
/// rows (#343), so it keeps its scroll position too: a new list is scrolled to the top here.
/// </summary>
public partial class MainWindow
{
    /// <summary>What was in view and selected, by path, while the same list is read again.</summary>
    private sealed record ListPlace(string? Top, string? Bottom, IReadOnlyList<string> Selected);

    private ListPlace? _keepPlace;

    /// <summary>A new list is coming: show it from the top.</summary>
    private bool _toTop;

    private ViewModels.LibraryViewModel? _placeKeptFor;

    private void InitializeKeepPlace()
    {
        DataContextChanged += (_, _) =>
        {
            if (DataContext is not ViewModels.MainWindowViewModel { Library: { } library } || library == _placeKeptFor)
                return;
            _placeKeptFor = library;
            library.ResultsReplacing += same =>
            {
                _keepPlace = same ? PlaceNow() : null;
                _toTop = !same;
            };
            library.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName != nameof(ViewModels.LibraryViewModel.Results))
                    return;
                if (_keepPlace is { } place)
                {
                    _keepPlace = null;
                    Dispatcher.UIThread.Post(() => PutBack(place), DispatcherPriority.Loaded);
                }
                else if (_toTop)
                {
                    _toTop = false;
                    // After the grid's layout: it puts its old scroll position back as it lays out the reset rows.
                    Dispatcher.UIThread.Post(ScrollToTop, DispatcherPriority.Loaded);
                }
            };
        };
    }

    /// <summary>The first and last songs in view, and the songs selected.</summary>
    private ListPlace? PlaceNow()
    {
        var selected = TrackGrid.SelectedItems.OfType<Track>().Select(t => t.Path).ToList();
        var rows = TrackGrid.GetVisualDescendants().OfType<DataGridRow>()
            .Where(r => r.IsVisible && r.DataContext is Track)
            .Select(r => (Row: r, Top: r.TranslatePoint(new Point(0, 0), TrackGrid)?.Y))
            .Where(r => r.Top is { } y && y + r.Row.Bounds.Height > 0 && y < TrackGrid.Bounds.Height)
            .OrderBy(r => r.Top)
            .ToList();
        if (rows.Count == 0 && selected.Count == 0)
            return null;
        string? top = rows.Count > 0 ? ((Track)rows[0].Row.DataContext!).Path : null;
        string? bottom = rows.Count > 0 ? ((Track)rows[^1].Row.DataContext!).Path : null;
        return new ListPlace(top, bottom, selected);
    }

    /// <summary>The first row in the list's order (it may be sorted by a column) in view.</summary>
    private void ScrollToTop()
    {
        if (TrackGrid.CollectionView?.Cast<object>().FirstOrDefault() is { } first)
            TrackGrid.ScrollIntoView(first, null);
    }

    /// <summary>The same songs in view again (the last, then the first, so they sit where they were), and selected again.</summary>
    private void PutBack(ListPlace place)
    {
        var results = Vm.Library.Results;
        if (results.Count == 0)
            return;
        var byPath = new Dictionary<string, Track>(StringComparer.Ordinal);
        foreach (Track track in results)
            byPath.TryAdd(track.Path, track);
        if (place.Selected.Count > 0)
        {
            TrackGrid.SelectedItems.Clear();
            foreach (string path in place.Selected)
                if (byPath.TryGetValue(path, out Track? track))
                    TrackGrid.SelectedItems.Add(track);
        }
        bool atTop = place.Top is not null && byPath.TryGetValue(place.Top, out Track? first) && ReferenceEquals(first, results[0]);
        if (atTop)
            return;   // it was at the top, and is
        if (place.Bottom is not null && byPath.TryGetValue(place.Bottom, out Track? last))
            TrackGrid.ScrollIntoView(last, null);
        if (place.Top is not null && byPath.TryGetValue(place.Top, out Track? top))
            TrackGrid.ScrollIntoView(top, null);
    }
}
