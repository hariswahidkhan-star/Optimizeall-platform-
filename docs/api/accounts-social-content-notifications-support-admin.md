# API: Accounts, Social profiles, Content, Notifications, Support, Administration

All routes are under `/api/v1`. JSON is camelCase, enums are strings, timestamps are UTC ISO-8601.
Errors are RFC 7807 problem responses: `{ "status", "title", "code", "traceId", "errors"? }` where `errors`
(when present) maps a camelCase field name to messages. Automatic DataAnnotations failures return 400 with
ASP.NET's `errors` dictionary (no `code`).

Common error codes on every endpoint: `401` (no/expired/revoked token), `403` (`auth.forbidden` or missing
permission, empty body from the authorization layer), `409 concurrency.conflict` (stale `concurrencyStamp`),
`429 rate_limited`.

**Paging.** List endpoints take `?page=1&pageSize=25&search=&sort=&desc=true` (`pageSize` 1–200) and return

```json
{ "items": [ ... ], "total": 42, "page": 1, "pageSize": 25, "totalPages": 2 }
```

**Concurrency.** Editable staff resources return `concurrencyStamp` (GUID). Updates must send the stamp they last
read; a stale stamp returns `409 concurrency.conflict`.

**Permissions** (see `Api/Common/Security/Permissions.cs`): participant self-service requires
`participant.portal` unless marked "any authenticated user".

---

## 1. Accounts

### `GET /me/profile` — any authenticated user
### `PUT /me/profile` — any authenticated user

Request:

| field | rules |
|---|---|
| `displayName` | required, 2–100 |
| `countryCode` | required, ISO 3166-1 alpha-2 (stored upper case) |
| `languageCode` | required, BCP 47 (`en`, `ar`, `pt-BR`) |
| `timeZone` | required, IANA id (`Asia/Karachi`) |
| `interests` | ≤ 20 tags, each ≤ 40 chars; trimmed, lower-cased, de-duplicated |
| `marketingEmailOptIn` | bool |
| `whatsAppNumber` | optional E.164 `^\+[1-9]\d{7,14}$` (spaces/dashes stripped) |
| `whatsAppOptIn` | bool; `true` requires a number. When `false` the stored number is removed. |

Response (`ProfileDto`, both verbs):

```json
{
  "id": "guid", "email": "sara@example.com", "emailVerified": true, "displayName": "Sara Khan",
  "countryCode": "PK", "languageCode": "en", "timeZone": "Asia/Karachi", "interests": ["fashion", "tech"],
  "marketingEmailOptIn": false, "whatsAppNumber": "+923001234567", "whatsAppOptIn": true,
  "tier": "Standard", "referralCode": "K7P2QX9M", "createdAt": "2026-09-01T10:00:00Z"
}
```

Errors: `400 profile.invalid` with field errors (`timeZone`, `languageCode`, `interests`, `whatsAppNumber`).
Audited as `account.profile_updated` (changed field names only).

### `GET /me/payout-profile`
### `PUT /me/payout-profile`

Request:

| field | rules |
|---|---|
| `method` | required: `BankTransfer` \| `PayPal` \| `MobileWallet` \| `Other` |
| `accountHolderName` | required, 2–150 |
| `destination` | required, ≤ 200, **write-only**. PayPal: email. BankTransfer: 8–34 letters/digits after removing spaces/dashes; IBAN-shaped values must pass the mod-97 checksum. MobileWallet: E.164. Other: 3–200 chars. |
| `preferredCurrency` | required, supported ISO 4217 code (`Money.SupportedCurrencies`) |
| `countryCode` | optional ISO2 |

Response (`PayoutProfileDto`):

```json
{ "configured": true, "method": "BankTransfer", "accountHolderName": "Sara Khan",
  "destinationHint": "••••5432", "preferredCurrency": "GBP", "countryCode": "GB",
  "updatedAt": "2026-09-23T12:00:00Z" }
```

When nothing is saved yet: `{ "configured": false, "method": null, ... all null }`.
The raw destination is encrypted with ASP.NET Data Protection (purpose
`OptimizeAll.PayoutProfile.Destination.v1`) and is **never** returned. Hints: bank/wallet/other `••••1234`,
PayPal `j•••@gmail.com`.

Errors: `400 payout_profile.invalid_destination`, `400 payout_profile.unsupported_currency`,
`400 payout_profile.invalid_method`, `409 payout_profile.concurrent_update`.
Audited as `account.payout_profile_updated` (method, hint, currency, country — no raw values).

