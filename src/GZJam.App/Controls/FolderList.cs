using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.VisualTree;
using GZJam.App.ViewModels;

namespace GZJam.App.Controls;

/// <summary>
/// The library's folder tree, shown as one list of rows (see <see cref="LibraryViewModel.FolderRows"/>):
/// each row indented for its place, with an arrow for the folders in it. One click or tap on a
/// folder opens it as well as choosing it; a click on the folder already chosen closes it again.
/// The arrow keys go up and down the rows, right opens a folder (then goes into it), left closes
/// it (then goes up to the folder it's in), as in a tree.
/// </summary>
public class FolderList : ListBox
{
    protected override Type StyleKeyOverride => typeof(ListBox);

    protected override Control CreateContainerForItemOverride(object? item, int index, object? recycleKey) => new FolderRow();

    protected override bool NeedsContainerOverride(object? item, int index, out object? recycleKey) =>
        NeedsContainer<FolderRow>(item, out recycleKey);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (SelectedItem is FolderNodeViewModel folder && e.KeyModifiers == KeyModifiers.None)
        {
            FolderNodeViewModel? go = null;
            if (e.Key == Key.Right && folder.HasChildren)
            {
                if (!folder.IsExpanded)
                    folder.IsExpanded = true;
                else
                    go = folder.Children[0];
                e.Handled = true;
            }
            else if (e.Key == Key.Left)
            {
                if (folder.IsExpanded)
                    folder.IsExpanded = false;
                else
                    go = folder.Parent;
                e.Handled = true;
            }
            if (go is not null)
                Go(go);
            if (e.Handled)
                return;
        }
        base.OnKeyDown(e);
    }

    /// <summary>Chooses a folder's row, scrolled to, with the keyboard's focus on it.</summary>
    private void Go(FolderNodeViewModel folder)
    {
        SelectedItem = folder;
        ScrollIntoView(folder);
        ContainerFromItem(folder)?.Focus(NavigationMethod.Directional);
    }
}

/// <summary>A row of the folder list: a folder, its arrow, its name and its count.</summary>
public class FolderRow : ListBoxItem
{
    /// <summary>Whether this folder was already the chosen one when it was pressed.</summary>
    private bool _wasSelected;

    public FolderRow()
    {
        Classes.Add("folder-row");
        AddHandler(TappedEvent, OnTapped);
    }

    protected override Type StyleKeyOverride => typeof(ListBoxItem);

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        _wasSelected = IsSelected;
        base.OnPointerPressed(e);   // chooses the folder
    }

    private void OnTapped(object? sender, TappedEventArgs e)
    {
        // The arrow opens and closes by itself. (The second click of a double-click is no tap.)
        if (e.Handled || DataContext is not FolderNodeViewModel { HasChildren: true } folder
            || (e.Source as Visual)?.FindAncestorOfType<ToggleButton>(includeSelf: true) is not null)
            return;
        folder.IsExpanded = !_wasSelected || !folder.IsExpanded;
        e.Handled = true;
    }
}
