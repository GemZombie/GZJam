using GZJam.Library;

namespace GZJam.App.ViewModels;

/// <summary>
/// What saving tag edits did (#129): how many songs, how many files were written, and which were
/// kept in the library only, and why. <see cref="Undo"/> puts every one back as it was.
/// </summary>
public sealed record TagEditOutcome(int Songs, int Written, IReadOnlyList<(Track Track, string Why)> KeptInLibrary, Func<Task> Undo)
{
    /// <summary>For the notice: "Saved 37 songs, in GZJam's library." / "Saved into 36 files; 1 kept in GZJam's library only: …".</summary>
    public string Message
    {
        get
        {
            string songs = Songs == 1 ? "1 song" : $"{Format.Count(Songs)} songs";
            if (Written == 0 && KeptInLibrary.Count == 0)
                return $"Saved {songs}, in GZJam's library.";
            if (KeptInLibrary.Count == 0)
                return Written == 1 ? "Saved into the song's file." : $"Saved into {Format.Count(Written)} files.";
            var (track, why) = KeptInLibrary[0];
            string first = $"\"{track.DisplayTitle}\": {why}";
            string kept = KeptInLibrary.Count == 1
                ? $"kept in GZJam's library only ({first})"
                : $"{Format.Count(KeptInLibrary.Count)} kept in GZJam's library only ({first}, and others)";
            return Written == 0
                ? $"Saved {songs}, {kept}."
                : $"Saved into {(Written == 1 ? "1 file" : $"{Format.Count(Written)} files")}; {kept}.";
        }
    }
}

public sealed partial class MainWindowViewModel
{
    /// <summary>The song's file is open in GZJam: on a deck or being previewed. Its tags aren't written then.</summary>
    internal bool IsPlayingFile(string path) =>
        Decks.Any(d => d.Track?.Path == path) || Preview?.Track?.Path == path;

    /// <summary>
    /// Saves edits to songs' tags (#129), by <see cref="SongFields"/> field (null takes a tag off):
    /// kept in the library, over what the files say; or, with <see cref="AppSettings.WriteTagsToFiles"/>,
    /// written into the files, except a song on a deck or a file that can't be written, kept in the
    /// library instead. Every copy of the songs on screen shows the edit. Songs outside the library
    /// are left out. Null if there was nothing to save.
    /// </summary>
    public async Task<TagEditOutcome?> EditTagsAsync(IReadOnlyList<Track> tracks, IReadOnlyDictionary<string, string?> changes)
    {
        var songs = tracks.Where(t => !t.IsOutsideLibrary).DistinctBy(t => t.Path).ToList();
        if (songs.Count == 0 || changes.Count == 0)
            return null;
        bool write = _settings.WriteTagsToFiles;
        var playing = songs.Where(t => IsPlayingFile(t.Path)).Select(t => t.Path).ToHashSet(StringComparer.Ordinal);

        var editsBefore = new Dictionary<string, IReadOnlyDictionary<string, string?>>(StringComparer.Ordinal);
        var fileBefore = new Dictionary<string, IReadOnlyDictionary<string, string?>>(StringComparer.Ordinal);
        var kept = new List<(Track Track, string Why)>();
        await Task.Run(() =>
        {
            var libraryOnly = new List<string>();
            foreach (Track song in songs)
            {
                editsBefore[song.Path] = _database.GetTagEdits(song.Path);
                if (!write)
                {
                    libraryOnly.Add(song.Path);
                    continue;
                }
                string? why;
                if (playing.Contains(song.Path))
                {
                    why = "it's on a deck";
                }
                else if (FileVersion.Of(song.Path) is not { } version || TagWriter.ReadFields(song.Path, changes.Keys) is not { } old)
                {
                    why = "the file isn't there";
                }
                else if ((why = TagWriter.Write(song.Path, changes)) is null)
                {
                    fileBefore[song.Path] = old;
                    _database.TagsWritten(song.Path, version);
                    continue;
                }
                kept.Add((song, why));
                libraryOnly.Add(song.Path);
            }
            _database.EditTags(libraryOnly, changes);
            if (fileBefore.Count > 0)
                _database.ClearTagEdits(fileBefore.Keys, changes.Keys);   // the files say it now; read them again
        });
        await ShowTagsChangedAsync(songs);

        string what = songs.Count == 1 ? $"\"{songs[0].DisplayTitle}\"" : $"{songs.Count} songs";
        string fields = string.Join(", ", changes.Keys.Select(SongFields.Describe));
        Log.Info($"Tags edited ({fields}) for {what}: " + (write
            ? $"{fileBefore.Count} written to the files, {kept.Count} kept in the library" + string.Concat(kept.Select(k => $"; {k.Track.Path}: {k.Why}"))
            : "kept in the library"));

        bool undone = false;
        async Task Undo()
        {
            if (undone)
                return;
            undone = true;
            var stillPlaying = fileBefore.Keys.Where(IsPlayingFile).ToHashSet(StringComparer.Ordinal);
            var notPutBack = new List<string>();
            await Task.Run(() =>
            {
                foreach (var (path, old) in fileBefore)
                {
                    FileVersion? version = FileVersion.Of(path);
                    if (stillPlaying.Contains(path) || version is null || TagWriter.Write(path, old) is not null)
                    {
                        notPutBack.Add(path);
                        continue;
                    }
                    _database.TagsWritten(path, version.Value);
                }
                foreach (var (path, edits) in editsBefore)
                {
                    // A file that can't be put back as it was keeps its old values as edits, so it shows them.
                    var restored = notPutBack.Contains(path)
                        ? edits.Concat(fileBefore[path].Where(f => !edits.Keys.Any(k => SongFields.Same(k, f.Key))))
                            .ToDictionary(StringComparer.Ordinal)
                        : edits;
                    _database.SetTagEdits(path, restored);
                }
            });
            await ShowTagsChangedAsync(songs);
            Log.Info($"Tag edits undone for {what}" + (notPutBack.Count > 0 ? $"; {notPutBack.Count} files kept as they were, their old tags in the library" : ""));
        }
        return new TagEditOutcome(songs.Count, fileBefore.Count, kept, Undo);
    }

