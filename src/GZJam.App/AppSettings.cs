using System.Text.Json;

namespace GZJam.App;

/// <summary>Everything GZJam remembers between sessions. Saved as JSON; unknown or bad files fall back to defaults.</summary>
public sealed class AppSettings
{
    public WindowPlacement? Window { get; set; }

    /// <summary>The folder selected in the library tree, as root path + relative folder.</summary>
    public string? SelectedRootPath { get; set; }
    public string? SelectedFolder { get; set; }

    public sealed class WindowPlacement
    {
        public int X { get; set; }
        public int Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public bool Maximized { get; set; }
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static AppSettings Load(string path)
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions) ?? new();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            System.Diagnostics.Trace.WriteLine($"Ignoring unreadable settings: {ex.Message}");
        }
        return new();
    }

    public void Save(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            // Write then swap, so a crash mid-save never leaves a truncated file.
            string temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(this, JsonOptions));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Trace.WriteLine($"Could not save settings: {ex.Message}");
        }
    }
}
