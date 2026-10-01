# S1: KN5 format

**Verdict: 🟢 GREEN.** The layout was confirmed by parsing real Kunos and community track KN5s byte-exact to end of file
(`tools/spikes/Kn5AiProbe`). Files checked:
- `ek_sadamine/idas_uphill.kn5` (dummies only)
- `BurnoutParadise/BurnoutParadise_CPits.kn5` (dummies only)
- `magione/magione.kn5` (312 MB, full Kunos track)
- `ek_sadamine/collision.kn5` (collision-only)

## Layout (little-endian, strings = int32 byte length + UTF-8 bytes)

```
"sc6969"                      6 bytes magic
int32 version                 6 in every file checked (Kunos + community)
int32 extra                   only if version > 5; was 0 in every file → write 0
int32 textureCount
  per texture: int32 active (1), string name, int32 size, byte[size] blob
int32 materialCount
  per material:
    string name, string shader
    byte   alphaBlendMode     0 opaque, 1 alpha blend (seen on glass)
    byte   alphaTested (bool)
    int32  depthMode          0 in all samples
    int32  propCount
      per prop: string name, float valueA, float[2] valueB, float[3] valueC, float[4] valueD   (40 bytes of floats)
    int32  textureSlotCount
      per slot: string slotName (e.g. txDiffuse), int32 slot index, string textureName
node (recursive, root first)
  int32 class                 1 = dummy/transform, 2 = static mesh, 3 = skinned mesh (cars only, not needed)
  string name
  int32 childCount
  byte  active (bool)
  class 1: float[16] matrix   row-major, translation in m[12..14] (DirectX row-vector convention)
  class 2: byte castShadows, byte isVisible, byte isTransparent
           int32 vertexCount, vertices × 44 bytes: pos(3f) normal(3f) uv(2f) tangent(3f)
           int32 indexCount, uint16 indices        ← 65,535-vertex cap per mesh
           int32 materialIndex, int32 layer, float lodIn, float lodOut,
           float[3] boundingSphereCenter, float boundingSphereRadius, byte isRenderable
  then childCount child nodes
```

Notes:
- `ek_sadamine/collision.kn5` has ~170 KB of trailing bytes after the root node tree. AC tolerates trailing data.
  It's probably an editor or CSP addition. We write nothing after the tree.
- The texture blob can be any format AC can decode. `NULL.dds` in the collision KN5 is actually a 70-byte **PNG**.
  TMNF DDS (DXT1/DXT5 with mips) can be embedded as-is.
- Dummies exported by ksEditor sometimes carry a same-named child mesh (a visual marker box, `lod 0-0`). It isn't
  required: Sadamine's dummy-only KN5 has none.

## Winding and LOD (checked in Phase 1)
- **Front faces:** `cross(v1 − v0, v2 − v0)` points along the vertex normal. On Magione road physics meshes the count was
  341,245 triangles to 1. Collision-only meshes (Sadamine) are mixed about 50/50, so physics is double-sided and doesn't care.
- **`lodIn = lodOut = 0` means no LOD limit.** All visible meshes in `baby_park`, `drift` and `bugx_la_blocks` use 0-0.
- `ksPerPixelAT` with `alphaTested=1` is used for decals with transparent backgrounds.

## Materials we'll emit
Observed property sets (Magione):

| Shader | Properties | Texture slots |
|---|---|---|
| `ksPerPixel` | `ksAmbient`, `ksDiffuse`, `ksSpecular`, `ksSpecularEXP`, `ksEmissive`, `ksAlphaRef` | `txDiffuse` |
| `ksPerPixelNM` | The `ksPerPixel` set + `fresnelC`, `fresnelEXP`, `nmObjectSpace`, `isAdditive`, `fresnelMaxLevel` | `txDiffuse` + `txNormal` |
| `ksPerPixelAT` | Alpha-tested (fences) | |
| `ksPerPixelReflection` | | |
| `ksPerPixelMultiMap` | | |

Typical values are `ksAmbient` 0.3–0.4, `ksDiffuse` 0.3–0.6, `ksSpecular` 0.1–1, and `ksSpecularEXP` 8–250.

Collision-only material (from Sadamine): `ksPerPixel`, all props 0, with `txDiffuse` = a tiny placeholder texture.

## Decisions for the writer
- Write **version 6** with extra int 0, matching every AC/CSP-era file seen.
- Root node is a class-1 dummy with an identity matrix (Kunos uses the name `FBX: <file>.FBX`. We'll use the track ID).
- Meshes are split at 65,535 vertices.
- Collision meshes go in a **separate `collision.kn5`**, listed in `models.ini` (see S7). Those meshes have `isRenderable=false`, `castShadows=false`, and a shared null material.
