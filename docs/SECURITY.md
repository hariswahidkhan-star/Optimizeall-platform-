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

### 1.1 Sign in with Google

Optional OpenID Connect sign-in (`Modules/Auth/Google`), enabled only when `Authentication:Google:ClientId` and
`ClientSecret` are configured (the secret from the environment/secret manager or user-secrets only). Otherwise
`GET /auth/providers` reports it disabled, the web app shows no button and the Google endpoints answer 404.
Setup: [DEPLOYMENT.md § 5.11](DEPLOYMENT.md#511-sign-in-with-google-optional).

* **Flow**: authorization code with **PKCE (S256)**, exchanged **server side** with the client secret; the browser never
  sees Google tokens. `POST /auth/google/start` returns the Google URL whose `state` is signed and encrypted (ASP.NET
  Data Protection, 10-minute expiry) and sets `oa_google_flow`, an HttpOnly, `SameSite=Strict`, `Secure`, 10-minute
  cookie (sent as `Max-Age`, so a browser clock running ahead of the server's does not drop it; the session cookies
  use `Max-Age` too) (path `/api/v1/auth/google`) holding the encrypted nonce and PKCE verifier. `POST /auth/google/callback`
  requires the state to verify **and** to match that cookie (so a state from another browser — login CSRF — is
  useless), deletes the cookie (single use) and exchanges the code. The redirect URI comes from configuration
  (`{Email:AppBaseUrl}/auth/google/callback`), never from the request; the post-login `returnTo` is kept only if it is
  a same-origin path (and checked again by the SPA).
* **ID token validation**: RS256 signature against Google's JWKS (cached per `Cache-Control`, 5 min – 24 h; an
  unknown key id forces at most one refresh per minute; during a JWKS outage the last good keys are kept and the
  endpoint is retried at most once a minute), issuer `accounts.google.com` / `https://accounts.google.com`,
  audience = only our client id (extra audiences, or an `azp` naming another client, are refused), expiry (60 s skew, app clock), the **nonce** of this attempt (constant-time compare),
  `email_verified = true`, and the `hd` claim when `AllowedHostedDomains` is set. `alg=none`/HMAC tokens are refused.
* **Account resolution** (`external_logins`: provider + `sub` unique, one Google identity per user; the email is
  only a snapshot at link time — the `sub` is the identity, emails can change hands):
  1. A known `(google, sub)` signs in its user.
  2. Otherwise, a user with the same email is linked **only if that user's email is verified** and the user holds
     no staff role (only Participant/Client accounts are linked automatically) **and Google is authoritative for
     the address**: a `@gmail.com`/`@googlemail.com` address or a Workspace-managed account (`hd` claim). A
     consumer Google account registered with any other address only proves that the address was verified once,
     possibly by a previous owner of the mailbox or of a lapsed/reclaimed domain. Unverified accounts are refused
     (`auth.google_link_unverified`); staff accounts and non-authoritative addresses must sign in with their
     password and link from the profile (`auth.google_link_requires_sign_in`), so a Google account cannot take over
     an account whose mailbox ownership was never proven, a privileged account, or an account whose address Google
     cannot vouch for today.
  3. Otherwise nothing is created until the user accepts the participant rules on the terms step: the callback
     returns `needsTerms` with a signed, encrypted, 15-minute ticket and `POST /auth/google/complete` creates a
     **Participant** (email verified, **no password**). A ticket can only create an account, never sign in to an
     existing one. **Staff roles are never granted through Google.**
* **Status**: suspended, deactivated and locked-out users are refused exactly as with a password, and the session
  is issued by the same `AuthService` code (short-lived JWT + rotating refresh cookie).
* **No-password accounts**: `PasswordHash` is empty; password sign-in answers like a wrong password (after a dummy
  hash) until the user sets a password through "forgot password" (which proves mailbox control).
