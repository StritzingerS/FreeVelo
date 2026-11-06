# FreeVelo server on Render (free, no credit card)

[Render](https://render.com) runs the FreeVelo server from this repository: it builds the Docker image itself and
**redeploys automatically on every push** to the chosen branch. So after a one-time setup, updates to the server
need nothing from you. The settings are in [`render.yaml`](../render.yaml) in the repository root.

Time: about 10 minutes. You need a GitHub account (no credit card).

> Render's free plan and its limits may change – check <https://render.com/pricing> and Render's docs on free
> instances.

## One-time setup

1. Open <https://render.com> → **Get Started** → **GitHub** and allow Render to use your GitHub account.
2. In the Render dashboard: **New** → **Blueprint**.
3. **Connect a repository:** if `StritzingerS/FreeVelo` is not listed, choose *Configure GitHub* and give Render
   access to this repository (choosing *Only select repositories* is enough).
4. Select **StritzingerS/FreeVelo** and the branch **`phase2-server`** (after phase 2 is merged: `main`).
   Blueprint name: `freevelo`.
5. Render shows what it will create (one free web service `freevelo`, region Frankfurt) → **Apply**
   (or *Deploy Blueprint*).
6. The first build takes about 5–10 minutes (*Events* / *Logs* of the service show the progress).
7. At the top of the service page is its address, e.g. **https://freevelo-ab12.onrender.com**. Open
   `https://freevelo-ab12.onrender.com/health` in a browser → `{"status":"ok"}`.

## In the app

The app uses **https://freevelo.onrender.com** (FreeVelo's public server on Render) unless a rider enters another
address under **Settings → Group rides → Server** (leaving the field empty goes back to the default). If you host
your own Render service, enter its address there, e.g. `https://freevelo-ab12.onrender.com`. Riders who switch
servers are registered on the new one automatically with their name.

## What the free plan means for group rides

- **Sleeps after about 15 minutes without use.** The next request wakes it, which takes up to about a minute –
  the app waits for that. Tip: open the `/health` address in the phone's browser a minute before the group ride.
  While riders are connected, the server is in use and stays awake.
- **No storage that survives a restart or deploy.** The server then forgets all riders. The app notices and
  registers again with the stored name, so nobody has to do anything. Group rides live in memory anyway: a
  restart during a ride ends it (the apps show *The group ride has ended* – just create a new one).
- **750 free instance hours per month** – enough for one server running the whole month.

## Updates

Automatic: every push to the Blueprint's branch builds and deploys a new version (running group rides end at
that moment). Render emails you when a deploy fails; details are under the service's *Events* and *Logs*.

After phase 2 is merged into `main`: Render dashboard → *Blueprints* → `freevelo` → *Settings* → branch `main`.

## Optional: let Claude check deploys

Claude can see the deploy status and logs itself with a Render API key (Render → *Account Settings* →
*API Keys*). Claude asks for it with a secure form when needed – never paste the key into the chat.
