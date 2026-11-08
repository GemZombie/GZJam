using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using GZJam.Library;

namespace GZJam.App.ViewModels;

/// <summary>Where songs added to My music go (#324): a folder, whether their own folders come too, and whether it's the usual place.</summary>
public sealed record AddPlace(string Folder, bool KeepFolders, bool IsUsual);

/// <summary>A folder of My music, in the Add to my music dialog: its own folders read from the disk when it's opened.</summary>
public sealed partial class AddFolderRow : ObservableObject
{
    private readonly string? _notThis;
    private bool _listed;

    /// <param name="notThis">A folder not to show, inside this one or any under it.</param>
    public AddFolderRow(string name, string path, string? notThis)
    {
        Name = name;
        Path = path;
        _notThis = notThis;
        Children.Add(new AddFolderRow());   // so it has an arrow: the disk isn't read until it's opened
    }

    private AddFolderRow()
    {
        Name = Path = "";
        _listed = true;
    }

    public string Name { get; }

    public string Path { get; }

    public ObservableCollection<AddFolderRow> Children { get; } = [];

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    partial void OnIsExpandedChanged(bool value)
    {
        if (value)
            List();
    }

    /// <summary>Reads its folders from the disk, the first time.</summary>
    internal void List()
    {
        if (_listed)
            return;
        _listed = true;
        Children.Clear();
        foreach (FilePlace folder in FileSources.Folders(Path))
            if (_notThis is null || !FileSources.IsIn(folder.Path, _notThis))
                Children.Add(new AddFolderRow(folder.Name, folder.Path, _notThis));
    }

    /// <summary>Reads its folders again, with one just made among them.</summary>
    internal void ListAgain()
    {
        _listed = false;
        List();
    }
}

/// <summary>
/// The Add to my music dialog (#324): which folder of My music songs go to. It opens on the last
/// folder chosen, or on the usual place: where GZJam has always put them, so accepting that is one
/// click and lands them where they've always gone.
/// </summary>
public sealed partial class AddPlaceViewModel : ObservableObject
{
    private readonly IReadOnlyList<string> _musicFolders;
    private readonly string _usual;
    private readonly bool _rememberedKeepFolders;

    /// <param name="heading">What's being asked: "Add 3 songs to My music?".</param>
    /// <param name="details">What will happen to them, wherever they go.</param>
    /// <param name="usual">Where they go unless the DJ says otherwise.</param>
    /// <param name="musicFolders">My music's folders, by path.</param>
    /// <param name="last">The folder chosen last time, if any.</param>
    /// <param name="keepFolders">Whether their folders came along last time.</param>
    /// <param name="canKeepFolders">Whether they have folders of their own to bring.</param>
    /// <param name="notThis">A folder that can't be chosen: the servers' downloads, whose songs GZJam can remove.</param>
    public AddPlaceViewModel(string heading, string details, string usual, IReadOnlyList<string> musicFolders, string? last, bool keepFolders, bool canKeepFolders, string? notThis = null)
    {
        Heading = heading;
        Details = details;
        _usual = usual;
        _musicFolders = musicFolders;
        _rememberedKeepFolders = keepFolders;
        CanKeepFolders = canKeepFolders;
        foreach (string folder in musicFolders)
            Roots.Add(new AddFolderRow(RootName(folder), folder, notThis));
        if (Roots.Count == 1)
            Roots[0].IsExpanded = true;
        UseUsualPlace();
        if (last is not null && IsInMyMusic(last) && Directory.Exists(last) && (notThis is null || !FileSources.IsIn(last, notThis)))
            Reveal(last);
    }

    public string Heading { get; }

    public string Details { get; }

    public ObservableCollection<AddFolderRow> Roots { get; } = [];

    public bool CanKeepFolders { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanMakeFolder))]
    public partial AddFolderRow? SelectedRow { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IntoText))]
    public partial string Folder { get; private set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanChooseKeepFolders))]
    [NotifyPropertyChangedFor(nameof(IntoNote))]
    public partial bool IsUsual { get; private set; }

    [ObservableProperty]
    public partial bool KeepFolders { get; set; }

    /// <summary>Why a folder couldn't be made; null if nothing's wrong.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProblem))]
    public partial string? Problem { get; private set; }

    public bool HasProblem => Problem is not null;

    public string IntoText => Display(Folder);

    public string IntoNote => IsUsual ? "The usual place" : "";

    public bool CanMakeFolder => SelectedRow is not null;

    public bool CanChooseKeepFolders => CanKeepFolders && !IsUsual;

    /// <summary>What was chosen.</summary>
    public AddPlace Result() => new(Folder, IsUsual || (CanKeepFolders && KeepFolders), IsUsual);

    public void UseUsualPlace()
    {
        SelectedRow = null;
        Folder = _usual;
        IsUsual = true;
        KeepFolders = true;
    }

    partial void OnSelectedRowChanged(AddFolderRow? value)
    {
        if (value is null || value.Path.Length == 0)
            return;
        Folder = value.Path;
        IsUsual = false;
        KeepFolders = _rememberedKeepFolders;
        Problem = null;
    }

    /// <summary>Makes a folder in the one selected, and selects it. If it's there already, it's simply selected.</summary>
    public void MakeFolder(string name)
    {
        if (SelectedRow is not { } parent)
            return;
        string path = System.IO.Path.Combine(parent.Path, FileSources.SafeName(name));
        try
        {
            Directory.CreateDirectory(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Info($"Add to my music: couldn't make {path}: {e.Message}");
            Problem = $"Couldn't make that folder: {e.Message}";
            return;
        }
        parent.ListAgain();
        parent.IsExpanded = true;
        SelectedRow = parent.Children.FirstOrDefault(c => string.Equals(c.Path, path, StringComparison.OrdinalIgnoreCase)) ?? parent;
    }

    private bool IsInMyMusic(string path) => _musicFolders.Any(f => FileSources.IsIn(path, f));

    /// <summary>Opens the folders down to one, and selects it.</summary>
    private void Reveal(string path)
    {
        AddFolderRow? row = Roots.Where(r => FileSources.IsIn(path, r.Path)).MaxBy(r => r.Path.Length);
        while (row is not null && !string.Equals(row.Path, path, StringComparison.OrdinalIgnoreCase))
        {
            row.IsExpanded = true;
            row = row.Children.FirstOrDefault(c => FileSources.IsIn(path, c.Path));
        }
        if (row is not null)
            SelectedRow = row;
    }

    private static string RootName(string folder) =>
        Path.GetFileName(Path.TrimEndingDirectorySeparator(folder)) is { Length: > 0 } name ? name : folder;

    /// <summary>"Music › Lindy › Slow": from its music folder's name, or the whole path if it's in none yet.</summary>
    private string Display(string path)
    {
        string? root = _musicFolders.Where(f => FileSources.IsIn(path, f)).MaxBy(f => f.Length);
        if (root is null)
            return path;
        string inside = Path.GetRelativePath(root, path);
        return inside == "." ? RootName(root)
            : string.Join(" › ", [RootName(root), .. inside.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)]);
    }
}
