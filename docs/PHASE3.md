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
- [ ] Afterwards: merge `phase2-server` into `main`, switch the Render Blueprint to `main`

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
- [x] Virtual gears, 24 gears (0.75–5.5), both variants of CONCEPT.md 4.4 selectable in Settings → Controls:
      "Grade" (SIM grade so the trainer brakes like the road in the virtual gear) and "ERG" (SIM via ERG: road power
      at cadence × virtual gear); on-screen + / − while riding; in ERG workout blocks + / − change the block intensity
- [x] Zwift Click V2 (and Click / Play / Ride): own module `FreeVelo.Devices.Zwift` after the community-documented
      protocol (RideOn handshake, keep-alive every 3 s, 0x23 button bitmap). + / A harder, − / B easier.
      The Click V2 **left** unit is locked by Zwift (unlock by connecting it to Zwift, lasts about 24 h);
      the right unit works on its own (+ harder, B easier). The devices page shows the controller next to trainer, power meter and heart rate, with "locked" for a locked unit.
- [x] Controller settings (Settings → Trainer & shifting): what each Click V2 button does (harder / easier / nothing),
      number of virtual gears (12–30), ERG step per press (1–5 %)
- [ ] Test with the real Click V2 (both units, keep-alive, locked left unit) and decide Grade vs. ERG gears by feel

### Done when
- A plan runs for weeks, rides upload to Strava automatically.
