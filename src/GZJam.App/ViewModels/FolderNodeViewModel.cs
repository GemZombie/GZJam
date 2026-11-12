using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GZJam.Library;

namespace GZJam.App.ViewModels;

/// <summary>A folder in the library tree.</summary>
public sealed partial class FolderNodeViewModel : ObservableObject
{
    private readonly LibraryViewModel _owner;

    public FolderNodeViewModel(LibraryViewModel owner, FolderNode node, FolderNodeViewModel? parent)
    {
        _owner = owner;
        Node = node;
        Parent = parent;
        Depth = parent is null ? 0 : parent.Depth + 1;
        _children = node.Children.Select(c => new FolderNodeViewModel(owner, c, this)).ToList();
        _listed = !node.IsFiles || node.Files == FilesKind.Computer;
    }

    private List<FolderNodeViewModel> _children;

    /// <summary>A place in Files has its folders read once it's opened (#318): a drive has thousands.</summary>
    private bool _listed;

    public FolderNode Node { get; private set; }
    public FolderNodeViewModel? Parent { get; }
    public IReadOnlyList<FolderNodeViewModel> Children => _children;

    /// <summary>A place in Files, opened: its folders, read now (and again when it's opened next).</summary>
    private void ListFolders()
    {
        // The folders listed before stay as they were (opened, chosen): only what's new is made.
        var had = _children.ToDictionary(c => c.Node.FullPath, StringComparer.Ordinal);
        _children = FileSources.Folders(Node.FullPath)
            .Select(f => had.GetValueOrDefault(f.Path) ?? new FolderNodeViewModel(_owner, FolderNode.ForFiles(f), this)).ToList();
        _listed = true;
        OnPropertyChanged(nameof(HasChildren));
    }

    /// <summary>How far down the tree: 0 for a music folder or a server.</summary>
    public int Depth { get; }

    /// <summary>The room before its row's arrow, for its place in the tree.</summary>
    public double Indent => Depth * 16;

    /// <summary>It has folders in it (or, in Files, may have, until it's opened): an arrow to open it.</summary>
    public bool HasChildren => !_listed || Children.Count > 0;

    public string Name => Node.Name;

    /// <summary>The same folders, the same way down (after a scan that only changed songs): this node can take <paramref name="node"/>'s counts.</summary>
    internal bool SameShape(FolderNode node)
    {
        if (Node.IsFiles || node.IsFiles)   // its folders are its own, read as it's opened
            return Node.Files == node.Files && Node.Name == node.Name && Node.FullPath == node.FullPath
                && (Node.Files != FilesKind.Computer || Children.Select(c => c.Node.FullPath).SequenceEqual(node.Children.Select(c => c.FullPath)));
        if (Node.Name != node.Name || Node.RelativePath != node.RelativePath || Node.FullPath != node.FullPath || Node.RootId != node.RootId
            || Node.IsServer != node.IsServer || Node.ServerId != node.ServerId || Node.ServerPlaylistId != node.ServerPlaylistId
            || Children.Count != node.Children.Count)
            return false;
        for (int i = 0; i < Children.Count; i++)
            if (!Children[i].SameShape(node.Children[i]))
                return false;
        return true;
    }

    /// <summary>Takes the same-shaped <paramref name="node"/>'s song counts, here and all the way down.</summary>
    internal void Follow(FolderNode node)
    {
        if (Node.IsFiles)
        {
            Node = node;
            return;
        }
        bool counted = Node.TrackCount != node.TrackCount;
        Node = node;
        if (counted)
            OnPropertyChanged(nameof(CountText));
        for (int i = 0; i < Children.Count; i++)
            Children[i].Follow(node.Children[i]);
    }
    public string CountText => IsSyncing ? "↻" : (IsServer && Node.TrackCount == 0) || IsFiles ? "" : Format.Count(Node.TrackCount);
    public bool IsRoot => Node.IsRoot;

    /// <summary>A music server (#290), not a folder: its songs are the server's.</summary>
    public bool IsServer => Node.IsServer;

    /// <summary>A place in Files (#318), or a folder in one: the computer's files, not the library's.</summary>
    public bool IsFiles => Node.IsFiles;

    /// <summary>One of My music's folders: scanned, its tags read again.</summary>
    public bool IsFolder => !IsServer && !IsFiles;

    /// <summary>A folder on disk, opened in the file manager: a music folder, or one in Files.</summary>
    public bool CanOpenFolder => !IsServer && Node.Files != FilesKind.Computer;

    /// <summary>"This computer", in Files.</summary>
    public bool IsComputer => Node.Files == FilesKind.Computer;

