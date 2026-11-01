using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using GZJam.Library;

namespace GZJam.App.Views;

/// <summary>
/// Suggestions under the search box as the DJ types (#241): filter names, then what's in the
/// library. They never take the keyboard from the box: ↓ and ↑ choose, Enter or Tab uses the
/// chosen one, Escape closes them, and typing on simply carries on.
/// </summary>
public partial class MainWindow
{
    private SearchSuggestionList? _suggestions;
    private int _chosenSuggestion = -1;

    /// <summary>The suggestions showing now, for tests.</summary>
    internal IReadOnlyList<SearchSuggestion> ShownSuggestions => SuggestionPopup.IsOpen ? _suggestions?.Items ?? [] : [];

    private void InitializeSuggestions()
    {
        SearchBox.TextChanged += (_, _) => UpdateSuggestions();
        SearchBox.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.CaretIndexProperty)
                UpdateSuggestions();
        };
        // A tap on a suggestion is handled as it's pressed, before the box would lose the focus.
        SearchBox.LostFocus += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            if (!SearchBox.IsFocused)
                CloseSuggestions();
        });
    }

    private void UpdateSuggestions()
    {
        if (!SearchBox.IsFocused)
        {
            CloseSuggestions();
            return;
        }
        string text = SearchBox.Text ?? "";
        var list = SearchSuggestions.For(text, SearchBox.CaretIndex, Vm.Library.SuggestionValues);
        if (list is null)
        {
            CloseSuggestions();
            return;
        }
        bool same = _suggestions is not null && SuggestionPopup.IsOpen && _suggestions.Items.SequenceEqual(list.Items);
        _suggestions = list;
        if (same)
            return;   // the caret moved on, the list didn't change: keep the choice
        _chosenSuggestion = -1;
        ShowSuggestionRows();
        SuggestionPopup.MinWidth = SearchBox.Bounds.Width;
        SuggestionPopup.IsOpen = true;
    }

    private void CloseSuggestions()
    {
        SuggestionPopup.IsOpen = false;
        _suggestions = null;
        _chosenSuggestion = -1;
    }

    private void ShowSuggestionRows()
    {
        var rows = SuggestionRows.Children;
        rows.Clear();
        if (_suggestions is null)
            return;
        for (int i = 0; i < _suggestions.Items.Count; i++)
        {
            SearchSuggestion item = _suggestions.Items[i];
            bool filterName = item.Text.EndsWith(':');
            var line = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 12 };
            line.Children.Add(new TextBlock
            {
                Text = item.Text,
                Classes = { item.Insert is null ? "detail" : filterName ? "filter" : "name" },
                VerticalAlignment = VerticalAlignment.Center,
                MinWidth = filterName ? 72 : 0,
            });
            if (item.Detail is { } detail)
            {
                var d = new TextBlock { Text = detail, Classes = { "detail" }, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(d, 1);
                line.Children.Add(d);
            }
            if (item.Count is { } count)
            {
                var c = new TextBlock { Text = Format.Count(count), Classes = { "detail" }, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(c, 2);
                line.Children.Add(c);
            }
            var row = new Border { Classes = { "suggestion" }, Child = line };
            if (item.Insert is null)
            {
                row.Classes.Add("hint");
            }
            else
            {
                SearchSuggestion chosen = item;
                row.PointerPressed += (_, e) =>
                {
                    e.Handled = true;
                    UseSuggestion(chosen);
                };
            }
            rows.Add(row);
        }
        MarkChosenSuggestion();
    }

    private void MarkChosenSuggestion()
    {
        for (int i = 0; i < SuggestionRows.Children.Count; i++)
            SuggestionRows.Children[i].Classes.Set("chosen", i == _chosenSuggestion);
    }

    private void UseSuggestion(SearchSuggestion item)
    {
        if (_suggestions is not { } list || item.Insert is null)
            return;
        var (text, caret) = SearchSuggestions.Apply(SearchBox.Text ?? "", list, item);
        CloseSuggestions();
        SearchBox.Focus();
        SearchBox.Text = text;
        SearchBox.CaretIndex = caret;
        UpdateSuggestions();   // after artist: come the artists
    }

    /// <summary>The keys that work the suggestions while they show. Returns whether the key was theirs.</summary>
    private bool OnSuggestionKey(KeyEventArgs e)
    {
        if (!SuggestionPopup.IsOpen || _suggestions is not { } list || e.KeyModifiers != KeyModifiers.None)
            return false;
        int choices = list.Items.Count;
        switch (e.Key)
        {
            case Key.Down:
                _chosenSuggestion = NextChoice(list, _chosenSuggestion, +1);
                break;
            case Key.Up:
                _chosenSuggestion = NextChoice(list, _chosenSuggestion, -1);
                break;
            case Key.Enter or Key.Tab when _chosenSuggestion >= 0 && _chosenSuggestion < choices:
                UseSuggestion(list.Items[_chosenSuggestion]);
                return true;
            case Key.Escape:
                CloseSuggestions();
                return true;
            default:
                return false;
        }
        MarkChosenSuggestion();
        return true;
    }

    /// <summary>The next choice up or down, skipping hints; -1 (none chosen) above the first.</summary>
    private static int NextChoice(SearchSuggestionList list, int from, int step)
    {
        for (int i = from + step; i >= 0 && i < list.Items.Count; i += step)
        {
            if (list.Items[i].Insert is not null)
                return i;
        }
        return step < 0 ? -1 : from;
    }
}
