using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using GZJam.App.ViewModels;

namespace GZJam.App.Tests;

/// <summary>Search filters without memorizing them (#241): suggestions as you type, the Filter button, the cheat sheet.</summary>
public sealed class SearchFilterAppTests
{
    [Theory]
    [InlineData("basie jump", "artist", "artist:basie artist:jump")]
    [InlineData("basie -live bpm:130-150", "title", "title:basie -title:live bpm:130-150")]
    [InlineData("\"count basie\" energy:high", "artist", "artist:\"count basie\" energy:high")]
    [InlineData("artist:basie title:jump", "album", "album:basie album:jump")]   // words already in a field move too
    [InlineData("artist:basie -title:live rating:yes", null, "basie -live rating:yes")]   // back to everywhere
    [InlineData("rating:yes pocket:corner", "artist", "rating:yes pocket:corner")]   // nothing to move
    public void Search_only_in_moves_the_words_into_a_field(string text, string? field, string expected) =>
        Assert.Equal(expected, LibraryViewModel.MoveWordsInto(text, field));

    [Theory]
    [InlineData("basie jump", "")]
    [InlineData("artist:basie -artist:live bpm:140", "artist")]
    [InlineData("artist:basie jump", null)]   // a mix
    [InlineData("bpm:140", null)]             // no words
    public void Where_the_words_are_searched(string text, string? expected) =>
        Assert.Equal(expected, LibraryViewModel.SearchedInField(text));

    [Theory]
    [InlineData("basie energy:low", "energy", "energy:high", "basie energy:high")]
    [InlineData("basie -played:7", "played", "played:never", "basie played:never")]   // either way round
    [InlineData("basie rating:yes", "rating", null, "basie")]
    public void A_filter_from_the_menu_replaces_the_one_on_its_field(string text, string field, string? filter, string expected) =>
        Assert.Equal(expected, LibraryViewModel.ReplaceFieldFilter(text, field, filter));

    private static TextBox SearchBox(AppHarness app) => app.Window.FindControl<TextBox>("SearchBox")!;

    private static string[] Shown(AppHarness app) => app.Window.ShownSuggestions.Select(s => s.Text).ToArray();

    private static void Press(AppHarness app, Key key, PhysicalKey physical)
    {
        app.Window.KeyPress(key, RawInputModifiers.None, physical, null);
        AppHarness.Settle(100);
    }

    [AvaloniaFact]
    public void Typing_offers_a_filter_and_then_whats_in_the_library()
    {
        using var app = new AppHarness();
        AppHarness.Settle(200);
        SearchBox(app).Focus();

        app.Window.KeyTextInput("ar");
        AppHarness.Settle(100);
        Assert.Equal(["artist:"], Shown(app));

        Press(app, Key.Down, PhysicalKey.ArrowDown);
        Press(app, Key.Enter, PhysicalKey.Enter);
        Assert.Equal("artist:", app.Library.SearchText);
        Assert.Equal("Count Basie", Shown(app)[0]);   // the most songs first

        app.Window.KeyTextInput("chi");
        AppHarness.Settle(100);
        Assert.Equal(["Chick Webb"], Shown(app));
        Press(app, Key.Down, PhysicalKey.ArrowDown);
        Press(app, Key.Enter, PhysicalKey.Enter);
        AppHarness.Settle();

        Assert.Equal("artist:\"Chick Webb\" ", app.Library.SearchText);
        Assert.Empty(Shown(app));   // a space after it: nothing being typed
        Assert.Equal(3, app.Library.Results.Count);
    }

    [AvaloniaFact]
    public void Enter_without_choosing_and_typing_on_leave_the_words_alone()
    {
        using var app = new AppHarness();
        AppHarness.Settle(200);
        SearchBox(app).Focus();

        app.Window.KeyTextInput("al");
        AppHarness.Settle(100);
        Assert.Equal(["album:"], Shown(app));
        Press(app, Key.Enter, PhysicalKey.Enter);   // nothing chosen: not the suggestion's
        Assert.Equal("al", app.Library.SearchText);

        app.Window.KeyTextInput("most");   // "almost": no filter starts with it
        AppHarness.Settle(100);
        Assert.Empty(Shown(app));
    }

    [AvaloniaFact]
    public void Escape_closes_the_suggestions_first_and_clears_the_search_after()
    {
        using var app = new AppHarness();
        AppHarness.Settle(200);
        SearchBox(app).Focus();
        app.Window.KeyTextInput("energy:");
        AppHarness.Settle(100);
        Assert.Equal(["high", "medium", "low", "none"], Shown(app));

        Press(app, Key.Escape, PhysicalKey.Escape);
        Assert.Empty(Shown(app));
        Assert.Equal("energy:", app.Library.SearchText);

        Press(app, Key.Escape, PhysicalKey.Escape);
        Assert.Equal("", app.Library.SearchText);
    }

