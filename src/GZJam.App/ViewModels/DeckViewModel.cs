using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GZJam.Audio;
using GZJam.Library;

namespace GZJam.App.ViewModels;

/// <summary>One deck: what's loaded, transport, tempo, and the big readout.</summary>
public sealed partial class DeckViewModel : ObservableObject
{
    private readonly MainWindowViewModel _owner;
    private readonly AppSettings _settings;

    public DeckViewModel(MainWindowViewModel owner, Deck deck, AppSettings settings)
    {
        _owner = owner;
        _settings = settings;
        Deck = deck;
        Readout = settings.DeckReadouts.GetValueOrDefault(Letter, DeckReadout.Remaining);
    }

    public Deck Deck { get; }
    public string Letter => Deck.Name;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTrack), nameof(Title), nameof(Subtitle), nameof(TrackBpm))]
    public partial Track? Track { get; private set; }

    [ObservableProperty]
    public partial DeckReadout Readout { get; private set; }

    /// <summary>Shown briefly under the controls, e.g. "Hold to pause the live deck".</summary>
    [ObservableProperty]
    public partial string? Hint { get; private set; }

    public bool HasTrack => Track is not null;
    public bool IsPlaying => Deck.IsPlaying;

    /// <summary>The room is hearing this deck (the crossfader leans to it).</summary>
    public bool IsLive => HasTrack && _owner.Mix?.LiveDeck == Deck;

    /// <summary>Playing to the room right now: needs protecting from stray loads and touches.</summary>
    public bool IsOnAir => IsLive && IsPlaying;

    public bool IsCue => HasTrack && !IsLive;

    public string Status => !HasTrack ? "" : IsOnAir ? "ON AIR" : IsLive ? "LIVE · PAUSED" : "CUE";

    public string Title => Track?.DisplayTitle ?? "No track loaded";

    public string Subtitle => Track is { } t
        ? string.Join(" · ", new[] { t.Artist, t.Album, t.Year?.ToString(CultureInfo.InvariantCulture) }.Where(s => !string.IsNullOrWhiteSpace(s)))
        : "Drag a track here, or right-click › Load to Deck " + Letter;

    /// <summary>0..1 through the track.</summary>
    public double Progress => Deck.Length > TimeSpan.Zero ? Deck.Position / Deck.Length : 0;

    /// <summary>Seeking the deck the room is dancing to is blocked; cue it on the other deck instead.</summary>
    public bool CanSeek => HasTrack && !IsOnAir;

    public double? TrackBpm => Track?.Bpm;
    public double? CurrentBpm => TrackBpm * (1 + Deck.TempoPercent / 100);

    public double TempoPercent
    {
        get => Deck.TempoPercent;
        set
        {
            if (Math.Abs(Deck.TempoPercent - value) < 0.001)
                return;
            Deck.TempoPercent = Math.Round(value, 1);
            OnPropertyChanged();
            OnPropertyChanged(nameof(TempoText));
            RefreshReadout();
        }
    }

    public string TempoText => Deck.TempoPercent.ToString("+0.0'%';-0.0'%';'0.0%'", CultureInfo.CurrentCulture);

    public string BpmText => CurrentBpm is { } bpm ? Format.Bpm(Math.Round(bpm, 1)) : "—";

    public string BpmCaption => TrackBpm is { } original
        ? Deck.TempoPercent == 0 ? "BPM" : $"BPM · from {Format.Bpm(original)}"
        : "BPM unknown";

    public string ReadoutText => Readout switch
    {
        DeckReadout.Elapsed => Format.Duration(Deck.Position),
        DeckReadout.Bpm => BpmText,
        _ => HasTrack ? "-" + Format.Duration(Deck.Remaining) : "-0:00",
    };

    public string ReadoutCaption => Readout switch
    {
        DeckReadout.Elapsed => $"elapsed of {Format.Duration(Deck.Length)}",
        DeckReadout.Bpm => $"{BpmCaption} · {TempoText}",
        _ => $"{Format.Duration(Deck.Position)} / {Format.Duration(Deck.Length)}",
    };

    // ───────────── Commands ─────────────

    [RelayCommand]
    private void CycleReadout()
    {
        Readout = Readout switch
        {
            DeckReadout.Remaining => DeckReadout.Elapsed,
            DeckReadout.Elapsed => DeckReadout.Bpm,
            _ => DeckReadout.Remaining,
        };
        _settings.DeckReadouts[Letter] = Readout;
        RefreshReadout();
    }

    /// <summary>
    /// Whether pausing needs a press-and-hold: true for the deck the room is dancing to,
    /// unless the DJ chose tap-to-pause in Settings.
    /// </summary>
    public bool PauseNeedsHold => IsOnAir && _settings.LiveDeckPause == LiveDeckPause.Hold;

    /// <summary>A tap on play/pause.</summary>
    [RelayCommand]
    private void TogglePlay()
    {
        if (!HasTrack)
            return;
        if (IsPlaying)
        {
            if (PauseNeedsHold)
            {
                ShowHint("Hold to pause the live deck");
                return;
            }
            Deck.Pause();
        }
        else
        {
            Deck.Play();
        }
        Refresh();
    }

    /// <summary>A completed press-and-hold on play/pause: always pauses.</summary>
    public void HoldPause()
    {
        if (IsPlaying)
            Deck.Pause();
        Refresh();
    }

    [RelayCommand]
    private void FadeOut()
    {
        Deck.FadeOut(TimeSpan.FromSeconds(_settings.FadeOutSeconds));
        Refresh();
    }

    [RelayCommand]
    private void Eject()
    {
        if (IsPlaying)
            return;
        Deck.Unload();
        Track = null;
        Refresh();
    }

    [RelayCommand]
    private void ResetTempo() => TempoPercent = 0;

    public void SeekTo(double fraction)
    {
        if (!CanSeek)
            return;
        Deck.Seek(Deck.Length * Math.Clamp(fraction, 0, 1));
        Refresh();
    }

    /// <summary>Loads a track (the caller has already checked deck protection).</summary>
    internal void Load(Track track)
    {
        Deck.Load(track.Path);
        Deck.TempoPercent = 0;
        Track = track;
        OnPropertyChanged(nameof(TempoPercent));
        OnPropertyChanged(nameof(TempoText));
        Refresh();
    }

    private async void ShowHint(string text)
    {
        Hint = text;
        await Task.Delay(2500);
        if (Hint == text)
            Hint = null;
    }

    /// <summary>Re-reads everything that changes as the track plays.</summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(IsPlaying));
        OnPropertyChanged(nameof(IsLive));
        OnPropertyChanged(nameof(IsOnAir));
        OnPropertyChanged(nameof(IsCue));
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(CanSeek));
        OnPropertyChanged(nameof(Progress));
        OnPropertyChanged(nameof(PauseNeedsHold));
        RefreshReadout();
    }

    private void RefreshReadout()
    {
        OnPropertyChanged(nameof(CurrentBpm));
        OnPropertyChanged(nameof(BpmText));
        OnPropertyChanged(nameof(BpmCaption));
        OnPropertyChanged(nameof(ReadoutText));
        OnPropertyChanged(nameof(ReadoutCaption));
    }
}
