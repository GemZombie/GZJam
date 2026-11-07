using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
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
        // Its styles go by its own class, not "TreeView.folders TreeViewItem": a rule that looks
        // at the tree above it keeps the row listening to the tree, and rows thrown away with
        // the tree stayed in memory (#286).
        Classes.Add("folder-row");
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

    /// <summary>The tree's scroll area, which keeps the rows in view as candidates to anchor its scrolling to.</summary>
    private IScrollAnchorProvider? _anchors;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _anchors = this.FindAncestorOfType<ScrollContentPresenter>();
    }

    /// <summary>
    /// A row taken out of the tree leaves the scroll area's anchor candidates. The panels that
    /// show a folder's subfolders register their rows there, but rows thrown away with the tree
    /// (made again after each scan: with each song a sync brings down) were never taken out, so
    /// every old tree stayed in memory, the server's thousands of folders and their menus with it:
    /// 2.4 GB after a few hours of syncing, and the window stuttered as .NET went through it all.
    /// </summary>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _anchors?.UnregisterAnchorCandidate(this);
        _anchors = null;
    }

    /// <summary>The first click opened the folder; the second of a double-click shouldn't close it.</summary>
    protected override void OnHeaderDoubleTapped(TappedEventArgs e) => e.Handled = true;

    private bool IsOwnHeader(object? source) =>
        (source as Visual)?.FindAncestorOfType<TreeViewItem>(includeSelf: true) == this;
}
