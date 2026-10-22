using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.VisualTree;
using GZJam.App.ViewModels;

namespace GZJam.App.Tests;

/// <summary>Opening folders in the library's tree with one click or tap.</summary>
public sealed class FolderTreeTests
{
    private static FolderNodeViewModel Folder(AppHarness app, string name) =>
        app.Library.Roots.SelectMany(r => r.SelfAndDescendants()).First(n => n.Name == name);

    private static Control Part(AppHarness app, FolderNodeViewModel folder, string part)
    {
        var tree = app.Window.FindControl<TreeView>("FolderTree")!;
        var item = (TreeViewItem)tree.TreeContainerFromItem(folder)!;
        return item.GetVisualDescendants().OfType<Control>().First(c => c.Name == part);
    }

    /// <summary>The middle of the folder's name.</summary>
    private static Point NameOf(AppHarness app, FolderNodeViewModel folder)
    {
        var name = Part(app, folder, "PART_HeaderPresenter");
        return name.TranslatePoint(new Point(30, name.Bounds.Height / 2), app.Window)!.Value;
    }

    private static void Click(AppHarness app, Point at)
    {
        app.Window.MouseDown(at, MouseButton.Left, RawInputModifiers.None);
        app.Window.MouseUp(at, MouseButton.Left, RawInputModifiers.None);
        AppHarness.Settle(100);
    }

    private static void Tap(AppHarness app, Point at)
    {
        var finger = app.Window.TouchBegin(at, RawInputModifiers.None);
        AppHarness.Settle(40);
        app.Window.TouchEnd(finger, at, RawInputModifiers.None);
        AppHarness.Settle(100);
    }

    [AvaloniaFact]
    public void One_click_on_a_folder_opens_it_and_a_click_on_the_chosen_folder_closes_it()
    {
        using var app = new AppHarness();
        var swing = Folder(app, "Swing");
        Assert.False(swing.IsExpanded);

        Click(app, NameOf(app, swing));
        Assert.Same(swing, app.Library.SelectedFolder);
        Assert.True(swing.IsExpanded);

        AppHarness.Settle(700);   // a later click, not the second half of a double-click
        Click(app, NameOf(app, swing));
        Assert.False(swing.IsExpanded);
        Assert.Same(swing, app.Library.SelectedFolder);
    }

    [AvaloniaFact]
    public void A_double_click_leaves_the_folder_open()
    {
        using var app = new AppHarness();
        var blues = Folder(app, "Blues");
        var at = NameOf(app, blues);

        app.Window.MouseDown(at, MouseButton.Left, RawInputModifiers.None);
        app.Window.MouseUp(at, MouseButton.Left, RawInputModifiers.None);
        app.Window.MouseDown(at, MouseButton.Left, RawInputModifiers.None);
        app.Window.MouseUp(at, MouseButton.Left, RawInputModifiers.None);
        AppHarness.Settle(100);

        Assert.True(blues.IsExpanded);
    }

    [AvaloniaFact]
    public void A_finger_tap_opens_a_folder()
    {
        using var app = new AppHarness();
        var swing = Folder(app, "Swing");

        Tap(app, NameOf(app, swing));
        Assert.True(swing.IsExpanded);
        Assert.Same(swing, app.Library.SelectedFolder);

        var lindy = Folder(app, "Lindy Hop");
        Tap(app, NameOf(app, lindy));
        Assert.True(lindy.IsExpanded);
        Assert.True(swing.IsExpanded);   // the tap was the subfolder's, not its parent's
        Assert.Same(lindy, app.Library.SelectedFolder);
    }

    [AvaloniaFact]
    public void The_space_before_a_folders_name_opens_and_closes_it_without_choosing_it()
    {
        using var app = new AppHarness();
        var swing = Folder(app, "Swing");
        var chosen = app.Library.SelectedFolder;
        var arrow = Part(app, swing, "PART_ExpandCollapseChevronContainer");
        Assert.True(arrow.Bounds.Width >= 32);
        // Near the edge of the space, well away from where the small arrow is drawn.
        var edge = arrow.TranslatePoint(new Point(3, arrow.Bounds.Height - 3), app.Window)!.Value;

        Click(app, edge);
        Assert.True(swing.IsExpanded);
        Assert.Same(chosen, app.Library.SelectedFolder);

        AppHarness.Settle(700);
        Tap(app, edge);
        Assert.False(swing.IsExpanded);
        Assert.Same(chosen, app.Library.SelectedFolder);
    }
}