* **Linking from the profile** (`POST /auth/external-logins/google/start`, signed in) binds the flow to the user id;
  the callback must carry the same user's session. A Google account already linked elsewhere is refused. The
  unique indexes settle concurrent links: the losing request re-reads the database and gets the same answer as a
  sequential one (`auth.google_already_linked` / `auth.google_other_account`, or simply signed in when another tab
  linked the same identity to the same user), never a bare duplicate-record error.
  **Unlinking** (`DELETE /auth/external-logins/google`) is refused while Google is the only sign-in method (no
  password, no other provider).
* **Audit**: `auth.google_sign_in`, `auth.external_login_linked` (method `verified_email_match`, `profile` or
  `sign_up`), `auth.external_login_unlinked`, and `auth.registered` for new accounts.
* **Rate limits**: start, callback and complete use the `auth` policy; the provider list uses `public`.

## 2. Authorization (permission-based RBAC)

Endpoints authorize **by permission, never by role** (`[HasPermission(Permissions.X)]`); roles are bundles of
permissions (`backend/src/OptimizeAll.Api/Common/Security/Permissions.cs`). Participants can only access their
own data (`/api/v1/me/...` resolves the caller from the token; object ids are always checked against the
caller). Built-in role grants are audited and require `roles.assign`; custom roles (below) require `roles.manage`.

**Default deny.** The authorization `FallbackPolicy` requires an authenticated user, so an endpoint without any
attribute is never public by accident. Only these endpoints carry an explicit `[AllowAnonymous]` (asserted by
`IntegrationTests/Auth/DefaultDenyTests`, which also sends an anonymous request to every other endpoint and expects
`401`):

* auth: `POST /auth/register|login|refresh|logout|verify-email|resend-verification|forgot-password|reset-password`,
  Google sign-in `GET /auth/providers`, `POST /auth/google/start|callback|complete` (`/auth/me`,
  `/auth/change-password` and `/auth/external-logins/*` require a session);
* public growth pages: `GET /public/invitations/{code}`, `GET /public/campaigns/{slug}`, `POST /public/conversions`
  (HMAC-signed), the tracking redirect `GET /t/{code}`;
* `GET /files/{id}` (the handler checks access per file, see § 5), `GET /campaign-categories`, `GET /content/faqs`,
  `GET /meta/currencies`;
* the dev mailbox `GET /dev/mailbox` (404 unless enabled outside Production), the non-production test sign-in
  `GET /dev/test-accounts` / `POST /dev/test-login` (§ 2.2) and the health checks `/health/live`, `/health/ready`.

**`users.view` for staff.** Reviewers, campaign managers and finance hold `users.view` so they can look people up
while doing their job (a reviewer checking a participant's history, finance resolving a hold, a manager answering a
partner). It opens the read-only admin user directory (profile, roles, status, social accounts, activity); it
never exposes payout details (only masked hints), password data, raw IPs or device ids, and changing anything
needs `users.manage` / `users.suspend` / `roles.assign` (admins only). The web app's admin portal is reachable with
any permission that opens one of its sections, but post-login landing there is reserved for
`settings.manage`/`content.manage`.

| Permission | Participant | Reviewer | Campaign manager | Finance | Admin |
|---|:-:|:-:|:-:|:-:|:-:|
| `participant.portal` | ✓ | | | | ✓ |
| `campaigns.view` | | ✓ | ✓ | ✓ | ✓ |
| `campaigns.manage` | | | ✓ | | ✓ |
| `campaigns.publish` | | | ✓ | | ✓ |
| `rewards.edit` *(sensitive)* | | | ✓ | | ✓ |
| `rewards.approve_bonus` | | | ✓ | ✓ | ✓ |
| `codes.view` | | | ✓ | ✓ | ✓ |
| `codes.manage` *(sensitive)* | | | ✓ | | ✓ |
| `codes.assign` *(sensitive)* | | | ✓ | | ✓ |
| `sales.review` *(sensitive)* | | ✓ | | | ✓ |
| `sales.reverse` *(sensitive)* | | | | ✓ | ✓ |
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
| `roles.manage` *(sensitive)* | | | | | ✓ |
| `users.impersonate` *(sensitive)* | | | | | ✓ |
| `content.manage` | | | | | ✓ |
| `settings.manage` | | | | | ✓ |
| `support.manage` | | ✓ | | | ✓ |
| `audit.view` | | | | ✓ | ✓ |
| `jobs.view` | | | | | ✓ |

