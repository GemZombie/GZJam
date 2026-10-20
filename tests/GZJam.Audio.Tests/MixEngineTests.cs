using GZJam.Testing;

namespace GZJam.Audio.Tests;

/// <summary>
/// Drives the real BASS mixer offline (no audio devices) and measures what reaches the speakers
/// (main) and the headphones (cue). Tones are at amplitude 0.5.
/// </summary>
[Collection("BASS")]   // BASS is process-global; keep these tests sequential
public sealed class MixEngineTests : IDisposable
{
    private const float Full = 0.5f;
    private const float Silent = 0.01f;

    private readonly string _dir = Directory.CreateTempSubdirectory("gzjam-audio-").FullName;
    private readonly AudioEngine _engine = new();
    private readonly ManualTime _time = new();
    private readonly MixEngine _mix;
    private readonly string _tone;

    public MixEngineTests()
    {
        _mix = new MixEngine(_engine, offline: true, time: _time);
        _tone = TestWav.WriteTone(Path.Combine(_dir, "tone.wav"), TimeSpan.FromSeconds(10));
    }

    public void Dispose()
    {
        _mix.Dispose();
        _engine.Dispose();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private Deck A => _mix.Decks[0];
    private Deck B => _mix.Decks[1];

    private (float Main, float Cue) Render(double seconds = 0.3) => _mix.Render(TimeSpan.FromSeconds(seconds));

    [Fact]
    public void The_deck_the_crossfader_is_on_plays_to_the_room()
    {
        _mix.Crossfader = 0;
        A.Load(_tone);
        A.Play();

        var (main, cue) = Render();

        Assert.Equal(Full, main, 0.02f);
        Assert.InRange(cue, 0, Silent);   // headphones only hear decks with cue on
        Assert.Same(A, _mix.LiveDeck);
        Assert.True(A.IsInRoom);
    }

    [Fact]
    public void The_other_deck_is_silent_in_the_room()
    {
        _mix.Crossfader = 0;
        B.Load(_tone);
        B.Play();

        var (main, cue) = Render();

        Assert.InRange(main, 0, Silent);
        Assert.InRange(cue, 0, Silent);
        Assert.False(B.IsInRoom);
    }

    [Fact]
    public void Cue_sends_a_deck_to_the_headphones_whatever_the_crossfader()
    {
        A.Load(_tone);
        A.Cue = true;
        A.Play();

        _mix.Crossfader = 0;
        Render(0.1);
        Assert.Equal((Full, Full), Round(Render()));

        _mix.Crossfader = 1;   // out of the room, still in the headphones
        Render(0.1);           // the mixer ramps volume changes briefly, which avoids clicks
        var (main, cue) = Render();
        Assert.InRange(main, 0, Silent);
        Assert.Equal(Full, cue, 0.02f);

        A.Cue = false;
        Render(0.1);
        Assert.InRange(Render().Cue, 0, Silent);
        Assert.True(A.IsPlaying);
    }

    [Fact]
    public void Moving_the_crossfader_fades_the_room_without_stopping()
    {
        A.Load(_tone);
        A.Play();
        Render();

        _mix.Crossfader = 1;
        Render(0.1);
        var (main, _) = Render();

        Assert.InRange(main, 0, Silent);
        Assert.True(A.IsPlaying);
        Assert.False(A.IsInRoom);
        Assert.Same(B, _mix.LiveDeck);
    }

    [Fact]
    public void Centre_crossfader_blends_at_equal_power()
    {
        _mix.Crossfader = 0.5;
        A.Load(_tone);
        A.Play();
        B.Load(_tone);

        var (main, cue) = Render();

        Assert.Equal(Full * 0.707f, main, 0.02f);
        Assert.InRange(cue, 0, Silent);
        Assert.True(A.IsInRoom);
        Assert.True(B.IsInRoom);
    }

    private static (float, float) Round((float Main, float Cue) levels) =>
        (MathF.Round(levels.Main * 50) / 50, MathF.Round(levels.Cue * 50) / 50);

    [Fact]
    public void Loaded_but_paused_decks_are_silent()
    {
        A.Load(_tone);
        B.Load(_tone);

        var (main, cue) = Render();

        Assert.InRange(main, 0, Silent);
        Assert.InRange(cue, 0, Silent);
        Assert.Equal(TimeSpan.Zero, A.Position);
    }

    [Fact]
    public void Pause_holds_position_and_play_resumes()
    {
        A.Load(_tone);
        A.Play();
        Render(1);
        A.Pause();
        TimeSpan paused = A.Position;
        Render(1);

        Assert.Equal(paused, A.Position);
        Assert.InRange(paused.TotalSeconds, 0.9, 1.1);

        A.Play();
        Assert.Equal(Full, Render().Main, 0.02f);
    }

    [Fact]
    public void Preview_reaches_only_the_headphones()
    {
        _mix.Preview.Play(_tone);

        var (main, cue) = Render();

        Assert.InRange(main, 0, Silent);
        Assert.Equal(Full, cue, 0.02f);
    }

    [Fact]
    public void Tempo_speeds_up_playback_without_changing_length()
    {
        A.Load(_tone);
        A.TempoPercent = 10;
        A.Play();

        Render(2);

        Assert.InRange(A.Position.TotalSeconds, 2.1, 2.3);   // 2 s of output used 2.2 s of track
        Assert.Equal(10, A.Length.TotalSeconds, 0.01);
        Assert.InRange(A.Remaining.TotalSeconds, 7.0, 7.2);   // (10 - 2.2) / 1.1
    }

    [Fact]
    public void Seek_jumps_and_keeps_playing()
    {
        A.Load(_tone);
        A.Play();
        Render(0.5);

        A.Seek(TimeSpan.FromSeconds(7));
        Render(0.5);

        Assert.InRange(A.Position.TotalSeconds, 7.4, 7.65);
        Assert.True(A.IsPlaying);
    }

    [Fact]
    public void Fade_out_lowers_the_volume_then_stops_the_deck()
    {
        A.Load(_tone);
        A.Play();
        int finished = 0;
        A.Finished += (_, _) => finished++;

        A.FadeOut(TimeSpan.FromSeconds(4));
        _time.Advance(TimeSpan.FromSeconds(2));
        _mix.Update();
        Assert.Equal(Full * 0.5f, Render(0.1).Main, 0.03f);

        _time.Advance(TimeSpan.FromSeconds(2.1));
        _mix.Update();
        Assert.False(A.IsPlaying);
        Assert.Equal(1, finished);
        Assert.InRange(Render().Main, 0, Silent);

        // The next play is at full volume again.
        A.Play();
        Assert.Equal(Full, Render().Main, 0.02f);
    }

    [Fact]
    public void Playing_to_the_end_raises_finished_and_play_starts_over()
    {
        string shortTone = TestWav.WriteTone(Path.Combine(_dir, "short.wav"), TimeSpan.FromSeconds(1));
        A.Load(shortTone);
        A.Play();
        int finished = 0;
        A.Finished += (_, _) => finished++;

        Render(1.5);
        _mix.Update();

        Assert.Equal(1, finished);
        Assert.False(A.IsPlaying);

        A.Play();
        Render(0.3);
        Assert.True(A.IsPlaying);
        Assert.InRange(A.Position.TotalSeconds, 0.2, 0.45);
    }

    [Fact]
    public void Loading_replaces_the_track_and_starts_paused()
    {
        string other = TestWav.WriteTone(Path.Combine(_dir, "other.wav"), TimeSpan.FromSeconds(3));
        A.Load(_tone);
        A.Play();
        Render();

        A.Load(other);

        Assert.False(A.IsPlaying);
        Assert.Equal(other, A.TrackPath);
        Assert.Equal(3, A.Length.TotalSeconds, 0.01);
        Assert.InRange(Render().Main, 0, Silent);
    }

    [Theory]
    [InlineData(0.0, 0.0)]     // fader away from the deck: silent in the room
    [InlineData(1.0, 1.0)]     // fader on the deck: full
    [InlineData(0.5, 0.707)]   // equal power in the middle
    public void Crossfade_curve(double audibility, double main) =>
        Assert.Equal(main, Crossfade.MainGain(audibility), 3);
}

[CollectionDefinition("BASS", DisableParallelization = true)]
public sealed class BassCollection;
