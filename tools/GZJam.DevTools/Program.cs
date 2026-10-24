using System.Diagnostics;
using GZJam.DevTools;
using GZJam.Library;

// Developer utilities. Read-only: music files are never modified.
//   bench <music folder>             Scan a folder into a throwaway database; report timings and tag coverage.
//   analyze <music folder> [count]   Analyze tracks (waveform, silence, BPM): speed, and detected BPM
//                                    against the tagged BPMs.
//   art <music folder> [count]       How many songs have album art (in the file, in the folder), and
//                                    how long finding it and making the small copies takes.
//   search <library.db> [times]     How long searches take on a copy of a library, and on the copy made
//                                    that many times bigger (default 10), for very large collections.
//   clock [seconds]                  How evenly a playing deck reports its position, and how
//                                    smoothly the close-up's playhead follows (on the default output, silent).
//   icon <repo folder>               Draw the app icon (the .ico, .png and .icns files) and the logo's
//                                    SVG files in branding/.

if (args is ["swing", var swingBpm, var swingBackbeat])
{
    string file = Path.Combine(Path.GetTempPath(), $"gzjam-swing-{swingBpm}-{swingBackbeat}.wav");
    GZJam.Testing.TestWav.WriteSwingBeat(file, double.Parse(swingBpm), TimeSpan.FromSeconds(40), backbeat: double.Parse(swingBackbeat));
    return AnalyzeBench.Tempo(file);
}

if (args is ["icon", var repoFolder])
    return IconArt.Generate(repoFolder);

if (args is ["tempo", var tempoFile])
    return AnalyzeBench.Tempo(tempoFile);

if (args is ["envelope", var envelopeFile])
    return AnalyzeBench.Envelope(envelopeFile);

if (args is ["clock", ..])
    return ClockBench.Run(args.Length > 1 ? double.Parse(args[1]) : 10);

if (args is ["art", var artFolder, ..])
    return ArtBench.Run(artFolder, args.Length > 2 ? int.Parse(args[2]) : 400);

if (args is ["search", var searchLibrary, ..])
    return SearchBench.Run(searchLibrary, args.Length > 2 ? int.Parse(args[2]) : 10);

if (args is ["analyze", var analyzeFolder, ..])
    return await AnalyzeBench.RunAsync(analyzeFolder, args.Length > 2 ? int.Parse(args[2]) : 60);

if (args is not ["bench", var musicFolder])
{
    Console.WriteLine("usage: GZJam.DevTools bench <music folder>");
    Console.WriteLine("       GZJam.DevTools analyze <music folder> [count]");
    Console.WriteLine("       GZJam.DevTools art <music folder> [count]");
    Console.WriteLine("       GZJam.DevTools search <library.db> [times]");
    Console.WriteLine("       GZJam.DevTools clock [seconds]");
    return 1;
}

