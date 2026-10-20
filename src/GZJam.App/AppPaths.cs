namespace GZJam.App;

/// <summary>
/// Where GZJam keeps its files. Set GZJAM_DATA_DIR to use a different folder (handy for
/// trying things out without touching your real library and settings).
/// </summary>
internal static class AppPaths
{
    private static readonly string? Override = Environment.GetEnvironmentVariable("GZJAM_DATA_DIR");

    /// <summary>Small settings that follow the user: %APPDATA%\GZJam, ~/.config/GZJam.</summary>
    public static string SettingsDirectory { get; } = Override ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GZJam");

    /// <summary>The library database, which is machine-specific: %LOCALAPPDATA%\GZJam, ~/.local/share/GZJam.</summary>
    public static string DataDirectory { get; } = Override ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GZJam");

    public static string SettingsFile => Path.Combine(SettingsDirectory, "settings.json");
    public static string LibraryDatabase => Path.Combine(DataDirectory, "library.db");
}
