using Avalonia.Controls;
using Avalonia.Interactivity;
using GZJam.App.ViewModels;

namespace GZJam.App.Views;

/// <summary>
/// The library and queue panels either side of the song list: dragged wider or narrower by their
/// dividers, or folded into a strip at their side. Dragged narrower than <see cref="FoldBelow"/>, a
/// panel folds; a tap anywhere on the strip opens it to its last width. At the screen's edge a
/// divider was hard to catch by touch, as the system takes swipes from the edge for itself.
/// The queue can also take the middle, with the song list narrowed beside it (#178).
/// </summary>
public partial class MainWindow
{
    /// <summary>A folded panel's strip.</summary>
    internal const double StripWidth = 34;

    /// <summary>Dragged narrower than this, a panel folds into its strip.</summary>
    internal const double FoldBelow = 120;

    /// <summary>With the queue wide, the song list is never narrower than this.</summary>
    internal const double LeastSongListWidth = 240;

    /// <summary>A queue panel narrower than this has its tabs closer together, to fit (#187).</summary>
    internal const double NarrowQueue = 175;

    private const double DefaultLibraryWidth = 260, DefaultQueueWidth = 320, MostPanelWidth = 560, DefaultWideSongListWidth = 420;

    private void InitializePanes()
    {
        DataContextChanged += (_, _) => ApplyPanes();
        TreeSplitter.DragCompleted += (_, _) => PanelDragged(library: true);
        QueueSplitter.DragCompleted += (_, _) => PanelDragged(library: false);
        QueuePanel.SizeChanged += (_, e) => QueuePanel.Classes.Set("narrow", e.NewSize.Width < NarrowQueue);
    }

    private void OnLibraryStripClick(object? sender, RoutedEventArgs e) => FoldPanel(library: true, folded: false);
    private void OnQueueStripClick(object? sender, RoutedEventArgs e) => FoldPanel(library: false, folded: false);
    private void OnFoldLibraryClick(object? sender, RoutedEventArgs e) => FoldPanel(library: true, folded: true);
    private void OnFoldQueueClick(object? sender, RoutedEventArgs e) => FoldPanel(library: false, folded: true);
    private void OnQueueWideClick(object? sender, RoutedEventArgs e) => ToggleQueueWide();

    internal bool IsLibraryFolded => DataContext is MainWindowViewModel vm && vm.PanelSettings.LibraryPanelFolded;
    internal bool IsQueueFolded => DataContext is MainWindowViewModel vm && vm.PanelSettings.QueuePanelFolded;
    internal bool IsQueueWide => DataContext is MainWindowViewModel vm && vm.PanelSettings.QueueWide;

    private void FoldPanel(bool library, bool folded)
    {
        AppSettings settings = Vm.PanelSettings;
        if (library)
            settings.LibraryPanelFolded = folded;
        else
        {
            settings.QueuePanelFolded = folded;
            if (folded)
                settings.QueueWide = false;   // folded away, the song list has the room again
        }
        Log.Info($"{(library ? "Library" : "Queue")} panel {(folded ? "folded" : "opened")}");
        ApplyPanes();
    }

    /// <summary>
    /// The queue takes the middle and the song list narrows beside it, or back (#178). Each keeps
    /// its own divider: the song list's width here, the queue's width the other way.
    /// </summary>
    internal void ToggleQueueWide()
    {
        AppSettings settings = Vm.PanelSettings;
        settings.QueueWide = !settings.QueueWide;
        if (settings.QueueWide)
            settings.QueuePanelFolded = false;
        Log.Info(settings.QueueWide ? "Queue wide" : "Song list wide");
        ApplyPanes();
    }

    /// <summary>A divider let go: narrower than <see cref="FoldBelow"/>, the panel folds and keeps its width from before.</summary>
    private void PanelDragged(bool library)
    {
        AppSettings settings = Vm.PanelSettings;
        double width = PanesGrid.ColumnDefinitions[library ? 0 : 4].ActualWidth;
        if (width < FoldBelow)
        {
            FoldPanel(library, folded: true);
            return;
        }
        if (library)
            settings.LibraryPanelWidth = width;
        else if (!settings.QueueWide)
            settings.QueuePanelWidth = width;
        if (settings.QueueWide)
            settings.WideSongListWidth = PanesGrid.ColumnDefinitions[2].ActualWidth;
    }

    private void ApplyPanes()
    {
        if (DataContext is not MainWindowViewModel vm)
            return;
        AppSettings settings = vm.PanelSettings;
        bool wide = settings.QueueWide && !settings.QueuePanelFolded;
        vm.QueuePanelOpen = !settings.QueuePanelFolded;
        ApplyPane(0, LibraryPanel, LibraryStrip, TreeSplitter, TreeSplitterGrip, settings.LibraryPanelFolded,
            settings.LibraryPanelWidth ?? DefaultLibraryWidth);
        ApplyPane(4, QueuePanel, QueueStrip, QueueSplitter, QueueSplitterGrip, settings.QueuePanelFolded,
            settings.QueuePanelWidth ?? DefaultQueueWidth, stretch: wide);

        // Wide, the queue takes what's left and the song list keeps a width of its own.
        ColumnDefinition songs = PanesGrid.ColumnDefinitions[2];
        songs.MinWidth = wide ? LeastSongListWidth : 0;
        songs.Width = wide ? new GridLength(Math.Max(settings.WideSongListWidth ?? DefaultWideSongListWidth, LeastSongListWidth)) : GridLength.Star;
        QueueWideButton.Classes.Set("wide", wide);
        ToolTip.SetTip(QueueWideButton, wide
            ? $"Give the song list the room ({Shortcuts.Command}+L)"
            : $"Give the queue the room, for a long queue ({Shortcuts.Command}+L)");
    }

    private void ApplyPane(int column, Control panel, Control strip, Control splitter, Control grip, bool folded, double width, bool stretch = false)
    {
        ColumnDefinition definition = PanesGrid.ColumnDefinitions[column];
        definition.MaxWidth = folded ? StripWidth : stretch ? double.PositiveInfinity : MostPanelWidth;
        definition.Width = folded ? new GridLength(StripWidth)
            : stretch ? GridLength.Star
            : new GridLength(Math.Clamp(width, FoldBelow, MostPanelWidth));
        panel.IsVisible = !folded;
        strip.IsVisible = folded;
        // Folded, there's nothing to drag: the strip opens it.
        splitter.IsVisible = grip.IsVisible = !folded;
    }
}
