using Avalonia.Headless.XUnit;
using GZJam.Testing;

namespace GZJam.App.Tests;

/// <summary>
/// A new DJ's first night is also their first-time analysis (#369): while music plays, the library
/// is analyzed on one thread, not up to three, so the laptop isn't busy and hot under the music.
/// </summary>
public sealed class GentleAnalysisTests : IDisposable
{
    private readonly string _music = Directory.CreateTempSubdirectory("gzjam-gentle-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_music, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [AvaloniaFact]
    public void While_music_plays_the_library_is_analyzed_on_one_thread()
    {
        for (int i = 0; i < 8; i++)
            TestWav.WriteTone(Path.Combine(_music, $"Song {i}.wav"), TimeSpan.FromSeconds(20), frequency: 220 + 40 * i);
        using var app = new AppHarness(musicFolder: _music, start: false);
        var analysis = app.ViewModel.Analysis!;
        var deck = app.ViewModel.Deck1!;
        AppHarness.Await(app.ViewModel.LoadToDeckAsync(deck, app.ToneTrack("Playing")));
        deck.TogglePlayCommand.Execute(null);
        app.Play(0.1);
        Assert.True(analysis.MusicPlaying);   // set by the tick
        app.Start();

        AppHarness.WaitFor(() => analysis.Status is null && app.Database.GetPendingAnalysis(GZJam.Audio.TrackAnalyzer.Version).Count == 0,
            TimeSpan.FromSeconds(60));
        Assert.Equal(1, analysis.MostAtOnce);
    }

    /// <summary>With nothing playing, it goes as fast as before: more than one thread, where the computer has them.</summary>
    [AvaloniaFact]
    public void With_nothing_playing_the_library_is_analyzed_on_several_threads()
    {
        if (Environment.ProcessorCount < 4)
            return;   // one thread anyway
        for (int i = 0; i < 8; i++)
            TestWav.WriteTone(Path.Combine(_music, $"Song {i}.wav"), TimeSpan.FromSeconds(20), frequency: 220 + 40 * i);
        using var app = new AppHarness(musicFolder: _music);
        var analysis = app.ViewModel.Analysis!;
        AppHarness.WaitFor(() => analysis.Status is null && app.Database.GetPendingAnalysis(GZJam.Audio.TrackAnalyzer.Version).Count == 0,
            TimeSpan.FromSeconds(60));
        Assert.False(analysis.MusicPlaying);
        Assert.True(analysis.MostAtOnce > 1);
    }
}
