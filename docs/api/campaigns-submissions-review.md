# API: campaigns, rewards, files, submissions, review, appeals

Base path `/api/v1`. JSON is camelCase, enums are strings, timestamps are UTC ISO-8601. Money is a JSON number
with its currency beside it. Errors are RFC 7807 problems with `code` (and `errors` for details):

```json
{ "status": 409, "title": "This post has already been submitted…", "code": "submission.duplicate_url", "traceId": "…" }
```

Common codes: `auth.forbidden` (403), `<entity>.not_found` (404: `campaign.not_found`, `submission.not_found`,
`appeal.not_found`, `asset.not_found`, `category.not_found`, `file.not_found`), `concurrency.conflict` (409),
`confirmation.required` (400), `reason.required` (400). DataAnnotation failures are 400 validation problems
(`errors` keyed by field). Paged lists return:

```json
{ "items": [ … ], "total": 42, "page": 1, "pageSize": 25, "totalPages": 2 }
```
(`?page=&pageSize=` max 200, `search`, `sort`, `desc` where noted.)

Enum values: `SocialPlatform` Instagram|TikTok|X|Facebook|LinkedIn|YouTube|Threads|Pinterest|Snapchat;
`ParticipantTier` Standard|Silver|Gold|Platinum; `CampaignStatus` Draft|Scheduled|Active|Paused|Ended|Archived;
`CampaignVisibility` Public|InviteOnly; `SubmissionStatus` Pending|UnderReview|Approved|NeedsCorrection|Rejected|Reversed;
`LiveCheckStatus` NotRequired|Pending|ConfirmedLive|Removed; `EarningType` PostReward|FirstPostBonus|TimeLimitedBonus|QualityBonus|…;
`EarningStatus` PendingApproval|Approved|Scheduled|Paid|Reversed|Declined; `AppealStatus` Open|Upheld|Overturned|Withdrawn;
`RewardRuleType` BaseRate|RateOverride|TimeLimitedBonus|FirstPostBonus|QualityBonus; `BonusApprovalMode` Automatic|ManualApproval;
`CampaignAssetType` Image|Video|Caption|Link|Document.

---

## 1. Participant campaigns

### GET /campaign-categories — anonymous
Active categories ordered by `sortOrder`.
```json
[{ "id": "…", "name": "Technology", "slug": "technology", "description": null, "icon": "cpu", "sortOrder": 10, "isActive": true }]
```

### GET /campaigns — `participant.portal`
Active and upcoming (Scheduled, `upcoming: true`) **Public** campaigns. InviteOnly campaigns are **unlisted**: never
returned here or in `recommended`, but reachable by slug.

Query: `platform`, `categoryId`, `topic`, `minReward` (compared with `reward.baseAmount`), `deadlineBefore`
(ISO), `eligibleOnly` (bool), `search` (title/summary), `sort` = `deadline` (default, soonest first) | `reward`
(base amount desc) | `newest`, `page`, `pageSize`.

Item (`CampaignCard`):
```json
{
  "id": "…", "slug": "autumn-launch", "title": "Autumn launch", "summary": "…",
  "category": { "id": "…", "name": "Gaming", "slug": "gaming" },
  "topics": ["fitness"], "platforms": ["Instagram", "TikTok"], "status": "Active", "upcoming": false,
  "startsAt": "…Z", "endsAt": "…Z", "submissionDeadline": "…Z", "heroImageUrl": null,
  "reward": { "currency": "USD", "baseAmount": 5.0, "maxAmount": 7.0, "hasBonuses": true },
  "eligibility": { "isEligible": false, "reasons": [{ "code": "participant.no_qualifying_account", "message": "…" }] },
  "mySubmissionCount": 0, "remainingSubmissions": 5
}
```
`reward` is null only if the campaign has no rule set. `maxAmount` = highest of base rate and current/future overrides.
`mySubmissionCount` counts all of the caller's submissions; `remainingSubmissions` = max − non-Rejected ones.
Eligibility reason codes: `account.email_unverified`, `account.suspended`, `participant.country_not_targeted`,
`participant.language_not_targeted`, `participant.tier_not_targeted`, `participant.interests_not_matched`,
`participant.no_social_accounts`, `participant.no_qualifying_account`; per account: `social.platform_not_allowed`,
`social.account_too_new`, `social.followers_below_minimum`, `social.not_verified`, `social.inactive`,
`social.verification_rejected`.

