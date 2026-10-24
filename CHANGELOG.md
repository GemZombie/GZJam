# What's new in GZJam

What changed in each release, newest first. Downloads are on the
[Releases page](https://github.com/GemZombie/GZJam/releases).

<!--
Before tagging a release, add its section here: "## <version> (<date>)", then what a DJ would
notice, under "New", "Changed" and "Fixed" as needed. The release workflow refuses a version that
has no section, and uses the section as the release's notes (tools/release-notes.sh).
Bring ROADMAP.md up to date at the same time: it's the big picture, this is what changed.
-->

## 0.5.0 (unreleased)

New:

- **A close-up of the song on each deck.** Above the whole song, a few seconds around the
  playhead scroll past it, beat by beat, so you can see the end of a phrase coming before you tap
  a cue key, and find the exact spot for a cue, a jump or where a song starts. The box on the whole
  song shows what the close-up covers. The mouse wheel or a pinch shows more or less (2 to 30
  seconds; 8 to start with). On a deck the room can't hear, drag the close-up to move through the
  song like a record, or click a spot to go there. Right-click or long-press it for the same menu
  as the whole song, with "here" just where you pressed. With four decks there's no room for both:
  the mouse wheel or a pinch on a deck's waveform zooms it in, and out again.
- **Drag the markers.** A song's start and end, either end of a jump, and its cues can be dragged
  along the close-up (for precision) or the whole song (for big moves), by mouse or finger. Let go
  and it's saved for the song. It works on air too, since moving a point changes nothing the room
  hears until the music gets there; somewhere a point can't go (a cue on top of another, say), it
  goes back and says why. A deck waiting at its start follows the start when you move it.
- **Tap tempo.** For a song whose BPM is wrong: on a deck, BPM menu › *Tap tempo…*, then tap the
  pad (or press Space) along to the beat. The BPM shows after four taps and settles the longer you
  tap; stop for two seconds to start again. SET makes it the song's BPM, saved in GZJam, not the
  file. With the deck's tempo changed, you tap what you hear and it works out the song's own BPM.
  It covers only that deck's waveform, so you can keep mixing while you tap a song in your
  headphones.
- **Labels on the decks and queue, and renaming them.** A song's labels now show after the artist
  on its deck and its queue row, as in the library, in whatever room the title and artist leave.
  Rename a label from Labels › *Rename a label* (right-click songs) or the Label button by the
  search; a search for it follows the new name. Rename it to another label's name and the two
  become one.
- **Choose how near `~bpm:` searches reach.** Settings › Library › *Near a BPM*: ±8% to start with
  (as far as the tempo slider goes), from ±1% to ±20%. The BPM button's *Near Deck* choices follow
  it. For one search, type `~bpm:140/5`.

Changed:

- **A new logo and app icon:** a waveform wrapped into a G, and the name in letters cut into bars
  like a waveform. It's in the top bar and on Settings › About too.

Fixed:

- **A hint or the "Analyzing…" label never makes a deck taller.** With four decks in touch mode,
  they used to push the deck down a few pixels while they showed.

## 0.4.0 (2026-10-23)

New:

- **A level meter on each deck,** along its edge beside the mixer. It shows the deck's level
  before the crossfader, so you can see a deck you're cueing is too quiet or too loud before the
  room hears it. The top light means as loud as the speakers go.
- **Cue keys: go to a point in the song at a tap.** Right-click (or long-press) the waveform ›
  *Cue 1 here* or *at the playhead*, up to four per song. A numbered key for each appears over the
  waveform, and a numbered flag marks the spot: tap the key and the deck carries on from there at
  once, on air too, at the moment you choose (wait for the end of a phrase, then go to the outro).
  With four decks, they're in the deck's ⋯ menu. Saved for the song in GZJam, never in the file.
  Import from Mixxx brings over your hot cues 1 to 4 as cue keys 1 to 4, for songs that don't have
  cues in GZJam yet.
- **Phones mix: hear the room in the headphones too.** A new fader under MAIN and PHONES blends
  what the headphones hear, from what's cued (CUE, where it starts: as before) to what the room
  hears (MAIN), with both at full in the middle. Keep one ear on the dancers while you preview
  the next song.

Changed:

- **One bar at the top on Windows and Mac:** GZJam's top bar is now also the window's title bar,
  giving the decks and library about 50 more pixels. Drag its empty space to move the window, and
  double-click it to maximise; minimise, maximise and close sit at its end. Linux keeps its own
  title bar where it has one.

Fixed:

- **Nothing on a deck moves when it starts or goes on air.** The ON AIR line used to appear above
  the title and push it, the waveform and (with four decks) the title down or sideways; its space
  is now kept.
- **The JUMP key fits a four-deck waveform** in touch mode, instead of making that deck taller.
- **With four decks, all four are the same height,** lined up with the mixer beside them. In
  touch mode the upper decks used to be taller than the lower ones.

## 0.3.0 (2026-10-22)

New:

- **Windows: the speakers are GZJam's alone.** While GZJam is open, no other program's sound (a
  notification, a browser) can reach the room through the speaker output. Settings › Sound ›
  *Speakers for GZJam alone*, on by default, for an output chosen by name (not *Default*). The
  headphones stay shared. If the card is unplugged mid-set, the music moves to the computer's
  default output and GZJam says so. When those speakers are also Windows' default output, Settings
  says other programs may refuse to play, and how to avoid it.
- **GZJam asks before closing while the room is hearing music**, so a stray click on the close
  button can't stop the set. *Keep playing* is the default (Enter and Escape choose it). Music only
  in the headphones doesn't count, and GZJam never holds up the computer shutting down.
- **Extra protection against crackles** (Settings › Sound): for a computer where the sound crackles
  or drops out. Play and the crossfader respond a little later. The activity log notes when the
  speakers' sound runs dry, and the README has a short troubleshooting list.
- **Read all tags again** (right-click a music folder in the library): for tags edited with a
  program set to keep the file's date, which GZJam wouldn't otherwise notice. GZJam also rereads
  every song by itself whenever it gets better at reading tags.

Fixed:

- **The speakers always open, even if the headphones can't** (they're silent, and GZJam says why).
  Before, both stayed shut and the decks played to nothing.
- **Headphones never end up in the room after an unplug.** When the speakers fall back to the
  computer's default output and that's where the headphones are (a laptop taken off its dock), the
  headphones are switched off, with a notice.
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
