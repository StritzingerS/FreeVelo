# Phase 2 – Server & riding together

**Status: complete** (everything built). The real-device checks below – group ride with friends, more Strava
athletes, long validation ride – move to [phase 3](PHASE3.md) (section A).

Goal: three friends ride together in the same world, stay together (Keep Together), and a rider who stops
pedaling is towed along. Concept: [CONCEPT.md](CONCEPT.md) sections 9 (multiplayer) and 11 (accounts).

Server logic and network messages are plain C#, tested with xUnit like phase 1. The server is one Docker
container (ASP.NET Core + SQLite), so it runs on Oracle Free or any small VPS / home server.

## Backlog

### A. Server (`src/FreeVelo.Server`) – riders & group rides
- [x] ASP.NET Core project, health endpoint, configuration, Dockerfile, CI build + tests
- [x] SQLite storage (riders only – data minimization: no email, rides, FTP or weight on the server)
- [x] Riders without email or password: display name → server-generated device secret → JWT session;
      rename, delete; rate limits per client IP
- [x] Group rides (in memory): create (name, route or workout, mode Keep Together / Free, max riders, private or
      public), 4-letter invite code for private rides, public list, join, leave, host hand-over, idle cleanup

### B. Shared protocol (`src/FreeVelo.Shared`) – client + server
- [x] Network messages (rider state 4 Hz, snapshots, lobby events, emoji) – compact binary encoding without
      external dependencies (works with Unity IL2CPP)
- [x] Keep Together algorithm (server-authoritative anchor, elastic offsets, towing riders who stop) – in
      `FreeVelo.Core/Multiplayer`, routes shared by id (`BuiltInRoutes`)

### C. Server – realtime
- [x] WebSocket realtime: rider states in, snapshots out at 4 Hz; join mid-ride at the group's position;
      reconnect within 2 min keeps the place, then the rider leaves the ride
- [x] Keep Together anchor simulation on the server; Free mode relays positions
- [x] Emoji reactions (relay, max. one per second per rider)

### D. Unity app
- [x] Client code in `FreeVelo.Shared` (tested against the real server): REST client (`FreeVeloApi`), live
      connection (`LiveLink`), group state with smoothing between snapshots (`GroupRideState`)
- [x] First start: display name + server address (rider created in the background, secret kept on the device)
- [x] Group rides: create (Keep Together / Free, private with code / public), join with the 4-letter code, public
      list, share the code (share sheet + clipboard), waiting room with riders, leave; restored after an app restart
- [x] Other riders in the world (avatars in jersey colours, own lanes, cornering lean), name tags with W/kg,
      rider list with gaps, emoji buttons and bubbles (six drawn icons – emoji fonts do not render reliably)
- [x] Group ride HUD: connecting / towed status; Keep Together positions from the server (trainer gets the grade
      there), Free mode starts next to the group; workouts can be ridden inside a group ride (ERG)
- [x] Friends: "+" in the waiting room sends a request, the other accepts; both phones store each other's secret
      friend code (nothing stored on the server). Friends' private rides show up in the ride list and can be joined
      without the 4-letter code; a new friend code locks everyone out
- [→] First real test with three riders – moved to phase 3

### E. Deployment
- [x] Oracle Free VM setup guide (`docs/ORACLE.md`): Docker Compose with Caddy (HTTPS via Let's Encrypt),
      DuckDNS name; `deploy/` folder, checked in CI
- [x] Backups of the SQLite file (`deploy/backup.sh`, daily via cron)
- [x] Render (free, no credit card): `render.yaml` Blueprint with automatic deploys on push (`docs/RENDER.md`);
      the app re-registers riders a forgetful free server lost, and waits up to 70 s for a sleeping server

### F. Strava
- [x] OAuth token exchange / refresh through the server (client secret stays on the server, nothing stored):
      app starts the login with a random state, Strava sends the browser back to the server, the app collects the
      tokens once; tokens are kept on the phone
- [x] Upload the FIT file from the device after a ride (automatic / manual), queue while offline with back-off,
      duplicates count as uploaded; status and "View on Strava" in the recent rides and the ride summary
- [x] Strava API app registered, client id/secret on Render (`docs/RENDER.md` → Strava); first real upload
      (ramp test, 2026-11-06)
- [→] Ask Strava for more than one athlete – moved to phase 3

### G. Carried over from phase 1
- [→] Long validation ride on the Neo 2 – moved to phase 3

### Done when
- 3 friends ride together, stay together, and stop-and-tow works – built; the real test is in phase 3.
