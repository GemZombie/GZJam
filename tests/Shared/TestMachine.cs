namespace GZJam.Testing;

/// <summary>Where the tests are running.</summary>
public static class TestMachine
{
    /// <summary>
    /// On GitHub's CI machines. Some have a sound device, but a virtual one that doesn't play in
    /// real time: tests that time the sound on real speakers can't be judged there. A 400 ms stall
    /// went unseen on one, and .NET paused for 3.4 s on an overloaded one (2026-11-10).
    /// </summary>
    public static bool IsCi => Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true";

    public const string NeedsRealSpeakers = "Needs real speakers: CI machines have none, or a virtual device that doesn't keep time.";
}