Keep this table in sync with `Permissions.cs` when permissions change. Sensitive actions (reward-rate changes,
batch finalization, payout settings, suspensions) additionally require an explicit `"confirm": true` and a
`reason`, and are audited. Grant Admin sparingly; day-to-day staff should hold the narrowest role.

### Custom roles (dynamic RBAC)

Besides the built-in roles above (an enum; read-only in the UI), admins can define **custom roles** — named bundles of
exactly the permissions they choose — under **Admin → Roles & permissions** (`/api/v1/admin/roles`, requires
`roles.manage`). A user's **effective permissions** are the union of their built-in role permissions and the
permissions of every custom role assigned to them. Everything that checks permissions uses the effective set:
`[HasPermission]`, `ICurrentUser.HasPermission` (and so `IClientScope.IsStaff`), the session's `permissions` list (which
alone decides portal access in the web app), and "who holds permission X" lookups (`IPermissionDirectory`: ticket/CRM/
review assignees, notification recipients, delivery/ads staff pickers, "is staff" checks). **Routing** lookups — inbound
lead round-robin (`crm.manage`), payout-batch notifications (`payouts.finalize`), payment-claim notifications
(`billing.manage`) — use `WorkersWithAnyPermissionAsync`: holders through a job role (any built-in role except Admin, or a
custom role). Admins hold every permission, so they are routed work only when they also hold such a role; granting
someone Admin never puts them into the lead rotation. The session (`/auth/me`, login/refresh) also lists `customRoles`
(names) for the header badges.

* **Immediate effect.** Built-in roles travel in the access token (changing them revokes sessions, as before). Custom
  roles are resolved per request by `IPermissionResolver`: the JWT validation step reads the user's `PermissionVersion`
  with the `SecurityVersion`, and custom-role permissions are cached per instance keyed by (user, `PermissionVersion`).
  Every custom-role change (create/edit/delete a role, assign/unassign) bumps the affected users' version in the same
  transaction, so the next request on any instance sees the new set — no re-login; the web app shows it on its next
  session refresh.
