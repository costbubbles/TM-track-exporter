# Tm2Ac Plan

How we get from an empty folder to SPEC.md. The phases are ordered to remove the biggest risks first. The riskiest
unknowns are the KN5 writer, TM asset extraction and coordinate conventions, so those are tackled before any GUI work.

Legend: `[ ]` todo · `[x]` done · `[~]` in progress · **S#** = research spike. Each spike writes its result to
`docs/research/S#-*.md`, ending in a verdict.

---

## Phase 0: Foundation & research spikes

**Goal:** a repo skeleton, plus answers to the questions that could sink the project.

- [x] `git init`, `.gitignore` (bin/obj, `*.Gbx`, `*.pak`, `*.kn5`, `*.dds`, `*.ai`, cache/output dirs), `.editorconfig`, `.gitattributes`
- [x] Solution (`Tm2Ac.slnx`) + 9 src and 7 test projects per CLAUDE.md layout. Includes `Directory.Build.props` (nullable,
      warnings-as-errors), central `Directory.Packages.props`, and `global.json` (.NET 10 SDK + Microsoft.Testing.Platform)
- [x] GitHub Actions: build + offline tests on `windows-latest` (`.github/workflows/build.yml`)
- [x] **License decision.** GBX.NET.LZO is GPL-3.0-or-later, so the project is **GPL-3.0-or-later**. LICENSE is added
- [x] **S1: KN5 format.** 🟢 Verified byte-exact on real tracks. Write v6, with collision in a separate non-renderable KN5
- [x] **S2: TMNF assets.** 🟢 Stadium.pak decrypts via GBX.NET.PAK. Block → mobil → solid → visual + collision + material
      chain confirmed. DDS are plain files in GameData. Turbo block exported to OBJ and rendered correctly.
      *Moved to Phase 3/4:* nested collision-material resolution, ground/air variants, multi-unit blocks, pillar/clip rules
- [x] **S3: TMX API.** 🟢 Endpoints, fields and enums verified. User-Agent is required. TM2020 replays are rare on TMX
- [x] **S4: Ghost parsing.** 🟢 TMNF WR ghost: 100 ms samples with position, rotation, inputs and per-wheel contact and material, plus CP times
- [~] **S5: Conventions.** 🟡 AC dummy/gate conventions verified, and the TMNF block→world mapping verified. **TM→AC handedness is
      open**, to be settled by the Phase 1 in-game test
- [x] **S6: fast_lane.ai.** 🟢 v7 layout verified on Magione. "No grid" acceptance is to be tested in Phase 6
- [x] **S7: AC track requirements.** 🟢 Minimal folder, models.ini, map.ini, surfaces.ini, CSP extended surfaces, and the
      `<digits><KEY><digits>` mesh-naming rule

**Exit criteria:** S1, S2 and S5 are answered with a green or workable verdict. **Met.** S5's remaining handedness question is
covered by Phase 1's L-shaped test by design.

---

## Phase 1: "Hello AC", a hand-built track

**Goal:** prove that our KN5 writer and folder writer produce a track AC loads, before any TM code exists.

- [x] `Tm2Ac.Kn5`: writer + minimal reader, with round-trip, byte-identical rewrite, header and real-file tests
- [x] `Tm2Ac.AcTrack`: writers for models.ini, surfaces.ini (incl. CSP extended), map.ini + map.png, outline/preview,
      lighting/groove/cameras.ini, ui_track.json, README and conversion-report.json. Refuses to overwrite folders it did not create.
      ext_config.ini is deferred to Phase 6 because nothing needs it yet
- [x] `Tm2Ac.Geometry`: `AcAxes` conventions, `MeshData` with a 65k splitter, `Centerline` (rounded polygons), ribbon/wall/ground builders
- [x] `Tm2Ac.Core`: Steam library + AC/CSP detection
- [x] CLI: `tm2ac dev test-track [--out DIR] [--ac-path DIR]`
- [x] Synthetic track: the oval and L-shape are **merged into one asymmetric L-shaped circuit**, `tm2ac_test_lcircuit` (1,010 m).
      It has ROAD/GRASS/WALL collision, a blue LEFT and red RIGHT wall, a "TM2AC ▲" start decal, 8 grid/pit slots, 3 timing gates and a hotlap start
