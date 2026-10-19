using System.Text.Json;
using GZJam.Audio;

namespace GZJam.AudioSpike;

/// <summary>Remembers device choices between runs.</summary>
internal sealed class SpikeSettings
{
    public AudioDevicePreference? MainDevice { get; set; }
    public AudioDevicePreference? CueDevice { get; set; }

    // %APPDATA%\GZJam on Windows, ~/.config/GZJam on Linux.
    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GZJam", "audio-spike.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static SpikeSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<SpikeSettings>(File.ReadAllText(FilePath)) ?? new();
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            Console.WriteLine($"Ignoring unreadable settings file: {ex.Message}");
        }
        return new();
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
    }
}
