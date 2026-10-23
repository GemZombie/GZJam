# GZJam: what it does, and what's next

GZJam is a free DJ app for Windows, Mac and Linux that aims to be simple, intuitive and useful:
what you need to play music for people, without the clutter. This page is the big picture: what
GZJam can do today, what's coming, and what's being considered. It's brought up to date after each
release.

**Up to date as of 0.3.0** (22 October 2026). [Download the latest release](https://github.com/GemZombie/GZJam/releases/latest) ·
[what changed in each release](CHANGELOG.md) · [ideas and questions](https://github.com/GemZombie/GZJam/discussions)

## What GZJam does today

**Library**
- Your music folders are the library: a folder tree, with search covering everything under the
  folder you pick. Nothing to import, and your music files are never changed.
- Search filters: `bpm:130-150`, `~bpm:140` (within reach of the tempo slider), `artist:`,
  `year:`, `label:`, `rating:`, `energy:` and `played:` (`-played:7` leaves out the songs the room heard last week).
- Mark songs with a heart or broken heart, an energy level (low, medium, high), and your own labels.
- A queue, and a preview player on every song, to listen in the headphones without loading a deck.

**Decks**
- Two decks, or four in the same space.
- Tempo with key lock, a waveform of the whole song, one-button fade-out, per-song EQ.
- Protection against loading a new song over one the room is hearing.
- Songs start at their first note: silence (or applause) at either end is found automatically, or
  set by hand.
- Jumps: skip a part, or play one again, at the end of a phrase, without a gap.
- BPM detection, with easy fixes when it's wrong.

**Mixing and sound**
- The crossfader only affects the room; each deck has its own cue button for the headphones.
- Speakers and headphones on separate outputs, mono by default.
- Loudness matching, so quiet old transfers and loud remasters play at about the same level.
- A level meter and a safety limiter for the speakers.
- Auto-play through the queue, with a gap of real silence between songs.
- On Windows the speakers can be GZJam's alone, so no notification sound reaches the room. On Linux
  a sound card can be used directly, for the same reason.

**During the night**
- History: every night saved as a session, with set lists to export as text or a spreadsheet.
- GZJam asks before closing while the room is hearing music.
- Touch: everything works by finger, with a touch mode for bigger keys and rows.

**Coming from Mixxx**
- Import ratings, crates (as labels), history and locked BPMs. Mixxx's own files are never changed.

Plays MP3, FLAC, WAV, AIFF, OGG Vorbis, Opus and Apple Lossless everywhere, and AAC on Windows and Mac.

## Finished, coming in 0.4.0

- **A level meter on each deck**, showing its level before the crossfader, so a song you're cueing
  is visibly too quiet or too loud before the room hears it.
- **Cue keys:** mark up to four points in a song, then tap a numbered key to go there at once,
  at the moment you choose (wait for the end of a phrase, then go to the outro). Hot cues come
  over from Mixxx.
- **Phones mix:** blend the headphones from what's cued to what the room hears, to keep an ear on
  the dancers while previewing the next song.
- **Nothing moves:** nothing on a deck shifts or resizes as it starts or goes on air, and with four
  decks all four are the same height.
- **One bar at the top** on Windows and Mac, for more room.

## Planned next

- [Quieter logs on Linux](https://github.com/GemZombie/GZJam/issues/21) (0.4.0)
- [Testing on Linux](https://github.com/GemZombie/GZJam/issues/19) and [on a real Mac](https://github.com/GemZombie/GZJam/issues/18)

## Being considered

Requests and ideas, in no particular order. Each links to its discussion; add a 👍 or a comment
to one you'd use.

**Finding the right spot in a song**
- [A zoomed, scrolling waveform](https://github.com/GemZombie/GZJam/issues/14), for setting
  points precisely
- [Drag the start, end and jump markers](https://github.com/GemZombie/GZJam/issues/12), by
  finger or mouse
- [Tap tempo](https://github.com/GemZombie/GZJam/issues/11), to set a BPM by tapping along

**Library**
- [Ordered, saved playlists](https://github.com/GemZombie/GZJam/issues/6)
- [Group different versions of the same song](https://github.com/GemZombie/GZJam/issues/9)
- [Rename labels, and show them on the queue and decks](https://github.com/GemZombie/GZJam/issues/13)
- [Choose how close `~bpm:` searches are](https://github.com/GemZombie/GZJam/issues/7)

**Sound and outputs**
- [A booth (monitor) output](https://github.com/GemZombie/GZJam/issues/8)
- [Speakers and headphones from one output](https://github.com/GemZombie/GZJam/issues/15), with a
  splitter cable, for laptops and tablets
- [A one-knob compressor for old recordings](https://github.com/GemZombie/GZJam/issues/5)
- [A sweepable mid EQ](https://github.com/GemZombie/GZJam/issues/4)

**Elsewhere**
- [A tablet version](https://github.com/GemZombie/GZJam/issues/16), iPad first
- [An Arch Linux (AUR) package that updates itself](https://github.com/GemZombie/GZJam/issues/30)
  (waiting on the AUR)
- [A better logo](https://github.com/GemZombie/GZJam/issues/17)

## Decided against

- **Streaming services** (Spotify, Apple Music, TIDAL, Beatport): DJ streaming is only open to
  partner companies, and Spotify's terms forbid mixing its music.

Everything is tracked in [GitHub issues](https://github.com/GemZombie/GZJam/issues); the reasoning
behind each decision is in [docs/FEATURES.md](docs/FEATURES.md).
