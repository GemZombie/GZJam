# GZJam

A focused DJ application for swing-dance DJing, without the clutter of club-DJ software.

## Stack

- .NET 10 + [Avalonia](https://avaloniaui.net/) (Windows & Linux)
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

## Tests

```
dotnet test
```

UI tests run headless. `ScreenshotTests` also renders the window over a real music folder
(`C:\music`, or `GZJAM_SCREENSHOT_MUSIC`) and saves PNGs to `%TEMP%\gzjam-screenshots`.

## Layout

- `src/GZJam.App` — the Avalonia desktop app
- `src/GZJam.Library` — library index: SQLite + FTS5 search, scanner, folder tree, folder watcher
- `src/GZJam.Audio` — audio engine: devices, decks (tempo/key-lock), BPM detection
- `src/GZJam.AudioSpike` — throwaway console harness for testing the audio engine
- `tests/` — library tests and headless UI tests
- `tools/GZJam.DevTools` — `bench <folder>`: scan timings and tag coverage for a real library
- `lib/bass/` — native BASS + BASS_FX for Windows (x64), Linux (x64) and macOS (universal); `Bass.targets` copies the right set into each app's output
