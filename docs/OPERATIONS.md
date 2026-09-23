# Optimize All — Operations Runbooks

Audience: platform operators, finance and support leads. Deployment is covered in
[DEPLOYMENT.md](DEPLOYMENT.md), security controls in [SECURITY.md](SECURITY.md).

All timestamps in the database and APIs are **UTC**. SQL below is MySQL 8; run read queries against a replica
when available, and never modify financial tables (`earning_entries`, `payout_*`, `payment_attempts`,
`audit_logs`) by hand — use the application, which audits every change.

## 1. Background jobs

Jobs run inside every API instance (`Jobs__Enabled=true`). Each job takes a **database lease**
(`job_leases`, 10-minute lease) so only one instance runs it at a time, and each run is logged in `job_runs`
(`Status` = Running / Succeeded / Failed, `Summary`, full `Error`, `InstanceId`). Jobs are written to be
**safe to retry**: they use idempotency keys, unique constraints and conditional updates, so a crash midway or
a second run cannot duplicate effects. A failed run is simply retried on the next tick.

| Job (class) | Interval | What it does | If it fails |
|---|---|---|---|
| `NotificationDispatchJob` | 30 s | Sends pending Email/WhatsApp deliveries from the outbox (`notification_deliveries`) with retry/backoff | Deliveries stay Pending/Failed; see § 3 |
| `CampaignScheduleJob` | 1 min | Starts/ends campaigns at their scheduled times, enforces budgets/caps | Campaigns don't open/close on time |
| `LiveCheckReminderJob` | 1 h | Reminds reviewers/participants about posts that must stay live for the hold period | Missed live-check reminders |
| `PayoutPreparationJob` | 15 min | Prepares the draft payout batch when a payout period closes (idempotent per period) | No draft batch; finance can prepare manually |
| `ReferralExpiryJob` | 1 h | Expires referral rewards whose qualification window has passed | Referrals stay pending longer |
| `RetentionJob` | 1 h | Engagement automations: onboarding reminders, new-campaign alerts, reactivation (deduplicated per user/kind; setting `retention.enabled`) | Fewer engagement messages; nothing financial |

The run-log name of a job (`job_runs.JobName`) is the job's `Name` and may differ slightly from the class name
(e.g. `payouts.prepare`); the admin UI lists them.

### Monitoring and re-running

* **Admin UI → System → Jobs** (permission `jobs.view`) lists each job with its last run and error, and the
  paged run history (`GET /api/v1/admin/jobs`, `GET /api/v1/admin/jobs/runs`).
* **Run now:** admins with `settings.manage` can trigger a job (`POST /api/v1/admin/jobs/{name}/run`). The
  run takes the same lease, so it is skipped (returns no run) if another instance is currently running it.
* SQL checks:

  ```sql
  -- Latest run per job
  SELECT r.JobName, r.Status, r.StartedAt, r.FinishedAt, LEFT(r.Summary, 120) AS Summary
  FROM job_runs r
  JOIN (SELECT JobName, MAX(StartedAt) AS s FROM job_runs GROUP BY JobName) x
    ON x.JobName = r.JobName AND x.s = r.StartedAt
  ORDER BY r.JobName;

  -- Failures in the last 24 h
  SELECT JobName, COUNT(*) failures, MAX(StartedAt) last_failure
  FROM job_runs WHERE Status = 'Failed' AND StartedAt > UTC_TIMESTAMP() - INTERVAL 1 DAY
  GROUP BY JobName;

  -- Leases (a lease far in the future with no running instance = stuck)
  SELECT * FROM job_leases;
  ```
  (Enums are stored as their names, e.g. `'Failed'`.)
* **Runs stuck in `Running`** (instance killed mid-run): nothing to do — the lease expires after 10 minutes and
  the next tick runs the job again. The stale `Running` row is informational.
* **Repeated failures:** read `job_runs.Error` (full exception) and the API logs around `StartedAt`. Typical
  causes: database connectivity, SMTP/WhatsApp outage, a data invariant violation (bug). Fix the cause, then
  "Run now" or wait for the next tick. Do not delete `job_runs` rows while investigating.

## 2. Payout cycle (finance runbook)

Default schedule: biweekly (admin-configurable under payout settings, permission `payouts.settings`).
Payments are **manual** until a payment provider is integrated: the platform prepares and tracks, a person
moves the money. Every step is audited. Roles: *preparer* and *approver* must be different people (four-eyes).

1. **Prepare** (automatic or manual). After the period closes, `PayoutPreparationJob` creates a **Draft**
   batch for the period (idempotent: one batch per period). Finance can also run *Prepare* in the Payouts
   screen (`payouts.prepare`). The batch contains one item per participant whose *approved* earnings reach the
   minimum threshold and who has a valid payout profile. The exclusions report lists who was left out and why
   (below threshold, no payout method, on hold, suspended).
