using GZJam.Testing;

namespace GZJam.Audio.Tests;

/// <summary>
/// The headphones or booth unplugged during a night (#72): they go quiet, the room plays on, and
/// they come back when plugged in. Offline, with stand-ins for the device stopping and returning.
/// </summary>
[Collection("BASS")]
public sealed class UnpluggedOutputTests : IDisposable
{
    private const float Full = 0.5f;
    private const float Silent = 0.01f;

    private readonly string _dir = Directory.CreateTempSubdirectory("gzjam-audio-").FullName;
    private readonly AudioEngine _engine = new();
    private readonly MixEngine _mix;
    private readonly string _tone;

    /// <summary>The outputs a test has "unplugged", and whether they're plugged in again.</summary>
    private readonly HashSet<TableOutput> _stopped = [];
    private bool _pluggedIn;
    private int _reconnectTries;

    public UnpluggedOutputTests()
    {
        _mix = new MixEngine(_engine, offline: true);
        _mix.DeviceStopped = bus => (bus == _mix.Cue && _stopped.Contains(TableOutput.Headphones))
                                 || (bus == _mix.Booth && _stopped.Contains(TableOutput.Booth));
        _mix.Reconnect = output =>
        {
            _reconnectTries++;
            if (!_pluggedIn)
                return false;
            _stopped.Remove(output);
            return true;
        };
        _tone = TestWav.WriteTone(Path.Combine(_dir, "tone.wav"), TimeSpan.FromSeconds(10));
    }

    public void Dispose()
    {
        _mix.Dispose();
        _engine.Dispose();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void Unplugged_headphones_are_said_once_and_tried_again_until_they_come_back()
    {
        var unplugged = new List<TableOutput>();
        var back = new List<TableOutput>();
        _mix.OutputUnplugged += (_, o) => unplugged.Add(o);
        _mix.OutputBack += (_, o) => back.Add(o);

        _stopped.Add(TableOutput.Headphones);
        _mix.Update();
        _mix.Update();
        Assert.Equal([TableOutput.Headphones], unplugged);
        Assert.True(_mix.IsUnplugged(TableOutput.Headphones));
        Assert.False(_mix.IsUnplugged(TableOutput.Booth));
        Assert.Equal(0, _reconnectTries);   // not straight away: every couple of seconds

        Thread.Sleep(2100);
        _mix.Update();
        Assert.Equal(1, _reconnectTries);   // still unplugged
        Assert.Empty(back);

        _pluggedIn = true;
        Thread.Sleep(2100);
        _mix.Update();
        Assert.Equal([TableOutput.Headphones], back);
        Assert.False(_mix.IsUnplugged(TableOutput.Headphones));
        _mix.Update();
        Assert.Single(unplugged);
    }

    [Fact]
    public void An_unplugged_booth_is_watched_the_same_way()
    {
        var unplugged = new List<TableOutput>();
        _mix.OutputUnplugged += (_, o) => unplugged.Add(o);
        _stopped.Add(TableOutput.Booth);
        _mix.Update();
        Assert.Equal([TableOutput.Booth], unplugged);
        Assert.True(_mix.IsUnplugged(TableOutput.Booth));
        Assert.False(_mix.IsUnplugged(TableOutput.Headphones));
    }

    [Fact]
    public void Opening_the_headphones_again_keeps_the_room_playing_and_brings_back_whats_cued()
    {
        var deck = _mix.Decks[0];
        _mix.Crossfader = 0;
        deck.Load(_tone);
        deck.Cue = true;
        deck.Play();
        _mix.Cue.Volume = 0.5f;
        _mix.Render(TimeSpan.FromSeconds(0.2));
        OutputBus main = _mix.Main;

        _mix.Reopen(TableOutput.Headphones, null);

        Assert.Same(main, _mix.Main);   // the speakers weren't touched
        Assert.Equal(0.5f, _mix.Cue.Volume);
        _mix.Render(TimeSpan.FromSeconds(0.1));
        var (room, phones) = _mix.Render(TimeSpan.FromSeconds(0.3));
        Assert.Equal(Full, room, 0.02f);
        Assert.Equal(Full * 0.5f, phones, 0.02f);
        Assert.True(deck.IsPlaying);
    }

    [Fact]
    public void Opening_the_headphones_again_brings_back_the_preview()
    {
        _mix.Preview.Play(_tone);
        _mix.Reopen(TableOutput.Headphones, null);
        _mix.Render(TimeSpan.FromSeconds(0.1));
        var (room, phones) = _mix.Render(TimeSpan.FromSeconds(0.3));
        Assert.InRange(room, 0, Silent);   // never the room
        Assert.Equal(Full, phones, 0.02f);
    }

    [Fact]
    public void A_paused_deck_stays_paused_on_the_reopened_headphones()
    {
        var deck = _mix.Decks[0];
        deck.Load(_tone);
        deck.Cue = true;
        _mix.Reopen(TableOutput.Headphones, null);
        var (_, phones) = _mix.Render(TimeSpan.FromSeconds(0.3));
        Assert.InRange(phones, 0, Silent);
        Assert.False(deck.IsPlaying);
    }

    [Fact]
    public void Opening_the_booth_again_keeps_it_hearing_the_room()
    {
        var deck = _mix.Decks[0];
        _mix.Crossfader = 0;
        deck.Load(_tone);
        deck.Play();
        _mix.Reopen(TableOutput.Booth, null);
        _mix.RenderAll(TimeSpan.FromSeconds(0.1));
        var (room, phones, booth) = _mix.RenderAll(TimeSpan.FromSeconds(0.3));
        Assert.Equal(Full, room, 0.02f);
        Assert.Equal(Full, booth, 0.02f);
        Assert.InRange(phones, 0, Silent);
    }
}
