# Clients, projects & delivery API

Module owners: `Api/Modules/Clients`, `Api/Modules/Projects` (domain in `Domain/Agency`, `Domain/Projects`).
Conventions follow `docs/ARCHITECTURE.md`: JSON is camelCase, enums are strings, timestamps are UTC ISO-8601,
calendar dates (`dueDate`, time-entry `date`, report periods) are `yyyy-MM-dd`, money is `decimal` + ISO currency.
Errors are RFC 7807 problems with a stable `code` (plus `errors` for field validation). Paged lists take
`?page=1&pageSize=25&search=` and return `{ "items", "total", "page", "pageSize", "totalPages" }`.

Common errors on every endpoint: `401`, `403 auth.forbidden` (missing permission), `400` with `errors`,
`404` (not found **or** belongs to a client the caller cannot see — tenancy never leaks as 403),
`409 concurrency.conflict` (stale `concurrencyStamp` on an editable record).

Two API surfaces:

| Surface | Prefix | Who | Tenancy |
|---|---|---|---|
| Staff | `/api/v1/agency/...` | Agency staff (`clients.view`, `projects.view`, …) | `IClientScope`: staff with `clients.view` see every client |
| Client portal | `/api/v1/client/orgs/{clientId}/...` | Client users (`client.portal`) | Caller must be a member of `{clientId}`; writes also check the member role (duty) |

Client member roles: `Owner` (everything + manage members), `Approver` (approve/request changes, briefs, comments,
messages), `Billing` (billing only; read access to delivery), `Viewer` (read only).

Editable records carry a `concurrencyStamp`; send it back on `PUT` and on state transitions. Privileged changes
(client status, team, members, rates, timesheet decisions, deliverable decisions, report publish, file downloads)
are written to the audit log.

