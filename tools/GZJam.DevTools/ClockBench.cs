using System.Diagnostics;
using GZJam.Audio;

namespace GZJam.DevTools;

/// <summary>
/// How evenly a playing deck reports its position, as the close-up waveform sees it (sampled every
/// 33 ms, like the app's playback timer): the steps between reports, and how far each report is
/// from a steady line; then how the close-up's playhead follows them, eased all at once (as in
/// 0.5) or spread out (as now). Plays silently (the deck's volume at zero) on the default output.
/// </summary>
internal static class ClockBench
{
    public static int Run(double seconds)
    {
        string tone = GZJam.Testing.TestWav.WriteTone(Path.Combine(Path.GetTempPath(), "gzjam-clock.wav"), TimeSpan.FromSeconds(seconds + 5));
        using var audio = new AudioEngine();
        using var mix = new MixEngine(audio);
        AudioDevice device = audio.GetOutputDevices().First(d => d.IsDefault);
        mix.SetOutputs(device, null);
        Deck deck = mix.Decks[0];
        deck.Load(tone);
        deck.Volume = 0;
        deck.Play();

        var clock = Stopwatch.StartNew();
        var samples = new List<(double At, double Position)>();
        while (clock.Elapsed.TotalSeconds < seconds)
        {
            samples.Add((clock.Elapsed.TotalSeconds, deck.Position.TotalSeconds));
            Thread.Sleep(33);
        }
        deck.Pause();

        // The steady line through them all (least squares), and each report's distance from it.
        int n = samples.Count;
        double mt = samples.Average(s => s.At), mp = samples.Average(s => s.Position);
        double slope = samples.Sum(s => (s.At - mt) * (s.Position - mp)) / samples.Sum(s => (s.At - mt) * (s.At - mt));
        var residuals = samples.Select(s => (s.Position - (mp + slope * (s.At - mt))) * 1000).ToList();
        var steps = samples.Zip(samples.Skip(1), (a, b) => (b.Position - a.Position) * 1000).ToList();
        var distinctSteps = steps.Select(s => Math.Round(s)).GroupBy(s => s).OrderByDescending(g => g.Count()).Take(6);

        Console.WriteLine($"{device.Name}: {n} reports over {seconds:0} s, speed {slope:0.0000}x");
        Console.WriteLine($"  off the steady line: sd {Sd(residuals):0.0} ms, worst {residuals.Max(Math.Abs):0.0} ms");
        Console.WriteLine($"  steps between reports (ms, count): {string.Join(", ", distinctSteps.Select(g => $"{g.Key}×{g.Count()}"))}");
        Console.WriteLine($"  reports that didn't move: {steps.Count(s => s == 0)}");

        // How the close-up would move through these reports, drawn at 60 frames a second: the
        // frame-to-frame steps, in pixels at the tightest zoom (2 s across 520 px).
        const double pixelsPerSecond = 520 / 2.0;
        foreach (var (name, follower) in new (string, IFollower)[]
        {
            ("all at once (0.5)", new SpreadEasing(0.1, 1, 0)),
            ("spread out (now)", new SpreadEasing(0.1, 0.03, 0.15)),
        })
        {
            var frames = new List<double>();
            var off = new List<double>();   // from the steady line, in ms, once settled
            int next = 0;
            for (double t = samples[10].At; t < samples[^1].At; t += 1 / 60.0)
            {
                while (next < n && samples[next].At <= t)
                {
                    follower.Report(samples[next].At, samples[next].Position);
                    next++;
                }
                frames.Add(follower.At(t));
                if (t > samples[10].At + 2)
                    off.Add((follower.At(t) - (mp + slope * (t - mt))) * 1000);
            }
            var moves = frames.Zip(frames.Skip(1), (a, b) => (b - a) * pixelsPerSecond).ToList();
            Console.WriteLine($"  {name,-18} each frame moves {moves.Average():0.00} px, sd {Sd(moves):0.000} px, " +
                $"most off {moves.Max(m => Math.Abs(m - moves.Average())):0.00} px; from the music: sd {Sd(off):0.0} ms, worst {off.Max(Math.Abs):0.0} ms");
        }
        return 0;
    }

    private interface IFollower
    {
        void Report(double at, double position);
        double At(double t);
    }

    /// <summary>
    /// CloseUpWaveform's playhead: a clock, eased toward each report by <paramref name="gain"/> of
    /// its error (at most <paramref name="cap"/> s), shown over <paramref name="spread"/> s (0: at once).
    /// </summary>
    private sealed class SpreadEasing(double gain, double cap, double spread) : IFollower
    {
        private double _from, _at, _target, _shown, _shownAt;
        private bool _started;

        private double Shown(double t)
        {
            if (t > _shownAt)
            {
                _shown = spread > 0 ? _target - (_target - _shown) * Math.Exp(-(t - _shownAt) / spread) : _target;
                _shownAt = t;
            }
            return _shown;
        }

        public double At(double t) => _from + (t - _at) + Shown(t);

        public void Report(double at, double position)
        {
            double error = position - (_from + (at - _at) + _target);
            if (!_started || Math.Abs(position - At(at)) > 0.25)
                (_from, _at, _target, _shown, _shownAt, _started) = (position, at, 0, 0, at, true);
            else
            {
                Shown(at);
                _target += Math.Clamp(error, -cap, cap) * gain;
            }
        }
    }

    private static double Sd(IReadOnlyList<double> values)
    {
        double mean = values.Average();
        return Math.Sqrt(values.Sum(v => (v - mean) * (v - mean)) / values.Count);
    }
}