    /// <summary>A USB stick, in Files.</summary>
    public bool IsStick => Node.Files == FilesKind.Stick;

    /// <summary>A folder in Files, or a stick, whose songs can be added to My music (not a whole drive or place).</summary>
    public bool CanAddFilesFolder => Node.Files is FilesKind.Folder or FilesKind.Stick;

    /// <summary>A music folder, which can be removed from the library (a server is removed in Settings).</summary>
    public bool IsRemovableRoot => IsRoot && IsFolder;

    /// <summary>The server itself, not one of its folders: its own menu.</summary>
    public bool IsServerItself => IsServer && IsRoot;

    /// <summary>One of a server's folders, or playlists: its downloads can be removed from the computer.</summary>
    public bool IsServerFolder => IsServer && !IsRoot && !Node.IsServerPlaylists;

    /// <summary>A folder on a server (not its playlists): "Download to this computer".</summary>
    public bool IsServerRealFolder => IsServerFolder && !IsServerPlaylist;

    /// <summary>One of a server's playlists (#290), under its "Playlists".</summary>
    public bool IsServerPlaylist => Node.ServerPlaylistId is not null;

    /// <summary>A folder's icon: everything but a server and its playlists, the computer and a stick.</summary>
    public bool HasFolderIcon => !IsServerItself && !IsServerPlaylist && !IsComputer && !IsStick;

    /// <summary>A server playlist that isn't kept on the computer yet: "Keep on this computer".</summary>
    public bool CanKeep => IsServerPlaylist && !_owner.ServerPlaylistKept(Node.ServerId!.Value, Node.ServerPlaylistId!);

    /// <summary>A server playlist kept on the computer: "Open in my playlists".</summary>
    public bool IsKept => IsServerPlaylist && !CanKeep;

    /// <summary>The server can't be reached right now: a crossed-out cloud, and why in its tooltip.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOnline), nameof(NameTip))]
    public partial bool IsOffline { get; set; }

    public bool IsOnline => IsServerItself && !IsOffline;

    /// <summary>The server is syncing now: its count says so, and its menu can stop it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountText), nameof(CanSync))]
    public partial bool IsSyncing { get; set; }

    public bool CanSync => IsServerItself && !IsSyncing;

    /// <summary>The whole server is being kept in the DJ's library, in the background (#285).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanKeepWhole), nameof(CanPauseKeepWhole), nameof(CanResumeKeepWhole))]
    public partial bool IsKeepingWhole { get; set; }

    /// <summary>Keeping the whole server waits until the DJ resumes it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPauseKeepWhole), nameof(CanResumeKeepWhole))]
    public partial bool IsKeepWholePaused { get; set; }

    public bool CanKeepWhole => IsServerItself && !IsKeepingWhole;
    public bool CanPauseKeepWhole => IsServerItself && IsKeepingWhole && !IsKeepWholePaused;
    public bool CanResumeKeepWhole => IsServerItself && IsKeepingWhole && IsKeepWholePaused;

    public string NameTip => IsOffline ? $"{Name} can't be reached right now. Songs on this computer play as usual." : Name;
    public (long RootId, string Folder) Key => (Node.RootId, Node.RelativePath);

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    partial void OnIsExpandedChanged(bool value)
    {
        if (value && IsFiles && Node.Files != FilesKind.Computer)
            ListFolders();   // as it is now: a folder made or taken away since shows
        _owner.FolderToggled();
    }

    /// <summary>"Jazz &amp; Swing › Classic Swing", including the root's name.</summary>
    public string DisplayPath => Parent is null ? Name : $"{Parent.DisplayPath} › {Name}";

    public IEnumerable<FolderNodeViewModel> SelfAndAncestors()
    {
        for (FolderNodeViewModel? n = this; n is not null; n = n.Parent)
            yield return n;
    }

    public IEnumerable<FolderNodeViewModel> SelfAndDescendants() =>
        Children.SelectMany(c => c.SelfAndDescendants()).Prepend(this);

    [RelayCommand]
    private void Rescan() => _owner.RescanRoot(Node.RootId);

    /// <summary>Every song's tags again, for tags edited by a program that keeps the file's date.</summary>
    [RelayCommand]
    private void ReadAllTagsAgain() => _owner.RescanRoot(Node.RootId, readAllTags: true);

    [RelayCommand]
    private void RemoveFromLibrary() => _owner.RemoveRoot(Node.RootId);

    [RelayCommand]
    private void OpenInFileManager() => FileManager.Open(Node.FullPath);
}