Contents: [Clients](#clients) · [Client portal account](#client-portal-account) · [Files](#files) ·
[Dashboard](#dashboard) · [Projects](#projects) · [Tasks](#tasks) · [Templates](#templates) ·
[Deliverables](#deliverables--approvals) · [Time](#time-tracking) · [Reports](#reports) ·
[Briefs, messages, meetings](#briefs-messages-meetings) · [Client portal delivery](#client-portal-delivery) ·
[Background jobs](#background-jobs) · [Notification links](#notification-links)

---

## Clients

All under `/api/v1/agency/clients`, class permission `clients.view`; writes need `clients.manage` unless noted.

| Method & path | Permission | Notes |
|---|---|---|
| `GET /` | clients.view | Paged; `?status=Onboarding\|Active\|Paused\|Churned&accountManagerId=&search=` |
| `POST /` | clients.manage | Creates client + default onboarding checklist. `409 client.slug_taken` |
| `GET /health` | clients.view | Health board for every visible client, worst first |
| `GET /{id}` | clients.view | Detail incl. summary, SLA settings, team, health |
| `PUT /{id}` | clients.manage | Profile, `approvalSlaDays` (1–30, default 3), `autoApproveAfterDays` (null = off) |
| `POST /{id}/status` | clients.manage | `{ status, reason, concurrencyStamp }`; `reason` required for `Paused`/`Churned` (`client.reason_required`) |
| `POST /{id}/logo` | clients.manage | multipart `file`; PNG/JPEG/WebP only (`client.logo_not_image`) |
| `GET /{id}/health` | clients.view | `{ score, level: Green\|Amber\|Red, reasons: [{ code, level, message, penalty }] }` |
| `GET/POST /{id}/team`, `DELETE /{id}/team/{assignmentId}` | clients.manage for writes | Service roles: `AccountManager`, `Strategist`, `Seo`, `Ads`, `Social`, `Content`, `Design` |
| `GET/POST /{id}/members`, `PUT/DELETE /{id}/members/{userId}` | clients.manage for writes | Invite by email: creates a client user and sends a set-password email (`client.member_exists`, `client.invite_staff_account`, `client.last_owner`) |
| `GET/POST /{id}/onboarding`, `PUT /{id}/onboarding/{itemId}` | clients.manage for writes | Checklist items with owner `Client`/`Agency`, status `Pending`/`Done`/`NotApplicable`, `completedBy`, `completedAt`, `completedOnBehalfOfClient`. Ticking or un-ticking a **client-owned** step with `PUT` is refused (`400 onboarding.client_item`): use `on-behalf` |
| `POST /{id}/onboarding/{itemId}/on-behalf` | clients.manage; denied while impersonating | `{ done: bool, note? }`: marks a client-owned step done (or back to Pending) on the client's behalf. Records who and when, sets `completedOnBehalfOfClient` (shown to the client) and audits `client.onboarding_item_completed_on_behalf` / `…_reopened_on_behalf` (reason "Completed by staff on behalf of client"). Idempotent. Agency-owned step: `400 onboarding.not_client_item` |
| `GET/PUT /{id}/brand-kit` | deliverables.submit for PUT | Colors (hex, ≤ 24), fonts, voice, personas (≤ 12), competitors, do/don't |
| `POST /{id}/brand-kit/assets`, `DELETE .../assets/{assetId}` | deliverables.submit / clients.manage (DELETE denied while impersonating) | multipart `file`, `kind`, `name`. DELETE returns the brand kit; audited `client.brand_asset_removed`; the file (row and bytes) is deleted unless it is still used elsewhere; a second or concurrent removal is a 404 |
| `GET /{id}/feedback` | clients.view | CSAT/NPS responses + summary (avg CSAT 90 days, NPS by quarter) |

`GET /api/v1/agency/staff` (`clients.view`) — active staff directory `[{ id, displayName, email, roles }]` for pickers.

### Health score

`100 − Σ penalties`, clamped 0–100. Level is **Red** if any reason is red or score < 50, **Amber** if any is amber
or score < 75, otherwise **Green**. Reasons (each explains itself in `message`):

| Code | Level | Rule |
|---|---|---|
| `overdue_tasks` | Amber / Red | 1–4 overdue open tasks (10 + 3 each) / 5+ (30) |
| `approvals_waiting` / `approvals_stale` | Amber / Red | Deliverables waiting on the client, oldest 3+ / 7+ days |
| `no_activity`, `quiet`, `inactive` | Amber / Amber / Red | No delivery activity ever / 14+ days / 30+ days |
| `csat_mixed` / `csat_low` | Amber / Red | 90-day average CSAT < 3.5 / < 2.5 |
| `nps_detractor` | Amber / Red | Latest NPS 5–6 / 0–4 |
| provider codes | any | Other modules add reasons by implementing `IClientHealthSignalProvider` (e.g. billing: overdue invoices) |

## Client portal account

Class permission `client.portal`, under `/api/v1/client`.

| Method & path | Duty | Notes |
|---|---|---|
| `GET /orgs` | — | `[{ clientId, name, slug, status, role, logoUrl, currency, timeZone }]` for the org switcher |
| `GET /orgs/{clientId}/team` | member | Account team (name, email, roles, `isAccountManager`, `isPrimary`) |
| `GET/POST /orgs/{clientId}/members`, `PUT/DELETE .../members/{userId}` | Owner for writes | Same rules as the staff endpoints |
| `GET /orgs/{clientId}/onboarding`, `PUT .../onboarding/{itemId}` | Approver+ for PUT | Client may only tick items owned by `Client` |
| `GET /orgs/{clientId}/brand-kit`, `POST .../brand-kit/assets` | Approver+ for POST | |
| `GET/POST /orgs/{clientId}/feedback/nps` | Approver+ for POST | `{ score: 0–10, comment }`, once per user per quarter (`feedback.already_submitted`) |
| `POST /orgs/{clientId}/deliverables/{id}/csat` | Approver+ | `{ score: 1–5, comment }`, only after approval (`feedback.not_approved`), once per user |

## Files

All delivery files (task attachments, deliverable versions, brand assets, logos) are stored **privately** through
`IFileStorage` and streamed through authorised endpoints:

- Upload: `POST /api/v1/agency/clients/{clientId}/files` (`deliverables.submit`) or
  `POST /api/v1/client/orgs/{clientId}/files` (any member of the organization). multipart `file`. Returns `{ id, fileName, contentType, size, staffUrl, clientUrl }`.
- Download: `GET /api/v1/agency/files/{id}` (`clients.view`) / `GET /api/v1/client/orgs/{clientId}/files/{id}`.
  Client users only see files attached to client-visible items (a client-visible task, a sent deliverable version,
  the brand kit, or their own uploads).
- Validation: max **50 MB**; content sniffed by magic bytes — PNG, JPEG, WebP, PDF, MP4 only. Errors
  `400 file.empty`, `400 file.too_large`, `400 file.unsupported_type`.

## Dashboard

`GET /api/v1/agency/dashboard` (`projects.view`) — role-aware: `myTasks`, `myTaskCounts`, `reviewQueue` (first 10) and
`reviewQueueTotal` (the whole queue, shown as "Awaiting my review"), `pendingClientApprovals`, `todaysMeetings`, `timer`, `myMinutesThisWeek`; `accountManager` (health board,
overdue by client, utilization) for users with `clients.manage`; `admin` (agency snapshot) for `settings.manage`. Other areas add tiles on the frontend via `features/agency/shared/dashboardTiles.ts`.

## Projects

Under `/api/v1/agency/projects`, class permission `projects.view`.

| Method & path | Permission | Notes |
|---|---|---|
| `GET /` | projects.view | Paged; `?clientId=&status=&member=me&search=` |
| `POST /` | projects.manage | `{ clientId, name, description, type, serviceLines, startDate, endDate, budgetHours, budgetAmount, defaultHourlyRate, ownerUserId, memberUserIds, templateKey?, status }` (amounts in the client's currency) — a template creates milestones, tasks (relative due dates), checklists and recurring rules |
| `GET /{id}`, `PUT /{id}` | view / manage | |
| `POST /{id}/status` | projects.manage | `Planning`, `Active`, `OnHold`, `Completed`, `Cancelled` |
| `POST /{id}/milestones`, `PUT/DELETE /{id}/milestones/{milestoneId}` | projects.manage | |
| `GET /{id}/tasks`, `POST /{id}/tasks` | view / deliverables.submit | `?status=&assigneeId=&milestoneId=&search=` |
| `GET/POST /{id}/recurring-tasks`, `PUT/DELETE .../{ruleId}` | projects.manage for writes | `Weekly` (day of week) or `Monthly` (day of month) rules |
| `GET /{id}/files` | projects.view | Every file attached to the project's tasks and deliverables |
| `GET /{id}/budget` | projects.view | Budget burn: hours and cost (rate = user override → role → project default), `% used`, `forecast`. Cost fields only with `time.view_all` |
| `GET /{id}/time` | time.view_all | Time entries on the project |

## Tasks

Under `/api/v1/agency/tasks` (`projects.view`).

| Method & path | Permission | Notes |
|---|---|---|
| `GET /mine` | projects.view | `?filter=open\|overdue\|today\|week\|done` |
| `GET /{id}`, `PUT /{id}` | view / deliverables.submit | Title, description, priority, due date, estimate, labels, `clientVisible`, assignees (≤ 10), `blockedByTaskIds` (`task.dependency_cycle`, `task.self_dependency`) |
| `POST /{id}/move` | deliverables.submit | `{ status, afterTaskId, concurrencyStamp }` — kanban move/reorder. `409 task.blocked` when moving a blocked task to `Done` |
| `DELETE /{id}` | projects.manage | `409 task.has_time` if time is logged |
| `POST /{id}/comments` | projects.view | `@mentions` by user id in `mentionedUserIds` (must be project members/assignees; `task.invalid_mention`) — mentioned users are notified |
| `POST/DELETE /{id}/watch` | projects.view | Watchers are notified of comments and status changes |
| `POST /{id}/checklist`, `PUT/DELETE .../checklist/{itemId}` | deliverables.submit | ≤ 50 items |
| `POST /{id}/attachments`, `DELETE .../attachments/{attachmentId}` | deliverables.submit (DELETE denied while impersonating) | `{ fileId }` from the upload endpoint. Both audited (`task.attachment_added` / `task.attachment_removed`). DELETE deletes the file (row and bytes) unless it is still used elsewhere; a second or concurrent removal is a 404 |

Statuses: `Todo`, `InProgress`, `InReview`, `Blocked`, `Done`.

## Templates

`/api/v1/agency/templates` (`projects.view`): `GET/POST projects`, `PUT projects/{id}` (`projects.manage`),
`GET briefs`, `GET reports`. The baseline seeder installs project templates (SEO monthly retainer, Social media monthly content,
Google Ads launch, Website build, Email program setup), brief templates (social content, paid ads campaign,
blog/article, SEO landing page, email campaign, design request, video) and report templates (Monthly performance
report, Campaign wrap-up).

## Deliverables & approvals

Under `/api/v1/agency/deliverables` (`projects.view`).

| Method & path | Permission | Notes |
|---|---|---|
| `GET /` | projects.view | `?clientId=&projectId=&status=&view=review\|client\|mine` |
| `POST /` | deliverables.submit | `{ projectId, taskId?, title, type, description, reviewerUserId }` |
| `GET /{id}`, `PUT /{id}` | view / submit | Detail includes `versions`, `comments` (pinned to a version), `history`, `allowedActions` |
| `POST /{id}/versions` | deliverables.submit | `{ fileId? , linkUrl?, body?, notes }` — at least one of file/link/body |
| `POST /{id}/submit` | deliverables.submit | Draft/ChangesRequested → InternalReview |
| `POST /{id}/internal-approve` | deliverables.submit | Assigned reviewer or `projects.manage` (`deliverable.not_reviewer`); sends the current version to the client: status ClientReview, `clientDueAt` = sent + client `approvalSlaDays` business days, client approvers notified |
| `POST /{id}/internal-request-changes` | deliverables.submit | Comment required |
| `POST /{id}/publish` | projects.manage | Approved → Published |
| `POST /{id}/comments` | projects.view | `{ body, versionNumber, isInternal }` — internal comments are never shown to clients |

State machine:

```
Draft ──submit──► InternalReview ──internal-approve──► ClientReview ──client approve / auto-approve──► Approved ──publish──► Published
  ▲                    │ internal-request-changes             │ client request-changes
  └────────────────────┘                                      ▼
                                ChangesRequested ──(new version)──submit──► InternalReview
```

Guarantees:
- Every decision names the version: `{ version, comment }`. Deciding on anything but the current sent version is
  `409 deliverable.stale_version` (the client must reload).
- Transitions are conditional updates (`WHERE status = expected AND currentVersion = n`), so two approvers racing
  produce exactly one approval; the loser gets `409 deliverable.not_awaiting_approval`.
- `DeliverableApproved` is published once, after commit.
- Changes requested require a comment (`deliverable.comment_required`).
- SLA reminders: one reminder when feedback is due within 24 h and one overdue notice, each deduplicated per
  deliverable + version.
- Auto-approve is **off by default**. When a client sets `autoApproveAfterDays`, deliverables still in client review
  that many business days after sending are approved as `autoApproved: true` and the account manager is notified.

## Time tracking

Under `/api/v1/agency/time` (`time.track`).

| Method & path | Permission | Notes |
|---|---|---|
| `GET /timer`, `POST /timer/start`, `POST /timer/stop` | time.track | One running timer per user (unique index); `409 time.timer_running`, `409 time.no_timer`. `204` when none running |
| `GET /entries`, `POST /entries`, `PUT/DELETE /entries/{id}` | time.track | Manual entries `{ projectId, taskId?, date, minutes, billable, note }`. `?userId=` requires `time.view_all`. Entries in a submitted/approved week are locked (`time.week_locked`) |
| `GET /entries/export.csv` | time.track | Same filters; every matching entry (not only the 2,000 the list shows), ≤ 50,000 (over the cap: 422 `export.too_large`); CSV with formula-injection-safe cells |
| `GET /timesheets/week?weekStart=` | time.track | Week starts Monday |
| `POST /timesheets/submit` | time.track | `{ weekStart }` (`time.empty_week`, `time.already_submitted`) |
| `GET /timesheets/pending`, `POST /timesheets/{id}/approve`, `POST /timesheets/{id}/reject` | projects.manage | Cannot decide your own (`time.own_timesheet`); reject needs a comment |
| `GET /utilization?from=&to=` | time.view_all | Per user: total, billable, capacity (8 h per weekday), `utilizationPercent`, `billablePercent` |
| `GET /rates`, `PUT /rates`, `DELETE /rates/{id}` | time.view_all (+ projects.manage to write) | Hourly cost/bill rates by user, role or project default, with currency |

## Reports

Under `/api/v1/agency/reports` (`reports.manage`).

| Method & path | Notes |
|---|---|
| `GET /` | `?clientId=&status=Draft\|Published` |
| `GET /providers` | Registered section providers (`IClientReportSection`) |
| `POST /` | `{ clientId, projectId?, periodStart, periodEnd, templateKey }` — sections are filled from providers |
| `GET /{id}`, `PUT /{id}` | Edit title, sections, manual KPIs |
| `POST /{id}/refresh-section` | `{ sectionKey }` — re-run the provider (`report.manual_section` for manual sections) |
| `POST /{id}/publish` | Draft → Published; client users are notified. Published reports are read-only |
| `DELETE /{id}` | Drafts only |

Every KPI carries `measurement: Measured | Estimated | Manual` and a `source` (required for Manual/Estimated,
`report.kpi_source_required`). A channel section whose provider has no data returns `providerNote`; clients never
see empty sections. Other modules add channels by registering an `IClientReportSection`
(`Key`, `Title`, `BuildAsync(ClientReportContext)`); `delivery` and `feedback` providers ship with this module.

## Briefs, messages, meetings

Under `/api/v1/agency` (`clients.view`).

| Method & path | Permission | Notes |
|---|---|---|
| `GET /briefs`, `POST /briefs`, `GET /briefs/{id}` | clients.view / deliverables.submit | Answers validated against the brief template fields |
| `POST /briefs/{id}/status` | projects.manage | `InReview`, `Accepted`, `Declined` |
| `POST /briefs/{id}/convert` | projects.manage | Creates a project (from template) or tasks/deliverables in an existing project; once only (`brief.already_converted`) |
| `GET /clients/{clientId}/threads/paged?page=&pageSize=&search=` | clients.view | The client's threads a page at a time (`PagedResult`, pageSize ≤ 200), most recent activity first with the id as tie-breaker, so pages never repeat or skip a thread; `search` matches the subject or the last message. The web app uses this. `GET .../threads` still returns a bare list of the 200 most recent threads (kept for older clients) and now sends the full count in `X-Total-Count`. |
| `GET/POST /clients/{clientId}/threads`, `GET .../threads/{threadId}`, `POST .../messages` | clients.view | Per-client threads. `POST` takes `{ subject, body, projectId?, attachmentFileIds, isInternal }`. `isInternal: true` creates a staff-only thread (set once, audited `message.internal_thread_created`). It is never listed or returned to client users (404 by id), its messages notify only the account team, and its attachments stay staff-only. Summaries and threads carry `isInternal`; a thread also carries `canReply`. Opening a thread records a read receipt; `readBy` per message |
| `GET /meetings`, `POST /meetings`, `PUT /meetings/{id}` | projects.view | Agenda, notes, attendees, `actionItems` |
| `POST /meetings/{id}/action-items/{itemId}/convert` | deliverables.submit | Turns an action item into a task (once) |

## Client portal delivery

Under `/api/v1/client/orgs/{clientId}` (`client.portal`, caller must be a member). Only client-visible data is
returned: client-visible tasks, deliverables that have been sent to the client (and only sent versions),
published reports, non-internal threads/comments.

| Method & path | Duty |
|---|---|
| `GET /home` | Onboarding, awaiting approval, recent deliverables, latest report, meetings, threads, team, projects, NPS due, `canApprove` |
| `GET /projects`, `GET /projects/{projectId}` | member |
| `GET /deliverables`, `GET /deliverables/{id}` | member |
| `POST /deliverables/{id}/approve` | Approver/Owner — `{ version, comment? }` |
| `POST /deliverables/{id}/request-changes` | Approver/Owner — `{ version, comment }` |
| `POST /deliverables/{id}/comments` | Approver/Owner |
| `GET /reports`, `GET /reports/{id}` | member (published only) |
| `GET /brief-templates`, `GET/POST /briefs`, `GET /briefs/{id}` | Approver/Owner to submit |
| `GET /threads/paged?page=&pageSize=&search=` | Paged threads as for staff (non-internal only); `GET /threads` is the first 200 plus `X-Total-Count` |
| `GET/POST /threads`, `GET /threads/{threadId}`, `POST /threads/{threadId}/messages` | Any member reads; only Approver/Owner write (Viewer and Billing: `403 client.insufficient_role`). Internal threads are never returned (404). `isInternal: true` from a client: `400 message.internal_not_allowed` |
| `GET /meetings` | member |

## Background jobs

| Job | Interval | Idempotency |
|---|---|---|
| `RecurringTaskJob` | 1 h | Unique `RecurrenceKey` (rule + occurrence date) per generated task |
| `DeliverableSlaJob` | 15 min | `DeliveryDispatchKey` per deliverable/version/kind for reminders; conditional update for auto-approve |
| `MonthlyReportDraftJob` | 6 h | Unique `AutoKey` (client + month) — one draft per active client per month, created from the 1st |

## Notification links

Notification `link` values point at frontend routes; they are listed in
`frontend/src/features/agency/shared/deliveryLinks.fixture.json` and verified by
`DeliveryLinksTests` (backend) and `delivery.test.tsx` (frontend route matching).
