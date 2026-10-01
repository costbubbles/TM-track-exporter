# S4: Ghost / replay parsing (TMNF)

**Verdict: 🟢 GREEN.** It was tested on the WR replay of TMNF-X #924307 (`/recordgbx/7325831`, 1:29.250) with GBX.NET 2.4.5.

## API
```csharp
Gbx.LZO = new GBX.NET.LZO.Lzo(); Gbx.ZLib = new GBX.NET.ZLib.ZLib();
var replay = Gbx.ParseNode<CGameCtnReplayRecord>(path);
var ghost  = replay.Ghosts[0];                  // CGameCtnGhost
ghost.RaceTime, ghost.Checkpoints               // Checkpoint[] with .Time (incl. finish as last)
var data = ghost.SampleData;                    // CGameGhost.Data: SamplePeriod=100 ms, IsFixedTimeStep
foreach (CSceneVehicleCar.Sample s in data.Samples) ...
```

## What each sample gives us (`CSceneVehicleCar.Sample`)
- `Position` (world meters, TM space), `Rotation` (quaternion), `Velocity`, `AngularVelocity`, `VelocitySpeed`,
  `SpeedForward`, `SpeedSideward`.
- `Gas`, `Brake`, `Steer`, `RPM`, `TurboStrength`.
- Per wheel: `xxOnGround`, `xxIsSliding` and `xxGroundContactMaterial`, where the material is the physics surface name: `Concrete`, `Asphalt`,
  `Turbo_Deprecated`, `TurboRoulette_Deprecated`, `Metal`, `Rubber` and so on.

## Results on the test replay
- 893 samples at 100 ms, matching the race time.
- 12 checkpoint times (finish is the last), strictly increasing.
- Airborne (all four wheels off ground) 390/893 samples, or 44%. This is a PressForward/loop-heavy map, so it's a good "Red" reference.
- Contact materials: Concrete 476, Asphalt 218, TurboRoulette 144, Turbo 29, Metal 23, Rubber 3.

## Use in the pipeline
| Need | Source |
|---|---|
| AI line path | Resample `Position` at 1–2 m, smooth it, map TM→AC |
| Checkpoint order | Interpolate the position at each `Checkpoint.Time` (index ≈ t / SamplePeriod) and match it to the nearest CP block |
| Compatibility | Airtime runs (all wheels off), upside-down (rotation up-vector · world up < 0 while on ground → wallride/loop), surface mix |

## Gotchas
- Finding the sample nearest a CP time with `MinBy(Time)` returned sample 0 for one CP. Index by `t / SamplePeriod`
  instead of trusting the per-sample `Time`.
- TMNF waypoint blocks are identified by name: `...StartLine`, `...FinishLine`, `...Checkpoint*`, `StadiumCheckpointRing*`.
  `WaypointSpecialProperty` is null in TMNF. Note that `...BiSlopeStart`, `...LoopStart` and `...SlopeStart` are **not** waypoints, so
  "Start" substring matching is wrong. Use an explicit table in `data/block-flags.tmnf.json`.
- TM2020 maps expose `WaypointSpecialProperty.Tag` (`Spawn`, `Goal`, `Checkpoint`, `LinkedCheckpoint`), which is easier.
- TM2020 ghosts weren't tested. TMX rarely hosts TM2020 replays anyway (see S3).
