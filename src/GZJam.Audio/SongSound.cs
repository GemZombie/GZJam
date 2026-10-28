using ManagedBass;
using ManagedBass.Fx;

namespace GZJam.Audio;

/// <summary>
/// A song's EQ and evening out, on the BASS channel it's decoded through: a deck's, or the
/// headphone preview's, so a song previewed sounds as it will on a deck (#137). Kept across
/// songs: <see cref="Attach"/> puts the current settings on each new channel.
/// </summary>
internal sealed class SongSound(string owner)
{
    private static readonly (BQFType Type, float Hz)[] EqBands =
        [(BQFType.LowShelf, 150), (BQFType.PeakingEQ, 1000), (BQFType.HighShelf, 5000)];

    private readonly double[] _eqDb = new double[3];   // low, mid, high
    private readonly int[] _eqFx = new int[3];
    private double _midHz = 1000;
    private Deck.Evening _even;
    private int _evenFx;                               // the compressor, only while evening out
    private int _channel;

    /// <summary>EQ in dB (0 = flat).</summary>
    public (double Low, double Mid, double High) Eq => (_eqDb[0], _eqDb[1], _eqDb[2]);

    /// <summary>Where the mid band sits, in Hz.</summary>
    public double MidHz => _midHz;

    /// <summary>Puts the EQ and evening out on a new channel (the last one's went with its stream).</summary>
    public void Attach(int channel)
    {
        Detach();
        _channel = channel;
        for (int band = 0; band < EqBands.Length; band++)
        {
            _eqFx[band] = Bass.ChannelSetFX(_channel, EffectType.BQF, band);
            if (_eqFx[band] == 0)
                throw new AudioException($"Could not add EQ to {owner}", Bass.LastError);
            ApplyEq(band);
        }
        ApplyEven();
    }

    /// <summary>The channel is gone (its effects freed with it).</summary>
    public void Detach()
    {
        _channel = 0;
        Array.Clear(_eqFx);
        _evenFx = 0;
    }

    public void SetEq(double low, double mid, double high, double midHz = 1000)
    {
        (_eqDb[0], _eqDb[1], _eqDb[2]) = (low, mid, high);
        _midHz = Math.Clamp(midHz, 20, 20000);
        for (int band = 0; band < EqBands.Length; band++)
            ApplyEq(band);
    }

    public Deck.Evening Even
    {
        get => _even;
        set
        {
            value = value with { Amount = Math.Clamp(value.Amount, 0, 1) };
            if (value == _even)
                return;
            _even = value;
            ApplyEven();
        }
    }

    private void ApplyEven()
    {
        if (_channel == 0)
            return;
        if (_even.Amount <= 0)
        {
            if (_evenFx != 0)
                Bass.ChannelRemoveFX(_channel, _evenFx);
            _evenFx = 0;
            return;
        }
        if (_evenFx == 0)
        {
            // Priority below the EQ's (0 to 2): BASS runs higher priorities first, so this comes after it.
            _evenFx = Bass.ChannelSetFX(_channel, EffectType.Compressor, -1);
            if (_evenFx == 0)
                throw new AudioException($"Could not add evening out to {owner}", Bass.LastError);
        }
        var parameters = new CompressorParameters
        {
            fGain = (float)_even.MakeUpDb,
            fThreshold = (float)_even.ThresholdDb,
            fRatio = (float)_even.Ratio,
            fAttack = 30,     // ms: slow enough to keep the attack of a drum or a horn
            fRelease = 400,   // ms: gentle, so quiet passages don't pump back up
            lChannel = FXChannelFlags.All,
        };
        if (!Bass.FXSetParameters(_evenFx, parameters))
            throw new AudioException($"Could not set {owner}'s evening out", Bass.LastError);
    }

    private void ApplyEq(int band)
    {
        if (_eqFx[band] == 0)
            return;
        var (type, hz) = EqBands[band];
        bool shelf = type != BQFType.PeakingEQ;
        // BASS takes a shelf's slope or a bell's width (Q), and refuses a filter given both.
        var parameters = new BQFParameters
        {
            lFilter = type,
            fCenter = type == BQFType.PeakingEQ ? (float)_midHz : hz,
            fGain = (float)_eqDb[band],
            fBandwidth = 0,
            fQ = shelf ? 0 : 0.7f,   // the bell: about two octaves wide
            fS = shelf ? 0.8f : 0,   // the shelves: a gentle slope
            lChannel = FXChannelFlags.All,
        };
        if (!Bass.FXSetParameters(_eqFx[band], parameters))
            throw new AudioException($"Could not set {owner}'s EQ", Bass.LastError);
    }
}
