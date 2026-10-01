# S2: TMNF asset extraction

**Verdict: 🟢 GREEN.** We can read TMNF Stadium block meshes, materials, collision meshes and textures from a
stock Steam install. Proof: the `StadiumRoadMainTurbo` ground variant was exported to OBJ and rendered top-down. It came out as a
correct 32×32 m road block with turbo pad, borders and turbine stands. See `tools/spikes/TmnfPakProbe`.

## Where things live (TMNF Steam install)
| Asset | Location | Access |
|---|---|---|
| Block infos, mobils, solids (meshes), materials, bitmaps | `Packs/Stadium.pak` (90 MB, 1,931 files) | Encrypted |
| Pak keys | `Packs/packlist.dat` | `PakList.Parse(path, PakListGame.TM)` |
| **Textures (DDS)** | `GameData/Stadium/Media/Texture/Image/*.dds` (201 files) | **Plain files on disk** |
| Mood assets (sky, etc.) | `GameData/Stadium/Media/Moods/<Mood>/` | Plain files |

DDS format is DXT1 (diffuse) / DXT5 (normal), 1024×512 with full mips. AC-compatible, so embed as-is.

## Libraries
- **GBX.NET.PAK 2.4.4 (MIT)**: Blowfish pak decryption, file table, and opening Gbx with ref-table resolution.
  - Keys: `PakList.Parse(packlist.dat, PakListGame.TM)` gives the `stadium` key. Use `Pak.Parse(stream, key, KeyType.BaseKey)`.
    `EncryptionKey` throws, and `MasterServerKey` is only for pak v6+. Stadium.pak is v3.
- **GBX.NET.Crypto** `MD5.Compute136(name)` provides the hashed file names. Many pak entries (all solids and materials) are stored
  as `<dir>\<34-hex hash>`. Resolve a reference path by hashing progressively longer suffixes:
  `dir\hash(file)`, then `parent\hash(sub\file)`, and so on. Our 15-line `Resolve()` in the probe does this.
- `Pak.OpenGbxFile(file, settings, importExternalNodesFromRefTable: true)` resolves **only one level** of references. We
  must resolve nested references (mobil → solid → material) ourselves with `Resolve()`.
- `Pak.BruteforceFileHashesAsync` returned 0 names for TMNF. We don't need it.

## Object graph (confirmed)
```
CGameCtnBlockInfoClassic  (Stadium\ConstructionBlockInfo\ConstructionBlockInfoClassic\<Block>.TMEDClassic.Gbx)
 ├─ GroundMobils[unit][variant] / AirMobils[unit][variant] → External<CSceneMobil>  (Stadium\Mobil\<Block>Ground.Mobil.Gbx)
 │    └─ Item: CHmsItem → Solid: CPlugSolid → TreeFile = "Media\Solid\...\Ground.Solid.Gbx" (resolve!)
 ├─ GroundBlockUnitInfos / AirBlockUnitInfos (per-unit offsets, clips, AcceptPylons)
 ├─ WayPointType, SpawnLocGround / SpawnLocAir (Iso4), PillarShapeMultiDir
CPlugSolid (.Solid.Gbx)
 └─ Tree: CPlugTree (Location: Iso4)
     ├─ CPlugTreeVisualMip → Levels[{FarZ, Tree}]     LOD 0 = smallest FarZ (64 m), LOD 1 = 4096 m
     │    └─ CPlugTree … (Location) → Visual: CPlugVisualIndexedTriangles
     │         Vertices[] (Position, Normal), IndexBuffer.Indices, TexCoords[0..2] (0 = diffuse UV)
     │         MaterialFile = "Material\<Name>.Material.Gbx"  (relative, ancestor level 4 → Stadium\Media\Material\)
     └─ Surface: CPlugSurface
          Materials[] (SurfMaterial → MaterialFile)
          Geom: CPlugSurfaceGeom → Surf: CPlugSurface.Mesh { Vertices[], CookedTriangles[{Indices, SurfaceIndex}] }
CPlugMaterial (.Material.Gbx)
 ├─ SurfaceId  ← physics material (e.g. Turbo_Deprecated, Concrete)  → our surface mapping key
 └─ CustomMaterial.Textures[{Name: Diffuse|Normal|Occlusion|Specular|…, Texture: CPlugBitmap → ImageFile "Image\<X>.dds"}]
```

GBX.NET helpers: `CPlugSolid.GetAllChildrenWithLocation(lod)` gives (tree, accumulated Iso4), and `CPlugSolid.ExportToObj(...)`
handles quick debugging.

Transform: `world = (XX·x + XY·y + XZ·z + TX, YX·x + YY·y + YZ·z + TY, ZX·x + ZY·y + ZZ·z + TZ)`. With this, LOD-0 visuals of
the Turbo block land in exactly x∈[0,32], z∈[0,32].

## Turbo block numbers
- LOD 0 visual: 3,120 triangles, 4,670 vertices, 12 materials.
- Collision: 1,852 triangles. They need the owning tree's `Location` applied, since raw coords were at 48..80 × 40..72.
  Each triangle has a `SurfaceIndex` → `Surface.Materials[i]` → material → `SurfaceId`.

## Not yet done (moves to Phase 3)
- Loading materials referenced from collision `SurfMaterial`s (nested refs) to read `SurfaceId` per collision triangle.
- Ground vs air variant selection per placed block (`CGameCtnBlock.IsGround`, flags 0x1000), and multi-unit blocks
  (`BlockUnitInfos[].RelativeOffset`).
- **Implicit geometry:**
  - Pillars: the map has `IsPillar` blocks, and `StadiumInflatablePillar` appears 172× in the test map. Some pillars are explicit.
  - Clips: `CGameCtnBlockInfoClip` per unit face, chosen by neighbour connectivity.
  - Terrain: the Stadium ground plane and `StadiumDirtHill` etc. are explicit blocks.

  Rules are to be derived in Phase 4 by comparing with in-game screenshots.
- Stadium decoration (stands, screens). `Decoration` = ("Day", "Stadium", "Nadeo"). Look at
  `StadiumConstructionDecoration\` in the pak.
