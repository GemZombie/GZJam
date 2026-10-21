using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GZJam.Library;

namespace GZJam.App.ViewModels;

/// <summary>The folder tree, search box and search results.</summary>
public sealed partial class LibraryViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan SearchDelay = TimeSpan.FromMilliseconds(120);

    private readonly LibraryDatabase _database;
    private readonly LibraryScanner _scanner;
    private readonly LibraryWatcher _watcher = new();
    private readonly SemaphoreSlim _scanLock = new(1, 1);
    private CancellationTokenSource? _typingDelay;
    private CancellationTokenSource? _searchCancel;
    private int _searchVersion;
    private bool _suppressSearch;
    private (string RootPath, string Folder)? _pendingSelection;
    private Dictionary<string, Track> _resultsByPath = new(StringComparer.Ordinal);

    public LibraryViewModel(LibraryDatabase database)
    {
        _database = database;
        _scanner = new LibraryScanner(database);
        _watcher.RootChanged += root => Dispatcher.UIThread.Post(() => _ = ScanAsync(root));
    }

    public ObservableCollection<FolderNodeViewModel> Roots { get; } = [];

    /// <summary>A scan added, changed or removed tracks.</summary>
    public event Action? Scanned;

    [ObservableProperty]
    public partial FolderNodeViewModel? SelectedFolder { get; set; }

    [ObservableProperty]
    public partial string SearchText { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResultSummary), nameof(EmptyMessage), nameof(HasNoResults))]
    public partial IReadOnlyList<Track> Results { get; private set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoRoots))]
    public partial bool HasLoaded { get; private set; }

    [ObservableProperty]
    public partial string? ScanStatus { get; private set; }

    [ObservableProperty]
    public partial double ScanPercent { get; private set; }

    public bool HasNoRoots => HasLoaded && Roots.Count == 0;
    public bool HasNoResults => HasLoaded && Roots.Count > 0 && Results.Count == 0;
    public bool HasScope => SelectedFolder is not null;
    public string ScopeText => SelectedFolder is { } f ? $"in {f.DisplayPath} · all subfolders" : "Whole library";
    public string ResultSummary => Results.Count == 1 ? "1 track" : $"{Format.Count(Results.Count)} tracks";

    public string EmptyMessage
    {
        get
        {
            string where = SelectedFolder is { } f ? $" in {f.Name}" : "";
            return string.IsNullOrWhiteSpace(SearchText) ? $"No tracks{where}." : $"Nothing matches \"{SearchText.Trim()}\"{where}.";
        }
    }

    partial void OnResultsChanged(IReadOnlyList<Track> value)
    {
        _resultsByPath = new Dictionary<string, Track>(value.Count, StringComparer.Ordinal);
        foreach (Track track in value)
            _resultsByPath[track.Path] = track;
    }

    /// <summary>The track for a file in the current results, if it's there.</summary>
    public Track? ResultFor(string path) => _resultsByPath.GetValueOrDefault(path);

    partial void OnSelectedFolderChanged(FolderNodeViewModel? value)
    {
        // Picking a folder by hand overrides a remembered one that hasn't appeared yet.
        if (!_suppressSearch)
            _pendingSelection = null;
        OnPropertyChanged(nameof(HasScope));
        OnPropertyChanged(nameof(ScopeText));
        if (!_suppressSearch)
            RunSearch();
    }

    partial void OnSearchTextChanged(string value)
    {
        // Debounce typing: each keystroke restarts the delay, so a fast typist triggers one search.
        _typingDelay?.Cancel();
        if (_suppressSearch)
            return;
        var delay = _typingDelay = new CancellationTokenSource();
        Task.Delay(SearchDelay, delay.Token).ContinueWith(_ => Dispatcher.UIThread.Post(() =>
        {
            if (!delay.IsCancellationRequested)
                RunSearch();
        }), CancellationToken.None, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);
    }

    [RelayCommand]
    private void ClearScope() => SelectedFolder = null;

    [RelayCommand]
    private void ClearSearch() => SearchText = "";

    // ───────────── Lifecycle ─────────────

    /// <summary>Shows what's already indexed immediately, then rescans every root in the background.</summary>
    public async Task StartAsync(string? selectedRootPath, string? selectedFolder)
    {
        RefreshTree();
        if (selectedRootPath is not null)
        {
            _pendingSelection = (selectedRootPath, selectedFolder ?? "");
            _suppressSearch = true;
            RestorePendingSelection();
            _suppressSearch = false;
        }
        HasLoaded = true;
        RunSearch();

        foreach (LibraryRoot r in _database.GetRoots())
        {
            _watcher.Watch(r);
            await ScanAsync(r);
        }
    }

    /// <summary>What to remember: the selected folder as (root path, relative folder).</summary>
    public (string? RootPath, string? Folder) GetSelection()
    {
        if (SelectedFolder is not { } f)
            return (null, null);
        string? rootPath = _database.GetRoots().FirstOrDefault(r => r.Id == f.Node.RootId)?.Path;
        return (rootPath, f.Node.RelativePath);
    }

    public async Task AddRootAsync(string path)
    {
        LibraryRoot root = _database.AddRoot(path);
        RefreshTree();
        _watcher.Watch(root);
        await ScanAsync(root);
    }

    public void RemoveRoot(long rootId)
    {
        _watcher.Unwatch(rootId);
        _database.RemoveRoot(rootId);
        RefreshTree();
        RunSearch();
    }

    public void RescanRoot(long rootId)
    {
        if (_database.GetRoots().FirstOrDefault(r => r.Id == rootId) is { } root)
            _ = ScanAsync(root);
    }

    private async Task ScanAsync(LibraryRoot root)
    {
        await _scanLock.WaitAsync();
        try
        {
            bool firstImport = _database.GetFolderCounts().All(c => c.RootId != root.Id);
            DateTime lastRefresh = DateTime.UtcNow;
            var progress = new Progress<ScanProgress>(p =>
            {
                if (p.Enumerating)
                {
                    ScanStatus = $"Looking for music… {Format.Count(p.FilesFound)}";
                    ScanPercent = 0;
                    return;
                }
                if (p.FilesToRead == 0)
                    return;
                ScanStatus = $"Reading tags… {Format.Count(p.FilesRead)} / {Format.Count(p.FilesToRead)}";
                ScanPercent = 100.0 * p.FilesRead / p.FilesToRead;

                // During a first import, let the library fill in as it goes.
                if (firstImport && DateTime.UtcNow - lastRefresh > TimeSpan.FromSeconds(1.5))
                {
                    lastRefresh = DateTime.UtcNow;
                    RefreshTree();
                    RunSearch();
                }
            });

            ScanResult result = await _scanner.ScanAsync(root, progress);
            if (result.Added + result.Updated + result.Removed > 0)
            {
                RefreshTree();
                // A remembered folder may only exist once its root has been scanned (e.g. a fresh database).
                RestorePendingSelection();
                RunSearch();
                Scanned?.Invoke();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ScanStatus = $"Couldn't scan {root.Path}: {ex.Message}";
            await Task.Delay(TimeSpan.FromSeconds(5));
        }
        finally
        {
            ScanStatus = null;
            ScanPercent = 0;
            _scanLock.Release();
        }
    }

    // ───────────── Tree ─────────────

    /// <summary>Rebuilds the tree from the database, keeping expansion and selection.</summary>
    private void RefreshTree()
    {
        var expanded = Roots.SelectMany(r => r.SelfAndDescendants()).Where(n => n.IsExpanded).Select(n => n.Key).ToHashSet();
        var selected = SelectedFolder?.Key;
        bool firstBuild = Roots.Count == 0;

        var tree = FolderTree.Build(_database.GetRoots(), _database.GetFolderCounts());
        _suppressSearch = true;
        Roots.Clear();
        foreach (FolderNode node in tree)
        {
            var vm = new FolderNodeViewModel(this, node, null);
            foreach (var n in vm.SelfAndDescendants())
                n.IsExpanded = expanded.Contains(n.Key) || (firstBuild && n.IsRoot);
            Roots.Add(vm);
        }
        SelectedFolder = selected is { } key ? Find(key.RootId, key.Folder) : null;
        _suppressSearch = false;
        OnPropertyChanged(nameof(HasNoRoots));
        OnPropertyChanged(nameof(ScopeText));
    }

    private FolderNodeViewModel? Find(long rootId, string folder) =>
        Roots.Where(r => r.Node.RootId == rootId).SelectMany(r => r.SelfAndDescendants()).FirstOrDefault(n => n.Node.RelativePath == folder);

    private void RestorePendingSelection()
    {
        if (_pendingSelection is not { } pending ||
            _database.GetRoots().FirstOrDefault(r => string.Equals(r.Path, pending.RootPath, StringComparison.OrdinalIgnoreCase)) is not { } root ||
            Find(root.Id, pending.Folder) is null)
            return;

        _pendingSelection = null;
        Reveal(root.Id, pending.Folder);
    }

    /// <summary>Selects a folder in the tree, expanding its parents so it's visible.</summary>
    public void Reveal(long rootId, string folder)
    {
        if (Find(rootId, folder) is not { } node)
            return;
        for (var p = node.Parent; p is not null; p = p.Parent)
            p.IsExpanded = true;
        SelectedFolder = node;
    }

    /// <summary>Jumps the tree to a track's own folder and clears the search, showing its album.</summary>
    public void RevealTrack(Track track)
    {
        _suppressSearch = true;
        SearchText = "";
        Reveal(track.RootId, track.Folder);
        _suppressSearch = false;
        RunSearch();
    }

    // ───────────── Search ─────────────

    private void RunSearch()
    {
        _searchCancel?.Cancel();
        var cancel = _searchCancel = new CancellationTokenSource();
        int version = ++_searchVersion;
        string text = SearchText;
        FolderScope? scope = SelectedFolder?.Node.Scope;

        Task.Run(() => _database.Search(text, scope, cancel.Token), cancel.Token).ContinueWith(t =>
        {
            if (t.IsCompletedSuccessfully && version == _searchVersion)
                Dispatcher.UIThread.Post(() =>
                {
                    if (version == _searchVersion)
                        Results = t.Result;
                });
        }, TaskScheduler.Default);
    }

    public void Dispose()
    {
        _typingDelay?.Cancel();
        _searchCancel?.Cancel();
        _watcher.Dispose();
    }
}
