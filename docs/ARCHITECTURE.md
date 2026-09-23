# Optimize All — Architecture & Conventions

Optimize All runs paid social sharing campaigns. Participants share company-approved content from their
established social accounts, submit proof, and are paid for qualifying posts.

```
frontend/  React 18 + TypeScript (Vite). One app with five role portals.
backend/   ASP.NET Core 8 Web API (C#), EF Core 8 + Pomelo MySQL provider, MySQL 8.
docs/      Architecture, API, deployment and operations docs.
deploy/    Docker Compose staging stack, nginx config.
```

## Backend layout

| Project | Contents |
|---|---|
| `OptimizeAll.Domain` | Entities, enums and **pure** business rules (eligibility, reward calculation, payout periods, URL normalization, money rounding). No I/O. Unit-tested. |
| `OptimizeAll.Infrastructure` | `AppDbContext`, EF configurations (`Persistence/Configurations/*`), migrations. |
| `OptimizeAll.Api` | `Program.cs`, cross-cutting services (`Common/*`), and feature modules (`Modules/<Module>/`). |
| `tests/OptimizeAll.UnitTests` | xUnit tests of Domain rules and pure services. |
| `tests/OptimizeAll.IntegrationTests` | `WebApplicationFactory` tests against a real MySQL database. |

### Modules

Each module lives in `Api/Modules/<Name>/` and owns its controllers, services, DTOs, jobs and event
handlers. It registers everything in `<Name>Module.Add<Name>Module(...)`, which `Program.cs` already calls.
Modules talk to each other through:

* **Shared entities** in `AppDbContext` (read freely; write only what your module owns).
* **Cross-cutting services** in `Api/Common` (see below).
* **Domain events** (`Domain/Events`) published with `IEventPublisher` *after commit*; other modules
  implement `IEventHandler<TEvent>` and register it with `services.AddScoped<IEventHandler<T>, Handler>()`.

### Cross-cutting services (`Api/Common`)

| Service | Purpose |
|---|---|
| `ICurrentUser` | Caller id, roles, permissions, IP. `Require(permission)` throws 403. |
| `[HasPermission(Permissions.X)]` | Endpoint authorization. **Authorize by permission, never by role.** Role→permission map: `Common/Security/Permissions.cs`. Default deny: the fallback policy requires a signed-in user, so public endpoints need an explicit `[AllowAnonymous]` (list in SECURITY.md § 2). |
| `IAuditLogger` | `Record(action, entityType, id, before, after, reason)` stages an append-only audit row saved in the same `SaveChanges` as the change. Required for campaign edits, reviews, reward changes, payout actions, suspensions, settings. |
| `ILedgerWriter` | The **only** way to create/approve/decline/reverse `EarningEntry` rows. Idempotent by key; converts to settlement currency and stores original amount + rate. |
| `IPayoutScheduleProvider`, `IExchangeRateProvider` | Active payout schedule (biweekly default) and FX lookup. |
| `INotificationService` | `StageAsync(new NotificationRequest(...))` adds an in-app notification + outbox rows (Email/WhatsApp) in the caller's transaction. |
| `AppLinks` | The only source of web-app paths for notification/email/onboarding links (`AppLinks.Submission(id)`, `AppLinks.FinanceBatch(id)`, …). Never hard-code `"/app/..."`; add a member here and to `frontend/src/app/appLinks.fixture.json` (both sides are tested). |
| `ImageUrlPolicy` | Image URL rule for hero/asset/banner images: uploads (`/api/v1/files/{id}`) or https on `Content:AllowedImageHosts` (mirrors the web CSP). |
| `IEmailSender` | Direct transactional email (auth flows). Other messages go through the notification outbox. |
| `ISettingsService` | Admin-editable settings (`Domain/Settings/SettingKeys`). |
| `IEventPublisher` | Publish domain events after commit. |
| `JobRunner` / `IJob` / `services.AddRecurringJob<TJob>(interval)` | Background jobs with DB lease, run log (`job_runs`) and safe retry. |
| `PageQuery` / `PagedResult<T>` / `ToPagedAsync` | Standard list endpoints. `PagingExtensions.LikePattern` escapes search. |
| `Csv.File(...)` | CSV exports with formula-injection protection. |
| `RateLimitPolicies` | `[EnableRateLimiting(RateLimitPolicies.Submissions)]` etc. |

### API conventions

* Routes: `/api/v1/<area>/...`. Participant self-service lives under `/api/v1/me/...`; staff areas under
  `/api/v1/admin/...`, `/api/v1/review/...`, `/api/v1/finance/...`, `/api/v1/marketing/...`.
* JSON is camelCase; enums serialize as strings; timestamps are UTC ISO-8601 (`...Z`).
* Validation: DataAnnotations on request DTOs (automatic 400) plus business checks throwing
  `DomainException(code, message, kind)`. The global handler maps it to RFC 7807 with `code` and `traceId`.
* Optimistic concurrency: entities implementing `IConcurrencyStamped` get a new `ConcurrencyStamp` on each
  update. Mutating staff endpoints accept the stamp the client last saw and reject stale writes with 409.
* Race-sensitive transitions (claiming a submission, deciding it, recording a payment, including earnings
  in a batch) use **conditional updates** (`ExecuteUpdateAsync` with the expected current state in the
  `WHERE`) and/or unique indexes, so two actors or a retried job can never both succeed.
* Money: `decimal` with `DECIMAL(19,4)`; round with `Money.Round(amount, currency)` (minor units, away from zero).
  Every amount travels with its ISO currency code.
* Sensitive actions (reward-rate changes, batch finalization, payment settings, suspensions) require the
  specific permission, `"confirm": true` in the body, and a `reason`, and are audited.

### Data rules

* `EarningEntry` is immutable after insert (amount, currency, rate, rule version, keys). Corrections are new
  Adjustment/Reversal entries. `AuditLog` is append-only. Both are enforced in `AppDbContext`.
* Submissions capture `RewardRuleSetId/Version` when created; approval computes earnings from that version.
* Unique indexes: canonical per-platform post key (binary collation; a post can be claimed once), `(Platform, NormalizedHandle)` social
  profiles, earning idempotency keys, one payout item per user per batch, batch idempotency key per period.
* Schema changes: edit the entity + its `IEntityTypeConfiguration`, then add a migration:
  `dotnet ef migrations add <Name> -p src/OptimizeAll.Infrastructure -s src/OptimizeAll.Api -o Persistence/Migrations`.

## Frontend layout

```
frontend/src/
  app/          router, providers, route guards, portal layouts
  components/   design system (ui/), shared widgets (DataTable, forms, charts, status badges)
  lib/          api client (auth + refresh), formatting (money/dates), hooks
  features/     participant/, reviewer/, campaigns/ (manager), finance/, admin/, auth/, public/
```

* Server state via TanStack Query; the API client keeps the access token in memory and refreshes it through
  the HttpOnly refresh cookie (`POST /api/v1/auth/refresh` with `X-Requested-With`).
* All business rules (eligibility, rewards, payouts) come from the API. The UI never computes money.
* Accessibility: semantic HTML, labelled controls, focus management in dialogs, WCAG AA contrast, keyboard
  support. Responsive from 360px wide.
