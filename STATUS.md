# Tm2Ac Status

_Last updated: 2026-09-30_

## Current state

| | |
|---|---|
| **Phase** | 2 is ✅ complete. Next is **Phase 3: TMNF asset extraction & mesh cache** |
| **Release** | none |
| **Next up** | Pak reader in `Tm2Ac.Assets` (nested ref resolution), block → mesh/material/collision extraction, internal mesh cache, `tm2ac assets extract/status` |
| **Blockers** | none |
| **Repo** | https://github.com/costbubbles/TM-track-exporter (branch `main`) |

## Phase progress

| Phase | Status |
|---|---|
| 0 Foundation & spikes | ✅ done (S5 handedness is deferred to Phase 1 by design) |
| 1 Hello AC (synthetic track) | ✅ done (user verified all in-game checks 2026-10-01) |
| 2 TMX client + TMNF parsing | ✅ done (2026-10-01) |
| 3 TMNF asset extraction | ⬜ next |
| 4 Geometry → first TMNF track in AC | ⬜ |
| 5 Route, timing, spawns, pits | ⬜ |
| 6 AI line, UI assets, CSP config | ⬜ |
| 7 Compatibility → v0.1 (CLI) | ⬜ |
| 8 Desktop app → v0.2 | ⬜ |
| 9 TM2020 → v0.3 | ⬜ |

## Research spikes

| Spike | Topic | Verdict |
|---|---|---|
| [S1](docs/research/S1-kn5-format.md) | KN5 format | 🟢 Layout verified byte-exact on real tracks. Write v6 |
| [S2](docs/research/S2-tmnf-assets.md) | TMNF pak/asset extraction | 🟢 GBX.NET.PAK decrypts Stadium.pak. Block→solid→mesh/collision/material works. DDS on disk |
| [S3](docs/research/S3-tmx-api.md) | TMX API v2 endpoints | 🟢 All endpoints verified. TM2020 replays are rare |
| [S4](docs/research/S4-ghost-parsing.md) | Ghost/replay parsing | 🟢 TMNF ghosts: 100 ms samples, CP times, wheel contact |
| [S5](docs/research/S5-conventions.md) | Coordinate & dummy conventions | 🟢 AC side confirmed in game (Phase 1). TM side predicted identity (no mirror), to confirm on a real map in Phase 4 |
| [S6](docs/research/S6-fast-lane-ai.md) | fast_lane.ai format | 🟢 v7 verified. Acceptance without the spatial grid is still untested |
| [S7](docs/research/S7-ac-track-requirements.md) | AC track folder / CSP | 🟢 Folder, inis, CSP extended surfaces and the mesh-naming rule |
| S8 | TM2020 asset access | ⬜ Phase 9. Openplanet is already installed on the dev machine |

## Decision log

| Date | Decision |
|---|---|
| 2026-09-30 | Sources: TMNF/TMUF + TM2020, with **TMNF (Stadium) first**. TM2020 follows in v0.3 |
| 2026-09-30 | Form: shared C# core + CLI + WPF desktop GUI. Install into AC `content/tracks` for CM to pick up |
| 2026-09-30 | Stack: C# / **.NET 10 LTS** (changed from .NET 8, which reaches end of support in Nov 2026), GBX.NET for Gbx, WPF for GUI |
| 2026-09-30 | Geometry: **extract real meshes/textures from the user's TM install**. Nothing redistributed |
| 2026-09-30 | TM-only features: convert anyway, rate compatibility (Green/Yellow/Red), warn and approximate surfaces |
| 2026-09-30 | KN5: our own writer. No ksEditor/Blender in the pipeline |
| 2026-09-30 | MVP track features: start/finish + timing, pits + spawns, AI line, preview/outline/map + ui_track.json |
| 2026-09-30 | CSP is required for output tracks |
| 2026-09-30 | Scale: user-selectable uniform scale, default 1.0, with presets |
| 2026-09-30 | Distribution: open source on GitHub under **GPL-3.0-or-later** (forced by GBX.NET.LZO). Chosen by the user over writing our own MIT LZO decoder |
| 2026-09-30 | Tests: xUnit v3 on Microsoft.Testing.Platform (the .NET 10 `dotnet test` default). Exit code 8 (zero tests) is ignored until the projects have tests |
| 2026-09-30 | Output layout: visual `<trackId>.kn5` + `collision.kn5` (non-renderable, `1<KEY><NNNN>` names) via `models.ini` |
| 2026-09-30 | Surfaces: enable CSP extended surfaces (`WAV_PITCH=extended-0`) and use `_EXT_SURFACE_TYPE` for ice/grass/sand |
| 2026-09-30 | TM2020 AI-line default is centerline, because TMX rarely hosts TM2020 replays. The user can supply a local ghost |
| 2026-09-30 | GitHub repo: `costbubbles/TM-track-exporter` (public). The product and code name stays "Tm2Ac" for now, and the final product name is still open |
| 2026-10-01 | TMX search: `primarytype` is single-valued, so the default is Race (0), and circuits are found via tag Multilap |
| 2026-10-01 | Waypoint detection uses a table generated from Stadium.pak `WayPointType`, never name matching |

