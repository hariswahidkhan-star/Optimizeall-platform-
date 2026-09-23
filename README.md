# Optimize All

*Discover the world of solution.*

Optimize All is a platform for paid social media sharing campaigns. Participants share company-approved content from their
established social accounts, submit proof, and are paid for qualifying posts after human review. Reviewers, campaign
managers, finance and administrators each have their own portal.

| | |
|---|---|
| Frontend | React 18 + TypeScript (Vite), bespoke accessible design system, TanStack Query — `frontend/` |
| Backend | ASP.NET Core 8 Web API (C#), EF Core 8 + Pomelo — `backend/` |
| Database | MySQL 8 (utf8mb4), EF Core migrations |
| Delivery | Docker images, Docker Compose staging stack (MySQL + Mailpit + API + nginx web), GitHub Actions CI |

## Quick start (local)

Prerequisites: .NET 8 SDK, Node 22, MySQL 8.

```bash
scripts/dev-setup.sh      # checks prerequisites, creates the local DB/user, restores packages
scripts/dev-start.sh      # API on http://localhost:5080 (Development: migrations + Baseline + Demo seed), web on http://localhost:5173
scripts/dev-stop.sh
scripts/dev-start.sh --sqlite   # same on SQLite (no MySQL needed)
```

* API docs: http://localhost:5080/api/docs (Swagger UI) — static copy in [`docs/api/openapi.json`](docs/api/openapi.json)
  and per-module references in [`docs/api/`](docs/api).
* Development email is written to files; `GET /api/v1/dev/mailbox?to=<email>` returns the latest message (dev/staging only).
* Demo accounts and step-by-step demo scripts: [`docs/DEMO.md`](docs/DEMO.md) (password `Demo#2026!pass`, demo data only).

Staging (full stack with real SMTP to Mailpit): `scripts/staging-up.sh` — see [`docs/DEPLOYMENT.md`](docs/DEPLOYMENT.md).

The API runs on MySQL 8 or SQLite (`Database__Provider`). Hosted demo on Render: `render.yaml` is a one-click
Blueprint (web + API on SQLite, the cheapest option); `deploy/render/render-mysql.yaml` adds a MySQL service — see
[`docs/RENDER.md`](docs/RENDER.md).

## Tests

```bash
scripts/test-all.sh            # backend build + unit + integration (real MySQL), frontend typecheck/lint/test/build
scripts/test-all.sh --e2e      # + Playwright smoke suite
scripts/test-all.sh --sqlite   # integration tests on SQLite instead of MySQL
scripts/e2e-journeys.sh        # full-stack Playwright journeys (participant, reviewer, finance, admin) on a fresh DB
```

Integration tests boot the real API against a freshly migrated, uniquely named MySQL database per test class, with a
controllable clock for cutoffs, holds and expiries.

## What is built

### Stage 1 — reliable core (fully working)
* **Accounts**: registration, email verification, secure sign-in (short-lived JWT + rotating HttpOnly refresh cookie with
  reuse detection), lockout, password reset, profiles, encrypted payout details (only a masked hint is ever shown).
* **Social profiles & eligibility**: per-profile qualification with reasons; newly created accounts are ineligible until
  the configurable minimum age (`eligibility.minAccountAgeDays`, default 90); campaign-level overrides; staff verification.
* **Campaigns**: categories, approved assets, posting instructions, platform/country-specific disclosure wording,
  targeting, budgets, landing content, scheduling, publish lifecycle, full audit.
* **Dynamic rewards**: versioned reward rule sets per campaign (flat base rate; overrides by platform/country/tier;
  time-limited, first-post and quality bonuses with separate approval modes; daily/weekly/campaign caps; budgets;
  multi-currency). Submissions capture the rule version; approved earnings are immutable ledger entries at that rate.
* **Submissions & verification**: post URL + platform + account + posting date + private screenshot; canonical per-platform
  post keys (a post can be claimed once), reused-screenshot and repeated-content detection, date-window and velocity
  flags, configurable post-live duration with live checks. A screenshot is review evidence, never proof.
* **Review**: claim-based queue, side-by-side workspace, approve / request correction / reject (reason required),
  conditional updates so two reviewers can never both decide, no self-review, appeals resolved by a different reviewer,
  reversals with ledger clawback.
* **Ledger & payouts**: earnings buckets (pending, approved, on hold, scheduled, paid, reversed), adjustments with
  second-person approval, exchange rates stored per entry, biweekly schedule by default (configurable, time-zone and DST
  aware), payout batches with review screen, participant breakdown, holds, minimum threshold carry-over, CSV export,
  payment-instruction export (audited), manual payment recording with references, reconciliation, and
  segregation of duties. Double payment is prevented by unique keys, conditional updates and locks.
* **Participant portal** (dynamic homepage driven by account state), **reviewer**, **finance** and **admin** portals;
  admin-managed banners, announcements, FAQs, onboarding steps, categories and settings; support tickets; audit log;
  in-app notification center with email delivery.

### Stage 2 — growth (fully working)
Referral links with fraud signals and rewards only after the qualifying action; invitation links and campaign landing
pages; segmentation by language, location, platform, interests, tier and verified attributes; post templates and content
calendar; tracking links with UTM parameters, click measurement (bots excluded) and signed conversion postbacks; A/B
experiments on titles, creative, instructions and landing pages with sticky assignment and honest significance testing;
achievements, onboarding reminders, campaign alerts and reactivation messages; performance dashboards that keep
**counted**, **measured** and **estimated** figures separate.

### Depends on external credentials or providers
| Capability | Status | What is needed |
|---|---|---|
| Email delivery | Working via any SMTP server (Mailpit in staging) | Production SMTP provider credentials (`Email__Smtp*`) |
| WhatsApp notifications | Adapter implemented (WhatsApp Business Cloud API); deliveries are recorded as *Skipped — credentials not configured* | WhatsApp Business account, phone number id, access token, approved template |
| Automatic payouts | **Not sending money.** Batches are paid manually and references recorded; the provider layer (`IPaymentProvider`) is ready | A payment provider account + credentials and an adapter (see [`docs/PAYOUTS.md`](docs/PAYOUTS.md)) |
| Verified conversions | Postback endpoint with HMAC signature implemented | Advertiser integration + `Tracking__PostbackSecret` |

## Documentation

| Doc | Contents |
|---|---|
| [ARCHITECTURE.md](docs/ARCHITECTURE.md) | Modules, conventions, data rules |
| [REWARD_ENGINE.md](docs/REWARD_ENGINE.md) | Reward rules, caps, versioning, posting-time rules |
| [PAYOUTS.md](docs/PAYOUTS.md) | Earnings lifecycle, periods, batches, double-payment protection, segregation of duties, finance runbook |
| [GROWTH.md](docs/GROWTH.md) | Referrals, tracking/UTM & postbacks, experiments, retention, metric definitions |
| [SECURITY.md](docs/SECURITY.md) | Authentication, RBAC matrix, uploads, encryption, audit, financial controls |
| [DEPLOYMENT.md](docs/DEPLOYMENT.md) / [OPERATIONS.md](docs/OPERATIONS.md) | Staging/production deployment, migrations, rollback, jobs, runbooks |
| [FRONTEND.md](docs/FRONTEND.md) | Frontend structure, design tokens, components, testing |
| [DEMO.md](docs/DEMO.md) | Demo accounts and scripted walkthroughs |
| [RENDER.md](docs/RENDER.md) | Deploying the demo environment to Render with the Blueprint |
| [api/](docs/api) | Endpoint reference per module + OpenAPI document |
