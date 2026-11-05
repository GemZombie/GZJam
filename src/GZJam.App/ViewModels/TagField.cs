using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GZJam.Library;

namespace GZJam.App.ViewModels;

/// <summary>
/// One tag in a song's info (#129), edited in place: for several songs, a value they share shows,
/// and one where they differ shows "(several)" and stays as each song has it unless typed over
/// (or cleared on all of them). Emptied, a tag comes off.
/// </summary>
public sealed partial class TagField : ObservableObject
{
    /// <param name="values">Each song's value, null where it has none.</param>
    /// <param name="edited">Kept as an edit in GZJam's library, over what a file says, on any of the songs.</param>
    public TagField(string field, string name, IReadOnlyList<string?> values, bool edited, bool editable, bool isNew = false)
    {
        Field = field;
        Name = name;
        IsEditable = editable;
        IsNew = isNew;
        IsLibraryEdit = edited;
        var distinct = values.Select(v => v ?? "").Distinct(StringComparer.Ordinal).ToList();
        IsSeveral = distinct.Count > 1;
        Original = IsSeveral ? "" : Shown(field, distinct.FirstOrDefault() ?? "");
        Text = Original;
    }

    /// <summary>The <see cref="SongFields"/> field.</summary>
    public string Field { get; }

    public string Name { get; }

    /// <summary>The songs' value as shown in the box when it opened ("" for none, or for several).</summary>
    public string Original { get; }

    /// <summary>The songs differ: the box is empty, saying "(several)".</summary>
    public bool IsSeveral { get; }

    public bool IsEditable { get; }

    public bool IsReadOnly => !IsEditable;

    /// <summary>A tag the DJ added, which none of the songs had.</summary>
    public bool IsNew { get; }

    /// <summary>GZJam's library has its own value for it, over what the file says.</summary>
    public bool IsLibraryEdit { get; }

    public string? LibraryEditTip => IsLibraryEdit ? "Changed in GZJam's library; the file says otherwise" : null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChanged), nameof(Error), nameof(CanClearAll), nameof(Watermark))]
    public partial string Text { get; set; }

    /// <summary>For several songs that differ: the tag comes off all of them.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChanged), nameof(CanClearAll), nameof(Watermark))]
    public partial bool ClearsAll { get; private set; }

    partial void OnTextChanged(string value)
    {
        if (value.Length > 0)
            ClearsAll = false;
        Changed?.Invoke();
    }

    partial void OnClearsAllChanged(bool value) => Changed?.Invoke();

    /// <summary>Typed into, cleared, or (several) cleared on all.</summary>
    public event Action? Changed;

    public string? Watermark => !IsSeveral ? null : ClearsAll ? "(none, on every song)" : "(several)";

    public bool CanClearAll => IsEditable && IsSeveral && !ClearsAll && Text.Length == 0;

    [RelayCommand]
    private void ClearAll() => ClearsAll = true;

    public bool IsChanged => IsSeveral ? Text.Trim().Length > 0 || ClearsAll : Text.Trim() != Original.Trim() || IsNew && Text.Trim().Length > 0;

    /// <summary>What's saved: the value as an edit keeps it ("1957", "156.5"), or null to take the tag off.</summary>
    public string? NewValue
    {
        get
        {
            string text = Text.Trim();
            if (text.Length == 0)
                return null;
            return Field switch
            {
                SongFields.Year or SongFields.Track => SongFields.ParseWhole(text)?.ToString(CultureInfo.InvariantCulture),
                SongFields.Bpm => ParseBpm(text) is { } bpm ? SongFields.BpmText(bpm) : null,
                _ => text,
            };
        }
    }

    /// <summary>Why what's typed can't be saved: a year that isn't one, a BPM out of reach.</summary>
    public string? Error
    {
        get
        {
            string text = Text.Trim();
            if (text.Length == 0 || !IsChanged)
                return null;
            return Field switch
            {
                SongFields.Year when SongFields.ParseWhole(text) is not (>= 1 and <= 9999) => "A year, like 1957",
                SongFields.Track when SongFields.ParseWhole(text) is not (>= 1 and <= 9999) => "A number, like 3",
                SongFields.Bpm when ParseBpm(text) is not (>= TagReaderMinBpm and <= 999) => "A BPM, like 132 or 156.5",
                _ => null,
            };
        }
    }

    /// <summary>The slowest BPM a tag can have and be read (as <c>TagReader.MinTagBpm</c>).</summary>
    private const double TagReaderMinBpm = 40;

    /// <summary>A BPM as typed, with a decimal point or comma ("156,5").</summary>
    private static double? ParseBpm(string text) => SongFields.ParseBpm(text.Replace(',', '.'));

    /// <summary>A value as the box shows it: a BPM as GZJam writes it elsewhere.</summary>
    private static string Shown(string field, string value) =>
        field == SongFields.Bpm && SongFields.ParseBpm(value) is { } bpm ? Format.Bpm(bpm) : value;

    /// <summary>Puts back what it showed when it opened.</summary>
    public void Revert()
    {
        Text = Original;
        ClearsAll = false;
    }
}

/// <summary>A label in a song's info: on every song (checked), on some (a dash), or on none (added, or taken off).</summary>
public sealed partial class LabelChoice : ObservableObject
{
    public LabelChoice(string name, bool? on, bool isNew = false)
    {
        Name = name;
        Original = on;
        IsChecked = on;
        IsNew = isNew;
    }

    public string Name { get; }

    /// <summary>On every song (true), some (null), none (false).</summary>
    public bool? Original { get; }

    public bool IsNew { get; }

    /// <summary>Only a label on some of the songs can be left as it is, on some.</summary>
    public bool IsThreeState => Original is null;

    [ObservableProperty]
    public partial bool? IsChecked { get; set; }

    partial void OnIsCheckedChanged(bool? value) => Changed?.Invoke();

    public event Action? Changed;

    public bool IsChanged => IsChecked != Original;

    public void Revert() => IsChecked = Original;
}
