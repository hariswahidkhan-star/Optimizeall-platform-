# Optimize All — Deployment Guide

This guide covers local development, the Docker Compose staging/demo stack, and production deployment.
Operational runbooks (jobs, payouts, incidents, backups) are in [OPERATIONS.md](OPERATIONS.md); security
controls are in [SECURITY.md](SECURITY.md); every configuration key is documented in
[`/.env.example`](../.env.example).

## 1. Architecture

```
                    Internet (HTTPS)
                          |
             +------------v-------------+
             |  TLS reverse proxy / LB  |   HSTS, certificates, (optional) WAF
             +------------+-------------+
                          | http :8080  (X-Forwarded-For / X-Forwarded-Proto)
             +------------v-------------+
             |  web  (nginx, non-root)  |   SPA static files (frontend/dist)
             |  optimizeall-web image   |   /api/*, /t/*, /health/*  --> api
             +------------+-------------+
                          | http :8080
             +------------v-------------+        +---------------------------+
             |  api  (ASP.NET Core 8)   |------->|  SMTP provider            |
             |  optimizeall-api image   |------->|  WhatsApp Cloud API (opt.)|
             |  N instances             |        +---------------------------+
             |  background jobs inside  |
             +-----+--------------+-----+
                   |              |
      +------------v---+   +------v------------------------+
      |  MySQL 8       |   |  File storage /app/storage    |
      |  utf8mb4       |   |  (shared volume / NFS / EFS)  |
      |  + backups     |   |  screenshots & uploads        |
      +----------------+   +-------------------------------+

   migrate (one-shot)  optimizeall-migrator image: EF Core migrations bundle, runs before a new api version
```

* **Single origin.** The browser only talks to the web container; nginx proxies the API. No CORS is needed and
  the refresh cookie (`SameSite=Strict`, path `/api/v1/auth`) works without cross-site exceptions.
* **Stateless API.** Sessions are JWT access tokens + refresh tokens stored in MySQL. ASP.NET Data Protection
  keys (used to encrypt payout destinations) are stored in the `data_protection_keys` table, so every instance
  shares them. Background jobs coordinate through database leases (`job_leases`), so any number of API
  instances can run with `Jobs__Enabled=true`.
* **State lives in two places:** the MySQL database and the file storage directory. Back up both.

| Image | Built from | Purpose |
|---|---|---|
| `optimizeall-api` | `backend/Dockerfile` (target `runtime`, default) | API on :8080, non-root (uid 1654), alpine |
| `optimizeall-migrator` | `backend/Dockerfile --target migrator` | Self-contained EF Core migrations bundle (`/app/efbundle`) |
| `optimizeall-web` | `frontend/Dockerfile` | nginx-unprivileged (uid 101) on :8080 serving the SPA and proxying the API |

## 2. Prerequisites

| Tool | Version | Needed for |
|---|---|---|
| .NET SDK | 8.0.x | backend build/tests |
| Node.js / npm | 22 LTS | frontend |
| MySQL | 8.0 (local install or container) | development and integration tests |
| Docker + Compose v2 | 24+ / 2.20+ | staging stack, image builds |
| curl, openssl, mysql client | any | scripts |
| `dotnet-ef` (optional) | 8.0.10 | creating migrations: `dotnet tool install --global dotnet-ef --version 8.0.10` |

## 3. Local development

```bash
scripts/dev-setup.sh            # checks tools, creates DB/user, dotnet restore, npm ci
scripts/dev-start.sh            # API :5080 + Vite :5173 in the background (.dev/*.pid, .dev/*.log)
scripts/dev-stop.sh             # stops both (whole process groups)
scripts/test-all.sh             # backend build + unit + integration tests, frontend typecheck/lint/test/build
scripts/test-all.sh --e2e       # ...plus Playwright against a fresh database with the Demo seed
```

* The API runs with `ASPNETCORE_ENVIRONMENT=Development` and `appsettings.Development.json`: database
  `optimizeall_dev` (user `optimizeall` / `optimizeall_dev`), Baseline + Demo seed, emails written to
  `backend/src/OptimizeAll.Api/storage/mail` (browse them with the dev mailbox), Swagger at
  <http://localhost:5080/api/docs>, bootstrap admin `admin@optimizeall.local` / `Admin#Demo2026!`.
