using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.VisualTree;
using GZJam.App.ViewModels;
using GZJam.Testing;

namespace GZJam.App.Tests;

/// <summary>Opening folders in the library's tree with one click or tap.</summary>
public sealed class FolderTreeTests
{
    private static FolderNodeViewModel Folder(AppHarness app, string name) =>
        app.Library.Roots.SelectMany(r => r.SelfAndDescendants()).First(n => n.Name == name);

    private static Control Row(AppHarness app, FolderNodeViewModel folder)
    {
        var tree = app.Window.FindControl<ListBox>("FolderTree")!;
        return tree.ContainerFromItem(folder)!;
    }

    /// <summary>The space before the folder's name, its arrow's.</summary>
    private static Control Arrow(AppHarness app, FolderNodeViewModel folder) =>
        Row(app, folder).GetVisualDescendants().OfType<Panel>().First(c => c.Classes.Contains("folder-arrow"));

    /// <summary>The middle of the folder's name.</summary>
    private static Point NameOf(AppHarness app, FolderNodeViewModel folder)
    {
        var name = Row(app, folder).GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == folder.Name);
        return name.TranslatePoint(new Point(Math.Min(30, name.Bounds.Width / 2), name.Bounds.Height / 2), app.Window)!.Value;
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
        var arrow = Arrow(app, swing);
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

    [AvaloniaFact]
    public void The_arrow_keys_open_close_and_go_through_the_folders_as_in_a_tree()
    {
        using var app = new AppHarness();
        var swing = Folder(app, "Swing");
        Click(app, NameOf(app, swing));
        AppHarness.Settle(700);
        Click(app, NameOf(app, swing));   // chosen, and closed again
        Assert.False(swing.IsExpanded);

        app.Window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
        AppHarness.Settle(50);
        Assert.True(swing.IsExpanded);

        app.Window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
        AppHarness.Settle(50);
        Assert.Same(swing.Children[0], app.Library.SelectedFolder);

        app.Window.KeyPress(Key.Left, RawInputModifiers.None, PhysicalKey.ArrowLeft, null);
        AppHarness.Settle(50);
        Assert.Same(swing, app.Library.SelectedFolder);

        app.Window.KeyPress(Key.Left, RawInputModifiers.None, PhysicalKey.ArrowLeft, null);
        AppHarness.Settle(50);
        Assert.False(swing.IsExpanded);
    }

    [AvaloniaFact]
    public void A_closed_folder_keeps_the_folder_chosen_in_it_and_shows_it_chosen_again_once_opened()
    {
        using var app = new AppHarness();
        var swing = Folder(app, "Swing");
        var lindy = Folder(app, "Lindy Hop");
        swing.IsExpanded = true;
        app.Library.SelectedFolder = lindy;
        AppHarness.Settle(100);
        var tree = app.Window.FindControl<ListBox>("FolderTree")!;
        Assert.Same(lindy, tree.SelectedItem);

        swing.IsExpanded = false;
        AppHarness.Settle(100);
        Assert.Same(lindy, app.Library.SelectedFolder);   // its songs still showing
        Assert.DoesNotContain(lindy, app.Library.FolderRows);

        swing.IsExpanded = true;
        AppHarness.Settle(100);
        Assert.Same(lindy, tree.SelectedItem);
        app.Screenshot("folder-rows-chosen");
    }
    /// <summary>
    /// Opening a folder by its arrow, above the chosen folder (scrolled away from it), leaves the
    /// tree where it was (#413): it used to jump to the chosen folder, whose place in the list the
    /// new rows had moved.
    /// </summary>
    [AvaloniaFact]
    public void Opening_a_folder_above_the_chosen_one_keeps_the_tree_where_it_was()
    {
        string music = Directory.CreateTempSubdirectory("gzjam-tree-").FullName;
        for (int i = 1; i <= 40; i++)
            TestMp3.Write(Path.Combine(music, $"Band {i:00}", "Album", "01 - Song.mp3"));
        using var app = new AppHarness(musicFolder: music);
        var tree = app.Window.FindControl<ListBox>("FolderTree")!;
        var root = app.Library.Roots[0];
        root.IsExpanded = true;
        AppHarness.Settle(100);
        app.Library.SelectedFolder = Folder(app, "Band 35");
        AppHarness.Settle(200);
        var scroller = tree.GetVisualDescendants().OfType<ScrollViewer>().First();
        Assert.True(scroller.Offset.Y > 0);   // the chosen folder is far enough down to scroll to

        scroller.Offset = default;
        AppHarness.Settle(200);
        var band = Folder(app, "Band 02");
        double before = Row(app, band).TranslatePoint(default, tree)!.Value.Y;
        Click(app, Arrow(app, band).TranslatePoint(new Point(3, 3), app.Window)!.Value);
        AppHarness.Settle(200);

        Assert.True(band.IsExpanded);
        Assert.Same(Folder(app, "Band 35"), app.Library.SelectedFolder);
        Assert.Equal(0, scroller.Offset.Y);
        Assert.Equal(before, Row(app, band).TranslatePoint(default, tree)!.Value.Y);
    }
}
