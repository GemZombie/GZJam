using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GZJam.Audio;
using GZJam.Library;

namespace GZJam.App.ViewModels;

/// <summary>
/// The room EQ for the venue you're at (#272), on the speakers only: a key on the mixer under
/// MAIN, which rolls a panel down under the decks. Venues are named and remembered, each with its
/// EQ; choosing one changes the sound at once, mid-song, so it can be set during a soundcheck with
/// music playing. One EQ, two views: three bands as on a song (with starting points for a boomy or
/// a padded room), or a 10-band graphic EQ. The headphones and booth never get it.
/// </summary>
public sealed partial class RoomEqViewModel : ObservableObject
{
    public const string NoVenue = "No venue";

    private readonly MainWindowViewModel _owner;
    private readonly MixEngine _mix;   // its Main is made again when the outputs change: always asked for afresh
    private readonly AppSettings _settings;

    public RoomEqViewModel(MainWindowViewModel owner, MixEngine mix, AppSettings settings)
    {
        _owner = owner;
        _mix = mix;
        _settings = settings;
        if (settings.Venue is { } name && Find(name) is null)
            settings.Venue = null;
        Bands = [.. RoomEq.BandHz.Select((hz, i) => new RoomEqBand(this, i, hz))];
        _shownView = Eq.View;
        Apply();
        RefreshSound();
    }

    // ───────────── The venue ─────────────

    private Venue? Current => _settings.Venue is { } name ? Find(name) : null;

    private Venue? Find(string name) => _settings.Venues.FirstOrDefault(v => string.Equals(v.Name, name, StringComparison.CurrentCultureIgnoreCase));

    public bool HasVenue => Current is not null;

    /// <summary>The venue's name, or <see cref="NoVenue"/>.</summary>
    public string VenueName => Current?.Name ?? NoVenue;

    /// <summary>
    /// The venues to choose from: none first, then yours by name. One list, changed in place: a
    /// new list each time makes the drop-down lose its choice.
    /// </summary>
    public ObservableCollection<string> VenueChoices { get; } = [NoVenue];

    private void SyncChoices()
    {
        List<string> wanted = [NoVenue, .. _settings.Venues.Select(v => v.Name).Order(StringComparer.CurrentCultureIgnoreCase)];
        for (int i = VenueChoices.Count - 1; i >= 0; i--)
        {
            if (!wanted.Contains(VenueChoices[i]))
                VenueChoices.RemoveAt(i);
        }
        for (int i = 0; i < wanted.Count; i++)
        {
            if (i >= VenueChoices.Count || VenueChoices[i] != wanted[i])
                VenueChoices.Insert(i, wanted[i]);
        }
    }

    /// <summary>The venue chosen in the panel's list: loaded at once, its EQ heard straight away.</summary>
    public string SelectedVenue
    {
        get => VenueName;
        set
        {
            // A venue taken out of the list clears its choice for a moment: that isn't the DJ choosing.
            if (value is null)
                return;
            Venue? venue = value == NoVenue ? null : Find(value);
            if (venue?.Name == Current?.Name)
                return;
            Log.Info(venue is null ? "Room EQ: no venue (flat)" : $"Room EQ: venue \"{venue.Name}\"");
            Load(venue);
        }
    }

    /// <summary>Says which venue is loaded as GZJam starts, so last week's room doesn't follow you unnoticed.</summary>
    internal void Announce()
    {
        if (Current is { } venue)
            _owner.ShowNotice(venue.Eq.IsFlat ? $"Venue: {venue.Name}" : $"Venue: {venue.Name}, its room EQ on the speakers", seconds: 6);
    }

    /// <summary>A new venue, flat to start, and loaded. Null if the name is empty or taken.</summary>
    public string? NewVenue(string name)
    {
        name = name.Trim();
        if (!IsNameFree(name, except: null))
        {
            SayTaken(name);
            return null;
        }
        var venue = new Venue { Name = name };
        _settings.Venues.Add(venue);
        Log.Info($"Room EQ: new venue \"{name}\"");
        Load(venue);
        return name;
    }

    public bool RenameVenue(string name)
    {
        name = name.Trim();
        if (Current is not { } venue)
            return false;
        if (!IsNameFree(name, except: venue))
        {
            SayTaken(name);
            return false;
        }
        Log.Info($"Room EQ: venue \"{venue.Name}\" renamed \"{name}\"");
        venue.Name = name;
        _settings.Venue = name;
        Apply();
        return true;
    }

