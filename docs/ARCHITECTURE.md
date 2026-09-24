# Optimize All — Architecture & Conventions

Optimize All runs paid social sharing campaigns. Participants share company-approved content from their
established social accounts, submit proof, and are paid for qualifying posts.

```
frontend/  React 18 + TypeScript (Vite). One app with five role portals.
backend/   ASP.NET Core 8 Web API (C#), EF Core 8 on MySQL 8 (Pomelo) or SQLite (Database:Provider).
docs/      Architecture, API, deployment and operations docs.
deploy/    Docker Compose staging stack, nginx config.
```

## Backend layout

| Project | Contents |
|---|---|
| `OptimizeAll.Domain` | Entities, enums and **pure** business rules (eligibility, reward calculation, payout periods, URL normalization, money rounding). No I/O. Unit-tested. |
| `OptimizeAll.Infrastructure` | `AppDbContext`, EF configurations (`Persistence/Configurations/*`), MySQL migrations. |
| `OptimizeAll.Infrastructure.Sqlite` | SQLite migrations only (same model). |
| `OptimizeAll.Api` | `Program.cs`, cross-cutting services (`Common/*`), and feature modules (`Modules/<Module>/`). |
| `tests/OptimizeAll.UnitTests` | xUnit tests of Domain rules and pure services. |
| `tests/OptimizeAll.IntegrationTests` | `WebApplicationFactory` tests against a real MySQL database, or a SQLite file with `OPTIMIZEALL_TEST_PROVIDER=Sqlite`. |

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
| `ICurrentUser` | Caller id, roles, **effective** permissions (built-in + custom roles, via `IPermissionResolver`), IP. `Require(permission)` throws 403. |
| `IPermissionResolver` | Effective permissions of the caller (memoized per request; custom-role part cached per instance keyed by user + `User.PermissionVersion`) or of any user (`ForUserAsync`). Used by `[HasPermission]`, `ICurrentUser` and the session DTO. Never compute permissions with `RolePermissions.For(...)` outside it. |
| `IPermissionDirectory` | "Who holds permission X": `UsersWithPermissionAsync(permission)` / `UsersWithAnyPermissionAsync(...)` return a composable `IQueryable<User>` of built-in **and custom-role** holders (add `Status == Active` yourself); `UserHasPermissionAsync(userId, permission)` for assignee checks. Use it for recipients, assignees and owners — never `u.Roles.Any(...)` on a role list derived from permissions. |
| `[HasPermission(Permissions.X)]` | Endpoint authorization. **Authorize by permission, never by role.** Role→permission map: `Common/Security/Permissions.cs`. Default deny: the fallback policy requires a signed-in user, so public endpoints need an explicit `[AllowAnonymous]` (list in SECURITY.md § 2). |
| `IImpersonationContext` / `[DeniedWhileImpersonating]` | Whether the caller is a staff member "viewing as" this user (impersonation, SECURITY.md § 2.1). Put `[DeniedWhileImpersonating]` on any new high-risk endpoint (credentials, email/2FA, payout destinations, payments, API keys). |
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
* JSON is camelCase; enums serialize as strings; timestamps are UTC ISO-8601 (`...Z`). Request bodies may send an enum as its
  name or its (defined) number; a value that is not a member of the enum is a 400 (`DefinedEnumJsonConverterFactory`).
* Validation: DataAnnotations on request DTOs (automatic 400) plus business checks throwing
  `DomainException(code, message, kind)`. The global handler maps it to RFC 7807 with `code` and `traceId`.
  Every other problem gets them too (`ProblemDefaults`: `validation_failed` for field errors, else `http_<status>`;
  bodiless 401/403/404/405/415 answers are written as problems by the status-code pages). Rules every request value
  obeys without per-DTO attributes: undefined enum values (`"platform": 999`) are a 400 (`DefinedEnumJsonConverter`);
  dates outside 1900–2200, `null` list items (unless the item type is nullable) and a missing `[Required] JsonElement`
  are 400s (`RequestValueValidatorProvider`); JSON errors never echo serializer messages. On decimal/long properties use
  `[Range(typeof(decimal), "0", "1000")]`, never the Int32 overload (it throws on large values; guarded by a test).
  Authorize in metadata, not in the handler: "any of" checks use `[RequireAnyPermission]`.
* API contract suite (`IntegrationTests/Contract`, run with `--filter FullyQualifiedName~Contract`): enumerates every
  endpoint and checks each built-in role against its permission metadata, a hostile-input matrix (never a 5xx, 4xx are
  problems) against unknown and existing records, and cross-tenant access. New endpoints are covered automatically.
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
* Indexes follow queries: see [DATABASE.md](DATABASE.md) (index catalogue, guard test `PerformanceIndexTests`,
  retention of high-volume tables by `DataRetentionJob`).
