# FreeVelo – Concept & Implementation Plan

> Source-available indoor cycling training app for small groups of friends (PolyForm Noncommercial 1.0.0).
> Focus: structured ERG workouts & adaptive training plans, riding together with "Keep Together".
> Status: concept v1.0 (2026-11-04)

---

## 1. Goals & Non-Goals

### Goals
- Structured training: workouts (ERG), workout editor, FTP tests, adaptive (rule-based) training plans.
- Ride together with 2–4 (max. 8) friends, with **Keep Together** (group speed = average W/kg) or free mode.
- Stylized 3D world (roughly TrainingPeaks Virtual level).
- Routes: built-in procedural fictional routes + **GPX import → 3D road with real surface & vegetation**.
- Android first (phone + tablet), later iOS & Windows.
- Tacx Neo 2, Wahoo Kickr (Core), HR monitors, power meters, Zwift Click/Play. ANT+ later (Windows).
- Strava auto upload (→ Garmin Connect via Strava sync). FIT export.
- German & English, metric units.
- Zero budget: free assets, free cloud tier, no paid services. No AI required at runtime.
- Source-available under PolyForm Noncommercial 1.0.0 (free noncommercial forks allowed, no commercial use by others).

### Non-Goals (for now)
- Gamification (XP, levels, unlocks), races, leaderboards.
- Chat / voice (only emoji reactions).
- Server-side storage of ride data (rides stay on device).
- Avatar customization (future).

---

## 2. Key Decisions

| Topic | Decision |
|---|---|
| Engine | **Unity 6 (LTS)**, URP mobile renderer, C# |
| Shared logic | Plain C# libraries (`netstandard2.1`), engine-independent and unit-testable |
| BLE on Android | Own Kotlin plugin (`.aar`), exposed to C# via a thin bridge |
| Server | **ASP.NET Core (.NET 10 LTS)**, WebSockets + MessagePack, PostgreSQL (or SQLite) |
| Hosting | **Oracle Cloud Always Free** (ARM VM), Caddy as reverse proxy with Let's Encrypt, free domain (e.g. DuckDNS) |
| Identity | **No email, no password:** on first start the app creates a rider with just a display name; the server returns a random secret that stays on the device and is used to get a JWT session. Private group rides are protected by a **4-letter invite code** |
| Client data | SQLite on device + FIT files; manual backup export/import (ZIP) |
| Strava | Server only proxies OAuth token exchange/refresh (keeps client secret). Upload goes device → Strava directly |
| License | **PolyForm Noncommercial 1.0.0** (source-available, no commercial use by others); contributor agreement in `CONTRIBUTING.md`; only CC0 / redistributable assets in the repo |

---

## 3. Architecture

```
┌──────────────────────── FreeVelo client (Unity 6) ────────────────────────┐
│ UI (UI Toolkit, Unity Localization de/en)                                  │
│ ┌──────────────┐ ┌────────────────┐ ┌──────────────┐ ┌──────────────────┐ │
│ │ Ride scene   │ │ Workout editor │ │ Plans/History│ │ Settings/Devices │ │
│ └──────┬───────┘ └───────┬────────┘ └──────┬───────┘ └────────┬─────────┘ │
│        │      FreeVelo.Core (pure C#, unit tested)            │           │
│        │  Physics · WorkoutEngine · Plans · Metrics · FIT · Zones          │
│        │  RouteModel · KeepTogether (client side) · Persistence (SQLite)   │
│ ┌──────┴───────────────┐ ┌────────────────────┐ ┌────────────────────────┐ │
│ │ FreeVelo.Devices     │ │ FreeVelo.Worldgen  │ │ FreeVelo.Net (client)  │ │
│ │ IBleTransport        │ │ GPX→spline, OSM,   │ │ REST + WebSocket       │ │
│ │ FTMS/CPS/HRM/FE-C/   │ │ DEM, mesh, scatter │ │                        │ │
│ │ ZwiftClick           │ │                    │ │                        │ │
│ └──────┬───────────────┘ └────────────────────┘ └───────────┬────────────┘ │
└────────┼───────────────────────────────────────────────────┼──────────────┘
         │ Android: Kotlin BLE plugin (.aar)                  │ HTTPS / WSS
         │ iOS: Swift plugin (later) · Windows: WinRT (later) │
         ▼                                                    ▼
  Trainer / HRM / PM / Zwift Click        FreeVelo.Server (ASP.NET Core, Oracle Free VM)
                                          Riders · Lobbies · Realtime relay ·
                                          Keep-Together anchor sim · Strava OAuth proxy
```

