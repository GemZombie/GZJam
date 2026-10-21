namespace GZJam.Library;

/// <summary>
/// The tempo range the DJ expects their music to fall in (a setting). Tempo detection's mistakes
/// are nearly always half or double time, so a detected BPM outside the range is doubled or
/// halved into it. A range narrower than double (say 120-180) leaves exactly one choice. Tag
/// BPMs are never changed.
/// </summary>
public static class BpmRange
{
    /// <summary>The detector's own limits: with these, its choices stand.</summary>
    public const int Lowest = 60, Highest = 320;

    public static int Min { get; private set; } = Lowest;
    public static int Max { get; private set; } = Highest;

    public static void Set(int min, int max)
    {
        Min = Math.Clamp(min, 30, 400);
        Max = Math.Clamp(max, Min, 400);
    }

    /// <summary>Doubles or halves <paramref name="bpm"/> into the range (the fewest steps, if several fit).</summary>
    public static double Fold(double bpm) => Fold(bpm, Min, Max);

    public static double Fold(double bpm, int min, int max)
    {
        if (bpm <= 0)
            return bpm;
        double best = bpm, bestDistance = double.MaxValue;
        foreach (int k in new[] { 0, 1, -1, 2, -2, 3, -3 })
        {
            double candidate = bpm * Math.Pow(2, k);
            // How far outside the range, in octaves (0 inside it).
            double distance = candidate < min ? Math.Log2(min / candidate) : candidate > max ? Math.Log2(candidate / max) : 0;
            if (distance < bestDistance - 1e-9)
                (best, bestDistance) = (candidate, distance);
        }
        return best;
    }
}
