# Reference maps (TMNF-X)

These are the maps used to check conversions end to end. **The map files are not committed** (they belong to their authors).
`tm2ac info tmnf <id>` downloads them into the local cache. Summary data below is from `tm2ac info` on 2026-10-01.

| # | TMX id | Name | Why it's here | Shape | Blocks | Waypoints | WR replay |
|---|---|---|---|---|---|---|---|
| R1 | [18451](https://tmnf.exchange/trackshow/18451) | Always be mine | **Simplest**: short (≈27 s) RoadMain tech map with few blocks. First map to convert in Phase 4 | A to B | 227 (22 clip, 0 pillar) | 1 start, 4 CP, 3 finish | 13237704 |
| R2 | [414041](https://tmnf.exchange/trackshow/414041) | ESL-Hockolicious | Classic tech A-to-B with many checkpoints (CP ordering, sectors) | A to B | 317 (43 pillar) | 1 start, 13 CP, 1 finish | 12563397 |
| R3 | [1531338](https://tmnf.exchange/trackshow/1531338) | Rockridge | Multilap circuit (start/finish block, 2 laps), water and pool blocks | Circuit | 704 | 1 start/finish, 4 CP | 1557786 |
| R4 | [509742](https://tmnf.exchange/trackshow/509742) | Dirty Dreams... | Dirt/offroad terrain (`StadiumDirtHill` ×544), dirt surface mapping. Has a start/finish **and** a separate finish | Circuit (1 lap) | 1595 (1271 ground) | 1 start/finish, 9 CP, 1 finish | 8000320 |
| R5 | [93481](https://tmnf.exchange/trackshow/93481) | Smooth Life | FullSpeed: high speed, jumps, circuit blocks. Expected Yellow | A to B | 836 (145 pillar) | 1 start, 3 CP, 2 finish | 11598575 |
| R6 | [11023114](https://tmnf.exchange/trackshow/11023114) | TWC 2023 /// Copenhagen | Nascar multilap, 19 checkpoints, large block count (performance) | Circuit (2 laps) | 1569 (177 pillar) | 1 start/finish, 19 CP, 1 finish | 11023127 |
| R7 | [924307](https://tmnf.exchange/trackshow/924307) | [PF] Ph/\ntom Fake | PressForward with loops and turbos, airborne 44% of the WR. **Expected Red** | A to B | 3668 | 1 start, 11 CP, 3 finish | 7325831 |

Notes:
- Several maps have **more than one finish block** (R1, R5, R7). Conversion needs to work out which one the route
  actually uses, from the ghost.
- R4 has both a start/finish (multilap) block and a plain finish.