### Repository layout
```
FreeVelo/
├─ docs/                      # this concept, protocol docs, ADRs
├─ src/
│  ├─ FreeVelo.Core/          # netstandard2.1 – physics, workouts, plans, metrics, FIT
│  ├─ FreeVelo.Devices/       # netstandard2.1 – protocol parsers/encoders (no BLE I/O)
│  ├─ FreeVelo.Worldgen/      # netstandard2.1 – GPX, OSM, DEM, route package building
│  ├─ FreeVelo.Shared/        # netstandard2.1 – network DTOs (client + server)
│  └─ FreeVelo.Server/        # net10.0 – ASP.NET Core
├─ tests/                     # xUnit tests for all pure C# libs
├─ android/freevelo-ble/      # Kotlin BLE plugin (Gradle → .aar)
└─ unity/FreeVelo/            # Unity 6 project (references the libs as DLLs/asmdefs)
```
Most logic lives **outside Unity** in pure C#, so you can write and test it with your
normal C# workflow (IDE + xUnit) without knowing much about games.
Unity is the "shell" for rendering, input and UI.

---

## 4. Device Layer

### 4.1 Abstraction
```csharp
interface IBleTransport { Scan(); Connect(id); Subscribe(svc, chr); Write(svc, chr, bytes); Events... }
interface IPowerSource   { event Action<PowerSample> OnPower; }    // watts, cadence
interface IHeartRateSource { event Action<int> OnHeartRate; }
interface ITrainerControl {
    Task SetErgPower(int watts);
    Task SetSimulation(double gradePct, double crr, double cwa, double windMps);
    Task SetResistance(double pct);
}
interface IShifterInput  { event Action<ShiftEvent> OnShift; }      // Up/Down/Action
```
Protocol code (`FreeVelo.Devices`) only turns bytes into data and data into bytes. The platform plugin
only moves bytes. That makes protocols testable with recorded byte dumps.

### 4.2 Protocols
| Device | Protocol | UUIDs | Priority |
|---|---|---|---|
| Tacx Neo 2, Kickr Core | **FTMS** – Indoor Bike Data, Control Point (Request Control, Set Target Power, Set Indoor Bike Simulation) | Service 0x1826, IBD 0x2AD2, CP 0x2AD9 | Phase 0 |
| Power meters (+ trainers) | Cycling Power | 0x1818, Measurement 0x2A63 | Phase 0 |
| HR monitors | Heart Rate | 0x180D, Measurement 0x2A37 | Phase 0 |
| Tacx (fallback + road feel) | ANT+ FE-C over BLE (Tacx proprietary) | 6E40FEC1-B5A3-F393-E0A9-E50E24DCCA9E | Phase 4 |
| Zwift Click / Play | Proprietary, community reverse-engineered (handshake, possibly encrypted) | to verify | Phase 3 |
| ANT+ USB stick | ANT+ FE-C / PWR / HRM | – | Windows phase |

### 4.3 Device behavior
- **Power source selection:** trainer power or external power meter (user choice). Optional *power match*
  (ERG target corrected by power meter reading) – nice to have.
- **Android:** BLE runs in a **foreground service**, so the connection survives screen-off and app switching. Keep screen on during rides.
  Permissions: `BLUETOOTH_SCAN` / `BLUETOOTH_CONNECT` (Android 12+), location for older versions.
- Auto-reconnect, remembered devices, battery level, spindown/calibration hint.
- **ERG safety:** smooth transitions between targets. Detect "ERG death spiral" (cadence < 50 rpm for 3 s), then temporarily
  lower the target.