## Open questions

- **Project name.** "Tm2Ac" is a placeholder.
- **Scenery default.** Should stadium stands/decoration be `full` by default, or `minimal` for performance?
- **TM2020 asset route.** Is requiring Openplanet for extraction acceptable? (Decide after S8.)
- **Timing of TMUF non-Stadium environments.** Currently scheduled after v0.3.

## Reference maps
See [tests/reference-maps.md](tests/reference-maps.md): R1–R7, from the simplest A-to-B (18451) to the expected-Red loop map (924307).

## Phase 1 in-game checklist (`Tm2Ac Test Circuit (L)`): ✅ all passed 2026-10-01

Regenerate or reinstall with `dotnet run --project src/Tm2Ac.Cli -- dev test-track`.

- [x] Track appears in Content Manager with name, preview (yellow L shape) and outline
- [x] Loads in AC with CSP, with no error or crash
- [x] Practice/hotlap: car spawns on the road facing the "▲ TM2AC" start decal and checkered line
- [x] **Blue "LEFT ▶" wall is on your left, red "◀ RIGHT" wall on your right, and arrows point the way you drive**
- [x] Text on the walls and on the road reads normally (not mirrored, not upside down)
- [x] Corner sequence from the start: **right, right, right, LEFT, right, right**
- [x] The road is visible from above (not see-through), and grass and walls render
- [x] The car drives on road, grass slows it down, and walls stop it
- [x] Lap time registers on crossing the line, and sector times show (3 sectors)
- [x] Minimap: the car dot moves the same way you drive
- [x] Race mode: cars line up on the grid behind the line. Pits: return to pits works

## Acceptance checklist (per release, per reference map)

- [ ] Appears in Content Manager with correct name, preview and outline
- [ ] Loads in AC (with CSP) without errors
- [ ] Car spawns on the road, facing the race direction
- [ ] Not mirrored, and layout matches TMX screenshots
- [ ] Lap (circuit) or A-to-B (time attack) timing registers, and sectors register
- [ ] Surfaces feel plausible (road/dirt/grass/ice)
- [ ] Pits: enter, stop in box, exit
- [ ] AI completes a lap or run
- [ ] Conversion report matches what you see in game (warnings are accurate)

## Work log

- **2026-09-30:** Project defined. Wrote CLAUDE.md, SPEC.md, PLAN.md and STATUS.md from the requirements Q&A.
- **2026-09-30:** Phase 0.
  - Installed the .NET 10 SDK.
  - Scaffolded the solution, central packages, CI workflow, GPL-3.0 LICENSE and gitignore. Build and `dotnet test` are green.
  - Ran spikes S1–S7 against the real AC install (129 tracks, CSP 0.3.0-preview445), TMX, and a fresh TMNF install.
  - Saved the probe programs to `tools/spikes/`.
  - Updated SPEC with the verified facts.
- **2026-10-01:** Created the public repo https://github.com/costbubbles/TM-track-exporter and pushed `main`. CI first failed
  because `.gitignore`'s `*.Gbx` and `*.kn5` patterns also hid the `src/Tm2Ac.Gbx/` and `src/Tm2Ac.Kn5/` folders (Windows git is
  case-insensitive). Fixed with `!*/`, verified from a fresh clone, and CI is now green.
- **2026-10-01:** Phase 1 code.
  - Wrote the KN5 writer/reader, track-folder writer, geometry builders, Steam/AC detection and `tm2ac dev test-track`. 32 tests pass.
  - Verified more formats against real tracks: triangle winding, `lod 0-0` = unlimited, and map.ini semantics (WIDTH = PNG pixels,
    pixel = (world + offset) / scale). AC's default surfaces have no WALL.
  - Ghost steering analysis predicts no TM→AC mirroring (S5).
  - Installed `tm2ac_test_lcircuit` into AC. Waiting on the in-game check.
- **2026-10-01:** The user drove `tm2ac_test_lcircuit`: everything works (walls/sides, text, corner order, timing, sectors, pits, minimap). Phase 1 is closed. Started Phase 2.
- **2026-10-01:** Phase 2.
  - Wrote the TMX client (cache, retries, concurrency limit) and the TMNF map reader with the pak-generated waypoint table.
  - Added `tm2ac search/info/doctor` and picked reference maps R1–R7. `info` works on all 7.
  - 79 tests pass (5 live network tests).
