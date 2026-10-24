using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using GZJam.App.Controls;
using GZJam.App.ViewModels;
using GZJam.App.Views;
using GZJam.Library;

namespace GZJam.App.Tests;

/// <summary>Labels from the app: tagging songs, the chips after the title, the label button.</summary>
public sealed class LabelsAppTests
{
    [Theory]
    [InlineData("basie", "Lindy", "basie label:Lindy")]
    [InlineData("basie label:lindy jump", "Female vocals", "basie jump label:\"Female vocals\"")]   // replaces, quotes spaces
    [InlineData("label:\"female vocals\" bpm:130-150", null, "bpm:130-150")]
    [InlineData("", "none", "label:none")]
    public void A_label_filter_replaces_the_one_in_the_search_text(string text, string? label, string expected) =>
        Assert.Equal(expected, LibraryViewModel.ReplaceLabelFilter(text, label));

    [AvaloniaFact]
    public void Labelling_songs_shows_on_every_copy_and_the_button_filters_by_it()
    {
        using var app = new AppHarness();
        var vm = app.ViewModel;
        var songs = app.Library.Results.Take(3).ToList();
        vm.Queue.Add([songs[0]]);
        int everything = app.Library.Results.Count;

        vm.SetLabel(songs, "Lindy", on: true);
        vm.SetLabel([songs[0]], "Balboa", on: true);

        Assert.Equal(["Balboa", "Lindy"], songs[0].Labels);   // the library's copy, which the queue shares
        Assert.Equal(["Lindy"], songs[2].Labels);

        var button = app.Window.FindControl<Button>("LabelFilterButton")!;
        button.Flyout!.ShowAt(button);
        AppHarness.Settle(200);
        var items = ((MenuFlyout)button.Flyout).Items.OfType<MenuItem>().ToList();
        Assert.Equal(["Balboa  (1)", "Lindy  (3)", "Songs without a label", "Any label"], items.Take(4).Select(i => (string)i.Header!));
        items[1].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        button.Flyout.Hide();
        AppHarness.Settle();

        Assert.Equal("label:Lindy", app.Library.SearchText);
        Assert.Equal("Lindy", app.Library.LabelFilterLabel);
        Assert.Equal(3, app.Library.Results.Count);

        // Taking the label off one song updates the filtered list.
        vm.SetLabel([songs[2]], "Lindy", on: false);
        AppHarness.Settle();
        Assert.Equal(2, app.Library.Results.Count);
        Assert.Empty(songs[2].Labels);

        app.Library.SetLabelFilter(null);
        AppHarness.Settle();
        Assert.Equal(everything, app.Library.Results.Count);
    }

    [AvaloniaFact]
    public void The_track_menu_adds_a_new_label_to_the_selected_songs()
    {
        using var app = new AppHarness();
        var vm = app.ViewModel;
        vm.PromptAsync = (_, _, _, _) => Task.FromResult<string?>("Female vocals");
        var grid = app.Window.FindControl<DataGrid>("TrackGrid")!;
        grid.SelectedItems.Add(app.Library.Results[0]);
        grid.SelectedItems.Add(app.Library.Results[1]);

        var menu = app.Window.FindControl<ContextMenu>("TrackMenu")!;
        menu.Open(grid);
        AppHarness.Settle(100);
        var labels = app.Window.FindControl<MenuItem>("TrackLabelsMenu")!;
        var create = labels.Items.OfType<MenuItem>().Single(i => (string)i.Header! == "New label…");
        create.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        AppHarness.Settle(100);
        menu.Close();

        Assert.Equal(["Female vocals"], app.Library.Results[0].Labels);
        Assert.Equal(["Female vocals"], app.Library.Results[1].Labels);
        Assert.Equal(2, vm.GetLabels().Single().Count);

        // Next time, the menu ticks it for those songs.
        menu.Open(grid);
        AppHarness.Settle(100);
        var item = labels.Items.OfType<MenuItem>().First();
        Assert.Equal("Female vocals", item.Header);
        Assert.True(item.IsChecked);
        menu.Close();
    }

