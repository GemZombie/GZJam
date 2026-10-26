# What's new in GZJam

What changed in each release, newest first. Downloads are on the
[Releases page](https://github.com/GemZombie/GZJam/releases).

<!--
Before tagging a release, add its section here: "## <version> (<date>)", then what a DJ would
notice, under "New", "Changed" and "Fixed" as needed. The release workflow refuses a version that
has no section, and uses the section as the release's notes (tools/release-notes.sh).
Bring ROADMAP.md up to date at the same time: it's the big picture, this is what changed.
-->

## 0.9.1 (unreleased)

New:

- **QUIT GZJAM in Settings,** at the bottom of the side column: a way to close GZJam on Linux
  desktops that draw no title bar (Hyprland), so no close button. It asks first while the room hears
  music.
- **A song added to the queue or a playlist it's in already** still goes in, but the top bar says
  so (*"Shiny Stockings" is in the queue already (5th)*), with **Undo** to take the new copy out
  again.
- **MAIN and PHONES volume from the keyboard:** Ctrl+↑ and Ctrl+↓ (Cmd on a Mac) turn MAIN up or
  down a little each press; with Shift, PHONES. They work from anywhere, the search box too.
- **Choose the loudness target:** Settings › Sound › *Loudness target*, under *Match loudness*.
  -16 LUFS as before, or -18, -20 or -23 for more headroom on a mixer or PA that clips easily (turn
  its channel up to make up for it). Quiet, peaky old transfers also come up closer to the rest.
- **The user guide: plugging into the venue's sound** (under *At a gig*): which input and cable,
  why mono, setting the levels so nothing distorts or hisses, a powered speaker with no mixer, and
  what to try for hum.
- **Auto-play can fade each song out after a set time,** for practice nights, classes and jams:
  *FADE AFTER* under the gap, *Off* or 1:00 to 5:00. The deck's time counts down to the end of
  the fade, then the gap, then the next song. Songs shorter than that play to their end.

Changed:

- **New installs: the deck the room hears takes two taps to pause or fade out,** so a stray touch
  or click can't stop the dance. The first tap says *Tap again to pause* (or *to fade out*) and
  lights the key red. If you already use GZJam, your setting stays as it is; to switch, choose *Tap
  twice* in Settings › Decks › *Pausing or fading out the live deck*.
- **The top bar says the analysis of your songs is a one-time job.** On a new library it says
  *First-time analysis…*, then roughly how long is left; pointing at it explains what's happening
  and that GZJam is fine to use meanwhile. The Mixxx import says so too: the import takes seconds,
  the analysis after it is separate.

## 0.9.0 (2026-10-26)

New:

- **Compact decks,** for laptops: smaller decks so the library and queue show more songs. Switch
  with the arrows beside the gear, Ctrl+D (Cmd+D), or the grip under the mixer, as often as you like
  during a night.
- **Import playlists (M3U)** from MediaMonkey, foobar2000, iTunes, VirtualDJ and most other
  players: Settings › Library. Each file becomes a playlist of the same name, in order.
- **Ratings saved in the song files** (by MediaMonkey, foobar2000, Windows Media Player and
  others) come over as hearts for your unrated songs: 4-5 stars ♥, 1-2 a broken heart. Settings ›
  Library › *Ratings in the song files*. The files are only read.

Changed:

- **Fade out on air takes two taps too,** when you've chosen two taps to pause the deck the room
  hears (Settings › Decks › *Pausing or fading out the live deck*): a fade ends the dance as surely
  as a pause.
- **Space previews in the queue too,** as in the library.
- **The decks come back after a normal close too,** not just the queue: each with its song, tempo
  and cue button, paused at the start of the song. Closing GZJam no longer loses what was loaded.
  After a crash they still come back exactly where they were.
- **American spelling throughout:** *color*, *center*, *gray*, *license* and the rest, in GZJam
  itself (*Full license texts*, *Maximize*) and in the guide.

Fixed:

- **Delete in the queue** left nothing selected, so the arrow keys stopped working until you
  clicked in the queue again. The next song is selected now.
- **A song deleted beside a copy of itself** (same size and date) could be taken as moved onto
  the copy, which then got its hearts, cues and playlist places. With more than one music folder only.
- **Settings › About's links say where they go:** *User guide · GZJam on GitHub*. The project's
  address beside *User guide* read as the guide's own address, so it was clicked instead.

## 0.8.0 (2026-10-25)

New:

- **GZJam says when a new version is out.** At startup it asks GitHub for the newest release, and
  if there's one, says so quietly in the top bar, with a link to what's new and the downloads.
  Never during a set: it goes as soon as the room hears music. Close it and that version isn't
  mentioned again. Nothing about you or your music is sent, and nothing is downloaded or installed
  by itself. Don't want it? Settings › About › *Tell me when a new version is out*; off, GZJam
  never goes online by itself. To look whenever you like, even with it off: Settings › About ›
  *Check now*.

- **The night comes back after a crash.** If GZJam closes without warning, or the laptop dies or
  restarts mid-dance, the next start puts back the queue and the decks as they were: each song
  paused where it was, at its tempo, with its cue button. Nothing plays until you press play, and
  auto-play stays off until you turn it on. A song that's gone since (a drive not plugged in) is
  left out, with a note.
- **The queue is still there the next time you open GZJam,** after a normal close too, so it can
  be built ahead of a night. The decks start empty.
- **Back up your library, and restore it** (Settings › Library): hearts, labels, cues, trims,
  EQ, BPMs, playlists, History and your Settings, in one file to keep on a USB stick or in a
  cloud folder (your music files aren't in it). Restoring says what's in the backup and asks
  first, then GZJam opens again with it; the library it replaces is kept, not deleted. A backup
  from a newer GZJam is refused. GZJam also keeps a copy of its own, once a day it's opened, the
  last five beside the library.
- **A user guide:** getting started, the outputs, the decks, the library, auto-play, History,
  Settings, a checklist for the night and what to do if the sound stops. In
  [the user guide](https://github.com/GemZombie/GZJam/blob/main/docs/GUIDE.md), and linked from Settings › About.

Fixed:

- **Headphones unplugged during a night go quiet, and come back by themselves** when plugged in
  again, with no trip to Settings. The preview never moves to the speakers or anywhere else, and
  the room plays on untouched. A notice says so both ways, and CUE in the top bar says *Unplugged*
  meanwhile. The booth does the same.
- **With two taps to pause, a quick tap while the screen is frozen no longer pauses the room.**
  If the screen stalled for a moment during a tap, the tap could count as a press-and-hold and
  pause the deck on one tap. GZJam now goes by when the tap really started and ended.
- **A song moved to another music folder keeps its marks** (hearts, labels, cues, playlists,
  plays), as a song renamed within one folder already did.
- **A song in the queue or on a deck follows when it's renamed or moved:** it loads from its new
  name, and a deck that's playing plays on.
- **Settings › Sound warns about headphones on *Default*,** which follows the computer's own choice:
  unplugged, the computer may move the preview to another output, even the room's.

## 0.7.3 (2026-10-24)

New:

- **Play a song straight from the file manager.** Drag a file from Explorer, Finder or your Linux
  file manager onto a deck, or any number of files and folders into the queue. A song from
  outside your music folders, say one a dancer brings on a USB stick, plays like any other and
  shows in History, without joining your library. A song from your library keeps its marks.
- **Start over with an empty library:** Settings › Library › *Start over…*. GZJam closes and opens
  again with no music folders, for a fresh start or to add your music again from scratch. Your
  music files aren't touched and your Settings stay; the old library isn't deleted, but kept in
  a "previous libraries" folder beside it.

Fixed:

- **On a Mac, the menu bar says GZJam,** not "Avalonia Application".
- **The activity log says what the library is doing:** each scan of a music folder (songs found,
  read, added and removed, how long it took, and any error) and each time the library's list is
  rebuilt. On one Mac, adding a large folder left the library empty until GZJam was restarted;
  the log will now show why, if it happens again.

## 0.7.2 (2026-10-24)

Changed:

- **One output for both is a switch of its own,** easy to find: Settings › Sound › *One output for
  both (splitter cable)*, which says what to plug in. On, the Speakers and Headphones rows say
  which side each is on, and the top bar starts with *Left* and *Right*, where a long output name
  used to hide it. It used to be an entry in the Headphones list.

## 0.7.1 (2026-10-24)

Changed:

- **GZJam says who it's for:** Settings › About, and the app menu on Linux, now call it a simple
  DJ app for social dance nights.

Nothing else a DJ would notice. This is the release to try with the new
[testing checklist](https://github.com/GemZombie/GZJam/blob/main/docs/TESTING.md), on Windows, a
Mac or Linux.

## 0.7.0 (2026-10-24)

New:

- **Playlists: plan a set before the night.** Under the folder tree, *+ New* makes one; drag songs
  onto its name, or right-click songs › *Add to playlist*. Tap a playlist to open it in the song
  list, numbered, in its order: drag rows to reorder them, and Delete (or right-click) takes a song
  out of it, never out of your library. A song can be in a playlist twice. *Add to queue* puts
  the whole playlist on the end of the queue (right-click a song for *Add to queue from here*),
  and auto-play can run it. The queue's menu and a night in History can each be saved as a
  playlist too. Import from Mixxx brings your Mixxx playlists across, in their order.
- **A booth output:** a monitor speaker for you at the DJ table, for a big room where the
  speakers face away from you. Choose it in Settings › Sound › Booth: it plays what the room
  hears, at its own volume, and never the headphones' preview. Off unless you choose one.
- **Speakers and headphones from one output,** for a laptop with only a headphone socket: with a
  splitter cable, the room plays from the left side and your headphones from the right, each in
  mono. Choose it in Settings › Sound › Headphones › *Speakers' output, right side*.

Changed:

- **Menus stand out from the panels behind them:** a step lighter, with a brighter edge, so on a
  dim screen it's clear where a menu ends and the song list begins.
- **Faster searches in very large libraries.** With 100,000+ songs, the whole library lists about
  a third faster, BPM searches about a third faster, and `rating:`, `energy:` and `played:` are
  instant. Smaller libraries are a little faster too.

Fixed:

- **A song renamed or moved keeps its heart, energy, labels, cues and the rest.** Renaming a
  file (or its folder), in a tag editor or anywhere else, used to make GZJam treat it as a new
  song: its marks were lost from view, it dropped out of your playlists, and its Played column
  started over. Now they all go with it, whether GZJam was open or not, as long as it stays in
  the same music folder you added.
- **The BPM and Label menus by the search open with their choices again.** A click opened them
  empty, just a thin gray line under the button. A compact deck's ⋯ menu (with four decks) had
  the same fault.
- **A library a newer GZJam has updated no longer makes GZJam close without a word.** Going back
  to an older GZJam (or from a test build) used to make it vanish as it started; now it says
  your library is from a newer GZJam, and offers the newest one. Nothing is changed or lost.
  And from this version on, an older GZJam opens a library a newer one has updated, as long as
  the newer one only added to it (as most updates do).

## 0.6.0 (2026-10-24)

New:

- **Album art on the decks and queue,** small, to see at a glance that the right record is on the
  right deck. It's the picture in the song's file, or failing that the cover in its folder
  (`cover.jpg`, `folder.jpg`, `front.jpg`, or the folder's only picture). A song without one shows
  a dim record in the deck's color. The library has an Art column too, hidden to start with
  (right-click a column heading to show it): an album's songs all share one picture, so in a long
  list it doesn't tell them apart. GZJam keeps small copies of the pictures in its own folder;
  your music files are never changed, and nothing is downloaded.
- **Even out a recording.** For an old record whose quiet passages are hard to hear and loud ones
  too loud: the EVEN fader in a deck's EQ panel brings them closer together (a compressor). It
  works around the song's own loudness, so the same setting suits a quiet transfer and a loud
  one; at the top, passages 20 dB apart end up about 7 dB apart. Off at the bottom, and
  remembered for the song with its EQ.
- **Move the mid band.** Under the MID fader, the arrows move the band a third of an octave at a
  time, from 200 Hz to 5 kHz, to find just the frequency that's boomy or harsh. Tap the
  frequency for 1 kHz again. Remembered for the song.
- **Auto-play's gap shows what's coming.** Through the gap of silence between songs, the
  crossfader slides over to the next deck (the room is silent, so nobody hears it move) and that
  deck says *STARTS IN 3*, counting down, its light blinking, where ON AIR shows. Grab the
  crossfader during the gap and it's yours: auto-play leaves it where you put it.
- **The time readout blinks in a song's last 30 seconds,** as on CDJs: on the deck the room
  hears, counted to where its music ends (after trimmed silence, with the tempo and armed jumps
  taken into account). Time to have the next song ready.

Changed:

- **The dividers between the folders, library and queue have a grip** to show where to drag
  them, and in touch mode a finger needn't land exactly in the gap.
- **A deck's number is printed above its title,** as *DECK 1* in the deck's color, with ON AIR
  beside it while the room hears it, rather than in a colored square beside the title. The album
  art has the corner instead, and the title and artist have more room. With four decks it's just
  the number, and ON AIR sits just before the EQ key.
- **The close-up scrolls more smoothly zoomed in tight.** A deck reports where it is in uneven
  steps; the close-up used to make each small correction in one frame, a visible jerk when a
  couple of seconds fill the screen. Now each is spread over the next moments, and no single
  report can pull it far, so the music glides past (about five times steadier, measured), and
  it stays as close to what the room hears. Zoomed right in, the shape is smoother too.
- **Flat** in the EQ panel now puts everything back: the bands and gain at 0 dB, the mid at
  1 kHz, and evening out off.

## 0.5.0 (2026-10-23)

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
- **A startup screen.** The logo shows the moment GZJam starts, just where its window will be,
  while it opens your library and outputs; then the decks appear. It's there only as long as that
  takes (about a second), never longer. On Windows and Mac; on Linux GZJam starts as before, for now.

Changed:

- **GZJam is ready about a second sooner** from when you start it: the downloads now come
  compiled ahead of time, rather than compiling as GZJam starts.
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
  double-click it to maximize; minimize, maximize and close sit at its end. Linux keeps its own
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