    public void DeleteVenue()
    {
        if (Current is not { } venue)
            return;
        Load(null);
        _settings.Venues.Remove(venue);
        Log.Info($"Room EQ: venue \"{venue.Name}\" deleted");
        Apply();
    }

    /// <summary>
    /// Loads a venue (or none): its room EQ, and its mono and loudness target where it has its own
    /// (#440), all heard at once.
    /// </summary>
    private void Load(Venue? venue)
    {
        double targetBefore = _settings.LoudnessTargetNow;
        _settings.Venue = venue?.Name;
        ShownView = Eq.View;
        Apply();
        _owner.SoundSettingsChanged(targetBefore);
    }

    // ───────────── The venue's mono and loudness target (#440) ─────────────

    /// <summary>The venue's mono: as in Settings (the default, named), mono, or stereo.</summary>
    public IReadOnlyList<VenueChoice> MonoChoices { get; } =
        [new(null), new(true, "Mono"), new(false, "Stereo")];

    /// <summary>The venue's loudness target: as in Settings (the default, named), or one of its own.</summary>
    public IReadOnlyList<VenueChoice> TargetChoices { get; } =
        [new(null), .. LoudnessMatch.Targets.Select(t => new VenueChoice(t, LoudnessMatch.TargetName(t)))];

    public VenueChoice MonoChoice
    {
        get => MonoChoices.First(c => Equals(c.Value, Current?.Mono));
        set
        {
            if (value is null || Current is not { } venue || Equals(venue.Mono, value.Value))
                return;
            double target = _settings.LoudnessTargetNow;
            venue.Mono = (bool?)value.Value;
            Log.Info($"Venue \"{venue.Name}\": {(venue.Mono is { } mono ? mono ? "mono" : "stereo" : "mono as in Settings")}");
            _owner.SoundSettingsChanged(target);
        }
    }

    public VenueChoice TargetChoice
    {
        get => TargetChoices.First(c => Equals(c.Value, Current?.LoudnessTarget));
        set
        {
            if (value is null || Current is not { } venue || Equals(venue.LoudnessTarget, value.Value))
                return;
            double target = _settings.LoudnessTargetNow;
            venue.LoudnessTarget = (double?)value.Value;
            Log.Info($"Venue \"{venue.Name}\": loudness target {(venue.LoudnessTarget is { } own ? $"{own:0} LUFS" : "as in Settings")}");
            _owner.SoundSettingsChanged(target);
        }
    }

    /// <summary>The choices as Settings has the defaults now, and the venue's as chosen.</summary>
    internal void RefreshSound()
    {
        MonoChoices[0].Label = $"As in Settings: {(_settings.Mono ? "mono" : "stereo")}";
        TargetChoices[0].Label = $"As in Settings: {(LoudnessMatch.TargetName(_settings.LoudnessTarget) is "Usual" ? "usual" : LoudnessMatch.TargetName(_settings.LoudnessTarget))}";
        OnPropertyChanged(nameof(MonoChoice));
        OnPropertyChanged(nameof(TargetChoice));
    }

    private void SayTaken(string name)
    {
        if (name.Length > 0)
            _owner.ShowNotice($"There's already a venue called \"{name}\".");
    }

    public bool IsNameFree(string name, Venue? except) =>
        name.Length > 0 && !string.Equals(name, NoVenue, StringComparison.CurrentCultureIgnoreCase)
        && _settings.Venues.All(v => v == except || !string.Equals(v.Name, name, StringComparison.CurrentCultureIgnoreCase));

    internal Venue? CurrentVenue => Current;

    // ───────────── The panel and the key ─────────────

    /// <summary>The panel is rolled down under the decks.</summary>
    public bool IsShown
    {
        get => _settings.RoomEqShown;
        set
        {
            if (value == _settings.RoomEqShown)
                return;
            _settings.RoomEqShown = value;
            OnPropertyChanged();
        }
    }

    [RelayCommand]
    private void Toggle() => IsShown = !IsShown;

    /// <summary>The mixer key's light: on while the speakers get a room EQ.</summary>
    public bool IsOn => !Eq.IsFlat;

