# Phase 3 – Editor, plans, controls

Goal (see [CONCEPT.md](CONCEPT.md), roadmap): a training plan runs for weeks, rides upload to Strava automatically,
and the bike can be controlled with Zwift Click / Play buttons and virtual gears.

## Backlog

### A. Carried over from phase 2 – tests with real devices
- [ ] Group ride with three riders: riding together, Keep Together, stop-and-tow (the "done when" of phase 2);
      also check the latest fixes on the phones – smooth towing, a force-closed app drops out, friends see and
      join each other's private rides
- [ ] Ask Strava for more than one athlete (strava.com/settings/api; screenshots of "Connect with Strava" and of
      an upload) – needed before friends can connect Strava
- [ ] Long validation ride: full 4×4 on the Neo 2, partly with the phone locked; FIT file checked in
      Garmin Connect / Strava (carried over from phase 1)
- [x] Afterwards: merge `phase2-server` into `main`, switch the Render Blueprint to `main`

Known limitations from phase 2 (fix if they get in the way): the group ride only reconnects while the app is in the
foreground; pending friend requests are lost when the free server restarts; the free server needs up to a minute
to wake up.

### B. Already there (from phases 1–2)
- Workout editor, power zones settings, power curve in the ride summary, Strava upload with offline queue

### C. Training plans & history
- [x] Ride history: one summary per ride (`history.json`), saved FIT files read once at the first start
      (`FitReader`), FTP history (manual changes and accepted tests)
- [x] Fitness / fatigue / form (CTL 42 d / ATL 7 d / TSB), weekly volume, power curve (90 days vs. all time)
- [x] Training plans: templates Base, FTP Build, VO2max Block, Maintenance (4–12 weeks, 3–5 days a week);
      recovery week every 4th week, FTP test at the end of build blocks; adaptation rules 1–7 of CONCEPT.md
      (`PlanAdapter`, tested); today's session on the home screen, started with the plan's intensity
- [x] Group and free rides count as load (CTL/ATL) only; a session is done when its workout was ridden
- [x] Backup: one ZIP (settings, rides, history, own workouts, plan) via the share sheet; restored by picking it in the
      import file picker. Not included: server identity and Strava login
- [x] Ride detail (tap a ride in History or the calendar): tiles, power / heart rate / elevation graph, time in power
      and heart rate zones, laps per workout block (read from the FIT file)
- [x] Heart rate zones: 5 zones in % max HR or 7 zones in % LTHR (Friel), set in Settings → Profile
- [x] History month calendar: TSS per day, the plan's sessions as dots, tap opens the ride
- [ ] Test on the phone: plan for a few weeks, history after importing the existing rides, backup to another phone

### D. Controls
- [x] Virtual gears, 24 gears (0.75–5.5), one switch in Settings → Controls (on / off). How they reach the trainer
      is automatic: trainers with built-in virtual shifting (the Zwift protocol on the Zwift service FC82 /
      00000001-19ca…: Zwift Hub, newer Wahoo KICKR, Elite, Tacx Neo 2/2T/3M with current firmware, JetBlack, Van
      Rysel …) get the plain road and the gear ratio and shift themselves (`ZwiftTrainer`, community-documented
      protocol, byte patterns checked against captures from Zwift); all other trainers get a grade that brakes like
      the road in the virtual gear. On-screen + / − while riding; in ERG workout blocks + / − change the block
      intensity. (The earlier "SIM via ERG" variant is gone.)
- [x] Zwift Click V2 (and Click / Play / Ride): own module `FreeVelo.Devices.Zwift` after the community-documented
      protocol (RideOn handshake, keep-alive every 3 s, 0x23 button bitmap). + / A harder, − / B easier.
      The Click V2 **left** unit is locked by Zwift (unlock by connecting it to Zwift, lasts about 24 h);
      the right unit works on its own (+ harder, B easier). The devices page shows the controller next to trainer, power meter and heart rate, with "locked" for a locked unit.
- [x] Controller settings (Settings → Trainer & shifting): what each Click V2 button does (harder / easier / nothing),
      number of virtual gears (12–30), ERG step per press (1–5 %)
- [x] Trainers with the Zwift service (KICKR CORE, Neo 2 …) stay trainers; trainers / power meters with a heart rate
      service only provide heart rate while they send values (a heart rate sensor always wins)