2. **Review** the draft (`payouts.view`):
   * compare totals with the previous cycles and the ledger (`GET /api/v1/finance/payout-batches/{id}` and export CSV);
   * investigate outliers (unusually large items, new accounts, fraud flags) and **hold** items that need
     checks (`payouts.hold`, reason required) — held items and their earnings roll to a later batch;
   * *Regenerate* the draft if earnings were approved/reversed since preparation.
3. **Finalize** (`payouts.finalize`, confirmation + reason, **must be a different person than the preparer**).
   The batch and its items are frozen (status Finalized, items AwaitingPayment). Finalizing never marks
   anything as paid.
4. **Pay manually.** Download *payment instructions* CSV (`/payment-instructions.csv`) — it contains the
   decrypted payout destinations, so handle it as confidential, don't email it, delete it after use. Execute
   the transfers in the bank / wallet / payment tool.
5. **Record references** for each item as payments complete (`payouts.record_payment`): per item
   (*Record payment*: reference, date, amount actually sent) or in bulk (`/record-payments`). Mark items that
   bounced as *Failed* with the reason; their earnings return to the participant's balance for a later batch.
   Recording is idempotent (a second recording of the same item is rejected), so retries are safe.
6. **Reconcile.** Open the batch *Reconciliation* view / CSV and match it line by line against the bank
   statement: every item Paid has a reference and the sent amount matches; total paid = bank debits. When all
   items are Paid/Failed/Cancelled the batch becomes **Completed**. File the reconciliation CSV with the
   period's accounting records.

Corrections after payment are **never edits**: use a ledger adjustment or reversal (`ledger.adjust`), which
creates a new immutable entry and an audit record. A batch that must be abandoned before payment is
*Cancelled* (releases all its earnings).

## 3. Failed notifications (incident runbook)

Notifications are written to an **outbox** in the same transaction as the business change
(`notifications` = in-app, `notification_deliveries` = one row per Email/WhatsApp delivery with `Status`
Pending/Sending/Sent/Failed/Skipped, `Attempts`, `NextAttemptAt`, `LastError`). `NotificationDispatchJob`
sends them with retry and backoff. In-app notifications are unaffected by channel outages.

1. **Detect**: alert on the Failed count or on Pending deliveries older than 15 minutes:

   ```sql
   SELECT Channel, Status, COUNT(*) n, MIN(CreatedAt) oldest
   FROM notification_deliveries
   WHERE CreatedAt > UTC_TIMESTAMP() - INTERVAL 1 DAY
   GROUP BY Channel, Status;

   SELECT Channel, LEFT(LastError, 200) err, COUNT(*) n
   FROM notification_deliveries WHERE Status = 'Failed' AND CreatedAt > UTC_TIMESTAMP() - INTERVAL 1 DAY
   GROUP BY Channel, err ORDER BY n DESC;
   ```
2. **Diagnose** from `LastError` and the API logs:
   * *SMTP auth / connection errors* → provider credentials, network egress, provider status page.
   * *Rate limited / throttled* → provider quota; deliveries retry automatically with backoff.
   * *Recipient rejected / bounced* → invalid address; not retried forever (Failed). Support can ask the user
     to update their email.
   * *WhatsApp 401/403* → access token expired/revoked (system-user tokens can be revoked in Meta Business
     Manager); *template errors* → template not approved or parameter mismatch.
   * `Skipped` is not an error: channel disabled, user opted out, or no phone/address.
3. **Mitigate**: fix credentials/config and restart (or wait for) the API; pending deliveries resume on the
   next tick. If a provider is down for long, it is acceptable to let deliveries fail — users still have the
   in-app notification. For critical auth emails (verification, reset), users can use "resend".
4. **Recover**: deliveries still inside their retry window are retried automatically. Re-queue only failed
   deliveries from the incident window and only after the cause is fixed — prefer the admin notification tools
   if available; otherwise a reviewed SQL update (keep the WHERE tight and record it in the incident log):

   ```sql
   UPDATE notification_deliveries
   SET Status = 'Pending', NextAttemptAt = UTC_TIMESTAMP(), LockedUntil = NULL
   WHERE Status = 'Failed' AND Channel = 'Email'
     AND CreatedAt BETWEEN '2026-01-01 10:00:00' AND '2026-01-01 12:00:00'
     AND LastError LIKE '%Authentication%';
   ```
   Don't re-queue deliveries older than a day or two (stale "campaign starts now" messages confuse users).
5. **Post-incident**: note the window, affected counts and root cause; add an alert if it was not caught.

## 4. Other incidents (quick reference)

| Symptom | Check | Action |
|---|---|---|
| `/health/ready` 503 | database reachability, credentials, connection limits | fail over / fix DB; API recovers automatically |
| Login fails for everyone | `Jwt__SigningKey` changed? clock skew? | restore the key (rotation signs everyone out, by design) |
| 429 responses | rate limits (`RateLimiting__*`), a proxy hiding client IPs (all users share one IP) | fix `X-Forwarded-For` / `real_ip` config; don't just raise limits |
| Screenshots 404 on some instances | storage not shared between API instances | mount the same shared volume on every instance |
| Suspected account takeover | audit log (`audit_logs`), refresh-token reuse warnings in logs | suspend the user (revokes sessions), force password reset |
| Suspicious payout item | fraud flags, submission history | hold the item (reason), escalate; never delete ledger rows |

