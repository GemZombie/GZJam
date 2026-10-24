# Testing GZJam on a real computer

The automated tests check a great deal, but not real sound cards, real speakers, a trackpad, an
unsigned download, or a whole night. This is the list to go through by hand on each computer
before 1.0, and again before a release that changes sound or packaging.

Do **Every computer** on each one, then that system's own section. A full pass takes about two
hours, most of it the night of auto-play, which can run while you do something else.

## Before you start

**You'll need:**
- Speakers or a powered monitor on a **USB sound card**, and **headphones** on the computer's own
  socket.
- A **splitter cable** (one stereo plug to two sockets, sold as a "DJ splitter" or "Y cable").
- A second speaker to act as the **booth**, if you can: any third output will do.
- A **music folder** with a few hundred songs, including MP3s, FLACs and a few M4As (AAC, and
  Apple Lossless if you have any).
- Optionally, a **Mixxx library** to import.

**Write down:** the date, the computer (make, model, Windows/macOS/Linux version), the sound card,
and the GZJam version (Settings › About).

**Report** each failure as a comment on that system's issue (Windows: a new issue; Mac: #18;
Linux: #19), with what you did, what happened, and the activity log attached:

| System | Activity log |
|---|---|
| Windows | `%LOCALAPPDATA%\GZJam\logs` |
| macOS | `~/Library/Application Support/GZJam/logs` |
| Linux | `~/.local/share/GZJam/logs` |

To leave your own library alone while trying a test build, start GZJam with the environment
variable `GZJAM_DATA_DIR` set to an empty folder: it keeps its library and settings there instead.
For a release you'd use at a dance, test your real library.

## Every computer

