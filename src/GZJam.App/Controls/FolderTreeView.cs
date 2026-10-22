using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace GZJam.App.Controls;

/// <summary>
/// The library's folder tree. One click or tap on a folder opens it as well as choosing it; a
/// click on the folder already chosen closes it again. (The standard tree only opens on a
/// double-click or on its small arrow, so opening a folder often took several tries.)
/// </summary>
public class FolderTreeView : TreeView
{
    protected override Type StyleKeyOverride => typeof(TreeView);

    protected override Control CreateContainerForItemOverride(object? item, int index, object? recycleKey) => new FolderTreeViewItem();

    protected override bool NeedsContainerOverride(object? item, int index, out object? recycleKey) =>
        NeedsContainer<FolderTreeViewItem>(item, out recycleKey);
}

public class FolderTreeViewItem : TreeViewItem
{
    /// <summary>Whether this folder was already the chosen one when it was pressed.</summary>
    private bool _wasSelected;

    public FolderTreeViewItem()
    {
        AddHandler(TappedEvent, OnTapped);
    }

    protected override Type StyleKeyOverride => typeof(TreeViewItem);

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (IsOwnHeader(e.Source))
            _wasSelected = IsSelected;
        base.OnPointerPressed(e);   // chooses the folder
    }

    private void OnTapped(object? sender, TappedEventArgs e)
    {
        // The arrow opens and closes by itself; a tap on a subfolder is that subfolder's.
        if (e.Handled || !IsOwnHeader(e.Source) || (e.Source as Visual)?.FindAncestorOfType<ToggleButton>(includeSelf: true) is not null)
            return;
        if (ItemCount > 0)
            SetCurrentValue(IsExpandedProperty, !_wasSelected || !IsExpanded);
        e.Handled = true;
    }

    /// <summary>The first click opened the folder; the second of a double-click shouldn't close it.</summary>
    protected override void OnHeaderDoubleTapped(TappedEventArgs e) => e.Handled = true;

    private bool IsOwnHeader(object? source) =>
        (source as Visual)?.FindAncestorOfType<TreeViewItem>(includeSelf: true) == this;
}
