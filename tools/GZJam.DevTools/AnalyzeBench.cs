using System.Collections.Concurrent;
using System.Diagnostics;
using GZJam.Audio;
using GZJam.Library;

namespace GZJam.DevTools;

/// <summary>
/// Runs the track analyzer over a sample of a real library: how long it takes, how much silence
/// it trims, and how detected BPMs compare with the BPMs some tracks have in their tags.
/// </summary>
internal static class AnalyzeBench
{
    public static async Task<int> RunAsync(string musicFolder, int sampleSize)
    {
        string work = Directory.CreateTempSubdirectory("gzjam-analyze-").FullName;
        try
        {
            var db = new LibraryDatabase(Path.Combine(work, "library.db"));
            await new LibraryScanner(db).ScanAsync(db.AddRoot(musicFolder));
            var all = db.Search(null);

            // Every tagged track (to check BPM), plus a random sample (for speed and trimming).
            var tagged = all.Where(t => t.TagBpm is not null).ToList();
            var sample = all.Where(t => t.TagBpm is null).OrderBy(_ => Random.Shared.Next()).Take(sampleSize).ToList();
            Console.WriteLine($"{all.Count:N0} tracks; analyzing {tagged.Count} with BPM tags and {sample.Count} others");

            using var engine = new AudioEngine();
            var analyzer = new TrackAnalyzer(engine);
            var results = new ConcurrentBag<(Track Track, TrackAnalysis? Result, TimeSpan Took, string? Error)>();
            int threads = Math.Clamp(Environment.ProcessorCount / 2, 1, 4);
            var total = Stopwatch.StartNew();
            Parallel.ForEach(tagged.Concat(sample), new ParallelOptions { MaxDegreeOfParallelism = threads }, track =>
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    results.Add((track, analyzer.Analyze(track.Path), sw.Elapsed, null));
                }
                catch (AudioException ex)
                {
                    results.Add((track, null, sw.Elapsed, ex.Message));
                }
            });
            total.Stop();

            var ok = results.Where(r => r.Result is not null).ToList();
            double audioMinutes = ok.Sum(r => r.Result!.Length.TotalMinutes);
            Console.WriteLine($"\n{results.Count} tracks in {total.Elapsed.TotalSeconds:0.0} s on {threads} threads " +
                $"({total.Elapsed.TotalSeconds / results.Count:0.00} s per track; {audioMinutes * 60 / total.Elapsed.TotalSeconds / threads:0}x real time per thread)");
            Console.WriteLine($"Per track: median {Median(ok.Select(r => r.Took.TotalSeconds)):0.00} s, slowest {ok.Max(r => r.Took.TotalSeconds):0.00} s");
            Console.WriteLine($"Estimate for the whole library: {TimeSpan.FromSeconds(total.Elapsed.TotalSeconds / results.Count * all.Count):h\\:mm}");
            ReportLoudness(ok.Select(r => r.Result!).ToList());
            foreach (var r in results.Where(r => r.Error is not null))
                Console.WriteLine($"  FAILED {r.Track.Path}: {r.Error}");

            Console.WriteLine("\nSilence trimmed");
            var heads = ok.Select(r => r.Result!.AudibleStart.TotalSeconds).ToList();
            var tails = ok.Select(r => (r.Result!.Length - r.Result.AudibleEnd).TotalSeconds).ToList();
            Console.WriteLine($"  start: median {Median(heads):0.00} s, max {heads.Max():0.00} s, {heads.Count(h => h > 0.5)} tracks over 0.5 s");
            Console.WriteLine($"  end:   median {Median(tails):0.00} s, max {tails.Max():0.00} s, {tails.Count(t => t > 1)} tracks over 1 s");
            foreach (var r in ok.OrderByDescending(r => (r.Result!.Length - r.Result.AudibleEnd) + r.Result.AudibleStart).Take(8))
                Console.WriteLine($"    {r.Result!.AudibleStart.TotalSeconds,5:0.00} s / {(r.Result.Length - r.Result.AudibleEnd).TotalSeconds,5:0.00} s  {Name(r.Track)}");

