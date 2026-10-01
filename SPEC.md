# Tm2Ac Specification

This document defines what Tm2Ac builds. Items marked **[VERIFY]** are best-known information that a research spike must
confirm (see PLAN.md). When a spike disproves something here, update this file.

---

## 1. Goals and non-goals

### Goals
- G1. Browse and search Trackmania Exchange from a desktop app, then pick a map.
- G2. Convert the map into a working Assetto Corsa track that is drivable, timed, has spawns and pits, and has an AI line.
- G3. Install it into the AC `content/tracks` folder so Content Manager lists it right away.
- G4. Faithful visuals, using block meshes and textures extracted from the user's own TM install.
- G5. Tell the user honestly what won't work in AC: a per-map compatibility rating plus specific warnings.
- G6. Run fully automated, with no Kunos SDK, ksEditor or Blender in the loop.

### Non-goals (for now)
- Redistributing converted tracks or TM assets.
- Shipping TM assets in the repo or in releases.
- Making AC physics behave like TM. We approximate surfaces and flag the rest.
- Supporting vanilla AC without CSP.
- macOS/Linux.
- TM2 (ManiaPlanet) and TM Turbo. They are possible later additions.

### Release targets
| Version | Scope |
|---|---|
| v0.1 (MVP) | TMNF **Stadium** maps, end to end, via CLI. All §7 track features except the AI line (dropped). |
| v0.2 | WPF GUI: TMX browser, compatibility view, conversion options, library of installed tracks. |
| v0.3 | Trackmania 2020 maps (blocks + items + embedded custom items). |
| v0.4 | TMUF non-Stadium environments, CSP Lua gameplay effects (boosters), polish. |

---

## 2. User-facing behavior

### 2.1 CLI (`tm2ac`)
```
tm2ac search  <game> [query] [--author X] [--tag T]... [--multilap] [--order MostAwards|Newest|...] [--limit N]
tm2ac info    <game> <tmxId | path.Gbx>            # metadata + block/waypoint summary (downloads map); compatibility added in Phase 7
tm2ac analyze <game> <tmxId | path.Gbx>            # full compatibility report, no output written
tm2ac convert <game> <tmxId | path.Gbx> [options]
tm2ac assets  check [tmxIds...]                     # extract all blocks, report gaps (and for given maps)
tm2ac doctor                                       # check AC/CSP/TM paths and cache state
```
`<game>` is one of `tmnf`, `tmuf` or `tm2020`.

`convert` options:
| Option | Default | Meaning |
|---|---|---|
| `--scale <f>` | `1.0` | Uniform world scale, from 0.25 to 4.0. |
| `--install` / `--out <dir>` | `--install` | Write into AC `content/tracks`, or into a custom folder. |
| `--zip` | off | Also produce a CM-installable zip. |
| `--ai-source <replay\|centerline\|none>` | `replay` | Where the AI line comes from (§7.5). |
| `--replay <tmxReplayId \| path>` | best TMX record | Which ghost to use for the AI line and checkpoint ordering. |
| `--layout <auto\|circuit\|a2b>` | `auto` | Force the track type (§7.2). |
| `--pitboxes <n>` | `10` | Number of pits and grid spawns to generate. |
| `--scenery <full\|minimal\|none>` | `full` | Include stadium/environment decoration. |
| `--force` | off | Convert even if the compatibility rating is Red. |

### 2.2 Desktop app (WPF)
- **Browse:** pick the source (TMNF-X / TMUF-X / TMX 2020), search by name, author, tags, length and awards, and sort. Results show the
  TMX thumbnail, name, author, awards, length and a compatibility badge. The badge comes from a tag heuristic until the
  map has been analyzed, and from the real analysis after that.
- **Map detail:** screenshots, description, full compatibility report with warnings, the conversion options above,
  and a **Convert & Install** button with a progress log.
- **Library:** tracks this tool has installed. Actions are re-convert (with new options), uninstall, open folder,
  view report, and open in Content Manager **[VERIFY: `acmanager://` URI support]**.
- **Settings:** AC, TMNF and TM2020 install paths (auto-detected, with overrides), cache location and size, asset
  extraction status, default conversion options.
- Long operations run asynchronously, report progress, and can be cancelled.