* **Guardrails** (`Modules/Admin/Roles/CustomRoleGuardrails.cs`, enforced server-side):
  * nobody can grant a permission they don't hold themselves — this covers creating, editing, deleting, assigning and
    unassigning a role (a role holding a permission you lack is read-only for you);
  * `roles.manage`, `settings.manage` and `users.impersonate` can only be put into a role (or assigned through one) by a
    user holding the **built-in Admin** role, so a delegated role manager can never mint another role manager;
  * `client.portal` cannot be combined with staff permissions in one role, and a staff custom role cannot be assigned to
    a user whose effective set contains `client.portal` (or vice versa) — client users stay tenant-scoped. The same
    invariant holds for **built-in** role changes (`PUT /admin/users/{id}/roles`, staff creation, test users: 409
    `roles.client_staff_conflict`, checked against the user's custom roles too), client invitations and proposal
    signers (a user who is staff through a custom role is never given the Client role). Role assignment and built-in role
    changes lock the user row, and role edits lock the role row, so concurrent changes can't slip past each other's check;
  * built-in role grants follow the same rule: `roles.assign` (and `users.manage` for test users) only lets you grant or
    remove built-in roles whose staff permissions you hold yourself, and the Admin role only if you are an admin (403
    `roles.cannot_grant_unheld` / `roles.admin_only_permission`), so `roles.assign` held through a custom role can't mint
    admins. Suspending/reactivating anyone who is staff (built-in *or* custom role) needs the built-in Admin role;
  * only known permissions; names are unique (case-insensitive) and cannot reuse a built-in role name;
  * a role still assigned to people is deleted only with `?confirm=true` (optionally `&reassignTo={roleId}` to move the
    holders), and edits use optimistic concurrency (`concurrencyStamp`, 409 when stale).
* **Audit.** `admin.custom_role_created|updated|deleted` (entity `CustomRole`, before/after name, description and
  permissions; deletions list the holders and any reassignment) and `admin.custom_role_assigned|unassigned` (entity
  `User`, before/after list of the user's custom roles).
* The permission catalog (`GET /admin/roles/catalog`, `Common/Security/PermissionCatalog.cs`) groups every permission
  by area with a label and description; a unit test fails when a new permission has no catalog entry.
### 2.1 Impersonation ("Log in as user")

Support and QA sometimes need to see exactly what a user sees. `POST /api/v1/admin/users/{id}/impersonate`
(`users.impersonate`, Admin only by default; `reason` of 5+ characters and `"confirm": true`; the UI also asks for the
target's email to be typed) starts a time-boxed session:

* **Who can be impersonated.** Not yourself (`admin.impersonation_self`), not Admins or anyone whose effective permissions (built-in or custom roles) include
  `users.impersonate` (`admin.impersonation_target_forbidden`), not suspended/deactivated accounts
  (`admin.impersonation_target_inactive`, 409 — reactivate first). Starting a new session ends the caller's previous one.
* **Session design.** An `impersonation_sessions` row (impersonator, target, reason, SHA-256 of an opaque token,
  impersonator's security version, 60-minute hard expiry `Impersonation:SessionMinutes`, capped at 60) and a separate
  HttpOnly cookie `oa_impersonation` (same flags and path as `oa_refresh`). The staff member's own refresh cookie is
  **kept and not rotated**, so their session is preserved. Access tokens for the target carry `imp_sid` (session id),
  `act_sub` (impersonator id) and `act_name` (impersonator name — RFC 8693 "act" semantics, flattened), and expire at
  the earlier of the normal 15 minutes and the session end. `POST /auth/refresh` first resumes a live impersonation —
  only together with a live, unrevoked refresh token **of the impersonator** — otherwise drops the impersonation
  cookie and refreshes the staff member's own session. Sessions are never extended.
* **Validation on every request.** An impersonation token is accepted only while its session exists for that subject
  and impersonator, is not ended or expired, and the impersonator is still active with the same security version
  (suspending the impersonator, changing their password or signing them out ends it immediately). Because custom-role
  changes don't bump security versions, permissions are re-checked on every request and on every resume too: the
  impersonator must still hold `users.impersonate` (built-in or custom role) and the target must not have become an
  admin or impersonator; otherwise the token is refused (401) and the next refresh ends the session
  (`permissions_changed`) and returns the staff member's own session. Logging out ends it.
* **Exit.** `POST /api/v1/auth/impersonation/exit` ends the session (audited `admin.impersonation_ended`), clears the
  cookie and returns the staff member's own session from their refresh cookie (idempotent; 401 if that is gone too).
  The web app shows a persistent high-contrast banner in every layout ("You are viewing as Jane Doe (participant) —
  Exit", with TEST for test accounts); Exit lands on Admin → Users.
* **Blocked while impersonating** (403 `auth.impersonation_forbidden_action`, `[DeniedWhileImpersonating]`): changing
  the password (and any future email/2FA change endpoints must carry the attribute too), changing the payout
  destination, every write of payout batches (prepare, hold, finalize, dispatch, record payment, mark failed), payout
  schedule and holds, ledger adjustments/reversals, pending-earning approvals, exchange rates, the decrypted payment
  instructions export, invoice payment recording, client online payment, agency payments and credit notes,
  integration credentials (API keys), user/role/status administration and test-user management, and starting another
  impersonation; also linking/unlinking Google sign-in, profile changes (identity, WhatsApp number, consents), invoice,
  contract, billing-settings, tax-rate and price-catalog writes, reward rules, submission decisions/reversals and appeal
  resolutions (they create or reverse earnings), referral rejections, platform settings, the public website's site
  settings (they choose the tag-manager/analytics scripts on every public page and the canonical site URL) and manual
  job runs, social
  OAuth connections/tokens and the email provider choice, client member invitations/role changes/removals (agency and
  client portal), client proposal acceptance, and billable rates. Reads stay available so the impersonator can see what
  the user sees. `UnitTests/Admin/ImpersonationCoverageTests` scans every controller action by reflection: any write
  guarded by a money/credential/identity/role/integration permission must carry the attribute (or be allow-listed with a
  reason there), and a maintained deny list covers the endpoints that are sensitive for what they do.
* **Audit.** `admin.impersonation_started` (with the reason) and `admin.impersonation_ended` are recorded; every
  business audit row written during the session has `ActorUserId` = the user and `ImpersonatorUserId` = the staff
  member (`ActorType` "impersonation"; the audit log UI and CSV show "Admin X as User Y"); every state-changing
  request made with an impersonation token, including refused ones, is recorded as `impersonation.request` and every
  read as `impersonation.read` (method, path, query, status) on the impersonated user. Every ending is audited as
  `admin.impersonation_ended` with its reason (`exit`, `logout`, `expired`, `replaced`, `admin_session_ended`,
  `permissions_changed`).

### 2.2 Test accounts and the non-production test sign-in

* **Test users** (`users.manage`; staff roles additionally need `roles.assign`): `POST /api/v1/admin/test-users` creates
  a verified account with `User.IsTestAccount = true`, any role(s), optional client organization membership for the
  Client role, a generated email `test+<slug>-<random>@<TestAccounts:EmailDomain>` and a random password returned
  **once** (never stored in clear, never audited). The flag can only be set at creation — no endpoint changes it on an
  existing account. `DELETE /api/v1/admin/test-users/{id}` deactivates a test account (409 for real accounts).
  Admin lists filter with `?isTestAccount=true|false` and label them TEST.
* **Excluded from money and metrics.** Payout batch planning excludes test accounts first (`PayoutExclusionReason.TestAccount`:
  they are never paid and their earnings stay unattached); analytics (funnel, posts, spend, reach, clicks,
  conversions, time series) and the marketing tracking summary/leaderboard ignore test accounts and their links.
* **One-click test sign-in** (`GET /api/v1/dev/test-accounts`, `POST /api/v1/dev/test-login`, anonymous): answers 404
  unless `DevTools:TestLoginEnabled` is true **and** the environment is one of `Development`, `Staging` or `Testing`
  (an allow-list, so a misspelt or custom production environment name fails closed; the name comes from the process
  environment, never from a request). It lists and signs in only active test accounts and seeded demo
  accounts (`@demo.optimizeall.app` and client subdomains; plain-ASCII domains only, so look-alike Unicode never
  matches); any other account gets 404. It issues an ordinary session
  and is audited as `auth.test_login`. Enabled by default only in `appsettings.Development.json` and
  `appsettings.Staging.json`; keep it off on any environment reachable by real users.

## 3. Rate limiting and abuse controls

* Global token bucket: 300 requests/min per client IP.
* `auth` policy: 10 requests/min per IP on credential endpoints (`RateLimiting__AuthPerMinute`).
* `refresh` policy: 240 session refreshes/min per IP (`RateLimiting__RefreshPerMinute`). Every page load and tab
  refreshes silently and an office behind one NAT address shares the budget, so it is generous; refresh tokens are
  random, rotate on use and trigger reuse detection, so nothing can be guessed through this endpoint.
* `submissions` policy: 30 writes/min per user for actions that create staff work (submissions, tickets, appeals).
* `public` policy: 120 requests/min per IP for unauthenticated endpoints (landing pages, `/t/{code}`, postbacks).
* `tracking` policy: 1,200 requests/min per IP for email open pixels, click redirects and one-click unsubscribes
  (`/e/*`; mailbox providers fetch these for many recipients from a few proxy IPs), `RateLimiting__TrackingPerMinute`.
* `webhooks` policy: 6,000 requests/min per endpoint path, i.e. per provider and workspace, for signature-verified
  provider webhooks (SendGrid, Mailgun, Twilio), `RateLimiting__WebhooksPerMinute`. `tracking` and `webhooks`
  endpoints are exempt from the global per-IP bucket, so a large send cannot lose bounce/complaint events.
* Email links that change state (double opt-in confirmation, newsletter confirm/unsubscribe pages) act only when the
  reader presses a button — never on page load — so mail security scanners that open links cannot act for them.
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
* **Links in the SPA**: every href that comes from the API (banner CTAs, onboarding actions, campaign assets,
  post and profile URLs, notification links) goes through `isSafeHref` / `SafeExternalLink`
  (`frontend/src/lib/safeHref.ts`): only absolute `http(s)` URLs without credentials and app paths starting with a
  single `/` become links; other schemes, protocol-relative `//host` and backslashes render as plain text.
* **Image URLs** (campaign hero and image assets, homepage banners) must be uploads (`/api/v1/files/{id}`) or https
  URLs on a host listed in `Content:AllowedImageHosts` (default empty), matching the web CSP `img-src` (§ 8).

## 5. File uploads and private storage

* Screenshot/proof uploads are limited to 12 MB per request (API multipart limit and nginx
  `client_max_body_size`), restricted to image types, and verified by content (magic bytes), not by the
  client-supplied name or MIME type.
* Files are stored under `Storage__RootPath` with **server-generated keys**; client file names are never used
  as paths (no path traversal). Storage is outside any web root and **not served statically**: files are only
  returned by authorized API endpoints, with `X-Content-Type-Options: nosniff` and a `sandbox` CSP.
* **Metadata is stripped on upload** (`Files/ImageMetadataStripper`, no re-encoding): JPEG APP1 (Exif/XMP), APP13
  (IPTC) and COM segments; PNG `eXIf`, `tEXt`, `zTXt`, `iTXt`, `tIME` chunks; WebP `EXIF`/`XMP ` chunks (RIFF size
  and VP8X flags fixed). So GPS coordinates, device serials and capture times never reach storage. The stored bytes
  and their SHA-256 (duplicate-screenshot detection) are those of the stripped image, so the same picture with or
  without metadata still matches.
* **Screenshot access**: the owner; users with `submissions.review`; and campaign managers only for submissions to
  campaigns they created (`Campaign.CreatedByUserId`). Anyone else gets `404` (existence is not revealed).
* Use a private volume/bucket with encryption at rest; never make it public.
* Discount-code sale proofs (`FilePurpose.SaleProof`) are private images like submission screenshots: readable by
  their owner and by staff with `sales.review` or `codes.view` only.

## 6. Encryption of payout destinations

* Participants' payout details (bank account, wallet, PayPal email...) are encrypted at rest with **ASP.NET Core
  Data Protection** (authenticated encryption, AES-256-CBC + HMACSHA256 by default); only a masked summary is
  stored in clear for display. Decryption happens only for finance workflows (payment instructions export),
  which are permission-gated and audited.
* The data-protection **key ring is stored in MySQL** (`data_protection_keys`) so all instances share it; keys
  rotate automatically (90 days) and old keys remain for decryption. The first key is created at startup under a
  named lock (database initialization), so instances starting together on an empty key ring share one key instead of
  each activating its own.
* **Production recommendation:** protect the key ring itself (the API logs "No XML encryptor configured" until
  this is done), e.g. `ProtectKeysWithCertificate(...)`, Azure Key Vault (`ProtectKeysWithAzureKeyVault`) or an
  AWS KMS-backed XML encryptor, so a database dump alone cannot decrypt payout data. Until then, database
  backups must be treated as containing decryptable financial data (encrypted, access-restricted).
* Database connections should use TLS (`SslMode=Required`/`VerifyFull`); managed MySQL storage encryption on.

## 7. Secrets management

* Secrets: `ConnectionStrings__Default`, `Jwt__SigningKey`, `Security__HashSalt`, `Tracking__PostbackSecret`,
  `Email__SmtpPassword`, `WhatsApp__AccessToken`, `Bootstrap__AdminPassword`, `Authentication__Google__ClientSecret`. Generate with
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
* CSP `img-src 'self' data: blob:` plus `IMG_SRC_EXTRA` (web container, space-separated https origins) for external
  image hosts; keep it identical to the API's `Content__AllowedImageHosts__N` so every image URL the API accepts can
  load. Default: uploads only.
* Headers: HSTS on HTTPS (API; enable for the SPA in nginx once HTTPS-only), CSP, `X-Frame-Options: DENY`,
  `Referrer-Policy: strict-origin-when-cross-origin`, `Permissions-Policy` disabling camera/microphone/geolocation,
  `Cross-Origin-Opener-Policy: same-origin`.

## 9. Audit trail

* `audit_logs` is **append-only** (updates/deletes are rejected in `AppDbContext`). Entries record actor, action,
  entity, before/after snapshots, reason, the actor's IP address, correlation id and timestamp, and are written
  in the **same transaction** as the change they describe.
* Audited: campaign edits and publishing, reward rule changes, review decisions and reversals, appeals, ledger
  adjustments, payout batch actions (prepare, hold, finalize, record payment, cancel), payout settings, role
  grants, suspensions, settings changes, lockouts, impersonation (start/end, every write made while impersonating,
  with the impersonator in `ImpersonatorUserId`), test-user creation/deletion and test sign-ins.
* Viewable/exportable by `audit.view`. Retain at least as long as financial records. For stronger tamper
  evidence, ship audit rows to a write-once store (e.g. object storage with object lock) as well.

## 10. Data minimization and privacy

* IP addresses and device identifiers used for fraud signals and click tracking are stored only as **keyed
  HMAC-SHA256 hashes** (`Security__HashSalt`), enough for fraud detection and click de-duplication without
  keeping raw values. The one exception is the audit log, which keeps the acting user's IP address for
  accountability of privileged actions.
* Payout details are encrypted (§ 6); logs never contain passwords, tokens or payout details.
* **Reviewers see only what a review needs**: the review queue shows the participant's display name and country;
  the review workspace adds email (to identify the account and match support tickets), tier, join date, account
  status and their approved/rejected/reversed counts, plus the submitted post, its social account and related
  submissions. Payout details, phone numbers, IPs/device ids (only fraud flags derived from their hashes) and
  earnings outside the submission are not exposed to reviewers. Screenshots are scoped as in § 5.
* Uploaded images are stripped of EXIF/GPS/XMP/text metadata (§ 5).
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
* **Discount-code sales** (DISCOUNT_CODES.md): one live claim per order and program (unique index + per-program named
  lock → the first report wins, `409 code_sale.duplicate_order`); nobody decides a sale that is theirs or that they
  entered/imported (`403 code_sale.self_review` / `code_sale.four_eyes`); commissions go through `ILedgerWriter` with a
  per-sale idempotency key and are never priced at 0 when an exchange rate is missing (`409 fx.rate_missing`); refunds
  reverse through the ledger (clawback when paid); payout-rule changes need confirm + reason and are audited;
  participants only ever see their own codes and sales (another person's code or sale is a 404). Every money-affecting
  write (payout rules, overrides, code imports, assignment, approvals, refunds, sales-report import) and the
  participant's sale claims are denied while impersonating.
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
