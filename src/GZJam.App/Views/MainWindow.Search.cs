using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using GZJam.App.ViewModels;
using GZJam.Library;

namespace GZJam.App.Views;

/// <summary>
/// The buttons by the search box (BPM, Label, Filter) and its cheat sheet: tap-friendly ways to
/// write filters into the search text, which always shows the whole search.
/// </summary>
public partial class MainWindow
{
    private MenuFlyout BpmFilterMenu => (MenuFlyout)BpmFilterButton.Flyout!;

    private MenuFlyout LabelFilterMenu => (MenuFlyout)LabelFilterButton.Flyout!;

    private MenuFlyout FilterMenu => (MenuFlyout)FilterButton.Flyout!;

    /// <summary>The cheat sheet, under the search box (not the "?", at its right end, which would put it over the folders).</summary>
    internal Flyout SearchHelp { get; } = new() { Placement = PlacementMode.BottomEdgeAlignedLeft, FlyoutPresenterClasses = { "panel" } };

    private void OnSearchHelpClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => SearchHelp.ShowAt(SearchBox);

    /// <summary>The narrowest the search box gets before the result count gives it its room.</summary>
    private const double MinSearchBoxWidth = 200;

    private void InitializeSearch()
    {
        FlyoutMenus.FillBeforeOpening(BpmFilterButton, BpmFilterMenu, FillBpmFilterMenu);
        FlyoutMenus.FillBeforeOpening(LabelFilterButton, LabelFilterMenu, FillLabelFilterMenu);
        FlyoutMenus.FillBeforeOpening(FilterButton, FilterMenu, FillFilterMenu);
        SearchHelp.Content = CheatSheet();
        SearchHeader.SizeChanged += (_, _) => FitSearchHeader();
        InitializeSuggestions();
    }

    /// <summary>
    /// A narrow song list: the result count goes first, so the search box keeps room to type in
    /// (and comes back once there's room for both again).
    /// </summary>
    private void FitSearchHeader()
    {
        double box = SearchBox.Bounds.Width;
        if (ResultSummaryText.IsVisible && box < MinSearchBoxWidth)
            ResultSummaryText.IsVisible = false;
        else if (!ResultSummaryText.IsVisible && box - ResultSummaryText.DesiredSize.Width - SearchHeader.ColumnSpacing >= MinSearchBoxWidth)
            ResultSummaryText.IsVisible = true;
    }

    /// <summary>
    /// The Filter button: energy, hearts, when played, the year, and which field the words are
    /// searched in. Each choice writes its filter into the search text, in place of one there.
    /// </summary>
    private void FillFilterMenu()
    {
        var library = Vm.Library;
        var items = FilterMenu.Items;
        items.Clear();

        items.Add(Submenu("Energy", "energy",
            ("High", "energy:high"), ("Medium", "energy:medium"), ("Low", "energy:low"),
            ("Medium or high", "energy:2-3"), ("Not marked", "energy:none")));
        items.Add(Submenu("Hearts", "rating",
            ("♥ Hearted", "rating:yes"), ("Broken-hearted", "rating:no"), ("Not rated", "rating:none"),
            ("Not broken-hearted", "-rating:no")));
        items.Add(Submenu("Played", "played",
            ("In the last week", "played:7"), ("In the last month", "played:30"), ("Ever", "played:yes"),
            ("Never", "played:never"), ("Not in the last week", "-played:7"), ("Not in the last month", "-played:30")));
        items.Add(Submenu("Year", "year", library.Decades()
            .Select(d => ($"{d.Value}s  ({Format.Count(d.Count)})", $"year:{d.Value}-{int.Parse(d.Value, System.Globalization.CultureInfo.InvariantCulture) + 9}"))
            .ToArray()));
        items.Add(new Separator());
        items.Add(SearchOnlyIn());
        items.Add(new Separator());
        var help = new MenuItem { Header = "How to search…" };
        help.Click += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() => SearchHelp.ShowAt(SearchBox));   // once the menu has closed
        items.Add(help);

        MenuItem Submenu(string header, string field, params (string Header, string Filter)[] choices)
        {
            var menu = new MenuItem { Header = header };
            string? current = library.FieldFilter(field);
            foreach (var (name, filter) in choices)
                menu.Items.Add(Choice(name, filter, filter == current));
            if (choices.Length == 0)
                menu.Items.Add(new MenuItem { Header = "No years in the songs' tags", IsEnabled = false });
            menu.Items.Add(new Separator());
            var any = new MenuItem { Header = $"Any {header.ToLowerInvariant()}", IsEnabled = current is not null };
            any.Click += (_, _) => library.SetFieldFilter(field, null);
            menu.Items.Add(any);
            return menu;

            MenuItem Choice(string name, string filter, bool isChecked)
            {
                var item = new MenuItem
                {
                    Header = name,
                    ToggleType = MenuItemToggleType.Radio,
                    GroupName = $"filter-{field}",
                    IsChecked = isChecked,
                };
                item.Click += (_, _) => library.SetFieldFilter(field, filter);
                return item;
            }
        }

