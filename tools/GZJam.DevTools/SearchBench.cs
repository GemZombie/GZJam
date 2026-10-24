using System.Diagnostics;
using GZJam.Library;
using Microsoft.Data.Sqlite;

namespace GZJam.DevTools;

/// <summary>
/// How long searches take on a real library, and on the same library made many times bigger (every
/// song, its analysis, marks and labels copied under new paths), for DJs with huge collections.
/// Works on a copy: the library given is only read.
/// </summary>
internal static class SearchBench
{
    private static readonly string[] Queries =
    [
        "", "a", "basie", "count basie", "bpm:130-150", "~bpm:140", "rating:yes", "rating:none", "-rating:no",
        "energy:high", "label:a", "played:yes", "played:never", "year:1935-1940",
    ];

    public static int Run(string libraryPath, int times)
    {
        string work = Directory.CreateTempSubdirectory("gzjam-search-").FullName;
        try
        {
            string copy = Path.Combine(work, "library.db");
            using (var source = new SqliteConnection($"Data Source={libraryPath};Mode=ReadOnly;Pooling=False"))
            using (var target = new SqliteConnection($"Data Source={copy};Pooling=False"))
            {
                source.Open();
                target.Open();
                source.BackupDatabase(target);
            }

            var db = new LibraryDatabase(copy);
            var small = Time(db);
            Multiply(copy, times);
            var big = Time(db);

            Console.WriteLine($"{"search",-18} {small.Count0,9:N0} songs        {big.Count0,9:N0} songs");
            foreach (string q in Queries)
            {
                var (s, b) = (small.Results[q], big.Results[q]);
                Console.WriteLine($"  {('"' + q + '"'),-16} {s.Ms,7:0} ms {s.Hits,7:N0}    {b.Ms,7:0} ms {b.Hits,7:N0}");
            }
            return 0;
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(work, recursive: true);
        }
    }

    private sealed record Timings(int Count0, Dictionary<string, (double Ms, int Hits)> Results);

    private static Timings Time(LibraryDatabase db)
    {
        var results = new Dictionary<string, (double, int)>();
        foreach (string q in Queries)
        {
            db.Search(q);   // warm
            const int runs = 3;
            var sw = Stopwatch.StartNew();
            int hits = 0;
            for (int i = 0; i < runs; i++)
                hits = db.Search(q).Count;
            results[q] = (sw.Elapsed.TotalMilliseconds / runs, hits);
        }
        return new Timings(db.GetTrackCount(), results);
    }

    /// <summary>Copies every song, and what's kept for it, under new paths until there are <paramref name="times"/> as many.</summary>
    private static void Multiply(string path, int times)
    {
        using var c = new SqliteConnection($"Data Source={path};Pooling=False");
        c.Open();
        using var tx = c.BeginTransaction();
        void Execute(string sql)
        {
            using var cmd = c.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }
        Execute("CREATE TEMP TABLE originals AS SELECT path FROM tracks;");
        for (int k = 1; k < times; k++)
        {
            string tag = $"/copy{k}";
            Execute($"""
                INSERT INTO tracks (root_id, path, folder, file_name, file_size, modified_utc, title, artist, album, album_artist,
                                    genre, comment, year, track_number, bpm, duration_ms, tag_version)
                SELECT root_id, path || '{tag}', 'copy{k}/' || folder, file_name, file_size, modified_utc, title, artist, album, album_artist,
                       genre, comment, year, track_number, bpm, duration_ms, tag_version
                FROM tracks WHERE path IN (SELECT path FROM originals);
                INSERT INTO analysis SELECT path || '{tag}', file_size, modified_utc, version, length_ms, start_ms, end_ms, bpm,
                       zeroblob(length(waveform)), lufs, peak_db
                FROM analysis WHERE path IN (SELECT path FROM originals);
                INSERT INTO marks SELECT path || '{tag}', rating, energy FROM marks WHERE path IN (SELECT path FROM originals);
                INSERT INTO track_labels SELECT path || '{tag}', label_id FROM track_labels WHERE path IN (SELECT path FROM originals);
                """);
        }
        tx.Commit();
    }
}
