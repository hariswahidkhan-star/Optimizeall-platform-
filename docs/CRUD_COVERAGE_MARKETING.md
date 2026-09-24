# CRUD coverage — marketing services

Scope: agency Email & SMS, Social, Ads, SEO, Landing pages & forms, Integrations, and the client-portal email/social/SEO pages.
Every row lists what exists end to end (API **and** UI). Legend:

- **C / R / U / D** — create, read (list + detail), update, delete. `A` = archive/restore instead of delete (history kept), `H` = hide/show (built-in catalog rows the seeder owns).
- **Status / done** — state transitions and the "mark complete" action.
- **Before → after** — ✗ missing, ◐ API only (no UI) or UI only, ✓ complete. Items marked *new* were added in this change.
- Conventions for every write: `[HasPermission]` on the controller/action, tenant rows loaded through `IClientScope` (other tenants answer 404), `IAuditLogger` records, optimistic concurrency with `concurrencyStamp` → `409 concurrency.conflict` with a friendly message, destructive actions behind a `ConfirmDialog`, and disabled menu items carry the reason (`description`).

Agency-wide catalogs (presets, awareness days, audit rules, directories, page/form templates) are readable with the module permission and writable only with `settings.manage` as well. Their seeders are **insert-only** (or skip rows the agency edited), so edits survive restarts; "reset" restores the built-in copy.

## Email & SMS (`/agency/email`, `/agency/sms`)

| Entity | C | R | U | D / A | Status / done | Before → after |
|---|---|---|---|---|---|---|
| Lists | ✓ | ✓ | ✓ | A (archive) + **restore** *new* | — | U/A ◐ (no UI to edit from the list, archive, list archived or restore) → ✓ (row actions, "Show archived", `POST lists/{id}/restore`) |
| Subscribers / contacts | ✓ | ✓ | ✓ | erase (GDPR) | consent per channel, list membership | ✓ → ✓ |
| Tags (workspace) | via contact | **list with usage** *new* | **rename / merge** *new* | **remove from all contacts** *new* | — | ✗ → ✓ (`GET/POST tags/rename/DELETE tags`, Tags & fields page, typed confirmation) |
| Custom fields (workspace) | via contact/import | **list** *new* | **rename / merge** *new* | **remove** *new* | — | ✗ → ✓ (`fields`, same page) |
| Consent records | ✓ (change consent) | ✓ | append-only (proof) | never | — | ✓ (immutable by design) |
| Suppressions | ✓ | ✓ | — (delete + re-add) | ✓ (with reason) | bounce import | ✓ → ✓ |
| Segments | ✓ | ✓ | ✓ | ✓ | preview/count | ✓ → ✓ |
| Templates (incl. agency starter templates, seeded once as DB rows) | ✓ | ✓ + **archived** *new* | ✓ | A + **restore** *new* | duplicate / copy to workspace | archive/restore ◐ → ✓ (`GET templates/all`, `POST templates/{id}/restore`, row actions) |
| Email campaigns + A/B variants | ✓ | ✓ | ✓ (drafts; sent campaigns immutable) | ✓ (unsent) | schedule, unschedule, pause, resume, cancel, approval | ✓ → ✓ |
| SMS / WhatsApp campaigns | ✓ | ✓ | ✓ | ✓ | same as email | ✓ → ✓ |
| Journeys (automations) + steps | ✓ | ✓ + **archived** *new* | ✓ | **delete (never-entered drafts)** *new*, A + **restore (as paused)** *new* | activate/pause/archive, manual enroll, **duplicate** *new* | ◐ → ✓ (`GET automations/all`, `POST {id}/restore`, `POST {id}/duplicate`, `DELETE {id}` → 409 when active or with enrollments) |
| Senders | ✓ | ✓ | ✓ | ✓ | send / confirm verification | ✓ → ✓ |
| Workspace settings / provider | — | ✓ | ✓ | — | — | ✓ → ✓ |

## Social (`/agency/social`)

