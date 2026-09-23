# Optimize All

*Discover the world of solution.*

Optimize All is a full-service digital marketing agency platform: the agency's public website and CMS, sales and
billing, client delivery with a client portal, and the tools to run every marketing service it sells — email and
SMS/WhatsApp marketing, social media management, paid advertising, SEO, landing pages and forms, and paid social sharing
(influencer/advocacy) campaigns. Each team has its own portal: agency staff, clients, participants, reviewers, campaign
managers, finance and administrators.

| | |
|---|---|
| Frontend | React 18 + TypeScript (Vite), bespoke accessible design system, TanStack Query — `frontend/` |
| Backend | ASP.NET Core 8 Web API (C#), EF Core 8 + Pomelo — `backend/` |
| Database | MySQL 8 (utf8mb4) **or** SQLite (`Database__Provider`), EF Core migrations for each provider |
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

Integration tests boot the real API against a freshly migrated, uniquely named database per test class (MySQL, or SQLite
with `OPTIMIZEALL_TEST_PROVIDER=Sqlite`), with a
controllable clock for cutoffs, holds and expiries.

## What is built

### Agency services
| Service line | What the platform does | Docs |
|---|---|---|
| **Agency website & CMS** | Public marketing site (home, services, industries, pricing, case studies, testimonials, team, blog with RSS, careers with applications, search, CMS pages), SEO metadata, structured data, sitemap/robots, contact / free-audit / quote forms, consultation booking, newsletter with double opt-in | [WEBSITE.md](docs/WEBSITE.md) |
| **CRM, proposals & billing** | Contacts, companies, deal pipeline with stages and lead scoring, proposals with tokenized accept/decline pages, contracts, invoices with tax rates, partial payments, credit notes, four-eyes voids/write-offs, aging reports, tokenized invoice pages | [SALES_AND_BILLING.md](docs/SALES_AND_BILLING.md) |
| **Client delivery** | Client accounts and teams, brand kits, SLAs, projects, tasks, deliverables with versioned client approvals, time tracking and timesheets, utilization, client reports, agency home dashboard, **client portal** | [CLIENT_DELIVERY.md](docs/CLIENT_DELIVERY.md) |
| **Email & SMS/WhatsApp marketing** | Lists, subscribers, consent records, suppressions, segments, block-based templates, campaigns with A/B variants, client approval, scheduled sends, journeys/automations, open/click tracking, one-click unsubscribe, preference center, provider webhooks (SendGrid, Mailgun, Twilio) | [EMAIL_SMS.md](docs/EMAIL_SMS.md) |
| **Social media management** | Brand profiles, content calendar, composer with per-network validation, client approvals, queue and evergreen publishing, media library, hashtag sets, listening, unified inbox, competitor tracking, analytics | [SOCIAL_ADS.md](docs/SOCIAL_ADS.md) |
| **Paid advertising** | Ad accounts (Google Ads and Meta adapters, CSV import for others), campaigns/ad groups/ads, budget pacing and alerts, media plans vs actuals, creatives with approval, naming conventions, UTM builder, experiments | [SOCIAL_ADS.md](docs/SOCIAL_ADS.md) |
| **SEO** | Site audits by a safe crawler (SSRF-protected), on-page analyzer, keyword rank tracking, Search Console import, backlinks and outreach, local SEO profiles/citations/reviews, content briefs | [SEO_CRO.md](docs/SEO_CRO.md) |
| **Landing pages, forms & CRO** | Block-based page builder with templates, versions, A/B tests, analytics; multi-step forms with conditional logic, consent versions, file uploads, embeds; integrations hub with write-only encrypted secrets | [SEO_CRO.md](docs/SEO_CRO.md) |
| **Paid social sharing campaigns** | The participant/reviewer/payout platform described below | [REWARD_ENGINE.md](docs/REWARD_ENGINE.md), [PAYOUTS.md](docs/PAYOUTS.md) |

Agency roles: Admin, Account manager, Strategist, Content creator, Designer, SEO specialist, Ads specialist, Social
media manager, Sales rep, Finance; client users have per-client duties (Viewer, Approver, Billing, Owner). Every client
record is tenant-scoped: staff see the clients they work on, client users see only their own organization.


### Paid social sharing campaigns — stage 1: reliable core (fully working)
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

### Paid social sharing campaigns — stage 2: growth (fully working)
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
| Marketing email at scale | Campaigns send through SMTP; SendGrid and Mailgun adapters with signed delivery/bounce webhooks | ESP account, verified sending domain (SPF/DKIM/DMARC) — see [EMAIL_SMS.md](docs/EMAIL_SMS.md) |
| SMS / WhatsApp campaigns | Twilio adapter with inbound (STOP) and status webhooks | Twilio account and number (or WhatsApp sender) |
| Social publishing | Facebook, Instagram and X adapters with OAuth; other networks use the manual *mark as published* flow | A developer app per network (client id/secret) and app review where the network requires it |
| Ads data | Google Ads and Meta adapters; every platform supports CSV import | API access (developer token / app) per platform |
| Rank tracking, Search Console | Manual and CSV rank entry, DataForSEO adapter, Search Console import | DataForSEO credentials; Google OAuth app for Search Console |

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
| [RENDER.md](docs/RENDER.md) | Deploying the demo environment to Render (SQLite or MySQL Blueprint) |
| [WEBSITE.md](docs/WEBSITE.md) · [SALES_AND_BILLING.md](docs/SALES_AND_BILLING.md) · [CLIENT_DELIVERY.md](docs/CLIENT_DELIVERY.md) | Agency website/CMS, CRM and billing, client delivery and portal |
| [EMAIL_SMS.md](docs/EMAIL_SMS.md) · [SOCIAL_ADS.md](docs/SOCIAL_ADS.md) · [SEO_CRO.md](docs/SEO_CRO.md) | Marketing service lines: deliverability and compliance, social and ads, SEO and conversion |
| [api/](docs/api) | Endpoint reference per module + OpenAPI document |
