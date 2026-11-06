# FreeVelo server on Oracle Cloud (free)

This guide puts the FreeVelo server on the internet for free, so friends can join group rides from anywhere:

- **Oracle Cloud Always Free:** a small Linux server (ARM, Ampere A1) that costs nothing.
- **DuckDNS:** a free name like `yourname.duckdns.org` for it.
- **Docker Compose** from this repository: the FreeVelo server plus **Caddy**, which gets the HTTPS
  certificate (Let's Encrypt) automatically.

Time: about an hour the first time. You need a credit card for Oracle's identity check (Always Free is not
charged), a GitHub or Google account for DuckDNS, and a terminal (Windows: PowerShell).

> Oracle changes its console now and then, so button names may differ slightly. The free limits below are those at
> the time of writing – check [Oracle's Always Free page](https://docs.oracle.com/en-us/iaas/Content/FreeTier/freetier_topic-Always_Free_Resources.htm).

---

## 1. Oracle Cloud account

1. Sign up at <https://www.oracle.com/cloud/free/>.
2. **Home region:** pick one close to you, e.g. **Germany Central (Frankfurt)**. It cannot be changed later, and
   the free ARM servers can only be created there.
3. After the sign-up you are in the **OCI console** (cloud.oracle.com).

**Recommended: upgrade to "Pay As You Go"** (console menu → *Billing & Cost Management* → *Upgrade and Manage
Payment*). Always Free resources stay free after the upgrade, but:

- Oracle **reclaims idle Always Free servers** of free-tier accounts (roughly: CPU, network and memory use below 20 %
  for 7 days). The FreeVelo server is idle most of the time, so without the upgrade it may be stopped. Pay As You
  Go accounts are not affected.
- New ARM servers are often "out of capacity" for free-tier accounts; after the upgrade they usually work.

To make sure nothing is ever charged, create a **budget** with an alert at €1 (*Billing & Cost Management →
Budgets*), and only create resources marked **Always Free-eligible**.

## 2. Create the server

Console menu → **Compute → Instances → Create instance**.

| Setting | Choose |
|---|---|
| Name | `freevelo` |
| Image | **Canonical Ubuntu 24.04** (the aarch64 version is chosen automatically for ARM) |
| Shape | *Change shape* → **Ampere** → **VM.Standard.A1.Flex**, **1 OCPU, 6 GB memory** (plenty for FreeVelo; Always Free allows 4 OCPU / 24 GB in total) |
| Networking | *Create new virtual cloud network* and *Create new public subnet*; **Assign a public IPv4 address**: yes |
| SSH keys | **Generate a key pair for me** → *Save private key* (keep this file safe – it is your login) |
| Boot volume | default (about 47 GB; Always Free includes 200 GB) |

**Create.** After a minute the instance is *Running*; note its **Public IP address** (e.g. `141.147.12.34`).

*"Out of capacity"?* Try another *Availability domain* (on the same page), or try again later (or upgrade, see 1).

## 3. Open the web ports (Oracle firewall)

The server needs ports 80 (certificate check) and 443 (HTTPS) open to the internet.

Instance page → **Primary VNIC → Subnet** → **Security** (or *Security Lists*) → *Default Security List* →
**Add Ingress Rules**:

| Source CIDR | IP protocol | Destination port |
|---|---|---|
| `0.0.0.0/0` | TCP | `80` |
| `0.0.0.0/0` | TCP | `443` |
| `0.0.0.0/0` | UDP | `443` (optional, HTTP/3) |

Port 22 (SSH) is already open.

## 4. Log in

Windows PowerShell (the key file from step 2, e.g. in Downloads):

```
ssh -i $HOME\Downloads\ssh-key-2026-11-06.key ubuntu@141.147.12.34
```

Answer `yes` to the fingerprint question. If Windows says the key's permissions are too open:

```
icacls $HOME\Downloads\ssh-key-2026-11-06.key /inheritance:r /grant:r "$($env:USERNAME):(R)"
```

All following commands run on the server (the prompt shows `ubuntu@freevelo`).

## 5. Open the ports in Ubuntu's own firewall

Oracle's Ubuntu images block everything except SSH with `iptables`, in addition to step 3:

```
sudo iptables -I INPUT -p tcp -m multiport --dports 80,443 -m conntrack --ctstate NEW -j ACCEPT
sudo iptables -I INPUT -p udp --dport 443 -j ACCEPT
sudo netfilter-persistent save
```

## 6. Install Docker

```
curl -fsSL https://get.docker.com | sudo sh
sudo usermod -aG docker ubuntu
exit
```

Log in again (step 4), so the `docker` group applies. Check: `docker run --rm hello-world`.

## 7. A name for the server (DuckDNS)

1. Open <https://www.duckdns.org>, sign in (GitHub or Google).
2. Add a subdomain, e.g. `freevelo-sami` → `freevelo-sami.duckdns.org`.
3. Set **current ip** to the server's public IP from step 2 → *update ip*.

The public IP of an Oracle instance stays the same as long as the instance exists, so this is done once.
Check (on your PC): `ping freevelo-sami.duckdns.org` shows the server's IP.

## 8. Start FreeVelo

On the server:

```
git clone https://github.com/StritzingerS/FreeVelo.git
cd FreeVelo/deploy
cp .env.example .env
nano .env
```

Set your name: `FREEVELO_DOMAIN=freevelo-sami.duckdns.org` (save: Ctrl+O, Enter, Ctrl+X). Then:

```
docker compose up -d --build
```

The first build takes a few minutes. Then Caddy fetches the certificate (a few seconds). Check in a browser:
**https://freevelo-sami.duckdns.org/health** → `{"status":"ok"}`.

*(Private repository? Clone with a GitHub token: `git clone https://<token>@github.com/StritzingerS/FreeVelo.git`,
token from GitHub → Settings → Developer settings → Fine-grained tokens, read access to the repository.)*

## 9. In the app

**Settings → Group rides → Server:** `https://freevelo-sami.duckdns.org` → **OK** (by default the app uses the
public server https://freevelo.onrender.com). Send your friends the address once; after that, only the 4-letter
ride codes.

---

## Running it

| Task | Command (in `~/FreeVelo/deploy`) |
|---|---|
| Status | `docker compose ps` |
| Logs (live) | `docker compose logs -f freevelo` (Ctrl+C to stop) |
| Update to the newest version | `git pull && docker compose up -d --build` |
| Restart | `docker compose restart` |
| Stop | `docker compose down` (data stays) |

The containers start again by themselves after a reboot of the server.

**Ubuntu updates:** `sudo apt update && sudo apt upgrade -y` now and then (security updates are installed
automatically by Ubuntu's *unattended-upgrades*).

### Backups

The server only stores riders (name + secret hash), so a loss is not dramatic – riders would just enter their
name again. A daily copy costs nothing though:

```
crontab -e
```

add the line

```
17 4 * * * /home/ubuntu/FreeVelo/deploy/backup.sh >> /home/ubuntu/freevelo-backups.log 2>&1
```

Copies land in `~/freevelo-backups` (14 days kept). To restore one:

```
docker compose down
docker run --rm -v deploy_freevelo-data:/data -v ~/freevelo-backups:/backup alpine:3 cp /backup/freevelo-2026-11-06.db /data/freevelo.db
docker compose up -d
```

---

## Troubleshooting

| Problem | Check |
|---|---|
| `/health` does not load | `docker compose ps` – both containers *running*? Ports 80/443 open in **both** firewalls (steps 3 and 5)? DuckDNS points to the right IP? |
| Browser warns about the certificate | `docker compose logs caddy`: Let's Encrypt must reach the server on port 80; the domain in `.env` must match exactly |
| App: "Server not reachable" | Address with `https://`, no typo, phone online; open `/health` in the phone's browser |
| App connects but group ride stays "Connecting …" | `docker compose logs freevelo` for errors; the live connection uses the same address (WebSocket through Caddy) |
| Instance was stopped by Oracle | Idle reclamation (see 1): start it again in the console and upgrade to Pay As You Go |
