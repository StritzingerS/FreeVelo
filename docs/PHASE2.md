# Phase 2 – Server & riding together

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
- [ ] Network messages (rider state 4 Hz, snapshots, lobby events, emoji) – compact binary encoding without
      external dependencies (works with Unity IL2CPP)
- [ ] Keep Together algorithm (server-authoritative anchor, elastic offsets, towing riders who stop)

### C. Server – realtime
- [ ] WebSocket realtime: rider states in, snapshots out at 4 Hz; join mid-ride at the group's position
- [ ] Keep Together anchor simulation on the server; Free mode relays positions
- [ ] Emoji reactions

### D. Unity app
- [ ] First start: display name (rider created in the background, secret kept on the device)
- [ ] Group rides: create (private with code / public), join with the 4-letter code, public list, share the code
- [ ] Other riders in the world (interpolated avatars, name tags), rider list with W/kg, emoji feed
- [ ] Group ride HUD (gap to the group, towing indicator)

### E. Deployment
- [ ] Oracle Free VM setup guide: Docker, Caddy (HTTPS via Let's Encrypt), free domain (e.g. DuckDNS)
- [ ] Backups of the SQLite file

### F. Strava
- [ ] OAuth token exchange / refresh through the server (client secret stays on the server)
- [ ] Upload the FIT file from the device after a ride (toggle), queue while offline

### G. Carried over from phase 1
- [ ] Long validation ride: full 4×4 on the Neo 2, partly with the phone locked; FIT file checked in
      Garmin Connect / Strava

### Done when
- 3 friends ride together, stay together, and stop-and-tow works.