* Schema changes: edit the entity + its `IEntityTypeConfiguration`, then add the migration for **both** providers
  with one command: `scripts/regenerate-migrations.sh --add <Name>` (see "Database portability" below).

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

## Agency platform conventions (marketing agency operating system)

Optimize All is a full-service marketing agency platform. The influencer/UGC sharing campaigns above are one service
line; the agency modules below run everything else (website, CRM, billing, delivery and marketing execution).

### Database portability (MySQL and SQLite)

The API runs on **MySQL 8** or **SQLite** (`Database:Provider` = `MySql` (default) | `Sqlite`):

* MySQL: `ConnectionStrings:Default`, or `Database:Host/Port/Name/User/Password`. Multi-instance capable.
* SQLite: `Database:SqlitePath` (e.g. `/app/storage/db/optimizeall.db`; wins), or a SQLite `ConnectionStrings:Default`
  (`Data Source=…`). The directory is created if missing. Every connection runs with `journal_mode=WAL`,
  `busy_timeout=10000`, `foreign_keys=ON`, `synchronous=NORMAL` (`SqlitePragmaInterceptor`). **Exactly one API
  instance** may use a SQLite file: named locks are in-process. The Data Protection key ring is stored next to the
  database file (`<name>-keys/`) instead of in it; back up both.

Rules for all code (other agents included):

* **No raw SQL in modules.** Use LINQ / `ExecuteUpdateAsync` / `ExecuteDeleteAsync`. For locking use
  `IDatabaseDialect` (`Common/Persistence/DatabaseDialect.cs`; inject it, or `db.Dialect()` in static helpers):
  * `BeginWriteTransactionAsync(db, ct[, isolationLevel])` for **every transaction that writes** (SQLite:
    `BEGIN IMMEDIATE`, which takes the single database write lock up front and waits up to the busy timeout).
  * `LockRowAsync(db, "table_name", id, ct[, RowLockMode.Share])` instead of `SELECT … FOR UPDATE/SHARE` (SQLite:
    existence check; throws outside a transaction). Returns false when the row doesn't exist.
  * `AcquireNamedLockAsync(db, name, timeout, ct)` instead of `GET_LOCK`. Acquire it **before** beginning the
    transaction and dispose it after (on SQLite, waiting for it while holding the write lock would deadlock; this
    throws `InvalidOperationException`).
  * `IsUniqueViolation(ex)` / `DatabaseErrors.IsUniqueViolation(ex)` / `ProblemExceptionHandler.IsUniqueViolation(ex)`
    all recognize MySQL and SQLite duplicates. Insert-if-absent = insert and catch the unique violation (EF wraps
    `SaveChanges` inside a transaction in a savepoint, so only that insert is undone); detach the failed entity.
* On SQLite a write transaction must not wait for **another** DbContext/connection that writes (e.g. a service
  that opens its own scope and saves): that is a self-deadlock until the busy timeout. Use the same context.