    [AvaloniaFact]
    public void Renaming_a_label_renames_it_everywhere_on_screen_and_the_search_follows()
    {
        using var app = new AppHarness();
        var vm = app.ViewModel;
        var songs = app.Library.Results.Take(3).ToList();
        vm.SetLabel(songs, "Lindy", on: true);
        vm.Queue.Add([songs[0]]);
        vm.LoadToDeckAsync(vm.Deck1!, songs[1]).GetAwaiter().GetResult();
        app.Library.SetLabelFilter("Lindy");
        AppHarness.Settle();
        string? asked = null;
        vm.PromptAsync = (heading, message, _, initial) =>
        {
            asked = $"{heading} | {message} | {initial}";
            return Task.FromResult<string?>("Lindy hop");
        };

        var menu = new MenuItem();
        LabelsMenu.Fill(menu, vm, [songs[0]]);
        var rename = menu.Items.OfType<MenuItem>().Single(i => (string?)i.Header == "Rename a label");
        rename.Items.OfType<MenuItem>().Single(i => (string?)i.Header == "Lindy  (3)").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        AppHarness.Settle();

        Assert.Equal("Rename \"Lindy\" | 3 songs. If another label already has this name, the two become one. | Lindy", asked);
        Assert.Equal(["Lindy hop"], vm.Queue.Items[0].Track.Labels);
        Assert.Equal(["Lindy hop"], vm.Deck1!.Track!.Labels);
        Assert.Equal("label:\"Lindy hop\"", app.Library.SearchText);
        Assert.Equal(3, app.Library.Results.Count);
        Assert.All(app.Library.Results, t => Assert.Equal(["Lindy hop"], t.Labels));

        // To another label's name: the two become one, and it says so.
        vm.SetLabel([songs[2]], "Balboa", on: true);
        vm.RenameLabel("Balboa", "lindy HOP");
        Assert.Equal(["Lindy hop"], app.Library.Results.Single(t => t.Path == songs[2].Path).Labels);   // the copy on screen
        Assert.Equal("\"Balboa\" and \"Lindy hop\" are one label now.", vm.Notice);
        Assert.Equal(["Lindy hop"], vm.GetLabels().Select(l => l.Name));
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_songs_labels_show_on_its_deck_and_queue_row_without_making_them_taller(bool fourDecks)
    {
        using var app = new AppHarness();
        var vm = app.ViewModel;
        vm.Settings.FourDecks = fourDecks;
        var songs = app.Library.Results.Take(2).ToList();
        vm.LoadToDeckAsync(vm.Deck1!, songs[0]).GetAwaiter().GetResult();
        vm.LoadToDeckAsync(vm.Deck3!, songs[1]).GetAwaiter().GetResult();
        vm.Queue.Add(songs);
        AppHarness.Settle(200);
        var deck = app.Window.FindControl<DeckView>("Deck1View")!;
        var queue = app.Window.FindControl<ListBox>("QueueList")!;
        double deckHeight = deck.Bounds.Height;
        double rowHeight = queue.ContainerFromIndex(0)!.Bounds.Height;

        vm.SetLabel([songs[0]], "Lindy", on: true);
        vm.SetLabel([songs[0]], "Request", on: true);
        AppHarness.Settle(200);

        var chips = deck.FindControl<LabelChips>(fourDecks ? "CompactDeckLabels" : "DeckLabels")!;
        Assert.True(chips.IsEffectivelyVisible);
        Assert.Equal(["Lindy", "Request"], chips.Items.Cast<string>());
        var row = queue.ContainerFromIndex(0)!;
        Assert.Equal(2, row.GetVisualDescendants().OfType<LabelChips>().Single().ItemCount);
        Assert.Equal(deckHeight, deck.Bounds.Height, 1);
        Assert.Equal(rowHeight, row.Bounds.Height, 1);
        Assert.Equal(rowHeight, queue.ContainerFromIndex(1)!.Bounds.Height, 1);   // one without labels
    }
}