* The Vite dev server on :5173 proxies `/api` and `/t` to :5080, so the browser uses a single origin.
* If `dev-setup.sh` cannot create the database (no local root access), run as a MySQL admin:

  ```sql
  CREATE DATABASE IF NOT EXISTS optimizeall_dev CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci;
  CREATE USER IF NOT EXISTS 'optimizeall'@'localhost' IDENTIFIED BY 'optimizeall_dev';
  CREATE USER IF NOT EXISTS 'optimizeall'@'127.0.0.1' IDENTIFIED BY 'optimizeall_dev';
  GRANT ALL PRIVILEGES ON `optimizeall\_%`.* TO 'optimizeall'@'localhost', 'optimizeall'@'127.0.0.1';
  -- integration tests create and drop one database per test class:
  GRANT ALL PRIVILEGES ON `oa\_test\_%`.* TO 'optimizeall'@'localhost', 'optimizeall'@'127.0.0.1';
  ```
* Integration tests read `OPTIMIZEALL_TEST_MYSQL` (server-level connection string, default
  `Server=127.0.0.1;Port=3306;User=optimizeall;Password=optimizeall_dev;`).
* Schema changes: edit the entity + configuration, then
  `dotnet ef migrations add <Name> -p src/OptimizeAll.Infrastructure -s src/OptimizeAll.Api -o Persistence/Migrations`
  (run in `backend/`). Export the API contract with `scripts/export-openapi.sh` (writes `docs/api/openapi.json`).

## 4. Staging / demo with Docker Compose

`deploy/docker-compose.staging.yml` runs MySQL 8, [Mailpit](https://mailpit.axllent.org/) (SMTP catcher with a
web UI), the API and the web container.

```bash
scripts/staging-up.sh           # first run creates deploy/.env.staging with generated secrets, builds, starts, waits
scripts/staging-down.sh         # stop (keeps data)
scripts/staging-down.sh --volumes   # stop and wipe database + uploads (next start re-seeds)
```

| URL | What |
|---|---|
| <http://localhost:8080> | Web app (`WEB_PORT`) |
| <http://localhost:8080/api/docs> | Swagger UI (enabled in staging) |
| <http://localhost:8080/health/ready> | Readiness (checks the database) |
| <http://localhost:8025> | Mailpit: every email sent by the platform (`MAILPIT_UI_PORT`) |

* The API starts with `Database__InitializationMode=Migrate` and seed profiles `Baseline` + `Demo`, so the
  stack comes up with demo users, campaigns and submissions. **Demo accounts** are created by the Demo seed —
  see the API log (`docker compose -p optimizeall-staging logs api`) and [docs/DEMO.md](DEMO.md). The
  administrator is `BOOTSTRAP_ADMIN_EMAIL` / `BOOTSTRAP_ADMIN_PASSWORD` from `deploy/.env.staging`.
* Email is sent over real SMTP to Mailpit (`Email__Mode=Smtp`), exercising the same code path as production.
* Set `DEMO_SEED=Baseline` for an empty staging database; set `SECURE_COOKIES=true` when staging is served over
  HTTPS; set `PUBLIC_BASE_URL` to the URL users open (used in email and tracking links).
* To deploy prebuilt images instead of building: set `API_IMAGE`/`WEB_IMAGE` and run `scripts/staging-up.sh --no-build`.
* Staging secrets are generated per environment by `scripts/generate-secrets.sh`; never reuse them in production.

## 5. Production deployment

`deploy/docker-compose.production.example.yml` is a hardened single-host reference (read-only root
filesystems, dropped capabilities, secrets from files, one-shot migrations, log rotation). The same
principles apply on Kubernetes/ECS.

### 5.1 Build and publish images

Build all three images from the same commit and tag them immutably (git SHA or semver); deploy by tag or digest.

```bash
TAG=$(git rev-parse --short HEAD)
docker build -t registry.example.com/optimizeall-api:$TAG backend
docker build -t registry.example.com/optimizeall-migrator:$TAG --target migrator backend
docker build -t registry.example.com/optimizeall-web:$TAG frontend
docker push registry.example.com/optimizeall-{api,migrator,web}:$TAG
```

CI (`.github/workflows/ci.yml`, job `docker`) builds all three on every push without publishing; add a
publish job with registry credentials when a registry is chosen.

### 5.2 Reverse proxy and TLS

* Terminate TLS in front of the web container (cloud load balancer, Caddy, Traefik or a host nginx) and forward
  to `web:8080`. The web container passes `X-Forwarded-Proto` through (or sets it from its own scheme) and
  appends the client address to `X-Forwarded-For`; the API trusts these headers
  (`ForwardedHeaders` with any proxy), so **the API and web containers must not be reachable directly from the
  internet**. Keep them on a private network / loopback.
* If another load balancer sits in front of nginx, add `set_real_ip_from <lb-cidr>; real_ip_header X-Forwarded-For;`
  to the nginx config so rate limits and fraud hashing use the real client IP.
* The API sends HSTS on HTTPS requests. Enable the `Strict-Transport-Security` header for the SPA in
  `frontend/nginx/snippets/security-headers.conf` once the site is HTTPS-only (start with a short max-age).