| Entity | C | R | U | D / A | Status / done | Before → after |
|---|---|---|---|---|---|---|
| Brand profiles | ✓ | ✓ + archived | ✓ **(UI new)** | A + **restore** *new* | connect (OAuth/token), disconnect | U/A ◐ → ✓ (edit dialog, archive confirm, "Show archived", `POST profiles/{id}/restore`) |
| Queue slots | via profile | ✓ | ✓ | ✓ (replace) | — | ✓ |
| Client settings | — | ✓ | ✓ (+ **default utm_medium in UI** *new*) | — | — | ◐ → ✓ |
| UTM campaigns | ✓ | ✓ + archived | ✓ **(UI new)** | **archive / restore** *new*, **delete when unused** *new* | archived campaigns cannot be picked for new posts (`social.campaign_archived`) | U ◐, D/A ✗ → ✓ |
| Posts + variants | ✓ | ✓ | ✓ (published locked; edits after review reset to Draft) | ✓ (not published) | submit, approve, request changes, schedule, queue, reschedule, mark published, retry, **duplicate** *new* | ✓ → ✓ |
| Post comments / feedback | ✓ | ✓ | — | **delete own note** *new* | **mark done / reopen** *new* (`IsResolved`) | ✗ → ✓ |
| Media library | ✓ (upload/URL) | ✓ | ✓ **(UI new)** | ✓ (confirm *new*) | — | U ◐ → ✓ |
| Hashtag sets | ✓ | ✓ | ✓ **(UI new)** | ✓ (confirm *new*) | — | U ◐ → ✓ |
| Caption snippets | ✓ | ✓ | ✓ **(UI new)** | ✓ (confirm *new*) | — | U ◐ → ✓ |
| Listening queries | ✓ | ✓ | **edit / pause / resume** *new* | ✓ **(UI new)** | active flag | U ✗, D ◐ → ✓ |
| Mentions | ✓ (log) | ✓ | sentiment tag | **delete** *new* | — | D ✗ → ✓ |
| Inbox items + replies | ✓ (log) | ✓ | assign, sentiment | — (closed items are kept) | open → assigned → replied → **closed / reopen** | ✓ |
| Competitors + snapshots | ✓ | ✓ | **edit** *new*, snapshot upsert | ✓ **(UI new)**, **snapshot delete** *new* | — | U ✗ → ✓ |
| Network presets (agency) | seeded | ✓ | **edit** *new* (limits, best times, source) | **reset** *new* | — | hardcoded-in-DB, no API → ✓ (`admin/presets`, Social settings page) |
| Awareness days (agency) | **create** *new* | ✓ | **edit** *new* | **delete (agency) / hide (built-in)** *new* | show/hide | ✗ → ✓ (`admin/awareness-days`; seeder keyed by `SeedKey`, never re-adds) |
| Metric imports | ✓ | ✓ | — | — (re-import upserts) | — | ✓ |

## Ads (`/agency/ads`)

| Entity | C | R | U | D / A | Status / done | Before → after |
|---|---|---|---|---|---|---|
| Ad accounts | ✓ | ✓ | ✓ **(UI new)** incl. active flag | **delete (no campaigns/metrics/imports)** *new*, otherwise deactivate | sync | U ◐, D ✗ → ✓ |
| Campaigns | ✓ **(UI new)** | ✓ | ✓ **(UI new)** | **delete planned w/o data** *new*; synced → status Removed | Draft/Active/Paused/Ended/Removed | C/U ◐, D ✗ → ✓ |
| Ad groups | ✓ **(UI new)** | ✓ **(UI new)** | **update** *new* | **delete (planned)** *new* | status | ◐ → ✓ (structure dialog) |
| Ads | ✓ **(UI new)** | ✓ **(UI new)** | **update** *new* (creative link) | **delete (planned)** *new* | status | ◐ → ✓ |
| Budgets | ✓ | ✓ | ✓ **(UI new)** | ✓ **(UI new)** | pacing | U/D ◐ → ✓ |
| Alerts | system | ✓ | — | — | acknowledge, resolve (**done**), **reopen** *new* | ◐ → ✓ |
| Media plans + lines | ✓ | ✓ + **GET by id** *new* | ✓ **(UI new)** | **delete (draft/archived)** *new* | draft → approved → archived **(UI new)**, **copy to next month** *new* | U ◐, D ✗ → ✓ |
| Creatives | ✓ | ✓ | ✓ | **delete (unused)** *new* | submit/approve/request changes/record client approval, **duplicate** *new* | D ✗ → ✓ |
| Experiments + variants | ✓ | ✓ | ✓ **(UI new: update results, conclude, winner)** | **delete (not running)** *new* | planned → running → concluded | U ◐, D ✗ → ✓ |
| UTM links | ✓ | ✓ | — (rebuild) | **delete** *new* | — | D ✗ → ✓ |
| Client settings: naming template, UTM defaults | — | ✓ | ✓ (**utm_source/medium defaults in UI** *new*) | — | naming check/generate | ◐ → ✓ |

## SEO (`/agency/seo`)

