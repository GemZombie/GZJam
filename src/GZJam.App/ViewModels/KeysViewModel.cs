using System.ComponentModel;
using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace GZJam.App.ViewModels;

/// <summary>
/// Settings › Keys (#135): each action with its key; click one and press a new key. A key another
/// action had moves here, and the page says which lost it. Delete leaves an action with no key;
/// Escape keeps the one it had. RESET puts every key back as GZJam has them.
/// </summary>
public sealed partial class KeysViewModel : ObservableObject
{
    private readonly MainWindowViewModel _owner;
    private readonly KeyMap _map;

    public KeysViewModel(MainWindowViewModel owner)
    {
        _owner = owner;
        _map = owner.Keys;
        Groups =
        [
            .. Enumerable.Range(1, 4).Select(deck => new KeyGroupViewModel($"DECK {deck}", KeyMap.Actions.Where(a => a.Deck == deck), this)),
            new KeyGroupViewModel("MIXER", KeyMap.Actions.Where(a => a.Command is >= KeyCommand.CrossfadeTo1 and <= KeyCommand.PhonesDown), this),
            new KeyGroupViewModel("SCREEN", KeyMap.Actions.Where(a => a.Command >= KeyCommand.Search), this),
        ];
        ShowDecks();
        _map.Changed += (_, _) =>
        {
            foreach (KeyRowViewModel row in Groups.SelectMany(g => g.Rows))
                row.Refresh();
            OnPropertyChanged(nameof(CanReset));
        };
        owner.PropertyChanged += OnOwnerChanged;
    }

    public IReadOnlyList<KeyGroupViewModel> Groups { get; }

    /// <summary>The row waiting for its new key, if one is.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChoosing))]
    public partial KeyRowViewModel? Choosing { get; private set; }

    public bool IsChoosing => Choosing is not null;

    /// <summary>What just happened, or what to do next: shown above the list.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotice))]
    public partial string Notice { get; private set; } = "";

    public bool HasNotice => Notice.Length > 0;

    public bool CanReset => !_map.AllDefaults;

    /// <summary>A row's key was clicked: the next key pressed is its new one.</summary>
    [RelayCommand]
    private void Choose(KeyRowViewModel row)
    {
        if (Choosing is { } before)
            before.IsChoosing = false;
        Choosing = row;
        row.IsChoosing = true;
        Notice = $"Press the new key for {row.FullName}. Delete for no key; Escape to keep {(row.HasKey ? row.KeysText : "it as it is")}.";
    }

    /// <summary>Stops waiting for a key (Escape, or the keyboard went elsewhere), keeping the key it had.</summary>
    public void Cancel()
    {
        if (Choosing is not { } row)
            return;
        row.IsChoosing = false;
        Choosing = null;
        Notice = "";
    }

    /// <summary>
    /// A key pressed while a row waits for one. Returns false when nothing is waiting, so the key
    /// goes on as usual. A modifier on its own waits for the rest.
    /// </summary>
    public bool Press(Key key, KeyModifiers modifiers)
    {
        if (Choosing is not { } row)
            return false;
        if (key == Key.Escape && modifiers == KeyModifiers.None)
        {
            Cancel();
            return true;
        }
        if (KeyChord.From(key, modifiers) is not { } chord)
        {
            Notice = "That key belongs to your computer's system: choose another.";
            return true;
        }
        if (chord.IsModifierKey)
            return true;
        if (key is Key.Delete or Key.Back && modifiers == KeyModifiers.None)
        {
            Done(row);
            _map.Set(row.Action, null);
            Notice = $"{row.FullName} has no key now.";
            Log.Info($"Keys: {row.Action.Id} has no key");
            return true;
        }
        if (chord.Refusal() is { } refusal)
        {
            Notice = refusal;
            return true;
        }
        Done(row);
        KeyAction? from = _map.Set(row.Action, chord);
        Log.Info($"Keys: {row.Action.Id} is {chord.Saved}{(from is { } f ? $", taken from {f.Id}" : "")}");
        Notice = from is { } lost
            ? _map.Text(lost) is { } left
                ? $"{chord.Display} was also for {FullName(lost)}, which keeps {left}."
                : $"{chord.Display} was for {FullName(lost)}, which has no key now."
            : "";
        return true;
    }

    private void Done(KeyRowViewModel row)
    {
        row.IsChoosing = false;
        Choosing = null;
    }

    [RelayCommand]
    private void Reset()
    {
        Cancel();
        _map.ResetAll();
        Notice = "Every key is back as GZJam has them.";
        Log.Info("Keys: all reset");
    }

    /// <summary>Settings closed: a row left waiting stops.</summary>
    public void Closed()
    {
        Cancel();
        Notice = "";
    }

    internal KeyMap Map => _map;

    /// <summary>"Deck 1 › Fade out", "Auto-play on or off".</summary>
    internal static string FullName(KeyAction action) => action.Deck > 0 ? $"Deck {action.Deck} › {action.Name}" : action.Name;

    private void OnOwnerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.FourDecks))
            ShowDecks();
    }

    /// <summary>Decks 3 and 4 only with four decks.</summary>
    private void ShowDecks()
    {
        Groups[2].IsShown = Groups[3].IsShown = _owner.FourDecks;
    }
}

/// <summary>A heading on Settings › Keys and its actions.</summary>
public sealed partial class KeyGroupViewModel : ObservableObject
{
    public KeyGroupViewModel(string title, IEnumerable<KeyAction> actions, KeysViewModel page)
    {
        Title = title;
        Rows = [.. actions.Select((a, i) => new KeyRowViewModel(a, page, i == 0))];
    }

    public string Title { get; }
    public IReadOnlyList<KeyRowViewModel> Rows { get; }

    [ObservableProperty]
    public partial bool IsShown { get; set; } = true;
}

/// <summary>One action on Settings › Keys, with its key.</summary>
public sealed partial class KeyRowViewModel : ObservableObject
{
    private readonly KeysViewModel _page;

    public KeyRowViewModel(KeyAction action, KeysViewModel page, bool isFirst)
    {
        Action = action;
        _page = page;
        IsFirst = isFirst;
    }

    public KeyAction Action { get; }
    public KeysViewModel Page => _page;
    public string Name => Action.Name;
    public string FullName => KeysViewModel.FullName(Action);
    public bool IsFirst { get; }

    /// <summary>What it does, and if the DJ changed its key, GZJam's own.</summary>
    public string? Help
    {
        get
        {
            // Changed: GZJam's own key too, unless it has none (fade out says so already).
            if (!_page.Map.IsChanged(Action) || KeyMap.Defaults(Action) is not { Count: > 0 } keys)
                return Action.Help;
            string own = $"GZJam's own: {string.Join(" or ", keys.Select(k => k.Display))}.";
            return Action.Help is { } help ? $"{help} {own}" : own;
        }
    }

    public bool HasHelp => Help is not null;

    public bool HasKey => _page.Map.KeysFor(Action).Count > 0;

    /// <summary>"Ctrl+P", "Ctrl+, or Ctrl+O", or "No key".</summary>
    public string KeysText => _page.Map.Text(Action) ?? "No key";

    /// <summary>On its key: what it is, or that a new one is wanted.</summary>
    public string ButtonText => IsChoosing ? "PRESS A KEY…" : KeysText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ButtonText))]
    public partial bool IsChoosing { get; set; }

    internal void Refresh()
    {
        OnPropertyChanged(nameof(KeysText));
        OnPropertyChanged(nameof(ButtonText));
        OnPropertyChanged(nameof(HasKey));
        OnPropertyChanged(nameof(Help));
        OnPropertyChanged(nameof(HasHelp));
    }
}
