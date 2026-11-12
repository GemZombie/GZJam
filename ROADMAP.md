# GZJam: what it does, and what's next

GZJam is a free DJ app for social dance nights: swing, West Coast, blues, ballroom and more, on
Windows, Mac and Linux. Simple, intuitive and useful: what you need to play music for dancers,
without the clutter. This page is the big picture: what
GZJam can do today, what's coming, and what's being considered. It's brought up to date after each
release.

**Up to date as of 1.7.0** (11 November 2026). [Download the latest release](https://github.com/GemZombie/GZJam/releases/latest) ·
[what changed in each release](CHANGELOG.md) · [ideas and questions](https://github.com/GemZombie/GZJam/discussions) ·
[the Facebook group](https://www.facebook.com/groups/2178840716051629/)

## What GZJam does today

**Library**
- Your music folders are the library: a folder tree, with search covering everything under the
  folder you pick. Nothing to import, and GZJam never changes your music files unless you ask it
  to: by moving songs in Organize music, dragging one from the queue to a folder and saying yes, or
  writing your tag edits into them.
- The library's sources, a click apart: **My music** (your music folders), **Servers** (your own
  music servers, below) and **Files** (the computer's places and drives, and any USB stick). A
  request on a stick plays without leaving GZJam, and pulling the stick out never stops the room.
  Each song in the queue, History and a playlist says where it's from.
- Organize music: two sides under the decks, to copy and move songs between your music, the
  computer's folders and USB sticks, with hearts, cues and playlists going along, and an Undo.
  *Add to my music* brings songs in from a server, Files or a stick, into the folder you choose,
  and a song dragged from the queue to a folder in My music goes there.
- Search filters: `bpm:130-150`, `~bpm:140` (within reach of the tempo slider, or as near as you choose), `artist:`,
  `year:`, `label:`, `rating:`, `energy:`, `played:` (`-played:7` leaves out the songs the room heard last week)
  and `file:`. Words in quotes are a phrase, and `OR` finds either of two (`label:balboa OR label:charleston`).
- Search puts the best matches first (title and artist), and songs that only mention your words in a
  comment last. Filters are suggested as you type, with what your library holds; a Filter button
  taps them in, a **?** shows how to search, and a typo that finds nothing gets "Did you mean...?"
- Choose the song list's columns, Grouping and Comment among them (as iTunes, Music and many
  other players keep them in the song files), and what the queue shows of each song. The queue's
  columns can be dragged into another order and width, and a narrow queue (or a thin song list
  column) gives way a part at a time rather than overlapping.
- Every tag in your song files, not only the usual ones (the composer, a mood, the dance in a tag
  of your own): shown as columns you choose, and searched (`composer:ellington`, `dance:waltz`).
- Song info (right-click a song, or Ctrl+I): everything about it in one window, and its tags
  edited there, for one song or many at once (an artist's spelling across every song a search
  finds), with Undo: kept in GZJam's library, or written into the files too if you turn that on.
- Mark songs with a heart or broken heart, an energy level (low, medium, high), and your own labels,
  which show on the decks and queue too, and can be renamed.
- A queue, and a preview player on every song, to listen in the headphones without loading a deck,
  as the deck will play it: with its EQ and loudness matching.
  A song added to the queue or a playlist twice is still added, but GZJam says so, with Undo.
  Ctrl+L gives the queue the room in the middle, for a night spent reordering a long one.
- Playlists: plan a set before the night, in order, saved by name, with songs from anywhere,
  even outside your music folders (they stay out of the library). Drag songs onto one (or add
  the song on a deck, or songs in the queue, from their menu), reorder it, and add it to the queue,
  shuffled if you like, or play it in one step, in place of the queue. The queue can be shuffled
  too, with Undo. The queue or a night in History can be saved as one, or copied
  out as files, in order, to a folder or USB stick. Fold the folders away, or drag the playlists'
  grip, for a longer list of them.
- A song renamed or moved (in a tag editor, say), even to another of your music folders, keeps its
  marks, labels, cues, playlists and plays; one in the queue or on a deck follows it.
- Songs can be dragged straight from the file manager onto a deck or into the queue, even ones
  from outside your music folders (a song on a dancer's USB stick), and onto a playlist.
- Back up your library (hearts, labels, cues, playlists, History and Settings) to one file, and
  restore it, on another computer too, with the music somewhere else (GZJam asks where); GZJam
  also keeps a copy of its own each day it's opened.
- Start over with an empty library whenever you like; the old one is kept, not deleted.
- Album art on the decks and queue (and a library column), from the songs' files or the covers in
  their folders, to see at a glance that the right record is loaded.
- Artist and title from file names, for old transfers and rips with empty tags but good names, and
  *Fill tags from file name* in song info to keep them, with Undo.

**Your own music server**
- Navidrome, or another server that speaks the Subsonic API, one or several (yours and a
  friend's), at home or over Tailscale: browsed and searched in the library like your own folders,
  offline too. [The guide](docs/GUIDE.md#your-own-music-server) covers setting one up.
- Its songs on the computer before the night (its playlists, and what's starred there, kept in
  step on every sync), so the room never depends on the Wi-Fi; or a song downloaded on the spot
  for the queue or a deck, waiting in its place until it's here. A Downloads window shows every
  song coming down, and why.
- A heart stars the song on the server. Downloads can go by themselves after the night or after 4
  weeks, make room on a small drive, or move to another drive, and songs moved on the server follow
  here. Add songs, or the whole server in the background, to your music for good.
- Send a playlist of yours to the server, in its order, and share a link to it for the setlist the
  next day.

**Decks**
- Two decks, or four in the same space.
- Double-click a song to put it on the deck that plays next, never the one on air.
- Compact decks for a laptop: smaller decks, so the library and queue show more songs. Or hide
  the decks, for the most songs on screen: they sit at the top of the queue, in the order the room
  will hear them, the song it hears now with its play key and time left, and what you've chosen
  the queue shows of each song, lined up with it. A switch in the top bar
  picks full, compact or hidden in one click (or a key, or the grip under the decks), as often as
  you like during a night.
- The deck the room hears has a red on-air bar that fills as the song plays.
- Tempo with key lock, a waveform of the whole song, one-button fade-out.
- A song with a long or repetitive outro fades out where you set it (*Song ends › Start fade-out
  here*), rather than stopping dead, with auto-play or playing by hand.
- Per-song EQ, with a movable mid band, and EVEN to bring an old recording's quiet and loud
  passages closer together: on a deck, or for any song from its right-click menu while preparing
  a set.
- A close-up of the song around the playhead, scrolling past beat by beat, to see the end of a
  phrase coming and find the exact spot for a cue or a jump. Zoom with the mouse wheel or a pinch;
  drag it like a record. It glides smoothly, even zoomed in tight.
- The time readout blinks in the last 30 seconds of the song the room hears, as on CDJs.
- Protection against loading a new song over one the room is hearing. Once you've crossfaded over,
  the other deck takes the next song while it plays on quietly, with no need to pause it.
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
- Nothing GZJam does is heard in the room: its sound processing runs as native code, so the music
  never waits while GZJam tidies its memory; songs play from memory, so a slow drive or an
  unplugged USB stick can't interrupt one; and fades, the crossfader and the end of a song are
  handled by the sound itself, so a busy screen never holds them up. Protection against crackles
  has four levels, and every dropout is written in the activity log.
- Auto-play through the queue, with a gap of real silence between songs. Through the gap the
  crossfader slides over to the next deck, which counts down to starting. For classes and practice
  nights it can fade each song out after a set time, or wait for you between songs; and it can
  stop at a set time, so the night ends on time, or after this song, for an announcement, with
  the next song ready for when you press play. Repeat sends each song back to the end of the
  queue, so a playlist plays over and over, or plays one song again and again. Load another song
  onto the deck waiting to play, and the one that was there goes back to the top of the queue.
- On Windows the speakers can be GZJam's alone (a switch, for a laptop kept for gigs), so no
  notification sound reaches the room. On Linux a sound card can be used directly, for the same reason.
- Headphones (or the booth) unplugged during a night go quiet, never move to the speakers, and come
  back by themselves when plugged in again. Speakers unplugged mid-song are noticed within a couple
  of seconds, and the music plays through the computer's default output; plugged in again, it
  moves back by itself as soon as nothing is playing to the room.

**During the night**
- History: every night saved as a session, with set lists to export as text or a spreadsheet.
- When the queue ends, beside the clock: the time the music queued runs out, counting the gaps,
  song ends and fades, so you can see whether it reaches the end of the night.
- The room's song from the Windows taskbar or the Mac Dock: pause, fade out (the next song, with
  auto-play) and auto-play, without switching to GZJam.
- GZJam asks before closing while the room is hearing music. Settings has a Quit key too, for Linux
  desktops that draw no title bar.
- If GZJam crashes or the computer restarts mid-dance, the night comes back: the queue and the decks
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
- Installs as each system expects: an installer (or a `.zip`) on Windows, a `.dmg` on Mac, and on
  Linux a `.deb`, an `.rpm` or an Arch package, or a `.tar.gz` for any other.
- GZJam says quietly, at startup, when a new version is out (never during a set), and a
  [user guide](docs/GUIDE.md) covers everything, from your first song to plugging into the venue's
  mixer or PA and a checklist for the night.

**Coming from other players**
- From Mixxx: ratings, crates (as labels), playlists, history, locked BPMs and hot cues.
- From MediaMonkey, foobar2000, iTunes and most other players: playlists saved as M3U, or as text
  from iTunes (even ones made on another computer, with the music somewhere else), and the star
  ratings they keep in the song files.
- The other player's files, and your music, are never changed.

Plays MP3, FLAC, WAV, AIFF, OGG Vorbis, Opus and Apple Lossless everywhere, AAC on Windows and Mac, and WMA on Windows.

## Finished, coming in the next release

**In 1.8**, not in a release yet:

- [The Time column](https://github.com/GemZombie/GZJam/issues/379) in the column menu, and next
  to Artist so it's in view on a laptop
- [The song list keeps its sort](https://github.com/GemZombie/GZJam/issues/376): the library
  remembers one, and each playlist its own, between runs too
- [Queue the song you're previewing](https://github.com/GemZombie/GZJam/issues/386) from the
  preview bar, by a key, a drag or its menu, after the list has moved on to another folder
- [One clear selection](https://github.com/GemZombie/GZJam/issues/377): only the list you're
  working in shows it strongly, with more contrast, and the open folder or playlist marked as
  where you are
- [No disk or device checks holding up the screen](https://github.com/GemZombie/GZJam/issues/367):
  the activity log has a writer of its own, and USB sticks and speakers are looked for in the
  background
- [The close-up waveform keeps gliding](https://github.com/GemZombie/GZJam/issues/364) when the
  computer is busy: drawn alongside the screen, not by the window
- [The decks' readouts do only the work that shows](https://github.com/GemZombie/GZJam/issues/365):
  updated when what they show changes, not 30 times a second for every deck
- [Waveforms and meters drawn only when they show something new](https://github.com/GemZombie/GZJam/issues/366)
- [First-time analysis goes gently while music plays](https://github.com/GemZombie/GZJam/issues/369):
  one core, not three, and a deck's song read from memory and decoded once
- [Big libraries listed quickly](https://github.com/GemZombie/GZJam/issues/368): a list of 60,000
  songs is sorted where the library reads it, not on the screen's time
- Curating new music in Files: [sort a folder by when songs arrived](https://github.com/GemZombie/GZJam/issues/383),
  newest first; [move the keepers into My music](https://github.com/GemZombie/GZJam/issues/384)
  rather than copy them; and [delete the rest](https://github.com/GemZombie/GZJam/issues/385),
  to the Recycle Bin
- [Preview the song on a deck](https://github.com/GemZombie/GZJam/issues/378) in the headphones,
  by button or key, without moving the deck
- [When each song in the queue plays](https://github.com/GemZombie/GZJam/issues/297): a *Plays at*
  column, counting fade-outs, jumps and auto-play's gaps, and a start time to plan a night from
- Servers, smaller: [reading a server's song list again](https://github.com/GemZombie/GZJam/issues/395)
  only if it has read its music since, and [sending a playlist](https://github.com/GemZombie/GZJam/issues/396)
  to a server that already has one by that name asks before replacing it
- [Auto-play turns CUE off](https://github.com/GemZombie/GZJam/issues/412) on the deck it starts
- [Opening a folder by its arrow](https://github.com/GemZombie/GZJam/issues/413) keeps the folder
  tree where it was
- [Playlists as a source](https://github.com/GemZombie/GZJam/issues/423), with the whole library
  column, rather than sharing it with the folder tree
- [Your servers' playlists in the Playlists panel](https://github.com/GemZombie/GZJam/issues/355),
  each under its server, in folders built from their names as Navidrome apps do, and sent back the
  same way
- [Playlist folders](https://github.com/GemZombie/GZJam/issues/354): your playlists in folders,
  nested, folded away, and brought in from a folder of M3U files with its folders
- [The waveform, times and meters go with the music](https://github.com/GemZombie/GZJam/issues/417),
  not ahead of it by what's in the output's buffers
- [No memory piling up over a night](https://github.com/GemZombie/GZJam/issues/407): the folder
  tree's old rows let go
- [The sound first in line on a busy Windows computer](https://github.com/GemZombie/GZJam/issues/381):
  GZJam's mixing in Windows' class for pro audio, and a dropout's log line saying whether it was
  kept waiting

## Planned next

**1.7 is out** (11 November 2026), the first release for everyone since 1.3.0: your own music
server, the library's sources with Files and USB sticks, Organize music, and no stutters. Fixes
come as 1.7.1, 1.7.2 and so on. Found something? Say so in
[Discussions](https://github.com/GemZombie/GZJam/discussions) or
[the Facebook group](https://www.facebook.com/groups/2178840716051629/).

**1.8: quality of life,** with ideas from DJs and from Danny Cherry's fork: all in, see above.

**After that:**
- [Your own keyboard shortcuts](https://github.com/GemZombie/GZJam/issues/135): a key for each
  action, chosen in Settings, with clashes shown and a way back to the defaults
- [Group different versions of the same song](https://github.com/GemZombie/GZJam/issues/9), to
  choose between them when it's time to play one
- [iTunes / Music as a library source](https://github.com/GemZombie/GZJam/issues/97), kept in
  sync: its playlists, ratings, play counts and Grouping, next to your music folders
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
- [File songs by their tags](https://github.com/GemZombie/GZJam/issues/332): choose a pattern like
  Artist / Album / Title, see every new path first, and undo it if you don't like it
- [Smooth with very large libraries](https://github.com/GemZombie/GZJam/issues/73) of several
  hundred thousand songs or more
- [Compact library](https://github.com/GemZombie/GZJam/issues/75): see what's still kept for songs
  that are gone, and clean it up
- [A recording's condition at a glance](https://github.com/GemZombie/GZJam/issues/274): a grade
  from worn to clean, to choose between versions of a song

**Playing**
- [Color songs by rules](https://github.com/GemZombie/GZJam/issues/124): BPM ranges, energy, a
  label, or any search
- [Dance types on songs, for ballroom](https://github.com/GemZombie/GZJam/issues/103): playlists by
  a sequence of dances, or so many of each
- [The night ahead in the queue](https://github.com/GemZombie/GZJam/issues/269): a tempo curve,
  and gentle marks for the same artist twice or a big tempo jump
- [Double-click a song in the file manager](https://github.com/GemZombie/GZJam/issues/336): GZJam
  as your default player, the song on a deck, or in the headphones if the room is hearing something

**Sound**
- [EVEN found automatically](https://github.com/GemZombie/GZJam/issues/271): the uneven songs found
  as they're analyzed, and EVEN suggested or set, never lifting a break
- [Room EQ for each venue](https://github.com/GemZombie/GZJam/issues/272): less boom for a big hall,
  more warmth for a packed room, saved by venue and loaded when you arrive
- [Plug in your speakers after starting GZJam](https://github.com/GemZombie/GZJam/issues/338): MAIN
  goes back to the output you chose by itself, once it's connected and the room is quiet
- [Restore old recordings](https://github.com/GemZombie/GZJam/issues/275): clicks, crackle and hiss
  taken out of worn 78s, while playing or as a cleaned copy made ahead of the night

**Elsewhere**
- [A visiting or remote DJ](https://github.com/GemZombie/GZJam/issues/204): sign in to your own
  library on a borrowed laptop and leave nothing behind, or play a dance from somewhere else, the
  songs sent ahead to a laptop in the room
- [Two rooms](https://github.com/GemZombie/GZJam/issues/120): two GZJams on the same network, for
  schools with two floors, either one controlling the other
- [GZJam in other languages](https://github.com/GemZombie/GZJam/issues/102)
- [A companion app for cleaning up tags and file names](https://github.com/GemZombie/GZJam/issues/76):
  fill in missing titles, fix misspelled artists, rename files by rule
- [A tablet version](https://github.com/GemZombie/GZJam/issues/16), iPad first

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
