# GZJam Feature Spec

Living document. Requirements come from how the DJ actually works; anything not listed here
is out of scope until it earns a place.

## Prior art

- **BPM Studio** (ALCATech): used in the past. Expensive and dated now.
- **Mixxx**: used, but its library support fell short of folder-based browsing and search.
- **DJay Pro**: the look worth keeping; search only works within the selected folder, not recursively.

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

### Status: built (step 1)

- Folder tree, recursive scoped search, drag / right-click / Enter to queue, queue reordering,
  "Show in folder tree", folder watching, remembered window and selected folder.
- Indexes MP3, MP2, OGG, WAV and AIFF (what BASS plays without add-ons). FLAC/M4A later if needed.
- Skips macOS `._name.mp3` metadata files (24 of them in the real library).

### What the real library looks like (12,686 tracks in `C:\music`)

- First scan 2.6 s, rescan 0.45 s; searches take 0.3-8 ms.
- Tags: title/artist 99%, album 95%, genre 86%, **year 14%**, **BPM essentially none**
  (28 tracks, stored x100, now scaled back). **BPM detection is essential**, not optional.
- Comments (53%) mostly hold artist names; iTunes data frames are filtered out.

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

### Status: analysis built (rest of step 3, plus waveforms from step 4), 2026-10-21

- **One decode per track** (`TrackAnalyzer`) gives the overview waveform, the audible start and
  end, and the BPM. About 0.4 s per track per thread; the whole 12,686-track library takes
  roughly 25-40 minutes once, on 1-3 below-normal-priority threads. Results are saved in the
  library database (keyed by path, invalidated when the file's size or date changes).
- **Order:** a track loaded onto a deck is analyzed first, then queued tracks, then the rest of
  the library in folder order. Progress shows in the top bar ("Analyzing tracks… n / total").
- **Silence:** 50 ms windows; silence is anything 40 dB below the track's loud passages (95th
  percentile) or below -60 dBFS. 50 ms is kept before the first note and 250 ms after the last.
  On the real library the median trim is 0.2 s at the start and 3 s at the end (old transfers
  often carry seconds of hiss). Checked against loudness plots of real endings: fades and final
  chords are kept.
- **Decks use the trims:** a loaded track is cued at its first note; it counts as finished
  (so the gap starts) where the music ends; "time left" counts down to there. The waveform shades
  the trimmed parts.
- **BPM** is our own detector (`TempoDetector`): spectral-flux onsets, autocorrelation across
  1-4 beats, then a swing-aware half/double-time check (a regular pulse midway between the
  slow beats means they're really two beats; a ballad's swung eighths fall at 2/3, not 1/2).
  BASS_FX's detector got 5 of the library's 28 tagged tracks right; ours gets 19 within 4%,
  with no half/double errors, and the rest mostly 5-7% off (the same recording measures
  steadily, so likely tag inaccuracy). Detected BPMs are shown as whole numbers.