* **No provider-specific column types or collations.** Do not call `HasColumnType("json"|"text"|...)` or
  `UseCollation(...)` directly in new configurations; use `HasJsonList()` for list columns and give long text a
  `HasMaxLength` (unbounded text: leave max length unset). Keep decimals as `decimal`. (Existing MySQL-only details —
  `json`/`text`/`char(36)`, `utf8mb4_bin`, charset, check-constraint SQL — are stripped or rewritten for SQLite by
  `Infrastructure/Persistence/PortableModel.cs`; SQLite's default `BINARY` collation is case-sensitive.)
* Check constraints: write them in MySQL syntax with backtick identifiers; `PortableModel` rewrites them for SQLite
  (double quotes, `CHAR_LENGTH` → `LENGTH`, decimal columns compared via `CAST(… AS REAL)`).
* Keep queries translatable on both providers (no MySQL-only functions, no `DateTime` arithmetic inside SQL that
  SQLite cannot translate; compute boundaries in C# and compare). `EF.Functions.Like(x, PagingExtensions.LikePattern(q), "\\")`
  — pass the escape character; SQLite has no default one.
* Paged lists end their `ORDER BY` with a unique key (`.ThenBy(x => x.Id)`, or `ThenByKey` after a sort switch): rows
  that tie on the sort key (same `CreatedAt`, name, status…) otherwise come back in a different order for each
  `LIMIT/OFFSET` on MySQL, so pages repeat some rows and never show others. `PageQuery.Skip` is capped instead of
  overflowing for far-out pages, and a `[FromQuery]` object must not be bound under the name of one of its own
  properties (`PageQuery page` makes MVC ignore `?page=`/`?pageSize=`); `QueryBindingTests` guards this.
* Money on SQLite: decimals are stored as TEXT. EF Core 8 translates decimal arithmetic/comparisons itself;
  `Common/Persistence/SqliteQuerySupport.cs` adds exact `Sum`/`Average`/`Min`/`Max` (computed in .NET `decimal`),
  decimal `OrderBy` (exact collation) and `Guid.NewGuid()` inside `ExecuteUpdate`. Values read back with their column
  scale, as on MySQL. Never convert money to `double`.
* Tests: the whole integration suite runs on both providers (`OPTIMIZEALL_TEST_PROVIDER=Sqlite`, or
  `scripts/test-all.sh --sqlite`). A test that must inspect provider-specific details checks `ApiFactory.IsSqlite`.

**Migrations** exist per provider: MySQL in `OptimizeAll.Infrastructure/Persistence/Migrations`, SQLite in
`OptimizeAll.Infrastructure.Sqlite/Migrations` (`MigrationsAssembly` is chosen per provider in
`DatabaseConnection.Configure`). One command regenerates/extends both and verifies them (no database server needed):

```bash
scripts/regenerate-migrations.sh              # delete both sets, recreate a single InitialCreate (pre-release)
scripts/regenerate-migrations.sh --add <Name> # add migration <Name> to both sets
scripts/regenerate-migrations.sh --check      # has-pending-model-changes for both (CI runs this)
```

Manually (from `backend/`), the design-time provider is selected through configuration:

```bash
dotnet ef migrations add <Name> -p src/OptimizeAll.Infrastructure -s src/OptimizeAll.Api -o Persistence/Migrations
Database__Provider=Sqlite Database__SqlitePath=/tmp/design.db \
  dotnet ef migrations add <Name> -p src/OptimizeAll.Infrastructure.Sqlite -s src/OptimizeAll.Api -o Migrations
```

### Client tenancy

`ClientAccount` (`Domain/Agency/ClientAccount.cs`) is a client organization; `ClientMember` links users with the
`Client` role to it with a duty (`Viewer`, `Approver`, `Billing`, `Owner`). Every client-owned entity has a
`ClientAccountId`. **All** reads/writes of client-owned data go through `IClientScope`
(`Common/Security/ClientScope.cs`): staff with `clients.view` see all clients; client users only their
organizations (other tenants' records answer 404). Client-portal endpoints live under `/api/v1/client/...` and
require `client.portal`; agency staff endpoints live under `/api/v1/agency/...`.

### Roles

Agency staff roles: `AccountManager`, `Strategist`, `ContentCreator`, `Designer`, `SeoSpecialist`, `AdsSpecialist`,
`SocialMediaManager`, `SalesRep` (plus `Admin`, `Finance`, `CampaignManager`); client users have `Client`. The
role → permission map is in `Common/Security/Permissions.cs` (frontend mirror: `lib/auth/permissions.ts`).

**Custom roles** (`Domain/Identity/CustomRole.cs`: `CustomRole` + `UserCustomRole`, tables `custom_roles`,
`user_custom_roles`) are admin-defined permission bundles managed in `Modules/Admin/Roles` (`/api/v1/admin/roles`,
`roles.manage`; UI `features/admin/roles`). Effective permissions = built-in ∪ custom (see `IPermissionResolver` above
and SECURITY.md § 2 "Custom roles" for the guardrails). When you add a permission constant, also add it to
`Common/Security/PermissionCatalog.cs` (area, label, description; a unit test enforces it) and to the frontend mirror.
The web app decides portal and section access **only** from the session's `permissions`, so a custom role with e.g.
just `crm.view` lands in the agency portal and sees only the CRM sections.

### Frontend

The agency staff portal (`/agency`) aggregates per-area route modules in `features/agency/<area>/routes.tsx`
(`nav`, `routes`, `opensWith`); the client portal (`/client`) aggregates `features/client/<area>/routes.tsx`. The
public marketing website lives in `features/public/**`.

Every routed page is code-split: route modules reference pages through `lazyPage(() => import('./pages/X'), 'X')`
(`app/lazyPage.tsx`, React.lazy + Suspense), so `element: <X />` and the permission guards that wrap it stay unchanged
while visitors of the public site download ~107 KB (gzip) of shared code instead of every portal. Public-website
routes use the router's `lazy` field. Add new pages the same way.

### Third-party integrations

External platforms (Meta/Instagram/Facebook, X, LinkedIn, TikTok, YouTube, Google Ads, Meta Ads, SMS/WhatsApp
providers, SEO data providers, payment gateways) are reached only through adapter interfaces whose default
implementation reports **"not configured"** and never pretends to have published, sent or synced anything.
Credentials are stored encrypted (Data Protection) and managed under `integrations.manage`.