    /// <summary>
    /// Forgets the library's edits to songs' tags (#129): they show what their files say again.
    /// Returns how to undo it.
    /// </summary>
    public async Task<Func<Task>> UseFileTagsAsync(IReadOnlyList<Track> tracks)
    {
        var songs = tracks.Where(t => !t.IsOutsideLibrary).DistinctBy(t => t.Path).ToList();
        var before = new Dictionary<string, IReadOnlyDictionary<string, string?>>(StringComparer.Ordinal);
        await Task.Run(() =>
        {
            foreach (Track song in songs)
                before[song.Path] = _database.GetTagEdits(song.Path);
            _database.ClearTagEdits(before.Where(b => b.Value.Count > 0).Select(b => b.Key));
        });
        await ShowTagsChangedAsync(songs);
        Log.Info($"The files' own tags again for {Describe(songs, [.. before.Keys])}");
        bool undone = false;
        return async () =>
        {
            if (undone)
                return;
            undone = true;
            await Task.Run(() =>
            {
                foreach (var (path, edits) in before.Where(b => b.Value.Count > 0))
                    _database.SetTagEdits(path, edits);
            });
            await ShowTagsChangedAsync(songs);
        };
    }

    /// <summary>The songs' tags changed in the library: every copy on screen (the list, the queue, the decks, History) shows them.</summary>
    private async Task ShowTagsChangedAsync(IReadOnlyList<Track> songs)
    {
        var fresh = await Task.Run(() => songs.Select(s => _database.FindTrack(s.Path)).ToList());
        for (int i = 0; i < songs.Count; i++)
        {
            if (fresh[i] is not { } read)
                continue;
            string path = songs[i].Path;
            var copies = TracksFor(path)
                .Concat(History.Plays.Select(p => p.Track).Where(t => t.Path == path))
                .Append(songs[i])
                .Distinct();
            foreach (Track copy in copies)
                copy.CopyTagsFrom(read);
        }
        Library.TagsEdited();
    }
}
