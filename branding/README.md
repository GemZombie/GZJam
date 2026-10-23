# GZJam logo

The logo is a waveform wrapped into a G, beside the name, whose letters are cut into bars like a
waveform. GZ is coloured as the part of a song already played, JAM as the part still to come.

Every file here is drawn by the developer tool, from the same shapes the app draws
(`src/GZJam.App/Controls/LogoShapes.cs`). After changing them, run
`dotnet run --project tools/GZJam.DevTools -- icon .`, which also redraws the app icons.

| File | Use |
|---|---|
| `gzjam-icon.svg` | The app icon: the waveform G on its dark tile. Below about 44 px, use the app's own icon files (`gzjam.ico`, `gzjam.png`), where the ring is drawn solid so the G stays sharp. |
| `gzjam-mark.svg`, `gzjam-mark-light.svg` | The waveform G alone, for dark and light backgrounds. |
| `gzjam-logo.svg`, `gzjam-logo-light.svg` | Icon and name together: website header, README, videos. |
| `gzjam-wordmark.svg`, `gzjam-wordmark-light.svg` | The name alone, in bars. Use it at 24 px tall or more. |
| `gzjam-wordmark-solid.svg`, `gzjam-wordmark-solid-light.svg` | The name in solid letters, for when it's smaller than that (bars blur when small). |

The `-light` files are for light backgrounds.

**Colours**

| | On dark | On light |
|---|---|---|
| Played (GZ, the G's first half) | `#36C2DC`, Deck 1's colour in the app | `#1597B0` |
| Still to come (JAM, the rest) | `#C3CBD6` | `#2A303B` |
| Solid letters | `#E8ECF2` | `#151920` |
| Tile | `#1E232C` to `#0C0F14` | |

The letters are drawn, not typed, so the logo needs no font. Leave space around it of at least
the height of the G's bar.

`concepts/` holds the pages the logo was chosen from (#17).
