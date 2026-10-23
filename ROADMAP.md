# GZJam: what it does, and what's next

GZJam is a free DJ app for Windows, Mac and Linux that aims to be simple, intuitive and useful:
what you need to play music for people, without the clutter. This page is the big picture: what
GZJam can do today, what's coming, and what's being considered. It's brought up to date after each
release.

**Up to date as of 0.4.0** (23 October 2026). [Download the latest release](https://github.com/GemZombie/GZJam/releases/latest) ·
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
- Cue keys: mark up to four points in a song, then tap a numbered key to go there at once, at the
  moment you choose (wait for the end of a phrase, then go to the outro), on air too.
- A level meter on each deck, before the crossfader, so a song you're cueing is visibly too quiet
  or too loud before the room hears it.
- Nothing on a deck shifts or resizes as it starts or goes on air.
- BPM detection, with easy fixes when it's wrong.

**Mixing and sound**
- The crossfader only affects the room; each deck has its own cue button for the headphones.
- Phones mix: blend the headphones from what's cued to what the room hears, to keep an ear on the
  dancers while previewing the next song.
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
- On Windows and Mac, GZJam's own bar is the window's title bar, leaving more room for the decks
  and library.

**Coming from Mixxx**
- Import ratings, crates (as labels), history, locked BPMs and hot cues. Mixxx's own files are
  never changed.

Plays MP3, FLAC, WAV, AIFF, OGG Vorbis, Opus and Apple Lossless everywhere, and AAC on Windows and Mac.

## Finished, coming in the next release

- **A close-up of the song on each deck:** a few seconds around the playhead, scrolling past
  beat by beat, over the whole song. See the end of a phrase coming, and find the exact spot for a
  cue, a jump or a song's start. Zoom with the mouse wheel or a pinch; drag it like a record.
- **Drag the markers:** move a song's start and end, its jumps and its cues by dragging them on
  the close-up or the whole song, by finger or mouse, on air too.
- **Tap tempo:** set a song's BPM by tapping along to it on a deck, while you keep mixing.
- **A new logo and app icon:** a waveform wrapped into a G, and the name in letters cut into
  bars like a waveform.

## Planned next

**0.5.0: setting points precisely.** Cue keys, jumps and a song's start and end are only as good
as where they're put; finding the exact spot, by mouse or finger, is the focus.
- [Rename labels, and show them on the queue and decks](https://github.com/GemZombie/GZJam/issues/13)
- [Choose how close `~bpm:` searches are](https://github.com/GemZombie/GZJam/issues/7)

**After that, 0.6.0:**
- [Rip CDs straight into the library](https://github.com/GemZombie/GZJam/issues/41), named and tagged
- [Album art on the decks and queue](https://github.com/GemZombie/GZJam/issues/44), small, to see at
  a glance that the right record is loaded

**Any time:** [testing on Linux](https://github.com/GemZombie/GZJam/issues/19) and
[on a real Mac](https://github.com/GemZombie/GZJam/issues/18), and [checking for a rare dropout with extra
crackle protection on](https://github.com/GemZombie/GZJam/issues/47) (Windows).

## Being considered

Requests and ideas, in no particular order. Each links to its discussion; add a 👍 or a comment
to one you'd use.

**Decks**
- [Smoother close-up scrolling when zoomed in tight](https://github.com/GemZombie/GZJam/issues/46)

**Library**
- [Ordered, saved playlists](https://github.com/GemZombie/GZJam/issues/6)
- [Group different versions of the same song](https://github.com/GemZombie/GZJam/issues/9)

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
- [A startup screen with the logo](https://github.com/GemZombie/GZJam/issues/49), while GZJam gets
  ready

## Decided against

- **Streaming services** (Spotify, Apple Music, TIDAL, Beatport): DJ streaming is only open to
  partner companies, and Spotify's terms forbid mixing its music.

Everything is tracked in [GitHub issues](https://github.com/GemZombie/GZJam/issues); the reasoning
behind each decision is in [docs/FEATURES.md](docs/FEATURES.md).
