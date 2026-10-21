# GZJam

A focused DJ application for swing-dance DJing, without the clutter of club-DJ software.

## Stack

- .NET 10 + [Avalonia](https://avaloniaui.net/) (Windows, Linux and macOS)
- [BASS](https://www.un4seen.com/) via ManagedBass — playback, multi-output headphone cue, BASS_FX tempo/key-lock and BPM detection
- TagLib# — tag reading/writing
- SQLite — library, play history, notes

BASS is used under its free non-commercial license (personal use only).

## Running

```
dotnet run --project src/GZJam.App
```

Click **+ Folder** and pick your music folder. GZJam indexes it (about 3 seconds for 13,000 MP3s),
then watches it for changes. Your files are only ever read, never modified.

Set `GZJAM_DATA_DIR` to keep the library database and settings somewhere other than the
default (`%LOCALAPPDATA%\GZJam` and `%APPDATA%\GZJam` on Windows).

## Linux

Build a self-contained copy on any machine (it includes .NET and the Linux BASS libraries):

```
dotnet publish src/GZJam.App -c Release -r linux-x64 --self-contained -o publish/linux-x64
```

Copy `publish/linux-x64` to the Linux machine and run `./GZJam` from it. Or, with the .NET 10 SDK
installed there, `dotnet run --project src/GZJam.App` from a clone. Settings go in
`~/.config/GZJam`, the library database and the activity log (`logs/`) in `~/.local/share/GZJam`.

### Linux test pass

Things that differ from Windows and need checking on real hardware (PipeWire, e.g. Omarchy):

1. **Outputs.** Settings › Audio outputs: which devices are listed? Typically "Default" (through
   PipeWire) and each card by name. Pick the USB interface's own entry as Main rather than
   Default: does audio play, and do desktop sounds (a notification, a browser) stay *out* of it?
   That would mean the card is used directly, bypassing PipeWire, which is what we want for the PA.
2. **Headphones on a second device** (the laptop's own output as Cue): both play at once, the cue
   button and the headphone preview reach only the headphones.
3. **Unplug and replug** the USB interface: the speakers fall back to the default, the notice
   says so, and the interface is picked up again from Settings › Refresh device list.
4. **Latency and dropouts:** move the crossfader and the tempo while playing; any crackles?
5. **The library:** add a music folder; scanning, search, and the folder watcher (add or rename a
   file while GZJam runs). Large libraries may need a higher inotify watch limit.
6. **Window and input:** scaling on a high-DPI screen, the window under a tiling compositor
   (Hyprland runs it through XWayland), trackpad and touch on the faders and keys.
7. **Fonts:** the readouts fall back from Cascadia Mono to DejaVu Sans Mono; do they line up?

The activity log (`~/.local/share/GZJam/logs`) records device changes and errors; attach it to
any report.

## Tests

```
dotnet test
```

UI tests run headless. `ScreenshotTests` also renders the window over a real music folder
(`C:\music`, or `GZJAM_SCREENSHOT_MUSIC`) and saves PNGs to `%TEMP%\gzjam-screenshots`.

## Layout

- `src/GZJam.App` — the Avalonia desktop app
- `src/GZJam.Library` — library index: SQLite + FTS5 search, scanner, folder tree, folder watcher
- `src/GZJam.Audio` — audio engine: devices, mixer (main + cue buses, crossfader), decks with tempo/key-lock, headphone preview, BPM detection
- `tests/` — library tests and headless UI tests
- `tools/GZJam.DevTools` — `bench <folder>`: scan timings and tag coverage for a real library
- `lib/bass/` — native BASS + BASS_FX for Windows (x64), Linux (x64) and macOS (universal); `Bass.targets` copies the right set into each app's output
