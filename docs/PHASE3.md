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
- [ ] Ride history (saved rides with summary), power curve over time
- [ ] Fitness / fatigue / form (CTL / ATL / TSB) from the rides' TSS
- [ ] Adaptive training plans over several weeks
- [ ] Backup: export / import of settings, workouts and ride history

### D. Controls
- [ ] Zwift Click / Play (BLE, community-documented protocol) – prototype, see CONCEPT.md
- [ ] Virtual gears (SIM mode)

### Done when
- A plan runs for weeks, rides upload to Strava automatically.
