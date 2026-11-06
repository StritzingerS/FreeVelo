# Phase 1 – Solo training

**Status: complete** (merged into `main`). The long validation ride (see C) moves to phase 2.

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
- [x] Workout file format: ZWO-compatible XML with FreeVelo extensions (nested repeats, watts, de/en); workout library
- [x] Import `.zwo`, `.erg`, `.mrc` (Android file picker); export/share as standard `.zwo`
- [x] Ride recorder (1 s samples: power, HR, cadence, speed, distance, altitude, target; lap per workout block)
- [x] FIT file writer (activity with records, laps per block, session incl. NP/IF/TSS/FTP; validated with fitparse)
- [x] Trainer controller (`DeviceHub`): device roles, live data, ERG/SIM via FTMS or FE-C, ERG ramping (50 W/s),
      low-cadence protection, resend target after reconnect, FE-C user config with every grade change
- [x] Simple procedural route (closed loop with hills)

### B. Unity app (`unity/FreeVelo`) – needs Unity 6 (see `docs/UNITY_SETUP.md`)
- [x] Unity project, Android settings
- [x] Use the C# libraries in Unity (Release build copies DLLs to `Assets/Plugins/FreeVelo`)
- [x] Bluetooth bridge: Kotlin `UnityBleBridge` as `.aar` (CI release) + C# `AndroidBleBackend`; simulator in the editor
- [x] Foreground service for long rides: Bluetooth, ERG, workout, recording, low-cadence protection and distance keep
      running with the screen locked or another app in front (same C# ride logic, driven by ticks from the Android plugin)
- [x] Device screen (scan, connect trainer / HR / power meter, remember devices) – first version
- [x] Ride scene: road, avatar, camera, HUD (power, HR, cadence, speed, workout graph, next block, cues) – first version
- [x] Workout library screen, start / pause / skip / intensity buttons – first version
- [x] Ride summary (time, distance, elevation, avg power, calories, heart rate, power curve, FTP test result;
      NP / IF / TSS stay in the FIT file)
- [x] Save FIT automatically, share (Android share sheet) or save to Downloads/FreeVelo
- [x] Settings: weight, FTP, trainer difficulty, power source, language de/en
- [x] Zone editor (boundaries in % FTP or watts, reset to defaults)

### Added beyond the original plan
- [x] Drag & drop workout editor (undo/redo, TSS estimate); built-in workouts editable with reset to default
- [x] Modern UI redesign (design system, Inter font, icons), landscape only, safe area for camera cut-outs
- [x] Detailed rider and road bike model (sculpted body and face, drivetrain, disc brakes, STI levers), pedaling,
      leaning into corners, foot down when stopped, wheels follow the speed
- [x] Workout complete → ride continues as free ride (trainer in simulation, own lap); alert when in the background
- [x] Import fix for files with leading whitespace (zwo.fyi)

### Moved to phase 2
- Long validation ride: full 4×4 on the Neo 2, partly with the phone locked; FIT file checked in Garmin Connect / Strava.
  (Short rides with skips, locked phone, YouTube in front and free ride after the workout are already verified.)
- Strava auto-upload (OAuth handled by the FreeVelo server, so friends need no own Strava API app). Until then: share/save the FIT file and upload it manually.

### C. Done when (validation ride moved to phase 2)
- 4×4 workout ridden on the Neo 2 in the 3D world, ERG follows the blocks, FIT file opens in Garmin Connect / Strava.