- [x] ERG and cadence glitches: trainers sometimes report half or a third of the cadence while the power stays on
      target. Low-cadence protection now also needs the power to show it (swinging > 15 % in 4 s or < 85 % of the
      target); the cadence filter catches drops in two steps and holds a glitch up to 45 s
- [x] Workout editor: intervals inside intervals can be opened (e.g. 40/20s); interval blocks can skip their last
      block in the last repetition (all built-in workouts do: no recovery before the cool-down); discarding shows
      a revert arrow. Built-in warm-ups at most 12 min including the opener (FTP tests keep 15 min)
- [x] Quick start on the home page: a workout that suits the form (TSB), then the workouts ridden last
- [ ] Test with the real Click V2 (both units, keep-alive, locked left unit; the log of 2026-11-07 had the
      handshake and battery but no button messages)
- [x] Built-in virtual shifting tested on the Neo 2: works (2026-11-10)

### E. The mountain – two legendary climbs
- [x] Climb data from GPX (`tools/climbs/process_gpx.py`): Passo dello Stelvio (24.3 km, +1842 m, Ø 7.6 %) and
      Alpe d'Huez (13.8 km, +1118 m, Ø 8.1 %), every 5 m with the original length, turns, elevation and gradient
      (elevation smoothed over 200 m against GPS spikes). The GPX source files are not in the repo.
- [x] Mountain range next to the home world (`Mountain`, `MountainTerrain`, `MountainLook`): both climbs end at a
      shared summit (1577 m in-game), joined by a 1 km road over the top, so you ride down the other climb.
      Two valleys; the Alpe d'Huez valley is higher, its access road (7.0 km) and the Stelvio's (6.9 km) take
      up the difference at about 5 %. Forest → spruce/pine → treeline 550 m below the summit → meadows, rock, snow.
      Summit signs at the top.
- [x] Free ride: the access roads branch off the main loop at two junctions; the main loop always stays the
      default, so you only get to the mountain by choosing the turn.
- [x] Home page: "Legendary climbs" cards (profile, length, gain, best time) start a ride at the foot of a climb
      (route `climb-stelvio` / `climb-alpe-dhuez`: up, over the top, down the other side and back).
- [x] Group rides: the lobby chooses its route (main loop or one of the climbs); Keep Together runs on it.
- [x] Climb timer while riding (time, km to the summit, gap to the best time), result in the ride summary,
      best times and ascents in the history, climbs in the ride detail.
- [x] Braking before bends (`Cornering`): every bend has a safe speed (30° lean, a 10 m hairpin ≈ 27 km/h); the
      rider brakes early (4 m/s²) to reach it. Only the virtual speed is limited, the trainer keeps simulating
      the road. While braking the rider freewheels and the HUD shows "Braking for the bend". Group rides: the
      server's Keep Together group brakes so every rider (±25 m around the anchor) takes the bend safely.
- [x] Ride HUD: big main values (time, distance, elevation gained | power | speed, cadence, heart rate) in a wide
      top bar, power shown as a 1 s average; side cards below it; gear control as a pill above the strip;
      gradient chip only uphill from 3 %
- [x] Climb view (ClimbPro style): next 500 m coloured by gradient, only on climbs in free rides; whole-round
      overview on rounds otherwise. Climbs smoothed ±150 m (fewer gradient changes, times within ~1 %)
- [x] Smooth riding: the rider follows a smoothed path through corners (no drifting), brakes before bends,
      terrain kept below the roads at every level of detail
- [x] Start-up: loading screen until the world is built; the world (terrain) is prepared at build time
      (FreeVelo → Prepare world, Resources/FreeVeloWorld.bytes, not in git) or kept on the phone after the first
      start – loading ~0.2 s instead of ~2.3 s generating (desktop). The 3D meshes are still built at start-up
- [x] FIT files carry virtual GPS: the world lies on Ushishir (Kuril Islands, 47.52° N 152.80° E, Austria's
      latitude), so Strava takes the elevation from the file
- [ ] Look at the mountain on a phone: far tiles, fog, colours, frame rate of the streamed near tiles
- [ ] Check on Strava that a new ride shows its elevation; first build with the "Prepare world" build step

### Done when
- A plan runs for weeks, rides upload to Strava automatically.
