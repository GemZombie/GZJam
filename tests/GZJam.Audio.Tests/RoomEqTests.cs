using GZJam.Testing;

namespace GZJam.Audio.Tests;

/// <summary>
/// The room's EQ on the speakers (#272): one EQ seen as three bands or ten, heard on the speakers
/// only, and what its curve says is what the room gets.
/// </summary>
[Collection("BASS")]
public sealed class RoomEqTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("gzjam-room-eq-").FullName;
    private readonly AudioEngine _engine = new();
    private readonly MixEngine _mix;

    public RoomEqTests() => _mix = new MixEngine(_engine, offline: true);

    public void Dispose()
    {
        _mix.Dispose();
        _engine.Dispose();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private Deck A => _mix.Decks[0];

    /// <summary>A tone at <paramref name="hz"/> playing on Deck A, to the room and the headphones.</summary>
    private void PlayTone(double hz)
    {
        A.Load(TestWav.WriteTone(Path.Combine(_dir, $"{hz}.wav"), TimeSpan.FromSeconds(10), frequency: hz));
        A.Cue = true;
        _mix.Crossfader = 0;
        A.Play();
        _mix.Render(TimeSpan.FromSeconds(0.2));
    }

    /// <summary>The level now on the speakers and headphones, once the filters have settled.</summary>
    private (float Main, float Cue) Levels()
    {
        _mix.Render(TimeSpan.FromSeconds(0.2));
        return _mix.Render(TimeSpan.FromSeconds(0.3));
    }

    private static double Gain(double db) => Math.Pow(10, db / 20);

    [Fact]
    public void Flat_has_no_filters_and_cuts_go_deeper_than_boosts()
    {
        Assert.True(RoomEq.Flat.IsFlat);
        Assert.Equal(RoomEq.FilterCount, RoomEq.Flat.Filters.Count);
        Assert.Contains(new RoomEq { Low = -3 }.WithAdvancedFromSimple().Bands, b => b != 0);

        var extreme = new RoomEq { Low = -30, High = 20 };
        Assert.Equal(RoomEq.MinDb, extreme.Filters[0].GainDb);
        Assert.Equal(RoomEq.MaxDb, extreme.Filters[2].GainDb);

        // Only the view chosen is heard; the other is kept as it was.
        var both = new RoomEq { Low = -6, Bands = [0, 0, 0, 0, 0, -9, 0, 0, 0, 0] };
        Assert.Equal(-6, both.Filters[0].GainDb);
        Assert.All(both.Filters.Skip(3), f => Assert.Equal(0, f.GainDb));
        var advanced = both with { View = RoomEqView.Advanced };
        Assert.All(advanced.Filters.Take(3), f => Assert.Equal(0, f.GainDb));
        Assert.Equal(-9, advanced.Filters[3 + 5].GainDb);
        Assert.Equal(-6, advanced.Low);
    }

    [Fact]
    public void Switching_to_advanced_keeps_the_curve_the_room_hears()
    {
        // A muddy room: less bass, and the low mids taken down.
        var simple = new RoomEq { Low = -6, Mid = -4, MidHz = 250, High = 2 };
        RoomEq advanced = simple.WithAdvancedFromSimple();

        Assert.Equal(RoomEqView.Advanced, advanced.View);
        foreach (double hz in RoomEq.BandHz)
            Assert.Equal(simple.ResponseDb(hz), advanced.ResponseDb(hz), 0.5);
        for (double hz = 40; hz < 12000; hz *= 1.25)   // between the bands too, near enough
            Assert.InRange(advanced.ResponseDb(hz) - simple.ResponseDb(hz), -1.5, 1.5);

        // Untouched since, the simple view can still show it; one band moved, it can't.
        Assert.True(advanced.AdvancedIsSimple);
        double[] moved = [.. advanced.Bands];
        moved[7] -= 3;
        Assert.False((advanced with { Bands = moved }).AdvancedIsSimple);
    }

    [Fact]
    public void The_speakers_get_the_curve_and_the_headphones_never_do()
    {
        PlayTone(60);
        var flat = Levels();

        var muddy = new RoomEq { Low = -9 };
        _mix.Main.RoomEq = muddy;
        var cut = Levels();

        Assert.Equal(flat.Main * Gain(muddy.ResponseDb(60)), cut.Main, flat.Main * 0.1);
        Assert.True(cut.Main < flat.Main * 0.5);
        Assert.Equal(flat.Cue, cut.Cue, 0.01f);   // cueing still sounds like the song

        _mix.Main.RoomEq = RoomEq.Flat;
        Assert.Equal(flat.Main, Levels().Main, 0.01f);
    }

    [Fact]
    public void An_advanced_band_takes_down_its_octave_and_leaves_the_others()
    {
        var harsh = new RoomEq { View = RoomEqView.Advanced, Bands = [0, 0, 0, 0, 0, -12, 0, 0, 0, 0] };

        PlayTone(1000);
        float flat1k = Levels().Main;
        _mix.Main.RoomEq = harsh;
        float cut1k = Levels().Main;
        Assert.Equal(flat1k * Gain(-12), cut1k, flat1k * 0.05);

        _mix.Main.RoomEq = RoomEq.Flat;
        PlayTone(125);
        float flat125 = Levels().Main;
        _mix.Main.RoomEq = harsh;
        Assert.Equal(flat125 * Gain(harsh.ResponseDb(125)), Levels().Main, flat125 * 0.05);
        Assert.True(Levels().Main > flat125 * 0.9);
    }

    [Fact]
    public void A_boost_is_still_caught_by_the_limiter()
    {
        A.Load(TestWav.WriteTone(Path.Combine(_dir, "loud.wav"), TimeSpan.FromSeconds(10), amplitude: 0.9, frequency: 60));
        _mix.Crossfader = 0;
        A.Play();
        _mix.Main.RoomEq = new RoomEq { Low = 6 };

        Assert.InRange(Levels().Main, 0, OutputBus.Ceiling + 0.005f);
    }

    [Fact]
    public void The_room_eq_goes_with_the_speakers_to_a_new_output()
    {
        // Choosing other speakers, or them coming back after being unplugged, makes a new output
        // that takes the old one's settings (MixEngine.Open): the room EQ among them.
        var room = new RoomEq { Low = -6, Mid = -3, MidHz = 300 };
        _mix.Main.RoomEq = room;
        int mixer = ManagedBass.Mix.BassMix.CreateMixerStream(MixEngine.SampleRate, MixEngine.Channels,
            ManagedBass.BassFlags.Decode | ManagedBass.BassFlags.Float);
        using var replacement = new OutputBus(mixer, null);

        replacement.CopySettingsFrom(_mix.Main);

        Assert.Equal(room, replacement.RoomEq);
    }
}
