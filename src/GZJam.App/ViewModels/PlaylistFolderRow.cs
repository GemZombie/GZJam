using CommunityToolkit.Mvvm.ComponentModel;
using GZJam.Library;

namespace GZJam.App.ViewModels;

/// <summary>
/// A folder of playlists, as a row of the Playlists panel (#354): its arrow opens and closes it,
/// and it says how many playlists it holds, all the way down. The same row stays as the folder
/// is renamed or moved, so the list doesn't jump.
/// </summary>
public sealed partial class PlaylistFolderRow : ObservableObject
{
    private readonly LibraryViewModel _owner;

    internal PlaylistFolderRow(LibraryViewModel owner, PlaylistFolder folder, bool isExpanded)
    {
        _owner = owner;
        Folder = folder;
        _isExpanded = isExpanded;
    }

    /// <summary>The folder as the library last said: its name, where it is, what it holds.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Id), nameof(Name), nameof(Count), nameof(Indent), nameof(Tip))]
    public partial PlaylistFolder Folder { get; internal set; }

    /// <summary>Folders or playlists in it: an arrow to open it.</summary>
    [ObservableProperty]
    public partial bool HasChildren { get; internal set; }

    public long Id => Folder.Id;
    public string Name => Folder.Name;
    public int Count => Folder.Count;

    /// <summary>Its place among the folders, as the folder tree indents.</summary>
    public double Indent => Folder.Depth * 16;

    public string Tip => Count == 1 ? $"{Name}: 1 playlist" : $"{Name}: {Count} playlists";

    private bool _isExpanded;

    /// <summary>Opened by the library itself (to show a playlist in it): no row change of its own.</summary>
    internal void SetExpandedQuietly(bool value)
    {
        _isExpanded = value;
        OnPropertyChanged(nameof(IsExpanded));
    }

    /// <summary>Open, showing what's in it; remembered between runs.</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (SetProperty(ref _isExpanded, value))
                _owner.PlaylistFolderToggled(this);
        }
    }
}
