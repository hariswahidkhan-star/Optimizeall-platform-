# Optimize All — Security

This document describes the security model of the platform and the controls operators must keep in place.
Configuration keys are documented in [`/.env.example`](../.env.example); deployment hardening in
[DEPLOYMENT.md](DEPLOYMENT.md).

## 1. Authentication

* **Passwords** are hashed with ASP.NET Core Identity's `PasswordHasher` (PBKDF2-HMAC-SHA512, per-user salt,
  high iteration count; hashes are upgraded transparently when parameters change). Policy
  (`Modules/Auth/PasswordPolicy.cs`, NIST 800-63B style): at least 10 characters, not in a common-password
  list, not overly repetitive, must not contain the email's local part. No forced composition rules or
  periodic rotation.
* **Email verification**: a participant must verify their email before becoming eligible for campaigns
  (enforced by the eligibility rules). Verification and password-reset links carry single-use, expiring tokens
  stored only as hashes (`user_tokens`; refresh tokens likewise). Registration, reset and sign-in responses do
  not reveal whether an email exists: registering an existing email sends that address a "someone tried to
  register with your email" notice, and sign-in for an unknown email performs a dummy hash to equalize timing.
* **Lockout**: repeated failed sign-ins lock the account for 15 minutes (audited as `auth.locked_out`); combined
  with per-IP rate limits this blocks online guessing and credential stuffing.
* **Tokens**:
  * *Access token*: short-lived JWT (HS256, `Jwt__AccessTokenMinutes`, default 15 min) with issuer/audience
    validation and 30 s clock skew. The SPA keeps it **in memory only** (never localStorage).
  * *Refresh token*: random opaque token, stored **hashed** in `refresh_tokens`, sent as cookie `oa_refresh`
    with `HttpOnly`, `Secure` (production), `SameSite=Strict`, `Path=/api/v1/auth`, lifetime
    `Jwt__RefreshTokenDays` (default 14). Every refresh **rotates** the token; presenting an already-rotated
    token is treated as theft (**reuse detection**): the whole session family is revoked and a warning logged.
  * Every authenticated request re-checks the user's status and `SecurityVersion`; suspending a user,
    changing the password or "sign out everywhere" invalidates existing access tokens immediately, not at expiry.
* Staff accounts should use strong unique passwords; enforcing SSO/MFA for staff at the identity layer is
  recommended when available.

## 2. Authorization (permission-based RBAC)

Endpoints authorize **by permission, never by role** (`[HasPermission(Permissions.X)]`); roles are bundles of
permissions (`backend/src/OptimizeAll.Api/Common/Security/Permissions.cs`). Participants can only access their
own data (`/api/v1/me/...` resolves the caller from the token; object ids are always checked against the
caller). Role grants are audited and require `roles.assign`.

| Permission | Participant | Reviewer | Campaign manager | Finance | Admin |
|---|:-:|:-:|:-:|:-:|:-:|
| `participant.portal` | ✓ | | | | ✓ |
| `campaigns.view` | | ✓ | ✓ | ✓ | ✓ |
| `campaigns.manage` | | | ✓ | | ✓ |
| `campaigns.publish` | | | ✓ | | ✓ |
| `rewards.edit` *(sensitive)* | | | ✓ | | ✓ |
| `rewards.approve_bonus` | | | ✓ | ✓ | ✓ |
| `submissions.review` | | ✓ | | | ✓ |
| `submissions.reverse` | | | | ✓ | ✓ |
| `appeals.resolve` | | ✓ | | | ✓ |
| `review.assign` | | | ✓ | | ✓ |
| `social.verify` | | ✓ | | | ✓ |
| `ledger.view` | | | | ✓ | ✓ |
| `ledger.adjust` | | | | ✓ | ✓ |
| `payouts.view` | | | | ✓ | ✓ |
| `payouts.prepare` | | | | ✓ | ✓ |
| `payouts.finalize` *(sensitive)* | | | | ✓ | ✓ |
| `payouts.record_payment` | | | | ✓ | ✓ |
| `payouts.hold` | | | | ✓ | ✓ |
| `payouts.settings` *(sensitive)* | | | | ✓ | ✓ |
| `marketing.manage` | | | ✓ | | ✓ |
| `analytics.view` | | | ✓ | ✓ | ✓ |
| `users.view` | | ✓ | ✓ | ✓ | ✓ |
| `users.manage` | | | | | ✓ |
| `users.suspend` | | | | | ✓ |
| `roles.assign` | | | | | ✓ |
| `content.manage` | | | | | ✓ |
| `settings.manage` | | | | | ✓ |
| `support.manage` | | ✓ | | | ✓ |
| `audit.view` | | | | ✓ | ✓ |
| `jobs.view` | | | | | ✓ |

