using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GZJam.Library;

namespace GZJam.App.ViewModels;

/// <summary>
/// The Filter button and the suggestions under the search box (#241): both only ever write into
/// the search text, so the box always shows the whole search.
/// </summary>
public sealed partial class LibraryViewModel
{
    /// <summary>The fields "Search only in" can put the typed words into, with their names in the menu.</summary>
    public static readonly IReadOnlyList<(string Field, string Name)> TextFields =
    [
        ("title", "Title"), ("artist", "Artist"), ("album", "Album"), ("genre", "Genre"),
        ("grouping", "Grouping"), ("comment", "Comment"), ("folder", "Folder"), ("file", "File name"),
    ];

    /// <summary>The filters the Filter button writes; it shows "on" while one is in the search.</summary>
    private static readonly string[] MenuFilters = ["energy", "rating", "played", "year"];

    private readonly Dictionary<string, IReadOnlyList<ValueCount>> _suggestionValues = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// What the library holds for a field, for the suggestions: the labels with their counts, or
    /// an artist, album, genre or Grouping's values. Kept until a scan or a label change.
    /// </summary>
    public IReadOnlyList<ValueCount> SuggestionValues(string field)
    {
        if (_suggestionValues.TryGetValue(field, out var cached))
            return cached;
        IReadOnlyList<ValueCount> values = field.Equals("label", StringComparison.OrdinalIgnoreCase)
            ? _database.GetLabels().Select(l => new ValueCount(l.Name, l.Count)).ToList()
            : LibraryDatabase.SuggestedFields.Contains(field) ? _database.GetFieldValues(field) : [];
        return _suggestionValues[field] = values;
    }

    /// <summary>The decades the library's songs come from, for Filter › Year.</summary>
    public IReadOnlyList<ValueCount> Decades() => _database.GetDecades();

    private void ForgetSuggestionValues()
    {
        _suggestionValues.Clear();
        _searchWords = null;
    }

    // ───────────── Did you mean...? (#242) ─────────────

    private volatile IReadOnlyList<SearchWord>? _searchWords;

    /// <summary>When a search finds nothing because of a typo: the search with it corrected ("duke ellington"), else null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDidYouMean))]
    public partial string? DidYouMean { get; private set; }

    /// <summary>What the empty list offers: "Did you mean duke ellington?  12 songs".</summary>
    [ObservableProperty]
    public partial string? DidYouMeanText { get; private set; }

    public bool HasDidYouMean => DidYouMean is not null;

    [RelayCommand]
    private void UseDidYouMean()
    {
        if (DidYouMean is { } better)
            SearchText = better;
    }

    /// <summary>
    /// On the search's own thread, once it found nothing: the search with its misspelled words
    /// corrected, and how many songs that finds, where it was searched. Null if no word was
    /// misspelled, or the corrected search finds nothing either.
    /// </summary>
    private (string Text, int Count)? DidYouMeanFor(string text, FolderScope? scope, CancellationToken cancel, double near, long? playlist)
    {
        if (!HasWords(text))
            return null;
        var words = _searchWords ??= _database.GetSearchWords();
        cancel.ThrowIfCancellationRequested();
        if (SearchSpelling.Suggest(text, words) is not { } better)
            return null;
        int count = _database.Search(better, scope, cancel, near, playlist).Count;
        return count > 0 ? (better, count) : null;
    }

    /// <summary>The Filter button lights up while one of its filters (energy, hearts, played, year, a field) is in the search.</summary>
    public bool HasMoreFilters => LibraryDatabase.SearchTokens(SearchText).Any(t => !t.EndsWith(':')   // artist: still being typed
        && MenuFilters.Concat(TextFields.Select(f => f.Field)).Any(name => IsFieldFilter(t, name)));

    /// <summary>The filter on <paramref name="field"/> in the search text (<c>energy:high</c>, <c>-played:7</c>), if any.</summary>
    public string? FieldFilter(string field) =>
        LibraryDatabase.SearchTokens(SearchText).LastOrDefault(t => IsFieldFilter(t, field))?.ToLowerInvariant();

    /// <summary>Puts a filter on <paramref name="field"/> in place of any already there (null: removes it).</summary>
    public void SetFieldFilter(string field, string? filter) => SearchText = ReplaceFieldFilter(SearchText, field, filter);

    internal static string ReplaceFieldFilter(string text, string field, string? filter) =>
        ReplaceFilter(text, t => IsFieldFilter(t, field), filter);

    /// <summary><c>energy:high</c>, <c>-energy:high</c>: a filter on that field, either way round.</summary>
    private static bool IsFieldFilter(string token, string field) =>
        token.TrimStart('-').StartsWith(field + ":", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Where the typed words are searched: "" for everywhere (plain words), a field if every one is
    /// in it (<c>artist:basie</c>), null for a mix, or no words at all.
    /// </summary>
    public string? SearchedIn => SearchedInField(SearchText);

    /// <summary>Whether the search has words to search for (plain, or in a field), so "Search only in" has something to move.</summary>
    internal static bool HasWords(string text) => LibraryDatabase.SearchTokens(text).Any(t => WordsField(t) is not null);

    internal static string? SearchedInField(string text)
    {
        var places = LibraryDatabase.SearchTokens(text).Select(WordsField).Where(f => f is not null).Distinct().ToList();
        return places.Count == 1 ? places[0] : null;
    }

    /// <summary>
    /// "Search only in": the typed words (and any already in a field) move into
    /// <paramref name="field"/>, <c>basie</c> becoming <c>artist:basie</c>; null puts them back
    /// as plain words, searched everywhere. Other filters stay as they are.
    /// </summary>
    public void SearchIn(string? field) => SearchText = MoveWordsInto(SearchText, field);

    internal static string MoveWordsInto(string text, string? field) =>
        string.Join(' ', LibraryDatabase.SearchTokens(text).Select(token =>
        {
            if (WordsField(token) is null)
                return token;
            string minus = token.StartsWith('-') ? "-" : "";
            string word = token[minus.Length..];
            if (!word.StartsWith('"') && word.IndexOf(':') is var colon and > 0)
                word = word[(colon + 1)..];
            return field is null ? minus + word : $"{minus}{field}:{word}";
        }));

    /// <summary>The field a token's words are searched in: "" for plain words, the field for <c>artist:basie</c>, null for any other filter.</summary>
    private static string? WordsField(string token)
    {
        string word = token.TrimStart('-');
        if (word.Length == 0 || token == LibraryDatabase.OrWord)
            return null;
        if (word.StartsWith('"'))
            return "";
        int colon = word.IndexOf(':');
        if (colon < 0)
            return "";
        string field = word[..colon].ToLowerInvariant();
        return TextFields.Any(f => f.Field == field) ? field : null;
    }
}