        MenuItem SearchOnlyIn()
        {
            var menu = new MenuItem { Header = "Search only in" };
            string? current = library.SearchedIn;
            bool hasWords = LibraryViewModel.HasWords(library.SearchText);
            menu.Items.Add(Place("Everywhere", null, current == ""));
            menu.Items.Add(new Separator());
            foreach (var (field, name) in LibraryViewModel.TextFields)
                menu.Items.Add(Place(name, field, current == field));
            if (!hasWords)
                menu.Items.Add(new MenuItem { Header = "Type some words first", IsEnabled = false });
            return menu;

            MenuItem Place(string name, string? field, bool isChecked)
            {
                var item = new MenuItem
                {
                    Header = name,
                    ToggleType = MenuItemToggleType.Radio,
                    GroupName = "filter-search-in",
                    IsChecked = isChecked,
                    IsEnabled = hasWords,
                };
                item.Click += (_, _) => library.SearchIn(field);
                return item;
            }
        }
    }

    /// <summary>
    /// The cheat sheet behind the "?" in the empty search box (and Filter › How to search): how to
    /// type words, and the filters. Tapping a filter puts that example in the search.
    /// </summary>
    private StackPanel CheatSheet()
    {
        var sheet = new StackPanel { Spacing = 2, Width = 410 };   // inside the flyout's widest, with its padding
        sheet.Children.Add(Heading("TYPE WORDS"));
        sheet.Children.Add(Line("basie jump", "both words, anywhere; best matches first", tap: false));
        sheet.Children.Add(Line("\"duke ellington\"", "the words together", tap: false));
        sheet.Children.Add(Line("-live", "leave out a word, or a filter", tap: false));
        sheet.Children.Add(Line("basie OR webb", "either one, words or filters (OR in capitals)", tap: false));
        sheet.Children.Add(Heading("OR FILTER"));
        sheet.Children.Add(Line("bpm:130-150", "a BPM range; ~bpm:140 near 140, bpm:>200"));
        sheet.Children.Add(Line("artist:basie", "one field: also title, album, genre, comment, grouping, folder, file"));
        sheet.Children.Add(Line("label:lindy", "a label; label:none for songs without one"));
        sheet.Children.Add(Line("energy:high", "energy: high, medium, low, none"));
        sheet.Children.Add(Line("rating:yes", "hearts: yes, no, none"));
        sheet.Children.Add(Line("played:7", "heard in the last 7 days; played:never"));
        sheet.Children.Add(Line("year:1935-1940", "the year, or a range"));
        sheet.Children.Add(new TextBlock
        {
            Text = "Tap a filter to try it. Start typing one (ar…) and the box suggests the rest.",
            Classes = { "muted" },
            FontSize = 11.5,
            Margin = new Thickness(6, 8, 6, 0),
            TextWrapping = TextWrapping.Wrap,
        });
        return sheet;

        static TextBlock Heading(string text) => new()
        {
            Text = text,
            Classes = { "muted" },
            FontSize = 11,
            FontWeight = FontWeight.Bold,
            LetterSpacing = 1,
            Margin = new Thickness(6, 6, 6, 2),
        };

        Control Line(string example, string description, bool tap = true)
        {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("128,*"), ColumnSpacing = 8 };
            grid.Children.Add(new TextBlock
            {
                Text = example,
                FontFamily = (FontFamily)Application.Current!.FindResource("MonoFont")!,
                Foreground = (IBrush)Application.Current!.FindResource("AccentBrush")!,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
            });
            var text = new TextBlock
            {
                Text = description,
                Classes = { "muted" },
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);
            if (!tap)
                return new Border { Padding = new Thickness(6, 4), Child = grid };
            var button = new Button { Classes = { "cheat" }, Content = grid };
            button.Click += (_, _) =>
            {
                SearchHelp.Hide();
                TryFilter(example);
            };
            return button;
        }
    }

    /// <summary>Puts an example filter from the cheat sheet in the search, in place of one on the same field.</summary>
    internal void TryFilter(string filter)
    {
        var library = Vm.Library;
        string field = filter[..filter.IndexOf(':')];
        if (field == "bpm")
            library.SetBpmFilter(filter);
        else if (field == "label")
            library.SetLabelFilter(filter[(field.Length + 1)..]);
        else
            library.SetFieldFilter(field, filter);
        SearchBox.Focus();
        SearchBox.CaretIndex = SearchBox.Text?.Length ?? 0;
    }

    /// <summary>The label button: each label (with how many songs), songs without one, or any.</summary>
    private void FillLabelFilterMenu()
    {
        var library = Vm.Library;
        var items = LabelFilterMenu.Items;
        items.Clear();
        string? current = library.LabelFilter;
        foreach (var label in Vm.GetLabels())
            items.Add(Choice($"{label.Name}  ({label.Count})", label.Name, label.Name.Equals(current, StringComparison.OrdinalIgnoreCase)));
        if (items.Count > 0)
            items.Add(new Separator());
        items.Add(Choice("Songs without a label", "none", "none".Equals(current, StringComparison.OrdinalIgnoreCase)));
        items.Add(Choice("Any label", null, false, enabled: current is not null));
        if (LabelsMenu.Rename(Vm) is { } rename)
        {
            items.Add(new Separator());
            items.Add(rename);
        }
        items.Add(new MenuItem
        {
            Header = "Label songs: right-click them › Labels.\nOr type label:slow  -label:requests",
            IsEnabled = false,
            FontSize = 11,
        });

        MenuItem Choice(string header, string? label, bool isChecked, bool enabled = true)
        {
            var item = new MenuItem
            {
                Header = header,
                IsEnabled = enabled,
                ToggleType = label is null ? MenuItemToggleType.None : MenuItemToggleType.Radio,
                GroupName = "label-filter",
                IsChecked = isChecked,
            };
            item.Click += (_, _) => library.SetLabelFilter(label);
            return item;
        }
    }

    private void FillBpmFilterMenu()
    {
        var library = Vm.Library;
        var items = BpmFilterMenu.Items;
        items.Clear();

        // Near a deck's tempo as the room would hear it (with its tempo slider), as near as
        // Settings › Library says (the slider's reach to start with: anything listed could be
        // pitched to match).
        foreach (DeckViewModel deck in Vm.ActiveDecks)
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
            Header = "Or type filters: bpm:130-150  ~bpm:140  artist:basie\nyear:1938  rating:yes  label:slow  -played:7  -word to leave out",
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