| Entity | C | R | U | D / A | Status / done | Before → after |
|---|---|---|---|---|---|---|
| Sites | ✓ | ✓ | ✓ | A + restore | — | ✓ |
| Audits | ✓ (queue) | ✓ | — (immutable results) | **delete finished** *new* | cancel queued **(UI new)** | D ✗ → ✓ |
| Audit issues | engine | ✓ | — | — | **fixed / ignored (note required) / reopen** *new*; ignored carries over to the next audit | ✗ → ✓ |
| Audit rules (agency) | catalog | **list** *new* | **title, copy, severity, enabled** *new* | **reset** *new* | disabled rules skipped, severity overrides used by new audits | DB copy without API → ✓ (SEO settings page) |
| Keywords | ✓ | ✓ | ✓ **(UI new)** | ✓ **(UI new)** | pause/resume tracking **(UI new)** | U/D ◐ → ✓ |
| Rank snapshots | ✓ (manual/CSV/provider) | ✓ | upsert | — | — | ✓ |
| Backlinks | ✓ | ✓ | **edit** *new* (URL change resets the check) | ✓ (confirm *new*) | checker | U ✗ → ✓ |
| Outreach prospects | ✓ | ✓ | ✓ | ✓ **(UI new)** | **mark won / lost** (done) **(UI new)**; audited *new* | D ◐ → ✓ |
| Local profile + GBP checklist | upsert | ✓ | ✓ | — | checklist items done | ✓ |
| Citation sources (agency) | **create** *new* | **list** *new* | **edit** *new* | **delete (agency, unused) / hide (built-in)** *new* | — | ✗ → ✓ |
| Citations | upsert | ✓ | ✓ | — | not started → submitted → live / needs update / rejected | ✓ |
| Reviews | ✓ | ✓ | ✓ **(UI new: reply/edit)** | ✓ **(UI new)** | responded | U/D ◐ → ✓ |
| Content briefs | ✓ | ✓ | ✓ | ✓ **(UI new)** | draft → ready → handed off → **published (done)** *new*, reopen, **duplicate** *new* | ◐ → ✓ |

## Landing pages & forms (`/agency/pages`)

| Entity | C | R | U | D / A | Status / done | Before → after |
|---|---|---|---|---|---|---|
| Landing pages | ✓ | ✓ | ✓ (draft; published versions immutable → publish a new version) | A + restore **(restore in list UI new)** | publish/unpublish, A/B, **duplicate** *new* | ✓ → ✓ |
| Page versions | publish | ✓ | never (immutable) | never | restore a version into the draft | ✓ |
| Page templates (agency) | **save page as template** *new* | ✓ + **library** *new* | **edit details** *new* | **delete (agency) / hide (built-in)** *new* | **reset** *new*; seeder skips edited/custom rows | read-only catalog → ✓ |
| Forms | ✓ | ✓ | ✓ | A + **restore** *new* | draft/active, **duplicate** *new* | ◐ → ✓ |
| Form templates (agency) | **save form as template** *new* | ✓ + **library** *new* | **edit texts** *new* (schema validated) | **delete / hide** *new* | **reset** *new* | read-only → ✓ |
| Submissions | public form | ✓ (+ **status filter**, spam hidden) *new* | **status + internal note** *new* | **delete / bulk delete** *new* (files removed) | **new → in progress → done / spam**, **bulk mark done/spam** *new* | ✗ → ✓ |

## Integrations (`/agency/integrations`)

| Entity | C | R | U | D / A | Status / done | Before → after |
|---|---|---|---|---|---|---|
| Connections (agency-wide / per client) | ✓ | ✓ | ✓ (secrets write-only) | ✓ + disconnect | test / verify | ✓ → ✓ (no gaps) |

## Client portal (`/client/email`, `/client/social`, `/client/seo`)

Clients read their KPIs, approve/request changes on email campaigns and social posts (Approver/Owner duty) and comment on posts. The resolved state of post feedback is included in the post DTO; no client-side edit/delete was added on purpose (the agency owns the content).

## Deliberately not editable

- Sent email/SMS campaigns, recipients, engagement events, consent records, published landing-page versions and audit results are records of what happened: they are immutable, and the UI explains why actions are unavailable.
- Synced/imported ad entities with platform data cannot be deleted (set status Removed); ad accounts with data are deactivated instead of deleted.
- Built-in catalog rows (presets, awareness days, rules, directories, templates) are hidden or reset, never deleted, so the insert-only seeders do not bring them back.
