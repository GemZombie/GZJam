using Avalonia.Controls;
using Avalonia.Interactivity;
using GZJam.App.ViewModels;

namespace GZJam.App.Views;

/// <summary>
/// The library and queue panels either side of the song list: dragged wider or narrower by their
/// dividers, or folded into a strip at their side. Dragged narrower than <see cref="FoldBelow"/>, a
/// panel folds; a tap anywhere on the strip opens it to its last width. At the screen's edge a
/// divider was hard to catch by touch, as the system takes swipes from the edge for itself.
/// </summary>
public partial class MainWindow
{
    /// <summary>A folded panel's strip.</summary>
    internal const double StripWidth = 34;

    /// <summary>Dragged narrower than this, a panel folds into its strip.</summary>
    internal const double FoldBelow = 120;

    private const double DefaultLibraryWidth = 260, DefaultQueueWidth = 320, MostPanelWidth = 560;

    private void InitializePanes()
    {
        DataContextChanged += (_, _) => ApplyPanes();
        TreeSplitter.DragCompleted += (_, _) => PanelDragged(library: true);
        QueueSplitter.DragCompleted += (_, _) => PanelDragged(library: false);
    }

    private void OnLibraryStripClick(object? sender, RoutedEventArgs e) => FoldPanel(library: true, folded: false);
    private void OnQueueStripClick(object? sender, RoutedEventArgs e) => FoldPanel(library: false, folded: false);
    private void OnFoldLibraryClick(object? sender, RoutedEventArgs e) => FoldPanel(library: true, folded: true);
    private void OnFoldQueueClick(object? sender, RoutedEventArgs e) => FoldPanel(library: false, folded: true);

    internal bool IsLibraryFolded => DataContext is MainWindowViewModel vm && vm.PanelSettings.LibraryPanelFolded;
    internal bool IsQueueFolded => DataContext is MainWindowViewModel vm && vm.PanelSettings.QueuePanelFolded;

    private void FoldPanel(bool library, bool folded)
    {
        AppSettings settings = Vm.PanelSettings;
        if (library)
            settings.LibraryPanelFolded = folded;
        else
            settings.QueuePanelFolded = folded;
        Log.Info($"{(library ? "Library" : "Queue")} panel {(folded ? "folded" : "opened")}");
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
        else
            settings.QueuePanelWidth = width;
    }

    private void ApplyPanes()
    {
        if (DataContext is not MainWindowViewModel vm)
            return;
        AppSettings settings = vm.PanelSettings;
        ApplyPane(0, LibraryPanel, LibraryStrip, TreeSplitter, TreeSplitterGrip, settings.LibraryPanelFolded,
            settings.LibraryPanelWidth ?? DefaultLibraryWidth);
        ApplyPane(4, QueuePanel, QueueStrip, QueueSplitter, QueueSplitterGrip, settings.QueuePanelFolded,
            settings.QueuePanelWidth ?? DefaultQueueWidth);
    }

    private void ApplyPane(int column, Control panel, Control strip, Control splitter, Control grip, bool folded, double width)
    {
        ColumnDefinition definition = PanesGrid.ColumnDefinitions[column];
        definition.MaxWidth = folded ? StripWidth : MostPanelWidth;
        definition.Width = new GridLength(folded ? StripWidth : Math.Clamp(width, FoldBelow, MostPanelWidth));
        panel.IsVisible = !folded;
        strip.IsVisible = folded;
        // Folded, there's nothing to drag: the strip opens it.
        splitter.IsVisible = grip.IsVisible = !folded;
    }
}