### 4.4 Virtual gears (Zwift Click/Play, on-screen buttons)
- 24 virtual gears (configurable). In **SIM mode** the gear factor scales the resistance sent to the trainer
  (simulated grade / rolling resistance scaled by `virtualRatio / referenceRatio`).
  Alternative if that feels wrong: "SIM via ERG" (compute target power from cadence × virtual gear × physics, then send it as ERG).
  → **Prototype both in Phase 3** and keep the better-feeling one.
- In **ERG mode** the buttons change the intensity of the **current block** (±1 % per click, configurable).

---

## 5. Simulation

### 5.1 Physics (per tick, 20–50 Hz)
```
P_wheel = P · η   (η = 0.975 drivetrain)
F = m·g·sin(θ) + m·g·Crr·cos(θ) + ½·ρ·CdA·(v + v_wind)²
m·dv/dt = P_wheel / v − F
```
- m = rider weight + 8 kg bike. CdA 0.32 m², ρ 1.225 kg/m³.
- **Crr depends on surface:** asphalt 0.004 · compacted gravel 0.006 · gravel 0.008 · dirt/forest 0.010 · grass 0.015.
- Grade sent to trainer = `grade × trainerDifficulty` (settings, 0–100 %; downhill optionally capped).
- Road feel (Tacx only, later): surface → vibration pattern, intensity slider, global off switch.

### 5.2 Route model
`Route` = list of points (distance, lat/lon or local xyz, elevation, smoothed grade, surface type, environment tags).
The same model is used for fictional and GPX routes. Positions are always **distance along the route** + lateral lane offset.

---

## 6. Workouts

### 6.1 Data model (JSON, own format)
```json
{
  "id": "4x4-vo2", "name": {"en": "4x4 VO2max", "de": "4x4 VO2max"},
  "tags": ["vo2max"], "author": "FreeVelo",
  "blocks": [
    {"type": "ramp",   "duration": 600, "from": {"pctFtp": 50}, "to": {"pctFtp": 75}},
    {"type": "repeat", "count": 4, "blocks": [
      {"type": "steady", "duration": 240, "target": {"pctFtp": 110}, "cadence": [90, 100],
       "cues": [{"at": 0, "text": {"en": "Go!", "de": "Los!"}}]},
      {"type": "steady", "duration": 180, "target": {"pctFtp": 55}}
    ]},
    {"type": "freeride", "duration": 300},
    {"type": "steady", "duration": 600, "target": {"watts": 120}}
  ]
}
```
- Block types: `steady`, `ramp`, `repeat` (nestable), `freeride` (SIM mode inside workout), `maxEffort` (for tests).
- Targets: `pctFtp` **or** absolute `watts`. Optional: cadence range, text cues.
- Import: `.zwo`, `.erg`, `.mrc`. Export: `.zwo` + own JSON.

### 6.2 Editor
- Timeline with blocks as bars (height = intensity, color = zone).
- **Drag & drop zones** from a palette onto the timeline. Drag edges to change duration. Drag top to change intensity.
- Select a block → panel with exact values (watts / % FTP, duration, cadence, cues).
- Group blocks → repeat block. Copy/paste, undo/redo.
- Shows total duration, estimated TSS/IF, time in zones.

### 6.3 Zones
- Default: Coggan 7 zones (Active Recovery, Endurance, Tempo, Threshold, VO2max, Anaerobic, Neuromuscular).
- Editable in settings: names, boundaries as **% FTP or absolute watts**, colors.
- HR zones (% max HR or % LTHR) for display/analysis.

### 6.4 Runtime (WorkoutEngine)
- Executes the block tree, emits target power / cadence / cues per tick.
- **Intensity adjustment per block** during the ride (buttons / Zwift Click). Default: applies only to the current block.
  A global intensity (e.g. 95 %) is also available.
- Skip block, pause, extend block (+1 min).
- 3D world keeps running. HUD shows workout graph, current/next block, countdown.

