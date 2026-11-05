# Phase 1 – Solo training

Goal: a full structured workout (e.g. 4×4) in ERG mode in a 3D world on Android, saved as a FIT file
and uploaded to Strava later.

Pure C# logic lives in `src/FreeVelo.Core` (tested with xUnit, no Unity needed); Unity is the presentation layer.

## Backlog

### A. Core library (`src/FreeVelo.Core`) – no Unity needed
- [x] Rider physics (power → speed, grade, surface rolling resistance, wind)
- [x] Workout model: steady, ramp, free ride / max effort, nested repeats, cadence targets, text cues (de/en)
- [x] Workout engine: targets over time, pause, skip, extend, global + per-block intensity, seek (group join)
- [x] Power zones (Coggan 7, editable as % FTP or watts)
- [x] Metrics: NP, IF, TSS, mean-max / power curve, time in zones
- [x] Built-in workouts (recovery, endurance, tempo, sweet spot, threshold, over-unders, 4×4, 5×5, 40/20s, sprints)
- [x] FTP tests (ramp, 20 min, 2×8 min) with evaluation
- [ ] Workout JSON format (save/load, own workouts)
- [ ] Import `.zwo`, `.erg`, `.mrc`; export `.zwo`
- [x] Ride recorder (1 s samples: power, HR, cadence, speed, distance, altitude, target; lap per workout block)
- [x] FIT file writer (activity with records, laps per block, session incl. NP/IF/TSS/FTP; validated with fitparse)
- [x] Trainer controller (`DeviceHub`): device roles, live data, ERG/SIM via FTMS or FE-C, ERG ramping (50 W/s),
      low-cadence protection, resend target after reconnect, FE-C user config with every grade change
- [x] Simple procedural route (closed loop with hills)

### B. Unity app (`unity/FreeVelo`) – needs Unity 6 (see `docs/UNITY_SETUP.md`)
- [x] Unity project, Android settings
- [x] Use the C# libraries in Unity (Release build copies DLLs to `Assets/Plugins/FreeVelo`)
- [x] Bluetooth bridge: Kotlin `UnityBleBridge` as `.aar` (CI release) + C# `AndroidBleBackend`; simulator in the editor
- [ ] Foreground service for long rides
- [x] Device screen (scan, connect trainer / HR / power meter, remember devices) – first version
- [x] Ride scene: road, avatar, camera, HUD (power, HR, cadence, speed, workout graph, next block, cues) – first version
- [x] Workout library screen, start / pause / skip / intensity buttons – first version
- [x] Ride summary (NP, IF, TSS, power curve, FTP test result)
- [x] Save FIT automatically, share (Android share sheet) or save to Downloads/FreeVelo
- [x] Settings: weight, FTP, trainer difficulty, power source, language de/en
- [ ] Zone editor

### C. Done when
- 4×4 workout ridden on the Neo 2 in the 3D world, ERG follows the blocks, FIT file opens in Garmin Connect / Strava.