- [x] Installed into AC (`content/tracks/tm2ac_test_lcircuit`)
- [x] **Manual check by the user** (checklist in STATUS.md), all passed 2026-10-01: shows in CM, loads with CSP, spawns face forward, walls on the
      correct sides, text and arrows readable, corner sequence R-R-R-L-R-R, laps and sectors time, pits work

**Exit:** a drivable synthetic track. S5 conventions are recorded in SPEC §6/§7.4.

---

## Phase 2: TMX client + TMNF map parsing

- [x] `Tm2Ac.Tmx`: search, track lookup, map/replay/image download, tags. Includes a disk cache (files forever, API 10 min, meta 1 day),
      at most 2 requests in flight, retry with backoff on 429/5xx/network errors only, and the required User-Agent
- [x] `Tm2Ac.Gbx`: TMNF map → blocks (coord, direction, ground/pillar/clip, variant), metadata, mood, laps/multilap and
      waypoints. The waypoint table `data/block-flags.tmnf.json` is **generated from Stadium.pak `WayPointType`** (18 blocks) and embedded
- [x] TM formatting-code stripper and track-ID slugger, both with tests
- [x] CLI: `search` (tags, multilap, order), `info` (TMX id or local file), and `doctor` (AC, CSP, TMNF + Stadium.pak/textures, TM2020 + Openplanet, cache)
- [x] `tests/reference-maps.md` with 7 reference maps, R1–R7
- [x] Tests: 79 total. 5 are network tests (live TMX + parsing R1/R3/R7) and run locally. CI runs the 74 offline tests

**Exit:** `tm2ac info tmnf <id>` prints metadata and the block summary for every reference map. ✅ Met (2026-10-01).

---

## Phase 3: TMNF asset extraction & mesh cache

- [ ] TMNF install discovery
- [ ] Pak reading (from S2), plus block info → variants (ground/air) → mesh + material + collision extraction
- [ ] Internal mesh cache format, keyed by game build + asset path, with `tm2ac assets extract|status`
- [ ] Material translation: TM material → KN5 material (shader choice, texture slots, alpha test)
- [ ] Collision: `CPlugSurface` mesh + physics material ID → surface key, via `data/surface-map.tmnf.json`
- [ ] Placeholder generator for missing assets (bounding box + `MISSING_ASSET` issue)

**Exit:** every block used by the reference maps is in the cache, or knowingly reported as missing.

---

## Phase 4: Geometry assembly, first real TMNF track in AC

- [ ] `TmToAcTransform` (from S5) + block placement (coord, direction, variant), with handedness tests
- [ ] Implicit geometry: pillars under air blocks, Stadium ground plane, clips (rules from S2)
- [ ] Scene builder: IR `TrackScene` from parsed map + cache
- [ ] Mesh batching by material × spatial chunk, the 65k-vertex split, and collision mesh naming `1<KEY>_<chunk>`
- [ ] Uniform scale option
- [ ] `tm2ac convert tmnf <id> --install` with a minimal route: start spawn only, no timing yet
- [ ] Manual check on the simplest reference map: it looks right, isn't mirrored, surfaces collide, and the car drives

**Exit:** a TMNF track drivable in AC (no timing yet).

---

## Phase 5: Route, timing, spawns & pits

- [ ] Gate extraction from start, finish and CP blocks (position, width, forward)
- [ ] Track type detection (circuit vs A-to-B) and `--layout` override
- [ ] Checkpoint ordering: from the replay (via S4), else a route-graph walk, else none + warning
- [ ] AC dummies: AC_TIME_n, AC_HOTLAP_START_0, AC_AB_START/FINISH
- [ ] Spawn/pit placement on existing road (checked against collision), or a generated pit platform + apron
- [ ] Manual check: circuit timing and sectors, A-to-B time attack, pits and grid

**Exit:** reference maps are timed correctly in AC hotlap and time-attack modes.

---

## Phase 6: AI line, UI assets, CSP config

- [ ] fast_lane.ai writer (from S6) with a round-trip test
- [ ] AI path from replay: resample, smooth, project to surface, compute speeds from curvature, raycast side distances
- [ ] Centerline fallback from the route graph
- [ ] Outline, map and preview generation (SkiaSharp), plus map.ini values
- [ ] Full `ui_track.json` from TMX metadata
- [ ] `ext_config.ini`: surfaces (ice/dirt), mood → lighting/time, night emissives
- [ ] `conversion-report.json` + `README_TM2AC.txt` in output, with `--zip` export
- [ ] Manual check: the AI completes laps on the circuit reference map, and the CM track page looks complete

