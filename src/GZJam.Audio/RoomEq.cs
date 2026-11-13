using System.Numerics;
using System.Text.Json.Serialization;

namespace GZJam.Audio;

/// <summary>How a room EQ is seen and set (#272): three bands, as on a song, or a 10-band graphic EQ.</summary>
public enum RoomEqView
{
    Simple,
    Advanced,
}

/// <summary>A filter's shape: the room EQ's are a low shelf, bells, and a high shelf.</summary>
public enum RoomFilterKind
{
    LowShelf,
    Peak,
    HighShelf,
}

/// <summary>
/// One filter of a room EQ: its shape, where it sits, its gain, and its width (a bell's Q, a
/// shelf's slope), as BASS's BQF effect takes them.
/// </summary>
public readonly record struct RoomFilter(RoomFilterKind Kind, double Hz, double GainDb, double Width);

/// <summary>
/// The EQ on the speakers for a room (#272): a hall that booms, a muddy room, a packed one that
/// swallows the warmth. One EQ with two ways to see and set it. <see cref="RoomEqView.Simple"/>:
/// low, mid (its frequency movable) and high, as the song EQ. <see cref="RoomEqView.Advanced"/>:
/// a graphic EQ, one band per octave from 31 Hz to 16 kHz. Only the view chosen is heard; the
/// other is kept as it was. Cuts go deeper than boosts (<see cref="MinDb"/>, <see cref="MaxDb"/>),
/// since a boost lights LIMIT sooner. Immutable: a change is a new one.
/// </summary>
public sealed record RoomEq
{
    public const double MinDb = -12, MaxDb = 6;

    /// <summary>The advanced view's bands: their centers, one octave apart.</summary>
    public static IReadOnlyList<double> BandHz { get; } = [31.5, 63, 125, 250, 500, 1000, 2000, 4000, 8000, 16000];

    /// <summary>The filters on the speakers: the simple view's three, then the advanced view's ten.</summary>
    public const int FilterCount = 3 + 10;

    // As the song EQ's (SongSound), so the three bands feel the same on a song and on a room.
    private const double LowHz = 150, HighHz = 5000, ShelfSlope = 0.8, MidQ = 0.7;

    /// <summary>One octave wide: neighboring bands meet about 3 dB down, so the bands add up smoothly.</summary>
    private const double BandQ = 1.41;

    public static RoomEq Flat { get; } = new();

    public RoomEqView View { get; init; }

    /// <summary>The simple view, in dB.</summary>
    public double Low { get; init; }
    public double Mid { get; init; }
    public double High { get; init; }

    /// <summary>Where the simple view's mid band sits.</summary>
    public double MidHz { get; init; } = 1000;

    /// <summary>The advanced view, in dB: one for each of <see cref="BandHz"/>.</summary>
    public IReadOnlyList<double> Bands
    {
        get => _bands;
        init => _bands = [.. Enumerable.Range(0, BandHz.Count).Select(i => i < value.Count ? value[i] : 0)];
    }

    private readonly double[] _bands = new double[10];

    /// <summary>Nothing changed: the speakers get the music as it is.</summary>
    [JsonIgnore]
    public bool IsFlat => Filters.All(f => f.GainDb == 0);

    /// <summary>
    /// The filters, <see cref="FilterCount"/> of them: the simple view's (low shelf, mid bell, high
    /// shelf), then a bell for each advanced band. The view not chosen is flat.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<RoomFilter> Filters
    {
        get
        {
            bool simple = View == RoomEqView.Simple;
            var filters = new RoomFilter[FilterCount];
            filters[0] = new(RoomFilterKind.LowShelf, LowHz, simple ? Clamp(Low) : 0, ShelfSlope);
            filters[1] = new(RoomFilterKind.Peak, Math.Clamp(MidHz, 20, 20000), simple ? Clamp(Mid) : 0, MidQ);
            filters[2] = new(RoomFilterKind.HighShelf, HighHz, simple ? Clamp(High) : 0, ShelfSlope);
            for (int i = 0; i < BandHz.Count; i++)
                filters[3 + i] = new(RoomFilterKind.Peak, BandHz[i], simple ? 0 : Clamp(_bands[i]), BandQ);
            return filters;
        }
    }

    private static double Clamp(double db) => Math.Clamp(db, MinDb, MaxDb);

