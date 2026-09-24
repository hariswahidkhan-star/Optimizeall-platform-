# Optimize All — Database performance, indexes and retention

This document describes how the backend tables are indexed and housekept, which query each performance-critical index
serves, and the measured effect (MySQL 8 `EXPLAIN ANALYZE` before/after on a Demo database plus synthetic volume).
The portability rules in [ARCHITECTURE.md § Database portability](ARCHITECTURE.md#database-portability-mysql-and-sqlite)
apply to everything here: every index and query below works on MySQL and SQLite, there is no raw SQL in modules.

Contents: [Conventions](#conventions) · [Index catalogue](#index-catalogue) · [Query fixes](#query-fixes) ·
[Benchmark](#benchmark-before--after) · [Retention](#retention) · [Column hygiene review](#column-hygiene-review) ·
[Recommendations for larger scale](#recommendations-for-larger-scale) · [Table inventory](#table-inventory)

## Conventions

* **Index = query.** Add an index only for a query that exists (list, dashboard, job, public endpoint), with equality
  columns first and the range/sort column last, e.g. `(ClientAccountId, SubmittedAt)` for "this client's leads in a date
  range, newest first". InnoDB secondary indexes carry the primary key, so `(X)` already sorts by `(X, Id)`.
* **No redundant indexes.** A non-unique index that is a leading prefix of another index on the same table only costs
  writes; extend the existing index instead. EF Core drops its conventional foreign-key index when another index starts
  with the FK column(s).
* **Guarded.** `tests/OptimizeAll.UnitTests/Foundation/PerformanceIndexTests.cs` fails when a performance-critical
  index below disappears or changes column order (both providers), when an index is a redundant prefix of another, or
  when a foreign key has no covering index. Changing one of these indexes means updating that test and this page.
* **Counts.** `PagedResult.Total` is an exact `COUNT(*)`. Count the filtered base table, not the joined projection:
  left joins to users/notifications on their primary key never change the row count but cost two lookups per row
  (see *Query fixes*). Time-series tables are bounded by retention so their counts stay cheap.
* **Ids are UUIDv7** (`IdGenerator`), so primary keys are insertion-ordered; `IdGenerator.LowerBound(t)` turns "created
  before t" into a primary-key range, which housekeeping uses instead of adding a time index to write-heavy tables.
* **Money** is `DECIMAL(19,4)`, exchange rates `DECIMAL(18,8)`; every enum is stored as its name (`varchar(40)`);
  every `DateTime` is UTC `datetime(6)` (converter in `AppDbContext`); e-mail columns are `varchar(254)` (the SMTP
  path limit, RFC 5321).

## Index catalogue

Indexes added or changed by the September 2026 review, with the query each one serves (✚ added, ↻ replaced an index
that was its prefix, so the index count did not grow):

| Table | Index | Serves |
|---|---|---|
| `job_runs` | ✚ `(JobName, StartedAt)` | Admin job list: last run of each of the 29 jobs (was a filesort of every run of the job); run log filtered by job |
| `job_runs` | ✚ `(StartedAt)` | Unfiltered run log, newest first; `DataRetentionJob` (oldest first) |
| `notifications` | ✚ `(UserId, CreatedAt)` | Notification list newest first (was a filesort of all of the user's notifications) |
| `notifications` | ✚ `(Type, CreatedAt)` | `LiveCheckReminderJob` "already reminded today" and the website-inquiry dedup check (both were full scans) |
| `submission_events` | ✚ `(ActorUserId, CreatedAt)` | Review stats / reviewer list: decisions per reviewer today (full scan before) |
| `submissions` | ✚ `(UserId, SubmittedAt)` | Participant's submissions newest first; 24-hour velocity risk signal on every new submission |
| `submissions` | ✚ `(ClaimedByUserId, ClaimExpiresAt)` | Review stats and "claimed by me" filter (full scan before) |
| `submissions` | ✚ `(SubmittedAt)`, ✚ `(Status, DecidedAt)` | Analytics: posts submitted / approvals decided in a date range |
| `submissions` | ↻ `(LiveCheckStatus, Status, LiveCheckDueAt)` (was `(LiveCheckStatus, LiveCheckDueAt)`) | Live-check list, due count and reminder job; all filter `Status = Approved`, which the optimizer otherwise answered from `(Status, SubmittedAt)` |
| `email_campaign_recipients` | ✚ `(CampaignId, SentAt)` | Send throttle "sent in the last minute" (every 30 s per sending campaign; was a scan of every recipient of the campaign); KPI ranges by send time |
| `email_subscribers` | ✚ `(ScopeKey, CreatedAt)` | Subscriber list, default sort newest first |
| `form_submissions` | ✚ `(IpHash, SubmittedAt)` | Per-IP rate limit on every public form post (full scan before) |
| `form_submissions` | ↻ `(ClientAccountId, SubmittedAt, LandingPageId)` (was `(ClientAccountId)`) | Client lead/SEO KPI reports (covering) |
| `landing_page_views` | ↻ `(PageId, VisitorHash, ViewedAt)` (was `(PageId, VisitorHash)`) | Unique-visitor check on every public page view |
| `landing_page_views` | ↻ `(ClientAccountId, ViewedAt, PageId)` (was `(ClientAccountId)`) | Client lead/SEO KPI reports (covering) |
| `tracking_clicks` | ✚ `(ClickedAt)` | Tracking/analytics summaries over a date range across links; tracking-event retention |
| `time_entries` | ✚ `(Date)` | Admin dashboard week totals, utilization report (full scans before) |
| `thread_messages` | ✚ `(ClientAccountId, CreatedAt)` | Client health "last activity" (full scan before) |

Pre-existing indexes the review confirmed as correct and now guards (selection): `audit_logs (EntityType, EntityId)`,
`(ActorUserId)`, `(CreatedAt)`; `notifications (UserId, ReadAt, CreatedAt)` (unread count); `notification_deliveries
(Status, NextAttemptAt)` (dispatch job); `email_campaign_recipients (CampaignId, Status, DueAt)` (send candidates) and
unique `(CampaignId, SubscriberId)`; `email_events (CampaignId, Type)`, `(SubscriberId, Type, OccurredAt)`,
`(ClientAccountId, Type, OccurredAt)`; `submissions (Status, SubmittedAt)` (review queue), unique `NormalizedPostUrl`;
`earning_entries` unique `IdempotencyKey`, `(UserId, Status)`; `form_submissions (FormId, SubmittedAt)`;
`tracking_clicks (TrackingLinkId, ClickedAt)`; `time_entries (UserId, Date)`, `(ProjectId, Date)`.

**Removed:** no index was a duplicate or a redundant prefix of another before the review (the guard test now keeps it
that way); the four indexes marked ↻ were replaced by their extension instead of adding a second index. Unique
constraints the code relies on for check-then-insert races were reviewed (post keys, idempotency keys, recipients per
campaign, tokens, dedup logs, link tables with composite primary keys); none was missing.

## Query fixes

| Where | Before | After |
|---|---|---|
| `AuditLogService.ListAsync` (admin audit log) | `COUNT(*)` over the audit rows **left-joined to users twice** (two PK lookups per audit row; the table grows forever) | Counts the filtered `audit_logs` rows only (`Count rows in a`), joins users only for the page |
| `NotificationCenterService` outbox list | Same count-through-joins pattern over every delivery | Without a search, counts `notification_deliveries` alone |
| `CampaignSendJob` throttle | `CampaignId = ? AND (Status = 'Sending' OR SentAt > ?)` read every recipient of the campaign every 30 s | Two index range counts (`Status = 'Sending'`; `SentAt > ? AND Status <> 'Sending'`), same result |
| `InquiryNotificationHandler` dedup | `Type = 'website.inquiry' AND LinkUrl = ?` scanned every notification | Also bounded by the inquiry's receipt time (notifications about it cannot be older), served by `(Type, CreatedAt)` |
| `IntegrationVerifiers` expiry-warning dedup | Audit lookup by `Action` only | Adds `EntityType`, served by `(EntityType, EntityId)` |
| `SocialEvergreenJob` | N+1: one query per evergreen candidate (up to 200 per run) for its existing copies | One query for all candidates' copies |

Reviewed and already fine: read paths use `AsNoTracking` or projections (tracked queries are write paths), paged lists
have a deterministic order (`…, Id`), exports are capped (`Take(50_000)`), bulk changes use `ExecuteUpdate/Delete`,
and there is no client-side evaluation (EF Core 8 throws). The admin job list issues one indexed single-row query per
job (29), which is cheaper than a window-function query over the whole run log.

## Benchmark: before / after

**Setup.** MySQL 8.0.46, one database seeded with Baseline + Demo, then `OptimizeAll.DbBench volume` added ~1.5 M
synthetic rows (deterministic, UUIDv7 ids at the row's own timestamp, skewed so a few users/clients/campaigns are
"busy"): 200k `audit_logs`, 150k `job_runs`, 130k notifications + deliveries, 100k `email_campaign_recipients`
(10 campaigns × 10k) + 137k `email_events` over 30k subscribers, 50k `submissions` + 125k `submission_events`,
50k `form_submissions`, 100k `landing_page_views`, 100k `tracking_clicks`, 50k `time_entries`, 20k thread messages,
20k CRM contacts, 50k ad metrics, 30k social post metrics, 70k tokens. The "after" database is a copy of the "before"
database with only the index changes applied, so both hold identical rows. Each case mirrors the LINQ of the endpoint
or job it names (`backend/tools/OptimizeAll.DbBench/BenchCases.cs`; the "before" run uses the old query shapes via
`--legacy true`).

**Rows read** is the number of InnoDB row reads for one execution of the case (sum of the session `Handler_read_*`
counters) — the load-independent measure. **ms** is the median of 5 runs on a shared 4-core machine that was also
running other test suites, so treat timings as indicative only. Full SQL and `EXPLAIN ANALYZE` plans for every
statement, before and after: [database/benchmark-plans.md](database/benchmark-plans.md).

| Case | Serves | Before ms | After ms | Before rows read | After rows read |
|---|---|---:|---:|---:|---:|
| `audit.list` | GET /admin/audit-logs (page 1, no filter: count + page) | 698.22 | 32.65 | 353,497 | 200,633 |
| `audit.list.entity` | GET /admin/audit-logs?entityType&entityId | 19.79 | 3.72 | 1,205 | 699 |
| `audit.list.actor` | GET /admin/audit-logs?actorUserId | 381.09 | 30.17 | 67,333 | 67,332 |
| `audit.list.range` | GET /admin/audit-logs?from&to (last 7 days) | 7.40 | 6.86 | 3,731 | 4,151 |
| `audit.user-status-history` | GET /admin/users/{id} (status history) | 1.51 | 1.92 | 1 | 1 |
| `notifications.list` | GET /me/notifications (bell list, page 1) | 65.71 | 6.44 | 50,908 | 25,474 |
| `notifications.unread-count` | GET /me/notifications/unread-count, home | 2.81 | 2.57 | 7,549 | 7,549 |
| `notifications.inquiry-dedup` | InquiryNotificationHandler (per website inquiry) | 82.78 | 1.03 | 100,445 | 1 |
| `notifications.live-check-dedup` | LiveCheckReminderJob (hourly) | 426.21 | 2.68 | 100,444 | 32 |
| `notifications.dispatch-candidates` | NotificationDispatchJob (every 30 s) | 1.94 | 2.44 | 100 | 100 |
| `notifications.outbox` | GET /admin/notifications/deliveries (page 1) | 145.96 | 20.35 | 118,002 | 60,276 |
| `jobs.list` | GET /admin/jobs (last run of each of 29 jobs) | 438.88 | 33.51 | 150,029 | 29 |
| `jobs.runs` | GET /admin/jobs/runs (page 1) | 173.71 | 18.93 | 300,005 | 150,028 |
| `jobs.runs.by-name` | GET /admin/jobs/runs?jobName (page 1) | 208.20 | 27.86 | 121,992 | 61,021 |
| `email.send-candidates` | CampaignSendJob (every 30 s, per sending campaign) | 17.38 | 4.98 | 10,002 | 3 |
| `email.campaign-report` | GET /email/campaigns/{id}/report (recipient aggregates + events) | 63.56 | 38.30 | 17,904 | 17,904 |
| `email.kpis` | GET /email/reports/kpis (last 30 days, client workspace) | 168.22 | 201.63 | 132,189 | 132,189 |
| `email.subscribers` | GET /email/subscribers (page 1, newest first) | 137.79 | 26.04 | 60,562 | 30,306 |
| `email.subscribers.search` | GET /email/subscribers?search= | 177.74 | 275.78 | 60,562 | 60,562 |
| `email.subscriber-activity` | GET /email/subscribers/{id} (activity) | 0.99 | 1.31 | 16 | 16 |
| `email.campaign-list-stats` | GET /email/campaigns (per-page recipient stats) | 571.48 | 357.92 | 101,923 | 101,923 |
| `review.queue` | GET /review/queue (oldest first) | 44.36 | 17.01 | 62,606 | 12,543 |
| `review.stats` | GET /review/stats (reviewer dashboard) | 171.18 | 20.20 | 213,418 | 23,500 |
| `review.reviewers` | GET /review/reviewers (decisions today per reviewer) | 58.51 | 1.79 | 125,849 | 343 |
| `submissions.mine` | GET /me/submissions (page 1) | 85.23 | 45.59 | 31,857 | 31,857 |
| `submissions.risk-velocity` | POST /me/submissions (risk signals) | 57.93 | 0.89 | 15,911 | 35 |
| `analytics.posts` | GET /analytics (posts in last 30 days) | 146.91 | 11.30 | 76,793 | 11,761 |
| `forms.rate-limit` | POST /public/forms/{id} (per-IP rate limit) | 32.59 | 2.11 | 50,137 | 2 |
| `forms.submissions` | GET /landing/forms/{id}/submissions (page 1) | 106.81 | 16.26 | 31,553 | 29,961 |
| `landing.view-dedup` | GET /public/p/{slug} (unique-view check per visit) | 1.14 | 3.19 | 68 | 1 |
| `seo.kpi-leads` | GET /seo/kpis, client reports (views + leads by client, 90 days) | 171.42 | 40.29 | 110,812 | 39,647 |
| `tracking.summary` | GET /marketing/tracking/summary (30 days) | 44.16 | 22.81 | 109,411 | 17,420 |
| `time.admin-week` | GET /agency/dashboard (admin: this week's minutes) | 25.44 | 3.71 | 100,548 | 1,532 |
| `time.utilization` | GET /agency/time/utilization (30 days) | 103.21 | 5.80 | 53,448 | 6,336 |
| `clients.health-last-activity` | GET /agency/clients (health: last message/time) | 149.95 | 66.18 | 90,291 | 50,281 |
| `crm.contacts` | GET /crm/contacts (page 1, newest first) | 9.45 | 4.72 | 20,037 | 20,037 |
| `ads.adgroup-metrics` | GET /ads/campaigns/{id}/ad-groups (30 days) | 12.01 | 6.94 | 430 | 430 |
| `retention.job-runs` | DataRetentionJob: job runs older than 30 days (batch of ids) | 61.10 | 2.74 | 150,003 | 1,001 |
| `retention.notifications` | DataRetentionJob: read notifications older than 180 days (batch of ids) | 17.05 | 8.67 | 32,546 | 32,542 |
| `retention.refresh-tokens` | DataRetentionJob: expired refresh tokens (batch of ids) | 2.26 | 1.41 | 1,001 | 1,001 |

Notes on the cases that did not improve:

* `email.kpis`, `email.campaign-list-stats`, `email.campaign-report`, `forms.submissions`, `crm.contacts`,
  `notifications.unread-count`: the work is inherent — they aggregate or count every matching row (e.g. all recipients
  of the 25 campaigns on a page). In the synthetic data one client owns every e-mail campaign, so the optimizer
  correctly prefers a scan; with many clients `(CampaignId, SentAt)` lets `email.kpis` start from the client's campaigns.
  See *Recommendations* (maintained counters).
* `submissions.mine`: the synthetic busiest participant has ~16k submissions (real participants have tens), so the
  optimizer joins from the 8 campaigns; for normal volumes `(UserId, SubmittedAt)` serves the page directly.
* `email.subscribers.search`: `LIKE '%term%'` cannot use a B-tree index (see *Recommendations*).
* `audit.list.actor`: the total is an exact count of 67k rows for the busiest actor; the page itself is instant.
* `retention.*` rows are the new housekeeping queries (no "before" code): `job_runs` uses `(StartedAt)`, the others a
  primary-key range (`IdGenerator.LowerBound`).

Selected plans (MySQL `EXPLAIN ANALYZE`, costs/timings trimmed):

```text
jobs.list — SELECT … FROM job_runs WHERE JobName = ? ORDER BY StartedAt DESC, Id DESC LIMIT 1   (× 29 jobs)
before  -> Sort: j.StartedAt DESC, j.Id DESC, limit input to 1 row(s) per chunk
            -> Index lookup on j using IX_job_runs_JobName_RunKey_Attempt (JobName='…')      150,029 rows read in total
after   -> Limit: 1 row(s)
            -> Index lookup on j using IX_job_runs_JobName_StartedAt (JobName='…') (reverse)    29 rows read in total

forms.rate-limit — SELECT COUNT(*) FROM form_submissions WHERE IpHash = ? AND SubmittedAt >= ?   (every public form post)
before  -> Table scan on f                                                                  50,137 rows read
after   -> Covering index range scan on f using IX_form_submissions_IpHash_SubmittedAt          2 rows read

review.stats — SELECT COUNT(*) FROM submission_events WHERE ActorUserId = ? AND CreatedAt >= ? AND Action IN (…)
before  -> Table scan on s                                                                 125,663 rows
after   -> Index range scan on s using IX_submission_events_ActorUserId_CreatedAt

email.send-candidates — COUNT(*) … WHERE CampaignId = ? AND (Status = 'Sending' OR SentAt > ?)   (every 30 s)
before  -> Index lookup on e using IX_email_campaign_recipients_CampaignId_SubscriberId       10,002 rows read
after   -> Covering index lookup … IX_email_campaign_recipients_CampaignId_Status_DueAt (Status='Sending')
         + Index range scan … IX_email_campaign_recipients_CampaignId_SentAt                         3 rows read

notifications.list — … WHERE UserId = ? ORDER BY CreatedAt DESC, Id DESC LIMIT 20
before  -> Sort … -> Index lookup on n using IX_notifications_UserId_ReadAt_CreatedAt
after   -> Index range scan on n using IX_notifications_UserId_CreatedAt (reverse)

time.admin-week — SELECT SUM(Minutes) FROM time_entries WHERE Date BETWEEN ? AND ?
before  -> Table scan on t                                                                  50,000 rows
after   -> Index range scan on t using IX_time_entries_Date

audit.list — total count
before  SELECT COUNT(*) FROM audit_logs a LEFT JOIN users u … LEFT JOIN users u0 …
        -> Nested loop left join -> Nested loop left join -> Table scan on a (+2 PK lookups per row)
after   SELECT COUNT(*) FROM audit_logs a
        -> Count rows in a
```

### Running the benchmark

```bash
# 1. a scratch MySQL database migrated and seeded with Baseline + Demo: start the API once against it
#    (Database__Seed__0=Baseline Database__Seed__1=Demo Jobs__Enabled=false), then stop it
# 2. add the synthetic volume (≈10 min); never point this at a real database
dotnet run --project backend/tools/OptimizeAll.DbBench -- volume --connection "Server=127.0.0.1;Database=oa_bench;User=…;Password=…"
# 3. measure (EXPLAIN ANALYZE + rows read + median of 5 runs) and compare two runs
dotnet run --project backend/tools/OptimizeAll.DbBench -- bench --connection "…" --out after.json [--only audit.] [--legacy true]
dotnet run --project backend/tools/OptimizeAll.DbBench -- report --before before.json --after after.json --out report.md
```

To compare a schema change, copy the database (`mysqldump | mysql`) and apply only the index DDL to the copy, so both
sides hold identical rows. When an endpoint's query changes, update its case in `BenchCases.cs`.

## Retention

`DataRetentionJob` (`Modules/Admin/Housekeeping`, hourly, listed in the admin job page) deletes old rows of
operational tables in batches: it selects up to `BatchSize` ids (oldest first) and deletes them by id, at most
`MaxBatchesPerTable` statements per table per run, so no statement holds locks for long and a crash only leaves work
for the next run. Settings (section `DataRetention`, environment `DataRetention__<Name>`; `0` keeps rows forever):

| Setting | Default | Deletes |
|---|---|---|
| `Enabled` | `true` | Master switch. |
| `JobRunDays` | `30` | `job_runs` that started earlier (never a run still marked Running). At the default intervals the jobs log ~20k runs/day. |
| `ReadNotificationDays` | `180` | In-app notifications the user has read, created earlier. |
| `NotificationDays` | `365` | Any notification created earlier. A notification whose e-mail/WhatsApp delivery is still Pending/Sending is never deleted; its `notification_deliveries` rows are removed with it (FK cascade). |
| `ExpiredTokenDays` | `30` | `refresh_tokens` and `user_tokens` (verification/reset) that expired more than this long ago (they can no longer be used). |
| `TrackingEventDays` | `0` (off) | Raw `tracking_clicks` and `landing_page_views`. Off by default because analytics for periods before the cutoff would count fewer clicks/views; `395` (13 months) matches the privacy guidance in OPERATIONS.md § 7. |
| `BatchSize` / `MaxBatchesPerTable` | `1000` / `50` | Per statement / per table per run (≤ 50k rows per table per hour). |

Never touched: `audit_logs` (legal record; indexed for its queries instead), `earning_entries` and all other money
tables, submissions and their history, consent records, e-mail events (they drive reports and automations),
impersonation sessions. Other existing housekeeping: `UsedFormTokenCleanupJob` (spent form tokens, hourly).
Notification and token candidates are found through a primary-key range (`Id < IdGenerator.LowerBound(cutoff)`) plus
the exact time condition, so those write-heavy tables need no extra time index; rows with non-UUIDv7 ids (none are
created by the application) would simply be kept. Tests: `tests/OptimizeAll.IntegrationTests/Admin/DataRetentionJobTests.cs`
(one per policy, both providers).

## Column hygiene review

The model (232 tables, ~480 indexes) was checked column by column (EF metadata dump):

* **Money / rates:** every `decimal` is `DECIMAL(19,4)` by convention except `exchange_rates.Rate` and
  `earning_entries.ExchangeRate` (`DECIMAL(18,8)`, FX rates). No money is stored as float/double.
* **Strings:** all short fields have a maximum length (e-mail `254` everywhere, codes/keys/slugs bounded, hashes
  `char(64)`, currencies 3 chars); unbounded (`longtext`) columns are only genuine long text (bodies, JSON, snapshots,
  proposals, cover letters, encrypted secrets).
* **Enums** are stored as their names in `varchar(40)` (one convention for the whole model); **DateTime** values are
  UTC `datetime(6)` through the `AppDbContext` converters; nullability follows the domain (`?` properties only).
* **Foreign keys:** cascades exist only from an aggregate to its own parts (invoice → lines/reminders, proposal →
  versions/lines, contract → lines, audit → pages/issues, subscriber → consent history for GDPR erasure, client →
  its marketing data). Money, ledger and audit rows use `Restrict` (payments, payout items, earnings, invoices from
  clients). Every foreign key has a covering index (guard test).
* **Unique constraints** backing race-prone insert-if-absent code were all present; none added.

No column change was needed; the review changed indexes and queries only.

## Recommendations for larger scale

* **Maintained counters** for per-campaign e-mail stats (sent/opened/clicked on `email_campaigns`, updated by the send
  job and tracking webhooks with `ExecuteUpdate … + 1`) would make the campaign list/report O(1) instead of aggregating
  recipients; likewise a per-day rollup table for tracking clicks/landing views would let raw events be pruned
  (`TrackingEventDays`) without changing analytics.
* **Approximate or cached totals** for unfiltered admin lists on forever-growing tables (audit log): e.g. show
  "200,000+" from `information_schema.TABLES.TABLE_ROWS` or cache the total for a minute.
* **Full-text search** for subscriber/CRM search (`LIKE '%…%'` scans the workspace): MySQL `FULLTEXT` / a search
  service, behind the provider abstraction.
* **Partitioning** `audit_logs`, `email_events`, `tracking_clicks` and `landing_page_views` by month (RANGE on the time
  column; requires the time column in the primary key) once they reach tens of millions of rows, so retention becomes
  `DROP PARTITION` and range scans prune partitions. MySQL-only: keep it in deployment DDL, not in the EF model.
* **Read replica** for reporting/analytics/exports (analytics, KPI and CSV exports are read-only and tolerate seconds
  of lag): a second `DbContext` registration with a read-only connection string.
* **Binary UUIDs**: ids are `char(36)` (36 bytes, plus 36 in every secondary index entry). `BINARY(16)` would roughly
  halve index sizes; it is a provider-specific type change for every table and needs a data migration, so it is only
  worth it at very large scale.
* Keep `innodb_buffer_pool_size` ≥ the hot working set (the high-volume tables' recent partitions and indexes) and
  monitor `performance_schema.events_statements_summary_by_digest` for new full scans (`SUM_NO_INDEX_USED`).

## Table inventory

Growth classes: **high-volume** (append-only or one row per event/recipient/day; indexes and retention matter),
**per-client / growing** (grows with clients/users), **per-client / business** (edited records), **static / config**
(seeded or admin-maintained, small).

| Table | Growth class | Notes |
|---|---|---|
| `achievements` | static / config | |
| `ads_accounts` | per-client / business | |
| `ads_ad_groups` | per-client / business | |
| `ads_ads` | per-client / business | |
| `ads_alerts` | per-client / business | |
| `ads_budgets` | per-client / business | |
| `ads_campaigns` | per-client / business | |
| `ads_client_settings` | static / config | |
| `ads_creatives` | per-client / business | |
| `ads_daily_metrics` | high-volume | daily metrics per ad entity |
| `ads_experiment_variants` | per-client / business | |
| `ads_experiments` | per-client / business | |
| `ads_import_batches` | per-client / business | |
| `ads_media_plan_lines` | per-client / business | |
| `ads_media_plans` | per-client / business | |
| `ads_utm_links` | per-client / business | |
| `announcements` | static / config | |
| `appeals` | per-client / business | |
| `audit_logs` | high-volume | append-only legal record (every privileged action); kept forever |
| `billing_number_sequences` | static / config | |
| `brand_assets` | per-client / business | |
| `brand_kits` | per-client / business | |
| `brief_templates` | static / config | |
| `briefs` | per-client / business | |
| `campaign_assets` | per-client / business | |
| `campaign_categories` | static / config | |
| `campaign_disclosures` | per-client / business | |
| `campaign_platforms` | per-client / business | |
| `campaigns` | per-client / business | |
| `client_accounts` | per-client / business | |
| `client_feedback` | per-client / business | |
| `client_meetings` | per-client / business | |
| `client_members` | per-client / business | |
| `client_onboarding_items` | per-client / business | |
| `client_reminder_policies` | per-client / business | |
| `client_reports` | per-client / business | |
| `client_team_assignments` | per-client / business | |
| `content_calendar_entries` | per-client / business | |
| `content_copy_entries` | static / config | |
| `contract_lines` | per-client / business | |
| `contracts` | per-client / business | |
| `credit_note_applications` | per-client / business | |
| `credit_notes` | per-client / business | |
| `crm_activities` | per-client / growing | CRM activities/tasks |
| `crm_assignment_cursors` | static / config | |
| `crm_companies` | per-client / business | |
| `crm_contacts` | per-client / business | |
| `crm_deal_contacts` | per-client / business | |
| `crm_deals` | per-client / business | |
| `crm_engagements` | high-volume | contact engagement events |
| `crm_inbound_events` | high-volume | dedup keys of processed inbound events |
| `crm_pipeline_stages` | static / config | |
| `crm_proposal_templates` | static / config | |
| `crm_saved_views` | per-client / business | |
| `crm_scoring_rules` | static / config | |
| `custom_roles` | static / config | |
| `data_protection_keys` | static / config | |
| `deliverable_comments` | per-client / growing | review comments |
| `deliverable_reviews` | per-client / business | |
| `deliverable_versions` | per-client / business | |
| `deliverables` | per-client / business | |
| `delivery_dispatch_keys` | per-client / growing | dedup keys of delivery reminders |
| `delivery_files` | per-client / business | |
| `earning_entries` | high-volume | immutable money ledger (kept forever) |
| `email_automation_enrollments` | high-volume | one per subscriber per automation iteration |
| `email_automation_step_runs` | high-volume | one per automation step per subscriber |
| `email_automation_steps` | per-client / business | |
| `email_automations` | per-client / business | |
| `email_campaign_recipients` | high-volume | one per campaign x recipient |
| `email_campaign_variants` | per-client / business | |
| `email_campaigns` | per-client / business | |
| `email_consent_records` | per-client / growing | consent evidence (legal; kept) |
| `email_events` | high-volume | send/open/click/bounce events |
| `email_imports` | per-client / business | |
| `email_list_memberships` | per-client / growing | subscriber x list |
| `email_lists` | per-client / business | |
| `email_segments` | per-client / business | |
| `email_sender_profiles` | per-client / business | |
| `email_subscriber_fields` | per-client / growing | subscriber x custom field |
| `email_subscriber_tags` | per-client / growing | subscriber x tag |
| `email_subscribers` | per-client / growing | contacts per workspace |
| `email_suppressions` | per-client / business | |
| `email_template_overrides` | static / config | |
| `email_templates` | static / config | |
| `email_tracked_links` | per-client / business | |
| `email_workspace_settings` | static / config | |
| `exchange_rates` | static / config | |
| `experiment_assignments` | per-client / growing | A/B assignment per visitor |
| `experiment_variants` | per-client / business | |
| `experiments` | per-client / business | |
| `external_logins` | per-client / business | |
| `faq_items` | static / config | |
| `form_consent_versions` | per-client / business | |
| `form_email_outbox` | per-client / growing | autoresponder outbox |
| `form_submission_files` | per-client / business | |
| `form_submissions` | high-volume | public form leads |
| `form_templates` | static / config | |
| `forms` | per-client / business | |
| `homepage_banners` | static / config | |
| `hourly_rates` | static / config | |
| `impersonation_sessions` | per-client / growing | security record (kept) |
| `integration_connections` | per-client / business | |
| `invitation_links` | per-client / business | |
| `invoice_lines` | per-client / business | |
| `invoice_payments` | per-client / business | |
| `invoice_reminders` | per-client / growing | reminders sent |
| `invoices` | per-client / business | |
| `job_leases` | static / config | |
| `job_runs` | high-volume | one row per job run (~20k/day at default intervals); DataRetention (30 days) |
| `landing_page_assignments` | per-client / business | |
| `landing_page_templates` | static / config | |
| `landing_page_versions` | per-client / business | |
| `landing_page_views` | high-volume | public page views; DataRetention (opt-in) |
| `landing_pages` | per-client / business | |
| `message_threads` | per-client / business | |
| `notification_deliveries` | high-volume | outbox, one per e-mail/WhatsApp copy; cascades with notifications |
| `notification_preferences` | per-client / business | |
| `notifications` | high-volume | append-only per user event; DataRetention |
| `onboarding_step_completions` | per-client / business | |
| `onboarding_steps` | static / config | |
| `payment_attempts` | per-client / growing | payout attempts |
| `payment_claims` | per-client / business | |
| `payment_proofs` | per-client / business | |
| `payout_batches` | per-client / business | |
| `payout_holds` | per-client / business | |
| `payout_item_earnings` | high-volume | ledger x payout link |
| `payout_items` | per-client / business | |
| `payout_profiles` | per-client / business | |
| `payout_schedules` | static / config | |
| `post_templates` | static / config | |
| `project_members` | per-client / business | |
| `project_milestones` | per-client / business | |
| `project_tasks` | per-client / business | |
| `project_templates` | static / config | |
| `projects` | per-client / business | |
| `proposal_lines` | per-client / business | |
| `proposal_versions` | per-client / business | |
| `proposals` | per-client / business | |
| `recurring_task_rules` | static / config | |
| `referrals` | per-client / business | |
| `refresh_tokens` | high-volume | one per sign-in/refresh rotation; DataRetention |
| `report_templates` | static / config | |
| `retention_message_logs` | per-client / growing | retention message dedup log |
| `reward_rule_sets` | static / config | |
| `reward_rules` | static / config | |
| `seo_audit_issues` | high-volume | issues per audit |
| `seo_audit_pages` | high-volume | crawled pages per audit |
| `seo_audit_rules` | static / config | |
| `seo_audits` | per-client / business | |
| `seo_backlinks` | per-client / growing | backlinks per site |
| `seo_citation_sources` | per-client / business | |
| `seo_citations` | per-client / business | |
| `seo_content_briefs` | per-client / business | |
| `seo_keywords` | per-client / business | |
| `seo_local_profiles` | per-client / business | |
| `seo_outreach_prospects` | per-client / business | |
| `seo_rank_snapshots` | high-volume | daily rank per keyword |
| `seo_reviews` | per-client / business | |
| `seo_search_performance` | high-volume | search console rows |
| `seo_sites` | per-client / business | |
| `service_catalog_items` | static / config | |
| `sm_awareness_days` | static / config | |
| `sm_brand_profiles` | per-client / business | |
| `sm_campaigns` | per-client / business | |
| `sm_caption_snippets` | per-client / business | |
| `sm_client_settings` | static / config | |
| `sm_competitor_snapshots` | high-volume | daily competitor snapshots |
| `sm_competitors` | per-client / business | |
| `sm_hashtag_sets` | per-client / business | |
| `sm_inbox_items` | per-client / growing | social inbox |
| `sm_inbox_replies` | per-client / business | |
| `sm_listening_queries` | per-client / business | |
| `sm_media_assets` | per-client / business | |
| `sm_mentions` | per-client / growing | listening results |
| `sm_metric_imports` | per-client / business | |
| `sm_network_presets` | static / config | |
| `sm_post_comments` | per-client / business | |
| `sm_post_metrics` | high-volume | daily metrics per post |
| `sm_post_variants` | per-client / business | |
| `sm_posts` | per-client / business | |
| `sm_profile_metrics` | high-volume | daily metrics per profile |
| `sm_publish_attempts` | per-client / growing | publish attempts |
| `sm_queue_slots` | per-client / business | |
| `social_accounts` | per-client / business | |
| `stored_files` | per-client / growing | uploaded files metadata |
| `submission_events` | high-volume | status history (3-5 per submission) |
| `submission_flags` | per-client / growing | risk flags per submission |
| `submissions` | high-volume | participant posts |
| `support_messages` | per-client / business | |
| `support_tickets` | per-client / business | |
| `system_settings` | static / config | |
| `task_assignees` | per-client / business | |
| `task_attachments` | per-client / business | |
| `task_checklist_items` | per-client / business | |
| `task_comments` | per-client / growing | task comments |
| `task_dependencies` | per-client / business | |
| `task_watchers` | per-client / business | |
| `tax_rates` | static / config | |
| `thread_messages` | high-volume | client messages |
| `thread_read_states` | per-client / business | |
| `time_entries` | high-volume | staff time tracking |
| `timesheets` | per-client / business | |
| `tracking_clicks` | high-volume | public link clicks; DataRetention (opt-in) |
| `tracking_conversions` | per-client / growing | advertiser conversions |
| `tracking_links` | per-client / business | |
| `user_achievements` | per-client / growing | awarded achievements |
| `user_custom_roles` | static / config | |
| `user_roles` | static / config | |
| `user_tokens` | per-client / growing | verification/reset tokens; DataRetention |
| `users` | per-client / business | |
| `website_blog_categories` | static / config | |
| `website_blog_posts` | per-client / business | |
| `website_case_studies` | static / config | |
| `website_consultation_blackouts` | per-client / business | |
| `website_consultation_bookings` | per-client / business | |
| `website_consultation_settings` | static / config | |
| `website_cv_files` | per-client / business | |
| `website_industries` | static / config | |
| `website_inquiries` | high-volume | website leads |
| `website_job_application_notes` | per-client / business | |
| `website_job_applications` | per-client / business | |
| `website_job_openings` | static / config | |
| `website_newsletter_subscribers` | per-client / business | |
| `website_page_revisions` | per-client / business | |
| `website_pages` | static / config | |
| `website_service_categories` | static / config | |
| `website_service_packages` | static / config | |
| `website_services` | static / config | |
| `website_settings` | static / config | |
| `website_team_members` | static / config | |
| `website_testimonials` | static / config | |
| `website_used_form_tokens` | per-client / growing | spent form tokens; UsedFormTokenCleanupJob |
