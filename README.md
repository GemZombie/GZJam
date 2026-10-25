<h1>
  <picture>
    <source media="(prefers-color-scheme: light)" srcset="branding/gzjam-logo-light.svg">
    <img src="branding/gzjam-logo.svg" alt="GZJam" height="64">
  </picture>
</h1>

A free DJ app for social dance nights: swing, West Coast, blues, ballroom and more. Simple,
intuitive and useful: what you need to play music for dancers, without the clutter.

![GZJam during a set: Deck 1 on air, Deck 2 cued in the headphones, a search of the library, and the queue](docs/images/main-screen.png)

## Download

**[Download the latest release](https://github.com/GemZombie/GZJam/releases/latest)** and pick
the file for your computer. How to use it, from your first song to a night's checklist, is in
the [user guide](docs/GUIDE.md). What changed in each release is in [CHANGELOG.md](CHANGELOG.md);
what GZJam does and what's coming is in [ROADMAP.md](ROADMAP.md).
Questions, ideas and news are in [Discussions](https://github.com/GemZombie/GZJam/discussions).

| Computer | File | To install |
|---|---|---|
| Windows | `GZJam-<version>-windows-x64-setup.exe` | Run it. Installs for you alone, no administrator needed. |
| Windows, without installing | `GZJam-<version>-windows-x64.zip` | Unzip anywhere and run `GZJam.exe`. |
| Mac with Apple Silicon (M1 or later) | `GZJam-<version>-macos-arm64.dmg` | Open it and drag GZJam to Applications. |
| Mac with Intel | `GZJam-<version>-macos-x64.dmg` | The same. |
| Linux (x64) | `GZJam-<version>-linux-x64.tar.gz` | Unpack it and run `./install.sh` in the folder. |

Everything GZJam needs comes with it, .NET included. Your music is only ever read, never changed.

At startup GZJam asks GitHub whether there's a newer release, and says so in the top bar before
the music starts. That one request sends nothing about you or your music, and GZJam never
downloads or installs anything by itself. To turn it off: Settings › About › *Tell me when a new
version is out*. To look yourself at any time: Settings › About › *Check now*.

Your hearts, labels, cues, playlists and History live in GZJam's library, not in your music
files: back them up now and then with Settings › Library › *Back up…* (GZJam also keeps a copy of
its own each day it's opened, in its data folder under `backups`).

GZJam plays MP3, FLAC, WAV, AIFF, OGG Vorbis, Opus and Apple Lossless (`.m4a`) everywhere, and
AAC (`.m4a`, `.aac`) on Windows and Mac. Linux can't play AAC yet.

GZJam is free and isn't signed with a paid certificate, so the first time you open it:

- **Windows** may say "Windows protected your PC": click **More info**, then **Run anyway**.
- **macOS** may say it can't check GZJam for malicious software: open **System Settings ›
  Privacy & Security**, scroll down and click **Open Anyway** beside the note about GZJam.

## If the sound crackles or drops out

- Turn on **Settings › Sound › Extra protection against crackles**. Play and the crossfader respond
  a little later, which hardly matters for most DJing.
- Use a wired output: Bluetooth speakers and headphones add delay and drop out.
- Plug the sound card straight into the computer rather than through a hub or dock.
- Keep the computer plugged in, and set its power mode to best performance during a set.
- On Windows, if other programs refuse to play while GZJam is open, the speakers GZJam has to
  itself are also Windows' default output: choose another output in Windows' Sound settings.

If it still happens, [open an issue](https://github.com/GemZombie/GZJam/issues/new/choose) and
attach the activity log: it notes when the speakers' sound ran dry.

## Supporting GZJam

GZJam is free, and always will be. If it's useful to you and you'd like to support the work,
you can [sponsor it on GitHub](https://github.com/sponsors/GemZombie). Telling other DJs about it,
reporting problems and sharing ideas help just as much.

## Stack

- .NET 10 + [Avalonia](https://avaloniaui.net/) (Windows, Linux and macOS)
- [BASS](https://www.un4seen.com/) via ManagedBass — playback, multi-output headphone cue, BASS_FX tempo/key-lock and BPM detection, BASSFLAC/BASSOPUS/BASSALAC for FLAC, Opus and Apple Lossless
- TagLib# — tag reading (GZJam never writes to music files)
- SQLite — library, play history, notes

## Licence

GZJam's own code is under the [MIT licence](LICENSE). It's built with other people's software,
each under its own licence, listed in [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt). BASS
(in `lib/bass/`) isn't open source: it's free for software that isn't sold, which is how GZJam
uses it. Donations are asked for here and on GitHub only, never inside GZJam, and donors get
nothing extra, which un4seen says keeps BASS free to use. Selling GZJam, or asking for money inside
it, would need a licence from [un4seen](https://www.un4seen.com/).

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

What to try on a real Linux computer (and on Windows and a Mac) is in
[docs/TESTING.md](docs/TESTING.md).

## Tests

```
dotnet test
```

UI tests run headless. `ScreenshotTests` also renders the window over a real music folder
(`C:\music`, or `GZJAM_SCREENSHOT_MUSIC`) and saves PNGs to `%TEMP%\gzjam-screenshots`. The
picture at the top of this page is its `readme-main-screen.png`, copied to `docs/images/main-screen.png`.

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

  First add the version's section to [CHANGELOG.md](CHANGELOG.md): what a DJ would notice since the
  last release. It becomes the release's notes (with download instructions added by
  `tools/release-notes.sh`), and a tag without one fails straight away. Bring
  [ROADMAP.md](ROADMAP.md) up to date too: the release's features move into "What GZJam does
  today", and "Up to date as of" gets the new version. And the [user guide](docs/GUIDE.md): what's
  new is in it, in the app's own words. Its pictures come from `ScreenshotTests` (see *Tests*),
  copied to `docs/images/guide/`. Then push a version tag to draft a GitHub
  release with the packages attached, and publish the draft by hand:

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
  version, so builds made in between read "0.3.0-dev". A test fails until it does
  (`A_build_made_here_is_ahead_of_the_newest_release`).

Uninstalling never removes settings, the library database or the DJ's data, and never touches music.

The packages aren't signed yet, so the first launch asks for confirmation:

- **Windows** SmartScreen shows "Windows protected your PC": click *More info* › *Run anyway*.
- **macOS** says it can't verify the app: open System Settings › Privacy & Security and click
  *Open Anyway*, or run `xattr -dr com.apple.quarantine /Applications/GZJam.app`.

The logo and app icon are drawn in code, so every size is sharp: the shapes are in
`src/GZJam.App/Controls/LogoShapes.cs`, which the app draws from too. After changing them (or
`tools/GZJam.DevTools/IconArt.cs`), run `dotnet run --project tools/GZJam.DevTools -- icon .` to redraw
`src/GZJam.App/Assets/gzjam.ico`, `gzjam.png`, `packaging/macos/GZJam.icns` and the logo's SVG files
in `branding/` (and a preview of every icon size in the temp folder).

Packaging files are in `packaging/` (the Inno Setup script, the macOS `Info.plist` and bundle
script, the Linux tarball and install scripts).

## Layout

- `src/GZJam.App` — the Avalonia desktop app
- `src/GZJam.Library` — library index: SQLite + FTS5 search, scanner, folder tree, folder watcher
- `src/GZJam.Audio` — audio engine: devices, mixer (main + cue buses, crossfader), decks with tempo/key-lock, headphone preview, BPM detection
- `tests/` — library tests and headless UI tests
- `tools/GZJam.DevTools` — `bench <folder>`: scan timings and tag coverage for a real library
- `lib/bass/` — native BASS + BASS_FX for Windows (x64), Linux (x64) and macOS (universal); `Bass.targets` copies the right set into each app's output
