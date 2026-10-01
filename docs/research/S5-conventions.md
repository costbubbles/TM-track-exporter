# S5: Coordinate and dummy conventions

**Verdict: 🟡 MOSTLY ANSWERED.** AC-side conventions are confirmed against real tracks. TMNF block→world mapping is
confirmed against a WR ghost. **One item is still open: TM→AC handedness (mirroring).** It needs the Phase 1 in-game
L-shaped test.

## AC side (confirmed from real KN5 + fast_lane.ai)

### Dummy forward axis = local +Z (matrix row 2)
Magione's `AC_PIT_*` dummies have row 2 = (−0.498, 0, −0.867), and the AI line's travel direction at the start is
(−0.50, 0, −0.866). Sadamine confirms it too: `AC_START_0` sits 4 m *behind* the `AC_AB_START` gate along +Z, and
`AC_HOTLAP_START_0` sits 12 m behind.

### Gate sides: `_L` is at `cross(up, forward)` from the gate midpoint
- Magione `AC_TIME_1_L/R`: the gate's +Z (0.31, 0, 0.95) matches the AI direction there (0.24, 0, 0.97). `L − mid` points
  along (0.98, 0, −0.2), which equals `cross(Y, forward)` = (f.z, 0, −f.x) ✓.
- Sadamine `AC_AB_START_L/R` agrees.
- Sadamine's `AC_AB_FINISH_*` dummies have an arbitrary rotation, but their positions still satisfy the rule for the
  real race direction. **So only positions matter for gates.** We'll still give gate dummies the race-forward rotation.

### Spawn and pit dummies
- `AC_START_n` are grid slots and `AC_PIT_n` are pit boxes. In small community layouts they're often identical positions.
- Magione pits are spaced ~8.2 m apart, along the pit lane.
- `AC_HOTLAP_START_0` sits behind the timing line.

### Dummy names observed
`AC_START_n`, `AC_PIT_n`, `AC_HOTLAP_START_0`, `AC_TIME_n_L/R` (0 = start/finish line), `AC_AB_START_L/R`,
`AC_AB_FINISH_L/R`. Others we don't emit: `AC_AUDIO_*`, `AC_CREW_*`, `AC_POBJECT_*`, `AC_LIGHT_*`.

## TMNF side (confirmed from map + WR ghost of TMNF-X #924307)
- The start block `StadiumRoadMainStartLine` has coord (16, 29, 29) and dir South. The ghost's first sample is (528, 234.2, 948.8):
  - x = 16·32 + 16, the block centre.
  - y = 29·8 = 232, plus 2.2 m (car).
  - z is inside [29·32, 30·32).
  - The car then moves toward −Z.
- **TMNF world = (X·32, Y·8, Z·32), with block coords at block min-corner and no Y offset.** Ground blocks
  (y=1) sit at 8 m, and the finish sample at y≈9.9 agrees.
- `Direction` South = −Z, and by implication North = +Z. Rotation of the remaining two (East/West) is still to confirm with a fixture.
- Block solids (see S2) are modelled in **block-local space 0..32 × 0..32** after applying the CPlugTree `Location`
  chain. Collision meshes need their parent tree transform too (raw coords were offset by (48, 40)).

## Evidence on TM handedness (ghost steering, Phase 1)
In the WR ghost of #924307, on-ground samples above 20 m/s with |steer| > 0.3 were checked: **78 of 87** have
`steer < 0` together with `(v_prev × v_next).y > 0`, i.e. turning from +Z toward +X. The car's local +Z matches its velocity.
If TM's negative steer means left (the usual TM input convention), then in TM world "left of +Z is +X", the same as AC.
**Prediction: the TM→AC mapping is the identity (no mirroring).** This still needs confirming on a real map in Phase 4.

## Block placement verified against ghosts (Phase 3)
`BlockPlacement` (src/Tm2Ac.Pipeline) places the start block's `SpawnLocGround/Air` (from its block info) at the block
coord. It matched the WR ghost's first sample with **0.00 m error and the same heading on all 7 reference maps**:
- Directions South, East and West.
- Ground and air start blocks.
- Both `StartLine` and `StartFinishLine` blocks.

This confirms:
- Block-local solids span [0, 32·units] and rotate about the footprint centre.
- **Direction d = rotation by −90°·d about Y**: North keeps +Z, East → −X, South → −Z, West → +X.
- World = (X·32, Y·8, Z·32) + local.
- North→East is a right turn in AC's frame. This is independent evidence that TM and AC share handedness (no mirroring).

Other placement facts:
- Block info **file names can differ from block ids** (`StadiumRoadMainStartLine` is in `StadiumRoadMainStart.TMEDClassic.Gbx`).
  Index by `Ident.Id`.
- The placed block's `Variant` (flags & 0x3F) indexes `GroundMobils`/`AirMobils` according to `IsGround`.
- Terrain blocks (Pool, Water, Dirt, Grass) sit at coord y=0 with their surface modelled at local y≈9. Ground road blocks
  sit at y=1 with the road at local y≈1, so both end up at world y≈9. **The default grass field is not stored in the map.**

## Open: handedness
Is TM's world left-handed relative to AC's? Mapping (x, y, z) → (x, y, z) directly may mirror the track. Plan:
1. In Phase 1, build the synthetic L-shaped track. Confirm in game which way it turns relative to `AC_START_0` facing.
2. In Phase 4, convert a TMNF map whose first corner is known (from the TMX screenshot or by driving it in TMNF) and
   compare.
3. Fix `TmToAcTransform` (probably negate X or Z) and lock it with a unit test using a known asymmetric map.

## Spawn heights (found in in-game check round 2, 2026-10-01)
TM spawn locations are at car-centre height, about 2.2 m above the block base. A fixed 1 m drop works on flat road, but
**`StadiumRoadMainStartLine` has a raised start pad** (local y 2.0, road 1.34). The fixed drop put the AC spawn and pit
dummies ~0.8 m inside the pad, which caused the collision glitch at the start ramp. All AC_* dummies are now snapped to the
collision surface under them (+5 cm). There's a regression test on R1.