### Installing
- [ ] Download the release from GitHub and install it (see your system's section).
- [ ] The startup screen shows the logo, then the main window opens where it was last time.
- [ ] **Upgrading:** installed over the previous version, your library, marks, playlists, History
      and Settings are all still there.

### Library
- [ ] Add your music folder (*+ Folder*). The first scan fills the folder tree and the song list,
      with a progress line while it runs.
- [ ] Search by title, artist and folder name; try `bpm:120-140`, `~bpm:130`, `rating:yes`,
      `energy:high`, `label:`, `played:7` and `-played:7`.
- [ ] Mark songs ♥ and broken heart, set energy, add and rename a label.
- [ ] **The folder watcher:** while GZJam is open, copy a new song into the music folder. It shows
      up within a few seconds. Delete it: it goes.
- [ ] **A rename:** give a song a ♥, a label and a cue, and add it to a playlist. Rename the file
      in the file manager. After a few seconds it still has all of them.
- [ ] Album art shows on the decks and queue for songs that have it.
- [ ] Every format plays: MP3, FLAC, WAV, OGG, M4A (AAC; on Linux an AAC song says why it can't).
- [ ] Optional: **Import from Mixxx** (Settings › Library) brings ratings, crates as labels,
      playlists, History, locked BPMs and hot cues.

### Decks
- [ ] Load songs by dragging to a deck, and by right-click › *Load to Deck 1*.
- [ ] Play, pause, the waveform and close-up scroll smoothly; zoom the close-up (wheel or pinch).
- [ ] Tempo with key lock: the song speeds up without the pitch rising.
- [ ] Set a start and end, a jump, and a cue key; drag them along the waveform. Tap a cue key on
      air.
- [ ] EQ, EVEN and the mid band change the sound, and are remembered when the song is loaded again.
- [ ] Tap tempo and the BPM menu fix a wrong BPM.
- [ ] Loading over a deck the room is hearing is refused (or asks, as set in Settings › Decks).
- [ ] Four decks (Settings › Decks): all four play, and the crossfader sides are 1+3 and 2+4.

### Sound and outputs
- [ ] **Speakers and headphones on separate outputs:** the room hears only the deck the crossfader
      is on; the headphones hear only cued decks and the preview. A preview is *never* heard in
      the speakers.
- [ ] **Phones mix:** turning it towards MAIN brings the room into the headphones.
- [ ] **Mono** (on by default): a song with an instrument on one side plays it on both.
- [ ] Two loud songs blended light the **LIMIT** light, and the sound doesn't distort.
- [ ] Moving the crossfader, the tempo and the volumes makes **no crackles**. If it does, turn on
      *Extra protection against crackles* and try again.
- [ ] **Splitter cable:** turn on *One output for both (splitter cable)*, with the speakers and
      headphones on the splitter. The room plays only from the left plug, the preview only from
      the right. The top bar says "Left · …" and "Right · …". If they're swapped, note which way
      round the cable was.
- [ ] **Booth** (Settings › Sound › Booth): it plays what the room hears, follows the crossfader,
      never plays the preview, and *Booth volume* works. Choosing the speakers' own output for it
      is refused with a notice.

### Unplugging
Do each while a song is playing to the room.
- [ ] **The speakers' sound card:** GZJam says so and falls back to another output (never the
      headphones' own). Plug it back in, *Refresh the list* in Settings › Sound, choose it again.
- [ ] **The headphones:** note what happens to the preview (#72 will change this).
- [ ] **The booth:** the room keeps playing.
- [ ] Start GZJam with the speakers' sound card unplugged: it says so and uses another output.
      Plugged in again before the next start, it's used again.

### A night
- [ ] Fill the queue (and a playlist, added to the queue) and turn on **Auto-play** for an hour or
      more, with a gap between songs. Every song starts and ends cleanly, with real silence in the
      gap, and the next deck counts down.
- [ ] Meanwhile, use the computer a little (a browser, a notification) and listen for sounds
      reaching the speakers.
- [ ] **History** shows the night as a session; export the set list as text and as a spreadsheet.
- [ ] Closing GZJam while the room hears music asks first, with *Keep playing* as the default.
- [ ] Reopen: the night continues as the same session if it's within three hours.
- [ ] Afterwards, read the activity log for `ERROR` lines.

### Screen and input
- [ ] At the computer's usual scaling (and at 150% or more on a high-resolution screen), nothing
      is cut off or blurry, and the time readouts line up.
- [ ] Trackpad and mouse on the faders, crossfader and waveforms.
- [ ] On a touch screen: **Touch mode** (Settings › Screen); drag songs to decks and the queue by
      finger; the faders never jump to where you touch.
- [ ] Keyboard in the song list: Enter adds to the queue, Shift+Enter plays next, Space previews.

## Windows

- [ ] **The installer** (`…-windows-x64-setup.exe`): "Windows protected your PC" › *More info* ›
      *Run anyway*. It installs without asking for an administrator.
- [ ] **The portable `.zip`:** unpacked anywhere, `GZJam.exe` runs.
- [ ] **Speakers for GZJam alone** (Settings › Sound, on by default) with a USB sound card chosen
      by name: a notification or a browser video makes **no sound** in the speakers while GZJam is
      open; after closing GZJam they play there again.
- [ ] The same with the **splitter cable**: still GZJam's alone.
- [ ] With the speakers also Windows' default output, Settings › Sound says other programs may
      refuse to play while GZJam is open; check that's what happens, and that choosing another
      default in Windows' Sound settings lets them play again.
- [ ] The title bar: GZJam's own top bar moves the window, double-click maximises, and the
      minimise, maximise and close keys work.
- [ ] **Uninstall** (Settings › Apps): GZJam goes, and reinstalling finds your library still there.

## macOS

- [ ] **The disk image:** the right one for the Mac (`arm64` for Apple Silicon M1 and later, `x64`
      for Intel). Drag GZJam to Applications.
- [ ] **First launch:** the warning that it can't be checked › System Settings › Privacy &
      Security › *Open Anyway*. The next launch opens without asking.
- [ ] If the music is in Music, Desktop, Documents or Downloads, macOS asks to allow access: allow
      it, and the scan finds the songs. Note if it never asks and finds nothing.
- [ ] **AAC and Apple Lossless** (`.m4a`) play.
- [ ] **Outputs:** the Mac's own output and a USB sound card are both listed with clear names, and
      a preview never reaches the speakers.
- [ ] **Trackpad:** two-finger scrolling in the lists, pinch to zoom the close-up, and dragging the
      faders.
- [ ] **Cmd** (not Ctrl) for the shortcuts that use it. Cmd+Q, like the close key, should ask
      first while the room hears music.
- [ ] The title bar: the traffic lights work, and GZJam's top bar moves the window.
- [ ] Sleep: close the lid mid-song, open it again, and play resumes or can be restarted.

## Linux

- [ ] **The tarball:** unpack, `./install.sh`, and GZJam appears in the app menu with its icon and
      description. `./install.sh --uninstall` removes it.
- [ ] **Outputs** (Settings › Sound): "Default" (through PipeWire or PulseAudio), "Built-in: …"
      for the computer's own outputs, and USB cards by name.
- [ ] **A sound card directly:** choose the USB card's own entry as the speakers. Desktop sounds (a
      notification, a browser) stay **out** of it.
- [ ] Switching the speakers between outputs lets go of the old one (another program can use it).
- [ ] **The library watcher** on a large library: songs added while open still show up. If not,
      the inotify watch limit may need raising (`fs.inotify.max_user_watches`).
- [ ] **The window:** under a tiling compositor (Hyprland) and a regular desktop (GNOME or KDE).
      Closing it with no title bar (#55).
- [ ] **Fonts:** the readouts fall back to DejaVu Sans Mono and still line up.
- [ ] AAC songs are listed and say why they can't play.
