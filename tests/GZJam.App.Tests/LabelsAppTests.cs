using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using GZJam.App.ViewModels;
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
}
