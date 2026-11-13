using GZJam.Library;

namespace GZJam.App.ViewModels;

/// <summary>Where a song opened from the file manager went (#336).</summary>
public enum OpenedTo
{
    /// <summary>Nowhere: it couldn't play, or there was nothing to open.</summary>
    Nothing,

    /// <summary>On a deck, playing: the window comes forward.</summary>
    Deck,

    /// <summary>In the headphones, the room hearing no change: the window stays where it is.</summary>
    Headphones,
}

/// <summary>
/// GZJam as the player for music files (#336): a song double-clicked in Explorer, Finder or a
/// Linux file manager plays here. With nothing playing to the room it plays on a deck, the one a
/// double-click in the library picks (#260); with a song on air, in the headphones only, so a
/// double-click never touches the room. A song outside My music plays as it is, without joining
/// the library.
/// </summary>
public sealed partial class MainWindowViewModel
{
    public async Task<OpenedTo> OpenFromFileManagerAsync(IReadOnlyList<string> files)
    {
        // Last night's decks and queue first: their restore skips a deck that has a song, so one
        // loaded before it would lose what was there.
        await _nightBack;
        if (files.Count == 0)
            return OpenedTo.Nothing;
        string path = files[0];
        string name = Path.GetFileName(path);
        string others = files.Count switch
        {
            1 => "",
            2 => " The other song opened with it was left: one at a time.",
            _ => $" The other {files.Count - 1} songs opened with it were left: one at a time.",
        };
        if (!File.Exists(path))
        {
            ShowNotice($"{name} isn't there any more: moved or deleted?", seconds: 6);
            return OpenedTo.Nothing;
        }
        if (!LibraryScanner.IsAudioFile(path))
        {
            ShowNotice($"GZJam can't play {name}: it isn't a kind of song file it knows.", seconds: 6);
            return OpenedTo.Nothing;
        }
        Track track = await Task.Run(() => _database.TrackForFile(path));

        if (Decks.FirstOrDefault(d => d.IsOnAir) is { } onAir)
        {
            // The room is hearing a song: this one only in the headphones.
            if (!HasCueOutput)
            {
                ShowNotice($"Deck {onAir.Label} is on air, so {track.DisplayTitle} would go to the headphones: choose a headphone output in Settings.", seconds: 8);
                return OpenedTo.Nothing;
            }
            Preview?.PlayFromStart(track);
            Log.Info($"Opened from the file manager, in the headphones (Deck {onAir.Label} on air): {path}");
            ShowNotice($"In the headphones: {track.DisplayTitle}. Deck {onAir.Label} is on air, so the room hears no change.{others}", seconds: 8);
            return OpenedTo.Headphones;
        }

        if (await LoadForPlayAsync(track) is not { } deck)
            return OpenedTo.Nothing;
        if (!deck.IsPlaying)
            deck.TogglePlayCommand.Execute(null);
        Log.Info($"Opened from the file manager, playing on Deck {deck.Label}: {path}");
        if (others.Length > 0)
            ShowNotice($"Playing {track.DisplayTitle} on Deck {deck.Label}.{others}", seconds: 8);
        return OpenedTo.Deck;
    }
}
