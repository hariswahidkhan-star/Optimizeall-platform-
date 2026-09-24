# Deploying the demo to Render

Two Render Blueprints create the whole staging/demo environment in one step. Pick one:

| Blueprint | File | Services | Good for |
|---|---|---|---|
| **SQLite** (default) | `render.yaml` (repo root) | web + API (database file on the API's disk) | Cheapest demo/staging; one API instance |
| **MySQL** | `deploy/render/render-mysql.yaml` | web + API + MySQL container on a disk | Several API instances; MySQL tooling |

Both run the same images and code; only the database differs (`Database__Provider`).

### SQLite (`render.yaml`)

| Service | Type | What it is |
|---|---|---|
| `optimizeall-web` | Web service (public) | nginx serving the React app; proxies `/api`, `/t` and `/health` to the API |
| `optimizeall-api` | Private service + 1 GB disk at `/app/storage` | ASP.NET Core API. The disk holds the database (`db/optimizeall.db`, its `-wal`/`-shm` files and the `optimizeall-keys/` encryption key ring) and uploaded files (`files/`). Applies migrations and loads the demo data on first start |

* **Cheapest:** two `starter` services and one small disk; no database service.
* **Exactly one API instance.** SQLite is a file on one disk: never scale `optimizeall-api` above 1 instance or turn on
  autoscaling (the Blueprint pins `numInstances: 1`). A service with a disk is also stopped before its new version
  starts, so each deploy has a short downtime.
* **Back up the database.** Render snapshots disks daily; restore one from the API service's **Disks** page. The
  snapshots are crash-consistent, which SQLite's write-ahead log recovers from. For an off-platform copy, take it
  while the app is idle (e.g. suspend `optimizeall-web` for a minute) from the API service's **Shell**:
  `tar czf /tmp/optimizeall-backup.tgz -C /app/storage db files`, then download it (e.g. `scp` over Render SSH).
  Always keep `db/optimizeall.db`, `db/optimizeall.db-wal` and `db/optimizeall-keys/` together: the keys decrypt
  stored payout destinations.
* Resetting the demo: delete the files in `/app/storage/db` (Shell) and restart the service, or delete the disk.

### MySQL (`deploy/render/render-mysql.yaml`)

| Service | Type | What it is |
|---|---|---|
| `optimizeall-web` | Web service (public) | nginx serving the React app; proxies `/api`, `/t` and `/health` to the API |
| `optimizeall-api` | Private service + 1 GB disk | ASP.NET Core API (uploads on the disk); applies migrations and loads the demo data on first start |
| `optimizeall-mysql` | Private service + 5 GB disk | MySQL 8 (Render has no managed MySQL, so it runs as a container with a persistent disk) |

* The API can run several instances (named locks use MySQL `GET_LOCK`, row locks use `SELECT … FOR UPDATE`), but
  uploads are on a per-instance disk, so for more than one instance move files to shared storage first.
* Resetting the demo: delete the `optimizeall-mysql` disk (or the service) and redeploy.

Only the web service is reachable from the internet. All services run in the Singapore region (they must share a
region to use Render's private network; change `region` on all of them together if you prefer another).

## Steps

1. Sign in at [dashboard.render.com](https://dashboard.render.com) and connect your GitHub account (grant access to
   `optimizeall-platform-`).
2. **New → Blueprint**, pick the repository and the branch.
   * SQLite: keep the default **Blueprint Path** `render.yaml`.
   * MySQL: set **Blueprint Path** to `deploy/render/render-mysql.yaml`.

   Then **Apply**. (An existing Blueprint's file path can be changed later under the Blueprint's **Settings**.)
3. Render asks for the one value it can't generate: **`Email__AppBaseUrl`**. Enter the web service's public URL,
   normally `https://optimizeall-web.onrender.com`. If Render gives the service a different URL (it adds a suffix
   when the name is taken), update `Email__AppBaseUrl` on `optimizeall-api` afterwards and redeploy the API.
4. Wait for the services to go live (first build ≈ 10–15 min; the API migrates and seeds, with MySQL after waiting
   for the database).
5. Open the web service URL and sign in with a demo account.

Secrets (`Jwt__SigningKey`, `Security__HashSalt`, and for MySQL `MYSQL_PASSWORD`/`MYSQL_ROOT_PASSWORD`) are generated
by Render and never stored in the repository.

### Optional: Sign in with Google

Both Blueprints declare `Authentication__Google__ClientId` and `Authentication__Google__ClientSecret` on
`optimizeall-api` with `sync: false`: Render asks for them when the Blueprint is created, and you can leave them empty
(Google sign-in then stays off and its button is hidden). To turn it on later:

1. Google Cloud console → **APIs & Services → Credentials → Create credentials → OAuth client ID** (type **Web
   application**) with the **authorized redirect URI** `https://<web-url>/auth/google/callback`, e.g.
   `https://optimizeall-web.onrender.com/auth/google/callback` (the same host as `Email__AppBaseUrl`). Configure the
   OAuth consent screen (scopes `openid`, `email`, `profile`) and publish it. Details: [DEPLOYMENT.md § 5.11](DEPLOYMENT.md#511-sign-in-with-google-optional).
2. Render dashboard → `optimizeall-api` → **Environment**: set `Authentication__Google__ClientId` and
   `Authentication__Google__ClientSecret` (the secret stays in Render, never in the repository), then **Save, rebuild
   and deploy**.
3. The sign-in and registration pages now show **Continue with Google**.

Switching an existing environment between the two Blueprints does not move data: the demo data is simply seeded
again into the new database.

## Demo accounts

All use the password `Demo#2026!pass` — full list and walkthroughs in [DEMO.md](DEMO.md).

| Email | Portal |
|---|---|
| `sara.participant@demo.optimizeall.app` | Participant (rich history) |
| `reviewer1@demo.optimizeall.app` / `reviewer2@…` | Reviewer |
| `manager@demo.optimizeall.app` | Campaign manager |
| `finance1@demo.optimizeall.app` / `finance2@…` | Finance (two people for four-eyes steps) |
| `admin@demo.optimizeall.app` | Admin (all agency areas too) |
| `am@demo.optimizeall.app`, `sales@…`, `seo@…`, `ads@…`, `social@…`, `designer@…` | Agency staff portal |
| `owner@nimbus.demo.optimizeall.app`, `approver@nimbus.…` | Client portal (Nimbus Fitness) |

The public agency website is the web service's root URL; no sign-in needed.

## Things to know

* **Cost.** Private services and disks need paid instances. SQLite: two `starter` services + a 1 GB disk. MySQL: three
  `starter` services + two disks (5 GB MySQL, 1 GB uploads). You can switch `optimizeall-web` to `free`, but it then
  sleeps when idle and the first visit takes ~1 minute.
* **Email is not sent.** The demo writes email to files. To follow a verification or reset link for a newly
  registered account, open `https://<web-url>/api/v1/dev/mailbox?to=<email>`. For real email set `Email__Mode=Smtp`,
  the `Email__Smtp*` settings and `DevTools__MailboxEnabled=false`.
* **API docs** are at `https://<web-url>/api/docs`.
* **Payments** are recorded manually; no money moves. WhatsApp shows as not configured.
* These Blueprints are for staging/demos (demo accounts, dev mailbox, Swagger on). For production follow
  [DEPLOYMENT.md](DEPLOYMENT.md): real SMTP, no demo seed, Swagger off, backups.

## Verified locally

Both Blueprints were reproduced end to end with Docker, most recently on **2026-09-24** (images built from
`backend/Dockerfile` and `frontend/Dockerfile`; every `envVars` entry translated to container environment, random
values for `generateValue` keys, `Email__AppBaseUrl` set, the Google keys left empty; a private network in
`10.0.0.0/8` like Render's; every disk an empty volume containing a root-owned `lost+found`; the API container started
as root, as Render does). All checks went through the web container only, as the internet would reach it.

* **SQLite (`render.yaml`):** the entrypoint gave the `app` user (uid 1654) `db/`, `files/`, `mail/` and even
  `lost+found`, and the API runs as `app`; every file on the disk is owned by `app`. The SQLite migrations applied,
  Baseline + Demo seeded (≈ 60 s on first start), `/health/ready` answered 200.
* **MySQL (`deploy/render/render-mysql.yaml`):** MySQL 8.0 started with the Blueprint's `dockerCommand` on a disk
  with `lost+found` (initialized into `data/`); the API, started at the same time as MySQL, waited for it, applied the
  MySQL migrations and seeded; same results for every check below.
* **Through nginx (`API_HOSTPORT`, `REAL_IP_FROM`, `PORT=8080`):** `/` returns the app shell (`no-cache`, CSP) and
  its hashed JS bundle (`immutable`); client routes fall back to the shell and unknown `/assets/*` are 404;
  `/api/v1/public/site`, `/api/v1/public/home`, `/robots.txt`, `/sitemap.xml` (74 URLs on `Email__AppBaseUrl`),
  `/health/live`, `/health/ready`, `/healthz`; `/e/o/x.gif` answers `image/gif` with `Cache-Control: no-store`;
  `/api/v1/auth/providers` reports Google disabled; `/api/v1/content/copy`; `/api/docs` (Swagger on).
* **Sign-in** (`POST /api/v1/auth/login`, refresh cookie `Secure`) for the participant, reviewer, manager, finance,
  admin, `am@`, `seo@` and `owner@nimbus` accounts, with 2–7 authenticated GETs per portal (submissions, earnings,
  review queue, campaigns, calendar, ledger, payout batches, payments hub list + summary, admin users, roles list +
  catalog, audit log, copy editor, agency dashboard/clients/projects/tasks, SEO sites, client org team/onboarding) all
  200; a participant gets 403 and anonymous 401 on admin endpoints.
* **Dev tools:** a password-reset email appears in `/api/v1/dev/mailbox?to=…` with a link on `Email__AppBaseUrl`.
  `/api/v1/dev/test-accounts` answers (58 demo accounts): `appsettings.Staging.json` sets
  `DevTools:TestLoginEnabled=true`, which is intended for this demo Blueprint (one-click sign-in on the login page, see
  [DEMO.md](DEMO.md)); set `DevTools__TestLoginEnabled=false` on `optimizeall-api` to turn it off.
* **Client IP:** with `REAL_IP_FROM`, the API's per-IP sign-in rate limit applies to the address the load balancer
  puts in `X-Forwarded-For`, not to the load balancer.
* **Restarts:** restarting the API (and, for MySQL, the database) kept all data; migrations were reported up to date
  and every demo seeder skipped (no duplicates).
* **API redeploys:** when the API came back on a new private IP, nginx (which resolves `proxy_pass` names only at
  start) kept proxying to the old address and answered 502 until restarted. Fixed in
  `frontend/nginx/12-optimizeall-platform.envsh`: the web container re-resolves the API host every
  `API_RESOLVE_INTERVAL` seconds (default 10) and reloads nginx when the address changes; at startup it waits up to
  `API_WAIT_SECONDS` (default 300) for the API host to resolve instead of exiting with `host not found in upstream`
  (e.g. when the web service of a new Blueprint starts before the API exists). Both cases were re-tested.