### 2.3 Settings & storage
- Settings live in `%APPDATA%\Tm2Ac\settings.json`.
- The cache lives in `%LOCALAPPDATA%\Tm2Ac\cache\`. It holds `tmx/` (API responses and map/replay/image files).
  Game assets are read from the TM install on demand (see §4.4).
- Path auto-detection:
  - Read the Steam install path from `HKLM\SOFTWARE\WOW6432Node\Valve\Steam` → `InstallPath`, then parse
    `steamapps\libraryfolders.vdf` for each library.
  - AC: app 244210, at `steamapps\common\assettocorsa`.
  - TMNF: app 11020, at `steamapps\common\TrackMania Nations Forever`, plus the standalone default path.
  - TM2020: app 2225070, at `steamapps\common\Trackmania`, plus Ubisoft Connect and Epic locations.
  - Prefer the library whose `apps` list contains the app ID. Stale folders can exist in other libraries.
- CSP detection: `extension/config/data_manifest.ini` → `[VERSION] SHADERS_PATCH=0.3.0-preview445`, `SHADERS_PATCH_BUILD=3978`.

---

## 3. Trackmania Exchange integration

Verified 2026-09-30. Details, field lists and enums are in [docs/research/S3-tmx-api.md](docs/research/S3-tmx-api.md).

| Purpose | TMNF-X / TMUF-X (`tmnf.exchange` / `tmuf.exchange`) | TMX 2020 (`trackmania.exchange`) |
|---|---|---|
| Search | `GET /api/tracks?fields=...&{filters}` | `GET /api/maps?fields=...&{filters}` |
| Map file | `GET /trackgbx/{trackId}` | `GET /mapgbx/{mapId}` |
| Replays list | `GET /api/replays?trackId=...&fields=...` | `GET /api/replays?mapId=...` (rarely has results) |
| Replay file | `GET /recordgbx/{replayId}` | `GET /recordgbx/{replayId}` |
| Images | `GET /trackshow/{id}/image/{n}` (0 small, 1 full) | `GET /mapthumb/{id}`, `/mapimage/{id}/{n}` |

Rules:
- A `User-Agent` header is **required**: `Tm2Ac/<version> (+https://github.com/costbubbles/TM-track-exporter)`.
- `fields=` is required on search, and unknown fields return HTTP 400. Field sets differ per site.
- At most 2 concurrent requests per host, with exponential backoff on 429/5xx.
- Cache: map and replay files are immutable per ID, so they are cached forever. Search responses are cached for 10 minutes.
- TMNF-X search results include `WRReplay.ReplayId`, so the default AI-line ghost needs no extra call.
- Default browser filter: `primarytype=0` (Race). The parameter is single-valued. Circuits are found by tag `Multilap` (4),
  because TMNF-X multilap maps are listed as Race.
- Metadata we keep: name, author(s), upload/update dates, tags, environment, mood, author time, awards,
  difficulty, TMX URL, description and image URLs.

---

## 4. Input formats (Gbx)

Parsed with GBX.NET. Body decompression needs GBX.NET.LZO.

### 4.1 TMNF/TMUF map (`*.Challenge.Gbx`, `CGameCtnChallenge`)
- Environment / collection (`Stadium`, `Island`, ...). **v0.1 supports Stadium only.**
- `Blocks`: name (for example `StadiumRoadMainStartLine`), coord (x,y,z), `Direction` (North/East/South/West),
  `IsGround` (flag 0x1000), `IsPillar`, `IsClip`, plus skin.
- Block grid is **32 × 8 × 32 m**, and world = (X·32, Y·8, Z·32) from the block min-corner, **with no Y offset**. This was verified
  against a ghost (see S5).
- Map metadata: name, author, `Decoration` (mood, for example ("Day","Stadium","Nadeo")), `IsLapRace`, `NbLaps`, author time.
- Start, finish, checkpoint and multilap blocks are identified by an **explicit block-name table** in
  `data/block-flags.tmnf.json`. Examples: `…StartLine`, `…FinishLine`, `…Checkpoint*`, `StadiumCheckpointRing*`.
  Substring matching on "Start" is wrong because of `BiSlopeStart` and `LoopStart`. The BlockInfo's `WayPointType` can
  cross-check the table.

**Implicit geometry.** The game generates some geometry at load time that the map file does not list: pillars under air
blocks, terrain and clip/connection pieces. The converter must regenerate it. Spike S2 documents which rules
apply.

### 4.2 TM2020 map (`*.Map.Gbx`, `CGameCtnChallenge`, newer chunks)
- Blocks, including free-placed blocks (float position + rotation) and variants/subvariants.
- **Items** (`AnchoredObjects`): free placement, pivot, Nadeo items and embedded custom items.
- Embedded data: a zip of custom `*.Item.Gbx` / `*.Block.Gbx` with their meshes (`CPlugStaticObjectModel`,
  `CPlugSolid2Model`).
- Gameplay surfaces and special blocks (Turbo, Turbo2, Reactor, NoEngine, NoSteering, NoGrip, Fragile, SlowMotion,
  Cruise, Reset).
- Texture mods (`ModPackDesc`): **out of scope**. Use default textures.

### 4.3 Ghosts / replays (`*.Replay.Gbx`, `CGameCtnGhost`)
- We use: position/rotation samples over time, checkpoint times and race time.
- Uses:
  - AI line (§7.5).
  - Checkpoint ordering (§7.2).
  - Compatibility heuristics: upside-down time, wall contact and airtime (§8).

### 4.4 Assets from the TM install
- TMNF (verified, see [S2](docs/research/S2-tmnf-assets.md)):
  - `Packs/Stadium.pak` is opened with GBX.NET.PAK. The key comes from `Packs/packlist.dat` (`KeyType.BaseKey`).
  - Hashed entry names are resolved with `MD5.Compute136` over progressively longer path suffixes.
  - Object graph: `CGameCtnBlockInfoClassic` → Ground/AirMobils → `CSceneMobil` → `CHmsItem` → `CPlugSolid` →
    `CPlugTree` (LOD via `CPlugTreeVisualMip`) → `CPlugVisualIndexedTriangles`, plus a `CPlugSurface` collision mesh
    whose per-triangle `SurfaceIndex` points to a `CPlugMaterial.SurfaceId`.
  - **Textures are plain DDS files** in `GameData/Stadium/Media/Texture/Image/` (DXT1/DXT5 with mips), embedded into the KN5 as-is.
- TM2020: pack files are encrypted and keys are delivered at runtime. Candidate routes are, in order:
  1. Assets already extracted with Openplanet (Fid Explorer extraction).
  2. Our own pak reader, if keys can be obtained legitimately.
  3. Embedded items only, with procedural fallback for Nadeo blocks.

  This is decided by spike S8.
- **No persistent mesh cache.** Extracting all 308 Stadium blocks takes ~6 s, so blocks are extracted on demand per conversion
  and memoised in memory (decision 2026-10-01).

---

## 5. Intermediate representation (IR)

This is the game-agnostic `TrackScene` that every parser produces and the AC writer consumes. Everything in it is in **AC space**: meters, Y-up,
AC handedness, already scaled.

```
TrackScene
  Source        { game, tmxId, name (plain), author, tags[], mood, url, images[], authorTimeMs }
  Parts[]       { meshRef, transform (4x4), materialOverrides?, role: Visual|Collision|Both, surfaceKey?, sourceBlock }
  Collision[]   { mesh, surfaceKey }                   # resolved collision geometry
  Route         { type: Circuit|AToB, laps, start: Gate, finish: Gate, checkpoints: Gate[] (ordered),
                  spawnHint: Pose, path: Polyline? (from ghost or centerline) }
  Gate          { left: Vec3, right: Vec3, forward: Vec3 }
  Generated[]   { kind: PitPlatform|Ground|Pillar|..., mesh, surfaceKey }
  Bounds, Stats { blockCount, itemCount, triangleCount, estLengthM }
  Issues[]      { severity: Info|Warn|Block, code, message, location? }
```

---

## 6. Coordinate system & scale

- TM → AC transform: one function, unit-tested with asymmetric fixtures (an L-shaped track must not come out mirrored).
  The **TM→AC handedness is still [VERIFY]**. It will be settled in Phase 1/4 with an in-game check (see S5).
- Block placement for TMNF: `world = (x * 32, y * 8, z * 32)` (block min-corner), with the block's local solid (0..32 × 0..32
  after applying the `CPlugTree.Location` chain) rotated about the block footprint centre by `Direction`. South = −Z.
  East/West signs and the pivot for multi-unit blocks are **[VERIFY]** in Phase 4.
