using Avalonia.Controls;
using GZJam.App.ViewModels;

namespace GZJam.App.Views;

/// <summary>The BPM filter button by the search box: a tap-friendly way to write bpm: filters.</summary>
public partial class MainWindow
{
    private MenuFlyout BpmFilterMenu => (MenuFlyout)BpmFilterButton.Flyout!;

    private void InitializeSearch()
    {
        BpmFilterMenu.Opening += (_, _) => FillBpmFilterMenu();
    }

    private void FillBpmFilterMenu()
    {
        var library = Vm.Library;
        var items = BpmFilterMenu.Items;
        items.Clear();

        // Near a deck's tempo as the room would hear it (with its tempo slider), within the
        // slider's reach, so anything listed could be pitched to match.
        foreach (DeckViewModel deck in Vm.Decks)
        {
            bool has = deck.CurrentBpm is not null;
            int bpm = (int)Math.Round(deck.CurrentBpm ?? 0);
            items.Add(Choice(has ? $"Near Deck {deck.Label} ({bpm} BPM)" : $"Near Deck {deck.Label}", has ? $"~bpm:{bpm}" : null, has));
        }
        items.Add(new Separator());
        foreach (var (range, label) in MainWindowViewModel.BpmRanges)
            items.Add(Choice(label, $"bpm:{range.Min}-{range.Max}"));
        items.Add(new Separator());
        items.Add(Choice("Any BPM", null, library.HasBpmFilter));
        items.Add(new MenuItem
        {
            Header = "Or type filters: bpm:130-150  ~bpm:140  artist:basie\nyear:1938  rating:yes  energy:high  -word to leave out",
            IsEnabled = false,
            FontSize = 11,
        });

        MenuItem Choice(string header, string? filter, bool enabled = true)
        {
            var item = new MenuItem
            {
                Header = header,
                IsEnabled = enabled,
                ToggleType = filter is null ? MenuItemToggleType.None : MenuItemToggleType.Radio,
                GroupName = "bpm-filter",
                IsChecked = filter is not null && filter == library.BpmFilter,
            };
            item.Click += (_, _) => library.SetBpmFilter(filter);
            return item;
        }
    }
}
