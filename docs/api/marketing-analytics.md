# Marketing, growth & analytics API

Module owners: `Api/Modules/Marketing`, `Api/Modules/Retention`, `Api/Modules/Analytics`.
Conventions follow `docs/ARCHITECTURE.md`: JSON is camelCase, enums are strings, timestamps are UTC ISO-8601,
errors are RFC 7807 problems with a stable `code` (and `errors` for field validation). Paged lists use
`?page=1&pageSize=25&search=` and return `{ "items": [...], "total", "page", "pageSize", "totalPages" }`.

Common errors on every authenticated endpoint: `401` (no/invalid token), `403 auth.forbidden` (missing permission),
`400` with `errors` (DataAnnotations validation). Date ranges (`from`/`to`) are validated by all range endpoints:
`400 range.invalid` (from > to) and `400 range.too_long` (more than 366 days).

Permissions: `participant.portal` (participants), `marketing.manage` (campaign managers, admins),
`analytics.view` (campaign managers, finance, admins). Public endpoints are anonymous and rate limited
(`public` policy: 120 requests/minute per IP).

Contents: [Referrals](#referrals) · [Invitations & landing pages](#invitations--landing-pages) ·
[Tracking links & conversions](#tracking-links--conversions) · [Experiments](#ab-experiments) ·
[Templates & calendar](#post-templates--content-calendar) · [Achievements](#achievements) ·
[Retention](#retention-automations) · [Analytics](#performance-analytics)

---

## Referrals

### `GET /api/v1/me/referrals` — `participant.portal`

The caller's referral code, share link, program terms, stats and referred participants (names masked).

```json
{
  "code": "K7QM2ZP4RA",
  "link": "https://app.optimizeall.app/register?ref=K7QM2ZP4RA",
  "program": {
    "enabled": true, "rewardAmount": 5.0, "currency": "USD",
    "qualifyingAction": "FirstApprovedSubmission", "qualifyWithinDays": 60
  },
  "stats": { "registered": 1, "qualified": 1, "rewarded": 0, "pendingReward": 1, "expired": 0, "rejected": 0 },
  "items": [
    {
      "id": "0192…", "maskedName": "Z***", "status": "Qualified",
      "registeredAt": "2026-09-01T10:00:00Z", "qualifiedAt": "2026-09-03T08:12:00Z", "qualifyBy": "2026-10-31T10:00:00Z",
      "rewardStatus": "PendingApproval"
    }
  ]
}
```

`status`: `Registered | Qualified | Rejected | Expired`. `rewardStatus` is the ledger status of the referrer's
reward (`PendingApproval | Approved | Scheduled | Paid | Reversed | Declined`) or `null` (no reward yet / none).
`rewarded` counts rewards that are Approved, Scheduled or Paid. At most the 200 most recent items are returned.

### `GET /api/v1/marketing/referrals` — `marketing.manage`

Query: `status` (ReferralStatus), `flagged` (`true` = with fraud signals, `false` = without), `search` (referrer or
referred name/email, or the code used), `page`, `pageSize`. Newest first.

```json
{
  "items": [
    {
      "id": "0192…",
      "referrer": { "id": "…", "displayName": "Sara Khan", "email": "sara@example.com" },
      "referred": { "id": "…", "displayName": "Zara Ali", "email": "zara@example.com" },
      "codeUsed": "K7QM2ZP4RA", "status": "Qualified", "qualifyingAction": "FirstApprovedSubmission",
      "registeredAt": "…", "qualifyBy": "…", "qualifiedAt": "…",
      "fraudSignals": ["shared_device"], "rejectionReason": null,
      "earningEntryId": "…", "rewardStatus": "PendingApproval", "rewardAmount": 5.0, "rewardCurrency": "USD"
    }
  ],
  "total": 1, "page": 1, "pageSize": 25, "totalPages": 1
}
```

### `POST /api/v1/marketing/referrals/{id}/reject` — `marketing.manage`

Body: `{ "reason": "Same household" }` (3–1000 chars). Rejects the referral; a `PendingApproval` reward is declined,
an `Approved` (unpaid) reward is reversed, `Scheduled`/`Paid` rewards are left unchanged. Audited
(`referral.rejected`, with the reason).

```json
{ "id": "0192…", "status": "Rejected", "rewardAction": "declined" }
```

`rewardAction`: `none | declined | reversed | unchanged_paid | unchanged_scheduled`.
Errors: `404 referral.not_found`, `409 referral.already_rejected`, `409 ledger.*` (ledger guards).

---

## Invitations & landing pages

### Invitation object

```json
{
  "id": "0192…", "code": "aZ3kP9qT", "url": "https://app.optimizeall.app/join/aZ3kP9qT",
  "name": "Spring newsletter", "campaignId": null, "campaignTitle": null,
  "utmSource": "newsletter", "utmMedium": "email", "utmCampaign": "spring",
  "expiresAt": "2026-12-31T23:59:59Z", "maxUses": 500, "isActive": true,
  "isUsable": true,
  "stats": { "visits": 42, "registrations": 7, "remainingUses": 493 },
  "createdAt": "…", "updatedAt": "…"
}
```

`registrations` = registrations that used the code (counted atomically, never above `maxUses`);
`visits` = landing page views. `isUsable` = active, not expired, not exhausted.

### `GET /api/v1/marketing/invitations` — `marketing.manage`
Query: `campaignId`, `isActive`, `search` (name or code), paging. Returns a paged list of invitation objects.

### `GET /api/v1/marketing/invitations/{id}` — `marketing.manage`
Returns an invitation object. `404 invitation.not_found`.

### `POST /api/v1/marketing/invitations` — `marketing.manage`

```json
{ "name": "Spring newsletter", "campaignId": null, "utmSource": "newsletter", "utmMedium": "email",
  "utmCampaign": "spring", "expiresAt": "2026-12-31T23:59:59Z", "maxUses": 500, "isActive": true }
```

`201` with the invitation object; the code is 8 random URL-safe characters (unique). Errors:
`400 invitation.campaign_not_found`, `400 invitation.campaign_closed` (campaign Ended/Archived),
`400 invitation.expiry_in_past`. Audited.

### `PUT /api/v1/marketing/invitations/{id}` — `marketing.manage`
Same body as create; returns the invitation object. Same errors plus `404 invitation.not_found`. Audited.

### `DELETE /api/v1/marketing/invitations/{id}` — `marketing.manage`
Deletes a link that was never visited or used; otherwise deactivates it (keeps its stats). Audited.
```json
{ "deleted": false, "deactivated": true }
```

### `GET /api/v1/public/invitations/{code}` — anonymous, rate limited

Landing payload for `/join/{code}`. Counts a visit (atomic increment). `?preview=true` from a signed-in caller with
`marketing.manage` (the manager portal's landing preview) counts no visit and makes no landing-page experiment
assignment; from anyone else (anonymous included) the flag is ignored and the visit counts. `404 invitation.not_found` when the code is
unknown, inactive, expired or used up, or (campaign invitations) the campaign is not Scheduled/Active. Invite-only
campaigns are allowed here.

```json
{
  "code": "aZ3kP9qT",
  "type": "campaign",
  "headline": "Share our spring drop",
  "body": "…",
  "heroImageUrl": "https://cdn…/hero.jpg",
  "campaign": {
    "slug": "spring-drop", "title": "Spring drop", "summary": "…",
    "platforms": ["Instagram", "TikTok"],
    "reward": { "currency": "USD", "baseAmount": 3.25 },
    "startsAt": "…", "endsAt": "…",
    "category": { "name": "Fashion", "slug": "fashion" }
  },
  "utm": { "source": "newsletter", "medium": "email", "campaign": "spring" },
  "experiment": null
}
```

Platform invitations return `"type": "platform"`, a generic headline/body, and `"campaign": null`.
`reward` is the base rate of the reward rule version in force (a teaser; `null` if none).
`experiment` is set when a LandingPage experiment applied (see below).

Registration: the web app passes `inviteCode` (and `referralCode`, `deviceId`) to `POST /api/v1/auth/register`.

### `GET /api/v1/public/campaigns/{slug}` — anonymous, rate limited

Public landing page of an **Active or Scheduled, Public** campaign (`404 campaign.not_found` otherwise).
Optional header `X-Visitor-Id: <random client id>`: when a LandingPage experiment is running for the campaign, the
visitor (hashed with the server salt; the raw id is never stored) gets a sticky variant. Without the header no
assignment is made and the base content is returned.

```json
{
  "slug": "spring-drop", "title": "Spring drop", "summary": "…",
  "headline": "Share our spring drop", "body": "…",
  "heroImageUrl": "https://cdn…/hero.jpg",
  "platforms": ["Instagram"],
  "reward": { "currency": "USD", "baseAmount": 2.5 },
  "startsAt": "…", "endsAt": "…", "submissionDeadline": "…",
  "category": null,
  "assets": [{ "id": "…", "title": "Hero image", "url": "https://cdn…/a.jpg" }],
  "disclosure": "#ad",
  "experiment": { "experimentId": "…", "variantId": "…", "key": "B" }
}
```

`headline`/`body` fall back to the campaign title/summary; `assets` lists image assets only.

---

## Tracking links & conversions

### Tracking link object

```json
{
  "id": "0192…", "campaignId": "…", "campaignTitle": "Spring drop",
  "code": "Qm3xZ81kTa", "shortUrl": "https://go.optimizeall.app/t/Qm3xZ81kTa",
  "destinationPreview": "https://shop.example.com/landing?ref=abc&utm_source=optimizeall&utm_medium=social&utm_campaign=spring-2026&utm_content=K7QM2ZP4RA#top",
  "utmSource": "optimizeall", "utmMedium": "social", "utmCampaign": "spring-2026", "utmContent": "K7QM2ZP4RA",
  "createdAt": "…",
  "stats": { "clicks": 3, "uniqueClicks": 2, "verifiedConversions": 1 }
}
```

`shortUrl` uses `Tracking:PublicBaseUrl` (default `Email:AppBaseUrl`). `utm_campaign` = campaign `UtmCampaign` or slug;
`utm_content` = the participant's referral code. Stats are measured (suspected bots excluded).

### `POST /api/v1/me/campaigns/{campaignId}/tracking-link` — `participant.portal`
Returns the caller's link for the campaign, creating it once (idempotent; unique per campaign + participant).
Errors: `404 campaign.not_found` (unknown, Draft or Archived), `409 tracking.not_enabled` (campaign has no
tracking destination).

### `GET /api/v1/me/tracking-links` — `participant.portal`
Array of the caller's tracking link objects, newest first.

### `GET /t/{code}` — anonymous, rate limited (not under `/api`)
`302 Found` to the campaign's stored destination with the UTM parameters merged into its query string (existing
`utm_*` with the same names are replaced; other parameters and the fragment are kept). Nothing from the request
influences the target (no open redirect). `404` for unknown codes. Headers: `Cache-Control: no-store`,
`Referrer-Policy: no-referrer-when-downgrade`. Records a click (see GROWTH.md for uniqueness and bot rules).

### `POST /api/v1/public/conversions` — anonymous, rate limited (server-to-server)

Headers: `Content-Type: application/json`, `X-OA-Signature: sha256=<hex HMAC-SHA256 of the raw body>` keyed with
`Tracking:PostbackSecret`. Body (max 16 KB):

```json
{ "code": "Qm3xZ81kTa", "externalReference": "order-1001", "value": 49.99, "currency": "USD", "occurredAt": "2026-09-23T10:15:00Z" }
```

`200` `{ "id": "0192…", "duplicate": false }`; a repeat of the same `(code, externalReference)` returns the existing id
with `"duplicate": true` (idempotent). Errors: `503 tracking.postback_not_configured` (no secret configured),
`401 tracking.invalid_signature`, `400 tracking.postback_invalid` (bad JSON; missing `code`/`externalReference`/`occurredAt`,
future `occurredAt`, negative value, unsupported currency, `value` without `currency`), `404 tracking.link_not_found`,
`413 tracking.postback_too_large`.

### `GET /api/v1/marketing/tracking/summary` — `marketing.manage`
Query: `campaignId?`, `from?`, `to?` (default last 30 days; clicks by click time, conversions by `occurredAt`).

```json
{
  "from": "…", "to": "…", "campaignId": null,
  "clicks": 120, "uniqueClicks": 95, "botClicksExcluded": 14, "verifiedConversions": 6,
  "conversionValue": [{ "currency": "USD", "amount": 310.5 }],
  "topParticipants": [{ "userId": "…", "displayName": "Sara Khan", "clicks": 40, "uniqueClicks": 33, "verifiedConversions": 3 }],
  "note": "Measured: clicks recorded by the redirect (suspected bots excluded) and conversions verified by signed postback."
}
```

---

## A/B experiments

### Experiment object

```json
{
  "id": "0192…", "campaignId": "…", "campaignTitle": "Spring launch", "name": "Title test", "hypothesis": "…", "element": "Title",
  "status": "Running", "startedAt": "…", "endedAt": null, "winningVariantId": null,
  "variants": [
    { "id": "…", "key": "A", "name": "Control", "weight": 50, "title": "Share our spring collection",
      "instructions": null, "assetId": null, "landingHeadline": null, "landingBody": null },
    { "id": "…", "key": "B", "name": "Benefit", "weight": 50, "title": "Earn by sharing our spring collection",
      "instructions": null, "assetId": null, "landingHeadline": null, "landingBody": null }
  ],
  "concurrencyStamp": "…", "createdAt": "…", "updatedAt": "…"
}
```

`element`: `Title | CreativeAsset | Instructions | LandingPage`; `status`: `Draft | Running | Paused | Completed`.
Only the override field matching the element is stored.

### `GET /api/v1/marketing/experiments` — `marketing.manage`
Query: `campaignId`, `status`, `search` (name), paging. Paged list of experiment objects.

### `GET /api/v1/marketing/experiments/{id}` — `marketing.manage`
`404 experiment.not_found`.

### `POST /api/v1/marketing/experiments` — `marketing.manage`

```json
{
  "campaignId": "…", "name": "Title test", "hypothesis": "A benefit-led title increases submissions", "element": "Title",
  "variants": [
    { "key": "A", "name": "Control", "weight": 50, "title": "Share our spring collection" },
    { "key": "B", "name": "Benefit", "weight": 50, "title": "Earn by sharing our spring collection" }
  ]
}
```

Rules: 2–4 variants, keys consecutive from `A` (`A` is the control), weight 1–100; per element: `title`,
`instructions`, `assetId` (must be an asset of the same campaign) or `landingHeadline`/`landingBody` required.
`201` with the experiment (status Draft). Errors: `400 experiment.campaign_not_found`, `400 experiment.invalid`
(with `errors` per field). Audited.

### `PUT /api/v1/marketing/experiments/{id}` — `marketing.manage`
Same body plus optional `concurrencyStamp`. Replaces name, hypothesis, element and variants — **Draft only**.
Errors: `409 experiment.not_draft`, `409 concurrency.conflict`, `400 experiment.campaign_immutable`, validation as create.

### `DELETE /api/v1/marketing/experiments/{id}` — `marketing.manage`
Draft only (`409 experiment.not_draft`). `204`.

### Lifecycle — `marketing.manage`, all audited, return the experiment object
| Endpoint | Transition | Errors |
|---|---|---|
| `POST /api/v1/marketing/experiments/{id}/start` | Draft → Running | `409 experiment.already_running` (another Running experiment for the same campaign + element), `409 experiment.invalid_transition` |
| `POST /api/v1/marketing/experiments/{id}/pause` | Running → Paused | `409 experiment.invalid_transition` |
| `POST /api/v1/marketing/experiments/{id}/resume` | Paused → Running | `409 experiment.already_running`, `409 experiment.invalid_transition` |
| `POST /api/v1/marketing/experiments/{id}/complete` | Running/Paused → Completed. Body (optional) `{ "winningVariantId": "…" }` | `400 experiment.invalid_winner`, `409 experiment.invalid_transition` |

### `GET /api/v1/campaigns/{campaignId}/experiment-variants` — `participant.portal`
Sticky variant for the caller in every Running experiment of the campaign (assigns on first call). The frontend
overlays these on the campaign detail and sends `variantId` with the submission (`Submission.ExperimentVariantId`).
`404 campaign.not_found` (unknown/Draft/Archived).

```json
[
  { "experimentId": "…", "element": "Title", "variantId": "…", "key": "B", "title": "Earn by sharing our spring collection",
    "instructions": null, "asset": null, "landingHeadline": null, "landingBody": null },
  { "experimentId": "…", "element": "CreativeAsset", "variantId": "…", "key": "A", "title": null, "instructions": null,
    "asset": { "id": "…", "url": "https://cdn…/a.jpg", "title": "Hero image", "type": "Image" },
    "landingHeadline": null, "landingBody": null }
]
```

### `GET /api/v1/marketing/experiments/{id}/results` — `marketing.manage`

```json
{
  "experimentId": "…", "status": "Running", "metric": "submissionRate", "measurement": "measured",
  "variants": [
    { "variantId": "…", "key": "A", "name": "Control", "weight": 50, "assigned": 120, "submissions": 12, "approved": 6,
      "totalSubmissions": 12, "totalApproved": 6, "submissionRate": 0.1, "approvalRate": 0.5 },
    { "variantId": "…", "key": "B", "name": "Benefit", "weight": 50, "assigned": 120, "submissions": 30, "approved": 20,
      "totalSubmissions": 31, "totalApproved": 20, "submissionRate": 0.25, "approvalRate": 0.6667 }
  ],
  "comparisons": [
    { "variantKey": "B", "controlKey": "A", "absoluteLift": 0.15, "relativeLift": 1.5, "zScore": 3.0579,
      "pValue": 0.00223, "significant": true,
      "note": "Statistically significant difference at the 5% level (two-sided two-proportion z-test)." }
  ],
  "method": "…"
}
```

`assigned` = stored sticky assignments (exposures); `submissions`/`approved` = distinct participants whose
submissions carry the variant id (with ≥1 approved submission); `totalSubmissions`/`totalApproved` = raw counts.
Rates are fractions (0–1) or `null` when the denominator is 0. `significant` is true only when `p < 0.05` **and**
every compared variant has ≥ 100 assignments; otherwise `note` is `"Not enough data for a reliable conclusion"`
(or "No statistically significant difference detected at the 5% level."). `404 experiment.not_found`.

---

## Post templates & content calendar

### Template object
```json
{ "id": "…", "name": "Launch post", "platform": "Instagram", "body": "Our spring drop is here! #ad",
  "hashtags": "#spring #drop", "languageCode": "en", "isArchived": false, "usageCount": 2,
  "createdAt": "…", "updatedAt": "…" }
```
`usageCount` = campaign assets referencing the template.

| Endpoint (`marketing.manage`) | Notes |
|---|---|
| `GET /api/v1/marketing/templates` | Query `search` (name, body, hashtags), `platform` (matches that platform or platform-less templates), `languageCode`, `includeArchived` (default false), `sort=name|updatedAt`, `desc`, paging. Paged list. |
| `GET /api/v1/marketing/templates/{id}` | `404 template.not_found` |
| `POST /api/v1/marketing/templates` | Body `{ "name", "platform"?, "body" (≤ 5000), "hashtags"? (≤ 500), "languageCode"? (BCP 47), "isArchived"? }` → `201` template. Audited. |
| `PUT /api/v1/marketing/templates/{id}` | Same body (set `isArchived` to archive/restore). Audited. |
| `DELETE /api/v1/marketing/templates/{id}` | Deletes an unused template; one referenced by campaign assets or calendar entries is archived instead. Returns `{ "deleted": bool, "archived": bool }`. Audited. |

### Calendar entry object
```json
{ "id": "…", "title": "Teaser post", "campaignId": "…", "campaignTitle": "Spring drop", "templateId": "…",
  "templateName": "Teaser", "platform": "TikTok", "scheduledFor": "2026-09-26T15:00:00Z", "notes": "…",
  "status": "Planned", "createdAt": "…", "updatedAt": "…" }
```
`status`: `Planned | Scheduled | Published | Cancelled`.

| Endpoint (`marketing.manage`) | Notes |
|---|---|
| `GET /api/v1/marketing/calendar` | Query `from`, `to` (default: today 00:00 UTC + 30 days; ≤ 366 days), `campaignId`, `platform`, `status`. Returns `{ "from", "to", "items": [entry…] }` ordered by `scheduledFor` (max 2000). |
| `GET /api/v1/marketing/calendar/{id}` | `404 calendarentry.not_found` |
| `POST /api/v1/marketing/calendar` | Body `{ "title", "campaignId"?, "templateId"?, "platform"?, "scheduledFor", "notes"? (≤ 2000), "status"? }` → `201`. Errors `400 calendar.campaign_not_found`, `400 calendar.template_not_found`, `400 calendar.template_archived`. Audited. |
| `PUT /api/v1/marketing/calendar/{id}` | Same body. Audited. |
| `DELETE /api/v1/marketing/calendar/{id}` | `204`. Audited. |

---

## Achievements

### `GET /api/v1/me/achievements` — `participant.portal`
Every active achievement with the caller's progress (the current value of its metric) and award time.
```json
[
  { "key": "first-approved-post", "name": "First approved post", "description": "Your first post was approved.",
    "icon": "badge-check", "criterion": "ApprovedSubmissions", "threshold": 1, "progress": 1, "awardedAt": "…" },
  { "key": "five-approved", "name": "Five approved posts", "description": "…", "icon": "star",
    "criterion": "ApprovedSubmissions", "threshold": 5, "progress": 1, "awardedAt": null }
]
```
`criterion`: `ApprovedSubmissions | CampaignsCompleted | TotalEarnedSettlement | QualifiedReferrals | PlatformsUsed`.
Icons are lucide icon names.

### Marketing (`marketing.manage`)
Achievement object: `{ "id", "key", "name", "description", "icon", "criterion", "threshold", "sortOrder", "isActive", "awardedCount", "createdAt", "updatedAt" }`.

| Endpoint | Notes |
|---|---|
| `GET /api/v1/marketing/achievements` | All achievements (incl. inactive) ordered by `sortOrder`. |
| `GET /api/v1/marketing/achievements/{id}` | `404 achievement.not_found` |
| `POST /api/v1/marketing/achievements` | Body `{ "key" (lower-case, digits, dashes), "name", "description", "icon"?, "criterion", "threshold" (> 0), "sortOrder", "isActive" }` → `201`. `409 achievement.key_taken`. Audited. |
| `PUT /api/v1/marketing/achievements/{id}` | Same body; set `isActive: false` to deactivate. `409 achievement.awarded` if the key of an awarded achievement changes; `409 achievement.key_taken`. Audited. |
| `DELETE /api/v1/marketing/achievements/{id}` | `204`; `409 achievement.awarded` when it was awarded to anyone (deactivate instead). Audited. |

---

## Retention automations

### `GET /api/v1/marketing/retention/summary` — `marketing.manage`
Query `from?`, `to?` (default last 30 days, by send time).
```json
{ "from": "…", "to": "…", "total": 11,
  "items": [
    { "kind": "onboarding.verify_email", "sent": 3 }, { "kind": "onboarding.add_social", "sent": 1 },
    { "kind": "onboarding.first_submission", "sent": 1 }, { "kind": "campaign.alert", "sent": 5 },
    { "kind": "reactivation", "sent": 1 }
  ] }
```

### `GET /api/v1/marketing/retention/log` — `marketing.manage`
Query `kind?`, `userId?`, `from?`, `to?`, `search?` (participant name/email), paging. Newest first.
```json
{ "items": [ { "id": "…", "userId": "…", "displayName": "Sara Khan", "email": "sara@example.com",
               "kind": "campaign.alert", "dedupKey": "0192…(campaign id)", "sentAt": "…" } ],
  "total": 5, "page": 1, "pageSize": 25, "totalPages": 1 }
```

The job itself (`RetentionJob`, hourly) is described in `docs/GROWTH.md`.

---

## Performance analytics

All endpoints require `analytics.view`. Query: `from?`, `to?` (UTC; default last 30 days; ≤ 366 days), `campaignId?`,
`platform?` (SocialPlatform).

### Metric and section shapes
```json
{ "key": "estimatedReach", "label": "Estimated reach (declared follower counts, not measured views)", "value": 3000,
  "unit": "count", "measurement": "estimated", "note": "…", "currency": null }
```
`unit`: `count | percent (0–100, 2 decimals) | money (with currency)`; `measurement`: `counted | measured | estimated`;
`value` is `null` when undefined (e.g. a rate with no data). A section is
`{ "key", "title", "measurement", "metrics": [metric…] }`.

### `GET /api/v1/analytics/overview`
```json
{
  "from": "…", "to": "…", "campaignId": null, "platform": null,
  "funnel":      { "key": "funnel", "measurement": "counted", "metrics": [registrations, emailVerified, participantsWithSocialAccount, eligibleAccounts, participantsWithSubmission, submissionRate] },
  "posts":       { "key": "posts", "measurement": "counted", "metrics": [postsSubmitted, postsApproved, postsRejected, postsNeedingCorrection, postsPending, postsReversed, approvalRate] },
  "spend":       { "key": "spend", "measurement": "counted", "metrics": [spend (per currency), costPerApprovedPost (per currency)] },
  "spendByCampaign": [ { "campaignId": "…", "title": "Alpha", "currency": "USD", "amount": 11.5 } ],
  "reach":       { "key": "reach", "measurement": "estimated", "metrics": [estimatedReach] },
  "traffic":     { "key": "traffic", "measurement": "measured", "metrics": [trackedClicks, uniqueClicks, botClicksExcluded] },
  "conversions": { "key": "conversions", "measurement": "measured", "metrics": [verifiedConversions, conversionValue (per currency)] },
  "timeseries": [ { "date": "2026-09-23", "registrations": 4, "submissions": 6, "approvals": 2, "clicks": 4 } ],
  "campaigns": [
    { "campaignId": "…", "title": "Alpha", "status": "Active", "submitted": 3, "approved": 2, "approvalRate": 66.67,
      "spend": [{ "currency": "USD", "amount": 11.5 }], "costPerApproved": [{ "currency": "USD", "amount": 5.75 }],
      "clicks": 3, "uniqueClicks": 2, "verifiedConversions": 1, "estimatedReach": 3000 }
  ],
  "platforms": null,
  "timeBasis": "Registrations by account creation; posts, spend and reach by submission time; clicks by click time; conversions by occurrence time (UTC)."
}
```
Metric definitions are in `docs/GROWTH.md` → *Metric definitions*.

### `GET /api/v1/analytics/campaigns/{id}`
Same payload for one campaign (`campaignId` = id) plus `platforms`:
```json
[ { "platform": "Instagram", "submitted": 3, "approved": 2, "approvalRate": 66.67,
    "spend": [{ "currency": "USD", "amount": 11.5 }], "costPerApproved": [{ "currency": "USD", "amount": 5.75 }],
    "estimatedReach": 3000 } ]
```
`404 campaign.not_found`.

### `GET /api/v1/analytics/overview/export.csv`
Same query. `text/csv` (UTF-8 with BOM, formula-injection protected), long format:
`section,campaign,key,label,value,unit,measurement,currency,note` — one row per overview metric, then one row per
campaign metric (`section = campaigns`, `campaign = title`).
