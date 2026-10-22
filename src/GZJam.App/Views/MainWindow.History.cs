using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using GZJam.App.ViewModels;
using GZJam.Library;

namespace GZJam.App.Views;

/// <summary>The Queue / History tabs and the History list: preview, re-queue, load to a deck, drag out.</summary>
public partial class MainWindow
{
    private DragStarter? _historyDrag;

    private void InitializeHistory()
    {
        HistoryList.AddHandler(PointerPressedEvent, OnHistoryPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        _historyDrag = new DragStarter(this, HistoryList, item => DragFromHistory((PlayItem)item), HistoryMenu);
        HistoryMenu.Opening += (_, _) => UpdateLoadMenuItems([HistoryLoad1, HistoryLoad2, HistoryLoad3, HistoryLoad4], SelectedPlays().Count == 1);
        HistoryMenu.Opened += (_, _) => UpdateLoadMenuItems([HistoryLoad1, HistoryLoad2, HistoryLoad3, HistoryLoad4], SelectedPlays().Count == 1);
        RenameSessionButton.Flyout!.Opened += (_, _) =>
        {
            SessionNameBox.Text = Vm.History.SelectedName ?? "";
            SessionNameBox.Focus();
            SessionNameBox.SelectAll();
        };
    }

    private void SaveSessionName(string? name)
    {
        Vm.History.RenameSelected(name);
        RenameSessionButton.Flyout!.Hide();
    }

    private void OnSessionNameSaveClick(object? sender, RoutedEventArgs e) => SaveSessionName(SessionNameBox.Text);

    private void OnSessionNameClearClick(object? sender, RoutedEventArgs e) => SaveSessionName(null);

    private void OnSessionNameKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            SaveSessionName(SessionNameBox.Text);
            e.Handled = true;
        }
    }

    private List<PlayItem> SelectedPlays() => HistoryList.SelectedItems?.OfType<PlayItem>().ToList() ?? [];

    private void OnQueueTabClick(object? sender, RoutedEventArgs e) => Vm.ShowHistory = false;

    private void OnHistoryTabClick(object? sender, RoutedEventArgs e) => Vm.ShowHistory = true;

    // Dragging a history entry works like a library track: onto a deck or into the queue (copy).

    private void OnHistoryPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if ((e.Source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true) is not { DataContext: PlayItem item })
            return;
        var point = e.GetCurrentPoint(HistoryList);
        if (point.Properties.IsRightButtonPressed)
        {
            if (HistoryList.SelectedItems?.Contains(item) != true)
                HistoryList.SelectedItem = item;
        }
        else if (point.Properties.IsLeftButtonPressed)
        {
            _historyDrag?.Pressed(e, item);
        }
    }

    /// <summary>Dragging a history row drags the selection if the row is in it, else that row alone.</summary>
    private TrackDrag DragFromHistory(PlayItem pressed)
    {
        var plays = SelectedPlays();
        if (!plays.Contains(pressed))
        {
            HistoryList.SelectedItems?.Clear();
            HistoryList.SelectedItem = pressed;
            plays = [pressed];
        }
        return new TrackDrag(plays.Select(p => p.Track).ToList(), null);
    }

    private void OnPreviewHistoryItemClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is PlayItem item)
            Vm.Preview?.Toggle(item.Track);
    }

    private void OnHistoryPreviewClick(object? sender, RoutedEventArgs e)
    {
        if (SelectedPlays().FirstOrDefault() is { } item)
            Vm.Preview?.Toggle(item.Track);
    }

    private void OnHistoryAddToQueueClick(object? sender, RoutedEventArgs e) =>
        Vm.Queue.Add(SelectedPlays().Select(p => p.Track));

    private async void OnHistoryLoadToDeckClick(object? sender, RoutedEventArgs e)
    {
        if (SelectedPlays() is [var item] && DeckAt(int.Parse((string)((MenuItem)sender!).Tag!)) is { } deck)
            await Vm.LoadToDeckAsync(deck, item.Track);
    }

    private void OnHistoryShowInTreeClick(object? sender, RoutedEventArgs e)
    {
        if (SelectedPlays().FirstOrDefault() is not { } item)
            return;
        if (item.Track.Id == 0)
        {
            Vm.ShowNotice($"\"{item.Title}\" is no longer in the library.");
            return;
        }
        Vm.Library.RevealTrack(item.Track);
    }
}
