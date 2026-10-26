using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using GZJam.App.ViewModels;
using GZJam.Library;

namespace GZJam.App.Views;

/// <summary>The Queue / History tabs and the History list: preview, re-queue, load to a deck, drag out; name, export or delete a session.</summary>
public partial class MainWindow
{
    private DragStarter? _historyDrag;

    private void InitializeHistory()
    {
        HistoryList.AddHandler(PointerPressedEvent, OnHistoryPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        _historyDrag = new DragStarter(this, HistoryList, item => DragFromHistory((PlayItem)item), HistoryMenu);
        HistoryMenu.Opening += (_, _) => UpdateLoadMenuItems([HistoryLoad1, HistoryLoad2, HistoryLoad3, HistoryLoad4], SelectedPlays().Count == 1);
        HistoryMenu.Opened += (_, _) => UpdateLoadMenuItems([HistoryLoad1, HistoryLoad2, HistoryLoad3, HistoryLoad4], SelectedPlays().Count == 1);
        NameSessionFlyout.Opened += (_, _) =>
        {
            SessionNameBox.Text = Vm.History.SelectedName ?? "";
            SessionNameBox.Focus();
            SessionNameBox.SelectAll();
        };
    }

    private void SaveSessionName(string? name)
    {
        Vm.History.RenameSelected(name);
        NameSessionFlyout.Hide();
    }

    private FlyoutBase NameSessionFlyout => FlyoutBase.GetAttachedFlyout(SessionMenuButton)!;

    private void OnNameSessionClick(object? sender, RoutedEventArgs e) => FlyoutBase.ShowAttachedFlyout(SessionMenuButton);

    /// <summary>Saves the session's set list where the DJ picks: a readable .txt, or .csv for a spreadsheet.</summary>
    private async void OnExportSetListClick(object? sender, RoutedEventArgs e)
    {
        if (Vm.History.SetListForSelected() is not { } setList)
            return;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export set list",
            SuggestedFileName = setList.FileName,
            DefaultExtension = "txt",
            ShowOverwritePrompt = true,
            FileTypeChoices =
            [
                new FilePickerFileType("Set list (text)") { Patterns = ["*.txt"] },
                new FilePickerFileType("Spreadsheet (CSV)") { Patterns = ["*.csv"] },
            ],
        });
        if (file is null)
            return;
        bool csv = file.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase);
        try
        {
            await using var stream = await file.OpenWriteAsync();
            // CSV with a byte-order mark, so Excel reads accented names correctly.
            await using var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: csv));
            await writer.WriteAsync(csv ? setList.Csv : setList.Text);
            Log.Info($"History: exported the set list to {file.Path}");
            Vm.ShowNotice($"Set list saved: {file.Name}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error($"Couldn't export the set list to {file.Path}", ex);
            Vm.ShowNotice($"Couldn't save the set list: {ex.Message}");
        }
    }

    private async void OnDeleteSessionClick(object? sender, RoutedEventArgs e)
    {
        if (Vm.History.SelectedSession is not { Session: { } session } choice || Vm.ConfirmAsync is null)
            return;
        string songs = session.PlayCount == 1 ? "its song" : $"its {session.PlayCount} songs";
        if (await Vm.ConfirmAsync($"Delete \"{choice.Label}\"?",
                $"The session and {songs} leave History, and the Played column forgets them. Your music files aren't touched.",
                "Delete"))
            Vm.History.DeleteSelected();
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
        Vm.AddToQueue(SelectedPlays().Select(p => p.Track).ToList());

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