## 5. Backup and restore (MySQL)

**What to back up:** the MySQL database (all state, including data-protection keys that decrypt payout
destinations — losing them makes stored payout details unreadable) and the file storage directory
(screenshots/uploads). Back up both on the same schedule; keep backups encrypted and access-controlled.

**Managed MySQL (recommended):** enable automated daily snapshots with >= 14 days retention plus
point-in-time recovery (binlog) >= 7 days; copy snapshots to a second region/account for disaster recovery.
**Files:** versioned object storage or daily snapshots of the shared volume.

**Self-managed logical backup:**

```bash
# consistent online dump (InnoDB), including routines/triggers; compress and encrypt at rest
mysqldump --single-transaction --quick --routines --triggers --set-gtid-purged=OFF \
  --default-character-set=utf8mb4 -h "$DB_HOST" -u backup -p optimizeall | gzip > optimizeall-$(date -u +%F).sql.gz
```

**Restore (drill at least quarterly, and before risky migrations):**

1. Restore into a **new** database/instance (never over production): PITR to the chosen time, or
   `gunzip < dump.sql.gz | mysql -h new-host -u admin -p optimizeall_restore`.
2. Verify: `SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId DESC LIMIT 1;` matches the
   release you will run; spot-check row counts of `users`, `earning_entries`, `payout_items`; start an API
   instance against it (staging) and run the smoke checks.
3. Stop API instances (or put the proxy in maintenance), switch `ConnectionStrings__Default`, start the API,
   run smoke checks. Restore the file storage snapshot from the same point in time.
4. Anything written after the restore point is lost: reconcile payments recorded in that window against the
   bank statement and re-record them.

Restoring the `data_protection_keys` table together with the rest of the data is essential; restoring a
database with keys from a different backup makes encrypted payout destinations unreadable.

## 6. Logging and monitoring

* **Logs**: the API writes to stdout. In production set `Logging__Console__FormatterName=json` (the production
  compose example does) and ship to your log platform (CloudWatch, Loki, Datadog, ELK). Include the trace id:
  every error response carries `traceId`, which appears in the logs. Logs never contain passwords, tokens or
  payout details; keep the default `Warning` level for `Microsoft.AspNetCore` and EF Core SQL.
* **Health checks**: liveness `/health/live` (process), readiness `/health/ready` (database). Use readiness for
  load-balancer membership and an external uptime check on `https://<host>/health/ready`.
* **Alerts** (suggested thresholds):
  * any `job_runs` Failed for the same job 3 times in a row, or a job with no successful run for 3× its
    interval (e.g. `NotificationDispatchJob` > 2 min, `CampaignScheduleJob` > 5 min);
  * `notification_deliveries` Failed > 20 in 15 min, or Pending older than 15 min;
  * HTTP 5xx rate > 1 % over 5 min; p95 latency > 1 s;
  * 429 rate spikes on auth endpoints (credential stuffing) and `auth.locked_out` audit events spikes;
  * log warnings "Refresh token reuse detected" (possible token theft);
  * database CPU/connections/storage > 80 %, replication lag, backup job failures;
  * certificate expiry < 14 days.
* **Dashboards**: request rate/latency/errors per route group (`/api/v1/me`, `/review`, `/finance`, `/t`),
  job run durations, outbox depth, submissions awaiting review, payout batch status.

## 7. Data retention

| Data | Retention guidance |
|---|---|
| `earning_entries`, `payout_batches`, `payout_items`, `payment_attempts` | Financial records: keep for the statutory period (typically 7–10 years). Immutable. |
| `audit_logs` | Append-only; keep >= the financial retention period. |
| Submissions and screenshots (`submissions`, `stored_files` + files) | Keep while needed for disputes/appeals and audits (e.g. 24 months after payout), then delete screenshots; keep the submission metadata linked to earnings. |
| `tracking_clicks`, `tracking_conversions` | Contain only hashed IPs/device ids; aggregate and delete raw rows after 13 months. |
| `notification_deliveries`, `notifications` | 90–180 days. |
| `job_runs` | 90 days (keep failures longer if under investigation). |
| `refresh_tokens`, `user_tokens` | Expired/revoked rows can be deleted after 30 days. |
| Support tickets | 24 months after closure. |
| Backups | 14–35 days rolling, plus monthly archives per policy. Deleted data persists in backups until they expire — state this in the privacy policy. |

`RetentionJob` is the **engagement** automation (reminders/alerts), not data deletion. Until automated purge
jobs exist, run approved clean-up SQL on a schedule for technical tables (`job_runs`, `notification_deliveries`,
expired tokens) in small batches, e.g.
`DELETE FROM job_runs WHERE StartedAt < UTC_TIMESTAMP() - INTERVAL 90 DAY AND Status <> 'Running' LIMIT 5000;`.
Account deletion requests: suspend/anonymize the user (name, email, phone, social handles, payout details)
while keeping financial and audit records required by law; document each request.