            Console.WriteLine("\nBPM: tag vs detected");
            int close = 0, octave = 0;
            foreach (var r in ok.Where(r => r.Track.TagBpm is not null).OrderBy(r => r.Track.TagBpm))
            {
                double tag = r.Track.TagBpm!.Value;
                double? found = r.Result!.Bpm;
                string verdict = found is not { } f ? "none"
                    : Math.Abs(f - tag) <= tag * 0.04 ? "ok"
                    : Math.Abs(f * 2 - tag) <= tag * 0.04 || Math.Abs(f / 2 - tag) <= tag * 0.04 ? "octave"
                    : "wrong";
                if (verdict == "ok") close++;
                if (verdict == "octave") octave++;
                string others = string.Join("  ", r.Result.TempoCandidates.Take(5).Select(c => $"{c.Bpm:0.0}:{c.Score:0.00}"));
                Console.WriteLine($"  {tag,6:0.0} {found,7:0.0}  {verdict,-7} {Name(r.Track),-60}  {others}");
            }
            Console.WriteLine($"  within 4%: {close} of {tagged.Count}; double/half: {octave}");

            var detected = ok.Where(r => r.Track.TagBpm is null && r.Result!.Bpm is not null).Select(r => r.Result!.Bpm!.Value).Order().ToList();
            Console.WriteLine($"\nUntagged sample: BPM found for {detected.Count} of {sample.Count}");
            foreach (var bucket in detected.GroupBy(b => (int)(b / 20) * 20).OrderBy(g => g.Key))
                Console.WriteLine($"  {bucket.Key,3}-{bucket.Key + 19,-3} {new string('#', bucket.Count())}");
            foreach (var r in ok.Where(r => r.Track.TagBpm is null).OrderBy(_ => Random.Shared.Next()).Take(15))
                Console.WriteLine($"  {r.Result!.Bpm,6:0.0}  {Name(r.Track)}");
            return 0;
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(work, recursive: true);
        }
    }

    /// <summary>Prints the loudness of a file's last seconds next to where the analyzer says the music ends.</summary>
    public static int Envelope(string path, double seconds = 12)
    {
        using var engine = new AudioEngine();
        var result = new TrackAnalyzer(engine).Analyze(path, detectBpm: false);
        int stream = ManagedBass.Bass.CreateStream(path, 0, 0, ManagedBass.BassFlags.Decode | ManagedBass.BassFlags.Float | ManagedBass.BassFlags.Prescan);
        var info = ManagedBass.Bass.ChannelGetInfo(stream);
        double from = Math.Max(0, result.Length.TotalSeconds - seconds);
        ManagedBass.Bass.ChannelSetPosition(stream, ManagedBass.Bass.ChannelSeconds2Bytes(stream, from));
        int step = info.Frequency / 4 * info.Channels;   // 250 ms
        var buffer = new float[step];
        double t = from;
        Console.WriteLine($"{Path.GetFileName(path)}: length {result.Length.TotalSeconds:0.00} s, audible {result.AudibleStart.TotalSeconds:0.00}-{result.AudibleEnd.TotalSeconds:0.00} s");
        while (ManagedBass.Bass.ChannelGetData(stream, buffer, buffer.Length * 4 | (int)ManagedBass.DataFlags.Float) is > 0 and var bytes)
        {
            int n = bytes / 4;
            double rms = Math.Sqrt(buffer.Take(n).Sum(x => (double)x * x) / n);
            double db = 20 * Math.Log10(Math.Max(rms, 1e-7));
            string bar = new string('#', (int)Math.Max(0, (db + 80) / 2));
            Console.WriteLine($"  {t,7:0.00} {db,6:0.0} dB {(t + 0.25 <= result.AudibleEnd.TotalSeconds ? "|" : " ")} {bar}");
            t += 0.25;
        }
        ManagedBass.Bass.StreamFree(stream);
        return 0;
    }

    /// <summary>Tempo candidates for each third of a file, to see whether the band speeds up.</summary>
    public static int Tempo(string path)
    {
        using var engine = new AudioEngine();
        engine.UseNoSoundDevice();
        int stream = ManagedBass.Bass.CreateStream(path, 0, 0, ManagedBass.BassFlags.Decode | ManagedBass.BassFlags.Float | ManagedBass.BassFlags.Prescan);
        var info = ManagedBass.Bass.ChannelGetInfo(stream);
        var detector = new TempoDetector(info.Frequency);
        var buffer = new float[8192 * info.Channels];
        while (ManagedBass.Bass.ChannelGetData(stream, buffer, buffer.Length * 4 | (int)ManagedBass.DataFlags.Float) is > 0 and var bytes)
        {
            for (int i = 0; i < bytes / 4; i += info.Channels)
            {
                float m = 0;
                for (int c = 0; c < info.Channels; c++)
                    m += buffer[i + c];
                detector.Push(m / info.Channels);
            }
        }
        ManagedBass.Bass.StreamFree(stream);
        double length = detector.Onsets.Count / detector.FrameRate;
        Console.WriteLine($"{Path.GetFileName(path)} ({length:0} s, {info.Frequency} Hz)");
        for (int part = 0; part < 4; part++)
        {
            var (from, to) = part == 3 ? (0.0, length) : (length * part / 3, length * (part + 1) / 3);
            var c = detector.Estimate(TimeSpan.FromSeconds(from), TimeSpan.FromSeconds(to), TrackAnalyzer.MinBpm, TrackAnalyzer.MaxBpm);
            Console.WriteLine($"  {(part == 3 ? "whole" : $"{from,4:0}-{to,4:0}s")}: {string.Join("  ", c.Take(5).Select(x => $"{x.Bpm:0.0}:{x.Score:0.00}"))}");
            if (part == 3)
            {
                var ac = detector.Autocorrelation(600);
                foreach (var x in c.Take(3))
                {
                    double p = 60 * detector.FrameRate / x.Bpm;
                    double A(double lag) => TempoDetector.Interpolate(ac, lag);
                    Console.WriteLine($"    {x.Bpm,6:0.0}: P {A(p):0.00}  P/2 {A(p / 2):0.00}  P/3 {A(p / 3):0.00}  2P/3 {A(2 * p / 3):0.00}  P/4 {A(p / 4):0.00}  2P {A(2 * p):0.00}");
                }
            }
        }
        return 0;
    }

    private static string Name(Track t) => $"{t.DisplayTitle} - {t.Artist} [{t.Folder.Split('/').ElementAtOrDefault(1)}]";

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToList();
        return sorted.Count == 0 ? 0 : sorted[sorted.Count / 2];
    }

    /// <summary>How loud the library is, and what loudness matching would do to it.</summary>
    private static void ReportLoudness(IReadOnlyList<TrackAnalysis> results)
    {
        var measured = results.Where(r => r.Lufs is not null).ToList();
        if (measured.Count == 0)
            return;
        static string Pcts(IEnumerable<double> values)
        {
            var sorted = values.Order().ToList();
            double P(double p) => sorted[(int)Math.Round(p * (sorted.Count - 1))];
            return $"min {sorted[0],6:0.0}  10% {P(0.1),6:0.0}  median {P(0.5),6:0.0}  90% {P(0.9),6:0.0}  max {sorted[^1],6:0.0}";
        }
        double Gain(TrackAnalysis r) => LoudnessMatch.GainDb(r.Lufs!.Value, r.PeakDb);
        var gains = measured.Select(Gain).ToList();
        int peakLimited = measured.Count(r => LoudnessMatch.TargetLufs - r.Lufs!.Value > Gain(r) + 0.05 && Gain(r) < LoudnessMatch.MaxBoostDb);
        int maxed = gains.Count(g => g >= LoudnessMatch.MaxBoostDb);
        Console.WriteLine();
        Console.WriteLine($"Loudness of {measured.Count} tracks (target {LoudnessMatch.TargetLufs} LUFS):");
        Console.WriteLine($"  LUFS       {Pcts(measured.Select(r => r.Lufs!.Value))}");
        Console.WriteLine($"  peak dBFS  {Pcts(measured.Select(r => r.PeakDb ?? 0))}");
        Console.WriteLine($"  gain dB    {Pcts(gains)}");
        Console.WriteLine($"  after      {Pcts(measured.Select(r => r.Lufs!.Value + Gain(r)))}");
        Console.WriteLine($"  turned up {gains.Count(g => g > 0)}, down {gains.Count(g => g < 0)}; held back by peaks {peakLimited}; at the +{LoudnessMatch.MaxBoostDb} dB limit {maxed}");
    }
}
