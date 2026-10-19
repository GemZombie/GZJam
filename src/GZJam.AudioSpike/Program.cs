using GZJam.Audio;
using GZJam.AudioSpike;

// Throwaway console harness proving the audio foundations:
// main output + headphone cue on separate devices, tempo change without pitch change,
// BPM detection, and remembering device choices between runs.

using var engine = new AudioEngine();
using var main = new Deck(engine, "Main");
using var cue = new Deck(engine, "Cue");
var bpmDetector = new BpmDetector(engine);
var settings = SpikeSettings.Load();

Console.WriteLine($"GZJam audio spike - BASS {engine.BassVersion}, BASS_FX {engine.FxVersion}");
Console.WriteLine($"Settings: {SpikeSettings.FilePath}");
Console.WriteLine();

RestoreDevice(main, settings.MainDevice);
RestoreDevice(cue, settings.CueDevice);
PrintStatus();
PrintHelp();

while (true)
{
    Console.Write("> ");
    string? line = Console.ReadLine();
    if (line is null)
        break;

    string[] parts = line.Trim().Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
    if (parts.Length == 0)
    {
        PrintStatus();
        continue;
    }

    try
    {
        string command = parts[0].ToLowerInvariant();
        if (command is "quit" or "exit" or "q")
            break;

        switch (command)
        {
            case "help" or "?":
                PrintHelp();
                break;

            case "devices":
                PrintDevices();
                break;

            case "main" or "cue":
            {
                Deck deck = command == "main" ? main : cue;
                AudioDevice device = PickDevice(Arg(parts, 1, "device number"));
                deck.SetDevice(device);
                if (command == "main")
                    settings.MainDevice = device.Preference;
                else
                    settings.CueDevice = device.Preference;
                settings.Save();
                Console.WriteLine($"{deck.Name} -> {device} (saved)");
                break;
            }

            case "load":
            {
                Deck deck = PickDeck(Arg(parts, 1, "m|c"));
                string path = Unquote(Arg(parts, 2, "file path"));
                deck.Load(path);
                deck.Play();
                Console.WriteLine($"{deck.Name}: playing {Path.GetFileName(path)} [{deck.Length:m\\:ss}]");
                break;
            }

            case "play":
                PickDeck(Arg(parts, 1, "m|c")).Play();
                break;

            case "pause":
                PickDeck(Arg(parts, 1, "m|c")).Pause();
                break;

            case "stop":
                PickDeck(Arg(parts, 1, "m|c")).Stop();
                break;

            case "seek":
            {
                Deck deck = PickDeck(Arg(parts, 1, "m|c"));
                deck.Seek(TimeSpan.FromSeconds(double.Parse(Arg(parts, 2, "seconds"))));
                break;
            }

            case "tempo":
            {
                Deck deck = PickDeck(Arg(parts, 1, "m|c"));
                deck.TempoPercent = double.Parse(Arg(parts, 2, "percent"));
                Console.WriteLine($"{deck.Name}: tempo {deck.TempoPercent:+0.0;-0.0;0}%");
                break;
            }

            case "vol":
            {
                Deck deck = PickDeck(Arg(parts, 1, "m|c"));
                deck.Volume = double.Parse(Arg(parts, 2, "0-100")) / 100;
                break;
            }

            case "bpm":
            {
                string rest = line.Trim()[parts[0].Length..].Trim();
                string path = rest.Length > 0 ? Unquote(rest)
                    : main.TrackPath ?? throw new ArgumentException("Give a file, or load one on main first.");
                Console.WriteLine("Detecting...");
                double? bpm = bpmDetector.Detect(path);
                Console.WriteLine(bpm is null ? "Could not detect BPM." : $"~{bpm:0.0} BPM ({Path.GetFileName(path)})");
                break;
            }

            case "status" or "s":
                PrintStatus();
                break;

            default:
                Console.WriteLine("Unknown command. Type 'help'.");
                break;
        }
    }
    catch (Exception ex) when (ex is AudioException or ArgumentException or FormatException or InvalidOperationException)
    {
        Console.WriteLine($"Error: {ex.Message}");
    }
}

void RestoreDevice(Deck deck, AudioDevicePreference? saved)
{
    AudioDevice? device = engine.FindDevice(saved);
    if (saved is not null && device is null)
        Console.WriteLine($"{deck.Name}: saved device '{saved.Name}' is not connected; using system default.");

    device ??= engine.GetDefaultDevice();
    if (device is null)
    {
        Console.WriteLine($"{deck.Name}: no output devices found.");
        return;
    }

    try
    {
        deck.SetDevice(device);
    }
    catch (AudioException ex)
    {
        Console.WriteLine($"{deck.Name}: {ex.Message}");
    }
}

void PrintDevices()
{
    foreach (AudioDevice d in engine.GetOutputDevices())
    {
        string tags = (d.Index == main.Device?.Index ? " [MAIN]" : "") + (d.Index == cue.Device?.Index ? " [CUE]" : "");
        Console.WriteLine($"  {d.Index,2}: {d}{tags}");
    }
}

void PrintStatus()
{
    foreach (Deck deck in new[] { main, cue })
    {
        string device = deck.Device?.Name ?? "(none)";
        string track = deck.TrackPath is null
            ? "empty"
            : $"{(deck.IsPlaying ? "PLAYING" : "paused ")} {Path.GetFileName(deck.TrackPath)} " +
              $"{deck.Position:m\\:ss}/{deck.Length:m\\:ss}, -{deck.Remaining:m\\:ss} left, " +
              $"tempo {deck.TempoPercent:+0.0;-0.0;0}%, vol {deck.Volume * 100:0}";
        Console.WriteLine($"{deck.Name,-4} on {device}: {track}");
    }
}

void PrintHelp()
{
    Console.WriteLine("""

    Commands (m = main deck, c = cue deck; press Enter alone for status):
      devices                  list output devices
      main <#> / cue <#>       route a deck to a device (remembered next run)
      load <m|c> <file>        load a track and start playing (drag a file into the terminal)
      play|pause|stop <m|c>
      seek <m|c> <seconds>
      tempo <m|c> <percent>    e.g. 'tempo m -8' = 8% slower, same pitch
      vol <m|c> <0-100>
      bpm [file]               detect BPM (defaults to the main deck's track)
      status | help | quit

    """);
}

AudioDevice PickDevice(string arg)
{
    int index = int.Parse(arg);
    return engine.GetOutputDevices().FirstOrDefault(d => d.Index == index)
        ?? throw new ArgumentException($"No device {index}. Type 'devices'.");
}

Deck PickDeck(string arg) => arg.ToLowerInvariant() switch
{
    "m" or "main" => main,
    "c" or "cue" => cue,
    _ => throw new ArgumentException("Deck must be 'm' or 'c'."),
};

static string Arg(string[] parts, int index, string name) =>
    parts.Length > index ? parts[index] : throw new ArgumentException($"Missing {name}.");

// Terminals wrap dragged-in paths in quotes (Windows) or single quotes (Linux).
static string Unquote(string path) => path.Trim().Trim('"', '\'');