### 6.5 Built-in workouts (sample library)
| Name | Content |
|---|---|
| 4x4 VO2max | 4 × 4 min @ 106–115 % / 3 min easy |
| 40/20s | 3 sets × 10 × (40 s @ 120 % / 20 s @ 50 %), 5 min between sets |
| Threshold 2x20 | 2 × 20 min @ 95–100 % / 5 min |
| Sweet Spot 3x15 | 3 × 15 min @ 88–93 % / 5 min |
| Over-Unders | 3 × 10 min alternating 2 min @ 95 % / 1 min @ 105 % |
| VO2max 5x5 | 5 × 5 min @ 110 % / 5 min |
| Tempo 2x30 | 2 × 30 min @ 80 % |
| Endurance | 60 / 90 / 120 min @ 60–70 % |
| Recovery | 30–45 min @ < 55 % |
| Sprints | 8 × 15 s max / 4:45 easy |

### 6.6 FTP tests
| Test | Protocol | FTP formula |
|---|---|---|
| Ramp test | Start 100 W, +20 W/min until failure (ERG) | 75 % of best 1-min power |
| 20-min test | Warm-up incl. 5 min hard, 20 min all-out (free ride / resistance mode) | 95 % of 20-min avg |
| 2 × 8-min test | 2 × 8 min all-out, 10 min recovery | 90 % of avg of both |

After a test the app **proposes** the new FTP (accept/reject). FTP history is stored.

---

## 7. Training Plans (adaptive, rule-based)

- Plan = weeks → days → workout references, plus goal (e.g. "FTP build 8 weeks", "Base 12 weeks"), with planned hours/week.
- Workouts reference % FTP, so **FTP changes automatically rescale** everything.
- Built-in plan templates: Base, FTP Build, VO2max Block, Maintenance (each 4–12 weeks, 3–5 sessions/week).
- **Adaptation rules** (all deterministic):
  1. Missed workout → if key session: move to next free day in the week. Otherwise drop it.
  2. Workout aborted early or with > 10 % intensity reduction → next session of the same type is −3 %.
  3. Several key workouts completed at > 100 % intensity → next week's key sessions +2 %.
  4. TSB (form) below −30 → insert a recovery day.
  5. Every 4th week is a recovery week (−40 % volume).
  6. FTP test scheduled at the end of each build block.
  7. Training days/week changed by user → plan is regenerated from the current week.

---

## 8. Analysis & History (local)

- Per ride: duration, distance, elevation, avg/NP power, IF, TSS, W/kg, avg/max HR, cadence, time in zones,
  lap/block breakdown, graphs (power/HR/cadence/elevation).
- History: calendar, weekly volume, **power curve** (best efforts 5 s … 60 min, compare periods),
  **fitness chart** CTL (42 d) / ATL (7 d) / TSB, FTP history.
