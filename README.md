# GZJam

A DJ app that aims to be simple, intuitive and useful: what you need to play music for people,
without the clutter.

## Download

**[Download the latest release](https://github.com/GemZombie/GZJam/releases/latest)** and pick
the file for your computer:

| Computer | File | To install |
|---|---|---|
| Windows | `GZJam-<version>-windows-x64-setup.exe` | Run it. Installs for you alone, no administrator needed. |
| Windows, without installing | `GZJam-<version>-windows-x64.zip` | Unzip anywhere and run `GZJam.exe`. |
| Mac with Apple Silicon (M1 or later) | `GZJam-<version>-macos-arm64.dmg` | Open it and drag GZJam to Applications. |
| Mac with Intel | `GZJam-<version>-macos-x64.dmg` | The same. |
| Linux (x64) | `GZJam-<version>-linux-x64.tar.gz` | Unpack it and run `./install.sh` in the folder. |

Everything GZJam needs comes with it, .NET included. Your music is only ever read, never changed.

GZJam plays MP3, FLAC, WAV, AIFF, OGG Vorbis, Opus and Apple Lossless (`.m4a`) everywhere, and
AAC (`.m4a`, `.aac`) on Windows and Mac. Linux can't play AAC yet.

GZJam is free and isn't signed with a paid certificate, so the first time you open it:

- **Windows** may say "Windows protected your PC": click **More info**, then **Run anyway**.
- **macOS** may say it can't check GZJam for malicious software: open **System Settings ›
  Privacy & Security**, scroll down and click **Open Anyway** beside the note about GZJam.

## Stack

- .NET 10 + [Avalonia](https://avaloniaui.net/) (Windows, Linux and macOS)
- [BASS](https://www.un4seen.com/) via ManagedBass — playback, multi-output headphone cue, BASS_FX tempo/key-lock and BPM detection, BASSFLAC/BASSOPUS/BASSALAC for FLAC, Opus and Apple Lossless
- TagLib# — tag reading (GZJam never writes to music files)
- SQLite — library, play history, notes

## Licence

GZJam's own code is under the [MIT licence](LICENSE). It's built with other people's software,
each under its own licence, listed in [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt). BASS
(in `lib/bass/`) isn't open source: it's free for software that makes no money, which is how
GZJam uses it, and anything sold would need a licence from [un4seen](https://www.un4seen.com/).

## Running from source

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

Copy `publish/linux-x64` to the Linux machine and run `./GZJam` from it, or unpack a release
tarball (see *Builds and releases*) and run `./install.sh`. Or, with the .NET 10 SDK
installed there, `dotnet run --project src/GZJam.App` from a clone. Settings go in
`~/.config/GZJam`, the library database and the activity log (`logs/`) in `~/.local/share/GZJam`.

### Linux test pass

Things that differ from Windows and need checking on real hardware (PipeWire, e.g. Omarchy):

1. **Outputs.** Settings › Sound: which devices are listed? Typically "Default" (through
   PipeWire), "Built-in: …" for the computer's own outputs, and USB interfaces by name. Pick the USB interface's own entry as Main rather than
   Default: does audio play, and do desktop sounds (a notification, a browser) stay *out* of it?
   That would mean the card is used directly, bypassing PipeWire, which is what we want for the PA.
2. **Headphones on a second device** (the laptop's own output as Cue): both play at once, the cue
   button and the headphone preview reach only the headphones.
3. **Unplug and replug** the USB interface: the speakers fall back to the default, the notice
   says so, and the interface is picked up again from Settings › Sound › Refresh the list.
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

## Builds and releases

GitHub Actions, in `.github/workflows`:

- **CI** (`ci.yml`) builds and runs the tests on each OS. Day to day, build and test locally; run
  CI at important times, from Actions › CI › *Run workflow* (Linux, Windows and macOS). Pull
  requests run it on Linux and Windows: macOS minutes cost ten times as much in a private repo.
  It makes no packages.
- **Release** (`release.yml`) runs the tests on each OS, then builds the packages:

  | | |
  |---|---|
  | Windows | `GZJam-<version>-windows-x64-setup.exe`: installs for the current user, no admin prompt; also a portable `.zip` |
  | macOS | `GZJam-<version>-macos-arm64.dmg` (Apple Silicon) and `-macos-x64.dmg` (Intel) |
  | Linux | `GZJam-<version>-linux-x64.tar.gz`: run `./install.sh` from it for a launcher entry and a `gzjam` command (`./install.sh --uninstall` removes it) |

  Push a version tag to draft a GitHub release with them attached, then publish the draft by hand:

  ```
  git tag v0.2.0
  git push origin v0.2.0
  ```

  A tag with a suffix (`v0.2.0-beta.1`) is marked as a pre-release. Actions › Release ›
  *Run workflow* builds test packages without a release; they're kept with the run for 14 days.

  **Version numbers:** every release build gets a new one; a tag is never moved or reused, even
  for a draft nobody has seen. The patch number is for fixes only (0.2.0 → 0.2.1), the minor
  number for anything new (0.2.x → 0.3.0), and 1.0 for when GZJam is ready for any DJ to pick up.
  After a release, `VersionPrefix` in `src/GZJam.App/GZJam.App.csproj` goes up to the next minor
  version, so builds made in between read "0.3.0-dev".

Uninstalling never removes settings, the library database or the DJ's data, and never touches music.

The packages aren't signed yet, so the first launch asks for confirmation:

- **Windows** SmartScreen shows "Windows protected your PC": click *More info* › *Run anyway*.
- **macOS** says it can't verify the app: open System Settings › Privacy & Security and click
  *Open Anyway*, or run `xattr -dr com.apple.quarantine /Applications/GZJam.app`.

The app icon is drawn in code, so every size is sharp: after changing `tools/GZJam.DevTools/IconArt.cs`,
run `dotnet run --project tools/GZJam.DevTools -- icon .` to redraw `src/GZJam.App/Assets/gzjam.ico`,
`gzjam.png` and `packaging/macos/GZJam.icns` (and a preview of every size in the temp folder).

Packaging files are in `packaging/` (the Inno Setup script, the macOS `Info.plist` and bundle
script, the Linux tarball and install scripts).

## Layout

- `src/GZJam.App` — the Avalonia desktop app
- `src/GZJam.Library` — library index: SQLite + FTS5 search, scanner, folder tree, folder watcher
- `src/GZJam.Audio` — audio engine: devices, mixer (main + cue buses, crossfader), decks with tempo/key-lock, headphone preview, BPM detection
- `tests/` — library tests and headless UI tests
- `tools/GZJam.DevTools` — `bench <folder>`: scan timings and tag coverage for a real library
- `lib/bass/` — native BASS + BASS_FX for Windows (x64), Linux (x64) and macOS (universal); `Bass.targets` copies the right set into each app's output