**Internal service for finance code:** `OptimizeAll.Api.Modules.Accounts.IPayoutDestinationReader`
(scoped) — `Task<string?> RevealAsync(Guid userId, CancellationToken ct = default)` returns the decrypted,
normalized destination (IBAN/account number without spaces, lower-case PayPal email, E.164 number) or `null`
when the user has no payout profile. Callers must authorize the access themselves and must never log, audit
or return the value to participants.

### `GET /me/home`

Dynamic homepage payload:

```json
{
  "user": { "id": "guid", "displayName": "Sara", "emailVerified": true, "tier": "Standard",
            "timeZone": "Asia/Karachi", "countryCode": "PK", "languageCode": "en" },
  "state": "AwaitingEligibility",
  "eligibleFrom": "2026-11-20T09:00:00Z",
  "onboarding": {
    "steps": [
      { "id": "guid", "key": "verify-email", "title": "...", "description": "...", "actionLabel": null,
        "actionUrl": null, "completionRule": "EmailVerified", "isManual": false, "completed": true }
    ],
    "completedCount": 3, "totalCount": 7, "progressPercent": 43
  },
  "banners": [ { "id": "guid", "title": "...", "body": "...", "imageUrl": "https://...", "ctaLabel": "Go", "ctaUrl": "/app/campaigns" } ],
  "announcements": [ { "id": "guid", "title": "...", "body": "...", "severity": "Info",
                       "publishAt": "...", "expiresAt": null } ],
  "submissions": { "total": 0, "pending": 0, "underReview": 0, "approved": 0,
                   "needsCorrection": 0, "rejected": 0, "reversed": 0 },
  "unreadNotificationCount": 2,
  "openSupportTicketCount": 0,
  "socialAccounts": { "total": 1, "eligible": 0, "soonestEligibleFrom": "2026-11-20T09:00:00Z", "minAccountAgeDays": 90 }
}
```

`state` (evaluated in order):
`VerifyEmail` (email not verified) → `AddSocialAccount` (no active social profile) → `AwaitingEligibility`
(profiles exist but none qualifies; `eligibleFrom` = soonest date a profile becomes old enough, only when age is its
only blocker, else `null`) → `Ready` (≥1 qualifying profile, no submissions) → `Active` (has submissions).
`eligibleFrom` is `null` in every other state.

Qualification uses the platform-wide rules (`eligibility.minAccountAgeDays`, `eligibility.minFollowers`) through
`EligibilityEvaluator.EvaluateAccount`; deactivated profiles are ignored.

Onboarding step completion (server-side): `EmailVerified`; `ProfileCompleted` (country + time zone + ≥1 interest);
`SocialAccountAdded` (≥1 active profile); `EligibleSocialAccount`; `PayoutProfileAdded`; `FirstSubmission`;
`FirstApprovedSubmission`; `Manual` (dismissed by the participant). Only active steps, ordered by `sortOrder`.

Banners: active, inside `startsAt`/`endsAt`, audience matches, `countryCode` (if set) equals the user's country,
`languageCode` (if set) matches the user's primary language. Announcements: active, `publishAt <= now <
expiresAt`, audience matches (max 50, newest first).

Audiences: `Everyone`; `Onboarding` = email unverified or no qualifying profile; `Eligible` = ≥1 qualifying
profile; `ActiveEarners` = ≥1 approved submission; `Inactive` = `lastActiveAt` (or `createdAt`) older than setting
`retention.inactivityDays` (default 30).

### `POST /me/onboarding/{stepId}/complete`

Dismisses a `Manual` step. Idempotent (repeat calls return the original time).
Response: `{ "stepId": "guid", "completed": true, "completedAt": "..." }`.
Errors: `404 onboardingstep.not_found` (unknown or inactive), `400 onboarding.not_manual`.

---

## 2. Social profiles

`SocialAccountDto`:

```json
{
  "id": "guid", "platform": "Instagram", "handle": "sara.k", "profileUrl": "https://www.instagram.com/sara.k",
  "accountCreatedAt": "2026-09-13T00:00:00Z", "accountAgeDays": 10, "followerCount": 2500,
  "primaryLanguage": "en", "audienceCountryCode": "PK",
  "verificationStatus": "Unverified", "verificationNote": null, "isActive": true,
  "qualifies": false, "eligibleFrom": "2026-12-12T00:00:00Z",
  "reasons": [ { "code": "social.account_too_new", "message": "Profiles must be at least 90 days old. This one qualifies in 80 days." } ],
  "createdAt": "...", "updatedAt": "...", "concurrencyStamp": "guid"
}
```

Reason codes: `social.account_too_new`, `social.followers_below_minimum`, `social.inactive`,
`social.verification_rejected`.

### `GET /me/social-accounts`

```json
{ "minAccountAgeDays": 90, "minFollowers": 0, "maxActiveAccounts": 10, "items": [ SocialAccountDto ] }
```

Includes deactivated profiles (active first).

### `POST /me/social-accounts` → `201 SocialAccountDto`

| field | rules |
|---|---|
| `platform` | required: `Instagram`, `TikTok`, `X`, `Facebook`, `LinkedIn`, `YouTube`, `Threads`, `Pinterest`, `Snapchat` |
| `handle` | required, 1–100 after stripping a leading `@`; no spaces/URL characters |
| `profileUrl` | required absolute `https://` URL whose host is (a subdomain of) instagram.com · tiktok.com · x.com/twitter.com · facebook.com/fb.com · linkedin.com · youtube.com/youtu.be · threads.net/threads.com · pinterest.com · snapchat.com |
| `accountCreatedAt` | required; not in the future, not before 2004-01-01 |
| `followerCount` | 0–1,000,000,000 |
| `primaryLanguage` | optional language code |
| `audienceCountryCode` | optional ISO2 |

