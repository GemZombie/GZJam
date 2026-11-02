namespace GZJam.App;

/// <summary>
/// Where GZJam keeps its files, following each OS's conventions:
///   Windows  settings in %APPDATA%\GZJam, database in %LOCALAPPDATA%\GZJam
///   macOS    both in ~/Library/Application Support/GZJam
///   Linux    settings in ~/.config/GZJam, database in ~/.local/share/GZJam
/// Set GZJAM_DATA_DIR to use a different folder (handy for trying things out without touching
/// your real library and settings).
/// </summary>
internal static class AppPaths
{
    private static readonly string? Override = Environment.GetEnvironmentVariable("GZJAM_DATA_DIR");

    private static string MacAppSupport =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "GZJam");

    /// <summary>Small settings that follow the user.</summary>
    public static string SettingsDirectory { get; } = Override ?? (OperatingSystem.IsMacOS()
        ? MacAppSupport
        : Path.Combine(Folder(Environment.SpecialFolder.ApplicationData), "GZJam"));

    /// <summary>The library database, which is machine-specific.</summary>
    public static string DataDirectory { get; } = Override ?? (OperatingSystem.IsMacOS()
        ? MacAppSupport
        : Path.Combine(Folder(Environment.SpecialFolder.LocalApplicationData), "GZJam"));

    /// <summary>
    /// The OS's folder, made if it isn't there yet (#250): a new Linux account may have no
    /// ~/.local/share, and asked for one that's missing, .NET gives "", which would put GZJam's
    /// library in whatever folder it was started from.
    /// </summary>
    private static string Folder(Environment.SpecialFolder folder) =>
        Environment.GetFolderPath(folder, Environment.SpecialFolderOption.Create);

    public static string SettingsFile => Path.Combine(SettingsDirectory, "settings.json");
    public static string LibraryDatabase => Path.Combine(DataDirectory, "library.db");
}
