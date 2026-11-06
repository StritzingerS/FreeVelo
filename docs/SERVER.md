# FreeVelo server

Group rides need the FreeVelo server (`src/FreeVelo.Server`): one small program with an SQLite file. Solo rides
work without it.

## Quick test on your PC (home Wi-Fi)

Good for a first group ride at home: the phones and the PC are in the same Wi-Fi.

1. Install the **.NET 10 SDK** (<https://dotnet.microsoft.com/download>), if not done for the Unity setup.
2. In the repository folder, start the server:

   ```
   dotnet run --project src/FreeVelo.Server --urls http://0.0.0.0:8080
   ```

   It prints `Now listening on: http://0.0.0.0:8080`. Leave the window open while riding.
3. Find the PC's address in the Wi-Fi: Windows `ipconfig` → *IPv4 address*, e.g. `192.168.1.20`.
4. Windows asks whether to allow the program in the firewall the first time: allow it for **private networks**.
   (If not asked: Windows Security → Firewall → *Allow an app* → `dotnet`.)
5. In the app: **Group** → enter your name and the server `http://192.168.1.20:8080` → **Continue**.
   The address can be changed later under **Settings → Group rides**.

Check from the phone's browser: `http://192.168.1.20:8080/health` shows `{"status":"ok"}`.

### With Docker instead

```
docker build -f src/FreeVelo.Server/Dockerfile -t freevelo-server .
docker run -d --name freevelo -p 8080:8080 -v freevelo-data:/data freevelo-server
```

## Data

The data folder (`src/FreeVelo.Server/data` with `dotnet run`, the `/data` volume with Docker) holds:

- `freevelo.db` – riders: id, display name, hash of the device secret, created / last seen. Nothing else.
- `jwt-signing.key` – signs the app sessions. If it is lost, apps simply sign in again.

Group rides live only in memory: a server restart ends running group rides (the apps then show
"The group ride has ended").

## Settings

`src/FreeVelo.Server/appsettings.json`, or environment variables (`Section__Key`, e.g. `Live__TickMs=250`):

| Setting | Default | Meaning |
|---|---|---|
| `Server:DataDirectory` | `data` | Database and key folder |
| `Jwt:SigningKey` | (generated) | Base64 key, at least 32 bytes; empty = generate once in the data folder |
| `Live:TickMs` | `250` | Snapshot interval (4 per second) |
| `Live:AwayGraceSeconds` | `120` | How long a rider whose app lost the connection keeps their place |
| `RateLimit:IdentityRequestsPerMinute` | `20` | Per IP: creating riders and sessions |
| `RateLimit:CodeJoinsPerMinute` | `10` | Per IP: joining with a code |

## Public server

**Easiest, no credit card:** Render's free plan, deployed automatically from this repository – see
[RENDER.md](RENDER.md). It sleeps when unused and forgets riders on restarts (the app re-registers them by itself).

**Always on, own server:** Oracle Cloud Free VM + Caddy (HTTPS) + a free DuckDNS name, all
free – step by step in [ORACLE.md](ORACLE.md). The `deploy/` folder has the Docker Compose setup it uses (also
fine for any other small Linux server or a home server).
