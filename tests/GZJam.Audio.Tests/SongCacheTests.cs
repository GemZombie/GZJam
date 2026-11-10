using GZJam.Testing;

namespace GZJam.Audio.Tests;

/// <summary>
/// Songs played from memory (#346): a deck or the headphones never wait on the disk once a song
/// is loaded, and songs about to be loaded are read ahead.
/// </summary>
[Collection("BASS")]
public sealed class SongCacheTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("gzjam-songcache-").FullName;
    private readonly List<string> _reads = [];

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string Song(string name, double seconds = 5) =>
        TestWav.WriteTone(Path.Combine(_dir, name + ".wav"), TimeSpan.FromSeconds(seconds));

    private SongCache Counting() => new(path =>
    {
        lock (_reads)
            _reads.Add(Path.GetFileNameWithoutExtension(path));
        return SongMemory.Read(path, SongCache.MaxSongBytes);
    });

    [Fact]
    public void A_song_read_ahead_isnt_read_again_when_its_loaded()
    {
        string song = Song("next");
        using var cache = Counting();
        cache.ReadAhead([song]);
        Assert.True(SpinWait.SpinUntil(() => cache.IsKept(song), 5000));

        SongMemory memory = Assert.IsType<SongMemory>(cache.Take(song));
        memory.Release();

        Assert.Equal(["next"], _reads);
        Assert.Equal(new FileInfo(song).Length, memory.Length);
    }

    [Fact]
    public void A_song_changed_on_disk_since_it_was_read_is_read_again()
    {
        string song = Song("edited");
        using var cache = Counting();
        cache.Take(song)!.Release();

        File.SetLastWriteTimeUtc(song, DateTime.UtcNow.AddMinutes(1));   // its tags were edited, say
        cache.Take(song)!.Release();

        Assert.Equal(["edited", "edited"], _reads);
    }

    [Fact]
    public void Only_a_few_are_kept_but_one_still_playing_stays_until_its_let_go()
    {
        using var cache = Counting();
        string playing = Song("playing");
        SongMemory held = cache.Take(playing)!;   // on a deck
        for (int i = 0; i < SongCache.Kept + 2; i++)
            cache.Take(Song("other " + i))!.Release();

        // Pushed out of the cache, but the deck still has it: still there, still whole.
        Assert.True(held.Hold());
        held.Release();
        var start = new byte[4];
        System.Runtime.InteropServices.Marshal.Copy(held.Data, start, 0, 4);
        Assert.Equal("RIFF"u8.ToArray(), start);
        held.Release();
        Assert.False(held.Hold());   // the last holder let go: freed

        _reads.Clear();
        cache.Take(playing)!.Release();
        Assert.Equal(["playing"], _reads);   // read again: it had gone from the cache
    }

    [Fact]
    public void A_deck_plays_on_after_its_song_file_is_gone()
    {
        using var engine = new AudioEngine();
        using var mix = new MixEngine(engine, offline: true);
        string song = Song("on the deck", seconds: 10);
        Deck deck = mix.Decks[0];
        deck.Load(song);
        deck.Play();
        mix.Main.Render(TimeSpan.FromSeconds(0.2));

        // The drive went to sleep, was unplugged, or the file was moved: the deck doesn't notice.
        File.Delete(song);
        Assert.False(File.Exists(song));

        Assert.Equal(0.5f, mix.Main.Render(TimeSpan.FromSeconds(3)), 0.02f);
        Assert.InRange(deck.Position.TotalSeconds, 3, 3.5);
    }

    [Fact]
    public void A_file_too_big_to_hold_plays_from_disk_as_before()
    {
        string song = Song("long mix");
        Assert.Null(SongMemory.Read(song, maxBytes: 1000));   // the deck opens it from disk then

        using var engine = new AudioEngine();
        using var mix = new MixEngine(engine, offline: true);
        mix.Decks[0].Load(song);
        mix.Decks[0].Play();
        Assert.Equal(0.5f, mix.Main.Render(TimeSpan.FromSeconds(0.5)), 0.02f);
    }
}
