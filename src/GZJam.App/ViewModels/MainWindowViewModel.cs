using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using GZJam.Audio;
using GZJam.Library;

namespace GZJam.App.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly AppSettings _settings;
    private readonly LibraryDatabase _database;
    private readonly DispatcherTimer _clock;
    private readonly DispatcherTimer _playbackTimer;

    /// <param name="audio">BASS, or null if it failed to load (the library still works).</param>
    /// <param name="mix">The mixer; tests pass an offline one.</param>
    /// <param name="audioError">Why audio isn't available, shown in the window.</param>
    public MainWindowViewModel(LibraryDatabase database, AppSettings settings,
        AudioEngine? audio = null, MixEngine? mix = null, string? audioError = null)
    {
        _settings = settings;
        _database = database;
        Audio = audio;
        Mix = mix;
        AudioError = audioError;
        Library = new LibraryViewModel(database);
        Queue = new QueueViewModel();
        History = new HistoryViewModel(database);
        Library.TonightSince = () => History.CurrentSessionStartUtc;
        if (audio is not null)
        {
            Analysis = new TrackAnalysisService(database, audio);
            Analysis.Analyzed += OnAnalyzed;
            Analysis.PropertyChanged += (_, _) => BackgroundChanged();
            Library.Scanned += Analysis.QueueLibrary;
            Queue.Items.CollectionChanged += (_, e) =>
            {
                if (e.NewItems is not null)
                    Analysis.Prioritize(e.NewItems.Cast<QueueItem>().Select(i => i.Track));
            };
        }
        Library.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(LibraryViewModel.ScanStatus) or nameof(LibraryViewModel.ScanPercent))
                BackgroundChanged();
        };

        if (mix is not null)
        {
            Decks = mix.Decks.Select(d => new DeckViewModel(this, d, settings)).ToList();
            _deckCount = settings.DeckCount == 4 && Decks.Count >= 4 ? 4 : Math.Min(2, Decks.Count);
            Mixer = new MixerViewModel(this, mix, settings);
            Preview = new PreviewViewModel(this, mix.Preview);
            AutoPlay = new AutoPlayViewModel(this, mix, Queue, settings);
            foreach (DeckViewModel deck in Decks)
            {
                deck.PlayedToRoom += (d, firstOnAir) =>
                {
                    History.Record(d.Track!, firstOnAir, d.Label, d.TempoPercent, d.Deck.Length);
                    MarkPlayed(d.Track!.Path, firstOnAir);
                };
                deck.Deck.Finished += (_, _) =>
                {
                    if (deck.Deck.IsInRoom)
                    {
                        Log.Info($"Deck {deck.Label}: finished \"{deck.Title}\"");
                        deck.MarkFinished();
                        AutoPlay.OnDeckFinished(deck);
                    }
                    else
                    {
                        // Ran out in the headphones only: the room hasn't heard it, so it's still
                        // the next song. Rewind it, ready to play.
                        Log.Info($"Deck {deck.Label}: \"{deck.Title}\" ended in the headphones; rewound");
                        deck.Rewind();
                    }
                };
                deck.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(DeckViewModel.Track))
                        AutoPlay.Refresh();
                };
            }
        }
        Settings = new SettingsViewModel(this, settings);

        _clock = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => UpdateClock());
        _playbackTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Render, (_, _) => Tick());
        UpdateClock();
    }

    public LibraryViewModel Library { get; }
    public QueueViewModel Queue { get; }
    public HistoryViewModel History { get; }

    /// <summary>Waveforms, silence trimming and BPM detection; null without audio.</summary>
    public TrackAnalysisService? Analysis { get; }

    /// <summary>What's happening in the background (scanning, then analyzing), for the top bar.</summary>
    public string? BackgroundStatus => Library.ScanStatus ?? Analysis?.Status;
    public double BackgroundPercent => Library.ScanStatus is not null ? Library.ScanPercent : Analysis?.Percent ?? 0;

    private void BackgroundChanged()
    {
        OnPropertyChanged(nameof(BackgroundStatus));
        OnPropertyChanged(nameof(BackgroundPercent));
    }

    /// <summary>Every copy of a file's track on screen: in the library results, the queue and on the decks.</summary>
    private IEnumerable<Track> TracksFor(string path) =>
        new[] { Library.ResultFor(path) }
            .Concat(Queue.Items.Select(i => i.Track))
            .Concat(Decks.Select(d => d.Track))
            .OfType<Track>()
            .Where(t => t.Path == path)
            .Distinct();

    /// <summary>The room heard a song: wherever it's on screen, it's now "played tonight".</summary>
    private void MarkPlayed(string path, DateTime playedUtc)
    {
        foreach (Track track in TracksFor(path))
        {
            track.LastPlayedUtc = playedUtc;
            track.PlayCount++;
            track.IsPlayedTonight = true;
        }
    }

    /// <summary>A track's analysis is in: show its detected BPM wherever the track is on screen.</summary>
    private void OnAnalyzed(string path, TrackAnalysis analysis)
    {
        foreach (Track track in TracksFor(path))
            track.DetectedBpm = analysis.Bpm;
    }

    /// <summary>
    /// The ranges offered when a tempo was detected wrongly. Each is under double, so there's
    /// only one choice between half and double time; neighbours overlap.
    /// </summary>
    public static IReadOnlyList<(BpmRange Range, string Label)> BpmRanges { get; } =
    [
        (new BpmRange(60, 110), "Slow (60–110)"),
        (new BpmRange(100, 160), "Medium (100–160)"),
        (new BpmRange(140, 220), "Up-tempo (140–220)"),
        (new BpmRange(200, 320), "Fast (200–320)"),
    ];

    /// <summary>
    /// Detects tracks' BPMs again within a range the DJ chose (null: back to automatic), and
    /// remembers the choice. With a range, the detected BPM is shown even if the tag has one.
    /// </summary>
    public async Task DetectBpmAsync(IReadOnlyList<Track> tracks, BpmRange? range)
    {
        if (Analysis is null || tracks.Count == 0)
            return;
        var paths = tracks.Select(t => t.Path).Distinct().ToList();
        foreach (string path in paths)
        {
            _database.SetBpmRange(path, range);
            foreach (Track track in TracksFor(path).Concat(tracks.Where(t => t.Path == path)))
                track.BpmRange = range;
        }
        string what = paths.Count == 1 ? $"\"{tracks[0].DisplayTitle}\"" : $"{paths.Count} tracks";
        Log.Info($"BPM range for {what}: {range?.ToString() ?? "automatic"}");
        if (paths.Count > 1)
            ShowNotice($"Detecting the BPM of {what}…");

        var results = await Task.WhenAll(paths.Select(Analysis.RedoAsync));
        if (paths.Count == 1 && results[0]?.Bpm is { } bpm && tracks[0].BpmRange == range)
            ShowNotice($"{what}: {Format.Bpm(tracks[0].Bpm ?? bpm)} BPM");
        else if (paths.Count > 1)
            ShowNotice($"BPM detected again for {what}.");
    }

    /// <summary>Where the DJ set a file to start and end.</summary>
    internal ManualTrim GetTrim(string path) => _database.GetTrim(path);

    internal void SaveTrim(string path, ManualTrim trim) => _database.SetTrim(path, trim);

    /// <summary>The EQ the DJ set for a file.</summary>
    internal TrackEq GetEq(string path) => _database.GetEq(path);

    internal void SaveEq(string path, TrackEq eq) => _database.SetEq(path, eq);

    // ───────────── Ratings and energy ─────────────

    /// <summary>Rates tracks (saved in the library, never in the files) wherever they are on screen.</summary>
    public void SetRating(IReadOnlyList<Track> tracks, Rating rating)
    {
        var paths = tracks.Select(t => t.Path).Distinct().ToList();
        _database.SetRating(paths, rating);
        foreach (string path in paths)
            foreach (Track track in TracksFor(path).Concat(tracks.Where(t => t.Path == path)))
                track.Rating = rating;
        Log.Info($"Rating for {Describe(tracks, paths)}: {rating}");
    }

    /// <summary>Marks tracks' energy wherever they are on screen.</summary>
    public void SetEnergy(IReadOnlyList<Track> tracks, Energy energy)
    {
        var paths = tracks.Select(t => t.Path).Distinct().ToList();
        _database.SetEnergy(paths, energy);
        foreach (string path in paths)
            foreach (Track track in TracksFor(path).Concat(tracks.Where(t => t.Path == path)))
                track.Energy = energy;
        Log.Info($"Energy for {Describe(tracks, paths)}: {energy}");
    }

    /// <summary>A tap on a track's heart: unrated → ♥ → broken heart → unrated.</summary>
    public void CycleRating(Track track) => SetRating([track], track.Rating switch
    {
        Rating.Unrated => Rating.Loved,
        Rating.Loved => Rating.Rejected,
        _ => Rating.Unrated,
    });

    /// <summary>A tap on a track's energy bars: unset → low → medium → high → unset.</summary>
    public void CycleEnergy(Track track) => SetEnergy([track], track.Energy == Energy.High ? Energy.Unset : track.Energy + 1);

    private static string Describe(IReadOnlyList<Track> tracks, IReadOnlyList<string> paths) =>
        paths.Count == 1 ? $"\"{tracks[0].DisplayTitle}\"" : $"{paths.Count} tracks";

    /// <summary>The right-hand panel shows the History tab rather than the Queue.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowQueue))]
    public partial bool ShowHistory { get; set; }

    public bool ShowQueue => !ShowHistory;
    public AudioEngine? Audio { get; }
    public MixEngine? Mix { get; }
    /// <summary>Every deck the mixer has, shown or not.</summary>
    public IReadOnlyList<DeckViewModel> Decks { get; } = [];
    public DeckViewModel? Deck1 => Decks.ElementAtOrDefault(0);
    public DeckViewModel? Deck2 => Decks.ElementAtOrDefault(1);
    public DeckViewModel? Deck3 => Decks.ElementAtOrDefault(2);
    public DeckViewModel? Deck4 => Decks.ElementAtOrDefault(3);

    private int _deckCount;

    /// <summary>The decks on screen, in playing order: 1, 2 (, 3, 4).</summary>
    public IReadOnlyList<DeckViewModel> ActiveDecks => Decks.Take(_deckCount).ToList();

    /// <summary>Four decks on screen rather than two (a setting).</summary>
    public bool FourDecks => _deckCount == 4;

    /// <summary>
    /// Shows 2 or 4 decks. Going back to 2 is refused while Deck 3 or 4 is playing; otherwise
    /// they're emptied. Returns whether it changed.
    /// </summary>
    public bool SetDeckCount(int count)
    {
        count = count == 4 && Decks.Count >= 4 ? 4 : Math.Min(2, Decks.Count);
        if (count == _deckCount)
            return false;
        var hidden = Decks.Skip(count).ToList();
        if (hidden.FirstOrDefault(d => d.IsPlaying) is { } playing)
        {
            ShowNotice($"Deck {playing.Label} is playing. Stop it before going back to two decks.");
            OnPropertyChanged(nameof(FourDecks));   // put the switch back
            return false;
        }
        foreach (DeckViewModel deck in hidden)
        {
            deck.IsCueOn = false;
            deck.EjectCommand.Execute(null);
        }
        _deckCount = count;
        _settings.DeckCount = count;
        Log.Info($"Decks: {count}");
        OnPropertyChanged(nameof(FourDecks));
        OnPropertyChanged(nameof(ActiveDecks));
        Mixer?.DeckCountChanged();
        AutoPlay?.Refresh();
        return true;
    }

    /// <summary>
    /// Another deck on the same side of the crossfader that the room is hearing. Only one song at
    /// a time per side: the next one waits until it's faded out.
    /// </summary>
    internal DeckViewModel? OnAirBeside(DeckViewModel deck) => ActiveDecks.FirstOrDefault(d =>
        d != deck && d.IsOnAir && Crossfade.Side(d.Deck.Index) == Crossfade.Side(deck.Deck.Index));
    public MixerViewModel? Mixer { get; }
    public PreviewViewModel? Preview { get; }
    public AutoPlayViewModel? AutoPlay { get; }
    public SettingsViewModel Settings { get; }

    [ObservableProperty]
    public partial string Clock { get; private set; } = "";

    /// <summary>Why there's no sound at all (e.g. BASS failed to load).</summary>
    [ObservableProperty]
    public partial string? AudioError { get; private set; }

    /// <summary>A short message in the top bar, e.g. why a load was refused. Clears itself.</summary>
    [ObservableProperty]
    public partial string? Notice { get; private set; }

    public string MainOutputName => OutputName(Mix?.Main);
    public string CueOutputName => OutputName(Mix?.Cue);
    public bool HasMainOutput => Mix is { } mix && (mix.IsOffline || mix.Main.Device is not null);

    /// <summary>Headphones are available. Offline mixes (tests) count, since nothing reaches the speakers.</summary>
    public bool HasCueOutput => Mix is { } mix && (mix.IsOffline || mix.Cue.Device is not null);

    private string OutputName(OutputBus? bus) =>
        Mix is null ? "No audio" : Mix.IsOffline ? "Offline" : bus?.Device?.Name ?? "Not set";

    private void UpdateClock() => Clock = DateTime.Now.ToString("t");

    public void Start()
    {
        _clock.Start();
        if (Mix is { IsOffline: false } && Audio is not null)
            ConnectSavedOutputs();
        if (Mix is not null)
            _playbackTimer.Start();
        _ = StartLibraryAsync();
    }

    private async Task StartLibraryAsync()
    {
        await Library.StartAsync(_settings.SelectedRootPath, _settings.SelectedFolder);
        Analysis?.QueueLibrary();   // after the scan, so new files are included
    }

    /// <summary>
    /// Reconnects the speakers and headphones chosen last time. The speakers fall back to the
    /// system default; the headphones never do, so cueing can't leak into the room.
    /// </summary>
    private void ConnectSavedOutputs()
    {
        AudioDevice? main = Audio!.FindDevice(_settings.MainDevice);
        AudioDevice? cue = Audio.FindDevice(_settings.CueDevice);
        if (_settings.MainDevice is not null && main is null)
            ShowNotice($"Speaker output \"{_settings.MainDevice.Name}\" isn't connected; using the system default.");
        else if (_settings.CueDevice is not null && cue is null)
            ShowNotice($"Headphone output \"{_settings.CueDevice.Name}\" isn't connected.");
        main ??= Audio.GetDefaultDevice();
        SetOutputs(main, cue, remember: false);
        Settings.RefreshDevices();
    }

    public void SetOutputs(AudioDevice? main, AudioDevice? cue, bool remember = true)
    {
        if (Mix is null)
            return;
        try
        {
            Mix.SetOutputs(main, cue);
            Log.Info($"Outputs: main = {main?.Name ?? "none"} [{main?.Driver}], cue = {cue?.Name ?? "none"} [{cue?.Driver}]");
            if (remember)
            {
                _settings.MainDevice = main?.Preference;
                _settings.CueDevice = cue?.Preference;
            }
        }
        catch (AudioException ex)
        {
            Log.Error("Couldn't open output", ex);
            ShowNotice($"Couldn't open that output: {ex.Message}");
        }
        OnPropertyChanged(nameof(MainOutputName));
        OnPropertyChanged(nameof(CueOutputName));
        OnPropertyChanged(nameof(HasMainOutput));
        OnPropertyChanged(nameof(HasCueOutput));
    }

    // ───────────── Decks ─────────────

    /// <summary>Asks the user to confirm something; the window supplies the dialog.</summary>
    public Func<string, string, Task<bool>>? ConfirmAsync { get; set; }

    /// <summary>Whether a load onto this deck would go ahead without being refused outright.</summary>
    public bool CanLoad(DeckViewModel deck) => !deck.IsPlaying || _settings.LoadOntoPlayingDeck != PlayingDeckLoad.Block;

    /// <summary>
    /// Loads a track onto a deck, protecting a deck that's playing as configured in Settings.
    /// Returns whether the track was loaded.
    /// </summary>
    public async Task<bool> LoadToDeckAsync(DeckViewModel deck, Track track)
    {
        if (deck.IsPlaying)
        {
            switch (_settings.LoadOntoPlayingDeck)
            {
                case PlayingDeckLoad.Block:
                    Log.Info($"Deck {deck.Label}: refused load of \"{track.DisplayTitle}\" (deck is playing)");
                    ShowNotice($"Deck {deck.Label} is playing. Pause it first to load a new track.");
                    return false;
                case PlayingDeckLoad.Ask:
                    string where = deck.IsOnAir ? " to the room" : "";
                    if (ConfirmAsync is null || !await ConfirmAsync(
                            $"Replace the track on Deck {deck.Label}?",
                            $"\"{deck.Title}\" is still playing{where}. Loading \"{track.DisplayTitle}\" will stop it."))
                        return false;
                    break;
            }
        }

        try
        {
            deck.Load(track);
            return true;
        }
        catch (AudioException ex)
        {
            Log.Error($"Couldn't load {track.Path}", ex);
            ShowNotice($"Couldn't load {track.DisplayTitle}: {ex.Message}");
            return false;
        }
    }

    public void RefreshDecks()
    {
        foreach (DeckViewModel deck in Decks)
            deck.Refresh();
    }

    /// <summary>One playback tick: fades, end-of-track, snaps, and all the moving readouts.</summary>
    public void Tick()
    {
        Mix?.Update();
        Mixer?.Update();
        AutoPlay?.Tick();
        RefreshDecks();
        Preview?.Refresh();
    }

    public async void ShowNotice(string text)
    {
        Notice = text;
        await Task.Delay(TimeSpan.FromSeconds(4));
        if (Notice == text)
            Notice = null;
    }

    /// <summary>Library columns the DJ has hidden (by header); changed by the column menu, saved with the settings.</summary>
    public List<string> HiddenColumns => _settings.HiddenColumns;

    /// <summary>Copies state worth remembering into the settings before they're saved.</summary>
    public void SaveState()
    {
        (_settings.SelectedRootPath, _settings.SelectedFolder) = Library.GetSelection();
    }

    public void Dispose()
    {
        _clock.Stop();
        _playbackTimer.Stop();
        Library.Dispose();
        Analysis?.Dispose();   // stops decoding before BASS goes away
        Mix?.Dispose();
        Audio?.Dispose();
    }
}