    /// <summary>What a click does, and the venue's name in full (the key may cut it short).</summary>
    public string KeyTip => Current is null
        ? "Room EQ: the speakers get the music flat. Click to choose the venue you're at, or make one."
        : Eq.IsFlat
            ? $"Venue: {Current.Name}. Its room EQ is flat. Click to set it."
            : $"Venue: {Current.Name}. Its room EQ is on the speakers (never the headphones). Click to change it.";

    // ───────────── The EQ ─────────────

    /// <summary>The venue's room EQ (flat with none): what the speakers get.</summary>
    public RoomEq Eq => Current?.Eq ?? RoomEq.Flat;

    private RoomEqView _shownView;

    /// <summary>The view on show. It's the one heard, except while Simple says *Set in Advanced*.</summary>
    public RoomEqView ShownView
    {
        get => _shownView;
        private set
        {
            if (SetProperty(ref _shownView, value))
                RaiseAll();
        }
    }

    public bool IsSimpleShown => ShownView == RoomEqView.Simple;
    public bool IsAdvancedShown => ShownView == RoomEqView.Advanced;

    /// <summary>
    /// Simple is on show, but the room hears an advanced setting three bands can't show: it says
    /// so, and offers to start again from the three bands. Nothing changes in the room by itself.
    /// </summary>
    public bool IsSetInAdvanced => IsSimpleShown && Eq.View == RoomEqView.Advanced && !Eq.AdvancedIsSimple;

    public bool ShowsSimpleFaders => IsSimpleShown && !IsSetInAdvanced;

    [RelayCommand]
    private void ShowSimple()
    {
        if (Eq.View == RoomEqView.Advanced && Eq.AdvancedIsSimple)
            Set(Eq with { View = RoomEqView.Simple });   // the same curve: nothing heard changes
        ShownView = RoomEqView.Simple;
    }

    /// <summary>Advanced starts shaped like the three bands, so the room hears no change on switching.</summary>
    [RelayCommand]
    private void ShowAdvanced()
    {
        if (Eq.View == RoomEqView.Simple)
            Set(Eq.WithAdvancedFromSimple());
        ShownView = RoomEqView.Advanced;
    }

    /// <summary>From *Set in Advanced*: the three bands as they were last set, heard from now on.</summary>
    [RelayCommand]
    private void UseThreeBands() => Set(Eq with { View = RoomEqView.Simple });

    public double MinDb => RoomEq.MinDb;
    public double MaxDb => RoomEq.MaxDb;

    public double Low
    {
        get => Eq.Low;
        set => Set(Eq with { Low = Round(value) });
    }

    public double Mid
    {
        get => Eq.Mid;
        set => Set(Eq with { Mid = Round(value) });
    }

    public double High
    {
        get => Eq.High;
        set => Set(Eq with { High = Round(value) });
    }

    public string LowText => EqEditor.Db(Eq.Low);
    public string MidText => EqEditor.Db(Eq.Mid);
    public string HighText => EqEditor.Db(Eq.High);
    public string MidHzText => Hz(Eq.MidHz);
    public bool IsMidMoved => Eq.MidHz != TrackEq.DefaultMidHz;

    public ObservableCollection<RoomEqBand> Bands { get; }

    internal static string Hz(double hz) => hz >= 1000 ? $"{hz / 1000:0.##} kHz" : $"{hz:0} Hz";

    private static double Round(double db) => Math.Round(Math.Clamp(db, RoomEq.MinDb, RoomEq.MaxDb) * 2) / 2;

    internal void SetBand(int band, double db)
    {
        double[] bands = [.. Eq.Bands];
        bands[band] = Round(db);
        Set(Eq with { View = RoomEqView.Advanced, Bands = bands });
    }

    /// <summary>Moves the mid band a third of an octave down or up, as on a song.</summary>
    [RelayCommand]
    private void MoveMid(string direction)
    {
        var steps = TrackEq.MidFrequencies;
        int at = steps.Select((hz, i) => (Distance: Math.Abs(Math.Log(hz / Eq.MidHz)), i)).MinBy(s => s.Distance).i;
        int to = Math.Clamp(at + (direction == "down" ? -1 : 1), 0, steps.Count - 1);
        Set(Eq with { MidHz = steps[to] });
    }

