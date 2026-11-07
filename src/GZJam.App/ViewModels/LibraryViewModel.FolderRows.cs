using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace GZJam.App.ViewModels;

/// <summary>
/// The folder tree as the window shows it: one list of rows, each folder whose folders above it
/// are all open, in order, indented for its place. One list, so one panel makes rows only for
/// the folders in view. The tree's own rows each had a panel of their own for the folders in
/// them (a server can have thousands in one folder), and those panels, one inside another,
/// guessed the heights of rows they hadn't made: a gap under an opened folder, and the whole
/// tree blank while scrolling, until scrolled back.
/// </summary>
public sealed partial class LibraryViewModel
{
    /// <summary>The tree's rows: see the class remarks. Follows <see cref="Roots"/> and each folder opened or closed.</summary>
    public FolderRowList FolderRows { get; } = new();

    /// <summary>While the tree is being made again: its rows follow once, at the end.</summary>
    private int _holdRows;

    /// <summary>
    /// The chosen folder, as the rows show it: none while it's inside a closed folder, chosen
    /// again once that opens. The list taking a row away doesn't unchoose the folder.
    /// </summary>
    public FolderNodeViewModel? SelectedFolderRow
    {
        get => SelectedFolder is { } folder && folder.SelfAndAncestors().Skip(1).All(a => a.IsExpanded) && IsInTree(folder) ? folder : null;
        set
        {
            if (value is not null && !ReferenceEquals(value, SelectedFolder))
                SelectedFolder = value;
        }
    }

    private bool IsInTree(FolderNodeViewModel folder)
    {
        FolderNodeViewModel root = folder.SelfAndAncestors().Last();
        return Roots.Contains(root);
    }

    /// <summary>A folder was opened or closed: its folders come or go.</summary>
    internal void FolderToggled()
    {
        if (_holdRows == 0)
            SyncFolderRows();
    }

    private void HoldRows() => _holdRows++;

    private void ReleaseRows()
    {
        if (--_holdRows == 0)
            SyncFolderRows();
    }

    private void OnRootsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_holdRows == 0)
            SyncFolderRows();
    }

    /// <summary>
    /// The rows made to match the tree: only the stretch that changed is taken out and put in,
    /// so the rows around it, the place scrolled to and the chosen row stay as they were.
    /// </summary>
    private void SyncFolderRows()
    {
        var want = new List<FolderNodeViewModel>(FolderRows.Count + 16);
        foreach (FolderNodeViewModel root in Roots)
            AddRows(want, root);

        int start = 0;
        while (start < want.Count && start < FolderRows.Count && ReferenceEquals(want[start], FolderRows[start]))
            start++;
        int endWant = want.Count, endHave = FolderRows.Count;
        while (endWant > start && endHave > start && ReferenceEquals(want[endWant - 1], FolderRows[endHave - 1]))
        {
            endWant--;
            endHave--;
        }
        if (endHave == start && endWant == start)
            return;
        FolderRows.Replace(start, endHave - start, want.GetRange(start, endWant - start));
        OnPropertyChanged(nameof(SelectedFolderRow));
    }

    private static void AddRows(List<FolderNodeViewModel> rows, FolderNodeViewModel node)
    {
        rows.Add(node);
        if (node.IsExpanded)
            foreach (FolderNodeViewModel child in node.Children)
                AddRows(rows, child);
    }
}

/// <summary>The folder tree's rows, changed a stretch at a time: a server's thousands of folders come and go as one change, not thousands.</summary>
public sealed class FolderRowList : ObservableCollection<FolderNodeViewModel>
{
    /// <summary>Takes <paramref name="remove"/> rows out at <paramref name="index"/>, then puts <paramref name="rows"/> in there.</summary>
    public void Replace(int index, int remove, IReadOnlyList<FolderNodeViewModel> rows)
    {
        CheckReentrancy();
        var items = (List<FolderNodeViewModel>)Items;
        if (remove > 0)
        {
            var old = items.GetRange(index, remove);
            items.RemoveRange(index, remove);
            Changed(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, old, index));
        }
        if (rows.Count > 0)
        {
            items.InsertRange(index, rows);
            Changed(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, rows.ToList(), index));
        }
    }

    private void Changed(NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(e);
    }
}
