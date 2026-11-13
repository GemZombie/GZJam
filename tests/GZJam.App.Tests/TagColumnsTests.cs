using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using GZJam.Library;

namespace GZJam.App.Tests;

/// <summary>The song files' other tags (#119) as library columns, from the column menu, and in the search box's suggestions.</summary>
public sealed class TagColumnsTests
{
    /// <summary>A harness whose songs have a Dance tag (TXXX), two a Composer (TCOM), before the first scan.</summary>
    internal static AppHarness Tagged()
    {
        var app = new AppHarness(start: false);
        var files = Directory.GetFiles(app.MusicPath, "*.mp3", SearchOption.AllDirectories).Order().ToList();
        string[] dances = ["Lindy", "Balboa", "Lindy", "Charleston"];
        for (int i = 0; i < files.Count; i++)
        {
            using var file = TagLib.File.Create(files[i]);
            var id3 = (TagLib.Id3v2.Tag)file.GetTag(TagLib.TagTypes.Id3v2, create: true);
            TagLib.Id3v2.UserTextInformationFrame.Get(id3, "DANCE", create: true).Text = [dances[i % dances.Length]];
            if (i < 2)
                id3.SetTextFrame("TCOM", "Duke Ellington");
            file.Save();
        }
        app.Start();
        return app;
    }

    private static DataGrid Grid(AppHarness app) => app.Window.FindControl<DataGrid>("TrackGrid")!;

    private static List<string> Headers(AppHarness app) => Grid(app).Columns.Select(c => c.Header as string ?? "").ToList();

    private static void Click(MenuItem item) => item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

    [AvaloniaFact]
    public void The_column_menu_offers_the_tags_the_songs_have_and_a_chosen_one_is_a_column_remembered()
    {
        using var app = Tagged();
        var menu = app.Window.ColumnsMenu();
        var items = menu.Items.OfType<MenuItem>().Select(i => (string)i.Header!).ToList();
        Assert.Equal(["Tags in your songs", "Dance", "Composer", "Widths as they came"], items[^4..]);   // most songs first

        Click(menu.Items.OfType<MenuItem>().Single(i => (string)i.Header! == "Dance"));
        menu.Close();
        AppHarness.Settle(200);

        Assert.Equal(["dance"], app.Settings.TagColumns);
        var headers = Headers(app);
        Assert.Equal(headers.IndexOf("Year") - 1, headers.IndexOf("Dance"));   // with the other text, before Year
        var cells = Grid(app).GetVisualDescendants().OfType<TextBlock>().Where(t => t.Tag as string == "dance").Select(t => t.Text).ToList();
        Assert.Contains("Balboa", cells);
        Assert.True(app.Window.ColumnsMenu().Items.OfType<MenuItem>().Single(i => (string)i.Header! == "Dance").IsChecked);

        app.Window.SetTagColumnShown("dance", false);
        Assert.DoesNotContain("Dance", Headers(app));
        Assert.Empty(app.Settings.TagColumns);
    }

    [AvaloniaFact]
    public void A_tag_column_sorts_and_follows_the_song_each_row_shows()
    {
        using var app = Tagged();
        app.Window.SetTagColumnShown("dance", true);
        app.Window.SetTagColumnShown("composer", true);
        var column = Grid(app).Columns.Single(c => c.Header as string == "Dance");

        column.Sort(ListSortDirection.Ascending);
        AppHarness.Settle(300);

        var rows = Grid(app).GetVisualDescendants().OfType<DataGridRow>().OrderBy(r => r.Bounds.Y).ToList();
        string? Shown(DataGridRow row) => row.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Tag as string == "dance").Text;
        var shown = rows.Select(Shown).ToList();
        Assert.Equal(shown.Order(StringComparer.CurrentCultureIgnoreCase), shown);
        app.Screenshot("library-tag-columns");
        Assert.All(rows, r => Assert.Equal(((Track)r.DataContext!).Tag("dance"), Shown(r)));
    }

    [AvaloniaFact]
    public void Tag_columns_shown_before_come_back_at_the_next_start()
    {
        var settings = new AppSettings { MatchLoudness = false, TagColumns = ["composer"] };
        using var app = new AppHarness(settings: settings);
        Assert.Contains("Composer", Headers(app));                      // no song has it (yet)
        Assert.Contains("Composer", app.Window.ColumnsMenu().Items.OfType<MenuItem>().Select(i => (string)i.Header!));   // so it can be taken away
    }

    [AvaloniaFact]
    public void A_tag_is_suggested_as_a_filter_and_its_values_after_it()
    {
        using var app = Tagged();
        var tags = app.Library.TagNames();

        var names = SearchSuggestions.For("da", 2, app.Library.SuggestionValues, tags)!.Items;
        Assert.Equal("dance:", names.Single().Text);
        Assert.Equal("only the Dance tag", names.Single().Detail);

        var values = SearchSuggestions.For("dance:", 6, app.Library.SuggestionValues, tags)!.Items.Select(i => i.Text).ToList();
        Assert.Equal("Lindy", values[0]);
        Assert.Contains("Charleston", values);

        app.Window.FindControl<Button>("SearchHelpButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        AppHarness.Settle(200);
        var help = (Control)app.Window.SearchHelp.Content!;
        Assert.Contains(help.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "dance:lindy");   // the help's example
        app.Window.SearchHelp.Hide();

        app.Library.SearchText = "dance:balboa";
        AppHarness.WaitFor(() => app.Library.Results.All(t => t.Tag("dance") == "Balboa") && app.Library.Results.Count > 0);
    }
}
