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
- One click or tap on a folder opens it as well as choosing it; a click on the chosen folder
  closes it. The arrow's target is the whole space before the name, and opens or closes a folder
  without choosing it.
- Plays MP3, MP2, OGG Vorbis, WAV and AIFF (BASS itself), FLAC, Opus and Apple Lossless (un4seen's
  BASSFLAC, BASSOPUS and BASSALAC add-ons, beside bass), and AAC in `.m4a`/`.aac` through the
  computer's own decoder: Media Foundation on Windows (missing from "N" editions), Core Audio on a
  Mac (which also plays ALAC, so there's no Mac bassalac). **Linux has no AAC:** the add-on for it,
  BASS_AAC, is GPL and third-party, so it isn't included. AAC songs are still listed there, and
  say why when loaded ("it's AAC, which GZJam can't play on Linux"). Tests decode a two-second tone
  in each format on all three systems (`tests/Shared/Formats`, made with ffmpeg).
- Skips macOS `._name.mp3` metadata files (24 of them in the real library).
- **Reading tags again** (2026-10-23, issue #26): each song remembers which way of reading tags it
  was read with (`TagReader.Version`, schema upgrade 14); raising it makes the next scan read every
  song again, so a fix in tag reading reaches libraries scanned before it. Right-click a folder ›
  *Read all tags again* does the same by hand, for tags edited by a program that keeps the file's
  date (Mp3tag can). About 3 s for 12,700 songs. The DJ's own data (ratings, labels, EQ, trims,
  History) is kept by path and untouched.

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
  the BPM on a deck, or a track in the library or queue › *BPM*: Automatic,
  Slow (60-110), Medium (100-160), Up-tempo (140-220), Fast (200-320). The track is analyzed
  again and the strongest tempo inside the range is chosen (if none is, the detector's choice is
  doubled or halved into it), which fixes 3:2 mistakes as well as half/double time. Each range
  is under double, so there's one answer. Several selected tracks (e.g. a Balboa album) can be
  set at once. The choice is saved per file (it survives re-analysis), and with a range chosen
  the detected BPM is shown even over a tag's BPM; Automatic goes back to the tag.
- A global "my music's tempo range" setting was tried first and dropped: the DJ felt one range
  for the whole library wouldn't fit, and wanted to fix individual songs instead.
- Tags storing BPM x10 ("1320") are now read as 132.0, as well as x100. Libraries scanned before
  that kept the raw value (a rescan skips unchanged files): schema upgrade 13 corrects them once,
  in the library and History (issue #26).
- **Waveform:** 1,000 slices per track; bars show loudness (RMS, solid) with peaks as a faint
  outline, coloured by frequency (red bass, green mids, blue highs). The played part is dimmed.
- Not yet: typing or tapping a BPM, per-folder BPM ranges,
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
- Deck count is configurable: 2 by default, or 4 (Settings › *Four decks*); see the four-deck status below.
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
- **Session menu** (the ⋯ key beside the session picker, built 2026-10-22):
  - *Name this session…*: "Friday Night, Oct 14" (was the NAME key).
  - *Export set list…*: saves where the DJ picks, as readable text (a heading with the name, date,
    times, song count and length, then a numbered line per song: time, title, artist, BPM as
    the room heard it, length) or CSV for a spreadsheet (date, time, title, artist, album, BPM,
    tempo change, length, deck, file; UTF-8 with a byte-order mark for Excel). Suggested name:
    "Set list - Friday Night, Oct 14 - 2026-10-17".
  - *Delete this session…*: asks first; the session and its songs leave History and the Played
    column forgets them. Deleting tonight's session starts a fresh one with the next song.

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

### Status: touch mode and touch dragging built (2026-10-22)

- **Dragging works by touch.** The DJ found tapping worked but dragging didn't: drags used the
  operating system's drag and drop, which on Windows follows the mouse only. GZJam now moves the
  drag itself (`MainWindow.Drag.cs`), the same for finger, pen, trackpad and mouse: a tag with the
  song follows the pointer (above a finger, so it isn't hidden), the deck under it lights up or
  shows it won't take the song, the queue shows a line where it would go and scrolls when held
  near its top or bottom edge, and holding it over the Queue tab opens the queue. Escape cancels.
- **Fixed after the DJ's first try (2026-10-22): drags from library rows "almost never" worked,
  worst on the title.** Found by driving GZJam with real Windows touch input (`InjectTouchInput`,
  on the DJ's touchscreen laptop): the list's own touch scrolling starts after 5 px of movement,
  either way, and from then on keeps the finger's moves to itself, while a drag needs 14 px to be
  told from a scroll. A real finger always wobbles 5 px first. The list's scrolling now waits
  18 px, so GZJam decides first: sideways becomes a drag, up-and-down is handed to the list to
  scroll. And a drag carries the row under the finger (with the rest of the selection if that
  row is selected): a finger's press doesn't always select its row in time. Checked by real touch:
  sideways swipes from Title and Artist to the queue, a vertical swipe scrolling, a held song
  dragged up to a deck, the queue reordered by its grip, a queue song held and dropped on a deck,
  and hold-and-lift opening the menu.
- **Press and hold** (a third of a second) picks a song up: it then drags in any direction,
  straight up to a deck included. Held and lifted without moving, it opens the song's menu, as
  long-press did before (the menu no longer opens while the finger is still down, which ended the
  drag). Each drag is noted in the activity log: what, by which pointer, and where it landed.
- **Faders and crossfader:** a finger or pen never makes the cap jump. Pressing anywhere on the
  track and sliding moves the cap from where it is by as much as the finger moves, so a slightly
  missed cap can't cut the room or swing the volume. A mouse click on the track still jumps there.
- **Touch mode** (Settings › Touch mode, off by default): library rows 44 px, folder rows 40,
  queue and history rows 52 with a wider grip, deck keys 42 (play 62; compact decks 34 and 40),
  a taller crossfader, bigger search box, filter buttons, steppers, switches and settings key, and
  44 px menu items. It's a stylesheet (`Styles/TouchStyles.axaml`) added to the app while on, plus
  the shared size resources swapped; menus and flyouts follow because they're app-wide. Two and
  four decks still fit in the same space.

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

## Roadmap: getting ready for other DJs (2026-10-21)

The plan is to give GZJam away, free, to any DJ who wants it. A fellow DJ (who uses Mixxx with
4 decks at a weekly dance, on Linux) reviewed what he relies on and what he misses. Already covered by what's built: seeing the queue and next track at a
glance (his reason for 4 decks), per-deck headphone cue and library preview, tempo with key
lock, and automatic history.

### Now: before handing it to other DJs

1. **Mono output** for the speakers: a setting, on by default. Early stereo jazz often puts the
   drums hard on one side, so half the floor loses the beat. (Since 2026-10-22 the headphones
   are mono too: see the settings redesign.)
2. **Master volume, level meter and clip light** for the speakers, with a safety limiter so
   nothing that's turned up can distort. Matters most when a laptop feeds a PA directly.
3. **Headphone volume.** (Hearing the room while previewing already works: cue the live deck.)
4. **Ratings, three-way:** unrated / ♥ (I'd play this for a dance) / 💔 (I wouldn't). Five stars
   become an inconsistent mess; the useful question is yes or no. Plus an **energy** mark
   (low / medium / high). Both stored in the library database, never in the files. Settable
   from the library, queue and decks; searchable.
5. **Search by BPM and by field:** `bpm:130-150`, `~bpm:140` (within 8%), `artist:basie`,
   `year:1938`, and rating/energy. Plus a tap-friendly way to do the BPM range.
6. **Set where a song starts (and ends) by hand**, e.g. after applause on a live recording.
   Already in the track prep spec; overrides the automatic trim, saved per file.

### Status: items 1-3 built (2026-10-21)

- Every output goes through volume → mono fold-down → a safety limiter → meter,
  in one BASS DSP on the output mixer (`OutputBus`). Settings carry over when outputs change.
- **Mono:** Settings › Sound › *Mono*, on by default, for the speakers and the headphones alike:
  the DJ hears what the room hears (including recordings that lose something when folded to
  mono), with either ear. The meter shows one bar in mono, two in stereo.
- **Limiter:** ceiling just under full scale; instant attack, 300 ms release. Loud MP3s often
  decode a little over full scale (one in the library peaks at +1.3 dB), which it absorbs quietly;
  the **LIMIT** light only comes on when it has to turn things down by more than 2 dB (two loud
  decks blended), and stays lit 1.5 s so it's noticed.
- **Volume** sliders for speakers and headphones in the mixer column (audio taper: the middle is
  -12 dB), remembered. No boost above 100.

### Status: item 4 built (ratings and energy)

- **Rating** column (♥) and **Energy** column (three rising bars) in the library; tap to cycle
  (unrated → ♥ → broken heart → unrated; unset → low → medium → high → unset). Both columns sort.
- The same two buttons on each deck, beside the artist, to mark the song while it plays.
- Queue rows show the heart and bars when set. Right-click (library or queue) › *Rating* /
  *Energy* marks several tracks at once.
- Stored in the library database (`marks`, schema upgrade 5) by path; files are never touched.
  Marks survive rescans and tag changes.
- Drawn as vector icons, not emoji, so they look the same on every system.

### Status: item 5 built (search filters)

- Typed filters, close to Mixxx's so its users feel at home (`SearchQuery`):
  `bpm:140` (anything that rounds to it), `bpm:130-150`, `bpm:>200`; `~bpm:140` = within 8%,
  the tempo slider's reach, so everything listed could be pitched to 140; `year:1938`,
  `year:1935-1940`, `year:<1950`; `artist:basie`, `title:`, `album:`, `genre:`, `comment:`,
  `folder:` (quote several words: `artist:"count basie"`); `rating:yes|no|none`;
  `energy:high|medium|low|none` or `energy:2-3`. A leading `-` leaves things out
  (`-rating:no`, `-live`). Filters combine with ordinary words and the folder scope.
- BPM filters use the BPM the app shows (the tag's, or the detected one; see Track prep).
- A half-typed filter (`bpm:`, `rating:`) is ignored rather than emptying the list.
- **BPM button** by the search box (tap-friendly): *Near Deck 1 (144 BPM)* / *Near Deck 2*
  (the deck's tempo as the room hears it, with its slider), the four tempo ranges, or *Any BPM*.
  It writes the filter into the search text, replacing any BPM filter already there, and shows
  the active one ("BPM ≈144").
- **Auto-play and the gap moved to the top of the queue panel** (the DJ's suggestion): they run
  the queue, so they belong with it. They sit above the Queue/History tabs so auto-play's status
  stays in view on either tab. The mixer column is now just meter, volumes and crossfader, and
  the decks are back to their original 268 px.

### Status: item 6 built (start and end by hand)

- Right-click (or long-press) a deck's waveform: *Song starts* › *Here* (where you clicked),
  *At the playhead* (find the spot in the headphones first), or *Automatic*; the same for
  *Song ends*. Times shown to a tenth of a second.
- Saved per file (`trims`, schema upgrade 6) and applied on load straight away, before analysis
  returns; it overrules the automatic trim. A deck still at the top moves to the new start.
- A hand-set point gets a purple marker with a flag, so you can see it isn't automatic.
- Refused with a hint: a start or end leaving under 5 s of song, and an end before the playhead
  on the deck the room is hearing (it would stop dead).
- Not yet: dragging the markers directly.

### Status: four decks built (2026-10-21)

The reviewing DJ runs 4 decks in Mixxx. They fit in the space of two, so the library loses nothing:

- Settings › *Four decks* (off by default; remembered). Deck 3 sits under Deck 1 and Deck 4 under
  Deck 2, each half height (two compact decks plus the gap are exactly one full deck, 268 px).
  New colours: Deck 3 pink, Deck 4 lime.
- **Compact deck:** number, ON AIR, title and artist on one line, with time left beside them; a
  shorter waveform (trim menu still on right-click); play, cue, fade out, tempo (tap the % to
  reset) and BPM. Eject, rating, energy and *BPM* move to a ⋯ menu.
- **Crossfader:** left side is Decks 1 and 3, right side 2 and 4 (DJay, Mixxx and Serato all do
  this). The side buttons show both numbers; the live label names the deck playing on the live
  side ("3 · LIVE"), or both ("1 / 3 · LIVE") when neither is.
- **One song per side:** starting a deck the room would hear while the other deck on that side is
  on air is refused with "Deck 1 is playing. Fade it out first". Off the room's side it plays
  as usual (for the headphones).
- **Auto-play** goes in number order, 1, 2, 3, 4, 1..., so every change crosses the crossfader.
  It keeps the next deck loaded, as with two.
- *Load to Deck 3 / 4* in the library, queue and history menus; *Near Deck 3/4* in the BPM button.
- Going back to two decks is refused while Deck 3 or 4 is playing; otherwise they're emptied.
- The engine always has four decks; ones not on screen are empty and cost nothing.

### Status: per-deck EQ built (2026-10-21)

- An **EQ** chip on each deck (beside the heart and energy bars; on a compact deck, beside the
  time) opens a panel laid out like a graphic EQ: a dB scale, three faders and the frequencies
  under them: **low** (shelf below 150 Hz), **mid** (bell around 1 kHz), **high** (shelf above
  5 kHz, where record hiss lives). -24 to +6 dB in half-dB steps.
- The faders (`EqFader`) are drawn like hardware: a ladder line every 3 dB, a brighter 0 dB line
  across all three, a slot and a metal cap matching the crossfader's. The slot and the lines
  between 0 dB and the cap light up in purple, so a cut reads as a cut. Drag, press to jump,
  double-tap for 0 dB, mouse wheel or arrow keys for half a dB.
- Tap a band's value to flatten it; *Less hiss* (treble -6 dB) as a starting point; *Flat*.
- The chip lights purple when the song has EQ, so it's clear at a glance.
- **Remembered per song** (`eq`, schema upgrade 7), saved as it's changed, and applied whenever the
  song is loaded on any deck. Flat forgets it. Never written into the files.
- It's the recording that's being fixed, so the EQ is heard in the room and the headphones alike
  (BASS_FX biquad filters on the deck's decoded stream, before the speaker/headphone split). It
  can be changed while the room is listening.
- Not a performance EQ: no permanent knobs, since it's set once per recording.

### Look: controls drawn like hardware, pass 1 of 3 (2026-10-21)

The UI read as generic ("AI-looking"): stock sliders and switches beside drawn controls, purple on
everything, big rounded cards and pills. The direction: draw controls like the hardware they
stand for, colour only where it means something, tighter corners. Pass 1 (decks and mixer):

- **Tempo:** a pitch fader (`PitchFader`): a tick every 1%, longer at 0 and ±4, labels under it
  on a full deck, a centre notch lit green at exactly 0, the crossfader's cap. Dragging is
  relative: pressing beside the cap never jumps the tempo of a song the room may be dancing to.
  Double-tap for 0, wheel or arrow keys for 0.1%.
- **Keys with LEDs** (`Button.hw`, `Border.led`): CUE, FADE OUT and the mixer's side buttons are
  dark keys; state is an LED, not a key filled with colour. LED colours by meaning: red = the
  room (on air, the live side, limit), amber = headphones, green = running, accent = set by you
  (EQ).
- **ON AIR:** a red LED and small caps, and a red bar along the deck's top edge, instead of a
  filled pill and a red outline. Number badges smaller and squarer.
- **Play:** a dark round key with a ring in the deck's colour.
- **Waveform:** solid (bars touching), as on DJ decks; tighter corners.
- **Mixer strip:** an upright LED meter (one column in mono, two in stereo) with a dB scale and a
  peak marker that holds 1.2 s; LIMIT is an LED under it; main and phones are upright faders
  (`Fader`, shared with the EQ) with lines at the dB marks of the volume curve; tooltips give the
  level in dB. Fills the space the old sliders left empty.
- Crossfader cap: square corners and a white index line, matching the other faders.
- An empty deck's readout is dimmed rather than showing a bright "-0:00".

### Look, pass 2: colour and corners (2026-10-21)

- **Purple retired.** The accent is now a muted steel blue (#7FA3CF), used sparingly: selected
  rows and folders (a faint tint), focus, the active BPM filter, "set by you" (the EQ LED and lit
  fader slot, hand-set start/end markers). Energy bars are light grey; the loved heart a muted
  red rather than pink. Deck colours a little less candy-bright (cyan, amber, rose, olive);
  the green a little less minty.
- **Corners:** panels 6 px (were 10), buttons and search box 4, chips 3. The EQ panel and its
  well follow.
- **Queue and history:** a plain list with dividers instead of rounded cards; the song auto-play
  takes next has a thin bar at its left edge.
- **Auto-play:** a key with a green LED instead of a switch in a glowing green box; the gap is a
  small readout window between − and + keys. The Queue/History tab underline is white, not green.
- **Waveform** colours toned down (a quarter of the way to grey, slightly darker): easier on the
  eye now that it's solid.
- An empty compact deck's "No track loaded" is dimmed.

### Look, pass 3: settings, menus, top bar (2026-10-21)

- **Settings** in the panel colours (like the EQ panel), grouped with dividers. On/off settings
  are keys with a green LED and ON/OFF (`ToggleButton.switch`), not phone-style switches.
  Fade-out length is − / readout / +, like the gap, instead of a number box. Setting names are
  semibold, explanations muted beneath.
- **Menus, drop-downs and text boxes** everywhere take the app's colours and 4-6 px corners via
  Fluent theme overrides (`ControlCornerRadius`, `MenuFlyoutPresenterBackground`,
  `ComboBox*`, `TextControl*`), instead of the stock grey.
- **Top bar:** a plain monochrome GZ mark instead of the gradient tile; the clock in a small
  readout window; the outputs as square indicators with an LED (green connected, red/amber not)
  and MAIN / CUE in small caps.
- **Headphone preview bar:** flat, with an amber edge and an amber headphones icon instead of an
  orange gradient and a filled tile; STOP is a key.
- The folder-scope chip's text is neutral (it was lavender).

### Status: loudness matching built (2026-10-21)

- **Measured** in the analysis pass (`LoudnessMeter`): integrated loudness per EBU R128 / ITU-R
  BS.1770 (K-weighting, 400 ms blocks every 100 ms, absolute and relative gating), in LUFS, plus the
  sample peak. Mono files count as dual mono, since both speakers play them. Stored with the
  analysis (schema upgrade 8); the analyzer version went to 2, so the library is re-analyzed once
  in the background (about 50 minutes for 12,686 tracks; a track loaded onto a deck jumps the queue).
- **Matched** (`LoudnessMatch`): each deck is turned towards **-16 LUFS**. Cuts are unlimited;
  boosts stop at +9 dB and where the track's peaks would go more than 1.5 dB over full scale (the
  limiter absorbs that much without its light coming on). On a 228-track sample of the real
  library: median -15.9 LUFS (so a typical song barely moves), 80% between -19.4 and -11.8;
  7% held back by their peaks, the quietest ending at -20 LUFS.
- Heard in the room and the headphones alike (deck gain on both splits). If a song's measurement
  arrives while the room is hearing it, or matching is switched on or off mid-song, the level eases
  over at 3 dB a second rather than jumping; off air it changes at once.
- **Settings › Match loudness**, on by default.
- **GAIN** fader in the deck's EQ panel: the DJ's adjustment for a song on top of matching, saved
  with its EQ (same -24..+6 dB range as the bands, so every fader's 0 dB lines up with the scale).
  Under the faders: the song's loudness and what matching does ("-19.5 LUFS · matching +3.5 dB").
- Not built: a one-knob compressor for old, unmastered recordings (wait and see if it's needed).

### Status: played recently built (2026-10-21)

- Every track knows when the room last heard it and how often, from History (joined in the
  library query; nothing extra to keep in step).
- **Played** column in the library: "Today", "3 days", "2 wk", "5 mo", "2 yr", blank for never;
  sorts by date.
- **Played tonight** (since the current session began) is dimmed in the library and the queue, as
  soon as it counts as played (30 s in the room), and in any later search. Restarting mid-gig keeps
  the session, so the dimming survives.
- **`played:` filter:** `played:7` (heard in the last 7 days), `-played:7` (leave out last week's
  set; never-played songs stay), `played:never`, `played:yes`. Listed in the BPM button's hint.
- **Naming a session:** History tab › NAME next to the session picker: "Friday Night, Oct 14". The
  picker then shows "Friday Night, Oct 14 · Oct 14 · 23 songs". Clear goes back to the date. Stored in
  `sessions.name` (schema upgrade 9).
- **Choosing columns:** right-click (or long-press) a column header to show or hide ♥ Rating,
  Album, Genre, Year, BPM, Energy and Played; remembered. A laptop can't fit them all. Genre starts
  hidden, since the folder tree already sorts music by genre and style.
- Not built: exporting a set list from History.

### Status: labels built (2026-10-21)

Mixxx's crates, renamed: "Lindy", "Balboa", "Female vocals". They cut across the folder tree
rather than being a second one.

- **Tagging:** right-click songs (library or queue) › *Labels*: every label, ticked if all the
  selected songs carry it ("(some)" if only some do); a tap puts it on all of them or takes it off.
  *New label…* asks for a name (`PromptDialog`). Names are case-insensitive; the first spelling wins.
- **Shown** as small chips after the title in the library. The title keeps at least 100 px; on a
  narrow window the chips are clipped (hide Album from the column menu to make room).
- **Filtering:** a *Label* button beside the BPM button lists the labels with how many songs carry
  each, plus *Songs without a label* and *Any label*; it writes `label:` into the search text and
  shows the active one. Typed: `label:lindy` (a prefix, any case), `label:"female vocals"`,
  `label:none`, `-label:balboa`. A label filter's results update when labels change.
- **Stored** in the library database by path (`labels`, `track_labels`, schema upgrade 10), never
  in the files. A label no song carries any more is forgotten.
- Not yet: renaming a label, labels on the queue rows and decks.

### Status: jumps built (2026-10-21)

- **A jump** leaves at the end of one phrase and lands at another, seamlessly: forward skips a
  part (a 5-minute track's third chorus), backward plays one again (stretching a tune to fit a
  dance). Up to two per song, saved by path (`jumps`, schema upgrade 11).
- **Setting one:** right-click the waveform › *Jump from here* (or *from the playhead*, after
  finding the spot in the headphones), then right-click where it lands › *Land the jump here*.
  *Remove jump 1 (1:42.0 → 3:05.0)* in the same menu. Refused with a reason: too near the start or
  end, landing within 2 s of where it leaves, or a third jump.
- **On the waveform:** a marker where it leaves (with an arrow the way it goes) and where it
  lands; an armed forward jump shades the part it skips, a backward one tints the part it repeats.
- **The JUMP key** (over the waveform, shown when the song has jumps): its green light means the
  song will jump. Each jump happens once, then the light goes out; tap to arm again (play a part
  once more) or, while lit, to cancel for this play. Jumps are armed when the song is loaded and
  when it's played again from the top.
- **Seamless:** the audio engine moves the position at the exact sample as the music reaches it
  (a BASS mixtime position sync on the deck's decode stream), not on the screen's tick; a test
  checks there's no quiet moment across a jump. Allowed on air, unlike seeking: it's deliberate.
- **Time left** counts the armed jumps ahead. A forward jump isn't counted as listening for History.

### Planned work

Features, bugs and testing still to do are tracked as [GitHub issues](https://github.com/GemZombie/GZJam/issues),
not here. This file records what was decided and built, and why.

### Status: import from Mixxx built (2026-10-22)

- **Settings › Library › Import from Mixxx.** Finds Mixxx's library where Mixxx keeps it
  (`%LOCALAPPDATA%\Mixxx` on Windows, `~/.mixxx` or the Flatpak's on Linux, Application Support on
  a Mac), or the DJ chooses `mixxxdb.sqlite`. The window shows what would come over, by kind, and
  how many Mixxx songs aren't in GZJam's music folders (and where). Mixxx's database is copied and
  only the copy read: Mixxx is never touched, running or not.
- **Matching:** by path (Mixxx writes forward slashes, also on Windows), else by file name and size
  if one song has them (music that moved since).
- **Ratings:** 4-5 stars become ♥, 1-2 a broken heart, 3 stays unrated. **Crates** become labels.
  **History:** each Mixxx night (its set logs) becomes a session with its songs and times, songs
  GZJam doesn't have included; dated names ("2024-03-14") are dropped, as GZJam shows dates.
  **Locked BPMs** (the DJ confirmed them in Mixxx) become typed BPMs.
- **Only fills gaps:** GZJam's own ratings and typed BPMs stand, labels are added to, and a night
  already imported is skipped, so importing again is safe.
- **Not brought over: cue points.** Mixxx places intro and outro markers automatically when it
  analyses a song; GZJam finds where songs start and end itself (and the DJ's own trims stay).
- **Typing a BPM** came with it: the BPM menu (library, queue and deck) has *Type the BPM…*; a typed
  BPM wins over the tag and detection, in the BPM column, on the deck and in `bpm:` searches
  (`manual_bpm`, schema upgrade 12). *Automatic* or a range forgets it.

### Status: About built (2026-10-22)

- **Settings › About:** the version (from the release tag; "-dev" when built locally), who made
  GZJam, the GitHub page, and the list of the software it's built with and their licences.
  *Full licence texts* opens `THIRD-PARTY-NOTICES.txt` in the computer's text viewer.
- `THIRD-PARTY-NOTICES.txt` (repo root) ships beside the program in every package: the list, then
  each licence in full, including the notices of what SkiaSharp and .NET carry inside them. A test
  fails if a library is added without being named in its list.

### Status: settings redesigned (2026-10-22)

- **Settings opens over the main screen**, which dims behind it, in a lighter, bluer grey than
  the decks with its own title bar, so it can't be mistaken for part of the set. It's inside the
  main window rather than a window of its own, so it looks the same everywhere (a tiling desktop
  like Hyprland would place a separate window beside GZJam). Music keeps playing. DONE, Escape
  or a tap on the dimmed screen closes it.
- **Sections down the side:** Sound (speaker and headphone outputs, mono, match loudness), Decks
  (four decks, fade-out length, and the two safety choices), Screen (touch mode), Library
  (import from Mixxx) and About (which used to be a window of its own). It reopens where it was
  left; MAIN and CUE at the top open it on Sound.
- **Mono covers the headphones too** (one switch): the DJ hears what the room hears, and a
  headphone worn on one ear misses nothing.
- **Linux output names** say what they are: "Built-in: Headphones / Speakers", "Built-in: HDMI 1",
  "Cable Creation (USB)", from what the kernel reports for any card (USB or not; HDMI, digital or
  analog), never one driver's naming. The sound server's own entry (the same as Default) and
  Intel SOF's second copy of the headphone jack are left out; anything unrecognised keeps ALSA's
  name. The log keeps both names.

### Status: speakers for GZJam alone on Windows (2026-10-22, issue #10)

- **Settings › Sound › Speakers for GZJam alone** (Windows only; on by default): the speakers' sound
  card is opened in WASAPI exclusive mode (un4seen's BASSWASAPI, `ExclusiveOutput`), so no
  notification or browser can reach the room while GZJam is open. Linux already gets this by
  opening a chosen card directly.
- Only for an output chosen by name, never *Default* (which follows whatever Windows picks), and
  only the speakers: the headphones stay shared, so the DJ can still hear other things on them.
- The mix runs at the card's own rate: 44.1 kHz where it takes it (most music, no resampling),
  else 48, 96, 88.2 or 192 kHz, with BASSmix resampling the decks. The limiter follows the rate.
- Windows lets an exclusive output take a card even from a program already playing on it, so a
  browser playing can't block GZJam. A card another program holds *exclusively* can't be opened
  at all: GZJam says who has it and the room keeps the output it had.
- A card that won't open alone for another reason (no stereo at those rates) is shared instead,
  with a notice saying so. The log says "(GZJam's alone)" when it is.
- If the card stops by itself (unplugged, or taken by Windows), GZJam opens it again if it's still
  there, else the system default, with a notice, as at startup.
- **The room comes first** (found testing on the move, 2026-10-23): headphones that won't open no
  longer stop the speakers opening (before, both stayed shut and the decks played to nothing);
  the headphones are silent and a notice says why. Speakers that fall back to the system default
  when it's the headphones' own output (a laptop unplugged from its dock and TV) leave the
  headphones off, with a notice, so a preview can't reach the room; that also predates this
  feature. Headphones chosen on the speakers' output on purpose share it rather than being shut
  out by an exclusive speaker output.
- **Unplugged mid-song** (also found on the move): Windows can still list a card just unplugged,
  so GZJam reopens the speakers only if they actually open; otherwise it goes to the system
  default, as above, and says so. When neither the new speakers nor the old ones open, the
  headphones carry on rather than going quiet with them. An exclusive output that runs dry because
  it's been unplugged counts as lost at once, not as a dropout.
- Tested on real devices on Windows (`ExclusiveOutputTests`), using a device that isn't the
  computer's default; they skip on CI machines, which have no sound cards.

### Status: extra protection against crackles (2026-10-23, issue #25)

- **Settings › Sound › Extra protection against crackles** (off by default): every output's buffers
  about three times as long (BASS's playback buffer 200 → 600 ms, the device buffer on Linux and
  Windows' shared output, the exclusive speakers 150 → 400 ms). One switch rather than numbers: a
  DJ can't be expected to know buffer sizes, and a wrong one causes what it's meant to fix.
  GZJam doesn't beatmatch or scratch, so a little delay costs little. Turning it on reopens the
  outputs (a short gap), since a device's buffer is set when it's opened.
- The exclusive speakers' normal buffer went from 100 to 150 ms for everyone: cheap insurance.
- **Dropouts are logged:** the exclusive speakers' buffer is checked about 30 times a second, and
  an empty one is noted in the activity log (at most every 10 s), with a hint to try the switch.
- **Windows default warning:** when the speakers GZJam has to itself are also Windows' default
  output, Settings › Sound says other programs may refuse to play (YouTube did, in testing) and
  how to avoid it: make another output Windows' default.

### Status: asking before closing mid-set (2026-10-23, issue #28)

- Closing GZJam (the close button, Alt+F4, Cmd+Q) while the room is hearing a deck asks
  "Stop the music and close GZJam?", naming the deck and song. *Keep playing* is the default:
  Enter and Escape choose it, so a reflex keypress can't quit. *Close GZJam* is red, the room's colour.
- Only the room counts (the DJ's decision): music only in the headphones (the preview, or a cued
  deck off air) closes straight away, as does an idle GZJam.
- Never when the computer is shutting down (`WindowCloseReason.OSShutdown`), so GZJam doesn't hold
  that up. The activity log notes each question and the answer.

### Status: one bar at the top (2026-10-22, issue #32)

- The maintainer's suggestion, after djay Pro. GZJam's top bar (the GZ mark, clock, background
  progress, MAIN and CUE, settings) is also the window's title bar on Windows and macOS
  (`MainWindow.TitleBar.cs`): 44 px instead of the system's 32 px plus GZJam's own 72, about 50 px
  more for the decks and library. Touch mode makes it 54 px, and the title bar follows.
- The bar is marked as the title bar for Windows' hit-testing, so its empty space moves the window
  and a double-click maximises; every key in it is marked as a key.
- **Windows:** Avalonia 12 would draw Windows' title bar itself, the window's title included,
  over the left of the bar, so the decorations are border-only and GZJam draws and handles
  minimise, maximise (restore when maximised) and close in Windows' style. First tried marking
  them as Windows' own buttons: Windows recognised them (and would show snap layouts on hover) but
  with border-only decorations neither it nor Avalonia acted on a click, so they did nothing (the
  maintainer found it). Snap layouts on hover would need Windows' messages handled directly; drag
  to a screen edge and Win+Z still snap. Checked by asking a real window (WM_NCHITTEST) about each
  spot. When maximised, the window's off-screen edge is kept clear of the bar.
- **macOS:** the bar leaves room on the left for the window's own buttons. Not yet seen on a Mac
  (issue #18).
- **Linux** keeps the system's title bar where there is one; tiling desktops like Hyprland have none.

### Status: a level meter on each deck (2026-10-23, issue #2)

- A slim LED column on each deck, in the frame's padding along the edge nearest the mixer (Decks
  1 and 3 on the right, 2 and 4 on the left), so it takes no room from the deck's controls; half
  height with four decks. One column (the louder side), no scale: the speakers' meter has that.
- Measured like a mixer's channel meter: after the deck's gain (loudness matching and the DJ's)
  and EQ, before the crossfader. A deck the room can't hear still shows its level, which is the
  point when cueing. A DSP on the deck's split to the speakers' mix (`Deck.TakePeak`), where BASS
  hasn't yet applied the crossfader's volume. Same segments and colours as the speakers' meter;
  the top (red) light is the limiter's ceiling, so a deck lighting it would make the speakers'
  limiter work at full crossfader.

### Status: nothing moves as a deck starts or goes on air (2026-10-23, issue #34)

- The maintainer noticed things resizing as a deck went live. Measured: the ON AIR / OFF AIR line
  only appeared while playing, so starting a deck pushed its title down 14 px and shrank the
  waveform by 10; with four decks the title jumped about 60 px sideways, then 3 back on air.
- The line now always keeps its space (blank until the deck plays), and with four decks has a
  fixed width. `LayoutStabilityTests` records where every visible control is, plays a deck and puts
  it on air, and fails if anything moved except the play key's icon and the words themselves.

### Status: cue keys (2026-10-23, issue #1)

- **The ask:** jump somewhere *now*, at a moment the DJ chooses (on a long track, wait for the end
  of a phrase and go to the outro). Jumps fire by themselves where they were set; this is the key
  for the moment itself. The maintainer chose numbered keys (over one key that goes to the next
  marked point) and allowed them on air, like jumps.
- **Marking:** the waveform's menu › *Cue N here* / *Cue N at the playhead* (N: the lowest free
  number, 1 to 4), *Remove cue N*. Two cues within a second of each other are refused. Saved by
  path (`cues`, schema upgrade 15); a removed cue's number is free again, the others keep theirs.
- **Going there:** a key per cue over the bottom left of the waveform (the JUMP key is top right),
  and a flag with the number on the waveform, in the jumps' blue (both are things the DJ set).
  Tapping one seeks at once, playing or paused, on air or not; seeking by the waveform stays locked
  on air. Not snapped to the beat: GZJam knows a song's BPM but not where its beats fall, so the
  tap is the timing. A test checks the room hears no quiet moment as it goes.
- **Touch:** keys 40 x 44 px, 8 apart, acting on release (sliding off cancels). A four-deck
  waveform is 24 px at its shortest, too short for them, so four decks have *Go to cue N* in the
  deck's ⋯ menu instead (the maintainer expects touch to be used mostly with two decks). The JUMP
  key, 32 px in touch mode, made a four-deck waveform (and its deck) taller; it's now 16 px there.
- Not yet: bringing over Mixxx's hot cues (issue #36).

### Status: phones mix (2026-10-23, issue #3)

- **The ask:** while previewing a song in the headphones, keep one ear on what the dancers hear,
  with a knob to balance the two (a DJ mixer's cue/master knob). Before, the only way was to cue
  the live deck too, at the same level as the preview.
- **PHONES MIX**, a small crossfader under MAIN and PHONES (CUE at the left, MAIN at the right; its
  cap's line amber, the headphones' colour). At CUE, where it starts, the headphones are as before:
  the cued decks and the preview. At MAIN, what the room hears and nothing else. At the centre,
  both at full; between, one turned down as the other comes up. Saved in the settings.
- **How:** no new audio path. Each deck's headphone feed is set to the louder of "cued" and "as
  much as the room hears of it" (the crossfader, the deck's level, a fade-out), each scaled by the
  phones mix; a deck that's both cued and on air isn't doubled. The preview is cued, so the room
  side turns it down. The PHONES fader still sets the overall level.
- **Height:** the mixer grew by the fader's row (two decks: 268 to 297 px; touch: 280 to 337) and
  the decks with it. With four decks the upper decks took all of the extra height, and in touch
  mode they already did (issue #37): the two rows now share it equally, and a test checks the
  decks are level with each other and with the mixer in all four layouts.

## Decided against

- **Streaming from Spotify (or Apple Music, TIDAL, Beatport).** DJ streaming is only available to
  partner companies (rekordbox, Serato, djay since September 2025); Spotify's public developer
  policy forbids mixing/segueing its content, and its audio can't be routed through our mixer.
  A playlist-matching feature (Spotify playlists → tracks in the local library) was possible but
  isn't useful without streaming. Revisit only if the streaming landscape changes.

## Open questions


- What worked well in BPM Studio that should carry over?
