using CommunityToolkit.Mvvm.ComponentModel;
using GZJam.Audio;
using GZJam.Library;

namespace GZJam.App.ViewModels;

/// <summary>
/// The line that stands in for hidden decks (#164): the deck the room hears, with its play key,
/// time left and on-air bar, then what's next: the other deck's song if one is loaded, otherwise
/// the first song in the queue.
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

    /// <summary>Called on every playback tick; cheap, and only changes what changed.</summary>
    public void Refresh()
    {
        var decks = owner.ActiveDecks;
        DeckViewModel? live = decks.FirstOrDefault(d => d.Deck == mix.LiveDeck);
        Now = live is { IsOnAir: true } ? live
            : decks.FirstOrDefault(d => d.IsOnAir)
            ?? (live is { HasTrack: true } ? live : null);

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

    private static string Describe(Track track) =>
        string.IsNullOrWhiteSpace(track.Artist) ? track.DisplayTitle : $"{track.DisplayTitle} · {track.Artist}";
}