Keep this table in sync with `Permissions.cs` when permissions change. Sensitive actions (reward-rate changes,
batch finalization, payout settings, suspensions) additionally require an explicit `"confirm": true` and a
`reason`, and are audited. Grant Admin sparingly; day-to-day staff should hold the narrowest role.

## 3. Rate limiting and abuse controls

* Global token bucket: 300 requests/min per client IP.
* `auth` policy: 10 requests/min per IP on credential endpoints (`RateLimiting__AuthPerMinute`).
* `submissions` policy: 30 writes/min per user for actions that create staff work (submissions, tickets, appeals).
* `public` policy: 120 requests/min per IP for unauthenticated endpoints (landing pages, `/t/{code}`, postbacks).
* Rejections return `429` with `Retry-After`. Limits are per API instance (in memory). The client IP comes from
  `X-Forwarded-For` set by the trusted nginx tier — see DEPLOYMENT.md § 5.2; a misconfigured proxy that hides
  client IPs makes all users share one bucket.
* Business anti-abuse: a post URL can be claimed only once (unique normalized URL), social profiles are unique
  per platform, self-referral is impossible, fraud signals use hashed IP/device ids.

## 4. Input validation and output encoding

* Request DTOs are validated with DataAnnotations (automatic 400 with field errors); business rules throw
  `DomainException`, mapped to RFC 7807 problem details with a stable `code` and `traceId`. Unhandled errors
  return a generic 500 without stack traces or internal details.
* **SQL injection**: all data access goes through EF Core with parameterized queries; `LIKE` searches escape
  wildcards (`PagingExtensions.LikePattern`); no string-concatenated SQL.
* **XSS**: the React SPA renders text through JSX escaping (no `dangerouslySetInnerHTML` for user content); the
  web tier sends a strict Content-Security-Policy (`script-src 'self'`, no inline scripts,
  `object-src 'none'`, `frame-ancestors 'none'`); the API sends `default-src 'none'` on its responses,
  `X-Content-Type-Options: nosniff` and `Cache-Control: no-store` on `/api`.
* **CSV exports** neutralize formula injection (`Csv.File`: non-numeric cells starting with `=`, `+`, `-`, `@`,
  tab or CR are prefixed with `'`).
* **Open redirects**: tracking links (`/t/{code}`) redirect only to the destination stored for that link by
  staff, never to a URL taken from the request. Post-login "return to" redirects in the SPA must accept only
  same-origin relative paths.
* **Uploaded URLs** (post links) are normalized and validated per platform before storage.

## 5. File uploads and private storage

* Screenshot/proof uploads are limited to 12 MB per request (API multipart limit and nginx
  `client_max_body_size`), restricted to image types, and verified by content (magic bytes), not by the
  client-supplied name or MIME type.
* Files are stored under `Storage__RootPath` with **server-generated keys**; client file names are never used
  as paths (no path traversal). Storage is outside any web root and **not served statically**: files are only
  returned by authorized API endpoints (the owner and staff with review permissions), with
  `X-Content-Type-Options: nosniff` and `Cache-Control: no-store`.
* Use a private volume/bucket with encryption at rest; never make it public.

## 6. Encryption of payout destinations

* Participants' payout details (bank account, wallet, PayPal email...) are encrypted at rest with **ASP.NET Core
  Data Protection** (authenticated encryption, AES-256-CBC + HMACSHA256 by default); only a masked summary is
  stored in clear for display. Decryption happens only for finance workflows (payment instructions export),
  which are permission-gated and audited.
* The data-protection **key ring is stored in MySQL** (`data_protection_keys`) so all instances share it; keys
  rotate automatically (90 days) and old keys remain for decryption.
* **Production recommendation:** protect the key ring itself (the API logs "No XML encryptor configured" until
  this is done), e.g. `ProtectKeysWithCertificate(...)`, Azure Key Vault (`ProtectKeysWithAzureKeyVault`) or an
  AWS KMS-backed XML encryptor, so a database dump alone cannot decrypt payout data. Until then, database
  backups must be treated as containing decryptable financial data (encrypted, access-restricted).
* Database connections should use TLS (`SslMode=Required`/`VerifyFull`); managed MySQL storage encryption on.

## 7. Secrets management

* Secrets: `ConnectionStrings__Default`, `Jwt__SigningKey`, `Security__HashSalt`, `Tracking__PostbackSecret`,
  `Email__SmtpPassword`, `WhatsApp__AccessToken`, `Bootstrap__AdminPassword`. Generate with
  `scripts/generate-secrets.sh --aspnet`; store in a secret manager; inject as environment variables or files
  (`*_FILE`, supported by the API image). Never commit them; `.env*` files are git-ignored.
* Development values in `appsettings.Development.json` are public and must never be used elsewhere. The API
  refuses non-SMTP email in Production; Swagger and the dev mailbox are off by default outside development.
* Rotation: JWT signing key (signs everyone out — schedule it), SMTP/WhatsApp credentials (no user impact),
  postback secret (coordinate with advertisers), DB password (rolling restart). `Security__HashSalt` should
  not be rotated (existing hashes would no longer match).
