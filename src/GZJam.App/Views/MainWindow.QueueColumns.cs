using Avalonia.Controls;

namespace GZJam.App.Views;

/// <summary>
/// Choosing what the queue shows of each song (#131): the queue's menu › Show. The title and
/// artist always show; the rest is ticked on or off, and remembered. Album, genre, year and when it
/// was last played start off: they're for a queue given the room in the middle (#178).
/// </summary>
public partial class MainWindow
{
    /// <summary>The queue's optional parts, by name (as kept in the settings) and as the menu says them.</summary>
    internal static readonly (string Name, string Header)[] QueueColumns =
    [
        ("Art", "Album art"), ("Rating", "♥ Rating"), ("Energy", "Energy"), ("Labels", "Labels"),
        ("Album", "Album"), ("Genre", "Genre"), ("Year", "Year"), ("Played", "Played"),
        ("BPM", "BPM"), ("Time", "Time"),
    ];

    private void InitializeQueueColumns() => DataContextChanged += (_, _) => ApplyQueueColumns();

    private void ApplyQueueColumns()
    {
        if (DataContext is null)
            return;
        foreach (var (name, _) in QueueColumns)
            QueueList.Classes.Set("hide-" + name.ToLowerInvariant(), Vm.HiddenQueueColumns.Contains(name));
    }

    private void FillQueueColumnsMenu()
    {
        QueueColumnsMenu.Items.Clear();
        foreach (var (name, header) in QueueColumns)
        {
            bool shown = !Vm.HiddenQueueColumns.Contains(name);
            var item = new MenuItem { Header = header, ToggleType = MenuItemToggleType.CheckBox, IsChecked = shown };
            item.Click += (_, _) => SetQueueColumnShown(name, !shown);
            QueueColumnsMenu.Items.Add(item);
        }
    }

    internal void SetQueueColumnShown(string name, bool shown)
    {
        Vm.HiddenQueueColumns.Remove(name);
        if (!shown)
            Vm.HiddenQueueColumns.Add(name);
        Log.Info($"Queue column {name}: {(shown ? "shown" : "hidden")}");
        ApplyQueueColumns();
    }
}
