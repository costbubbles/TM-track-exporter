# TM-track-exporter (Tm2Ac)

Convert **Trackmania Nations/United Forever** tracks from [Trackmania Exchange](https://tmnf.exchange) into
**Assetto Corsa** tracks, including the real block meshes and textures from your own Trackmania install, collision
surfaces, timing, grid and pits, an AI line and Content Manager previews.

> **Status: v0.1 in development.** TMNF Stadium tracks convert end to end from the command line. A desktop app (v0.2)
> and Trackmania 2020 support (v0.3) are planned. See [PLAN.md](PLAN.md) and [STATUS.md](STATUS.md).

## Requirements

- Windows 10/11
- **Assetto Corsa** with **[Custom Shaders Patch](https://acstuff.club/patch/)** (converted tracks need CSP)
- **TrackMania Nations Forever**. It's free on Steam (app 11020). Its game files are the source of all meshes and textures.
- Optional: [Content Manager](https://assettocorsa.club/content-manager.html)

## Quick start

```powershell
tm2ac doctor                              # checks AC, CSP, TMNF and the cache
tm2ac search tmnf --tag Tech --limit 10   # find tracks on TMNF-X
tm2ac analyze tmnf 18451                  # will it work in AC? (Green / Yellow / Red)
tm2ac convert tmnf 18451                  # convert and install into AC's content/tracks
```

The track then shows up in Content Manager and AC as e.g. `tmnf_18451_always_be_mine`.

### Useful options

| Option | Meaning |
|---|---|
| `--scale 1.5` | Scale the whole track. TM tracks are built for very fast cars; 1.5–2 suits road and GT cars |
| `--out <dir>` | Write the track folder somewhere else instead of installing it |
| `--zip <file.zip>` | Also produce a zip you can drag into Content Manager |
| `--replay <id or file>` | Use a specific replay for the AI line and checkpoint order (default: the TMX world record) |
| `--force` | Convert even if the track is rated Red |
| `--pitboxes <n>` | Number of grid slots and pit boxes (default 10) |

## What gets converted

| Trackmania | Assetto Corsa |
|---|---|
| Blocks (road, platforms, decoration) | Visual meshes with the original DDS textures (KN5) |
| Physics surfaces (asphalt, dirt, grass, ice-like, ...) | `surfaces.ini` + hidden collision meshes; steep surfaces become walls |
| Start / checkpoints / finish | AC timing gates: start/finish + sectors for lap races, A-to-B gates for point-to-point |
| World-record replay | Track layout (circuit vs A-to-B), sector order, minimap, hotlap start, compatibility rating |
| TMX metadata and screenshot | `ui_track.json`, preview and outline for Content Manager |

### Compatibility rating

Trackmania does things Assetto Corsa physics can't, such as loops, wall riding and very long jumps. `tm2ac analyze`
checks the world-record replay to see what the route actually requires:

- **Green**: should drive normally.
- **Yellow**: drivable with caveats, such as boosters that don't boost, big jumps or approximated special surfaces.
- **Red**: the route needs driving upside down, on walls, or multi-second flights. Not converted unless you pass `--force`.

Details of every conversion are in `conversion-report.json` in the track folder.

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
