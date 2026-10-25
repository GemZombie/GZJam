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
        Library = new LibraryViewModel(database) { NearBpm = settings.NearBpmPercent / 100 };
        Art = new AlbumArtCache(database, Path.Combine(Path.GetDirectoryName(Path.GetFullPath(database.DatabasePath))!, "art"));
        Queue = new QueueViewModel();
        History = new HistoryViewModel(database);
        Library.TonightSince = () => History.CurrentSessionStartUtc;
        History.SessionDeleted += wasTonight =>
        {
            // Played dates and counts come from History: show them without the deleted session.
            Library.RefreshResults();
            if (wasTonight)
            {
                foreach (Track track in Queue.Items.Select(i => i.Track).Concat(Decks.Select(d => d.Track)).OfType<Track>())
                    track.IsPlayedTonight = false;
            }
        };
        if (audio is not null)
        {
            audio.ExtraBuffering = settings.ExtraBuffering;
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
            mix.ExclusiveSpeakers = settings.ExclusiveSpeakers;
            mix.MainOutputLost += (_, _) => OnMainOutputLost();
            mix.MainDropout += (_, _) => Log.Info(
                $"Speakers: the sound buffer ran dry (a dropout may have been heard){(settings.ExtraBuffering ? "" : "; Settings › Sound › Extra protection against crackles may help")}");
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

    /// <summary>Songs' album art, for the decks, queue and library.</summary>
    public AlbumArtCache Art { get; }

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
        Library.ResultsFor(path)
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
        if (tracks.Any(t => t.ManualBpm is not null))
            SetManualBpm(tracks, null);   // detection again: forget a typed BPM
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

    /// <summary>
    /// Sets tracks' BPM by hand (when detection and the tag are both wrong), or with null goes
    /// back to the tag and detection. Saved in GZJam's database, never in the files.
    /// </summary>
    public void SetManualBpm(IReadOnlyList<Track> tracks, double? bpm)
    {
        var paths = tracks.Select(t => t.Path).Distinct().ToList();
        if (paths.Count == 0)
            return;
        _database.SetManualBpm(paths, bpm);
        foreach (string path in paths)
        {
            foreach (Track track in TracksFor(path).Concat(tracks.Where(t => t.Path == path)))
                track.ManualBpm = bpm;
            foreach (DeckViewModel deck in Decks.Where(d => d.Track?.Path == path))
                deck.TrackBpmChanged();
        }
        string what = paths.Count == 1 ? $"\"{tracks[0].DisplayTitle}\"" : $"{paths.Count} tracks";
        Log.Info($"BPM for {what}: {(bpm is { } b ? Format.Bpm(b) + " (typed)" : "automatic")}");
    }

    /// <summary>Settings › Import from Mixxx; afterwards, the library, labels and History show what came over.</summary>
    public MixxxImportViewModel CreateMixxxImport() => new(_database, _ =>
    {
        Library.LabelsChanged();
        Library.RefreshPlaylists();
        Library.RefreshResults();
        History.Reload();
    });

    /// <summary>Where the DJ set a file to start and end.</summary>
    internal ManualTrim GetTrim(string path) => _database.GetTrim(path);

    internal void SaveTrim(string path, ManualTrim trim) => _database.SetTrim(path, trim);

    /// <summary>The jumps the DJ set in a file.</summary>
    internal IReadOnlyList<SongJump> GetJumps(string path) => _database.GetJumps(path);

    internal void SaveJumps(string path, IReadOnlyList<SongJump> jumps) => _database.SetJumps(path, jumps);

    /// <summary>The cues the DJ marked in a file.</summary>
    internal IReadOnlyList<SongCue> GetCues(string path) => _database.GetCues(path);

    internal void SaveCues(string path, IReadOnlyList<SongCue> cues) => _database.SetCues(path, cues);

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

    // ───────────── Labels ─────────────

    /// <summary>Every label, with how many songs carry it.</summary>
    public IReadOnlyList<LabelInfo> GetLabels() => _database.GetLabels();

    /// <summary>Puts a label on tracks (creating it if new) or takes it off, wherever they are on screen.</summary>
    public void SetLabel(IReadOnlyList<Track> tracks, string name, bool on)
    {
        var paths = tracks.Select(t => t.Path).Distinct().ToList();
        if (paths.Count == 0 || string.IsNullOrWhiteSpace(name))
            return;
        if (on)
            name = _database.AddLabel(paths, name);
        else
            _database.RemoveLabel(paths, name);
        foreach (string path in paths)
        {
            var labels = _database.GetLabels(path);
            foreach (Track track in TracksFor(path).Concat(tracks.Where(t => t.Path == path)))
                track.Labels = labels;
        }
        Log.Info($"Label \"{name}\" {(on ? "on" : "off")} {Describe(tracks, paths)}");
        Library.LabelsChanged();
    }

    /// <summary>
    /// Renames a label on every song (or, if another label already has the name, makes the two
    /// one), wherever the songs are on screen. A search for the label follows it.
    /// </summary>
    public void RenameLabel(string name, string newName)
    {
        if (string.IsNullOrWhiteSpace(newName) || newName.Trim() == name)
            return;
        bool merging = _database.GetLabels().Any(l => l.Name.Equals(newName.Trim(), StringComparison.OrdinalIgnoreCase)
            && !l.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (_database.RenameLabel(name, newName) is not { } stored)
            return;
        var onScreen = Library.Results.Concat(Queue.Items.Select(i => i.Track)).Concat(Decks.Select(d => d.Track)).OfType<Track>()
            .Where(t => t.Labels.Contains(name, StringComparer.OrdinalIgnoreCase)).Distinct().ToList();
        foreach (var group in onScreen.GroupBy(t => t.Path))
        {
            var labels = _database.GetLabels(group.Key);
            foreach (Track track in group)
                track.Labels = labels;
        }
        Log.Info($"Label \"{name}\" renamed \"{stored}\"{(merging ? ", joining the label already called that" : "")}");
        if (merging)
            ShowNotice($"\"{name}\" and \"{stored}\" are one label now.");
        Library.LabelRenamed(name, stored);
    }

    // ───────────── Playlists ─────────────

    /// <summary>A new, empty playlist (a taken name gets a number). Null if the name is empty.</summary>
    public PlaylistInfo? CreatePlaylist(string name, IReadOnlyList<Track>? songs = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;
        PlaylistInfo playlist = _database.CreatePlaylist(name);
        if (songs is { Count: > 0 })
            _database.AddToPlaylist(playlist.Id, songs.Select(t => t.Path));
        Log.Info($"Playlist \"{playlist.Name}\" made{(songs is { Count: > 0 } ? $" with {songs.Count} songs" : "")}");
        Library.RefreshPlaylists();
        return playlist;
    }

    /// <summary>Asks for a name, then makes a playlist (of <paramref name="songs"/>, if any) and opens it.</summary>
    public async Task<PlaylistInfo?> CreatePlaylistAsync(IReadOnlyList<Track>? songs = null, string suggestion = "", bool open = true)
    {
        if (PromptAsync is null)
            return null;
        string message = songs is not { Count: > 0 } ? "A set to plan before the night, in order."
            : songs.Count == 1 ? $"With \"{songs[0].DisplayTitle}\"" : $"With {songs.Count} songs";
        if (await PromptAsync("New playlist", message, "Make it", suggestion) is not { } name || CreatePlaylist(name, songs) is not { } playlist)
            return null;
        if (open)
            Library.OpenPlaylist(playlist.Id);
        return playlist;
    }

    /// <summary>Adds songs to a playlist: at the end, or before its entry number <paramref name="index"/> (from 0).</summary>
    public void AddToPlaylist(PlaylistInfo playlist, IReadOnlyList<Track> songs, int? index = null)
    {
        if (songs.Count == 0)
            return;
        _database.AddToPlaylist(playlist.Id, songs.Select(t => t.Path), index);
        Log.Info($"Added {(songs.Count == 1 ? $"\"{songs[0].DisplayTitle}\"" : $"{songs.Count} songs")} to playlist \"{playlist.Name}\"");
        Library.RefreshPlaylists();
        if (Library.SelectedPlaylist?.Id != playlist.Id)
            ShowNotice(songs.Count == 1 ? $"Added to {playlist.Name}" : $"{songs.Count} songs added to {playlist.Name}");
    }

    /// <summary>Takes rows of the open playlist out of it. The files are untouched.</summary>
    public void RemoveFromPlaylist(IReadOnlyList<Track> rows)
    {
        if (Library.SelectedPlaylist is not { } playlist || rows.Select(t => t.PlaylistEntryId).OfType<long>().ToList() is not { Count: > 0 } entries)
            return;
        _database.RemoveFromPlaylist(playlist.Id, entries);
        Log.Info($"Removed {entries.Count} from playlist \"{playlist.Name}\"");
        Library.RefreshPlaylists();
    }

    /// <summary>Moves rows of the open playlist to before its entry number <paramref name="index"/> (from 0).</summary>
    public void MoveInPlaylist(IReadOnlyList<Track> rows, int index)
    {
        if (Library.SelectedPlaylist is not { } playlist || rows.Select(t => t.PlaylistEntryId).OfType<long>().ToList() is not { Count: > 0 } entries)
            return;
        _database.MovePlaylistEntries(playlist.Id, entries, index);
        Library.RefreshPlaylists();
    }

    public async Task RenamePlaylistAsync(PlaylistInfo playlist)
    {
        if (PromptAsync is null || await PromptAsync($"Rename \"{playlist.Name}\"", $"{playlist.Count} {(playlist.Count == 1 ? "song" : "songs")}",
                "Rename", playlist.Name) is not { } name || string.IsNullOrWhiteSpace(name))
            return;
        string stored = _database.RenamePlaylist(playlist.Id, name);
        Log.Info($"Playlist \"{playlist.Name}\" renamed \"{stored}\"");
        Library.RefreshPlaylists();
    }

    /// <summary>Deletes a playlist, after asking. Its songs stay in the library.</summary>
    public async Task DeletePlaylistAsync(PlaylistInfo playlist)
    {
        if (playlist.Count > 0 && (ConfirmAsync is null || !await ConfirmAsync($"Delete the playlist \"{playlist.Name}\"?",
                $"Its {playlist.Count} {(playlist.Count == 1 ? "song stays" : "songs stay")} in your library; only the list goes.", "Delete")))
            return;
        if (Library.SelectedPlaylist?.Id == playlist.Id)
            Library.OpenPlaylist(null);
        _database.DeletePlaylist(playlist.Id);
        Log.Info($"Playlist \"{playlist.Name}\" deleted");
        Library.RefreshPlaylists();
    }

    /// <summary>
    /// Adds a playlist's songs to the end of the queue (from row <paramref name="from"/> on, if
    /// given), never replacing what's there. Songs no longer in the library are skipped.
    /// </summary>
    public void AddPlaylistToQueue(PlaylistInfo playlist, Track? from = null)
    {
        var songs = _database.Search(null, playlist: playlist.Id).ToList();
        if (from?.PlaylistNumber is { } number)
            songs = songs.Where(t => t.PlaylistNumber >= number).ToList();
        if (songs.Count == 0)
            return;
        // The library's rows where they're showing, so marks and "played tonight" stay in step.
        var shown = songs.Select(t => Library.ResultsFor(t.Path).FirstOrDefault(r => r.PlaylistEntryId == t.PlaylistEntryId) ?? t).ToList();
        if (TonightSince() is { } since)
        {
            foreach (Track track in shown)
                track.IsPlayedTonight = track.LastPlayedUtc >= since;
        }
        Queue.Add(shown);
        Log.Info($"Playlist \"{playlist.Name}\" added to the queue: {shown.Count} songs{(from is null ? "" : $" from number {from.PlaylistNumber}")}");
        ShowNotice(shown.Count == 1 ? "1 song added to the queue" : $"{shown.Count} songs added to the queue");
    }

    private DateTime? TonightSince() => History.CurrentSessionStartUtc;

    /// <summary>Saves the queue, in its order, as a new playlist (asks for its name).</summary>
    public Task SaveQueueAsPlaylistAsync() =>
        CreatePlaylistAsync(Queue.Items.Select(i => i.Track).ToList(), open: false);

    /// <summary>Saves the night shown in History, in the order it was played, as a new playlist.</summary>
    public Task SaveSessionAsPlaylistAsync() =>
        CreatePlaylistAsync(History.Plays.Select(p => p.Track).ToList(), History.SelectedName ?? "", open: false);

    /// <summary>Asks the DJ for a short piece of text (heading, message, button); the window supplies the dialog.</summary>
    /// <summary>Asks for a short text (heading, message, OK button, starting text); the window supplies the dialog.</summary>
    public Func<string, string, string, string, Task<string?>>? PromptAsync { get; set; }

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

    // Split, the side comes first: a long name is trimmed at its end.
    public string MainOutputName => (Mix?.IsSplit == true ? "Left · " : "") + OutputName(Mix?.Main);
    public string CueOutputName => (Mix?.IsSplit == true ? "Right · " : "") + OutputName(Mix?.Cue);
    public bool HasMainOutput => Mix is { } mix && (mix.IsOffline || mix.Main.Device is not null);

    /// <summary>Headphones are available. Offline mixes (tests) count, since nothing reaches the speakers.</summary>
    public bool HasCueOutput => Mix is { } mix && (mix.IsOffline || mix.Cue.Device is not null);

    public string BoothOutputName => Mix?.Booth.Device?.Name ?? _settings.BoothDevice?.Name ?? "Not set";

    /// <summary>The booth is playing.</summary>
    public bool HasBoothOutput => Mix?.Booth.Device is not null;

    /// <summary>A booth output has been chosen (it may not be connected right now): the top bar shows it.</summary>
    public bool ShowsBoothOutput => HasBoothOutput || _settings.BoothDevice is not null;

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
        await Library.StartAsync(_settings.SelectedRootPath, _settings.SelectedFolder, _settings.OpenPlaylist);
        Analysis?.QueueLibrary();   // after the scan, so new files are included
    }

    /// <summary>
    /// Reconnects the speakers, headphones and booth chosen last time. The speakers fall back to
    /// the system default; the headphones and booth never do, so cueing can't leak into the room.
    /// </summary>
    private void ConnectSavedOutputs()
    {
        var (main, cue, notice) = ChooseOutputs(_settings.MainDevice, _settings.CueDevice,
            Audio!.FindDevice(_settings.MainDevice), Audio.FindDevice(_settings.CueDevice), Audio.GetDefaultDevice());
        AudioDevice? booth = Audio.FindDevice(_settings.BoothDevice);
        if (_settings.BoothDevice is { } savedBooth && booth is null)
            notice = $"{notice} Booth output \"{savedBooth.Name}\" isn't connected.".TrimStart();
        if (notice is not null)
        {
            Log.Info(notice);
            ShowNotice(notice);
        }
        SetOutputs(main, cue, booth, remember: false);
        Settings.RefreshDevices();
    }

    /// <summary>
    /// The outputs to open from the saved choices and what's connected now. Speakers that aren't
    /// connected fall back to the system default. If that's the very output the headphones are on,
    /// the headphones are left off, so a preview can't reach the room (after an unplug, a laptop's
    /// own output is often both).
    /// </summary>
    internal static (AudioDevice? Main, AudioDevice? Cue, string? Notice) ChooseOutputs(
        AudioDevicePreference? savedMain, AudioDevicePreference? savedCue, AudioDevice? main, AudioDevice? cue, AudioDevice? fallback)
    {
        string? notice = null;
        if (savedMain is not null && main is null)
            notice = $"Speaker output \"{savedMain.Name}\" isn't connected; using {fallback?.Name ?? "the system default"}.";
        else if (savedCue is not null && cue is null)
            notice = $"Headphone output \"{savedCue.Name}\" isn't connected.";

        if (main is null)
        {
            main = fallback;
            if (main is not null && cue?.Index == main.Index)
            {
                cue = null;
                notice = $"{notice} Headphones are off: they'd play in the room. Choose them in Settings › Sound.".TrimStart();
            }
        }
        return (main, cue, notice);
    }

    /// <summary>
    /// The speakers stopped by themselves while GZJam had them alone (unplugged, or taken by
    /// Windows): opens them again if they're still there, else the system default, as at startup.
    /// </summary>
    private void OnMainOutputLost()
    {
        AudioDevice? lost = Mix!.Main.Device;
        string name = lost?.Name ?? "The speaker output";
        Log.Error($"Speaker output stopped: {name}");
        AudioDevice? cue = Audio!.FindDevice(_settings.CueDevice);
        AudioDevice? booth = Audio.FindDevice(_settings.BoothDevice);

        // Still there (Windows took it for a moment): open it again. Windows may still list a card
        // just unplugged, so only opening it says it's back.
        if (Audio.FindDevice(_settings.MainDevice) is { } saved && SetOutputs(saved, cue, booth, remember: false, quiet: true))
        {
            ShowNotice($"{name} stopped for a moment; GZJam opened it again.");
            Settings.RefreshDevices();
            return;
        }

        // Gone: the system default, as at startup, keeping the headphones out of the room.
        AudioDevice? fallback = Audio.GetDefaultDevice() is { } byDefault && byDefault.Index != lost?.Index ? byDefault : null;
        var (main, newCue, _) = ChooseOutputs(_settings.MainDevice, _settings.CueDevice, null, cue, fallback);
        if (SetOutputs(main, newCue, booth, remember: false))
        {
            string notice = main is null
                ? $"{name} stopped (unplugged?), and no other speakers are connected. Choose them in Settings › Sound."
                : $"{name} stopped (unplugged?), so the music is playing through {main.Name}." +
                  (cue is not null && newCue is null ? " Headphones are off: they'd play in the room. Choose them in Settings › Sound." : "");
            Log.Info(notice);
            ShowNotice(notice);
        }
        Settings.RefreshDevices();
    }

    /// <summary>Applies <see cref="AppSettings.ExclusiveSpeakers"/>, opening the speakers again with it.</summary>
    public void SetExclusiveSpeakers(bool value)
    {
        _settings.ExclusiveSpeakers = value;
        Log.Info($"Speakers for GZJam alone: {(value ? "on" : "off")}");
        if (Mix is null)
            return;
        Mix.ExclusiveSpeakers = value;
        if (!Mix.IsOffline && Mix.Main.Device is not null)
            SetOutputs(Mix.Main.Device, Mix.Cue.Device, Mix.Booth.Device, remember: false);
    }

    /// <summary>Applies <see cref="AppSettings.ExtraBuffering"/>, opening the outputs again with it.</summary>
    public void SetExtraBuffering(bool value)
    {
        _settings.ExtraBuffering = value;
        Log.Info($"Extra protection against crackles: {(value ? "on" : "off")}");
        if (Audio is null)
            return;
        Audio.ExtraBuffering = value;
        if (Mix is { IsOffline: false })
            SetOutputs(Mix.Main.Device, Mix.Cue.Device, Mix.Booth.Device, remember: false);
    }

    /// <summary>Opens outputs. Returns whether they opened (if not, the last ones play on and a notice says why).</summary>
    /// <param name="remember">Save them as the DJ's choice (a booth that won't open is forgotten).</param>
    /// <param name="quiet">No notice if they won't open (GZJam is only trying).</param>
    public bool SetOutputs(AudioDevice? main, AudioDevice? cue, AudioDevice? booth, bool remember = true, bool quiet = false)
    {
        if (Mix is null)
            return false;
        bool opened = false;
        try
        {
            Mix.SetOutputs(main, cue, booth);
            Log.Info($"Outputs: main = {main?.Name ?? "none"} [{main?.Driver}]{(Mix.SpeakersAreExclusive ? " (GZJam's alone)" : "")}{(Mix.IsSplit ? " (split: speakers left, headphones right)" : "")}, " +
                     $"cue = {Mix.Cue.Device?.Name ?? "none"} [{Mix.Cue.Device?.Driver}], booth = {Mix.Booth.Device?.Name ?? "none"} [{Mix.Booth.Device?.Driver}]");
            if (Mix.CueRefusal is { } cueWhy)
            {
                Log.Error($"Headphones couldn't open: {cueWhy}");
                ShowNotice($"The headphones couldn't open: {cueWhy} The speakers are playing.");
            }
            if (Mix.BoothRefusal is { } boothWhy)
            {
                Log.Error($"Booth couldn't open: {boothWhy}");
                ShowNotice($"The booth is off: {boothWhy}.".Replace("..", "."));
            }
            if (Mix.ExclusiveRefusal is { } why)
            {
                Log.Info($"Speakers shared, not GZJam's alone: {why}");
                ShowNotice($"{why}, so it's shared: other programs' sounds can still play through it.");
            }
            if (remember)
            {
                _settings.MainDevice = main?.Preference;
                _settings.CueDevice = cue?.Preference;
                _settings.BoothDevice = Mix.Booth.Device?.Preference;
            }
            opened = true;
        }
        catch (AudioException ex)
        {
            Log.Error("Couldn't open output", ex);
            if (!quiet)
            {
                ShowNotice($"Couldn't open that output: {ex.Message}");
                Settings.RefreshDevices();   // show the outputs still in use
            }
        }
        OnPropertyChanged(nameof(MainOutputName));
        OnPropertyChanged(nameof(CueOutputName));
        OnPropertyChanged(nameof(HasMainOutput));
        OnPropertyChanged(nameof(HasCueOutput));
        OnPropertyChanged(nameof(BoothOutputName));
        OnPropertyChanged(nameof(HasBoothOutput));
        OnPropertyChanged(nameof(ShowsBoothOutput));
        return opened;
    }

    // ───────────── Decks ─────────────

    /// <summary>Starting over with an empty library was confirmed: GZJam closes and opens again (see <see cref="LibraryReset"/>).</summary>
    public event Action? StartOverRequested;

    internal void RequestStartOver() => StartOverRequested?.Invoke();

    /// <summary>Asks the user to confirm something (heading, message, the confirm button's text); the window supplies the dialog.</summary>
    public Func<string, string, string, Task<bool>>? ConfirmAsync { get; set; }

    /// <summary>
    /// Asks whether to close GZJam while the room is hearing music (the message says what's
    /// playing); true to close. The window supplies the dialog, with "Keep playing" as its default.
    /// </summary>
    public Func<string, Task<bool>>? ConfirmCloseAsync { get; set; }

    /// <summary>
    /// What the room is hearing, for the question before closing: "Deck 1 is playing "Jump
    /// Steady" to the room." Null when no deck is on air; music only in the headphones (the
    /// preview, or a cued deck) doesn't count.
    /// </summary>
    public string? WhatTheRoomIsHearing()
    {
        var onAir = Decks.Where(d => d.IsOnAir).ToList();
        return onAir.Count switch
        {
            0 => null,
            1 => $"Deck {onAir[0].Label} is playing \u201c{onAir[0].Title}\u201d to the room.",
            _ => $"Decks {string.Join(" and ", onAir.Select(d => d.Label))} are playing to the room.",
        };
    }

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
                            $"\"{deck.Title}\" is still playing{where}. Loading \"{track.DisplayTitle}\" will stop it.",
                            "Load anyway"))
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
        foreach (DeckViewModel deck in Decks)
            deck.UpdateMeter();
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
        _settings.OpenPlaylist = Library.SelectedPlaylist?.Id;
    }

    public void Dispose()
    {
        _clock.Stop();
        _playbackTimer.Stop();
        Library.Dispose();
        Art.Dispose();
        Analysis?.Dispose();   // stops decoding before BASS goes away
        Mix?.Dispose();
        Audio?.Dispose();
    }
}
