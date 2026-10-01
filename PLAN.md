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

## Phase 3: TMNF asset extraction

- [x] TMNF install discovery (Steam + standalone)
- [x] `TmnfPakFileSystem`: all decryptable paks + GameData, hashed-name resolution, and **recursive ref-table wiring** so lazy
      GBX.NET properties load across files. Unparseable files are recorded rather than thrown
- [x] `TmnfBlockExtractor`/`TmnfBlockLibrary`: block info (indexed by `Ident.Id`) → variants (ground/air × index) → LOD-0
      visual parts per material + collision per TM surface (root tree transform applied), units and spawn locations.
      All 308 Stadium blocks extract in ~6 s, with 9 lacking visuals (7 GBX.NET parse gaps, plus terrain/pylon specials)
- [x] ~~Mesh cache~~ **Dropped:** extraction is fast enough to do on demand (decision 2026-10-01)
- [x] Material translation (`MaterialTranslator`): diffuse (or best stand-in slot) DDS from GameData, ksPerPixel/AT,
      glow and fake-shadow materials dropped, water and fallback textures generated
- [x] Collision surfaces: `data/surface-map.tmnf.json` + `SurfaceMap` (TM surface → AC key and issue code; slopes over 55° → WALL)
- [x] `tm2ac assets check [tmx-ids]`, plus a new `Tm2Ac.Pipeline` project for the TM→AC conversion layer
- [x] Placement verified: block spawn locations match the WR ghosts' first samples with 0.00 m error on all 7 reference maps
- [ ] Placeholder geometry for missing blocks: moved to Phase 4 (fallback block table + `MISSING_ASSET` issue)

**Exit:** every block used by the reference maps is in the cache, or knowingly reported as missing. ✅ Met: the only gaps are
StadiumInflatableTube, StadiumPlatformTurbo and StadiumRoadMainTurboLeft (`tm2ac assets check <ids>`).

---

## Phase 4: Geometry assembly, first real TMNF track in AC

- [x] Block placement (coord, direction, variant, multi-unit footprints) in `BlockPlacement`. TM→AC needs no mirroring.
      Spawns verified against 7 ghosts, and road pieces join seamlessly in renders
- [x] Implicit geometry:
  - Default Stadium grass: the game's own ground quad, filled 5 cm under every non-terrain cell.
  - Edge walls around the map.
  - Terrain without meshes (StadiumDirt) drawn from its collision with the game's dirt/grass textures.
  - Pillars and clips are explicit blocks in TMNF maps, so nothing to generate.
- [x] Scene builder (`TmnfSceneBuilder`): visual parts batched by material × 128 m chunk, collision by AC surface × chunk
      (welded vertices), map centred at the origin with TM ground level at y=0. Fallback blocks + MISSING_ASSET issues
- [x] Mesh batching, 65k split (writer), collision naming `1<KEY><NNNN>`
- [x] Uniform scale option (`--scale`)
- [x] `tm2ac convert tmnf <id|file> [--out|--ac-path] [--scale] [--replay] [--no-grass]`: spawn/pit/hotlap at the start block,
      TMX screenshot as preview, ghost path as map, conversion-report.json with issues
- [x] `tm2ac dev render <track-folder> <png>`: top-down debug render of collision + dummies
- [x] All 7 reference maps convert in 3–9 s (R1: 509k visual / 265k collision triangles, 64 MB, R7: 3.8M / 3.0M, 345 MB)
- [ ] Manual check on the simplest reference map: it looks right, isn't mirrored, surfaces collide, and the car drives
      (**queued** in STATUS.md, combined with Phase 5)
- [ ] Later optimisation: KN5 size for big maps (vertex dedupe in visuals, drop dense grass-blade decoration, LODs)

**Exit:** a TMNF track drivable in AC (no timing yet).

---

## Phase 5: Route, timing, spawns & pits

- [x] Gates from start, finish and CP blocks: block centre plane, full 32 m cell width, height and direction from where the ghost crossed
- [x] Track type detection: ghost checkpoint crossings classified by nearest waypoint. Circuit if the start/finish line is
      crossed, else A to B. The map's lap flag is used without a ghost, and `ConversionOptions.Layout` can override
- [x] Checkpoint order from the ghost, first lap only. Side-by-side CPs crossed within 1 s merge. Without a ghost there are no sectors (issue)
- [x] AC dummies:
  - Circuit: AC_TIME_0 line + AC_TIME_n sectors.
  - A to B: AC_AB_START 2 m ahead of the spawn, and AC_AB_FINISH at the finish the ghost reached (MULTIPLE_FINISHES info).
  - Hotlap start 4 s before the lap line (circuits).
- [x] Grid/pits: staggered slots 8 m apart behind the spawn, validated with a collision height query. Otherwise a generated ROAD
      platform behind the start block (PIT_PLATFORM_GENERATED)
- [x] `ui_track.json` run direction (clockwise/anticlockwise from the lap path, or a2b)
- [x] Tests: R1 A-to-B, R3 circuit sectors in order and grid behind the line, R6 circuit despite a nearby finish block
- [ ] Manual check: circuit timing and sectors, A-to-B time attack, pits and grid (**queued**: R1 and R3 installed in AC)
- [ ] Sectors on A-to-B tracks (AC support unverified, see SPEC §7.2)

**Exit:** reference maps are timed correctly in AC hotlap and time-attack modes.

---

## Phase 6: AI line, UI assets, CSP config (AI line later dropped, 2026-10-01)

- [x] fast_lane.ai v7 writer + reader with round-trip test. **No spatial grid (flag 0)**, as 19 working community tracks do (S6)
- [x] AI path from the ghost:
  - One flying lap (line to line) on circuits, the whole run on A-to-B.
  - Resampled at 1.5 m, smoothed, and snapped to the collision surface through an indexed height query.
  - Speeds from curvature (μ 1.4) plus 10 m/s² braking and 6 m/s² acceleration passes, giving gas and brake hints.
  - AI_LINE_AIRBORNE warning when more than 10% has no ground.
- [x] ~~Centerline fallback~~ **AI line dropped** by user decision (2026-10-01). The builder and writer were removed; see SPEC §7.5
- [x] ~~Side distances~~ dropped with the AI line
- [x] Outline, map and preview generation (map from the ghost lap, preview from the TMX screenshot)
- [x] Full `ui_track.json` from TMX metadata (author, url, year, tags, run direction)
- [ ] `ext_config.ini`: **deferred**. Nothing needs it yet: extended physics is set in surfaces.ini, and AC sets time of day per session
- [x] `conversion-report.json` + `README_TM2AC.txt` in the output, with `--zip` for a CM-installable archive
- [ ] Manual check: the CM track page looks complete (**queued**). The AI part was dropped

**Exit:** all SPEC §7 features present for TMNF.

---

## Phase 7: Release polish → **v0.1 release (CLI)**

- [x] ~~Compatibility analysis (ghost heuristics, Green/Yellow/Red rating, `tm2ac analyze`, `--force`)~~: built, then
      **removed 2026-10-01** by user decision: the tool ports every map and doesn't judge drivability (SPEC §8)
- [x] `data/block-flags.tmnf.json` fallbacks for unparseable blocks
- [x] README (install, usage, compatibility, legal notice). `dotnet publish src/Tm2Ac.Cli -c Release -r win-x64` gives a single
      45 MB exe, and CI uploads it as an artifact
- [x] Run the acceptance checklist on the reference maps (user, 2026-10-01: R1, R2, R3)
- [ ] Tag v0.1.0 and publish the GitHub release (**waiting for the user's approval** after the acceptance checks)

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
