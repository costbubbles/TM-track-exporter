# S6: fast_lane.ai format

**Verdict: 🟢 GREEN for reading/understanding, 🟡 for writing.** The layout was confirmed on `magione/ai/fast_lane.ai`.
Whether AC accepts a file **without** the trailing spatial grid must be tested in Phase 6.

## Layout (little-endian)
```
int32 version            7
int32 pointCount         N
int32 lapTime            0 (Magione)
int32 sampleCount        0 (Magione)
N × { float x, y, z; float length (cumulative m); int32 id (0..N-1) }      20 bytes each
int32 extraCount         N
N × 18 floats (72 bytes):
   speed, gas, brake, obsoleteLatG, radius, sideLeft, sideRight, camber,
   direction, normal.x, normal.y, normal.z, length, forward.x, forward.y, forward.z, tag, grade
   e.g. 52.9 1 0 0.052 9786.8 4.73 6.258 -0.001 -1 0.001 1 0.005 0.686 -0.505 0 -0.863 0 0
then spatial grid:
   int32 hasGrid (1)
   float[3] maxExtreme, float[3] minExtreme   (487.5,0,745.3) / (-517.2,0,-775.7)
   int32 neighboursConsidered (10), float samplingFactor (10.0)
   int32 gridW (100), int32 gridH (152), then per-cell point index lists (≈ 650 KB for Magione)
```
- Magione has 1,754 points over 2,455 m, so about 1.4 m spacing.
- Speeds are in m/s, and side distances are in meters to track edges.
- Point 0 is near the start/finish line, and the line runs in race direction.
- `drift/ai/fast_lane.ai` is only 12 bytes (`7, 0, 0`), so AC tolerates an empty/stub AI file. That gives us a safe "no AI" fallback.

## Plan
1. Phase 6: write version 7 with points + extras, and try `hasGrid = 0` with no grid.
2. If AC rejects that, implement the grid: bucket point indices on a 2D XZ grid with `samplingFactor` m cells.
3. Validate in game: AI drives a lap. Re-read our own file with the probe reader.

Reference reader: `tools/spikes/Kn5AiProbe` (`ai`, `aitail` modes).
