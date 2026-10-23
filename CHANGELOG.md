# What's new in GZJam

What changed in each release, newest first. Downloads are on the
[Releases page](https://github.com/GemZombie/GZJam/releases).

<!--
Before tagging a release, add its section here: "## <version> (<date>)", then what a DJ would
notice, under "New", "Changed" and "Fixed" as needed. The release workflow refuses a version that
has no section, and uses the section as the release's notes (tools/release-notes.sh).
-->

## 0.3.0 (unreleased)

New:

- **Read all tags again** (right-click a music folder in the library): for tags edited with a
  program set to keep the file's date, which GZJam wouldn't otherwise notice. GZJam also rereads
  every song by itself whenever it gets better at reading tags.

Fixed:

- **BPMs over 1000** (Pink Panther Theme showed 1180) for songs whose tags store the BPM ten times
  over, in libraries first scanned before 0.2.1. They're corrected once when GZJam starts, in the
  library and in History.

## 0.2.1 (2026-10-22)

The first public release. (0.2.0 was never released: a test failed on the Linux build machine.)

- **Your music folders as the library:** a folder tree, search that covers everything under the
  folder you pick, and filters like `bpm:130-150`, `~bpm:140`, `artist:`, `year:`, `label:`,
  `rating:`, `energy:` and `played:`.
- **Two or four decks:** tempo with key lock, one-button fade-out, waveforms, and protection against
  loading over a song the room is hearing.
- **Queue and auto-play** with a gap of real silence between songs (silence at the start and end of
  each file is found automatically).
- **Headphones:** a cue button per deck and a preview player on every song. The crossfader only
  affects the room.
- **Sound:** mono for speakers and headphones (on by default), loudness matching, per-song EQ,
  speaker and headphone volume, a level meter and a safety limiter.
- **Marking songs:** a heart or broken-heart rating, an energy mark, and labels.
- **History:** every night saved as a session, with set lists to export as text or a spreadsheet.
- **Track prep:** BPM detection with easy fixes, start and end points set by hand, and jumps (skip
  ahead or repeat a part at the end of a phrase).
- **Import from Mixxx:** ratings, crates (as labels), history and locked BPMs. Mixxx's own files are
  never changed.
- **Touch:** everything works by finger, with a touch mode for bigger keys and rows.
- **Outputs:** speakers and headphones on separate outputs. On Linux, a sound card can be used
  directly so other programs can't reach the PA, and outputs have clear names ("Built-in: HDMI 1",
  "Cable Creation (USB)").
- **Settings** in one place, by section: Sound, Decks, Screen, Library and About.

Plays MP3, FLAC, WAV, AIFF, OGG Vorbis, Opus and Apple Lossless everywhere, and AAC on Windows and
Mac.
