# S3: TMX API v2

**Verdict: 🟢 GREEN.** Everything we need for TMNF/TMUF is public, unauthenticated and was confirmed working
on 2026-09-30. TM2020 works too, but **TMX has almost no TM2020 replays**, which affects the AI-line source (see below).

## Sources
- Docs UI: https://api.mania.exchange/ (JS app). Machine-readable data is behind it:
  - `GET https://api.mania.exchange/api/methods/{id}` gives the method (Name, Url, Description).
  - `GET .../api/methods/{id}/params/input|output` gives the parameters.
  - `GET .../api/enums/{enumId}` gives enum values per site.
- Rules (docs home): a **`User-Agent` header is required**, responses are JSON, all times are UTC, and **caching is encouraged**.
  No published rate limit was found. Keep ≤ 2 concurrent requests per host and back off on 429/5xx.

## Endpoints (verified)

| Purpose | TMNF-X / TMUF-X (`tmnf.exchange`, `tmuf.exchange`) | TMX 2020 (`trackmania.exchange`) |
|---|---|---|
| Search | `GET /api/tracks?fields=...&{filters}` (method 43) | `GET /api/maps?fields=...&{filters}` (method 53) |
| Map file | `GET /trackgbx/{trackId}` → `application/gbx` | `GET /mapgbx/{mapId}` → `application/x-gbx` (method 67) |
| Replays for map | `GET /api/replays?trackId={id}&fields=ReplayId,ReplayTime,User.Name,IsBest&count=N` (method 45) | `GET /api/replays?mapId={id}` (method 66). Usually empty |
| Replay file | `GET /recordgbx/{replayId}` → `application/gbx` | `GET /recordgbx/{replayId}` |
| Images | `GET /trackshow/{id}/image/{n}`. `n=0` is a small image (~17 KB), `n=1` the full screenshot (~120 KB) | `GET /mapthumb/{id}`, `GET /mapimage/{id}/{n}` |
| Tags | `GET /api/meta/tags` | `GET /api/meta/tags` |

Not working: `/trackshow/{id}/thumb`, `/tracks/thumbnail/{id}`, `/replaygbx/{id}` and `/mapscreen/{id}/{n}` (all 404).

### `fields` parameter
It is **required** on `/api/tracks` and `/api/maps`. It is a comma-separated list, URL-encoded. Nested fields use dots and
arrays use `[]`, for example `Uploader.Name,Authors[].User.Name,Images[]`. An unknown field returns **HTTP 400** with
`application/problem+json` and the detail `Specified field 'X' does not exist`. Field sets differ per site. For example,
`Awards` exists on TMNF-X but not on TMX 2020.

### Useful TMNF-X track fields
`TrackId, TrackName, UId, AuthorTime, AuthorScore, Uploader.Name, Authors[] (User.UserId, User.Name, Role), Tags[] (ids),
Environment, Car, Mood, Routes, Difficulty, PrimaryType, Awards, HasThumbnail, Images[] (Width, Height, HasHighQuality),
UploadedAt, UpdatedAt, AuthorComments, WRReplay (ReplayId, ReplayTime), AuthorBeaten`.

`WRReplay.ReplayId` comes back in the search result, so **one search call gives us the WR ghost ID**, and no extra
replay-list call is needed for the default AI source.

### Useful filters (TMNF-X)
`name, author, tag[], etag[] (exclude), primarytype, environment[], mood[], difficulty[], routes[], authortimemin/max,
inreplays, inhasrecord, order1 (Track Search Orders), count, after/before (cursor paging)`. Responses are
`{ "More": bool, "Results": [...] }`. Paging is cursor-based via `after={lastId}`.

### Enums (from `/api/enums/{id}`)
- Environments
  - TMNF-X: `7=Stadium` only.
  - TMUF-X: `1=Snow 2=Desert 3=Rally 4=Island 5=Coast 6=Bay 7=Stadium`.
  - TMX 2020: `1=Stadium 2=Red Island 3=Green Coast 4=Blue Bay 5=White Shore`.
- Moods (TMUF/TMNF): `0=Sunrise 1=Day 2=Sunset 3=Night`.
- Track Primary Type: `0=Race 1=Puzzle 2=Platform 3=Stunts 4=Shortcut 5=Laps`.
  - Default browser filter: Race + Laps. Platform/Stunts/Puzzle are not meaningful in AC.
  - `5=Laps` is a strong multilap hint for circuit detection, but the map file stays authoritative.
- TMNF-X tags (`/api/meta/tags`):

  | Id | Tag | Id | Tag | Id | Tag | Id | Tag |
  |---|---|---|---|---|---|---|---|
  | 0 | Race | 5 | FullSpeed | 10 | PressForward | 15 | Speedfun |
  | 1 | Stunt | 6 | LOL | 11 | Trial | 16 | Endurance |
  | 2 | Maze | 7 | Tech | 12 | Grass | 17 | Altered Nadeo |
  | 3 | Offroad | 8 | SpeedTech | 13 | Story | 18 | Transitional |
  | 4 | Multilap | 9 | RPG | 14 | Nascar | | |

  - Useful heuristics: RPG/Trial/LOL/PressForward/Altered Nadeo → likely Red/Yellow. Tech/Nascar/Multilap → likely Green.

## Found while implementing the client (Phase 2)
- **`primarytype` is single-valued.** Repeating it (`primarytype=0&primarytype=5`) returns HTTP 400 "Invalid value
  for 'primarytype'". `tag` does accept repeats.
- **Multilap maps are tagged `Multilap` (tag 4) but usually have PrimaryType Race (0).** `primarytype=5` (Laps)
  returned nothing in the TMNF-X results checked. Use the tag to find circuits.
- `tmuf.exchange` serves the identical API (search, `trackgbx`, enums) and shares track ids with TMNF-X for Stadium.
- `id=<n>` on `/api/tracks` is the simplest single-track lookup.

## Implications for SPEC
1. Fix the SPEC §3 endpoint table: `trackshow/{id}/image/{n}` is correct for TMNF, and TMX 2020 uses `mapthumb`/`mapimage`.
2. **TM2020 AI line:** TMX rarely has replays. Options, decided in Phase 9:
   - (a) Centerline from the route graph (default).
   - (b) Let the user supply a local `.Replay.Gbx` / `.Ghost.Gbx` (their own PB, which the game saves locally).
   - (c) Nadeo Live Services leaderboard ghosts. These need Ubisoft authentication, so they're out of scope unless requested.
3. User-Agent: `Tm2Ac/<version> (+https://github.com/costbubbles/TM-track-exporter)`.
