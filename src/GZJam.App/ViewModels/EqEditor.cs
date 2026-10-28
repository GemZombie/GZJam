using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GZJam.Library;

namespace GZJam.App.ViewModels;

/// <summary>
/// The EQ panel's controls for one song: a deck's, or one picked in the library, the queue, a
/// playlist or History, while preparing a set (#137). Changes are kept to half-dB steps within
/// range, heard at once (<paramref name="apply"/>: the deck, or the headphone preview) and saved
/// for the song (<paramref name="store"/>).
/// </summary>
public sealed partial class EqEditor(Func<string> heading, Func<string> title, Func<string> level,
    Action<TrackEq> apply, Action<TrackEq> store) : ObservableObject
{
    private TrackEq _eq;

    /// <summary>For a song that isn't on a deck: previews it in the headphones, to hear the EQ.</summary>
    public IRelayCommand? PreviewCommand { get; init; }

    public bool CanPreview => PreviewCommand is not null;

    /// <summary>The settings as they are now.</summary>
    public TrackEq Value => _eq;

    /// <summary>"DECK 1 · EQ", or "EQ" for a song that isn't on a deck.</summary>
    public string Heading => heading();

    public string Title => title();

    /// <summary>The song's measured loudness and what matching does to it.</summary>
    public string LevelText => level();

    /// <summary>The heading, title or loudness may have changed.</summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(Heading));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(LevelText));
    }

    /// <summary>EQ in dB, 0 = flat.</summary>
    public double Low
    {
        get => _eq.Low;
        set => Set(_eq with { Low = value });
    }

    public double Mid
    {
        get => _eq.Mid;
        set => Set(_eq with { Mid = value });
    }

    public double High
    {
        get => _eq.High;
        set => Set(_eq with { High = value });
    }

    /// <summary>The DJ's own gain for this song, on top of loudness matching (the same range as the bands).</summary>
    public double Gain
    {
        get => _eq.Gain;
        set => Set(_eq with { Gain = value });
    }

    /// <summary>How much the song is evened out (a compressor): 0 (off) to 100.</summary>
    public double Even
    {
        get => _eq.Even;
        set => Set(_eq with { Even = value });
    }

    public string LowText => Db(_eq.Low);
    public string MidText => Db(_eq.Mid);
    public string HighText => Db(_eq.High);
    public string GainText => Db(_eq.Gain);
    public string EvenText => _eq.Even == 0 ? "Off" : _eq.Even.ToString("0", CultureInfo.CurrentCulture);

    /// <summary>Where the mid band sits, e.g. "1 kHz", "315 Hz".</summary>
    public string MidHzText => Hz(_eq.MidHzOrDefault);

    public bool IsMidMoved => !_eq.IsMidAtDefault;

    /// <summary>The song has EQ set (an EQ button lights up).</summary>
    public bool IsSet => !_eq.IsFlat;

    /// <summary>For the activity log.</summary>
    public string Describe() => $"low {LowText}, mid {MidText} at {MidHzText}, high {HighText}, gain {GainText}, even {EvenText}";

    public double MinDb => TrackEq.MinDb;
    public double MaxDb => TrackEq.MaxDb;
    public double MaxEven => TrackEq.MaxEven;

    internal static string Hz(double hz) => hz >= 1000
        ? (hz / 1000).ToString("0.##", CultureInfo.CurrentCulture) + " kHz"
        : hz.ToString("0", CultureInfo.CurrentCulture) + " Hz";

    internal static string Db(double db) =>
        db == 0 ? "0 dB" : db.ToString("+0.#' dB';−0.#' dB'", CultureInfo.CurrentCulture);

    [RelayCommand]
    private void Flat() => Set(TrackEq.Flat);

    /// <summary>A starting point for a hissy transfer: the treble down 6 dB.</summary>
    [RelayCommand]
    private void LessHiss() => Set(_eq with { High = -6 });

    /// <summary>Tapping a band's value puts that band back to flat.</summary>
    [RelayCommand]
    private void FlatBand(string band) => Set(band switch
    {
        "low" => _eq with { Low = 0 },
        "mid" => _eq with { Mid = 0 },
        "gain" => _eq with { Gain = 0 },
        "even" => _eq with { Even = 0 },
        "midhz" => _eq with { MidHz = TrackEq.DefaultMidHz },
        _ => _eq with { High = 0 },
    });

    /// <summary>Moves the mid band one third of an octave down (-1) or up (+1).</summary>
    [RelayCommand]
    private void MoveMid(string direction)
    {
        var steps = TrackEq.MidFrequencies;
        int at = NearestMid(_eq.MidHzOrDefault);
        int to = Math.Clamp(at + (direction == "down" ? -1 : 1), 0, steps.Count - 1);
        Set(_eq with { MidHz = steps[to] });
    }

    private static int NearestMid(double hz)
    {
        var steps = TrackEq.MidFrequencies;
        int best = 0;
        for (int i = 1; i < steps.Count; i++)
        {
            if (Math.Abs(Math.Log(steps[i] / hz)) < Math.Abs(Math.Log(steps[best] / hz)))
                best = i;
        }
        return best;
    }

    /// <summary>Applies EQ (in half-dB steps, within range) and, if <paramref name="save"/>, saves it for the song.</summary>
    public void Set(TrackEq eq, bool save = true)
    {
        static double Step(double db, double min, double max) => Math.Round(Math.Clamp(db, min, max) * 2) / 2;
        eq = new TrackEq(Step(eq.Low, TrackEq.MinDb, TrackEq.MaxDb), Step(eq.Mid, TrackEq.MinDb, TrackEq.MaxDb),
            Step(eq.High, TrackEq.MinDb, TrackEq.MaxDb), Step(eq.Gain, TrackEq.MinDb, TrackEq.MaxDb),
            TrackEq.MidFrequencies[NearestMid(eq.MidHzOrDefault)], Math.Round(Math.Clamp(eq.Even, 0, TrackEq.MaxEven) / 5) * 5);
        bool changed = eq != _eq;
        _eq = eq;
        apply(eq);
        if (!changed)
            return;
        if (save)
            store(eq);
        foreach (string name in new[] { nameof(Low), nameof(Mid), nameof(High), nameof(Gain), nameof(Even), nameof(LowText),
                     nameof(MidText), nameof(HighText), nameof(GainText), nameof(EvenText), nameof(MidHzText), nameof(IsMidMoved),
                     nameof(IsSet), nameof(LevelText), nameof(Value) })
            OnPropertyChanged(name);
    }
}
