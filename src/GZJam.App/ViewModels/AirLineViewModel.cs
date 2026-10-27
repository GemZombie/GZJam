using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using GZJam.Audio;
using GZJam.Library;

namespace GZJam.App.ViewModels;

/// <summary>
/// The line that stands in for hidden decks (#164): the deck the room hears, with its play key,
/// time left and on-air bar, then what's next: the other deck's song if one is loaded, otherwise
/// the first song in the queue. With the queue in view, the decks sit atop it instead, in
/// <see cref="Order"/> (#177).
/// </summary>
public sealed partial class AirLineViewModel(MainWindowViewModel owner, MixEngine mix, QueueViewModel queue) : ObservableObject
{
    /// <summary>
    /// The deck the room hears: the one on air (the crossfader's side first, in a blend), else the
    /// loaded deck on the crossfader's side, ready to start. Null when there's neither.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNow))]
    public partial DeckViewModel? Now { get; private set; }

    public bool HasNow => Now is not null;

    /// <summary>What's next, "Title · Artist"; empty when nothing is loaded or queued.</summary>
    [ObservableProperty]
    public partial string NextText { get; private set; } = "";

    /// <summary>Where it's waiting: "NEXT · DECK 2", "NEXT · QUEUE", or "NEXT" with nothing.</summary>
    [ObservableProperty]
    public partial string NextFrom { get; private set; } = "NEXT";

    /// <summary>
    /// The decks in the order the room will hear them, for the rows atop the queue (#177): the one
    /// it hears now (or the crossfader's side, with nothing on air), then the others in turn.
    /// </summary>
    public ObservableCollection<DeckViewModel> Order { get; } = [];

    /// <summary>Called on every playback tick; cheap, and only changes what changed.</summary>
    public void Refresh()
    {
        var decks = owner.ActiveDecks;
        DeckViewModel? live = decks.FirstOrDefault(d => d.Deck == mix.LiveDeck);
        Now = live is { IsOnAir: true } ? live
            : decks.FirstOrDefault(d => d.IsOnAir)
            ?? (live is { HasTrack: true } ? live : null);
        UpdateOrder(decks, Now ?? live);

        if (decks.Where(d => d != Now && d.HasTrack).OrderBy(d => d.IsOnAir).FirstOrDefault() is { } other)
        {
            NextFrom = $"NEXT · DECK {other.Label}";
            NextText = Describe(other.Track!);
        }
        else if (queue.Items.FirstOrDefault() is { } first)
        {
            NextFrom = "NEXT · QUEUE";
            NextText = Describe(first.Track);
        }
        else
        {
            NextFrom = "NEXT";
            NextText = "";
        }
    }

    private void UpdateOrder(IReadOnlyList<DeckViewModel> decks, DeckViewModel? first)
    {
        int start = first is null ? 0 : Math.Max(0, IndexOf(decks, first));
        var order = Enumerable.Range(0, decks.Count).Select(i => decks[(start + i) % decks.Count]).ToList();
        if (order.SequenceEqual(Order))
            return;
        for (int i = 0; i < order.Count; i++)
        {
            int at = Order.IndexOf(order[i]);
            if (at < 0)
                Order.Insert(i, order[i]);
            else if (at != i)
                Order.Move(at, i);
        }
        while (Order.Count > order.Count)
            Order.RemoveAt(Order.Count - 1);
    }

    private static int IndexOf(IReadOnlyList<DeckViewModel> decks, DeckViewModel deck)
    {
        for (int i = 0; i < decks.Count; i++)
            if (decks[i] == deck)
                return i;
        return -1;
    }

    private static string Describe(Track track) =>
        string.IsNullOrWhiteSpace(track.Artist) ? track.DisplayTitle : $"{track.DisplayTitle} · {track.Artist}";
}
