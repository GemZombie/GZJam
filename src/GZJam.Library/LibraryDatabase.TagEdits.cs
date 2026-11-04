using Microsoft.Data.Sqlite;

namespace GZJam.Library;

/// <summary>
/// The DJ's edits to songs' tags (#129), kept in the library over what the files say. The index
/// shows them at once, and every scan puts them back over the tags it reads, until the DJ clears
/// them: so an edit lasts even if the file's tags change later. Never written to the files here;
/// writing them, when the DJ chooses to, is <see cref="TagWriter"/>'s.
/// </summary>
public sealed partial class LibraryDatabase
{
    /// <summary>A song's edits, by <see cref="SongFields"/> field; a null value takes the tag off.</summary>
    public IReadOnlyDictionary<string, string?> GetTagEdits(string path)
    {
        using var c = Open();
        using var edits = EditsReader.For(c);
        return edits?.Read(path) ?? new Dictionary<string, string?>();
    }

    /// <summary>Which fields of these songs are edited in the library, by path: for a song's info to mark them.</summary>
    public ILookup<string, string> GetTagEditFields(IEnumerable<string> paths)
    {
        var wanted = paths.ToHashSet(StringComparer.Ordinal);
        var found = new List<(string Path, string Field)>();
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT path, field FROM tag_edits;";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            if (wanted.Contains(r.GetString(0)))
                found.Add((r.GetString(0), r.GetString(1)));
        }
        return found.ToLookup(f => f.Path, f => f.Field, StringComparer.Ordinal);
    }

    /// <summary>
    /// Keeps edits to songs' tags in the library, over what their files say, and shows them in the
    /// index at once (searched, in the columns). Only the fields in <paramref name="changes"/>
    /// change; the songs' other edits stay.
    /// </summary>
    public void EditTags(IEnumerable<string> paths, IReadOnlyDictionary<string, string?> changes)
    {
        if (changes.Count == 0)
            return;
        var tracks = paths.Distinct(StringComparer.Ordinal).Select(p => (Path: p, Track: FindTrack(p))).ToList();
        using var c = Open();
        using var tx = c.BeginTransaction();
        using var edits = EditsReader.For(c);
        foreach (var (path, track) in tracks)
        {
            var kept = new Dictionary<string, string?>(edits?.Read(path) ?? new Dictionary<string, string?>(), StringComparer.Ordinal);
            foreach (var (field, value) in changes)
            {
                foreach (string same in kept.Keys.Where(k => SongFields.Same(k, field)).ToList())
                    kept.Remove(same);
                kept[field] = value;
            }
            WriteEdits(c, path, kept);
            if (track is not null)
            {
                track.ApplyEdits(changes);
                UpdateTags(c, track);
            }
        }
        tx.Commit();
        ForgetTagNames();
    }

    /// <summary>
    /// Puts a song's edits back as they were (for Undo): exactly <paramref name="edits"/>, none if
    /// empty. The song's tags in the index are read from its file again, with them over it.
    /// </summary>
    public void SetTagEdits(string path, IReadOnlyDictionary<string, string?> edits)
    {
        using (var c = Open())
        using (var tx = c.BeginTransaction())
        {
            WriteEdits(c, path, edits);
            tx.Commit();
        }
        ReadTagsAgain([path]);
    }

    /// <summary>
    /// Forgets edits: the songs show what their files say again (read from them now). With
    /// <paramref name="fields"/>, only those; else all of the songs' edits.
    /// </summary>
    public void ClearTagEdits(IEnumerable<string> paths, IEnumerable<string>? fields = null)
    {
        var list = paths.Distinct(StringComparer.Ordinal).ToList();
        var only = fields?.ToList();
        using (var c = Open())
        using (var tx = c.BeginTransaction())
        using (var edits = EditsReader.For(c))
        {
            foreach (string path in list)
            {
                var kept = only is null ? new Dictionary<string, string?>() : (edits?.Read(path) ?? new Dictionary<string, string?>())
                    .Where(e => !only.Any(f => SongFields.Same(f, e.Key)))
                    .ToDictionary(StringComparer.Ordinal);
                WriteEdits(c, path, kept);
            }
            tx.Commit();
        }
        ReadTagsAgain(list);
    }

    /// <summary>
    /// Reads songs' files again into the index (after GZJam wrote their tags, or an edit was
    /// cleared), with the DJ's edits over what they say. A file that isn't there or can't be
    /// opened now is left as the index has it.
    /// </summary>
    public void ReadTagsAgain(IEnumerable<string> paths)
    {
        var read = new List<Track>();
        foreach (string path in paths.Distinct(StringComparer.Ordinal))
        {
            if (RootOf(path) is not var (rootId, rootPath) || !CanOpen(path))
                continue;
            read.Add(TagReader.Read(rootId, rootPath, new FileInfo(path)));
        }
        if (read.Count > 0)
            UpsertTracks(read);
    }

    /// <summary>
    /// GZJam wrote a file's tags: its audio is as it was, so its analysis (waveform, BPM, loudness)
    /// still describes it, and is kept rather than done again for the file's new size and date.
    /// </summary>
    public void TagsWritten(string path, FileVersion before)
    {
        var file = new FileInfo(path);
        if (!file.Exists)
            return;
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            UPDATE analysis SET file_size = $size, modified_utc = $mod
            WHERE path = $path AND file_size = $old_size AND modified_utc = $old_mod;
            """;
        cmd.Parameters.AddWithValue("$path", path);
        cmd.Parameters.AddWithValue("$size", file.Length);
        cmd.Parameters.AddWithValue("$mod", file.LastWriteTimeUtc.Ticks);
        cmd.Parameters.AddWithValue("$old_size", before.Size);
        cmd.Parameters.AddWithValue("$old_mod", before.ModifiedTicks);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Puts a song's kept edits over its tags in the index (a song moved, and its edits with it).</summary>
    private void ApplyKeptEdits(SqliteConnection c, EditsReader edits, string path)
    {
        var kept = edits.Read(path);
        if (kept.Count == 0 || FindTrack(path) is not { } track)
            return;
        track.ApplyEdits(kept);
        UpdateTags(c, track);
    }

    private (long RootId, string RootPath)? RootOf(string path)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT t.root_id, r.path FROM tracks t JOIN roots r ON r.id = t.root_id WHERE t.path = $path;";
        cmd.Parameters.AddWithValue("$path", path);
        using var r = cmd.ExecuteReader();
        return r.Read() ? (r.GetInt64(0), r.GetString(1)) : null;
    }

    /// <summary>The file can be read now: read while locked or gone, a song's tags would be lost from the index.</summary>
    private static bool CanOpen(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void WriteEdits(SqliteConnection c, string path, IReadOnlyDictionary<string, string?> edits)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM tag_edits WHERE path = $path;";
        cmd.Parameters.AddWithValue("$path", path);
        cmd.ExecuteNonQuery();
        cmd.CommandText = "INSERT INTO tag_edits (path, field, value) VALUES ($path, $field, $value);";
        var field = cmd.Parameters.Add("$field", SqliteType.Text);
        var value = cmd.Parameters.Add("$value", SqliteType.Text);
        foreach (var (name, text) in edits)
        {
            field.Value = name;
            value.Value = (object?)text ?? DBNull.Value;
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>A song's tags in the index, as edited: the rest of its row stays.</summary>
    private static void UpdateTags(SqliteConnection c, Track t)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            UPDATE tracks SET title = $title, artist = $artist, album = $album, album_artist = $album_artist, genre = $genre,
                comment = $comment, grouping = $grouping, year = $year, track_number = $track, bpm = $bpm, tags = $tags, tag_text = $tag_text
            WHERE path = $path;
            """;
        cmd.Parameters.AddWithValue("$path", t.Path);
        cmd.Parameters.AddWithValue("$title", Db(t.Title));
        cmd.Parameters.AddWithValue("$artist", Db(t.Artist));
        cmd.Parameters.AddWithValue("$album", Db(t.Album));
        cmd.Parameters.AddWithValue("$album_artist", Db(t.AlbumArtist));
        cmd.Parameters.AddWithValue("$genre", Db(t.Genre));
        cmd.Parameters.AddWithValue("$comment", Db(t.Comment));
        cmd.Parameters.AddWithValue("$grouping", Db(t.Grouping));
        cmd.Parameters.AddWithValue("$year", (object?)t.Year ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$track", (object?)t.TrackNumber ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$bpm", (object?)t.TagBpm ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$tags", Db(SongTags.Encode(t.Tags)));
        cmd.Parameters.AddWithValue("$tag_text", Db(SongTags.SearchText(t.Tags)));
        cmd.ExecuteNonQuery();
    }

    /// <summary>Reads songs' edits on one connection, quickly, as a scan writes each song.</summary>
    private sealed class EditsReader : IDisposable
    {
        private readonly SqliteCommand _cmd;
        private readonly SqliteParameter _path;

        private EditsReader(SqliteConnection c)
        {
            _cmd = c.CreateCommand();
            _cmd.CommandText = "SELECT field, value FROM tag_edits WHERE path = $path;";
            _path = _cmd.Parameters.Add("$path", SqliteType.Text);
        }

        /// <summary>A reader, or null when no song has edits (as in most libraries): nothing to look up.</summary>
        public static EditsReader? For(SqliteConnection c) =>
            Scalar(c, "SELECT EXISTS (SELECT 1 FROM tag_edits);") is long and not 0 ? new EditsReader(c) : null;

        public Dictionary<string, string?> Read(string path)
        {
            _path.Value = path;
            using var r = _cmd.ExecuteReader();
            var edits = new Dictionary<string, string?>(StringComparer.Ordinal);
            while (r.Read())
                edits[r.GetString(0)] = r.IsDBNull(1) ? null : r.GetString(1);
            return edits;
        }

        public void Dispose() => _cmd.Dispose();
    }
}
