# S7: AC track folder requirements and CSP config

**Verdict: 🟢 GREEN.** Based on the 129 installed tracks (Kunos + community) and the local CSP install
(0.3.0-preview445, build 3978).

## Minimal working folder
`baby_park` loads with only `baby_park.kn5`, `map.png`, `data/map.ini` and `data/overlays.ini`, with no surfaces.ini.
We emit the full set anyway (SPEC §7.1).

## Files and contents seen

### `models.ini` (multiple KN5s)
```ini
[MODEL_0]
FILE=<track>.kn5
POSITION=0,0,0
ROTATION=0,0,0

[MODEL_1]
FILE=collision.kn5
...
```

Multi-layout tracks use `models_<layout>.ini` plus per-layout subfolders (`ek_sadamine`, `ks_drag`).

### `data/map.ini`
```ini
[PARAMETERS]
WIDTH=342.88
HEIGHT=861.583
MARGIN=20
SCALE_FACTOR=1
MAX_SIZE=1600
X_OFFSET=187.289
Z_OFFSET=444.422
DRAWING_SIZE=10
```

### `data/lighting.ini`
```ini
[LIGHTING]
SUN_PITCH_ANGLE=35
SUN_HEADING_ANGLE=240
```

### `ui/ui_track.json`
Fields: `name`, `description`, `tags[]`, `geotags[]`, `country`, `city`, `length`, `width`, `pitboxes`, `run`. Kunos stores
numbers as strings.

### `data/surfaces.ini`
Per `[SURFACE_n]`:

| Key | Notes |
|---|---|
| `KEY` | |
| `FRICTION` | |
| `DAMPING` | |
| `WAV` | |
| `WAV_PITCH` | |
| `FF_EFFECT` | |
| `DIRT_ADDITIVE` | |
| `BLACK_FLAG_TIME` | |
| `IS_VALID_TRACK` | |
| `SIN_HEIGHT` | |
| `SIN_LENGTH` | |
| `IS_PITLANE` | |
| `VIBRATION_GAIN` | |
| `VIBRATION_LENGTH` | |

Magione uses `KEY=PITLANE` (FRICTION 0.97, IS_PITLANE=1) and `TARMACA`/`TARMACB` (0.97–0.98).

## Physics mesh naming (important)
- CSP's `general.ini` documents the scheme: **`<digits><SURFACEKEY>[more digits][rest]`**. Tracks that leave out the trailing
  digits get misparsed as walls. CSP's `FIX_SURFACE_TYPES` papers over it.
- Real examples: `1ROAD18` (Sadamine collision), `06KERB001` and `01SAND003` (Magione).
- **Our names: `1<KEY><NNNN>`**, for example `1ROAD0003` and `1WALL0017`. Never put an underscore or letter straight after the key.
- Kunos tracks use the visible meshes themselves as physics (renderable). Community tracks often use separate
  `isRenderable=false` collision meshes in a dedicated `collision.kn5` (Sadamine has 86 meshes, max 8,430 vertices). We follow the
  community pattern.

## CSP extended physics/surfaces
- Enable it with `WAV_PITCH=extended-0` on `[SURFACE_0]`. CSP intercepts this, while vanilla AC would crash parsing it. We
  require CSP anyway.
- That unlocks extra `surfaces.ini` keys (they go in **surfaces.ini**, not ext_config):

  | Key | Values |
  |---|---|
  | `_EXT_SURFACE_TYPE` | `EXTRATURF`, `GRASS`, `GRAVEL`, `KERB`, `OLD`, `SAND`, `ICE`, `SNOW` |
  | `_EXT_SURFACE_TYPE_MODIFIER` | `LOOSE`, `REGULAR`, `FIRM` |
  | `_EXT_PERLIN_NOISE` | 0/1 |
  | `_EXT_PERLIN_OCTAVES` | 1–10 |
  | `_EXT_PERLIN_PERSISTENCE` | |

- It also unlocks ext_config extended physics, such as `[COLLISION_PARAMS_...]` (soft walls: `MESHES`, `SOFT_ERP`, `SOFT_CFM`, `BOUNCE`,
  `FRICTION`). That could make TM's bouncy "rubber" barriers feel right later.
- Caveat: online servers integrity-check `surfaces.ini`. That doesn't matter for our offline-first use.
- Most common ext_config sections on installed tracks: `MATERIAL_ADJUSTMENT`, `LIGHT_SERIES`/`LIGHT`, `SHADER_REPLACEMENT`,
  `GRASS_FX`, `RAIN_FX`, `LIGHTING`. For v0.1 we only need `[LIGHTING]` (mood) and maybe `MATERIAL_ADJUSTMENT` for emissives.

Sources: [CSP wiki: Surface tweaks](https://github.com/ac-custom-shaders-patch/acc-extension-config/wiki/Tracks-%E2%80%93-Surface-tweaks),
[CUP docs: Enabling extended physics](https://cup.acstuff.club/docs/csp/tracks/enabling-extended-physics).