- **Fixing a wrong BPM, per track (the DJ's request, from another app they used):** right-click
  the BPM on a deck, or a track in the library or queue › *Detect BPM in range*: Automatic,
  Slow (60-110), Medium (100-160), Up-tempo (140-220), Fast (200-320). The track is analyzed
  again and the strongest tempo inside the range is chosen (if none is, the detector's choice is
  doubled or halved into it), which fixes 3:2 mistakes as well as half/double time. Each range
  is under double, so there's one answer. Several selected tracks (e.g. a Balboa album) can be
  set at once. The choice is saved per file (it survives re-analysis), and with a range chosen
  the detected BPM is shown even over a tag's BPM; Automatic goes back to the tag.
- A global "my music's tempo range" setting was tried first and dropped: the DJ felt one range
  for the whole library wouldn't fit, and wanted to fix individual songs instead.
- Tags storing BPM x10 ("1320") are now read as 132.0, as well as x100.
- **Waveform:** 1,000 slices per track; bars show loudness (RMS, solid) with peaks as a faint
  outline, coloured by frequency (red bass, green mids, blue highs). The played part is dimmed.
- Not yet: adjusting trim points by hand, typing or tapping a BPM, per-folder BPM ranges,
  a zoomed scrolling waveform, loudness matching.

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
- **Drag and right-click are redundant by design.** Every track action is available both ways.
  Right-click on a track (library or queue): *Preview in headphones*, *Load to Deck 1/B…*
  (instant), *Add to queue*, *Play next*.
- **Auto-play alternates decks** (A, B, A, B…). The **crossfader fades the room between the
  decks**; auto-play moves it to the new deck when the next song starts.
- **One-button fade-out** per deck, with a configurable length. A fade-out counts as the song
  ending: the gap starts, then auto-play continues.

### Deck protection

- **A playing deck is protected.** By default, loading a track onto a deck that is playing is
  blocked, whether by drag or by right-click. A paused or stopped deck accepts loads normally.
- Blocked loads are visible, not silent: the right-click item is greyed out ("playing"), and
  dragging over the deck shows a "not allowed" outline and cursor.
- **Setting: "Loading onto a playing deck"**: *Block* (default), *Ask to confirm*, or *Allow*.

### Deck display

- **The big readout cycles when clicked**: time remaining → time elapsed → BPM (tempo-adjusted)
  → … and back. Each deck remembers its mode between sessions.

### Status: step 2 built

- Two decks with play/pause, seek, tempo (key lock), fade-out, eject and a tap-to-cycle readout
  (time left / elapsed / BPM, remembered per deck).
- Load by dragging onto a deck or right-click › Load to Deck 1/B; deck protection with the
  Block / Ask / Allow setting, shown on the deck outline and in the menu ("(playing)").
- Pause is a single tap by default. Optional "tap twice (or press and hold)" for a deck the room
  is hearing. Seeking is blocked while the room is hearing the deck.
- Crossfader with one-tap A / B switches (a 300 ms glide); shows "BLEND" when the room hears both.
- A daily activity log (`logs/` next to the library database) records plays, pauses, crossfader
  switches, output changes and errors, so problems at a gig can be traced afterwards.

### Status: auto-play built (part of step 3)

- The switch works whenever there's something to play (queued tracks or a loaded deck).
- Turning it on with empty decks loads the top of the queue onto the crossfader's deck and
  starts it, and loads the next track onto the other deck. Tracks leave the queue as they load.
- When the song the room is hearing ends or is faded out: wait the gap, start the other deck,
  move the crossfader to it, refill the finished deck from the queue. Stops (with a notice)
  when the queue runs out. Turning it off never touches the current song.
- A song that runs out in the headphones only isn't "used": it rewinds and stays next.
- Gap: − / + in the mixer (0-15 s, 0.5 s steps), remembered. Measured from where the music
  ends (silence trimming), not the end of the file.
- The crossfader's last 4% at each end snaps to the end; a deck counts as "in the room" from
  about -20 dB; tapping a locked position bar says how to unlock it.

### Changed after the first hardware test (2026-10-20)

- The DJ hit two problems: a deck that seemed impossible to pause (the then-default
  press-and-hold to pause the live deck is awkward with trackpad tap-to-click, and its hint was
  easy to miss), and confusion from the crossfader also driving the headphones.
- **Decision: the crossfader is only a crossfade between the decks for the room.** The
  headphones are chosen per deck with a cue button. Pause protection is off by default.
- Auto-play, the gap, silence trimming, BPM detection and waveforms have since been built (see
  Track prep).

## Queue as a crate

- In manual DJing the queue is a "box" of songs being considered. Tracks can be dragged (or
  right-click loaded) from it onto a deck; once loaded they leave the queue, as with auto-play.

## History

### Requirements (from the DJ)

- An automatic history of the current session, saved, so any past date can be looked up.

### Design decisions (built)

- **A song counts as played once the room has heard it for 30 seconds**, or if a shorter song
  played to its end in the room. Headphone cueing, previews, seeks and songs faded out early
  don't count. Measured in track time, so pauses don't count either.
- **Sessions are gigs.** A session starts with its first song; restarting GZJam within 3 hours of
  the last song continues it, so a crash or restart doesn't split the night.
- **History tab** next to Queue: a session drop-down ("This session", then past ones by date),
  time on air, title, artist, BPM (tempo-adjusted) and length. Entries can be previewed, dragged
  onto a deck or into the queue, loaded via right-click, or shown in the folder tree.
- Stored in the library database (schema upgrade 2) with its own copy of title/artist/album, so
  old sessions stay readable after files move or leave the library.
- Later: mark tracks already played tonight in the library list; export a set list.

## Headphone cue

### Requirements (from the DJ)

- **Preview is essential**, directly from the library file list and from the queue.
- **The crossfader is not part of cue** (revised after hardware testing). It only fades the room
  between the decks.

### Design decisions

- **Each deck has a cue (🎧) button** that sends it to the headphones, whatever the crossfader is
  doing. Off by default; it stays as set when a new track is loaded.
- **A preview player** (headphone icon on every library and queue row) plays a track to the
  cue output without loading a deck, with its own scrub bar.
- **Built on BASSmix**: every deck feeds both a main and a cue mixer through splitters. The
  crossfader sets each deck's main volume (equal-power curve); the cue button switches its cue
  volume. Neither interrupts playback.
- The headphones are never defaulted to the speaker device: with no cue output chosen, preview
  asks you to pick one in Settings rather than playing into the room.
- The preview player (headphone icon on every library and queue row, or Space) has its own seek
  bar; a second tap on the same track stops it.

## Touch

### Requirements (from the DJ)

- **The DJ uses a touchscreen laptop.** Touch must be first-class, not an afterthought.
- A **tablet version** is a possible future direction.

### Design decisions

- **Every action works by touch.** Long-press acts as right-click (opens the context menu).
- **Swipes don't fight scrolling** *(built)*. In the library list a vertical swipe scrolls and a
  sideways swipe (towards the queue and decks) drags the track. In the queue, reorder by dragging
  the ⋮⋮ handle; swiping elsewhere scrolls. With a mouse, dragging works from anywhere.
- **No hover-only features.** Anything needed during a set is visible without a mouse;
  tooltips are extras, never the only way to discover something.
- **Touch works everywhere, all the time.** Gestures (long-press, swipes, drag handles) never
  depend on a mode.
- **"Touch mode" is a manual toggle that mostly resizes**: finger-sized targets (about 44 px),
  taller rows, a roomier folder tree, bigger buttons. Sizes come from a shared set of
  size resources, so the toggle swaps one set for another rather than restyling each control.
  New controls (from step 2 on) take their sizes from those resources.
- **Why it matters:** today the DJ works mostly by trackpad, which can be unreliable mid-set;
  touch is meant to be the more dependable option.
- **Deck controls designed for fingers** (step 2): large play/pause, a big crossfader handle,
  tap the readout to cycle it, drag on the cue deck's waveform to scrub.
- **Accidental touches on the live deck are guarded.** Deck protection already blocks loads;
  pausing or stopping the live deck should also need a deliberate action (setting).

### Future tablet version (not planned yet, but don't paint ourselves into a corner)

- **iPad first**, then probably Android too.
- Keep platform-specific code at the edges: view models, library and audio engine stay free of
  Windows-only APIs (opening Explorer, folder watching, file pickers sit behind small seams).
- Building for iPad needs a Mac with Xcode and an Apple Developer account ($99/year) to install
  beyond 7-day test builds.
- BASS licensing: free as long as GZJam makes no money (no sales, no ads), on iPad too. If it
  is ever sold, licences are per platform (e.g. iOS single commercial €475, or the €125 shareware
  licence for low-priced apps). Checked against un4seen.com, October 2026.

- Avalonia runs on iPad and Android, and BASS has iOS and Android builds, so the engine and most
  of the UI could be shared.
- **Tablets usually have one audio output.** Headphone cue would need either a USB interface
  with two outputs, or a **split output** mode (main in mono on the left channel, cue on the right,
  via a splitter cable), as DJay Pro does. The step-2 mixer should allow for split output.
- A tablet library can't simply mirror a Windows folder tree; storage works differently there.

## Look and feel

- **The UI must look good.** This is the part of DJay Pro and similar apps worth keeping:
  a polished, dark, performance-oriented look with waveforms and clear deck displays.
  Fewer features does not mean plainer.
- **Layout approved** as in `docs/mockups/main-screen.html`: decks on top with a mixer column
  between them; folder tree, search results and queue below.
- **Fonts stay consistent across the app for now.** The deck readout font (especially time
  left) may be revisited once it's on screen in the real app.

## Decided against

- **Streaming from Spotify (or Apple Music, TIDAL, Beatport).** DJ streaming is only available to
  partner companies (rekordbox, Serato, djay since September 2025); Spotify's public developer
  policy forbids mixing/segueing its content, and its audio can't be routed through our mixer.
  A playlist-matching feature (Spotify playlists → tracks in the local library) was possible but
  isn't useful without streaming. Revisit only if the streaming landscape changes.

## Open questions


- What worked well in BPM Studio that should carry over?