string work = Directory.CreateTempSubdirectory("gzjam-bench-").FullName;
try
{
    var db = new LibraryDatabase(Path.Combine(work, "library.db"));
    var scanner = new LibraryScanner(db);
    LibraryRoot root = db.AddRoot(musicFolder);

    var lastReport = Stopwatch.StartNew();
    var progress = new Progress<ScanProgress>(p =>
    {
        if (lastReport.ElapsedMilliseconds < 1000) return;
        lastReport.Restart();
        Console.WriteLine(p.Enumerating
            ? $"  finding files... {p.FilesFound:N0}"
            : $"  reading tags... {p.FilesRead:N0} / {p.FilesToRead:N0}");
    });

    Console.WriteLine($"First scan of {musicFolder}");
    ScanResult first = await scanner.ScanAsync(root, progress);
    Console.WriteLine($"  {first.Added:N0} tracks in {first.Elapsed.TotalSeconds:0.0}s");

    ScanResult second = await scanner.ScanAsync(root);
    Console.WriteLine($"Rescan (nothing changed): {second.Elapsed.TotalSeconds:0.00}s, {second.Unchanged:N0} unchanged");

    var all = db.Search(null);
    int n = all.Count;
    string Pct(int count) => $"{count,6:N0}  ({100.0 * count / Math.Max(1, n):0}%)";
    Console.WriteLine();
    Console.WriteLine("Tag coverage");
    Console.WriteLine($"  title     {Pct(all.Count(t => t.Title is not null))}");
    Console.WriteLine($"  artist    {Pct(all.Count(t => t.Artist is not null))}");
    Console.WriteLine($"  album     {Pct(all.Count(t => t.Album is not null))}");
    Console.WriteLine($"  genre     {Pct(all.Count(t => t.Genre is not null))}");
    Console.WriteLine($"  year      {Pct(all.Count(t => t.Year is not null))}");
    Console.WriteLine($"  bpm       {Pct(all.Count(t => t.TagBpm is not null))}");
    Console.WriteLine($"  comment   {Pct(all.Count(t => t.Comment is not null))}");
    Console.WriteLine($"  duration  {Pct(all.Count(t => t.Duration > TimeSpan.Zero))}");

    Console.WriteLine();
    Console.WriteLine("Top genres");
    foreach (var g in all.Where(t => t.Genre is not null).GroupBy(t => t.Genre!).OrderByDescending(g => g.Count()).Take(12))
        Console.WriteLine($"  {g.Count(),6:N0}  {g.Key}");

    var bpms = all.Where(t => t.TagBpm is not null).Select(t => t.TagBpm!.Value).Order().ToList();
    if (bpms.Count > 0)
        Console.WriteLine($"\nBPM range: {bpms[0]:0} - {bpms[^1]:0}, median {bpms[bpms.Count / 2]:0}");

    Console.WriteLine("\nMost common comments");
    foreach (var g in all.Where(t => t.Comment is not null).GroupBy(t => t.Comment!).OrderByDescending(g => g.Count()).Take(10))
        Console.WriteLine($"  {g.Count(),6:N0}  {Shorten(g.Key)}");
    var bpmComments = all.Where(t => t.Comment is not null && t.Comment.Contains("bpm", StringComparison.OrdinalIgnoreCase)).ToList();
    Console.WriteLine($"Comments mentioning BPM: {bpmComments.Count:N0}");
    foreach (var c in bpmComments.Select(t => t.Comment!).Distinct().Take(12))
        Console.WriteLine($"          {Shorten(c)}");
    Console.WriteLine("Sample of unique comments");
    foreach (var t in all.Where(t => t.Comment is not null).GroupBy(t => t.Comment!).Where(g => g.Count() == 1).Select(g => g.First()).Take(10))
        Console.WriteLine($"          {Shorten(t.Comment!)}");

    var tree = FolderTree.Build(db.GetRoots(), db.GetFolderCounts());
    var sw = Stopwatch.StartNew();
    for (int i = 0; i < 10; i++)
        FolderTree.Build(db.GetRoots(), db.GetFolderCounts());
    Console.WriteLine($"\nFolder tree: {CountNodes(tree[0]):N0} folders, built in {sw.Elapsed.TotalMilliseconds / 10:0.0} ms");

    Console.WriteLine("\nSearch timings (whole library)");
    foreach (string q in new[] { "", "b", "basie", "count basie", "jump", "lindy", "o'clock", "sweet georgia", "zzzz" })
    {
        db.Search(q); // warm
        sw.Restart();
        const int runs = 5;
        int hits = 0;
        for (int i = 0; i < runs; i++)
            hits = db.Search(q).Count;
        Console.WriteLine($"  {('"' + q + '"'),-16} {hits,6:N0} hits  {sw.Elapsed.TotalMilliseconds / runs,7:0.0} ms");
    }

    FolderNode biggest = tree[0].Children.OrderByDescending(c => c.TrackCount).First();
    FolderNode deep = biggest.Children.OrderByDescending(c => c.TrackCount).FirstOrDefault() ?? biggest;
    sw.Restart();
    int scoped = db.Search("a", deep.Scope).Count;
    Console.WriteLine($"  \"a\" in {deep.RelativePath}: {scoped:N0} hits  {sw.Elapsed.TotalMilliseconds:0.0} ms");
    return 0;
}
finally
{
    Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    Directory.Delete(work, recursive: true);
}

static int CountNodes(FolderNode node) => 1 + node.Children.Sum(CountNodes);

static string Shorten(string s)
{
    s = s.ReplaceLineEndings(" ");
    return s.Length > 90 ? s[..90] + "…" : s;
}
