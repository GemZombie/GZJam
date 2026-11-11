using GZJam.Audio;

namespace GZJam.App.ViewModels;

/// <summary>
/// The speakers come back by themselves (#338). While MAIN is on a fallback (the saved output
/// wasn't there at startup, or it was lost during the night, #353), GZJam looks for the saved one
/// every couple of seconds. When it turns up and nothing is playing to the room, MAIN switches to
/// it, with the headphones and booth as saved; MAIN never moves under a song. And the saved choice
/// survives the fallback: changing the headphones meanwhile doesn't make the fallback the saved
/// MAIN, only choosing it does.
/// </summary>
public sealed partial class MainWindowViewModel
{
    /// <summary>How often the saved speakers are looked for while MAIN is on a fallback.</summary>
    private const long SpeakersLookMs = 2000;

    /// <summary>After switching back, how long before trying again if they're lost at once (a card Windows still lists, but dead).</summary>
    private const long SpeakersRetryMs = 30_000;

    private long _nextSpeakersLook;
    private bool _speakersWaitSaid;   // "plugged in, switching when nothing is playing", said once each time they turn up

    /// <summary>What to do about the saved speakers, looked at every couple of seconds.</summary>
    internal enum SpeakersBack
    {
        /// <summary>MAIN is on them, none are saved, or they aren't plugged in.</summary>
        Nothing,
        /// <summary>Plugged in, but a song is playing to the room: wait, and say so.</summary>
        Wait,
        /// <summary>Plugged in, and the room is quiet: switch MAIN to them.</summary>
        Switch,
    }

    /// <param name="saved">The output saved as MAIN.</param>
    /// <param name="found">The saved output, if it's plugged in now.</param>
    /// <param name="current">The output MAIN is on.</param>
    /// <param name="onAir">A song is playing to the room.</param>
    internal static SpeakersBack DecideSpeakersBack(AudioDevicePreference? saved, AudioDevice? found, AudioDevice? current, bool onAir) =>
        saved is null || found is null || found.Index == current?.Index ? SpeakersBack.Nothing
        : onAir ? SpeakersBack.Wait
        : SpeakersBack.Switch;

    /// <summary>
    /// The saved MAIN while MAIN is on another output (a fallback): not plugged in, or waiting for
    /// the room to be quiet. Null when MAIN is on it, or nothing was saved.
    /// </summary>
    internal AudioDevicePreference? AwaitedMain
    {
        get
        {
            if (_settings.MainDevice is not { } saved || Audio is null)
                return null;
            AudioDevice? found = Audio.FindDevice(saved);
            return found is not null && found.Index == Mix?.Main.Device?.Index ? null : saved;
        }
    }

    /// <summary>
    /// Whether a change of outputs saves its MAIN: always when the DJ chose it, otherwise not
    /// while MAIN is on a fallback (choosing headphones meanwhile mustn't forget the real speakers).
    /// </summary>
    internal static bool SavesMain(bool mainChosen, AudioDevicePreference? awaited) => mainChosen || awaited is null;

    /// <summary>The outputs are being listed in the background: the next tick waits for it.</summary>
    private bool _lookingForSpeakers;

    /// <summary>
    /// Whether MAIN is on the saved speakers already, by name (and driver), from the output it has:
    /// then there's nothing to look for, and the outputs aren't listed at all (#367).
    /// </summary>
    internal static bool IsOnSaved(AudioDevicePreference? saved, AudioDevice? current) =>
        saved is null || current is not null && current.Name == saved.Name && (current.Driver == saved.Driver || saved.Driver.Length == 0);

    /// <summary>
    /// From the tick: switches MAIN back to the saved speakers once they're plugged in and the room
    /// is quiet. Only while MAIN is on another output; the outputs are listed in the background
    /// (on Linux that reads a file for each sound card), and only the answer comes back here.
    /// </summary>
    private async void LookForSavedSpeakers()
    {
        long now = Environment.TickCount64;
        if (now < _nextSpeakersLook || _lookingForSpeakers || Audio is not { } audio || Mix is not { IsOffline: false } mix)
            return;
        _nextSpeakersLook = now + SpeakersLookMs;
        if (now - _speakersReopenedAt < SpeakersRetryMs || IsOnSaved(_settings.MainDevice, mix.Main.Device))
            return;
        AudioDevicePreference? saved = _settings.MainDevice;
        AudioDevice? found;
        _lookingForSpeakers = true;
        try
        {
            found = await Task.Run(() => audio.FindDevice(saved));
        }
        catch (Exception e)
        {
            Log.Error("Speakers: couldn't list the outputs", e);
            return;
        }
        finally
        {
            _lookingForSpeakers = false;
        }
        if (saved != _settings.MainDevice || Mix is not { IsOffline: false })
            return;   // the DJ chose other speakers meanwhile
        now = Environment.TickCount64;
        switch (DecideSpeakersBack(_settings.MainDevice, found, mix.Main.Device, Decks.Any(d => d.IsOnAir)))
        {
            case SpeakersBack.Nothing:
                _speakersWaitSaid = false;
                return;
            case SpeakersBack.Wait:
                if (!_speakersWaitSaid)
                {
                    _speakersWaitSaid = true;
                    string waiting = $"{found!.Name} is plugged in. The speakers switch to it when nothing is playing.";
                    Log.Info(waiting);
                    ShowNotice(waiting);
                }
                return;
        }

        _speakersWaitSaid = false;
        // The headphones and booth as saved: a fallback may have turned the headphones off,
        // since they'd have played in the room. Saved on the speakers' own output, they're split.
        AudioDevice? cue = Audio.FindDevice(_settings.CueDevice);
        AudioDevice? booth = Audio.FindDevice(_settings.BoothDevice);
        Log.Info($"Speakers: the saved output is back, switching MAIN to {found!.Name}");
        _speakersReopenedAt = now;
        if (SetOutputs(found, cue, booth, remember: false, quiet: true))
            ShowNotice($"Speakers are back on {found.Name}.");
        Settings.RefreshDevices();
    }
}