- AC side (verified):
  - Y-up, meters.
  - KN5 matrices are row-major with translation in the last row.
  - Dummy forward = local +Z.
  - Gate `_L` sits at `cross(up, forward)` from the midpoint.
- **Scale:** uniform, applied to the whole scene, collision meshes included, before export. Range 0.25–4.0, default 1.0.
  The GUI presets are:

  | Preset | Scale |
  |---|---|
  | Original | 1.0 |
  | Road car | 1.5 |
  | GT | 2.0 |
  | Formula | 2.5 |

  Scale does not change ramp angles. A warning is raised when scale makes jump gaps impossible (§8).

---

## 7. Output: Assetto Corsa track

### 7.1 Folder layout (single layout)
```
content/tracks/<trackId>/
  <trackId>.kn5              (visual meshes + AC_* dummies)
  collision.kn5              (non-renderable physics meshes)
  models.ini                 (MODEL_0 = <trackId>.kn5, MODEL_1 = collision.kn5)
  map.png
  data/
    surfaces.ini
    map.ini
    cameras.ini              (one generic replay camera set)
    lighting.ini             (sun direction from TM mood)
    groove.ini               (minimal/neutral)
  ai/
    fast_lane.ai
  ui/
    ui_track.json
    preview.png
    outline.png
  extension/
    ext_config.ini           (CSP)
  conversion-report.json
```
- `<trackId>` = `tm<game>_<tmxId>_<slug>`, lowercase ASCII, at most 64 characters.
- Re-converting overwrites the folder in place. Uninstall deletes only folders that contain our `conversion-report.json`.

