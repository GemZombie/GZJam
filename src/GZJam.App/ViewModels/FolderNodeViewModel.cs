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
        Children = node.Children.Select(c => new FolderNodeViewModel(owner, c, this)).ToList();
    }

    public FolderNode Node { get; }
    public FolderNodeViewModel? Parent { get; }
    public IReadOnlyList<FolderNodeViewModel> Children { get; }

    public string Name => Node.Name;
    public string CountText => IsSyncing ? "↻" : IsServer && Node.TrackCount == 0 ? "" : Format.Count(Node.TrackCount);
    public bool IsRoot => Node.IsRoot;

    /// <summary>A music server (#290), not a folder: its songs are the server's.</summary>
    public bool IsServer => Node.IsServer;

    public bool IsFolder => !IsServer;

    /// <summary>A music folder, which can be removed from the library (a server is removed in Settings).</summary>
    public bool IsRemovableRoot => IsRoot && !IsServer;

    /// <summary>The server itself, not one of its folders: its own menu.</summary>
    public bool IsServerItself => IsServer && IsRoot;

    /// <summary>One of a server's folders: its downloads can be removed from the laptop.</summary>
    public bool IsServerFolder => IsServer && !IsRoot;

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

    public string NameTip => IsOffline ? $"{Name} can't be reached right now. Songs on this laptop play as usual." : Name;
    public (long RootId, string Folder) Key => (Node.RootId, Node.RelativePath);

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    partial void OnIsExpandedChanged(bool value)
    {
        if (value)
            _owner.FolderExpanded();
    }

    /// <summary>"Jazz &amp; Swing › Classic Swing", including the root's name.</summary>
    public string DisplayPath => Parent is null ? Name : $"{Parent.DisplayPath} › {Name}";

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