* Remove `Bootstrap__AdminPassword` after the first administrator has signed in and changed the password.

## 8. CSRF, CORS and headers

* The access token travels in the `Authorization` header (not a cookie), so ordinary API calls are not
  CSRF-able. The only cookie is the refresh cookie: `SameSite=Strict`, scoped to `/api/v1/auth`, and the refresh
  endpoint additionally requires the `X-Requested-With` header (a simple cross-site form cannot set it).
* Same-origin deployment (nginx proxies the API) needs no CORS. If the SPA must run on another origin, list it
  explicitly in `Security__AllowedOrigins__N` — never `*` (credentials are allowed for listed origins).
* Headers: HSTS on HTTPS (API; enable for the SPA in nginx once HTTPS-only), CSP, `X-Frame-Options: DENY`,
  `Referrer-Policy: strict-origin-when-cross-origin`, `Permissions-Policy` disabling camera/microphone/geolocation,
  `Cross-Origin-Opener-Policy: same-origin`.

## 9. Audit trail

* `audit_logs` is **append-only** (updates/deletes are rejected in `AppDbContext`). Entries record actor, action,
  entity, before/after snapshots, reason, the actor's IP address, correlation id and timestamp, and are written
  in the **same transaction** as the change they describe.
* Audited: campaign edits and publishing, reward rule changes, review decisions and reversals, appeals, ledger
  adjustments, payout batch actions (prepare, hold, finalize, record payment, cancel), payout settings, role
  grants, suspensions, settings changes, lockouts.
* Viewable/exportable by `audit.view`. Retain at least as long as financial records. For stronger tamper
  evidence, ship audit rows to a write-once store (e.g. object storage with object lock) as well.

## 10. Data minimization and privacy

* IP addresses and device identifiers used for fraud signals and click tracking are stored only as **keyed
  HMAC-SHA256 hashes** (`Security__HashSalt`), enough for fraud detection and click de-duplication without
  keeping raw values. The one exception is the audit log, which keeps the acting user's IP address for
  accountability of privileged actions.
* Payout details are encrypted (§ 6); logs never contain passwords, tokens or payout details.
* Collect only what campaigns need (country, language, interests, social handles); retention periods are in
  [OPERATIONS.md § 7](OPERATIONS.md#7-data-retention). Account deletion anonymizes personal data while keeping
  legally required financial/audit records.

## 11. Financial integrity controls

* **Immutable ledger**: `EarningEntry` rows cannot be modified after insert (amount, currency, rate, rule
  version, keys); corrections are new Adjustment/Reversal entries. Only `ILedgerWriter` creates or transitions
  earnings.
* **Idempotency keys** on earnings (unique index) and on payout batches per period: retries and duplicate job
  runs cannot create money twice. Payment providers receive the payout item's idempotency key unchanged.
* **Unique constraints**: one claim per normalized post URL; one payout item per user per batch; unique
  social profiles per platform.
* **Conditional updates**: race-sensitive transitions (claiming/deciding a submission, including earnings in a
  batch, recording a payment) are `UPDATE ... WHERE <expected state>`, so two actors or a retried job can never
  both succeed; optimistic concurrency stamps reject stale staff edits with 409.
* **Four-eyes**: the person who prepared/regenerated a payout batch cannot finalize it; whoever recorded a
  manual bonus or adjustment cannot approve it. Sensitive actions require confirmation and a reason.
* **Versioned reward rules**: submissions capture the rule-set version at creation; approvals compute
  earnings from that version, so rate changes never retroactively alter pending work.
* Money uses `DECIMAL(19,4)` and currency-aware rounding; every amount carries its ISO currency; FX conversions
  store the original amount and rate.
* Generating or finalizing a batch never marks anything paid; payments are recorded per item with an external
  reference and reconciled against bank statements (OPERATIONS.md § 2).

## 12. Infrastructure hardening (summary)

* Containers run as non-root (API uid 1654, nginx uid 101), with read-only root filesystems, all capabilities
  dropped and `no-new-privileges` in the production example; images are minimal Alpine variants.
* Only the TLS proxy is internet-facing; API, web and database live on private networks.
* CI runs dependency checks (`.github/workflows/security.yml`: dependency review on PRs, NuGet
  `--vulnerable`, `npm audit --audit-level=high`, weekly schedule). Keep base images patched by rebuilding
  regularly.

## 13. Reporting a vulnerability

Please report suspected vulnerabilities privately to **security@optimizeall.app** (or via GitHub's
"Report a vulnerability" private advisory form on this repository). Include affected endpoint/component,
reproduction steps and impact. Do not access other users' data, run denial-of-service tests or use social
engineering. We acknowledge reports within 3 business days, keep you informed, and credit reporters who wish
to be named once a fix is released.
