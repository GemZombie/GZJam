using Avalonia.Controls;
using GZJam.App.ViewModels;
using GZJam.Library;

namespace GZJam.App.Views;

/// <summary>
/// "Detect BPM in range": Automatic plus the ranges, with the tracks' current choice ticked.
/// Used in the library and queue menus and on the decks' BPM.
/// </summary>
internal static class BpmMenu
{
    public const string Title = "Detect BPM in range";

    /// <summary>Replaces <paramref name="menu"/>'s items. <paramref name="withTitle"/> adds a heading (for a menu of its own).</summary>
    public static void Fill(ItemsControl menu, MainWindowViewModel vm, IReadOnlyList<Track> tracks, bool withTitle = false)
    {
        menu.Items.Clear();
        bool enabled = tracks.Count > 0 && vm.Analysis is not null;
        var chosen = tracks.Select(t => t.BpmRange).Distinct().ToList();
        BpmRange? current = chosen.Count == 1 ? chosen[0] : null;
        bool mixed = chosen.Count != 1;

        if (withTitle)
            menu.Items.Add(new MenuItem { Header = Title, IsEnabled = false });
        menu.Items.Add(Item("Automatic", null, !mixed && current is null));
        menu.Items.Add(new Separator());
        foreach (var (range, label) in MainWindowViewModel.BpmRanges)
            menu.Items.Add(Item(label, range, !mixed && current == range));

        MenuItem Item(string header, BpmRange? range, bool isChecked)
        {
            var item = new MenuItem
            {
                Header = header,
                ToggleType = MenuItemToggleType.Radio,
                GroupName = "bpm-range",
                IsChecked = isChecked,
                IsEnabled = enabled,
            };
            item.Click += (_, _) => _ = vm.DetectBpmAsync(tracks, range);
            return item;
        }
    }
}