### GET /campaigns/recommended?limit=6 — `participant.portal`
Active, open, public campaigns the caller is eligible for and has submissions left in, ranked by interest overlap
(campaign topics + targeted interests vs. the caller's interests), platform match with eligible accounts, base
reward and deadline proximity. `limit` 1–24.
```json
[{ "campaign": { …CampaignCard… }, "score": 6.25, "reason": "Matches your interest in fitness" }]
```
Reasons: `Matches your interest in {x}` | `Ends in {n} days` / `Ends within a day` | `Share it from your {Platform} account` | `You're eligible for this campaign`.

### GET /campaigns/{slug} — `participant.portal`
404 for Draft/Archived (and unknown slugs). Paused/Ended campaigns remain readable.
```json
{
  "id": "…", "slug": "…", "title": "…", "summary": "…", "category": { … } | null, "topics": [], "platforms": ["Instagram"],
  "status": "Active", "visibility": "Public", "upcoming": false, "isOpenForSubmissions": true,
  "startsAt": "…", "endsAt": "…", "submissionDeadline": "…", "timeZone": "Asia/Karachi",
  "heroImageUrl": null, "landingHeadline": null, "landingBody": null,
  "reward": { "currency": "USD", "baseAmount": 5.0, "maxAmount": 7.0, "hasBonuses": true },
  "description": "…", "postingInstructions": "…", "requiredHashtags": "#brand", "requiredMentions": "@brand",
  "assets": [{ "id": "…", "type": "Image", "title": "Hero", "url": "/api/v1/files/…", "fileId": "…", "body": null,
               "platform": "Instagram", "templateId": null, "sortOrder": 0 }],
  "disclosures": [{ "platform": "Instagram", "text": "Paid partnership" }],
  "rewardTerms": {
    "currency": "USD", "baseAmount": 5.0,
    "overrides": [{ "platform": null, "countryCode": "PK", "tier": null, "amount": 7.0, "validFrom": null, "validTo": null, "label": "Pakistan rate" }],
    "bonuses": [{ "type": "QualityBonus", "amount": 4.0, "approvalMode": "ManualApproval", "validFrom": null, "validTo": null, "label": null }],
    "dailyCap": null, "weeklyCap": null, "campaignCap": null, "minPostLiveHours": 48,
    "maxSubmissionsPerParticipant": 5, "requireScreenshot": true, "ruleSetVersion": 3
  },
  "eligibility": {
    "isEligible": true, "reasons": [],
    "accounts": [{ "socialAccountId": "…", "platform": "Instagram", "handle": "sara", "isEligible": false,
                   "eligibleFrom": "…Z", "reasons": [{ "code": "social.account_too_new", "message": "…" }] }]
  },
  "mySubmissions": [{ "id": "…", "status": "Pending", "submittedAt": "…" }],
  "mySubmissionCount": 1, "remainingSubmissions": 4,
  "trackingEnabled": true
}
```
`trackingEnabled` is true when the campaign has a valid https tracking destination; only then can the participant get
a personal tracking link (`POST /me/campaigns/{campaignId}/tracking-link`, created on request — the UI calls it only
when the participant asks, never on page view; existing links are read with `GET /me/tracking-links`).
`disclosures` has one entry per allowed platform, resolved for the caller's country: platform+country >
platform > country > the campaign default. `eligibleFrom` is set when account age is the only blocker.

---

## 2. Campaign management — `/admin/campaigns` (`campaigns.manage` unless noted)

### Campaign fields (create/update body)
```json
{
  "title": "Autumn launch", "slug": "autumn-launch" /* optional; generated from title, lower-case-hyphen */,
  "summary": "…", "description": "…", "categoryId": null, "topics": ["fitness"],
  "visibility": "Public", "startsAt": "…Z", "endsAt": "…Z", "submissionDeadline": "…Z" /* default endsAt+3d */,
  "timeZone": "Asia/Karachi", "postingInstructions": "…", "defaultDisclosureText": "#ad",
  "requiredHashtags": "#brand", "requiredMentions": "@brand",
  "budgetAmount": 1000.0, "budgetCurrency": "USD",
  "maxSubmissionsPerParticipant": 1 /* 1–100 */, "minPostLiveHours": 48 /* 0–720 */, "requireScreenshot": true,
  "eligibility": { "minAccountAgeDays": null, "minFollowers": 0, "requireVerifiedAccount": false,
                   "countries": ["PK"], "languages": ["en"], "interests": ["fitness"], "tiers": ["Gold"] },
  "platforms": ["Instagram", "TikTok"],
  "landingHeadline": null, "landingBody": null, "heroImageUrl": "/api/v1/files/{id} or https://{allowed image host}/…",
  "trackingDestinationUrl": "https://…", "utmCampaign": "autumn"
}
```
Validation codes (400): `campaign.invalid_dates` (endsAt ≤ startsAt), `campaign.invalid_deadline` (deadline <
endsAt), `campaign.invalid_time_zone`, `campaign.budget_currency_mismatch` (budget currency ≠ reward currency),
`campaign.disclosure_required`, `campaign.invalid_tracking_url` (must be absolute https),
`campaign.invalid_image_url` (an upload `/api/v1/files/{id}` or an https URL on a host in `Content:AllowedImageHosts`,
default none), `campaign.platform_required`, `campaign.invalid_country`,
`campaign.category_not_found`. 409 `campaign.slug_taken` (explicit slug in use). These business errors carry the
offending field in the problem's `errors` dictionary with a camelCase key, e.g.
`{ "code": "campaign.invalid_dates", "errors": { "endsAt": ["The campaign must end after it starts."] } }`:
`endsAt`, `submissionDeadline`, `timeZone`, `defaultDisclosureText`, `title`/`summary` (`campaign.title_required`),
`budgetCurrency` (`campaign.budget_currency_mismatch`), `budgetAmount` (`campaign.budget_change_unconfirmed` on
update), `trackingDestinationUrl`, `heroImageUrl`, `platforms`, `eligibility.countries`, `categoryId`, `slug`.

### GET /campaigns/options?search= — `campaigns.view`
Lightweight list for staff filters and pickers (reviewers, finance, managers): `[{ "id": "…", "title": "…", "status": "Active" }]`,
newest first, at most 500. `search` matches title or slug (use it to reach campaigns beyond the newest 500).

### AdminCampaign (response of GET/POST/PUT/publish/…)
```json
{
  "id": "…", "slug": "…", "title": "…", "summary": "…", "description": "…", "category": { … } | null, "topics": [],
  "status": "Draft", "visibility": "Public", "startsAt": "…", "endsAt": "…", "submissionDeadline": "…", "timeZone": "UTC",
  "postingInstructions": "…", "defaultDisclosureText": "#ad", "requiredHashtags": null, "requiredMentions": null,
  "budgetAmount": 1000.0, "budgetCurrency": "USD", "spent": 45.0, "budgetRemaining": 955.0,
  "maxSubmissionsPerParticipant": 1, "minPostLiveHours": 0, "requireScreenshot": true,
  "eligibility": { "minAccountAgeDays": null, "minFollowers": 0, "requireVerifiedAccount": false,
                   "countries": [], "languages": [], "interests": [], "tiers": [] },
  "platforms": ["Instagram"], "landingHeadline": null, "landingBody": null, "heroImageUrl": null,
  "trackingDestinationUrl": null, "utmCampaign": null,
  "assets": [ …CampaignAsset… ], "disclosures": [{ "id": "…", "platform": "Instagram", "countryCode": null, "text": "…" }],
  "currentRuleSet": { …RewardRuleSet… },
  "submissions": { "total": 3, "pending": 1, "approved": 1, "rejected": 1 },
  "createdByUserId": "…", "createdAt": "…", "updatedAt": "…", "publishedAt": null, "concurrencyStamp": "…",
  "publicLandingPath": "/c/spring-drop" /* shareable public page; live while Public and Scheduled/Active */
}
```
`spent` = sum of `Amount` of the campaign's earnings that are not Reversed/Declined (reward currency).
`submissions.pending` counts Pending + UnderReview.

| Method & path | Permission | Body | Response | Errors |
|---|---|---|---|---|
| GET `/admin/campaigns?status=&categoryId=&search=&sort=newest\|title\|startsAt\|deadline&desc=` | campaigns.manage | – | Paged `{ id, slug, title, status, visibility, category, platforms, startsAt, endsAt, submissionDeadline, submissions{total,pending,approved,rejected}, currency, spent, budget, budgetRemaining, createdAt, updatedAt, publishedAt }` | |
| POST `/admin/campaigns` | campaigns.manage **and** rewards.edit | fields + `rewardRules` (RewardRuleSetInput, becomes v1) | 201 AdminCampaign (Draft) | validation codes, `reward.invalid_rules`, 403 `auth.forbidden` |
| GET `/admin/campaigns/{id}` | campaigns.manage | – | AdminCampaign | 404 |
| PUT `/admin/campaigns/{id}` | campaigns.manage (+ rewards.edit if budget changes) | fields + `concurrencyStamp` (required) + `confirm`, `reason` (required when `budgetAmount` changes) | AdminCampaign (audited before/after diff of changed fields) | 409 `concurrency.conflict`, 409 `campaign.archived`, 400 `campaign.budget_change_unconfirmed`, 400 `campaign.budget_below_spent` (the budget can't be lower than the campaign's recorded earnings) |
| POST `/admin/campaigns/{id}/publish` | campaigns.manage + campaigns.publish | – | AdminCampaign (Scheduled if startsAt > now else Active; `CampaignPublished` event) | 400 `campaign.incomplete` (`errors.campaign[]`: base rate, platform, asset or instructions, dates, deadline passed), 409 `campaign.invalid_transition` |
| POST `/admin/campaigns/{id}/pause` | campaigns.manage | `{ "reason": "≥5 chars" }` | AdminCampaign (Active/Scheduled → Paused) | 400 `reason.required`, 409 `campaign.invalid_transition` |
| POST `/admin/campaigns/{id}/resume` | campaigns.manage | – | Paused → Active (or Scheduled) | 409 `campaign.invalid_transition`, 409 `campaign.deadline_passed` |
| POST `/admin/campaigns/{id}/end` | campaigns.manage | `{ "reason" }` | Active/Paused/Scheduled → Ended | 400 `reason.required`, 409 |
| POST `/admin/campaigns/{id}/archive` | campaigns.manage | – | Draft/Ended → Archived | 409 |
| POST `/admin/campaigns/{id}/duplicate` | campaigns.manage | – | 201 AdminCampaign: Draft copy (`{slug}-copy[-n]`, title "(copy)") with assets, disclosures and the latest rules as v1 | 409 `campaign.no_reward_rules` |

Status changes are conditional updates; every change is audited (`campaign.created`, `campaign.updated`,
`campaign.published`, `campaign.paused`, `campaign.resumed`, `campaign.ended`, `campaign.archived`,
`campaign.duplicated`, `campaign.reward_rules_changed`, `campaign.asset_*`, `campaign.disclosures_replaced`).
`CampaignScheduleJob` (every minute) moves Scheduled → Active when `startsAt ≤ now` and Active → Ended when
`submissionDeadline < now` (audited as system).

### Assets
| Method & path | Body | Response | Errors |
|---|---|---|---|
| POST `/admin/campaigns/{id}/assets` | `{ type, title, url?, fileId?, body?, platform?, sortOrder?, templateId? }` | 201 CampaignAsset | 400 `campaign.asset_url_invalid` (https or `/api/v1/files/{id}` only), `campaign.asset_file_invalid` (must be an uploaded public image), `campaign.asset_body_required` (Caption), `campaign.asset_url_required`, `campaign.asset_platform_invalid`, `campaign.template_not_found`; 409 `campaign.archived` |
| PUT `/admin/campaigns/{id}/assets/{assetId}` | same | CampaignAsset | same + 404 |
| DELETE `/admin/campaigns/{id}/assets/{assetId}` | – | 204 | 404 |
| POST `/admin/campaigns/{id}/assets/reorder` | `{ "assetIds": [ …every asset once, in order… ] }` | CampaignAsset[] | 400 `campaign.reorder_mismatch` |

With `fileId`, `url` becomes `/api/v1/files/{fileId}`. `sortOrder` defaults to last.

### Disclosures
| Method & path | Body | Response | Errors |
|---|---|---|---|
| GET `/admin/campaigns/{id}/disclosures` | – | `[{ id, platform, countryCode, text }]` | 404 |
| PUT `/admin/campaigns/{id}/disclosures` | `{ "disclosures": [{ platform?, countryCode?, text }] }` (replaces the set) | same list | 400 `campaign.duplicate_disclosure` (one per platform/country pair), `campaign.disclosure_required` |

### Categories — `/admin/campaign-categories` (campaigns.manage)
| Method & path | Body | Response | Errors |
|---|---|---|---|
| GET | – | `[{ id, name, slug, description, icon, sortOrder, isActive, campaignCount }]` (incl. inactive) | |
| POST | `{ name, slug?, description?, icon?, sortOrder, isActive }` | 201 same shape | 409 `category.slug_taken` |
| PUT `/{id}` | same | same shape | 404, 409 |
| DELETE `/{id}` | – | `{ "deleted": true, "deactivated": false, "campaignCount": 0 }` — categories used by campaigns are deactivated instead | 404 |

---

## 3. Reward rules

RewardRuleSetInput:
```json
{ "currency": "USD", "dailyCapPerParticipant": 20.0, "weeklyCapPerParticipant": null, "campaignCapPerParticipant": 100.0,
  "rules": [
    { "type": "BaseRate", "amount": 5.0 },
    { "type": "RateOverride", "amount": 7.0, "platform": "TikTok", "countryCode": "PK", "tier": null, "priority": 0, "label": "TikTok PK" },
    { "type": "TimeLimitedBonus", "amount": 1.0, "validFrom": "…Z", "validTo": "…Z" },
    { "type": "FirstPostBonus", "amount": 2.0 },
    { "type": "QualityBonus", "amount": 5.0, "approvalMode": "ManualApproval" } ] }
```
RewardRuleSet (response):
```json
{ "id": "…", "version": 2, "currency": "USD", "dailyCapPerParticipant": 20.0, "weeklyCapPerParticipant": null,
  "campaignCapPerParticipant": null, "effectiveFrom": "…", "createdAt": "…", "createdBy": { "id": "…", "displayName": "…" },
  "reason": "Rate increase", "summary": "v2 USD: base 5.00; 1 override; daily cap 20.00", "isCurrent": true,
  "inUseBySubmissions": 12,
  "rules": [{ "id": "…", "type": "BaseRate", "amount": 5.0, "platform": null, "countryCode": null, "tier": null,
              "validFrom": null, "validTo": null, "approvalMode": "Automatic", "priority": 0, "label": null }] }
```

| Method & path | Permission | Body | Response | Errors |
|---|---|---|---|---|
| GET `/admin/campaigns/{id}/reward-rules` | campaigns.view | – | RewardRuleSet[] newest first | 404 |
| POST `/admin/campaigns/{id}/reward-rules` | rewards.edit | RewardRuleSetInput + `reason` (5–500) + `confirm: true` + optional `baseVersion` (the version the editor showed; a newer saved version → 409 `reward.version_conflict` instead of silently replacing it) | 201 RewardRuleSet (version max+1, effective now; audited `campaign.reward_rules_changed` with before/after) | 400 `confirmation.required`, `reward.invalid_rules` (`errors.rules[]`), `campaign.budget_currency_mismatch`; 409 `reward.currency_locked`, `reward.version_conflict`, `campaign.archived` |
| POST `/admin/campaigns/{id}/reward-rules/preview` | campaigns.manage | `{ platform, countryCode, tier, postedAt?, isFirstApprovedPost, earnedToday, earnedThisWeek, earnedInCampaign, qualityBonusRequested?, ruleSetVersion?, draft?: RewardRuleSetInput, campaignBudgetRemaining? }` | RewardQuote | 400 `reward.invalid_rules`, `reward.quality_bonus_not_configured`; 404 `reward.version_not_found` |

RewardQuote:
```json
{ "ruleSetId": "…", "ruleSetVersion": 2, "currency": "USD",
  "lines": [{ "type": "PostReward", "ruleId": "…", "amount": 2.0, "uncappedAmount": 5.0, "requiresApproval": false, "label": "Post reward" }],
  "total": 2.0, "appliedCaps": ["daily_cap"], "ruleSetSummary": "v2 USD: …" }
```
(`ruleSetId`/`ruleSetVersion` are null for `draft` previews.) Existing submissions keep their captured version;
approved earnings are never changed. See `docs/REWARD_ENGINE.md`.

---

## 4. Files

### GET /files/{id} — anonymous route, access checked per file
* CampaignAsset/ContentImage files marked public: anyone (`Cache-Control: public, max-age=86400`).
* SubmissionScreenshot: the owner, users with `submissions.review`, or campaign managers (`campaigns.manage`) for
  submissions to campaigns they created (`Cache-Control: private, max-age=300`).
* Anything else / not allowed / unknown → 404 `file.not_found` (existence is never revealed).

Headers: real `Content-Type` (image/png|jpeg|webp), `Content-Disposition: inline`, `X-Content-Type-Options: nosniff`,
`Content-Security-Policy: default-src 'none'; sandbox`. Private screenshots need the bearer token, so the SPA must
fetch them (e.g. to a blob URL) rather than use a plain `<img src>`.

### POST /admin/files — `campaigns.manage` or `content.manage` (multipart/form-data)
Fields: `file` (required), `purpose` = CampaignAsset (default for campaign managers) | ContentImage.
```json
{ "id": "…", "url": "/api/v1/files/…", "contentType": "image/png", "sizeBytes": 123456, "width": 1200, "height": 628,
  "sha256": "…64 hex…", "originalFileName": "banner.png", "purpose": "CampaignAsset", "isPublic": true, "createdAt": "…" }
```
Upload rules (also for screenshots): ≤ 10 MB (`file.too_large`); type detected from magic bytes — PNG, JPEG, WebP
only, client type/extension ignored (`file.unsupported_type`); 200×200 to 10000×10000 px (`file.too_small`,
`file.too_large_dimensions`); empty → `file.empty`; `file.required`, `file.invalid_purpose`. EXIF/GPS/XMP/IPTC and
text metadata is removed without re-encoding before storage; `sizeBytes` and `sha256` describe the stripped image.
Files are stored privately under server-generated keys `yyyy/MM/{guid}.{ext}` in `Storage:RootPath`.

---

## 5. Participant submissions — `/me/submissions` (`participant.portal`)

### POST /me/submissions — multipart/form-data, rate limited (30/min/user)
Fields: `campaignId`, `socialAccountId`, `platform`, `postUrl`, `postedAt` (ISO UTC), `captionText?`,
`experimentVariantId?` (kept only if it belongs to a Running experiment of the campaign), `screenshot` (file).
Response 201 MySubmissionDetail (below).

Checks and codes, in order:
| Code | Status | When |
|---|---|---|
| `campaign.not_found` | 404 | unknown, Draft or Archived campaign |
| `submission.campaign_closed` | 409 | not Active, before start, or after `submissionDeadline` |
| `submission.social_account_invalid` | 400 | the account is not one of the caller's |
| `submission.platform_not_allowed` | 400 | platform not in the campaign |
| `submission.platform_mismatch` | 400 | account platform ≠ `platform` |
| `submission.social_account_inactive` | 400 | account deactivated |
| `submission.account_ineligible` | 409 | account fails campaign rules (`errors.reasons[]`: "code: message") |
| `submission.not_eligible` | 409 | participant fails campaign rules (`errors.reasons[]`) |
| `submission.invalid_url` | 400 | not an absolute http(s) URL, has credentials (`user:pw@`), or its post key would exceed 768 chars |
| `submission.url_platform_mismatch` | 400 | host is not exactly one of the platform's hosts or an allow-listed subdomain of one (e.g. `de.instagram.com` is refused; a trailing dot is ignored), or the URL is the site root |
| `submission.posted_at_in_future` | 400 | `postedAt` > now + 10 min |
| `submission.posted_at_too_old` | 400 | `postedAt` < submission time − 7 days (campaign-independent, see REWARD_ENGINE.md) |
| `submission.duplicate_url` | 409 | the post's **canonical key** was already submitted by anyone (see below); the unique, case-sensitive index is the final guard |
| `submission.screenshot_required` | 400 | campaign requires a screenshot |
| `submission.limit_reached` | 409 | non-Rejected submissions ≥ `maxSubmissionsPerParticipant` (serialized per participant) |
| `campaign.no_reward_rules` | 409 | campaign has no rule set |
| `file.*` | 400 | screenshot invalid (see Files) |

**Canonical post key** (`Domain/Submissions/PlatformUrlRules.cs`, stored in `NormalizedPostUrl`, which keeps its
historical name; binary `utf8mb4_bin` collation because ids such as Instagram shortcodes are case-sensitive):

| Platform | URL forms | Key |
|---|---|---|
| Instagram | `/p/{code}`, `/reel/`, `/reels/`, `/tv/` (optionally after `/{user}`) | `instagram:{code}` |
| TikTok | `/@user/video/{id}`, `/video/{id}` (`/photo/` too) | `tiktok:{id}` |
| TikTok short links | `vm.tiktok.com/{code}`, `vt.tiktok.com/{code}`, `tiktok.com/t/{code}` | `tiktok-short:{code}` + flag |
| X / Twitter | `/{user}/status/{id}` | `x:{id}` |
| YouTube | `watch?v={id}`, `youtu.be/{id}`, `/shorts/{id}`, `/live/{id}` | `youtube:{id}` |
| Facebook | `story.php`/`permalink.php?story_fbid=&id=`, `/posts/{id}`, `/reel/{id}`, `/watch?v=`, `photo.php?fbid=` | `facebook:story:{id}:{story_fbid}`, `facebook:post:{id}`, `facebook:reel:{id}`, `facebook:video:{v}`, `facebook:photo:{fbid}` |
| LinkedIn | `urn:li:activity:{id}` (e.g. `/feed/update/…`), `/posts/…-activity-{id}-…` | `linkedin:activity:{id}` |
| Threads | `/@user/post/{code}` | `threads:{code}` |
| Pinterest | `/pin/{id}` | `pinterest:{id}` |
| Snapchat | `/spotlight/{id}` | `snapchat:spotlight:{id}` |
| anything else | – | normalized URL: `https://{host without subdomain}{path without trailing slash}`, only identity query parameters kept (YouTube `v`; Facebook `story_fbid`, `id`, `v`, `fbid`) |

Allowed subdomains: Instagram `www`, `m`; TikTok `www`, `m`, `vm`, `vt`; X/Twitter `www`, `m`, `mobile`; Facebook
`www`, `m`, `mobile`, `web`, `mbasic`; LinkedIn/YouTube/Pinterest `www`, `m`; Threads/youtu.be/instagr.am `www`;
Snapchat `www`, `story`, `web`. Short-link hosts (`fb.watch`, `pin.it`, `lnkd.in`) get a `{platform}-short:{code}` key.
`postUrl` itself is stored as entered; `Normalization.PostUrl` is only a display helper.

On success the submission captures the current rule set id/version and an estimated reward, gets risk flags
(reviewer-only evidence; never auto-rejects): DuplicateScreenshot 40, RepeatedContent 20, OutsideCampaignWindow 30,
PostedLongBeforeSubmission 15 (`postedAt` more than 48 h before the submission), UnresolvedShortLink 10 (short link a
reviewer must open), AccountNotVerified 10 (only when the campaign doesn't require verification),
HighSubmissionVelocity 15 (> `fraud.submissionVelocityPer24h` in 24h), NewParticipant 5 (joined < 7 days). A "submitted" event, audit entry and
in-app `submission.received` notification are written; `SubmissionCreated` is published after commit.

### MySubmissionDetail
```json
{
  "id": "…", "campaign": { "id": "…", "slug": "…", "title": "…" }, "platform": "Instagram",
  "socialAccount": { "id": "…", "platform": "Instagram", "handle": "sara" },
  "postUrl": "…", "postedAt": "…", "captionText": "…", "screenshotUrl": "/api/v1/files/…",
  "status": "Rejected", "submittedAt": "…", "decidedAt": "…", "decisionReason": "…", "correctionCount": 0,
  "estimatedReward": 7.0, "currency": "USD", "rewardRuleSetVersion": 1,
  "liveCheck": { "status": "NotRequired", "dueAt": null, "checkedAt": null },
  "timeline": [{ "action": "submitted", "fromStatus": null, "toStatus": "Pending", "reason": null, "actor": "You", "at": "…" },
               { "action": "rejected", "fromStatus": "UnderReview", "toStatus": "Rejected", "reason": "…", "actor": "Reviewer", "at": "…" }],
  "earnings": [{ "id": "…", "type": "PostReward", "amount": 5.0, "currency": "USD", "status": "Approved", "createdAt": "…" }],
  "appeal": { "id": "…", "status": "Open", "decisionAppealed": "Rejected", "reason": "…", "resolutionNote": null, "createdAt": "…", "resolvedAt": null } | null,
  "canEdit": false, "canAppeal": true, "appealDeadline": "…", "canWithdraw": false
}
```
Actors are `You`, `Reviewer` or `System` (staff names are hidden). Risk score and flags are never included.
Timeline actions: submitted, claimed, approved, correction_requested, rejected, resubmitted, appealed,
appeal_overturned, appeal_upheld, reversed, live_check_confirmed, live_check_removed, withdrawn.
`canWithdraw`: status Pending, UnderReview or NeedsCorrection (nothing decided yet).
`canAppeal`: status Rejected or Reversed, within `review.appealWindowDays` (default 14) of `decidedAt`, no open
appeal and no appeal filed since that decision.

| Method & path | Body | Response | Errors |
|---|---|---|---|
| GET `/me/submissions?status=&campaignId=` | – | Paged `{ id, campaign{id,slug,title}, platform, postUrl, status, submittedAt, estimatedReward, currency, decisionReason }` newest first | |
| GET `/me/submissions/{id}` | – | MySubmissionDetail | 404 (not owner) |
| PUT `/me/submissions/{id}` (multipart, rate limited) | any of `postUrl`, `postedAt`, `captionText`, `screenshot` | MySubmissionDetail (status Pending, `correctionCount`+1, original rule version kept, flags recomputed, estimate re-priced, event `resubmitted`) | 409 `submission.not_editable` (not NeedsCorrection) + all create checks (URL uniqueness excludes itself; `postedAt` rules are re-run against the original `submittedAt`, which a correction doesn't change) |
| POST `/me/submissions/{id}/withdraw` (rate limited) | `{ "confirm": true, "reason": "optional, ≤ 1000" }` | MySubmissionDetail (status `Withdrawn`, event `withdrawn` with the reason, audited `submission.withdrawn`) | 400 `confirmation.required`, validation; 404 (not owner); 409 `submission.not_withdrawable` (already Approved/Rejected/Reversed/Withdrawn), `submission.changed` (kept changing under concurrent claims) |
| POST `/me/submissions/{id}/appeal` (rate limited) | `{ "reason": "20–2000 chars" }` | MySubmissionDetail (the appeal keeps the full text; the timeline/audit copy is cut to 1000 chars with "…") | 400 validation, 409 `appeal.not_allowed` |

---

## 6. Review — `/review`

| Method & path | Permission | Body | Response | Errors |
|---|---|---|---|---|
| GET `/review/queue?status=Pending\|UnderReview&campaignId=&platform=&minRisk=&flagged=&assignedToMe=&claimedByMe=&sort=oldest\|risk` | submissions.review | – | Paged ReviewQueueItem | |
| POST `/review/submissions/{id}/claim` | submissions.review | – | Claim | 403 `review.self_review`, 409 `review.claimed_by_other` (`errors.claimedBy`, `claimedByUserId`, `claimExpiresAt`), 409 `review.already_decided`, 404 |
| POST `/review/submissions/{id}/release` | submissions.review | – | 204 | 409 `review.not_claimed`, 404 |
| GET `/review/submissions/{id}` | submissions.review | – | ReviewDetail | 404 |
| POST `/review/submissions/{id}/decision` | submissions.review | `{ decision: Approve\|RequestCorrection\|Reject, reason? (≤900), qualityBonusAmount?, concurrencyStamp }` | DecisionResult | 400 `review.reason_required` (missing/blank for RequestCorrection/Reject), `review.reason_too_short` (<5 chars after trimming), `review.quality_bonus_requires_approval`, `reward.quality_bonus_not_configured`; 403 `review.self_review`; 409 `review.already_decided`, `review.claimed_by_other`, `participant.not_active` (Approve only); 409 `fx.rate_missing` |
| GET `/review/live-checks?due=true` | submissions.review | – | Paged LiveCheckItem | |
| POST `/review/submissions/{id}/live-check` | submissions.review | `{ result: ConfirmedLive\|Removed, note? (≤900) }` (note ≥5 after trimming for Removed) | LiveCheckResult | 400 `review.reason_required`, `review.reason_too_short`; 403 `review.self_review`; 409 `review.live_check_not_due` (before `max(postedAt, submittedAt) + minPostLiveHours`), `review.live_check_not_pending`, `participant.not_active` (ConfirmedLive only) |
| POST `/review/submissions/{id}/reverse` | submissions.reverse | `{ reason (5–900 after trimming), confirm: true }` | ReverseResult | 400 `confirmation.required`, `review.reason_too_short`; 403 `review.self_review`; 409 `review.not_approved`, 409 `ledger.in_payout_batch` |
| GET `/review/appeals?status=Open` | appeals.resolve | – | Paged AppealListItem (oldest first; `status` omitted = Open) | |
| GET `/review/appeals/{id}` | appeals.resolve | – | `{ appeal: ReviewAppeal, originalDecidedBy: {id,displayName}\|null, review: ReviewDetail }` | 404 |
| POST `/review/appeals/{id}/resolve` | appeals.resolve | `{ outcome: Upheld\|Overturned, note (5–900 after trimming), concurrencyStamp }` | `{ appeal: ReviewAppeal, submissionStatus, earnings: [ReviewEarning] }` | 400 `review.reason_too_short`; 403 `review.self_review` (caller is the submission's or the appeal's participant), 403 `appeal.same_reviewer` (the original decider — Admins included); 409 `appeal.already_resolved`, `appeal.submission_changed`; Overturned only: 409 `participant.not_active`, `appeal.campaign_archived`, `appeal.submission_limit_reached` |
| GET `/review/reviewers` | review.assign | – | `[{ id, displayName, email, assignedOpen, decisionsToday }]` | |
| POST `/review/assign` | review.assign | `{ submissionIds: [..≤200], reviewerId }` | `{ updated: 3, skippedIds: [ids not open] }` | 400 `review.not_a_reviewer` |
| GET `/review/stats` | submissions.review | – | ReviewStats | |

### Shapes
ReviewQueueItem:
```json
{ "id": "…", "campaign": { "id": "…", "title": "…" }, "participant": { "id": "…", "displayName": "…", "countryCode": "PK" },
  "platform": "Instagram", "handle": "sara", "status": "Pending", "submittedAt": "…", "riskScore": 45,
  "flagTypes": ["DuplicateScreenshot", "NewParticipant"], "claimedBy": { "id": "…", "displayName": "…" } | null,
  "claimExpiresAt": "…" | null, "assignedReviewer": { … } | null, "correctionCount": 0 }
```
`flagged=true` = has unresolved flags. `claimedByMe=true` = only submissions the caller holds an active (unexpired)
claim on. `claimedBy` only while the claim is active.

Claim: `{ "submissionId": "…", "status": "UnderReview", "claimedBy": { "id", "displayName" }, "claimExpiresAt": "…", "concurrencyStamp": "…" }`
(claims last `review.claimMinutes`, default 15; claiming again extends; claiming does not change the stamp).

ReviewDetail:
```json
{
  "requirements": { "campaignId": "…", "campaignTitle": "…", "postingInstructions": "…", "requiredHashtags": "…", "requiredMentions": "…",
                    "disclosureText": "#ad" /* for the submission's platform + participant country */, "allowedPlatforms": ["Instagram"],
                    "startsAt": "…", "endsAt": "…", "submissionDeadline": "…", "minPostLiveHours": 0, "requireScreenshot": true },
  "submission": { "id": "…", "postUrl": "…", "normalizedPostUrl": "…", "platform": "Instagram", "postedAt": "…", "captionText": "…",
                  "screenshotUrl": "/api/v1/files/…", "screenshotSha256": "…", "submittedAt": "…", "status": "UnderReview",
                  "correctionCount": 0, "riskScore": 45, "rewardRuleSetVersion": 1, "estimatedReward": 5.0, "currency": "USD",
                  "decidedAt": null, "decidedBy": null, "decisionReason": null, "liveCheckStatus": "NotRequired", "liveCheckDueAt": null,
                  "assignedReviewer": null, "concurrencyStamp": "…",
                  "claim": { "claimedBy": { … } | null, "claimExpiresAt": "…" | null, "isMine": true, "isActive": true } },
  "account": { "id": "…", "handle": "sara", "profileUrl": "…", "platform": "Instagram", "accountCreatedAt": "…", "accountAgeDays": 400,
               "followerCount": 5000, "verificationStatus": "Verified", "isActive": true },
  "participant": { "id": "…", "displayName": "…", "email": "…", "countryCode": "PK", "tier": "Standard", "joinedAt": "…",
                   "approvedCount": 3, "rejectedCount": 0, "reversedCount": 0,
                   "status": "Active" /* Active|Suspended|Deactivated — only Active participants can be approved */ },
  "history": [{ "id": "…", "campaign": { "id", "title" }, "status": "Approved", "submittedAt": "…", "postUrl": "…" }],
  "flags": [{ "id": "…", "type": "DuplicateScreenshot", "detail": "…", "weight": 40, "createdAt": "…", "resolved": false, "resolvedAt": null, "resolutionNote": null }],
  "relatedSubmissions": [{ "id": "…", "match": "screenshot|content|screenshot_and_content", "campaign": { … }, "participant": { "id", "displayName" }, "status": "…", "submittedAt": "…" }],
  "events": [{ "action": "claimed", "fromStatus": "Pending", "toStatus": "UnderReview", "reason": null, "actor": { "id", "displayName" } | null, "at": "…" }],
  "rewardQuote": { …RewardQuote from the recorded version, with current aggregates/caps… } | null,
  "earnings": [{ "id": "…", "type": "PostReward", "amount": 5.0, "currency": "USD", "status": "Approved", "createdAt": "…", "rewardRuleSetVersion": 1 }],
  "appeals": [ ReviewAppeal ],
  "qualityBonusMax": 10.0 /* Amount of the QualityBonus rule of the recorded rule-set version (its currency); null = no quality bonus */
}
```
ReviewAppeal: `{ id, status, decisionAppealed, reason, createdAt, resolvedBy: {id,displayName}|null, resolutionNote, resolvedAt, concurrencyStamp }`.

DecisionResult:
```json
{ "submissionId": "…", "status": "Approved", "decidedAt": "…", "decisionReason": null, "concurrencyStamp": "…",
  "reward": { …RewardQuote… } | null /* approvals only */, "earnings": [ ReviewEarning ],
  "liveCheckStatus": "Pending", "liveCheckDueAt": "…" }
```
Decision semantics: the caller must hold the claim or the submission must be unclaimed/expired (claimed
atomically). The campaign row is locked (`SELECT … FOR UPDATE`) and the submission is updated conditionally
(`Status IN (Pending, UnderReview) AND ConcurrencyStamp = @stamp`), so of two simultaneous decisions exactly one
succeeds (the other gets 409 `review.already_decided`). Approval prices the recorded rule-set version and writes
ledger entries (keys in REWARD_ENGINE.md); when `minPostLiveHours > 0` all earnings start PendingApproval and
`liveCheckStatus = Pending`, due at `max(postedAt, submittedAt) + minPostLiveHours` (a back-dated `postedAt` can't shorten
it; confirmation re-checks this bound). A 0 total (caps/budget) still approves, with
`reward.appliedCaps` and the caps in the event reason. Every decision resolves open flags, adds an event, audits
(`submission.approved|correction_requested|rejected`) and notifies the participant (in-app + email).
`SubmissionApproved` is published after commit.

**Withdrawal by the participant.** Pending, UnderReview and NeedsCorrection submissions can be withdrawn
(`POST /me/submissions/{id}/withdraw`). It is one conditional update on the status read just before, and a decision
is conditional on Pending/UnderReview and the concurrency stamp that the withdrawal replaces, so when a participant
withdraws while a reviewer decides, exactly one wins: the reviewer gets 409 `review.already_decided` ("The participant
withdrew this submission…"), or the participant gets 409 `submission.not_withdrawable`. A withdrawn submission leaves
the review queue (it is visible with `?status=Withdrawn`; the workspace shows "Withdrawn by participant"), its claim
and open flags are cleared, it has no earnings (none exist before approval), it stops counting toward
`maxSubmissionsPerParticipant`, and its post key is released (`withdrawn:{id}:{key}`) so the same post can be submitted
again. Withdrawal is final.

Integrity rules for every review command: staff can never act on their own submission (claim, decision, live
check, reversal, appeal resolution → 403 `review.self_review`). Approve, appeal overturn and live-check
confirmation read the participant's `Status` inside the transaction (shared row lock) and refuse with 409
`participant.not_active` unless it is Active; reject, correction, removal and reversal stay allowed. Reasons are
capped at 900 chars on input and every composed string written to `SubmissionEvent.Reason`,
`Submission.DecisionReason`, the audit reason (1000) or `Appeal.ResolutionNote` (2000) is cut with "…" to fit.

LiveCheckItem: `{ submissionId, campaign{id,title}, participant{id,displayName}, platform, postUrl, postedAt, decidedAt, dueAt, isDue }`.
LiveCheckResult: `{ submissionId, status: Approved|Reversed, liveCheckStatus, earnings: [ReviewEarning] }` — ConfirmedLive
approves pending earnings except ManualApproval bonuses; Removed reverses the submission. `LiveCheckReminderJob`
(hourly) sends each reviewer at most one in-app `review.live_check_due` notification per UTC day while checks are due.

ReverseResult: `{ submissionId, status: "Reversed", earnings: [ReviewEarning] }` — every live earning is reversed via
the ledger (unpaid → Reversed with a zero-sum negative leg; paid → negative clawback entry); participant notified
(`submission.reversed`); `SubmissionReversed` published after commit.

Appeal resolution: Overturned on a Rejected or Reversed submission approves it with fresh earnings (keys suffixed
`:appeal:{appealId}`; old reversed entries stay reversed; the first-post bonus follows the usual rule — it is paid
again only if every earlier first-post bonus was reversed, under `firstpost:{campaignId}:{userId}:{n}`). Before
overturning, the participant must be Active (`participant.not_active`), the campaign not Archived
(`appeal.campaign_archived`) and, for a Rejected submission, the participant's Approved/Pending/UnderReview/
NeedsCorrection submissions in the campaign must be below `maxSubmissionsPerParticipant`
(`appeal.submission_limit_reached`). Upheld changes nothing. Participant notified (`appeal.resolved`), audited `appeal.resolved`.

AppealListItem: `{ id, submissionId, campaign{id,title}, participant{id,displayName}, status, decisionAppealed, reason, createdAt, originalDecidedBy{id,displayName}|null, concurrencyStamp }`.

ReviewStats:
```json
{ "myDecisionsToday": 12, "queueByStatus": { "Pending": 30, "UnderReview": 4, "NeedsCorrection": 2 },
  "oldestPendingSubmittedAt": "…", "oldestPendingAgeHours": 5.25, "dueLiveChecks": 3, "openAppeals": 1, "claimedByMe": 1 }
```
