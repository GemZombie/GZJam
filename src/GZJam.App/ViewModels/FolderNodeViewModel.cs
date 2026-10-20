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
    public string CountText => Format.Count(Node.TrackCount);
    public bool IsRoot => Node.IsRoot;
    public (long RootId, string Folder) Key => (Node.RootId, Node.RelativePath);

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    /// <summary>"Jazz &amp; Swing › Classic Swing", including the root's name.</summary>
    public string DisplayPath => Parent is null ? Name : $"{Parent.DisplayPath} › {Name}";

    public IEnumerable<FolderNodeViewModel> SelfAndDescendants() =>
        Children.SelectMany(c => c.SelfAndDescendants()).Prepend(this);

    [RelayCommand]
    private void Rescan() => _owner.RescanRoot(Node.RootId);

    [RelayCommand]
    private void RemoveFromLibrary() => _owner.RemoveRoot(Node.RootId);

    [RelayCommand]
    private void OpenInFileManager() => FileManager.Open(Node.FullPath);
}
