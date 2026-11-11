using GZJam.Library;

namespace GZJam.App.ViewModels;

/// <summary>
/// Files, in the library (#318): the computer's places and drives, and any USB stick, as folders.
/// A folder's songs show when it's chosen, filtered by name as you type; nothing is searched
/// across the computer, nothing is added to the library.
/// </summary>
public sealed partial class LibraryViewModel
{
    /// <summary>Files' places in the tree: "This computer", and each USB stick plugged in.</summary>
    public Func<IReadOnlyList<FolderNode>> FileNodes
    {
        get => _fileNodesSource;
        set
        {
            _fileNodesSource = value;
            _fileNodes = null;
        }
    }

    private Func<IReadOnlyList<FolderNode>> _fileNodesSource = () => [];

    /// <summary>"This computer" has been in the tree: opened the first time, then as the DJ leaves it.</summary>
    private bool _computerShown;

    /// <summary>Files' places as last found: a stick plugged in or pulled out finds them again.</summary>
    private IReadOnlyList<FolderNode>? _fileNodes;

    /// <summary>A folder's songs, for the song list: those directly in it, each with what the DJ gave it.</summary>
    public Func<string, CancellationToken, Task<IReadOnlyList<Track>>>? ListFiles { get; set; }

    /// <summary>A USB stick was plugged in or pulled out: Files' places again, what's open kept if it's still there.</summary>
    public void RefreshFiles()
    {
        bool showing = SelectedFolder?.IsFiles == true;
        _fileNodes = null;
        RefreshTree();
        if (showing && SelectedFolder is null)
            RunSearch();   // its stick was pulled out
    }

    /// <summary>Why Files' song list is empty, or what to do; null when it isn't Files showing.</summary>
    private string? FilesMessage()
    {
        if (SelectedPlaylist is not null || Source != LibrarySource.Files)
            return null;
        if (SelectedFolder is not { } folder || folder.IsComputer)
            return "Files is this computer and any USB stick. Open a folder on the left to see its songs; the box above filters them by name.";
        return string.IsNullOrWhiteSpace(SearchText)
            ? $"No songs in {folder.Name} itself: open a folder in it on the left."
            : $"No song in {folder.Name} has \"{SearchText.Trim()}\" in its name.";
    }

    /// <summary>A folder in Files, chosen: its songs, those whose names have every word typed.</summary>
    private async Task RunFilesListAsync(FolderNodeViewModel? folder, string text, int version, object key, CancellationToken cancel)
    {
        IReadOnlyList<Track> songs = [];
        if (folder is { IsComputer: false } && ListFiles is not null)
        {
            try
            {
                songs = await ListFiles(folder.Node.FullPath, cancel);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
        if (version != _searchVersion)
            return;
        string[] words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        DidYouMean = null;
        DidYouMeanText = null;
        ServerProblem = null;
        ServerNote = null;
        if (!await ShowResultsAsync(words.Length == 0 ? songs : songs.Where(t => words.All(w => NameHas(t, w))).ToList(), key, version))
            return;
        IsSearching = false;
        OnPropertyChanged(nameof(EmptyMessage));
    }

    /// <summary>The file's name, title or artist has <paramref name="word"/> in it.</summary>
    private static bool NameHas(Track track, string word) =>
        track.FileName.Contains(word, StringComparison.CurrentCultureIgnoreCase)
        || track.DisplayTitle.Contains(word, StringComparison.CurrentCultureIgnoreCase)
        || track.DisplayArtist.Contains(word, StringComparison.CurrentCultureIgnoreCase);
}
