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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_deck_out_of_the_room_still_reports_its_end(bool cue)
    {
        string shortTone = TestWav.WriteTone(Path.Combine(_dir, "short-b.wav"), TimeSpan.FromSeconds(1));
        _mix.Crossfader = 0;   // the room hears Deck 1; Deck 2 is silent there
        B.Load(shortTone);
        B.Cue = cue;
        B.Play();
        int finished = 0;
        B.Finished += (_, _) => finished++;

        Render(1.5);
        _mix.Update();

        Assert.Equal(1, finished);
        Assert.False(B.IsPlaying);
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

    [Fact]
    public void A_trimmed_track_finishes_where_the_music_ends_and_replays_from_where_it_starts()
    {
        A.Load(_tone);   // 10 s
        A.SetTrim(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(4));
        A.Seek(A.Start);
        int finished = 0;
        A.Finished += (_, _) => finished++;
        A.Play();

        Render(2.5);
        _mix.Update();
        Assert.Equal(0, finished);
        Assert.InRange(A.Remaining.TotalSeconds, 0, 0.6);   // counts down to the end of the music, not the file

        Render(1);
        _mix.Update();
        Assert.Equal(1, finished);
        Assert.False(A.IsPlaying);

        A.Play();
        Assert.InRange(A.Position.TotalSeconds, 1, 1.3);
    }

    [Fact]
    public void Mono_puts_a_one_sided_recording_in_both_speakers()
    {
        // Drums hard left, as on plenty of early stereo jazz.
        string left = TestWav.WriteTone(Path.Combine(_dir, "left.wav"), TimeSpan.FromSeconds(10), right: 0);
        A.Load(left);
        A.Play();
        Render(0.1);

        _mix.Main.TakeLevels();
        Render();
        var stereo = _mix.Main.TakeLevels();
        Assert.Equal(Full, stereo.Left, 0.02f);
        Assert.InRange(stereo.Right, 0, Silent);

        _mix.Main.Mono = true;
        Render();
        var mono = _mix.Main.TakeLevels();
        Assert.Equal(Full / 2, mono.Left, 0.02f);
        Assert.Equal(mono.Left, mono.Right, 0.001f);

        // Each output has its own switch (the app turns both on together).
        A.Cue = true;
        Render(0.1);
        _mix.Cue.TakeLevels();
        Render();
        Assert.InRange(_mix.Cue.TakeLevels().Right, 0, Silent);
    }

    [Fact]
    public void Each_deck_has_a_meter_the_crossfader_does_not_change()
    {
        A.Load(_tone);
        B.Load(_tone);
        A.Play();
        B.Play();
        _mix.Crossfader = 0;   // the room hears only Deck 1
        Render(0.1);
        A.TakePeak();
        B.TakePeak();

        var (main, _) = Render();
        Assert.Equal(Full, main, 0.02f);
        Assert.Equal(Full, A.TakePeak(), 0.02f);
        Assert.Equal(Full, B.TakePeak(), 0.02f);   // out of the room, but its level shows

        B.GainDb = -6;   // gain (loudness matching and the DJ's) counts
        Render(0.1);
        B.TakePeak();
        Render();
        Assert.Equal(Full / 2, B.TakePeak(), 0.02f);

        B.Pause();
        Render(0.1);
        B.TakePeak();
        Render();
        Assert.Equal(0, B.TakePeak());
        Assert.Equal(Full, A.TakePeak(), 0.02f);
    }

    [Fact]
    public void Volume_turns_an_output_down()
    {
        A.Load(_tone);
        A.Cue = true;
        A.Play();
        _mix.Main.Volume = 0.5f;
        _mix.Cue.Volume = 0.2f;
        Render(0.1);

        var (main, cue) = Render();

        Assert.Equal(Full * 0.5f, main, 0.02f);
        Assert.Equal(Full * 0.2f, cue, 0.02f);
    }

    [Theory]
    [InlineData(60, 0)]      // bass: the low shelf
    [InlineData(1000, 1)]    // the middle: the bell
    [InlineData(10000, 2)]   // hiss territory: the high shelf
    public void Each_eq_band_cuts_its_own_part_of_the_sound(double hz, int band)
    {
        string tone = TestWav.WriteTone(Path.Combine(_dir, $"{hz}.wav"), TimeSpan.FromSeconds(10), frequency: hz);
        A.Load(tone);
        A.Cue = true;
        A.Play();
        Render(0.1);
        var cut = new double[3];
        cut[band] = -24;

        A.SetEq(cut[0], cut[1], cut[2]);
        Render(0.3);   // what was already mixed plays out
        var (main, cue) = Render();

        Assert.InRange(main, 0, Full * 0.15f);   // about -20 dB or more
        Assert.InRange(cue, 0, Full * 0.15f);    // the headphones hear the recording the same way

        // The other bands leave it alone.
        var others = new double[3];
        for (int b = 0; b < 3; b++)
            others[b] = b == band ? 0 : -24;
        A.SetEq(others[0], others[1], others[2]);
        Render(0.3);
        Assert.InRange(Render().Main, Full * 0.6f, Full * 1.1f);
    }

    [Fact]
    public void Gain_turns_a_deck_up_or_down_in_the_room_and_the_headphones()
    {
        string quiet = TestWav.WriteTone(Path.Combine(_dir, "quiet.wav"), TimeSpan.FromSeconds(10), amplitude: 0.25);
        A.Load(quiet);
        A.Cue = true;
        A.Play();
        Render(0.1);

        A.GainDb = 6;   // a quiet track brought up: above unity
        Render(0.3);
        var (main, cue) = Render();
        Assert.Equal(0.5f, main, 0.03f);
        Assert.Equal(0.5f, cue, 0.03f);

        A.GainDb = -6;
        Render(0.3);
        Assert.Equal(0.125f, Render().Main, 0.02f);
    }

    [Fact]
    public void An_armed_jump_skips_ahead_without_a_gap()
    {
        A.Load(_tone);   // 10 s
        A.SetJumps([(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(7), true)]);
        Assert.Equal(5, A.Remaining.TotalSeconds, 1);   // 10 s less the 5 skipped
        A.Play();

        // Through the jump in 50 ms steps: never a quiet moment.
        float quietest = Full;
        for (int i = 0; i < 50; i++)
        {
            quietest = Math.Min(quietest, Render(0.05).Main);
            _mix.Update();
        }

        Assert.InRange(A.Position.TotalSeconds, 7.3, 7.8);   // 2.5 s played: 2 before the jump, 0.5 after
        Assert.InRange(quietest, Full * 0.9f, Full * 1.1f);
        Assert.False(A.Jumps[0].Armed);   // it happens once
    }

    [Fact]
    public void The_phones_mix_lets_the_headphones_hear_the_room_too()
    {
        _mix.Crossfader = 0;   // the room hears Deck 1
        A.Load(_tone);
        B.Load(_tone);
        B.Cue = true;          // Deck 2 cued, not in the room
        A.Play();

        // All the way to cue (the default): the headphones hear only what's cued.
        Assert.Equal(0, _mix.PhonesMix);
        Assert.InRange(Render().Cue, 0, Silent);

        // All the way to the room: Deck 1, as the room hears it; Deck 2 (cued) not at all.
        _mix.PhonesMix = 1;
        Assert.Equal(Full, Render().Cue, 0.02f);
        A.Pause();
        B.Play();
        Assert.InRange(Render().Cue, 0, Silent);
        _mix.Crossfader = 1;   // the room hears Deck 2 now: so do the headphones
        Assert.Equal(Full, Render(0.5).Cue, 0.02f);

        // In the middle, both at full; a deck both cued and in the room isn't doubled.
        _mix.PhonesMix = 0.5;
        Assert.Equal(Full, Render(0.5).Cue, 0.02f);

        // The preview is cued: the room side leaves it out.
        B.Pause();
        _mix.Preview.Play(_tone);
        Assert.Equal(Full, Render().Cue, 0.02f);
        _mix.PhonesMix = 1;
        Render(0.1);   // what was mixed before the change
        Assert.InRange(Render().Cue, 0, Silent);
        _mix.PhonesMix = 0;
        Assert.Equal(Full, Render().Cue, 0.02f);
    }

    [Fact]
    public void Going_somewhere_else_while_playing_leaves_no_gap()
    {
        // A cue key on the deck the room hears: the music carries on from there, without a quiet moment.
        A.Load(_tone);
        A.Play();
        float quietest = Full;
        for (int i = 0; i < 20; i++)
        {
            quietest = Math.Min(quietest, Render(0.05).Main);
            if (i == 10)
                A.Seek(TimeSpan.FromSeconds(8));
        }

        Assert.InRange(A.Position.TotalSeconds, 8.3, 8.6);
        Assert.InRange(quietest, Full * 0.9f, Full * 1.1f);
    }

    [Fact]
    public void A_backward_jump_plays_a_part_again_once_and_can_be_rearmed()
    {
        A.Load(_tone);
        A.SetJumps([(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(1), true)]);
        int jumped = 0;
        A.Jumped += (_, _) => jumped++;
        A.Play();

        void Play(double seconds)
        {
            for (double t = 0; t < seconds - 0.001; t += 0.1)
            {
                Render(0.1);
                _mix.Update();
            }
        }

        Play(4);   // 0 to 3, back to 1, on to 2
        Assert.InRange(A.Position.TotalSeconds, 1.8, 2.3);
        Play(2);   // through 3 again: no second jump
        Assert.InRange(A.Position.TotalSeconds, 3.8, 4.3);
        Assert.Equal(1, jumped);

        A.Seek(TimeSpan.FromSeconds(2));
        A.ArmJumps(true);   // the DJ taps JUMP: once more
        Play(1.5);
        Assert.InRange(A.Position.TotalSeconds, 1.3, 1.8);
        Assert.Equal(2, jumped);
    }

    [Fact]
    public void Eq_stays_through_a_replay_from_the_end()
    {
        string tone = TestWav.WriteTone(Path.Combine(_dir, "short-hiss.wav"), TimeSpan.FromSeconds(1), frequency: 10000);
        A.Load(tone);
        A.SetEq(0, 0, -24);
        A.Play();
        for (int i = 0; i < 20 && A.IsPlaying; i++)
        {
            Render(0.1);
            _mix.Update();
        }
        Assert.False(A.IsPlaying);

        A.Play();   // reloads the file from the start
        Render(0.3);
        Assert.InRange(Render().Main, 0, Full * 0.15f);
        Assert.Equal((0.0, 0.0, -24.0), A.Eq);
    }

    [Fact]
    public void The_limiter_stops_a_loud_blend_from_clipping()
    {
        string loud = TestWav.WriteTone(Path.Combine(_dir, "loud.wav"), TimeSpan.FromSeconds(10), amplitude: 0.95);
        A.Load(loud);
        B.Load(loud);
        A.Play();
        B.Play();
        _mix.Crossfader = 0.5;   // both decks at equal power: 0.95 x 1.41 would clip
        Render(0.1);
        _mix.Main.TakeLevels();

        var (main, _) = Render();
        var levels = _mix.Main.TakeLevels();

        Assert.InRange(main, 0.8f, OutputBus.Ceiling + 0.001f);
        Assert.True(levels.Limited);

        // A normal level isn't touched, and the light goes out.
        B.Pause();
        _mix.Crossfader = 0;
        _mix.Main.Volume = 0.5f;
        Render(1);
        _mix.Main.TakeLevels();
        Render();
        levels = _mix.Main.TakeLevels();
        Assert.Equal(0.475f, levels.Left, 0.02f);
        Assert.False(levels.Limited);
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
