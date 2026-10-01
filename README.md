# TM-track-exporter (Tm2Ac)

Convert **Trackmania Nations/United Forever** tracks from [Trackmania Exchange](https://tmnf.exchange) into
**Assetto Corsa** tracks, including the real block meshes and textures from your own Trackmania install, collision
surfaces, timing, a start line spawn and Content Manager previews.

> **Status:** v0.1 (command line) is released. The desktop app is in testing for v0.2, and Trackmania 2020 support (v0.3) is
> planned. See [PLAN.md](PLAN.md) and [STATUS.md](STATUS.md).

## Requirements

- Windows 10/11
- **Assetto Corsa** with **[Custom Shaders Patch](https://acstuff.club/patch/)** (converted tracks need CSP)
- **TrackMania Nations Forever**. It's free on Steam (app 11020). Its game files are the source of all meshes and textures.
- Optional: [Content Manager](https://assettocorsa.club/content-manager.html)

## Desktop app

Run `Tm2Ac.exe` from the [releases](https://github.com/costbubbles/TM-track-exporter/releases). Browse TMNF-X, pick a
track, choose a scale and press **Convert & Install**. The Library page lists your converted tracks (re-convert, uninstall,
open folder), and Settings shows what was detected.

## Quick start (command line)

```powershell
tm2ac doctor                              # checks AC, CSP, TMNF and the cache
tm2ac search tmnf --tag Tech --limit 10   # find tracks on TMNF-X
tm2ac convert tmnf 18451                  # convert and install into AC's content/tracks
```

The track then shows up in Content Manager and AC as e.g. `tmnf_18451_always_be_mine`.

### Useful options

| Option | Meaning |
|---|---|
| `--scale 1.5` | Scale the whole track. TM tracks are built for very fast cars; 1.5–2 suits road and GT cars |
| `--out <dir>` | Write the track folder somewhere else instead of installing it |
| `--zip <file.zip>` | Also produce a zip you can drag into Content Manager |
| `--replay <id or file>` | Use a specific replay for checkpoint order and the minimap (default: the fastest TMX replay that isn't faster than the gold medal, since world records often use skips) |
| `--pitboxes <n>` | Grid slots and pit boxes. Default 1 (everyone starts at the start line); more adds a grid behind it |

## What gets converted

| Trackmania | Assetto Corsa |
|---|---|
| Blocks (road, platforms, decoration) | Visual meshes with the original DDS textures (KN5) |
| Physics surfaces (asphalt, dirt, grass, ice-like, ...) | `surfaces.ini` + hidden collision meshes; steep surfaces become walls |
| Start / checkpoints / finish | AC timing gates: start/finish + sectors for lap races, A-to-B gates for point-to-point |
| World-record replay | Track layout (circuit vs A-to-B), sector order, minimap |
| TMX metadata and screenshot | `ui_track.json`, preview and outline for Content Manager |

Every track is ported as it is. Trackmania routes can need things Assetto Corsa cars can't do (loops, wall riding, jumps
built for 400+ km/h); the tool converts them anyway. If the replay jumps over gaps, the conversion warns you how fast the hardest jump needs
you to go. Details of every conversion are in `conversion-report.json` in
the track folder.

## Legal

This tool reads assets **from your own Trackmania installation** at conversion time. No Nadeo/Ubisoft assets are included
in this repository or its releases. **Converted tracks contain Nadeo assets: they are for your personal use only. Do
not redistribute them.** Track designs belong to their authors on Trackmania Exchange.

## Building from source

```powershell
dotnet build
dotnet test --filter-not-trait "Category=Network" --filter-not-trait "Category=Assets"
dotnet publish src/Tm2Ac.Cli -c Release -r win-x64      # single-file tm2ac.exe
```

Needs the .NET 10 SDK. Developer documentation is in [CLAUDE.md](CLAUDE.md), [SPEC.md](SPEC.md) and [docs/research](docs/research).

## License

GPL-3.0-or-later (see [LICENSE](LICENSE)). It uses [GBX.NET](https://github.com/BigBang1112/gbx-net) (MIT) and
GBX.NET.LZO (GPL-3.0).

## Known limitations

- **No AI.** Few Trackmania maps can be driven conventionally in AC, so no AI line is generated.
- **Hitting thin walls can occasionally drop the car through the map.** This is a limitation of AC's collision with thin geometry
  that many popular track mods share.
- TM has steep steps taller than a wheel hub (e.g. the raised start pad). AC tyres can clip into them when driven onto from the
  side, especially cars with minimal collision hitboxes (some mod cars).
- Boosters don't boost, and special surfaces are approximated. Loops, wall rides and some jumps aren't drivable with AC physics.
