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

## Open: handedness
Is TM's world left-handed relative to AC's? Mapping (x, y, z) → (x, y, z) directly may mirror the track. Plan:
1. In Phase 1, build the synthetic L-shaped track. Confirm in game which way it turns relative to `AC_START_0` facing.
2. In Phase 4, convert a TMNF map whose first corner is known (from the TMX screenshot or by driving it in TMNF) and
   compare.
3. Fix `TmToAcTransform` (probably negate X or Z) and lock it with a unit test using a known asymmetric map.
