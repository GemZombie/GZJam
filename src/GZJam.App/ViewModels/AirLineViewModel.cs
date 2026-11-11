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

        DeckViewModel? other = null;
        foreach (DeckViewModel d in decks)
        {
            if (d != Now && d.HasTrack && (other is null || other.IsOnAir && !d.IsOnAir))
                other = d;   // a loaded deck, one off air first
        }
        Track? next = other?.Track ?? (queue.Items.Count > 0 ? queue.Items[0].Track : null);
        if ((other, next) == _next && next?.DisplayTitle == _nextTitle && next?.DisplayArtist == _nextArtist)
            return;   // the same as on the last tick: no text made again (#365)
        (_next, _nextTitle, _nextArtist) = ((other, next), next?.DisplayTitle, next?.DisplayArtist);
        NextFrom = other is not null ? $"NEXT · DECK {other.Label}" : next is not null ? "NEXT · QUEUE" : "NEXT";
        NextText = next is null ? "" : Describe(next);
    }

    private bool InOrder(IReadOnlyList<DeckViewModel> decks, int start)
    {
        if (Order.Count != decks.Count)
            return false;
        for (int i = 0; i < decks.Count; i++)
        {
            if (Order[i] != decks[(start + i) % decks.Count])
                return false;
        }
        return true;
    }

    /// <summary>Where the next song came from on the last tick, and what it said, so its text is made only when it changes.</summary>
    private (DeckViewModel? Deck, Track? Track) _next;
    private string? _nextTitle, _nextArtist;

    private void UpdateOrder(IReadOnlyList<DeckViewModel> decks, DeckViewModel? first)
    {
        int start = first is null ? 0 : Math.Max(0, IndexOf(decks, first));
        if (InOrder(decks, start))
            return;   // as it was: the usual tick
        var order = Enumerable.Range(0, decks.Count).Select(i => decks[(start + i) % decks.Count]).ToList();
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
        track.DisplayArtist.Length == 0 ? track.DisplayTitle : $"{track.DisplayTitle} · {track.DisplayArtist}";
}
