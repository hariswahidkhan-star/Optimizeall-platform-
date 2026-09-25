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
* **Upgrading a database from an earlier release** happens by itself: releases before 2026-09-25 each had their own
  `InitialCreate` migration, and a database created by one of them is upgraded on the first start of a newer release
  (`Database__BaselineUpgrade=Auto`, the default; [DATABASE.md § Baseline upgrade](DATABASE.md#baseline-upgrade-databases-from-earlier-releases)).
  The API backs the file up to `db/backups/optimizeall-<old baseline>-<utc>.db`, copies every row into the current
  schema, verifies it and swaps the files; the log line `Baseline upgrade from … finished` lists what was copied. The
  demo seed then adds any new reference data. Keep room for two copies of the database on the disk (the demo database
  is ≈ 20 MB), and delete old backups from the Shell once you no longer need them.
* **The demo always starts (`Database__BaselineUpgrade=AutoOrFresh`).** This Blueprint is a demo/staging environment,
  so if the upgrade cannot verify the data, or the database file is unreadable, the API does not stay down: it moves
  the old file **unchanged** to `db/backups/optimizeall-<old baseline>-<utc>-unmigrated.db` and starts on a fresh demo
  database. The deploy log then shows a `crit` line starting `DATABASE RESET` with the path and the reason; the old
  data is still in that file (stop the API, fix it, move it back to `db/optimizeall.db` to recover it). For data that
  matters use `Auto` (the default: the API stops and leaves the database as it was) or `Refuse` (never upgrade
  automatically); see [DATABASE.md § Baseline upgrade](DATABASE.md#baseline-upgrade-databases-from-earlier-releases).

### MySQL (`deploy/render/render-mysql.yaml`)

| Service | Type | What it is |
|---|---|---|
| `optimizeall-web` | Web service (public) | nginx serving the React app; proxies `/api`, `/t` and `/health` to the API |
| `optimizeall-api` | Private service + 1 GB disk | ASP.NET Core API (uploads on the disk); applies migrations and loads the demo data on first start |
| `optimizeall-mysql` | Private service + 5 GB disk | MySQL 8 (Render has no managed MySQL, so it runs as a container with a persistent disk) |

* The API can run several instances (named locks use MySQL `GET_LOCK`, row locks use `SELECT … FOR UPDATE`), but
  uploads are on a per-instance disk, so for more than one instance move files to shared storage first.
* Resetting the demo: delete the `optimizeall-mysql` disk (or the service) and redeploy.
* A MySQL database created by a release before 2026-09-25 is **not** upgraded automatically: the API stops with a
  message naming `scripts/upgrade-baseline-mysql.sh` ([DATABASE.md § Baseline upgrade](DATABASE.md#baseline-upgrade-databases-from-earlier-releases)).
  For a demo, resetting (above) is simplest.

Only the web service is reachable from the internet. All services run in the Singapore region (they must share a
region to use Render's private network; change `region` on all of them together if you prefer another).

## Steps

1. Sign in at [dashboard.render.com](https://dashboard.render.com) and connect your GitHub account (grant access to
   `optimizeall-platform-`).
2. **New → Blueprint**, pick the repository and the branch.
   * SQLite: keep the default **Blueprint Path** `render.yaml`.
   * MySQL: set **Blueprint Path** to `deploy/render/render-mysql.yaml`.

   Then **Apply**. (An existing Blueprint's file path can be changed later under the Blueprint's **Settings**.)
3. Render asks for **`Email__AppBaseUrl`** (optional, recommended). Enter the web service's public URL, normally
   `https://optimizeall-web.onrender.com`. If Render gives the service a different URL (it adds a suffix when the name
   is taken), update `Email__AppBaseUrl` on `optimizeall-api` afterwards and redeploy the API.

   **If you leave it empty** the API still builds correct links (see [Public URL](#public-url) below): it uses the
   address visitors reach the web service on, as nginx forwards it over the private network, so canonical URLs, the
   sitemaps, llms.txt, JSON-LD, certificate and LinkedIn links are on `https://optimizeall-web.onrender.com`. Emails and
   other links built by background jobs use the last such address the API saw (remembered once a day); before the
   first visit after a fresh install they are root-relative and the API logs a warning. Setting the value (or
   **Site settings → SEO → Site URL**) makes links independent of traffic and is what you want for a custom domain.
4. Wait for the services to go live (first build ≈ 10–15 min; the API migrates and seeds, with MySQL after waiting
   for the database). Even after Render shows the API as live, its **first start takes about 75 s** on starter
   (migrations plus the Baseline and Demo seed) before it serves requests; meanwhile `/health/live` already answers 200
   and every other API path 503 "starting", and the web service returns 503 for `/api` requests and pages.
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
* **First start ≈ 75 s.** On a fresh disk/database the API applies migrations and seeds the demo data before it serves
  requests, which takes about 75 s on starter's 0.5 CPU (later restarts: ≈ 20 s). A small startup responder holds the
  port meanwhile: `/health/live` answers 200, everything else 503 with `Retry-After`; `https://<web-url>/health/ready`
  answers 200 once the API is up. The log shows each phase (`Startup: schema ready …`, `Startup: seeder … done in … ms`
  with memory, `Startup: database initialization finished …`); a seeder that fails is logged and skipped instead of
  stopping the API.
* **Memory (starter = 512 MB).** The Blueprint sets `DOTNET_gcServer=0` and `DOTNET_GCHeapHardLimit=0x12C00000`
  (300 MiB managed heap), so the GC collects well before the instance limit even if the runtime cannot see it.
  Measured under `--memory=512m --cpus=0.5`: a fresh Baseline + Demo seed peaks at ≈ 155 MiB managed heap and
  225–260 MiB resident (anonymous) memory; upgrading and seeding the oldest release's database about the same, and an
  upgrade of a complete demo database ≈ 150 MiB. The log's `Startup:` lines show the memory after each phase.
  `render.yaml` configures no health check on the private API service: Render documents `healthCheckPath` for web
  services, and private-service support is not established, so the web service's own check is the one Render uses. It
  is nginx's `/healthz` (not `/`): public pages are rendered by the API (docs/SEO_CRO.md § 9), which may still be
  starting, and a starting API must not fail the web service's deploy.
* **The web service never waits for the API.** nginx starts and `/healthz` answers even when the API's host name does
  not resolve (API still being created, crash-looping or failed): `frontend/nginx/12-optimizeall-platform.envsh`
  resolves the host itself and keeps an `upstream` block pointed at its current address (checked every 2 s until it
  first resolves, then every `API_RESOLVE_INTERVAL`, default 10 s), reloading nginx when it changes. Meanwhile pages
  answer 503 + `Retry-After` with the app shell and `/api/…` a JSON 503; they recover by themselves when the API is
  up, without a web redeploy. A failing web deploy therefore points at the web image itself, not at the API.
* **Pages are always HTML.** nginx's `@document` keeps the API's HTML answers (200/301/404/410) and replaces every
  other status (400/401/403/405/408/413/429/500–504, often JSON from the API's rate limiter or error handler) with the
  app shell as `text/html` + 503, so no browser offers a page as a download (iOS Safari did for JSON bodies).
  `scripts/test-web-nginx.sh` (CI) checks this against a stub API.
* **API docs** are at `https://<web-url>/api/docs`.
* **SEO.** Public pages are server-rendered by the API through nginx (complete HTML for crawlers, real 404s),
  `/robots.txt`, `/sitemap.xml` (+ `/sitemaps/*.xml`), `/llms.txt` and `/{page}.md` come from the API too. Canonical URLs
  use the [public URL](#public-url) (**Site settings → SEO → Site URL**, else `Email__AppBaseUrl`, else the address the
  page was requested on); with a custom domain set the Site URL, point the
  domain at the web service, optionally turn on `Website__Seo__CanonicalHostRedirect=true` on `optimizeall-api` (301
  from `*.onrender.com` to the domain) and submit `https://<domain>/sitemap.xml` in Search Console. The demo
  Blueprints are staging: consider blocking the crawler groups under Website → SEO → Crawlers & AI unless the demo
  is the real site. Details: [SEO_CRO.md § 9](SEO_CRO.md#9-technical-seo-of-the-public-website).
* **Payments** are recorded manually; no money moves. WhatsApp shows as not configured.
* These Blueprints are for staging/demos (demo accounts, dev mailbox, Swagger on). For production follow
  [DEPLOYMENT.md](DEPLOYMENT.md): real SMTP, no demo seed, Swagger off, backups.

## Public URL

Every absolute link the API builds (canonical URLs, sitemaps, robots.txt, llms.txt, JSON-LD, Open Graph, certificate
verification links, Open Badges, LinkedIn "Add to profile"/"Share", invoice/proposal/referral links and emails) uses one
public origin, chosen in this order (`IPublicOrigin`, `backend/src/OptimizeAll.Api/Common/Hosting/PublicOrigin.cs`):

1. **Site settings → SEO → Site URL** (an admin setting; takes effect at once).
2. **`Email__AppBaseUrl`** on `optimizeall-api`, when set and not empty.
3. The address of the current request as the visitor used it: nginx forwards `Host`, `X-Forwarded-Host` and
   `X-Forwarded-Proto` (`frontend/nginx/snippets/proxy-api.conf`), and the API accepts them **only** from
   `Hosting__TrustedNetworks` (`10.0.0.0/8`, Render's private network) and only for a plain host name (no user info,
   path or odd characters, at most 253 characters). A client talking to the API from anywhere else cannot choose the
   host that ends up in links. Optional `Hosting__PublicHosts__0=…` (`*.example.com` allowed) restricts it further.
4. For emails and links built by background jobs (no request): the last origin seen under 3, remembered in the
   database (system setting `hosting.publicOrigin`, written at most once a day).
5. Otherwise links on the site are root-relative (`/verify/certificates/…`) and the API logs a warning. Links that
   leave the site are never sent relative: account emails (verification, password reset) and the invoice email to a
   client's billing address are not sent (logged as errors), emailing a proposal and starting Google or social sign-in
   answer 422 `hosting.public_origin_unknown`, email campaigns pause and journey emails wait, until 1–4 gives an origin.

Images and files the web app itself shows (course badges, certificate images and PDFs, partner logos) are always
root-relative (`/api/v1/public/learning/courses/{slug}/badge.svg`), so they load under the web app's CSP
(`img-src 'self'`) whatever the public URL is.

## Verified locally

**2026-09-25, API image under Render starter limits.** `backend/Dockerfile` built from scratch (`--no-cache`, the
NuGet packages in the restore layer), the image started with `render.yaml`'s API environment under
`docker run --memory=512m --cpus=0.5`: on an empty disk, and on the database of each of the 20 earlier releases of the
main line (5ca8a65 … 2d2b326, each created by building that commit and starting it once with Baseline + Demo). Every
start upgraded (where needed), seeded all 52 course packs and the demo data, answered `/health/live` within ≈ 3–5 s
and `/health/ready` plus the demo sign-in afterwards; no OOM (numbers in [DATABASE.md § Baseline upgrade](DATABASE.md#baseline-upgrade-databases-from-earlier-releases)).

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
* **Server-rendered pages and SEO files (2026-09-24, `render.yaml` images):** through the web container, `/`,
  `/services/seo`, `/blog` answered 200 with the page's own title, canonical on `Email__AppBaseUrl` and the built asset
  tags filled in by SSI (no `include` left); `/nope` 404; `/login` the shell with `X-Robots-Tag: noindex, nofollow`;
  `/robots.txt` (Sitemap line on the public URL), `/sitemap.xml` + `/sitemaps/*.xml`, `/llms.txt`, `/about.md` and
  `/.well-known/security.txt` from the API. With the API stopped, pages answered 503 + `Retry-After` with the app shell
  and `/healthz` stayed 200.
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
  `API_RESOLVE_INTERVAL` seconds (default 10) and reloads nginx when the address changes. Both cases were re-tested.
* **Web without the API (2026-09-25):** the startup wait for the API host (previously up to 300 s, after which nginx
  exited with `host not found in upstream` and the web deploy failed together with a crashing API) is now a
  best-effort `API_WAIT_SECONDS` (default 5) after which nginx starts regardless, proxying through an `upstream` block
  the envsh maintains. Re-tested with Docker: the web container started with no API container at all answered
  `/healthz` 200 after 6 s, pages 503 with the app shell, `/api/v1/public/site` a JSON 503; after the API started (on
  a different IP), the envsh logged `optimizeall-api now resolves to 172.18.0.4 (was unresolved); reloaded nginx` and
  `/`, `/learn`, `/partners`, `/sitemap.xml`, `/api/v1/public/site` answered 200 within 9 s, with the same nginx
  process. Against a running API, 18 paths (pages, SEO files, `/index.md`, `/t/…`, `/e/…`, `/health/*`, portals,
  `/__shell/…`) returned the same status, type and size as the previous nginx configuration.
* **Old database, new release (2026-09-25):** the API image of a730b95 created and seeded the SQLite database on a
  volume; the current API image started on the same volume upgraded it in 2.3 s (241 tables, 21 378 rows copied, 18 new
  tables, backup in `db/backups/`), every common table kept at least its rows (users 59 → 59, submissions 168 → 168,
  earnings 227 → 227; the rest only grew through the demo seed), `integrity_check` ok, no foreign key violations, and
  the demo admin signed in through nginx. The next start did nothing.