Errors: `400 social.invalid` (field errors), `409 social.limit_reached` (10 active profiles),
`409 social.already_registered` (same platform + handle owned by another user; the owner is never revealed),
`409 social.already_added` (the caller already added it; if deactivated, reactivate instead).
Audited `social.account_added`.

### `PUT /me/social-accounts/{id}` → `SocialAccountChangeResponse`

Body: `handle`, `profileUrl`, `accountCreatedAt`, `followerCount`, `primaryLanguage?`, `audienceCountryCode?`,
`concurrencyStamp` (required). Platform cannot change.

```json
{ "account": SocialAccountDto, "verificationReset": true,
  "message": "Profile updated. Because you changed the handle, creation date or follower count, it needs to be verified again." }
```

**Rule:** changing the handle, creation date or follower count of a `Verified` or `PendingReview` profile resets
it to `Unverified` (verifier and note cleared).
Errors: `404 socialaccount.not_found` (also for other users' profiles), `409 social.inactive`,
`409 concurrency.conflict`, `400 social.invalid`, `409 social.already_registered|already_added`.

### `DELETE /me/social-accounts/{id}` → `SocialAccountDto`
Deactivates (`isActive=false`); never deletes (submissions reference it). Idempotent.

### `POST /me/social-accounts/{id}/reactivate` → `SocialAccountDto`
Errors: `409 social.limit_reached`.

### `POST /me/social-accounts/{id}/request-verification` → `SocialAccountDto`
Only from `Unverified` or `Rejected` → `PendingReview`. Errors: `409 social.verification_not_allowed`,
`409 social.inactive`.

### Staff review — permission `social.verify`

#### `GET /review/social-accounts?status=&platform=&search=&page=&pageSize=&desc=`
`search` matches handle, owner email, owner display name. Ordered by `updatedAt` (`desc=false` → oldest first).
Items (`ReviewSocialAccountDto`):

```json
{ "id": "guid", "platform": "Instagram", "handle": "...", "profileUrl": "...", "accountCreatedAt": "...",
  "accountAgeDays": 30, "followerCount": 2500, "primaryLanguage": "en", "audienceCountryCode": "PK",
  "verificationStatus": "PendingReview", "verificationNote": null, "verifiedAt": null, "isActive": true,
  "createdAt": "...", "updatedAt": "...",
  "owner": { "id": "guid", "displayName": "Sara", "email": "sara@example.com", "countryCode": "PK" },
  "concurrencyStamp": "guid" }
```

#### `GET /review/social-accounts/{id}` → `ReviewSocialAccountDetailDto`

```json
{ "account": ReviewSocialAccountDto, "qualifies": false, "eligibleFrom": "...", "reasons": [ ... ],
  "verifiedBy": { "id": "guid", "displayName": "Reviewer" } | null,
  "ownerActiveAccountCount": 2,
  "history": [ { "at": "...", "action": "social.account_verified", "actorUserId": "guid", "actorDisplayName": "...", "reason": null } ] }
```

#### `POST /review/social-accounts/{id}/decision` → `ReviewSocialAccountDetailDto`

Body: `decision` (`Verified` \| `Rejected`), `note` (≤ 500, **required for Rejected**; shown to the participant),
`verifiedAccountCreatedAt?` (corrects the declared date), `verifiedFollowerCount?`, `concurrencyStamp`.
Sets `verifiedAt`/`verifiedByUserId`, audits `social.account_verified|social.account_rejected`, and stages a
`social.verification` notification (in-app + email, link `/app/social-accounts`).
Errors: `400 social.note_required`, `400 social.invalid_created_at`, `404`, `409 concurrency.conflict`.

---

## 3. Content / CMS — permission `content.manage`

Every mutation is audited (`content.banner_created|updated|deleted`, `content.banners_reordered`, and the same
pattern for `announcement`, `faq`, `onboarding_step`). Links (`ctaUrl`, `actionUrl`) must be absolute
`https://` URLs or app paths starting with a single `/`. Banner `imageUrl` must be an upload (`/api/v1/files/{id}`,
see `POST /admin/files` with `purpose=ContentImage`) or an https URL on a host in `Content:AllowedImageHosts`
(default none). Lists support `search` + paging.

### Banners `/admin/content/banners`

| verb | path | body / response |
|---|---|---|
| GET | `/admin/content/banners?search=&isActive=&audience=` | `PagedResult<BannerDto>` ordered by `sortOrder` |
| GET | `/admin/content/banners/{id}` | `BannerDto` |
| POST | `/admin/content/banners` | `BannerRequest` → `201 BannerDto` |
| PUT | `/admin/content/banners/{id}` | `BannerRequest + concurrencyStamp` → `BannerDto` |
| DELETE | `/admin/content/banners/{id}` | `204` |
| POST | `/admin/content/banners/reorder` | `{ "ids": [guid...] }` → `{ "updated": n }` (sortOrder 10, 20, 30…) |

`BannerRequest`: `title` (1–150), `body?` (≤1000), `imageUrl?`, `ctaLabel?` (≤60), `ctaUrl?` (label and URL go
together), `audience` (`Everyone|Onboarding|Eligible|ActiveEarners|Inactive`), `countryCode?`, `languageCode?`,
`startsAt?`, `endsAt?` (> `startsAt`), `sortOrder`, `isActive`.
`BannerDto` = those fields + `id`, `createdAt`, `updatedAt`, `concurrencyStamp`.

### Announcements `/admin/content/announcements`
Same verbs as banners (no reorder — ordered by `publishAt` desc). Filters `isActive`, `audience`.
Request: `title` (1–150), `body` (1–5000), `severity` (`Info|Success|Warning|Critical`), `audience`,
`publishAt` (required), `expiresAt?` (> `publishAt`), `isActive`.
`AnnouncementDto` = fields + `id`, `createdAt`, `updatedAt`, `concurrencyStamp`.

### FAQ `/admin/content/faqs`
Same verbs incl. `reorder`. Filters `category`, `isPublished`.
Request: `question` (5–300), `answer` (1–10000), `category` (1–60), `sortOrder`, `isPublished`.

### Onboarding steps `/admin/content/onboarding-steps`
Same verbs incl. `reorder`. Filter `isActive`.
Request: `key` (unique slug `^[a-z0-9]+(-[a-z0-9]+)*$`, 2–60), `title` (1–150), `description` (1–1000),
`actionLabel?` (required with `actionUrl`), `actionUrl?`, `completionRule`
(`Manual|EmailVerified|ProfileCompleted|SocialAccountAdded|EligibleSocialAccount|PayoutProfileAdded|FirstSubmission|FirstApprovedSubmission`),
`sortOrder`, `isActive`. Errors: `409 content.duplicate_key`.

Content errors: `400 content.invalid` (field errors), `400 content.reorder_duplicates`,
`400 content.reorder_unknown`, `404 homepagebanner.not_found|announcement.not_found|faqitem.not_found|onboardingstep.not_found`.

### Public reads

`GET /content/faqs` — anonymous (rate limit `public`). Published items grouped by category:

```json
{ "categories": [ { "category": "Getting started", "items": [ { "id": "guid", "question": "...", "answer": "..." } ] } ] }
```

`GET /content/announcements` — any authenticated user; audience-filtered `AnnouncementViewDto[]`
(`id`, `title`, `body`, `severity`, `publishAt`, `expiresAt`).

### Baseline seed (profile `Baseline`, idempotent)
Onboarding steps `verify-email`, `complete-profile`, `add-social-account`, `eligible-account`, `payout-details`,
`first-submission`, `first-approved` (action URLs `/app/profile`, `/app/social-accounts`,
`/app/profile/payout-details`, `/app/campaigns`); 15 FAQ items in *Getting started, Eligibility, Submissions, Payments,
Disclosure & rules, Account*; a welcome announcement for the `Onboarding` audience. Existing rows (matched by key /
question / title) are never overwritten, except that an onboarding step whose `actionUrl` is still a retired baseline
link (`payout-details`: `/app/payout-details`) is moved to the current one on every seed run; admin-chosen links are kept.

---

## 4. Notifications

### Notification center — any authenticated user

| verb | path | response |
|---|---|---|
| GET | `/me/notifications?unreadOnly=false&page=1&pageSize=20` (pageSize ≤ 100) | `PagedResult<NotificationDto>`, newest first |
| GET | `/me/notifications/unread-count` | `{ "count": 3 }` |
| POST | `/me/notifications/{id}/read` | `204` (idempotent; `404 notification.not_found` for others' ids) |
| POST | `/me/notifications/read-all` | `{ "updated": 5 }` |

`NotificationDto`: `{ "id", "type", "title", "body", "linkUrl", "createdAt", "readAt", "isRead" }`. `linkUrl` is an
app-relative web route built with `Common/Notifications/AppLinks.cs` (e.g. `/app/submissions/{id}`,
`/app/payouts/{itemId}`, `/review/live-checks`, `/finance/batches/{id}`); `frontend/src/app/appLinks.fixture.json`
lists every pattern and tests on both sides keep them resolving to real routes.

### Preferences — any authenticated user

`GET /me/notification-preferences`:

```json
{
  "channels": [
    { "channel": "InApp", "available": true, "reason": null },
    { "channel": "Email", "available": true, "reason": null },
    { "channel": "WhatsApp", "available": false, "reason": "WhatsApp notifications are not available yet." }
  ],
  "types": [
    { "type": "submission.decision", "label": "Submission decisions", "description": "...",
      "essential": false, "marketing": false,
      "channels": [
        { "channel": "InApp", "enabled": true, "locked": true, "available": true },
        { "channel": "Email", "enabled": true, "locked": false, "available": true },
        { "channel": "WhatsApp", "enabled": true, "locked": false, "available": false }
      ] }
  ]
}
```

Every `NotificationTypes` constant the caller can receive is a row. Staff-only types are listed only with the
matching permission: `review.live_check_due` needs `submissions.review`, `payout.batch_prepared` needs `payouts.view`
(`NotificationCatalog.StaffTypes`); for anyone else they are omitted and `PUT` treats them as unknown. Essential types (`account.email_verification`,
`account.password_reset`, `account.status_changed`, `payout.paid`) are `locked: true` on all channels. In-app is
always on and locked. WhatsApp `available=false` reasons: integration not configured, or the user has not opted in
with a number (profile). Marketing types (`campaign.alert`, `retention.reactivation`) additionally require
`marketingEmailOptIn` for email (enforced when staging).

`PUT /me/notification-preferences` body `{ "preferences": [ { "type": "submission.decision", "channel": "Email", "enabled": false } ] }`
(1–200 entries) → the full matrix. Errors: `400 notifications.preference_locked` (unknown type, InApp channel or
essential type), `409 notifications.concurrent_update`.

### Channel adapters and dispatch (internal)

* `INotificationChannelSender { NotificationChannel Channel; Task<ChannelSendResult> SendAsync(NotificationDelivery, Notification, User, CancellationToken) }`,
  `ChannelSendResult(ChannelSendStatus Status /* Sent|Failed|Skipped */, string? ProviderMessageId, string? Error)`.
  The dispatch job uses the **last registered** sender per channel.
* `EmailChannelSender` — plain-text + HTML (all user content HTML-encoded), link = `Email:AppBaseUrl` + `linkUrl`;
  the footer links to the notification preferences page (`AppLinks.NotificationPreferences`).
* `WhatsAppChannelSender` — WhatsApp Business Cloud API. Configuration section `WhatsApp`:
  `Enabled`, `PhoneNumberId`, `AccessToken` (secret), `ApiBaseUrl` (default `https://graph.facebook.com/v20.0`),
  `TemplateName` (approved template with body parameters `{{1}}` title and `{{2}}` body), `TemplateLanguage`
  (default `en`). Not configured → delivery `Skipped` with "WhatsApp Business credentials are not configured".
  Sends `POST {ApiBaseUrl}/{PhoneNumberId}/messages` (Bearer token) with a template message; success only on 2xx
  with `messages[0].id`; otherwise `Failed` with the status and truncated body.
* Suspended/deactivated recipients only receive essential types on external channels (others `Skipped`).
* `NotificationDispatchJob` (every 30 s): claims up to 100 due `Pending` deliveries one by one with a conditional
  `UPDATE … SET Status='Sending', LockedUntil=now+2min WHERE Id=@id AND Status='Pending' AND (LockedUntil IS NULL OR LockedUntil < now)`,
  so concurrent instances never send the same delivery. Outcomes: `Sent` (+ `sentAt`, `providerMessageId`),
  `Skipped` (+ `lastError`), `Failed` → retry after 1 m, 5 m, 30 m, 2 h, 12 h; permanently `Failed` after 6
  attempts. Deliveries stuck in `Sending` for > 5 min past their lock (crash mid-send) are marked `Failed` for manual
  retry rather than re-sent. Summary: `Claimed X of Y due deliveries: a sent, b skipped, c scheduled for retry, d failed permanently; e interrupted deliveries marked failed.`

### Outbox admin — permission `jobs.view`

`GET /admin/notifications/deliveries?status=&channel=&userId=&search=&page=&pageSize=&desc=` →
`PagedResult<DeliveryDto>`:

```json
{ "id": "guid", "notificationId": "guid", "userId": "guid", "userEmail": "...", "type": "submission.decision",
  "title": "...", "channel": "Email", "status": "Failed", "attempts": 6, "nextAttemptAt": "...",
  "lockedUntil": null, "lastError": "...", "providerMessageId": null, "createdAt": "...", "sentAt": null }
```

`POST /admin/notifications/deliveries/{id}/retry` → `DeliveryDto`. Only `Failed` → `Pending` (attempts reset,
due now). Errors: `404 notificationdelivery.not_found`, `409 notifications.not_failed`. Audited
`notification.delivery_retried`.

---

## 5. Support tickets

Statuses `Open | AwaitingParticipant | AwaitingStaff | Resolved | Closed`; priorities `Low | Normal | High | Urgent`;
categories `General | Account | SocialProfile | Submission | Payout | Technical | Dispute`.
References are `SUP-` + 6 upper-case alphanumerics (unique; regenerated on collision).

### Participant — `participant.portal`

| verb | path | notes |
|---|---|---|
| GET | `/me/support/tickets?status=&search=&page=` | `PagedResult<TicketSummaryDto>` newest activity first |
| POST | `/me/support/tickets` | rate limit `submissions`; → `201 ParticipantTicketDto` |
| GET | `/me/support/tickets/{id}` | `ParticipantTicketDto` (**internal notes never included**) |
| POST | `/me/support/tickets/{id}/messages` | `{ "body" }` (1–5000); rate limit `submissions`; sets `AwaitingStaff` (reopens `Resolved`); `409 support.ticket_closed` when Closed |
| POST | `/me/support/tickets/{id}/close` | → `Closed` (idempotent) |

Create body: `subject` (3–200), `category`, `body` (10–5000), `submissionId?`, `payoutItemId?` (must belong to the
caller: `400 support.invalid_submission` / `400 support.invalid_payout_item`). `Dispute` tickets start at `High`
priority, others `Normal`.

`TicketSummaryDto`: `{ id, reference, subject, category, status, priority, createdAt, updatedAt }`.

`ParticipantTicketDto`:

```json
{ "id": "guid", "reference": "SUP-7K2Q9X", "subject": "...", "category": "Submission", "status": "AwaitingParticipant",
  "priority": "Normal", "submissionId": "guid", "payoutItemId": null, "createdAt": "...", "updatedAt": "...",
  "resolvedAt": null, "canReply": true,
  "messages": [ { "id": "guid", "body": "...", "fromStaff": true, "authorName": "Optimize All Support", "createdAt": "..." } ] }
```

Other users' tickets → `404 supportticket.not_found`.

### Staff — `support.manage`

`GET /admin/support/tickets?status=&priority=&category=&assignedTo=&search=&page=&desc=` — `assignedTo` is a user
id, `me` or `unassigned`; `search` matches subject, reference, requester email. Items (`StaffTicketSummaryDto`):

```json
{ "id", "reference", "subject", "category", "status", "priority",
  "requester": { "id", "displayName", "email" }, "assignedTo": { "id", "displayName", "email" } | null,
  "createdAt", "updatedAt", "concurrencyStamp" }
```

`GET /admin/support/tickets/{id}` → `StaffTicketDto`:

```json
{ "id", "reference", "subject", "category", "status", "priority", "submissionId", "payoutItemId",
  "createdAt", "updatedAt", "resolvedAt",
  "requester": { "id", "email", "displayName", "countryCode", "status", "tier", "createdAt", "openTicketCount", "totalTicketCount" },
  "assignedTo": { "id", "displayName", "email" } | null,
  "messages": [ { "id", "body", "isInternalNote", "fromStaff", "authorUserId", "authorName", "createdAt" } ],
  "concurrencyStamp": "guid" }
```

`POST /admin/support/tickets/{id}/messages` `{ "body", "isInternalNote": false }` → `StaffTicketDto`. A public reply
sets `AwaitingParticipant` and stages `support.reply` (in-app + email, link `/app/support/{id}`); an internal note
changes nothing visible. Public replies on Closed tickets → `409 support.ticket_closed`. Audited
`support.staff_replied` / `support.internal_note_added`.

`PUT /admin/support/tickets/{id}` `{ "status", "priority", "assignedToUserId": guid|null, "concurrencyStamp" }` →
`StaffTicketDto`. The assignee must be an active user holding `support.manage` (`400 support.invalid_assignee`).
`resolvedAt` is set for Resolved/Closed, cleared otherwise. Audited `support.ticket_updated`.

---

## 6. Administration

### Users

| verb | path | permission |
|---|---|---|
| GET | `/admin/users?search=&role=&status=&country=&tier=&sort=email\|displayName\|lastActiveAt\|createdAt&desc=` | `users.view` |
| GET | `/admin/users/export.csv` (same filters, ≤ 50,000 rows; no secrets) | `users.view` |
| GET | `/admin/users/{id}` | `users.view` |
| POST | `/admin/users/{id}/suspend` `{ reason (3–500), confirm: true }` | `users.suspend` |
| POST | `/admin/users/{id}/reactivate` `{ reason }` | `users.suspend` |
| PUT | `/admin/users/{id}/roles` `{ roles: [..], reason, confirm: true }` | `roles.assign` |
| PUT | `/admin/users/{id}/tier` `{ tier, reason }` | `users.manage` |
| POST | `/admin/users/staff` `{ email, displayName, countryCode, roles: [..] }` → `201` | `roles.assign` |

List item: `{ id, email, displayName, countryCode, status, tier, roles: ["Participant"], emailVerified, createdAt, lastActiveAt }`.

All mutations return `AdminUserDetailDto`:

```json
{
  "profile": { "id", "email", "displayName", "countryCode", "languageCode", "timeZone", "interests": [],
               "status": "Active", "statusReason": null, "statusChangedAt": null, "tier": "Standard",
               "referralCode", "emailVerified": true, "emailVerifiedAt", "marketingEmailOptIn", "whatsAppOptIn",
               "whatsAppNumberHint": "••••4567", "lastLoginAt", "lastActiveAt", "createdAt" },
  "roles": ["Participant"],
  "statusHistory": [ { "at", "action": "admin.user_suspended", "actorUserId", "actorDisplayName", "reason" } ],
  "socialAccounts": [ { "id", "platform", "handle", "profileUrl", "accountCreatedAt", "accountAgeDays", "followerCount", "verificationStatus", "isActive" } ],
  "submissionCounts": { "total", "pending", "underReview", "approved", "needsCorrection", "rejected", "reversed" },
  "earnings": [ { "status": "Approved", "currency": "USD", "amount": 25.00, "count": 5 } ],
  "activePayoutHolds": [ { "id", "reason", "createdAt", "createdByUserId" } ],
  "payoutProfile": { "method", "destinationHint", "preferredCurrency", "updatedAt" } | null,
  "recentAudit": [ AuditLogDto ],
  "concurrencyStamp": "guid"
}
```

(`earnings` are sums of `settlementAmount` per status and settlement currency; `recentAudit` = last 20 entries about
or by the user.)

Rules and errors:
* Suspend: sets `Suspended`, `statusReason`, `statusChangedAt`; calls `IAuthService.RevokeAllSessionsAsync`
  (existing access tokens are rejected on the next request, refresh tokens revoked); stages
  `account.status_changed` (email). `403 admin.cannot_suspend_self`, `403 admin.staff_requires_admin` (target has
  a staff role and the caller is not Admin), `409 admin.already_suspended`, `400 admin.confirmation_required`.
* Reactivate: `409 admin.already_active`; stages `account.status_changed` (email).
* Roles: roles are replaced; sessions revoked so the new permission set applies after sign-in.
  `403 admin.cannot_remove_own_admin`, `409 admin.last_admin` (at least one active Admin must remain; checked under
  a row lock), `400 admin.invalid_role`, `400 admin.confirmation_required`.
* Tier: `400 admin.invalid_tier`.
* Staff: creates a verified user with an unusable random password and emails a password-reset link
  (`IAuthService.ForgotPasswordAsync`). Needs at least one non-Participant role (`400 admin.staff_role_required`);
  `409 admin.email_exists`.
* Audit actions: `admin.user_suspended`, `admin.user_reactivated`, `admin.user_roles_changed`,
  `admin.user_tier_changed`, `admin.staff_created`.

### Settings — `settings.manage`

`GET /admin/settings` → `SettingDto[]` (every key in `SettingKeys`):

```json
{ "key": "eligibility.minAccountAgeDays", "value": 90, "defaultValue": 90, "isDefault": true,
  "valueType": "integer", "description": "...", "updatedAt": null, "updatedBy": { "id", "displayName" } | null }
```

`PUT /admin/settings/{key}` `{ "value": <json>, "reason": "...", "confirm": true }` → `SettingDto`.

| key | type | allowed |
|---|---|---|
| `eligibility.minAccountAgeDays` | integer | 0–3650 |
| `eligibility.minFollowers` | integer | 0–10,000,000 |
| `fraud.submissionVelocityPer24h` | integer | 1–1000 |
| `fraud.highRiskThreshold` | integer | 1–1000 |
| `review.claimMinutes` | integer | 1–240 |
| `review.appealWindowDays` | integer | 1–365 |
| `retention.inactivityDays` | integer | 7–365 |
| `retention.enabled` | boolean | `true` / `false` |
| `referral.program` | object | `{ enabled, referrerRewardAmount (0–100,000, rounded to the currency), currency (supported), qualifyingAction (EmailVerified\|FirstApprovedSubmission\|FirstPaidPayout), qualifyWithinDays (1–365), requireManualApproval, maxRewardedReferralsPerUser (0–100,000) }` — send the full object; unknown properties are rejected; omitted properties take their defaults |

Errors: `404 setting.not_found`, `400 settings.invalid_value`, `400 admin.confirmation_required`. Audited
`admin.setting_changed` with `{ value }` before/after and the reason.

### Audit log — `audit.view`

`GET /admin/audit-logs?action=&entityType=&entityId=&actorUserId=&from=&to=&search=&page=&pageSize=` — newest
first; `action` is a prefix match (`admin.` or `admin.user_suspended`); `from`/`to` inclusive UTC.
`AuditLogDto`:

```json
{ "id": 123, "createdAt": "...", "actorUserId": "guid", "actorEmail": "...", "actorDisplayName": "...",
  "actorType": "Admin", "action": "admin.user_tier_changed", "entityType": "User", "entityId": "guid",
  "before": { "tier": "Standard" }, "after": { "tier": "Gold" }, "reason": "...", "ipAddress": "...", "correlationId": "..." }
```

`GET /admin/audit-logs/export.csv` — same filters, max 50,000 rows, UTF-8 with BOM, columns
`id,createdAt,actorUserId,actorEmail,actorType,action,entityType,entityId,reason,ipAddress,correlationId,before,after`
(formula-injection safe).

### Jobs — `jobs.view`

`GET /admin/jobs` → `[ { "name": "NotificationDispatchJob", "jobName": "NotificationDispatchJob", "intervalSeconds": 30, "lastRun": JobRunDto | null } ]`
(`name` = registered class name used in routes, `jobName` = run-log name).

`GET /admin/jobs/runs?jobName=&status=Running|Succeeded|Failed&page=` → `PagedResult<JobRunDto>`:
`{ "id", "jobName", "runKey", "status", "attempt", "startedAt", "finishedAt", "summary", "error", "instanceId" }`.

`POST /admin/jobs/{name}/run` — requires `jobs.view` **and** `settings.manage`; runs the job now through
`JobRunner` and returns its `JobRunDto`. Errors: `404 job.not_found`, `409 jobs.lease_held`. Audited
`admin.job_run_requested`.

---

## 7. Reference data — `/meta`

### `GET /meta/currencies` — anonymous
Currencies the platform accepts (`Money.SupportedCurrencies`), alphabetical, with their minor-unit digits:
`[{ "code": "AED", "minorUnits": 2 }, …, { "code": "JPY", "minorUnits": 0 }, { "code": "KWD", "minorUnits": 3 }, …]`.
The UI builds every currency picker from it (`frontend/src/lib/api/meta.ts#useSupportedCurrencies`).

### `GET /meta/eligibility-defaults` — any authenticated user
Platform-wide social-profile minimums (admin settings `eligibility.minAccountAgeDays`, `eligibility.minFollowers`)
that apply when a campaign leaves its own minimum blank: `{ "minAccountAgeDays": 90, "minFollowers": 0 }`.