* Redirect HTTP to HTTPS at the proxy. Allow request bodies of at least 12 MB (screenshot uploads).

### 5.3 Database: managed MySQL 8

* Use a managed MySQL 8.0 service (RDS/Aurora MySQL, Cloud SQL, Azure Database for MySQL) with automated daily
  backups, point-in-time recovery (binary logs) of at least 7 days, encryption at rest, and TLS in transit
  (`SslMode=Required` or `VerifyFull` in the connection string).
* Server settings: `character_set_server=utf8mb4`, `collation_server=utf8mb4_0900_ai_ci`, `time_zone='+00:00'`
  (the app stores UTC), `max_allowed_packet >= 64M`.
* Create the database with `CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci` and two accounts:
  * **migrator** — DDL rights on the schema (`ALL PRIVILEGES ON optimizeall.*`), used only by the migration job;
  * **app** — `SELECT, INSERT, UPDATE, DELETE, CREATE TEMPORARY TABLES, LOCK TABLES, EXECUTE` on `optimizeall.*`
    when migrations run as a separate job. (If you choose startup migrations, the app account needs DDL rights too.)
* See [OPERATIONS.md § Backup and restore](OPERATIONS.md#5-backup-and-restore-mysql) and test restores regularly.

### 5.4 Secrets and configuration

Required settings (see [`/.env.example`](../.env.example) for all of them):

| Setting | Secret | Notes |
|---|---|---|
| `ConnectionStrings__Default` | yes | MySQL connection string incl. password, `SslMode=Required` |
| `Jwt__SigningKey` | yes | >= 32 random bytes; rotating it signs everyone out |
| `Security__HashSalt` | yes | HMAC key for IP/device hashes; keep stable |
| `Tracking__PostbackSecret` | yes | shared with advertisers for signed conversion postbacks |
| `Email__SmtpHost/Port/Username/Password`, `Email__FromAddress` | password | Production refuses to start email without `Email__Mode=Smtp` |
| `Email__AppBaseUrl` | no | public HTTPS URL of the web app (links in emails) |
| `Tracking__PublicBaseUrl` | no | base of `/t/{code}` links (defaults to `Email__AppBaseUrl`) |
| `Security__SecureCookies=true`, `Swagger__Enabled=false`, `DevTools__MailboxEnabled=false` | no | production values (the defaults in `appsettings.json`) |
| `Database__Seed__0=Baseline` (no `Demo`) | no | never load demo data in production |
| `Bootstrap__AdminEmail` / `Bootstrap__AdminPassword` | password | first start only; remove afterwards |
| `WhatsApp__*` | token | only when WhatsApp is enabled |

* Generate values with `scripts/generate-secrets.sh --aspnet`. Store them in a secret manager (AWS Secrets
  Manager/SSM, GCP Secret Manager, Azure Key Vault, Vault, Kubernetes Secrets with encryption at rest).
* The API image supports `*_FILE` variables: `Jwt__SigningKey_FILE=/run/secrets/jwt_signing_key` exports
  `Jwt__SigningKey` from the file at startup. With plain Docker Compose, secret files are bind-mounted with
  their host ownership: make them readable by the container user
  (`chown 1654:1654 secrets/*; chmod 400 secrets/*` inside a `chmod 700` directory).
* Never put secrets in images, `appsettings*.json`, compose files or git. `.env*` files are git-ignored.

### 5.5 Migrations strategy

Two supported modes:

1. **Startup migration** (staging, small installs): `Database__InitializationMode=Migrate`. Every API instance
   applies pending migrations before it starts listening. EF Core 8 takes **no** migration lock, so several
   instances starting together could race on the same migration: use this mode only with a single instance
   (or start one instance first), otherwise use option 2.
2. **Migration job** (recommended for production): run the `optimizeall-migrator` image once per release,
   **before** rolling out the new API version, and start the API with `Database__InitializationMode=None`
   (it still applies the idempotent Baseline seed and bootstrap admin).

   ```bash
   docker run --rm -e ConnectionStrings__Default="$MIGRATOR_CONNECTION_STRING" \
     registry.example.com/optimizeall-migrator:$TAG
   ```

   The bundle is idempotent ("No migrations were applied" when up to date). In Kubernetes run it as a `Job`
   (or Helm pre-upgrade hook); in the production compose example it is the `migrate` service that `api`
   waits for. To inspect the SQL instead, generate an idempotent script in CI:
   `dotnet ef migrations script --idempotent -p src/OptimizeAll.Infrastructure -s src/OptimizeAll.Api`.

### 5.6 Zero-downtime deploys

* Make schema changes **expand/contract**: a release may only add nullable columns/tables/indexes that the
  previous version tolerates; drop or rename in a later release once no running version uses the old shape.
  Large index builds on big tables should use online DDL (`ALGORITHM=INPLACE, LOCK=NONE`) or be scheduled.
* Order: run migrations → roll API instances one at a time (readiness `/health/ready` gates traffic) →
  update the web image (static assets are content-hashed and `index.html` is `no-cache`, so users pick up the
  new SPA on the next navigation).
* Draining: ASP.NET Core finishes in-flight requests on SIGTERM (default 30 s shutdown timeout; set the
  orchestrator's grace period >= 30 s). Background jobs are idempotent and lease-based, so an interrupted run
  is retried by another instance after the lease expires.
* Refresh tokens and data-protection keys are in the database, so users stay signed in across deploys.

### 5.7 Horizontal scaling

* Run 2+ API instances behind the web tier (or give each web instance an `API_UPSTREAM` load-balanced service).
* Background jobs: safe on every instance (DB lease per job; one instance runs a job at a time). To isolate
  job load, run a dedicated instance with `Jobs__Enabled=true` and set `Jobs__Enabled=false` elsewhere.
* Data-protection keys: persisted in MySQL (`data_protection_keys`), shared automatically.
* **File storage must be shared**: `Storage__RootPath` (`/app/storage/files` in the image) has to be the same
  shared filesystem on every instance (NFS, EFS, Azure Files, GCS Fuse), or be replaced by an object-storage
  implementation of `IFileStorage`. A local volume per instance breaks screenshot access.
* Rate limits are per instance (in-memory); effective limits scale with the instance count.
* Health checks: liveness `/health/live` (process only), readiness `/health/ready` (database reachable).

### 5.8 Rollback

1. **Application only** (no schema change or backwards-compatible change): redeploy the previous
   `optimizeall-api` and `optimizeall-web` tags. This is always the first option.
2. **Schema rollback** (only when the new schema breaks the old version):
   * **Take a backup / snapshot first** and confirm it completed.
   * Review the target migration's `Down()` in
     `backend/src/OptimizeAll.Infrastructure/Persistence/Migrations`. Down migrations can drop columns/tables
     and **lose data written since the deploy** — financial tables (`earning_entries`, `payout_*`,
     `audit_logs`) must never lose rows; prefer a forward fix in that case.
   * Run the **previous release's** bundle (it knows the target) or the current one with an explicit target:

     ```bash
     docker run --rm -e ConnectionStrings__Default="$MIGRATOR_CONNECTION_STRING" \
       registry.example.com/optimizeall-migrator:$NEW_TAG <PreviousMigrationName>
     ```
     (`dotnet ef migrations list` shows names; `0` would revert everything — never do that in production.)
   * Redeploy the previous API/web images and run the smoke checks below.
3. If data was corrupted, restore from backup / PITR into a new instance, verify, then switch the connection
   string (see OPERATIONS.md).

### 5.9 Smoke checks after every deploy

```bash
BASE=https://app.example.com
curl -fsS $BASE/health/live               # "Healthy"
curl -fsS $BASE/health/ready              # "Healthy" (database reachable)
curl -fsSI $BASE/ | grep -i content-security-policy
curl -fsS -o /dev/null -w '%{http_code}\n' $BASE/api/docs/v1/openapi.json   # 404 in production (Swagger off)
```

Then, with a dedicated smoke-test participant and staff account:

1. Sign in (participant) → dashboard loads; refresh the page → still signed in (refresh cookie works).
2. Browse available campaigns; open one; the tracking link `/t/{code}` redirects.
3. Submit a test post with a screenshot on a test campaign → it appears in the reviewer queue.
4. Reviewer approves it → participant sees the earning as pending/approved; a notification email arrives.
5. Finance: payout batches list loads; no unexpected failed items.
6. Admin → Jobs: every job has a recent successful run; no new failed notification deliveries.
7. Password-reset email arrives through the real SMTP provider.

### 5.10 Features that depend on external credentials

| Feature | Requires | Without it |
|---|---|---|
| Transactional email (verification, reset, notifications) | SMTP provider (SES, SendGrid, Mailgun, Postmark...) with SPF/DKIM/DMARC on the sender domain | Production does not start without `Email__Mode=Smtp`; deliveries fail and are retried |
| WhatsApp notifications | WhatsApp Business Cloud API: `WhatsApp__PhoneNumberId`, `WhatsApp__AccessToken` (system-user token), approved template `WhatsApp__TemplateName` | `WhatsApp__Enabled=false`: WhatsApp deliveries are skipped; in-app + email still work |
| Payouts | `Payments__Provider=manual` (default): finance pays outside the platform and records payment references | No automated money movement until a provider integration (e.g. Wise, PayPal Payouts) is added |
| Advertiser conversion postbacks | `Tracking__PostbackSecret` shared with each advertiser | Postbacks are rejected; clicks are still tracked |
