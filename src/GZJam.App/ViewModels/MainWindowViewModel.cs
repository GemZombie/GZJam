using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using GZJam.Audio;
using GZJam.Library;

namespace GZJam.App.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly AppSettings _settings;
    private readonly DispatcherTimer _clock;
    private readonly DispatcherTimer _playbackTimer;

    /// <param name="audio">BASS, or null if it failed to load (the library still works).</param>
    /// <param name="mix">The mixer; tests pass an offline one.</param>
    /// <param name="audioError">Why audio isn't available, shown in the window.</param>
    public MainWindowViewModel(LibraryDatabase database, AppSettings settings,
        AudioEngine? audio = null, MixEngine? mix = null, string? audioError = null)
    {
        _settings = settings;
        Audio = audio;
        Mix = mix;
        AudioError = audioError;
        Library = new LibraryViewModel(database);
        Queue = new QueueViewModel();

        if (mix is not null)
        {
            Decks = mix.Decks.Select(d => new DeckViewModel(this, d, settings)).ToList();
            foreach (DeckViewModel deck in Decks)
                deck.Deck.Finished += (_, _) =>
                {
                    Log.Info($"Deck {deck.Letter}: finished \"{deck.Title}\"");
                    deck.Refresh();
                };
            Mixer = new MixerViewModel(this, mix);
            Preview = new PreviewViewModel(this, mix.Preview);
        }
        Settings = new SettingsViewModel(this, settings);

        _clock = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => UpdateClock());
        _playbackTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Render, (_, _) => Tick());
        UpdateClock();
    }

    public LibraryViewModel Library { get; }
    public QueueViewModel Queue { get; }
    public AudioEngine? Audio { get; }
    public MixEngine? Mix { get; }
    public IReadOnlyList<DeckViewModel> Decks { get; } = [];
    public DeckViewModel? DeckA => Decks.ElementAtOrDefault(0);
    public DeckViewModel? DeckB => Decks.ElementAtOrDefault(1);
    public MixerViewModel? Mixer { get; }
    public PreviewViewModel? Preview { get; }
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
        _ = Library.StartAsync(_settings.SelectedRootPath, _settings.SelectedFolder);
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
                    Log.Info($"Deck {deck.Letter}: refused load of \"{track.DisplayTitle}\" (deck is playing)");
                    ShowNotice($"Deck {deck.Letter} is playing. Pause it first to load a new track.");
                    return false;
                case PlayingDeckLoad.Ask:
                    string where = deck.IsOnAir ? " to the room" : "";
                    if (ConfirmAsync is null || !await ConfirmAsync(
                            $"Replace the track on Deck {deck.Letter}?",
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
        Mix?.Dispose();
        Audio?.Dispose();
    }
}
