# Tm2Ac Status

_Last updated: 2026-09-30_

## Current state

| | |
|---|---|
| **Phase** | 0 is ✅ complete (exit criteria met). Next is **Phase 1: "Hello AC" synthetic track** |
| **Release** | none |
| **Next up** | `Tm2Ac.Kn5` writer + reader with round-trip test, INI/UI writers, then the synthetic oval + L-shaped track to load in AC. The L-shape settles the S5 handedness question |
| **Blockers** | none |
| **Repo** | https://github.com/costbubbles/TM-track-exporter (branch `main`) |

## Phase progress

| Phase | Status |
|---|---|
| 0 Foundation & spikes | ✅ done (S5 handedness is deferred to Phase 1 by design) |
| 1 Hello AC (synthetic track) | ⬜ next |
| 2 TMX client + TMNF parsing | ⬜ |
| 3 TMNF asset extraction | ⬜ |
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
| [S5](docs/research/S5-conventions.md) | Coordinate & dummy conventions | 🟡 AC side + TMNF grid done. TM→AC handedness is open (Phase 1 test) |
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

## Open questions

- **Project name.** "Tm2Ac" is a placeholder.
- **Scenery default.** Should stadium stands/decoration be `full` by default, or `minimal` for performance?
- **TM2020 asset route.** Is requiring Openplanet for extraction acceptable? (Decide after S8.)
- **Timing of TMUF non-Stadium environments.** Currently scheduled after v0.3.

## Reference data found so far
- TMNF-X **#924307** "[PF] Ph/\ntom Fake" (WR replay 7325831) is a PressForward map with loops and turbos, airborne 44% of the
  time. It's the expected **Red** reference. More reference maps (simple circuit, A-to-B tech, dirt) will be added in Phase 2
  (`tests/reference-maps.md`).

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
