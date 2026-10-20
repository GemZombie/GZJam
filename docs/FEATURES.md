# GZJam Feature Spec

Living document. Requirements come from how the DJ actually works; anything not listed here
is out of scope until it earns a place.

## Library

### Requirements (from the DJ)

- The music is organized on disk as `genre / style / artist / album / song.mp3`.
  The app must show the library **the same way**, as a folder tree, in at least one view.
- **Search is recursive.** Searching with a folder selected covers everything beneath it,
  not just that folder's own files. Searching at the top covers the whole collection.
  (DJay Pro's non-recursive search forced everything into one giant folder; we must not.)
- Search results are dragged into the queue.
- About 13,000 tracks today, mostly MP3, and growing. Files are rarely reorganized;
  albums and songs are added from time to time.

### Design decisions

- **The file system is the source of truth for organization.** The app never moves or renames
  files. Its database is an index over the folders plus data the app owns (BPM overrides,
  trim points, notes, play history).
- **MP3 tags are the source of truth for track data.** Columns (title, artist, album, genre,
  year, BPM, comment) come from tags. Folders drive the tree view and also feed search.
- **One or more root folders** can be added; each appears as a top-level node in the tree.
- **Selected folder = search scope.** Select `Lindy Hop` and type `basie` to get every Basie
  track anywhere under `Lindy Hop`. Select the root (or clear the selection) to search everything.
- **Folder names are searchable** alongside tags, so `balboa` finds tracks in a `Balboa`
  folder even if their tags don't say so.
- **Built for 100,000+ tracks.** Indexed search (SQLite FTS5) updates as you type.
- **Rescan on start, and watch folders for changes** while running, so new albums added in
  Explorer show up without a manual import. Tracks are keyed by path; since files rarely
  move, a moved file is simply treated as new (no fingerprinting needed for now).

## Track prep

### Requirements (from the DJ)

- **Configurable gap between songs.** Some DJs want the next song almost immediately;
  others (including this DJ) want a few seconds of silence. The gap is a setting, and 0 is allowed.
- **The gap should be consistent**, regardless of how much silence is baked into each file.

### Design decisions

- **Automatic silence trimming.** Each track is analyzed once for its *audible start* and
  *audible end*. Playback starts at the audible start; the gap timer starts at the audible end.
  So a 3-second gap is 3 seconds of real silence between songs, every time.
- **Conservative threshold, manual override.** Quiet endings (soft piano tags, long fades) must
  not get clipped, so detection errs toward keeping audio. Trim points can be adjusted per track
  and are saved.
- **One analysis pass per track** computes trim points and BPM (and later, loudness). It runs in
  the background on first import (13,000 tracks will take a while once), then only for new files.
  Results are cached in the database, so the library is usable immediately while analysis runs.
- A BPM already in the tags (ID3 `TBPM`) is used rather than re-detected.

## Decks, queue and auto-play

### Requirements (from the DJ)

- **At least 2 decks.** Some DJs want 4, so deck count should be an option later.
- **Drag tracks into the queue.**
- **Auto-play toggle.** On: when the current song ends, wait the gap, then play the next song in
  the queue. Off: the current song keeps playing to its end, but nothing else starts.
- **Manual mode.** Ability to DJ one song at a time on the decks without using the queue.

### Design decisions

- Turning auto-play off mid-song never affects the current song; it only stops the next one
  from starting.
- With auto-play on, the next queued track is preloaded onto the idle deck, so it starts
  instantly and you can see exactly what's coming.
- Tracks can also be dragged straight onto a deck (manual mode).
- Deck count is configurable in the engine from the start (2 by default); the 4-deck UI comes later.

## Look and feel

- **The UI must look good.** This is the part of DJay Pro and similar apps worth keeping:
  a polished, dark, performance-oriented look with waveforms and clear deck displays.
  Fewer features does not mean plainer.

## Open questions

- Headphone routing: should each deck have a "send to headphones" toggle (like a mixer's cue
  button), and/or should the library have a one-click preview player that plays straight to the
  headphones without loading a deck?
- With auto-play on, should the next song alternate between decks (A, B, A, B...)?
- Is a one-button fade-out wanted for cutting long songs short?