**Exit:** all SPEC §7 features present for TMNF.

---

## Phase 7: Compatibility analysis → **v0.1 release (CLI)**

- [ ] `data/block-flags.tmnf.json`: loops, wallrides, boosters and special blocks
- [ ] Ghost-based heuristics: upside-down time, airtime/gaps vs scale
- [ ] Rating engine (Green/Yellow/Red) + `tm2ac analyze`, with conversion refusing Red unless `--force`
- [ ] README (install, usage, legal notice), and a GitHub release with a self-contained win-x64 CLI build
- [ ] Run the full acceptance checklist on all reference maps

**Exit:** v0.1 tagged.

---

## Phase 8: Desktop app → **v0.2**

- [ ] WPF shell (MVVM, CommunityToolkit.Mvvm), a DI host shared with the CLI core, and a CM-like dark theme
- [ ] Browse view: source switcher, search/filter/sort, thumbnails and tag-heuristic badges, with paging
- [ ] Map detail: screenshots, analysis on select (download map + best replay), options panel with scale presets, and Convert &
      Install with a progress log and cancel
- [ ] Library view: installed tracks (from `conversion-report.json`), re-convert, uninstall, open folder, open in CM
      (verify the `acmanager://` URI)
- [ ] Settings: paths with auto-detect, cache size/clear, asset extraction with progress, defaults
- [ ] First-run wizard: detect paths, check CSP, extract assets, show the legal notice
- [ ] Packaging: a self-contained single-file build or a zip release

**Exit:** v0.2 tagged.

---

## Phase 9: Trackmania 2020 → **v0.3**

- [ ] **S8: TM2020 assets.** Pick a route: Openplanet-extracted files, our own pack reader, or embedded-only + procedural fallback
- [ ] TM2020 map parsing: blocks (grid + free), variants/subvariants and items (anchored objects)
- [ ] Embedded items: unzip, parse `CPlugStaticObjectModel` / `CPlugSolid2Model`, collision + materials
- [ ] TM2020 surface/gameplay mapping (`data/surface-map.tm2020.json`), plus block-flags (loops, wallride, reactor,
      engine-off, no-steer, fragile, slow-mo, cruise)
- [ ] TM2020 ghost parsing for the AI line and CP order
- [ ] TMX 2020 in the GUI browser
- [ ] Acceptance on a TM2020 reference set

**Exit:** v0.3 tagged.

---

## Phase 10+: Later

- TMUF non-Stadium environments (Island, Bay, Coast, Desert, Rally, Snow). Each needs its block set extracted and checked.
- CSP Lua track scripts: booster pads that boost, and optional engine-off/no-steer zones **[VERIFY CSP allows physics
  scripts for offline tracks]**.
- Batch conversion (TMX campaigns/map packs), and auto-update when a TMX map changes.
- Multiple layouts per track (for example "original scale" + "GT scale" as two layouts of one track).
- Better AI line: optimisation pass, plus a CSP `ideal_line` export.
- TM2 (ManiaPlanet) support.

---

## Risk register

| Risk | Impact | Mitigation |
|---|---|---|
| ~~TMNF pak decryption or asset parsing not feasible (S2)~~ | Retired | S2 green: GBX.NET.PAK decrypts and the full graph is readable |
| TM2020 assets not obtainable without Openplanet (S8) | Delays v0.3 | Accept an Openplanet-extracted folder as input, and support embedded items + placeholders |
| Implicit geometry (pillars, clips, terrain) is complex | Visual gaps, floating roads | Rules documented in S2. Placeholders + issues where unknown |
| KN5 details wrong → AC crashes or invisible meshes | Blocks G2 | Phase 1 synthetic track before any TM work. Round-trip tests |
| Mirrored or rotated output | Wrong tracks | Asymmetric fixtures + unit tests for the transform |
| AI line quality poor at AC speeds | Bad AI | Recompute speeds for AC, and document the CSP in-game AI recording as a manual fallback |
| Most popular TM maps are Red (loops etc.) | User disappointment | Clear badges in the browser, `--force` available, and filter by Green/Yellow |
| TMX API changes or rate limits | Broken browse | Caching, backoff, endpoint config in one place |
| Dependency license is GPL | License choice | Decided in Phase 0 |
