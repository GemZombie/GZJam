# GZJam

A focused DJ application for swing-dance DJing, without the clutter of club-DJ software.

## Stack

- .NET 10 + [Avalonia](https://avaloniaui.net/) (Windows & Linux)
- [BASS](https://www.un4seen.com/) via ManagedBass — playback, multi-output headphone cue, BASS_FX tempo/key-lock and BPM detection
- TagLib# — tag reading/writing
- SQLite — library, play history, notes

BASS is used under its free non-commercial license (personal use only).

## Layout

- `src/GZJam.Audio` — audio engine: devices, decks (tempo/key-lock), BPM detection
- `src/GZJam.AudioSpike` — throwaway console harness for testing the engine
- `lib/bass/<rid>` — native BASS + BASS_FX libraries (x64), copied to output at build

## Audio spike

```
dotnet run --project src/GZJam.AudioSpike
```

Type `devices`, then `main <#>` and `cue <#>` to route the decks (remembered between runs),
then `load m <file>` / `load c <file>`. Type `help` for all commands.