    [AvaloniaFact]
    public void A_tap_on_a_suggestion_uses_it()
    {
        using var app = new AppHarness();
        AppHarness.Settle(200);
        SearchBox(app).Focus();
        app.Window.KeyTextInput("genre:");
        AppHarness.Settle(200);

        var rows = app.Window.FindControl<StackPanel>("SuggestionRows")!.Children;
        var blues = rows.OfType<Border>().Single(r => r.GetLogicalDescendantsText().Contains("Blues"));
        blues.RaiseEvent(new PointerPressedEventArgs(blues, new Pointer(1, PointerType.Touch, true), app.Window,
            default, 0, new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None));
        AppHarness.Settle();

        Assert.Equal("genre:Blues ", app.Library.SearchText);
        Assert.Equal(2, app.Library.Results.Count);
    }

    [AvaloniaFact]
    public void The_filter_button_writes_its_choices_into_the_search()
    {
        using var app = new AppHarness();
        AppHarness.Settle(200);
        app.Library.SearchText = "basie";
        var button = app.Window.FindControl<Button>("FilterButton")!;
        Assert.DoesNotContain("on", button.Classes);

        MenuItem Open(string header)
        {
            button.Flyout!.ShowAt(button);
            AppHarness.Settle(200);
            return ((MenuFlyout)button.Flyout).Items.OfType<MenuItem>().Single(i => (string?)i.Header == header);
        }
        void Click(MenuItem submenu, string header)
        {
            submenu.Items.OfType<MenuItem>().First(i => ((string?)i.Header)!.StartsWith(header)).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            button.Flyout!.Hide();
            AppHarness.Settle();
        }

        Click(Open("Energy"), "Medium or high");
        Assert.Equal("basie energy:2-3", app.Library.SearchText);
        Assert.Contains("on", button.Classes);
        Assert.True(Open("Energy").Items.OfType<MenuItem>().Single(i => (string?)i.Header == "Medium or high").IsChecked);
        button.Flyout!.Hide();

        Click(Open("Played"), "Not in the last week");
        Assert.Equal("basie energy:2-3 -played:7", app.Library.SearchText);

        var years = Open("Year").Items.OfType<MenuItem>().Select(i => (string?)i.Header).ToList();
        button.Flyout!.Hide();
        Assert.Contains(years, h => h!.StartsWith("1940s  ("));

        Click(Open("Search only in"), "Artist");
        Assert.Equal("artist:basie energy:2-3 -played:7", app.Library.SearchText);

        Click(Open("Energy"), "Any energy");
        Click(Open("Played"), "Any played");
        Click(Open("Search only in"), "Everywhere");
        Assert.Equal("basie", app.Library.SearchText);
        Assert.DoesNotContain("on", button.Classes);
    }

    [AvaloniaFact]
    public void The_question_mark_shows_while_the_box_is_empty_and_its_examples_go_into_the_search()
    {
        using var app = new AppHarness();
        AppHarness.Settle(200);
        var help = app.Window.FindControl<Button>("SearchHelpButton")!;
        Assert.True(help.IsVisible);

        help.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        AppHarness.Settle(200);
        var sheet = app.Window.SearchHelp;
        Assert.True(sheet.IsOpen);
        var example = ((Control)sheet.Content!).GetLogicalDescendants().OfType<Button>()
            .Single(b => b.GetLogicalDescendantsText().Contains("energy:high"));
        example.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        AppHarness.Settle();

        Assert.Equal("energy:high", app.Library.SearchText);
        Assert.False(help.IsVisible);   // the ✕ has its place now
        Assert.False(sheet.IsOpen);
    }

    [AvaloniaFact]
    public void A_narrow_song_list_gives_the_search_box_the_result_counts_room()
    {
        using var app = new AppHarness();
        AppHarness.Settle(200);
        var panes = app.Window.FindControl<Grid>("PanesGrid")!;
        var count = app.Window.FindControl<TextBlock>("ResultSummaryText")!;
        var box = SearchBox(app);
        Assert.True(count.IsVisible);

        panes.ColumnDefinitions[4].Width = new GridLength(560);
        panes.ColumnDefinitions[0].Width = new GridLength(420);
        AppHarness.Settle(200);
        Assert.False(count.IsVisible, $"the box is {box.Bounds.Width} px wide");
        Assert.True(box.Bounds.Width >= 150, $"the box is {box.Bounds.Width} px wide");

        panes.ColumnDefinitions[0].Width = new GridLength(260);
        panes.ColumnDefinitions[4].Width = new GridLength(320);
        AppHarness.Settle(200);
        Assert.True(count.IsVisible, $"the box is {box.Bounds.Width} px wide");
    }
}

internal static class LogicalText
{
    /// <summary>All the text in a control's TextBlocks, joined: to find a row by what it says.</summary>
    public static string GetLogicalDescendantsText(this Control control) =>
        string.Join(" ", Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(control).OfType<TextBlock>().Select(t => t.Text));

    public static IEnumerable<Avalonia.LogicalTree.ILogical> GetLogicalDescendants(this Control control) =>
        Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(control);
}
