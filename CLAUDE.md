# CLAUDE.md

Guidance for Claude Code when working in this repository.

## What this project is

Repo: https://github.com/costbubbles/TM-track-exporter (GPL-3.0-or-later)

**Tm2Ac** (working name) is a Windows desktop tool and CLI that fetches tracks from Trackmania Exchange,
converts them into Assetto Corsa tracks, and installs them so Content Manager picks them up.

- Sources: **TMNF/TMUF** (tmnf.exchange / tmuf.exchange) first, then **Trackmania 2020** (trackmania.exchange).
- Geometry comes from the **user's own Trackmania install**. Block meshes and textures are extracted locally and cached.
- Output is a complete `content/tracks/<id>/` folder: a KN5 we write ourselves, the data inis, an AI line, UI files,
  and a CSP `ext_config.ini`.
- CSP (Custom Shaders Patch) is a **hard requirement** for output tracks.

Read these before doing work:
- [SPEC.md](SPEC.md) is the source of truth for *what* we build: formats, mappings, conventions and requirements.
- [PLAN.md](PLAN.md) covers *how and in what order*: phases, tasks and research spikes.
- [STATUS.md](STATUS.md) shows *where we are*: current phase, recent work, blockers and open decisions.

## Stack

- C# / .NET 10 (LTS), on Windows only. The SDK is pinned in `global.json`.
- GBX.NET 2.x for Gbx parsing (maps, ghosts/replays, block/solid/material assets):
  - `.LZO` decompresses map bodies. It's **GPL-3.0, which is why this repo is GPL-3.0**.
  - `.ZLib` handles zlib-compressed parts.
  - `.PAK` decrypts TMNF `.pak` files.
- System.CommandLine for the CLI, and WPF + CommunityToolkit.Mvvm for the GUI.
- SkiaSharp for generated images (map.png, outline.png, preview).
- xUnit v3 on Microsoft.Testing.Platform, opted in via `global.json`.
- Package versions are central, in `Directory.Packages.props`. Shared build settings are in `Directory.Build.props`
  (warnings as errors, nullable).

## Repository layout (target)

```
src/
  Tm2Ac.Core/        IR (intermediate track representation), pipeline orchestration, settings
  Tm2Ac.Tmx/         TMX API clients (TMNF-X/TMUF-X, TMX 2020), download, metadata
  Tm2Ac.Gbx/         Map + ghost parsing via GBX.NET -> IR
  Tm2Ac.Assets/      TM pak filesystem (hashed names, recursive refs), block/material/collision extraction (on demand)
  Tm2Ac.Geometry/    Placement transforms, scaling, mesh merge/split, physics mesh build, surface mapping
  Tm2Ac.Kn5/         KN5 writer (+ minimal reader for round-trip tests)
  Tm2Ac.AcTrack/     AC track folder writer: inis, dummies, map/ui (no AI line: SPEC §7.5)
  Tm2Ac.Pipeline/    TM→AC conversion: block placement, surface map, material translation, scene builder, converter
  Tm2Ac.Cli/         `tm2ac` command-line entry point
  Tm2Ac.App/         WPF desktop app
tests/
  Tm2Ac.<Project>.Tests/
data/
  surface-map.tmnf.json, block-flags.tmnf.json   (data-driven mappings, embedded as resources)
docs/research/       Findings from research spikes (one md per spike)
tools/spikes/        Phase 0 probe programs (not in Tm2Ac.slnx)
```

## Commands

```
dotnet build                                                   # build everything
dotnet test --filter-not-trait "Category=Network" --filter-not-trait "Category=Assets"   # offline unit tests (what CI runs)
dotnet test --filter-trait "Category=Network"                  # opt-in tests that hit TMX
dotnet run --project src/Tm2Ac.Cli -- convert tmnf <trackId> --install
dotnet run --project src/Tm2Ac.App
```

Traits: `[Trait("Category", "Network")]` for TMX calls and `[Trait("Category", "Assets")]` for tests needing a local TM install.

## Dev machine paths
| What | Path |
|---|---|
| AC (with CSP 0.3.0-preview445) | `F:\SteamLibrary\steamapps\common\assettocorsa` |
| TMNF | `F:\SteamLibrary\steamapps\common\TrackMania Nations Forever`. Meshes are in `Packs\Stadium.pak`, DDS in `GameData\Stadium\Media\Texture\Image` |
| TM2020 (Openplanet installed) | `F:\SteamLibrary\steamapps\common\Trackmania` |

## Research and probes
- `docs/research/S*.md` hold the verified format facts. Read the relevant one before touching KN5, AI, pak or TMX code.
- `tools/spikes/` has standalone probe programs (not in the solution) that read real KN5, fast_lane.ai and TMNF pak files.

## Hard rules

1. **Never commit Nadeo/Ubisoft assets** or anything extracted from a TM install: meshes, textures, pak contents
   or cache files. **Never commit converted AC tracks either.** The converted output contains Nadeo assets and is for the user's
   personal use only.
2. **Never commit other people's maps or replays** as test fixtures. Use maps the user authored, synthetic
   fixtures built in code, or fetch from TMX at test time in `Category=Network` tests. The `.gitignore` blocks
   `*.Gbx`, `*.pak`, `*.kn5`, `*.dds` and `*.ai` as a safety net.
3. TMX API: always send a descriptive `User-Agent` (see SPEC §3), respect rate limits, and cache responses.
4. Keep mappings data-driven. Surface tables, unsupported-block lists and similar data live in `data/*.json`, not in code.
5. When a format detail is uncertain (KN5 fields, AC dummy orientation, TM coordinate handedness), verify it with
   a test or an in-game check. Record what you found in `docs/research/` and fix SPEC.md if it was wrong.

## Conventions

- All geometry inside the pipeline uses **AC space**: meters, Y-up. Conversion from TM space happens once, in
  `Tm2Ac.Geometry` (`TmToAcTransform`). Mirrored tracks are the classic bug, so test handedness explicitly.
- The IR (`TrackScene`) is game-agnostic. Parsers produce it and the AC writer consumes it. Neither side knows
  about the other.
- Strip TM text formatting codes (`$o`, `$f00`, `$l[...]` and so on) from every user-facing name.
- AC track folder IDs follow `tm<game>_<tmxId>_<slug>`, for example `tmnf_123456_speed_canyon`.
- Every conversion produces a `conversion-report.json` (warnings, compatibility, options used) in the output folder.

## Keeping docs current

- After finishing a task, tick it in PLAN.md and add a dated line to STATUS.md's log.
- If a decision changes, update SPEC.md *and* add a line to STATUS.md's decision log.
- Each research spike gets a `docs/research/<spike-id>-<topic>.md` with its findings and a clear verdict.