    /// <summary>What the room gets at <paramref name="hz"/>, in dB: the curve both views show.</summary>
    public double ResponseDb(double hz, int sampleRate = MixEngine.SampleRate) => ResponseDb(Filters, hz, sampleRate);

    private static double ResponseDb(IEnumerable<RoomFilter> filters, double hz, int sampleRate) =>
        filters.Where(f => f.GainDb != 0).Sum(f => FilterDb(f, hz, sampleRate));

    /// <summary>
    /// The advanced view shaped like the simple one, so switching to it doesn't change what the
    /// room hears: each band set so the curve matches the three bands' at its center.
    /// </summary>
    public RoomEq WithAdvancedFromSimple() => this with { View = RoomEqView.Advanced, Bands = BandsLikeSimple() };

    /// <summary>
    /// The advanced bands are still just the simple view's shape (as <see cref="WithAdvancedFromSimple"/>
    /// made them), so the simple view can show this EQ. Otherwise it says *Set in Advanced*.
    /// </summary>
    [JsonIgnore]
    public bool AdvancedIsSimple => BandsLikeSimple().Zip(_bands).All(b => Math.Abs(b.First - b.Second) < 0.05);

    private double[] BandsLikeSimple()
    {
        RoomEq simple = this with { View = RoomEqView.Simple };
        double[] target = [.. BandHz.Select(hz => simple.ResponseDb(hz))];
        // Neighboring bands overlap, so each is set, the curve measured, and the difference made up:
        // a few rounds bring it within a fraction of a dB.
        double[] bands = [.. target.Select(Clamp)];
        for (int round = 0; round < 8; round++)
        {
            RoomEq advanced = this with { View = RoomEqView.Advanced, Bands = bands };
            for (int i = 0; i < bands.Length; i++)
                bands[i] = Clamp(bands[i] + target[i] - advanced.ResponseDb(BandHz[i]));
        }
        return [.. bands.Select(b => Math.Round(b, 1))];
    }

    /// <summary>A filter's gain at <paramref name="hz"/>, in dB, from the same formulas BASS uses (RBJ's cookbook).</summary>
    private static double FilterDb(RoomFilter f, double hz, int sampleRate)
    {
        double a = Math.Pow(10, f.GainDb / 40), w0 = 2 * Math.PI * f.Hz / sampleRate;
        double cos = Math.Cos(w0), sin = Math.Sin(w0);
        double b0, b1, b2, a0, a1, a2;
        if (f.Kind == RoomFilterKind.Peak)
        {
            double alpha = sin / (2 * f.Width);
            (b0, b1, b2) = (1 + alpha * a, -2 * cos, 1 - alpha * a);
            (a0, a1, a2) = (1 + alpha / a, -2 * cos, 1 - alpha / a);
        }
        else
        {
            double alpha = sin / 2 * Math.Sqrt((a + 1 / a) * (1 / f.Width - 1) + 2), root = 2 * Math.Sqrt(a) * alpha;
            if (f.Kind == RoomFilterKind.LowShelf)
            {
                (b0, b1, b2) = (a * ((a + 1) - (a - 1) * cos + root), 2 * a * ((a - 1) - (a + 1) * cos), a * ((a + 1) - (a - 1) * cos - root));
                (a0, a1, a2) = ((a + 1) + (a - 1) * cos + root, -2 * ((a - 1) + (a + 1) * cos), (a + 1) + (a - 1) * cos - root);
            }
            else
            {
                (b0, b1, b2) = (a * ((a + 1) + (a - 1) * cos + root), -2 * a * ((a - 1) + (a + 1) * cos), a * ((a + 1) + (a - 1) * cos - root));
                (a0, a1, a2) = ((a + 1) - (a - 1) * cos + root, 2 * ((a - 1) - (a + 1) * cos), (a + 1) - (a - 1) * cos - root);
            }
        }
        Complex z = Complex.FromPolarCoordinates(1, -2 * Math.PI * hz / sampleRate);
        Complex h = (b0 + b1 * z + b2 * z * z) / (a0 + a1 * z + a2 * z * z);
        return 20 * Math.Log10(h.Magnitude);
    }

    public bool Equals(RoomEq? other) =>
        other is not null && View == other.View && Low == other.Low && Mid == other.Mid && High == other.High
        && MidHz == other.MidHz && _bands.AsSpan().SequenceEqual(other._bands);

    public override int GetHashCode() => HashCode.Combine(View, Low, Mid, High, MidHz, _bands.Sum());
}
