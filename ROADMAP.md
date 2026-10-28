# GZJam: what it does, and what's next

GZJam is a free DJ app for social dance nights: swing, West Coast, blues, ballroom and more, on
Windows, Mac and Linux. Simple, intuitive and useful: what you need to play music for dancers,
without the clutter. This page is the big picture: what
GZJam can do today, what's coming, and what's being considered. It's brought up to date after each
release.

**Up to date as of 0.9.4** (27 October 2026). [Download the latest release](https://github.com/GemZombie/GZJam/releases/latest) ·
[what changed in each release](CHANGELOG.md) · [ideas and questions](https://github.com/GemZombie/GZJam/discussions)

## What GZJam does today

**Library**
- Your music folders are the library: a folder tree, with search covering everything under the
  folder you pick. Nothing to import, and your music files are never changed.
- Search filters: `bpm:130-150`, `~bpm:140` (within reach of the tempo slider, or as near as you choose), `artist:`,
  `year:`, `label:`, `rating:`, `energy:` and `played:` (`-played:7` leaves out the songs the room heard last week).
- Choose the song list's columns, Grouping and Comment among them (as iTunes, Music and many
  other players keep them in the song files), and what the queue shows of each song.
- Mark songs with a heart or broken heart, an energy level (low, medium, high), and your own labels,
  which show on the decks and queue too, and can be renamed.
- A queue, and a preview player on every song, to listen in the headphones without loading a deck.
  A song added to the queue or a playlist twice is still added, but GZJam says so, with Undo.
  Ctrl+L gives the queue the room in the middle, for a night spent reordering a long one.
- Playlists: plan a set before the night, in order, saved by name. Drag songs onto one, reorder
  it, and add it to the queue. The queue or a night in History can be saved as one. Fold the
  folders away, or drag the playlists' grip, for a longer list of them.
- A song renamed or moved (in a tag editor, say), even to another of your music folders, keeps its
  marks, labels, cues, playlists and plays; one in the queue or on a deck follows it.
- Songs can be dragged straight from the file manager onto a deck or into the queue, even ones
  from outside your music folders (a song on a dancer's USB stick).
- Back up your library (hearts, labels, cues, playlists, History and Settings) to one file, and
  restore it; GZJam also keeps a copy of its own each day it's opened.
- Start over with an empty library whenever you like; the old one is kept, not deleted.
- Album art on the decks and queue (and a library column), from the songs' files or the covers in
  their folders, to see at a glance that the right record is loaded.

**Decks**
- Two decks, or four in the same space.
- Compact decks for a laptop: smaller decks, so the library and queue show more songs. Or hide
  the decks, for the most songs on screen: they sit at the top of the queue, in the order the room
  will hear them, the song it hears now with its play key and time left. A switch in the top bar
  picks full, compact or hidden in one click (or a key, or the grip under the decks), as often as
  you like during a night.
- The deck the room hears has a red on-air bar that fills as the song plays.
- Tempo with key lock, a waveform of the whole song, one-button fade-out.
- Per-song EQ, with a movable mid band, and EVEN to bring an old recording's quiet and loud
  passages closer together.
- A close-up of the song around the playhead, scrolling past beat by beat, to see the end of a
  phrase coming and find the exact spot for a cue or a jump. Zoom with the mouse wheel or a pinch;
  drag it like a record. It glides smoothly, even zoomed in tight.
- The time readout blinks in the last 30 seconds of the song the room hears, as on CDJs.
- Protection against loading a new song over one the room is hearing.
- Songs start at their first note: silence (or applause) at either end is found automatically, or
  set by hand.
- Jumps: skip a part, or play one again, at the end of a phrase, without a gap.
- Cue keys: mark up to four points in a song, then tap a numbered key to go there at once, at the
  moment you choose (wait for the end of a phrase, then go to the outro), on air too.
- Drag a song's start and end, its jumps and its cues along the waveform, by finger or mouse, on
  air too.
- A level meter on each deck, before the crossfader, so a song you're cueing is visibly too quiet
  or too loud before the room hears it.
- Nothing on a deck shifts or resizes as it starts or goes on air.
- BPM detection, with easy fixes when it's wrong, and tap tempo: tap along on a deck while you keep
  mixing.

**Mixing and sound**
- The crossfader only affects the room; each deck has its own cue button for the headphones.
- Phones mix: blend the headphones from what's cued to what the room hears, to keep an ear on the
  dancers while previewing the next song.
- Speakers and headphones on separate outputs, mono by default. On a laptop with one output, a
  splitter cable can carry both (*One output for both* in Settings): the room on the left, your
  headphones on the right.
- A booth output, for a monitor speaker at the DJ table: what the room hears, at its own volume.
- Loudness matching, so quiet old transfers and loud remasters play at about the same level, at a
  level you choose: lower leaves more headroom for a mixer or PA that distorts easily.
- Keys for the decks (play, CUE, load the selected song), the crossfader, auto-play, and MAIN and
  PHONES volume, all with Ctrl (Cmd on a Mac).
- A level meter and a safety limiter for the speakers.
- Auto-play through the queue, with a gap of real silence between songs. Through the gap the
  crossfader slides over to the next deck, which counts down to starting. For classes and practice
  nights it can fade each song out after a set time, or wait for you between songs; and it can
  stop at a set time, so the night ends on time.
- On Windows the speakers can be GZJam's alone, so no notification sound reaches the room. On Linux
  a sound card can be used directly, for the same reason.
- Headphones (or the booth) unplugged during a night go quiet, never move to the speakers, and come
  back by themselves when plugged in again.

**During the night**
- History: every night saved as a session, with set lists to export as text or a spreadsheet.
- GZJam asks before closing while the room is hearing music. Settings has a Quit key too, for Linux
  desktops that draw no title bar.
- If GZJam crashes or the laptop restarts mid-dance, the night comes back: the queue and the decks
  as they were, paused. After a normal close they come back too, each deck at the start of its song.
- With two taps to pause or fade out the deck the room hears, a stray tap (even while the screen is frozen)
  never stops the music.
- Touch: everything works by finger, with a touch mode for bigger keys and rows, and grips on the
  dividers between panels. A finger held on a position bar moves the song only when it lets go.
- Fold the library or the queue away into a slim strip, which a tap opens again.
- Full screen with F11 (on a Mac, also Ctrl+Cmd+F), and Settings with Ctrl+, or Ctrl+O (Cmd on a
  Mac).
- On Windows and Mac, GZJam's own bar is the window's title bar, leaving more room for the decks
  and library.
- GZJam says quietly, at startup, when a new version is out (never during a set), and a
  [user guide](docs/GUIDE.md) covers everything, from your first song to plugging into the venue's
  mixer or PA and a checklist for the night.

**Coming from other players**
- From Mixxx: ratings, crates (as labels), playlists, history, locked BPMs and hot cues.
- From MediaMonkey, foobar2000, iTunes and most other players: playlists saved as M3U, and the star
  ratings they keep in the song files.
- The other player's files, and your music, are never changed.

Plays MP3, FLAC, WAV, AIFF, OGG Vorbis, Opus and Apple Lossless everywhere, and AAC on Windows and Mac.

## Finished, coming in the next release

Nothing yet since 0.9.4.

## Planned next

**Before 1.0:** a reliable night, everywhere.

*Fixes:* none left. [A rare dropout with extra crackle protection on](https://github.com/GemZombie/GZJam/issues/47)
couldn't be reproduced in 170 runs on five sound cards; if the activity log ever says the speakers
ran dry, it's reopened.

*Testing*
- A test pass with [the checklist](docs/TESTING.md) [on Linux](https://github.com/GemZombie/GZJam/issues/19),
  with a sound card used directly, as at a dance
- The booth and the splitter cable by ear, a library of 50-60 thousand songs, and a real night
  with dancers
- With *Extra protection against crackles* on, a night with no "the sound buffer ran dry" in the
  activity log
- Done: [the checklist on a real Mac](https://github.com/GemZombie/GZJam/issues/18), and an hour's
  mock night on Windows and on Mac

**After 1.0:**
- [Restore a backup on a computer where the music lives somewhere else](https://github.com/GemZombie/GZJam/issues/112),
  for a new laptop with a different drive or system
- [iTunes / Music as a library source](https://github.com/GemZombie/GZJam/issues/97), kept in
  sync: its playlists, ratings, play counts and Grouping, next to your music folders
- [Group different versions of the same song](https://github.com/GemZombie/GZJam/issues/9)
- [Edit tags in GZJam](https://github.com/GemZombie/GZJam/issues/129): kept in GZJam's library
  only, or written to the files too if you turn that on
- [EQ a song that isn't on a deck](https://github.com/GemZombie/GZJam/issues/137), from the
  library, queue or a playlist
- [Show and search any tag in the song files](https://github.com/GemZombie/GZJam/issues/119), not
  only the usual ones (a dance in a custom tag, the composer)
- [A microphone or line input, with talkover](https://github.com/GemZombie/GZJam/issues/121), for a
  laptop plugged straight into a PA with no mixer
- [Hints for new DJs](https://github.com/GemZombie/GZJam/issues/123), quiet and optional: the same
  tempo for too long, a repeat, the limiter held on
- [Rip CDs straight into the library](https://github.com/GemZombie/GZJam/issues/41), named and tagged.
  Not something DJ apps usually do, and good free rippers exist (Exact Audio Copy, fre:ac,
  foobar2000), whose files GZJam picks up by itself.

## Being considered

Requests and ideas, in no particular order. Each links to its discussion; add a 👍 or a comment
to one you'd use.

**Library**
- [Smooth with very large libraries](https://github.com/GemZombie/GZJam/issues/73) of several
  hundred thousand songs or more
- [Compact library](https://github.com/GemZombie/GZJam/issues/75): see what's still kept for songs
  that are gone, and clean it up


**Playing**
- [Rearrange and resize the queue's columns](https://github.com/GemZombie/GZJam/issues/188), as in
  the library
- [Customizable keyboard shortcuts](https://github.com/GemZombie/GZJam/issues/135)
- [Color songs by rules](https://github.com/GemZombie/GZJam/issues/124): BPM ranges, energy, a
  label, or any search
- [Export a playlist as files](https://github.com/GemZombie/GZJam/issues/101), to a folder or USB
  stick, to hand a set to another DJ
- [Dance types on songs, for ballroom](https://github.com/GemZombie/GZJam/issues/103): playlists by
  a sequence of dances, or so many of each

**Elsewhere**
- [Music from your own server](https://github.com/GemZombie/GZJam/issues/184) (Navidrome and
  other Subsonic servers), copied to the laptop ahead of the night, for a laptop too small for the
  whole library
- [Two rooms](https://github.com/GemZombie/GZJam/issues/120): two GZJams on the same network, for
  schools with two floors, either one controlling the other
- [GZJam in other languages](https://github.com/GemZombie/GZJam/issues/102)
- [A companion app for cleaning up tags and file names](https://github.com/GemZombie/GZJam/issues/76):
  fill in missing titles, fix misspelled artists, rename files by rule
- [A tablet version](https://github.com/GemZombie/GZJam/issues/16), iPad first
- [An Arch Linux (AUR) package that updates itself](https://github.com/GemZombie/GZJam/issues/30)
  (waiting on the AUR)

## Decided against

- **Streaming services** (Spotify, Apple Music, TIDAL, Beatport): DJ streaming is only open to
  partner companies, and Spotify's terms forbid mixing its music. (Your own music on your own server is
  different: see [#184](https://github.com/GemZombie/GZJam/issues/184).)
- **DJ controllers (MIDI), for now:** they're made for beatmatching, scratching and live effects,
  and GZJam aims to stay simple: choosing the right song and playing it well. A controller's own sound card
  works as an output already. If you'd use one with GZJam, say so in
  [Ideas](https://github.com/GemZombie/GZJam/discussions/categories/ideas).

Everything is tracked in [GitHub issues](https://github.com/GemZombie/GZJam/issues); the reasoning
behind each decision is in [docs/FEATURES.md](docs/FEATURES.md).