### 7.2 Route, timing and track type
- **Type detection (`auto`):** each ghost checkpoint time is matched to its nearest waypoint block. The map is a **Circuit**
  if the ghost crosses the start/finish (multilap) block, otherwise **A-to-B**. Without a ghost, the map's lap flag or a
  start/finish block decides.
- **Circuit:**
  - `AC_TIME_0_L/R` sits at the start/finish line.
  - `AC_TIME_1..n_L/R` sit at ordered checkpoints (sectors).
  - `AC_HOTLAP_START_0` sits before the line.
- **A-to-B:**
  - `AC_AB_START_L/R` sits at the start block exit.
  - `AC_AB_FINISH_L/R` sits at the finish line.
  - Checkpoints become `AC_TIME_n` sectors where AC allows it **[VERIFY behavior of sectors in A-to-B]**.
  - Supported AC modes are hotlap/time attack (and practice). Race mode is listed as unsupported in the report.
- **Checkpoint ordering:**
  1. From the replay's checkpoint pass order.
  2. Otherwise from a route-graph walk of connected blocks.
  3. Otherwise no sectors, plus a warning.
- Gate width = road width at that point (from the block definition), plus 1 m margin each side.

### 7.3 Spawns and pits
- **Default: one starting spot (decision 2026-10-01, in-game round 4).** `AC_START_0`, `AC_PIT_0` and `AC_HOTLAP_START_0` all
  sit on the start block's own spawn point, just behind the line, snapped to the collision surface. Every AC session
  (race, practice/pit, hotlap, time attack) therefore starts at the start line, as in Trackmania.
- `--pitboxes N` (N > 1) adds a staggered grid behind the spawn: slots 8 m apart, ±3 m lateral, with pit boxes on the same spots.
  Slots are validated against the collision. If there's no surface, a flat ROAD platform is generated behind the start
  block (PIT_PLATFORM_GENERATED).
- All dummies face the race direction.

### 7.4 KN5 model
- Written by our own writer (§9) as KN5 **version 6**. There are two KN5s per track (visual and collision), tied together by `models.ini`.
  This is the community pattern seen in `ek_sadamine`.
- **Visual meshes:** grouped by material and by spatial chunk (default 128 m cells). Each mesh is kept to 65,535 vertices or fewer,
  since indices are uint16.
- **Collision meshes** live in `collision.kn5`:
  - Flags: `isRenderable=false`, `castShadows=false`, with a shared null material.
  - Naming follows CSP's documented scheme **`<digits><SURFACEKEY><digits>`**, for example `1ROAD0003` and `1WALL0017`. Never use `_` or a letter right after
    the key, or CSP/AC parses the mesh as a wall.
  - Built from TM collision meshes (`CPlugSurface`), not from visual meshes.
