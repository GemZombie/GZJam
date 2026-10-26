namespace GZJam.App;

/// <summary>How keyboard shortcuts are named on this computer, for tooltips and the guide.</summary>
internal static class Shortcuts
{
    /// <summary>The command key: Cmd on a Mac, Ctrl elsewhere.</summary>
    public static string Command => OperatingSystem.IsMacOS() ? "Cmd" : "Ctrl";
}