- Storage: SQLite (`sqlite-net`) for summaries + `.fit` file per ride (FIT SDK for C#).
- Backup: export everything (DB + FIT files + custom workouts + settings) as a ZIP and import it on another device.

---

## 9. Multiplayer

### 9.1 Server responsibilities (data minimization)
Stored: rider id, display name, hash of the device secret, created / last seen. **No email, no rides, no FTP, no weight.**
Transient (RAM only, during a ride): lobby state, rider positions, current W/kg, emoji reactions.
GDPR: privacy policy, rider deletion in the app, minimal logs.

### 9.2 Lobbies
- **Private by default:** every group ride gets a **4-letter code** (letters without I, L, O, e.g. `KPWD`), unique among
  open rides. Friends join with the code (sent via WhatsApp etc.); joining by code is rate-limited against guessing.
- **Public rides** (optional) also appear in the lobby list and can be joined from it.
- A rider is in one group ride at a time; the host role passes on when the host leaves.
- Creator sets (fixed after creation): name, **route _or_ workout**, mode (**Keep Together** / **Free**), max riders (default 8).
  - Route chosen → everyone rides this route (GPX routes are sent to joiners as a compressed route package via the server).
  - Workout chosen → a random built-in route (looping) is used. The creator's workout is **proposed** to everyone.
    Each rider may pick another workout or free ride (ERG workout inside a group ride is always possible).
- **Join anytime:** joiners spawn at the group's current position.
- Lobby is closed when the last rider leaves (or after 30 min without activity).
- Emoji reactions (👍 🔥 💪 😅 👋 …) shown above the avatar + in a feed.

### 9.3 Network
- WebSocket (WSS) + MessagePack. Client → server 4 Hz: `{distance, lane, speed, wkg, cadence, active}`.
- Server → clients 4 Hz: snapshot of all riders + group anchor. Clients interpolate (100–250 ms buffer).
- REST for auth, lobby list, Strava OAuth proxy.

### 9.4 Keep Together algorithm (server-authoritative anchor)
```
every tick (4 Hz):
  active = riders where wkg >= 0.5 AND cadence > 0 (last 3 s)
  avgWkg = mean(active.wkg)          # if none active: anchor slows to a stop
  # anchor = virtual reference rider (75 kg + 8 kg bike, CdA 0.32, Crr of current surface)
  P_ref = avgWkg * 75
  anchor.speed, anchor.distance = Physics.Step(P_ref, grade(anchor.distance), surface, dt)

  for each rider:
    if rider in active:
      target = clamp(K * (rider.wkg − avgWkg), −MAX, +MAX)   # K = 15 m per W/kg, MAX = 25 m
    else:
      target = −MAX                                         # towed along at the back
    rider.offset = EMA(rider.offset, target, τ = 5 s)
    rider.distance = anchor.distance + rider.offset
```
- Riders who stop pedaling (drink/toilet) are **excluded from the average** and **towed** at the back of the group.
- Elastic offset makes visible who is putting out more W/kg.
- **SIM mode:** each trainer gets the grade at the rider's own position (nearly identical for everyone).
- **ERG mode:** trainer gets the personal workout target. The world position still follows the anchor.
- **Free mode:** each client simulates its own physics, server only relays positions (drafting later).

---

## 10. World Generation

### 10.1 Pipeline (one pipeline for fictional + GPX routes)
```
GPX ──► parse ─► resample (5 m) ─► smooth (spline) ─► map-match to OSM ways (Valhalla/OSRM)
                                                        │
DEM (Copernicus 30/90 m via Open-Meteo / OpenTopography) ─► corrected elevation, smoothed grade (cap ±25 %)
                                                        │
OSM Overpass (bbox around route): highway, surface, tracktype, landuse=forest, natural=wood,
                                  meadow, water, buildings
ESA WorldCover (fallback land cover)                    │
                                                        ▼
                                  RoutePackage (compressed, cached locally)
                                  - centerline + elevation + grade
                                  - per-segment surface (asphalt/gravel/forest road/dirt)
                                  - environment polygons (forest, field, water, village)
                                                        ▼
                   Unity: road mesh along spline (width/material by surface)
                          terrain chunks from DEM (only a corridor ~300 m around the road)
                          vegetation scattering (dense trees in forest polygons, sparse in fields)
                          simple buildings in built-up areas
```
- **Fictional routes:** generated procedurally (noise-based spline + elevation + random surface/environment tags),
  then the same Unity build step. A few curated seeds are shipped as built-in routes.
- Generation happens **on device** at import time (online), then works **offline**.
- Respect free API fair-use limits (Overpass, Valhalla public instance): one batched query per route, caching.

### 10.2 Assets (all free, preferably CC0 so they can live in the public repo)
- Kenney (CC0), Quaternius (CC0 low-poly nature, buildings, characters), Poly Haven (CC0 textures).
- Cyclist avatar: low-poly rider + bike, cadence-synced pedaling animation (simple rig). Mixamo is not
  redistributable in a public repo, so avoid it or keep it out of the repo.

---

## 11. Identity, Offline & Strava

- **No accounts with email and password.** First start: enter a display name → the server creates a rider and returns
  a random 256-bit secret, stored on the device. The app gets a session (JWT) with rider id + secret whenever needed.
  Nothing personal is on the server, so a new phone simply means a new rider (type the name again).
- **Solo rides need no server at all** (work offline from the first start). Multiplayer, route import (OSM/DEM) and
  Strava upload need internet. Uploads are queued until online.
- Abuse protection: rate limits per client IP (creating riders, sessions, joining by code). A server-wide access code
  can be added later if strangers ever become a problem.
- **Strava:** OAuth via server proxy (client secret only on server). Access/refresh tokens stored on device (Android Keystore).
  Upload FIT automatically after ride (toggle). Garmin Connect via Strava → Garmin sync or manual FIT import.
  Apply to Strava for higher athlete capacity. Fallback: user-supplied API key.

---

## 12. Settings (overview)
- Profile: display name, weight, height, FTP (+ history), max HR / LTHR.
- Zones: power + HR zone definitions (% or absolute).
- Trainer: difficulty (0–100 %), road feel on/off + intensity, ERG smoothing, power source, power match.
- Controls: Zwift Click/Play mapping, number of virtual gears, ERG step size.
- Language (de/en), units (metric), Strava connection, backup export/import, rider (change display name, delete).

---

## 13. Roadmap

| Phase | Content | Done when |
|---|---|---|
| **0 – Device prototype** | Repo, Kotlin BLE plugin, FTMS/CPS/HRM parsing, ERG + SIM commands, minimal Unity scene with numbers | Neo 2 holds ERG watts set from the app, HR shown |
| **1 – Solo training** | Physics, WorkoutEngine, sample workouts, import ZWO/ERG/MRC, FTP tests, simple procedural 3D route, HUD, FIT recording, ride summary, SQLite, de/en | A full 4x4 workout in ERG in the 3D world, saved as FIT |
| **2 – Server & together** | ASP.NET server on Oracle Free, accounts (Argon2id, mail), lobbies, realtime, Keep Together + Free mode, avatars, emojis, join mid-ride | 3 friends ride together, stay together, stop-and-tow works |
| **3 – Editor, plans, controls** | Drag & drop workout editor, zones settings, adaptive plans, history/power curve/CTL, Zwift Click/Play, virtual gears, Strava upload, backup | Plan runs for weeks, rides auto-upload to Strava |
| **4 – GPX worlds** | GPX import, map matching, DEM, OSM surface/vegetation, route sharing in lobby, surface-dependent Crr, Tacx road feel | Own GPX ridden with gravel/forest sections and trees |
| **5 – More platforms** | iOS (Swift BLE plugin), Windows (WinRT BLE + ANT+ USB), avatar customization | |

### Phase 0 backlog (first steps)
1. Create repo structure (section 3), PolyForm Noncommercial license + CONTRIBUTING.md, `.gitignore` (Unity + .NET), GitHub Actions for `dotnet test`.
2. `FreeVelo.Devices`: FTMS Indoor Bike Data parser, Control Point encoder/response parser, CPS parser, HRM parser + unit tests with byte samples.
3. Kotlin plugin: scan (filter 0x1826/0x1818/0x180D), connect, subscribe, write, foreground service, events to Unity.
4. Unity 6 project (URP, Android target, min API 29 / Android 10), C# bridge (`AndroidJavaObject` + callbacks).
5. Debug scene: device list, live watts/cadence/HR, ERG slider, grade slider.
6. Test on Tacx Neo 2. Record byte logs for regression tests. Later verify on a Kickr Core.

---

## 14. Risks

| Risk | Mitigation |
|---|---|
| Zwift Click/Play protocol is proprietary and may change | Isolate in its own module; on-screen buttons always available |
| No Kickr Core / Zwift Click for testing | Ask friends; byte-log recording tool built into the app |
| BLE stability on various Android phones | Foreground service, reconnect logic, test on 2–3 devices |
| 3D performance on mid-range phones | Low-poly assets, corridor-only terrain, LOD, GPU instancing for trees, 30 fps option |
| Strava athlete capacity for public app | Apply early; user-API-key fallback |
| Free API limits (Overpass, Valhalla) | Batch + cache, one import per route, fallback without map matching |
| Oracle Free Tier availability/account issues | Server is a single Docker container – movable to any small VPS / home server |
| Learning curve Unity/3D | Keep logic in pure C#; Unity only for presentation; start with very simple visuals |