    /// <summary>Tapping a band's value puts it back to flat (the mid's frequency to 1 kHz).</summary>
    [RelayCommand]
    private void FlatBand(string band) => Set(band switch
    {
        "low" => Eq with { Low = 0 },
        "mid" => Eq with { Mid = 0 },
        "midhz" => Eq with { MidHz = TrackEq.DefaultMidHz },
        _ => Eq with { High = 0 },
    });

    /// <summary>
    /// A starting point for the room, in the simple view. Boomy: the bass and low mids down, for a
    /// reverberant hall or a muddy room. Padded: a little warmth, for a damped room full of people.
    /// </summary>
    [RelayCommand]
    private void Preset(string name)
    {
        RoomEq preset = name switch
        {
            "boomy2" => new RoomEq { Low = -6, Mid = -4, MidHz = 250 },
            "boomy1" => new RoomEq { Low = -3, Mid = -2, MidHz = 250 },
            "padded1" => new RoomEq { Low = 2, High = 1.5 },
            "padded2" => new RoomEq { Low = 4, High = 3 },
            _ => RoomEq.Flat,
        };
        Set(preset with { Bands = Eq.Bands });
        ShownView = RoomEqView.Simple;
    }

    /// <summary>Everything flat, in the view on show.</summary>
    [RelayCommand]
    private void Flat()
    {
        Set(IsAdvancedShown ? Eq with { View = RoomEqView.Advanced, Bands = new double[RoomEq.BandHz.Count] } : RoomEq.Flat with { Bands = Eq.Bands });
    }

    private void Set(RoomEq eq)
    {
        if (Current is not { } venue || eq == venue.Eq)
            return;
        venue.Eq = eq;
        _mix.Main.RoomEq = eq;
        RaiseAll();
    }

    /// <summary>The speakers get the venue's EQ (flat without one), and everything shown follows.</summary>
    private void Apply()
    {
        _mix.Main.RoomEq = Eq;
        SyncChoices();
        OnPropertyChanged(nameof(SelectedVenue));
        OnPropertyChanged(nameof(VenueName));
        OnPropertyChanged(nameof(HasVenue));
        RaiseAll();
    }

    private void RaiseAll()
    {
        OnPropertyChanged(nameof(Eq));
        OnPropertyChanged(nameof(IsOn));
        OnPropertyChanged(nameof(KeyTip));
        OnPropertyChanged(nameof(IsSimpleShown));
        OnPropertyChanged(nameof(IsAdvancedShown));
        OnPropertyChanged(nameof(IsSetInAdvanced));
        OnPropertyChanged(nameof(ShowsSimpleFaders));
        OnPropertyChanged(nameof(Low));
        OnPropertyChanged(nameof(Mid));
        OnPropertyChanged(nameof(High));
        OnPropertyChanged(nameof(LowText));
        OnPropertyChanged(nameof(MidText));
        OnPropertyChanged(nameof(HighText));
        OnPropertyChanged(nameof(MidHzText));
        OnPropertyChanged(nameof(IsMidMoved));
        foreach (RoomEqBand band in Bands)
            band.Refresh();
    }
}

/// <summary>One band of the advanced view's graphic EQ: an octave, centered on <see cref="Hz"/>.</summary>
public sealed partial class RoomEqBand(RoomEqViewModel owner, int index, double hz) : ObservableObject
{
    public double Hz => hz;

    /// <summary>Under the fader: 31, 63, 125 … 1k … 16k.</summary>
    public string Label => hz >= 1000 ? $"{hz / 1000:0.#}k" : $"{hz:0}";

    public double Value
    {
        get => owner.Eq.Bands[index];
        set => owner.SetBand(index, value);
    }

    public string ValueText => owner.Eq.Bands[index] is var db && db == 0 ? "0" : db.ToString("+0.#;−0.#", System.Globalization.CultureInfo.CurrentCulture);

    [RelayCommand]
    private void ToFlat() => owner.SetBand(index, 0);

    internal void Refresh()
    {
        OnPropertyChanged(nameof(Value));
        OnPropertyChanged(nameof(ValueText));
    }
}

/// <summary>A choice for a venue's own setting (#440): null is "as in Settings", its label naming the default.</summary>
public sealed partial class VenueChoice(object? value, string label = "") : ObservableObject
{
    /// <summary>A bool or a double, or null: as in Settings.</summary>
    public object? Value => value;

    [ObservableProperty]
    public partial string Label { get; set; } = label;
}