- **Dummies:** all `AC_*` nodes from §7.2 and §7.3, in the visual KN5.
  - Rotation: local +Z points in the race direction and +Y is up.
  - Gates: `_L` is placed at `mid + cross(up, forward) · halfWidth`, `_R` on the other side.
- **Materials:**

  | Shader | Used for |
  |---|---|
  | `ksPerPixel` | Default |
  | `ksPerPixelAT` | Alpha-tested (fences, foliage) |
  | `ksPerPixelNM` / `ksPerPixelMultiMap` | Normal-mapped, where TM provides the maps |

  Textures are embedded as DDS. TM DDS is passed through as-is where AC accepts it.
- Ground: a large ground plane (GRASS, or the TM environment's terrain) covering the bounds plus a 200 m margin.

### 7.5 AI line: dropped (decision 2026-10-01)
No `ai/fast_lane.ai` is generated. Very few Trackmania maps can be driven conventionally from start to finish in AC, let
alone by AI, so an AI line isn't worth its cost (user decision after the first in-game checks). The replay is still used for
layout detection, checkpoint order (sectors), the minimap path and the compatibility rating. The format research stays in
[S6](docs/research/S6-fast-lane-ai.md) in case this is revisited.

### 7.6 UI and map files
- `ui_track.json` fields:

  | Field | Value |
  |---|---|
  | `name` | TM name, formatting stripped |
  | `description` | TMX description + "Converted by Tm2Ac" |
  | `tags` | TMX tags + `trackmania`, `tm2ac` |
  | `country` | `Trackmania` |
  | `author` | TM author |
  | `url` | TMX URL |
  | `length` | Route length in meters |
  | `pitboxes` | N |
  | `run` | `clockwise` / `anticlockwise` / `a2b` |
  | `version` | Converter version |
  | `year` | Upload year |

- `preview.png`: the TMX screenshot, downscaled. If there is none, a top-down render.
- `outline.png` and `map.png` + `data/map.ini`: a top-down projection of the route polyline (or the road collision
  outline). Drawn with SkiaSharp at AC's expected sizes and margins.

### 7.7 Surfaces (`data/surfaces.ini` + CSP)
- Data-driven mapping from TM physics material IDs and gameplay types to AC surface keys lives in
  `data/surface-map.<game>.json`.
- The default mapping is below. Numbers are starting values, to be tuned in playtesting:

| TM material / gameplay | AC key | FRICTION | IS_VALID_TRACK | Notes |
|---|---|---|---|---|
| Asphalt, Concrete, Pavement, Tech road | ROAD | 0.99 | 1 | |
| Metal, Wood, Plastic (TM2020) | ROAD | 0.95 | 1 | |
| Dirt, DirtRoad | DIRT | 0.80 | 1 | DIRT_ADDITIVE, light SIN_HEIGHT |
| Grass | GRASS | 0.60 | 0 | |
| Sand | SAND | 0.55 | 0 | |
| Ice (TM2020 ice/snow) | ICE | 0.35 | 1 | CSP surface |
| Rubber/Bumper, barriers | WALL | — | — | collision only |
| Turbo/Turbo2/Reactor pads | ROAD | 0.99 | 1 | Effect not emulated in v0.1. Flagged. Lua boost in v0.4 |
| NoGrip / SlidingRubber | ICE | 0.30 | 1 | Approximation. Flagged |
| NotCollidable | (no collision) | | | |
| Water | (no collision) | | | Visual only. Flagged if on route |

- CSP extended surfaces ([S7](docs/research/S7-ac-track-requirements.md)):
  - `[SURFACE_0]` gets `WAV_PITCH=extended-0`.
  - Surfaces then set `_EXT_SURFACE_TYPE` (`GRASS`, `SAND`, `ICE`, `SNOW`, `GRAVEL`, `KERB`, `EXTRATURF`, `OLD`) and optionally
    `_EXT_SURFACE_TYPE_MODIFIER`. These keys live in **surfaces.ini**.
- `extension/ext_config.ini` holds:
  - Optional `[COLLISION_PARAMS_...]` soft walls for TM rubber barriers (post-v0.1).
  - Lighting/mood mapping (Day/Sunrise/Sunset/Night → default time of day).
  - Basic material tweaks (emissives at night).
  - Later, Lua scripts.

---

## 8. Compatibility analysis

Every map gets a rating plus a list of issues. Detection is data-driven (`data/block-flags.<game>.json`) and also uses
replay analysis when a ghost is available.

| Rating | Meaning |
|---|---|
| **Green** | Should drive normally in AC (possibly with approximated surfaces). |
| **Yellow** | Drivable with caveats: approximated special surfaces, boosters that don't boost, steep sections, big jumps. |
| **Red** | The route needs things AC can't do: loops, wallrides, upside-down sections, reactor flight, engine-off sections, or jumps that are impossible at the chosen scale. Conversion is still allowed with `--force`. |

Issue examples:
- `LOOP_BLOCK`
- `WALLRIDE_BLOCK`
- `UPSIDE_DOWN` (from ghost orientation)
- `BOOSTER_UNSUPPORTED`
- `SPECIAL_SURFACE_APPROX`
- `LONG_AIRTIME` (ghost airborne for more than N s, or a gap longer than X m)
- `NO_REPLAY_FOR_AI`
- `CHECKPOINT_ORDER_GUESSED`
- `PIT_PLATFORM_GENERATED`
- `MISSING_ASSET` (a block mesh is not found in the cache, so we use a bounding-box placeholder)
- `CUSTOM_ITEM_UNSUPPORTED`

---

## 9. KN5 writer

- The byte-level layout is verified against real Kunos and community tracks. See [S1](docs/research/S1-kn5-format.md) for the full layout,
  the material property sets and decisions. Reference reader: `tools/spikes/Kn5AiProbe`.
- Summary:

  | Part | Contents |
  |---|---|
  | Header | `sc6969`, version 6, extra int 0 |
  | Textures | Count, then active + name + blob for each |
  | Materials | Count, then name, shader, blend byte, alpha-test byte, depth int, props (name + 10 floats) and slots (name, index, texture) |
  | Node tree | Recursive. Class 1 holds a 16-float matrix. Class 2 holds flags, 44-byte vertices, uint16 indices, material, layer, LOD, bounding sphere and `isRenderable` |

- A minimal reader for round-trip tests. Golden test: write a known scene, read it back and compare. Then open it in
  Content Manager's showroom/track viewer as a manual check.

---

## 10. Non-functional requirements

| Area | Requirement |
|---|---|
| Performance | A typical TMNF map converts in under 30 s with a warm asset cache. One-time asset extraction takes under 10 min. |
| Robustness | A missing asset or unknown block never aborts conversion. It becomes a placeholder plus an issue. |
| Determinism | Same input + options + cache gives byte-identical output, apart from timestamps in the report. |
| Logging | Structured logs (Microsoft.Extensions.Logging), written to a file under `%LOCALAPPDATA%\Tm2Ac\logs`. |
| Errors | The GUI shows actionable messages ("TMNF install not found — set the path in Settings"). |
| Legal | The app shows a one-time notice: converted tracks contain Nadeo assets and must not be redistributed. Converted folders include a `README_TM2AC.txt` with the same notice. |

---

## 11. Testing

| Kind | What it covers |
|---|---|
| Unit | TM→AC transform (handedness), formatting-code stripping, surface mapping, INI writers (golden text), KN5 round-trip, fast_lane.ai round-trip, map.ini math, track-ID slugging |
| Network (opt-in) | TMX search, map download and replay download against stable public IDs |
| Asset (opt-in, needs local TM install) | Block extraction smoke tests |
| Manual acceptance per release (checklist in STATUS.md) | Track appears in CM with preview/outline. Loads in AC with CSP. Car spawns on track, facing the right way. Lap/AB timing registers. Sectors register. Surfaces feel right. AI completes a lap. Pits work. |
| Reference maps | A small set of TMNF-X IDs covering a simple circuit, an A-to-B tech map, a dirt map, a map with loops (expected Red) and a map with big jumps. IDs are listed in `tests/reference-maps.md`. Files are not committed. |

---

## 12. Licensing

- The project is open source on GitHub under **GPL-3.0-or-later**. This is required because GBX.NET.LZO is GPL-3.0-or-later.
  GBX.NET, GBX.NET.PAK and GBX.NET.ZLib are MIT.
- No TM assets, maps, replays or converted output go in the repo or in releases.
