using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using GZJam.App.ViewModels;
using GZJam.App.Views;
using GZJam.Audio;
using GZJam.Library;
using GZJam.Testing;

[assembly: AvaloniaTestApplication(typeof(GZJam.App.Tests.TestAppBuilder))]

namespace GZJam.App.Tests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false, OverlayPopups = true });
}

/// <summary>
/// The real main window and view models over a throwaway database. Music comes from a generated
/// folder, or from an existing folder (read-only) for screenshots of a real library.
/// </summary>
public sealed class AppHarness : IDisposable
{
    public static string ScreenshotDirectory { get; } = Path.Combine(Path.GetTempPath(), "gzjam-screenshots");

    private readonly string _work;

    /// <summary>The harness a test is using (UI tests run one at a time, on Avalonia's thread).</summary>
    private static AppHarness? _current;

    public AppHarness(string? musicFolder = null, AppSettings? settings = null, bool start = true)
    {
        _work = Directory.CreateTempSubdirectory("gzjam-app-test-").FullName;
        MusicPath = musicFolder ?? Path.Combine(_work, "Music");
        Database = new LibraryDatabase(Path.Combine(_work, "library.db"));
        // Loudness matching off unless a test asks for it: level checks expect tones at their own level.
        Settings = settings ?? new AppSettings { MatchLoudness = false };
        if (musicFolder is null)
            GenerateMusic(MusicPath);
        Database.AddRoot(MusicPath);

        // Real BASS mixing, but offline: nothing plays aloud, and tests pull audio with Render().
        Audio = new AudioEngine();
        Mix = new MixEngine(Audio, offline: true);
        ViewModel = new MainWindowViewModel(Database, Settings, Audio, Mix);
        Window = new MainWindow { DataContext = ViewModel, Width = 1440, Height = 900 };
        Window.Show();
        _current = this;
        if (start)
            Start();
    }

    public string MusicPath { get; }
    public LibraryDatabase Database { get; }
    public AppSettings Settings { get; }
    public MainWindowViewModel ViewModel { get; }
    public AudioEngine Audio { get; }
    public MixEngine Mix { get; }
    public string WorkDirectory => _work;

    /// <summary>A track backed by a generated tone (real audio, unlike the silent test MP3s).</summary>
    public Track ToneTrack(string title, double seconds = 20, double silenceBefore = 0, double silenceAfter = 0, double amplitude = 0.5)
    {
        string path = TestWav.WriteTone(Path.Combine(_work, "tones", title + ".wav"), TimeSpan.FromSeconds(seconds), amplitude: amplitude,
            silenceBefore: TimeSpan.FromSeconds(silenceBefore), silenceAfter: TimeSpan.FromSeconds(silenceAfter));
        return new Track { Path = path, Folder = "", FileName = title + ".wav", Title = title, Artist = "Test Band", TagBpm = 150 };
    }

    /// <summary>A drum pattern at <paramref name="bpm"/> with no BPM tag, for tempo detection.</summary>
    public Track BeatTrack(string title, double bpm, double seconds = 30)
    {
        string path = TestWav.WriteSwingBeat(Path.Combine(_work, "beats", title + ".wav"), bpm, TimeSpan.FromSeconds(seconds));
        return new Track { Path = path, Folder = "", FileName = title + ".wav", Title = title, Artist = "Test Drummer" };
    }

    /// <summary>Plays <paramref name="seconds"/> of audio through the offline mix, then runs a UI tick.</summary>
    public (float Main, float Cue) Play(double seconds)
    {
        var levels = Mix.Render(TimeSpan.FromSeconds(seconds));
        ViewModel.Tick();
        Dispatcher.UIThread.RunJobs();
        return levels;
    }
    public MainWindow Window { get; }
    public LibraryViewModel Library => ViewModel.Library;

    public void Start()
    {
        ViewModel.Start();
        WaitFor(() => Library.HasLoaded && Library.ScanStatus is null && Library.Roots.Count > 0 && Library.Results.Count > 0,
            TimeSpan.FromSeconds(60));
        Settle();
    }

    /// <summary>Runs the UI dispatcher until <paramref name="condition"/> holds.</summary>
    public static void WaitFor(Func<bool> condition, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("Condition not met in time.");
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(15);
        }
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// Lets debounce timers fire and background searches land: runs the UI for
    /// <paramref name="milliseconds"/>, then for as long as a library search is still on its way
    /// (slow CI machines can take longer than the fixed wait).
    /// </summary>
    public static void Settle(int milliseconds = 400)
    {
        var until = DateTime.UtcNow.AddMilliseconds(milliseconds);
        var latest = until.AddSeconds(10);
        while (DateTime.UtcNow < until || (_current?.Library.IsSearching == true && DateTime.UtcNow < latest))
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(15);
        }
    }

    public string Screenshot(string name)
    {
        Settle(200);
        Directory.CreateDirectory(ScreenshotDirectory);
        string path = Path.Combine(ScreenshotDirectory, name + ".png");
        using var frame = Window.CaptureRenderedFrame();
#pragma warning disable CS0618 // the replacement overload needs encoder options we don't care about
        frame?.Save(path);
#pragma warning restore CS0618
        return path;
    }

    /// <summary>A small library in the genre / style / artist / album layout.</summary>
    private static void GenerateMusic(string root)
    {
        (string Folder, string Artist, string Album, string[] Titles)[] albums =
        [
            ("Swing/Lindy Hop/Count Basie/The Complete Decca Recordings", "Count Basie", "The Complete Decca Recordings",
                ["One O'Clock Jump", "Jumpin' at the Woodside", "Swingin' the Blues", "Every Tub", "Topsy"]),
            ("Swing/Lindy Hop/Count Basie/April in Paris", "Count Basie Orchestra", "April in Paris",
                ["April in Paris", "Corner Pocket", "Shiny Stockings"]),
            ("Swing/Lindy Hop/Chick Webb/Stompin' at the Savoy", "Chick Webb", "Stompin' at the Savoy",
                ["Stompin' at the Savoy", "Liza", "Clap Hands! Here Comes Charley!"]),
            ("Swing/Balboa/Benny Goodman/Live at Carnegie Hall", "Benny Goodman", "Live at Carnegie Hall",
                ["Sing, Sing, Sing", "Don't Be That Way"]),
            ("Blues/Slow Blues/Jay McShann/Confessin' the Blues", "Jay McShann", "Confessin' the Blues",
                ["Confessin' the Blues", "Hootie Blues"]),
        ];
        var random = new Random(7);
        foreach (var (folder, artist, album, titles) in albums)
        {
            for (int i = 0; i < titles.Length; i++)
            {
                string file = Path.Combine(root, folder.Replace('/', Path.DirectorySeparatorChar), $"{i + 1:00} - {Sanitize(titles[i])}.mp3");
                TestMp3.Write(file, new TestMp3.Tags(titles[i], artist, album, folder.Split('/')[0], 1937 + random.Next(20), i + 1,
                    Bpm: (120 + random.Next(120)).ToString(), Duration: TimeSpan.FromSeconds(150 + random.Next(120))));
            }
        }
    }

    private static string Sanitize(string name) => string.Concat(name.Split(Path.GetInvalidFileNameChars()));

    public void Dispose()
    {
        if (_current == this)
            _current = null;
        Window.Close();
        ViewModel.Dispose();   // also disposes the mix
        Audio.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_work, recursive: true); }
        catch (IOException) { /* best effort */ }
    }
}
