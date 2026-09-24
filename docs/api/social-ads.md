# API: Social media management and paid advertising

Base path `/api/v1`. JSON is camelCase, enums are strings, timestamps are UTC ISO-8601, dates (`DateOnly`) are
`yyyy-MM-dd`. Money is a JSON number with its currency code beside it. Errors are RFC 7807 problems with a stable
`code` (and `errors` for field or row details). Editable records carry `concurrencyStamp`; a stale stamp returns
`409 concurrency.conflict`.

**Tenancy.** Every client-owned record is scoped with `IClientScope`. Staff see only the clients they may access,
and a record of another client answers `404` (never `403`) so its existence does not leak. Client-portal users see
only their own organizations.

**Permissions.** Only existing permissions are used:

| permission | grants |
|---|---|
| `social.manage` | every read, drafting, editing, submitting, comments, library, listening, inbox, analytics |
| `social.publish` | approve (internal), schedule, queue, unschedule, mark as published, retry, connect/disconnect profiles, queue slots, client social settings |
| `ads.manage` | everything under `/agency/ads` |
| `integrations.manage` | pasting a profile access token manually (`POST /agency/social/profiles/{id}/token`) |
| `reports.manage` | the KPI endpoints used by reports (`/agency/social/clients/{id}/kpis`, `/agency/ads/clients/{id}/kpis`), accepted as an alternative to `social.manage` / `ads.manage` |
| `client.portal` | `/client/social/*`; approving or requesting changes also needs the Approver or Owner duty in that organization |

**Measurement labels.** Every metric states where it came from: `Measured` (the platform API, or a platform export
imported as CSV) or `Manual` (typed in). Nothing is labelled Measured unless a provider or its export produced it.

---

## 1. Social: profiles and connection (`/agency/social`)

| method | path | permission | notes |
|---|---|---|---|
| GET | `/clients` | social.manage | clients the caller can access (id, name, slug, country, currency, time zone) |
| GET | `/clients/{clientId}/profiles` | social.manage | brand profiles with `connectionState` (`AppCredentialsRequired`, `NotConnected`, `Connected`, `Disconnected`, `Error`), `publishingSupported`, queue slots |
| POST | `/clients/{clientId}/profiles` | social.manage | `{ network, handle, displayName?, profileUrl?, externalId? }` — a profile can exist before it is connected (manual workflow) |
| PUT | `/profiles/{id}` | social.manage | edit handle/name/url/active; stamp required |
| DELETE | `/profiles/{id}` | social.publish | archives (keeps post history) |
| POST | `/profiles/{id}/connect/start` | social.publish | returns `{ authorizeUrl }`. `409 social.app_credentials_required` when the network's developer app is not configured |
| POST | `/oauth/callback` | social.publish | `{ code, state }` from the callback page. `400 social.oauth_state_invalid` (tampered, expired, other user, other profile) |
| POST | `/profiles/{id}/token` | integrations.manage | `{ accessToken, refreshToken?, externalId?, expiresAt? }` — paste a long-lived token instead of OAuth |
| POST | `/profiles/{id}/disconnect` | social.publish | deletes the stored token |
| PUT | `/profiles/{id}/queue-slots` | social.publish | `{ slots: [{ day: "Tuesday", time: "09:00" }] }` in the client's time zone |
| GET / PUT | `/clients/{clientId}/settings` | manage / publish | `{ requireClientApproval }` |
| GET / POST | `/clients/{clientId}/campaigns` | social.manage | social campaigns with UTM defaults |
| PUT | `/campaigns/{id}` | social.manage | |

OAuth `state` is `HMAC-SHA256(purpose|profileId|userId|expiresAt|nonce)` signed with `SocialMedia:OAuthStateSecret`
(or a key derived from `Jwt:SigningKey`), valid 15 minutes and bound to the user who started it. X uses PKCE; the
verifier is derived from the state nonce, so nothing is stored between start and callback. The provider redirects to
the web page `/agency/social/connect/callback`, which POSTs `code` + `state` to the authenticated callback endpoint,
so there is no anonymous API route.

## 2. Social: posts, workflow and publishing

| method | path | permission | notes |
|---|---|---|---|
| GET | `/presets` | social.manage | per-network limits (text, title, hashtags, mentions, media count, aspect ratio, link handling, first comment) and recommended times, each with its `source` |
| POST | `/validate` | social.manage | `{ clientAccountId, campaignId?, autoAppendUtm, variants[] }` → per-variant `finalText`, `textLength`, `issues[]` |
| GET | `/posts` | social.manage | paged; `clientId`, `status`, `search` |
| GET | `/posts/{id}` | social.manage | full post: variants, validation, comments, `allowedActions` |
| POST | `/posts` | social.manage | creates a Draft (see body below) |
| PUT | `/posts/{id}` | social.manage | content edits after Draft send the post back to Draft |
| DELETE | `/posts/{id}` | social.manage | not while Publishing/Published |
| POST | `/posts/{id}/submit` | social.manage | Draft → InternalReview |
| POST | `/posts/{id}/approve` | social.publish | InternalReview → ClientApproval (when the client requires approval) or Approved |
| POST | `/posts/{id}/request-changes` | social.manage | `{ comment }` required; back to Draft |
| POST | `/posts/{id}/schedule` | social.publish | Approved → Scheduled; `{ scheduledAt? }` |
| POST | `/posts/{id}/queue` | social.publish | schedules into the next free queue slot of its profiles |
| POST | `/posts/{id}/unschedule` | social.publish | Scheduled → Approved |
| POST | `/posts/{id}/reschedule` | social.manage | `{ scheduledAt, concurrencyStamp }` (calendar drag / keyboard) |
| POST | `/posts/{id}/mark-published` | social.publish | `{ variantId, url, externalPostId? }` — `url` must be https |
| POST | `/posts/{id}/retry` | social.publish | `{ confirmNotPublished }` — required when the outcome is Unknown |
| POST | `/posts/{id}/comments` | social.manage | `{ body, internal }` |
| GET | `/posts/{id}/attempts` | social.manage | every provider call with request summary, status and response |
| GET | `/calendar` | social.manage | `clientId?, from, to` → `{ posts, awarenessDays, bestTimes }` |
| GET | `/approvals` | social.manage | posts in InternalReview / ClientApproval |
| GET | `/publishing` | social.manage | Publishing, Failed and recently Published posts |

Post body:

```json
{
  "clientAccountId": "…", "title": "Autumn launch", "scheduledAt": null, "campaignId": null, "autoAppendUtm": true,
  "isEvergreen": false, "evergreenIntervalDays": 30, "evergreenMaxRepeats": 3,
  "variants": [
    { "profileId": "…", "text": "…", "title": null, "mediaIds": [], "altTexts": [], "link": "https://…",
      "firstComment": null, "hashtags": ["#autumn"], "mentions": [] }
  ],
  "concurrencyStamp": null
}
```

Workflow: `Draft → InternalReview → (ClientApproval →) Approved → Scheduled → Publishing → Published | Failed`.
Requesting changes at either review step returns the post to Draft with the comment; a Failed post can be retried
(back to Scheduled). A post is Published only when every variant is. An invalid move is `409 social.invalid_transition`. A post whose
variants fail validation cannot be submitted (`400 social.validation_failed`).

**Publishing** runs every minute. A due post is claimed with a conditional update (`Scheduled → Publishing` with a
claim id), then each variant (`Pending → Publishing`). Only the claimant sends, so a post is sent at most once even
with several app instances. A claim older than 15 minutes is recovered as **Unknown**, never re-sent automatically;
a person checks the network and then retries with `confirmNotPublished: true` or marks it as published. Transient
errors (rate limit, 5xx, network) retry after 2, 10 and 30 minutes (4 attempts by default); authorization errors
stop immediately and flag the profile and its stored credential as `Error`. A variant is only `Published` when the
provider returned a post id; otherwise it is `NotConfigured` and the UI offers "mark as published".

**Evergreen** (hourly): a published evergreen post is copied as a new Scheduled post after `evergreenIntervalDays`,
up to `evergreenMaxRepeats`. The copy has a unique `(recycledFromPostId, recycleNumber)` key, so a re-run never
duplicates.

## 3. Social: library

| method | path | notes |
|---|---|---|
| GET | `/clients/{clientId}/media` | paged; `kind`, `tag`, `search` |
| POST | `/clients/{clientId}/media/upload` | multipart image (validated and re-encoded by the file service), `altText?`, `tags?` |
| POST | `/clients/{clientId}/media/url` | `{ url, kind: Video|Image, altText?, width?, height? }` — videos are referenced by https URL |
| PUT / DELETE | `/media/{id}` | |
| GET | `/media/{id}/content` | private image bytes (auth required) |
| GET / POST | `/clients/{clientId}/hashtag-sets`, PUT / DELETE `/hashtag-sets/{id}` | |
| GET / POST | `/clients/{clientId}/snippets`, PUT / DELETE `/snippets/{id}` | caption snippets |

All require `social.manage`.

## 4. Social: listening, inbox, competitors (`social.manage`)

| method | path | notes |
|---|---|---|
| GET / POST | `/clients/{clientId}/listening/queries`, DELETE `/listening/queries/{id}` | keywords, excluded words, networks |
| GET | `/clients/{clientId}/listening/mentions` | paged; `sentiment`, `queryId`, `network` |
| POST | `/clients/{clientId}/listening/mentions` | log a mention by hand; sentiment is estimated by the lexicon scorer |
| PUT | `/listening/mentions/{id}/sentiment` | human override |
| GET | `/clients/{clientId}/listening/summary` | volume and sentiment share per day |
| POST | `/clients/{clientId}/listening/sync` | `{ configured, imported, message }` — `configured: false` (nothing imported) without a listening provider |
| GET / POST | `/clients/{clientId}/inbox` | messages and comments; POST logs one by hand |
| PATCH | `/inbox/{id}` | status, assignee |
| POST | `/inbox/{id}/replies` | `{ body, sendViaApi }`. `sendViaApi: true` without a provider → `409 social.reply_not_configured`; otherwise the reply is recorded as sent manually |
| POST | `/clients/{clientId}/inbox/sync` | same result shape; `configured: false` without an inbox provider |
| GET / POST | `/clients/{clientId}/competitors`, DELETE `/competitors/{id}` | |
| PUT | `/competitors/{id}/snapshots` | `{ date, followers, posts, engagementRate }` (Manual) |

The sentiment scorer is a transparent lexicon with negation, intensifiers and emoji. It returns the matched terms,
and the UI calls it an estimate.

## 5. Social: analytics

| method | path | permission | notes |
|---|---|---|---|
| GET | `/clients/{clientId}/analytics` | social.manage | `from, to, profileId?` → KPIs, daily series, top posts, source label |
| GET | `/clients/{clientId}/kpis` | social.manage or reports.manage | compact KPI block for reports |
| GET | `/clients/{clientId}/best-times` | social.manage | engagement by weekday/hour from your own posts (falls back to the preset times) |
| POST | `/profiles/{profileId}/metrics/import/preview` | social.manage | `{ csv, kind: Post|Profile }` → mapping, sample, errors |
| POST | `/profiles/{profileId}/metrics/import` | social.manage | upserts by (post, date) / (profile, date); labelled Measured (`PlatformExport`) |
| GET | `/clients/{clientId}/metric-imports` | social.manage | import history |

`SocialMetricsSyncJob` (daily) reads Facebook/Instagram insights for connected Meta profiles. Other networks rely on
CSV import.

## 6. Client portal (`/client/social`, `client.portal`)

| method | path | notes |
|---|---|---|
| GET | `/organizations` | the caller's organizations with `role` and `canApprove` |
| GET | `/calendar` | `clientId, from, to` — posts from ClientApproval onwards (no internal drafts) |
| GET | `/approvals` | posts awaiting client approval |
| GET | `/posts/{id}` | variants and non-internal comments; a Failed post reads `Scheduled` (variants `Pending`, `failureKind: None`, no failure reasons), as in the calendar and lists |
| POST | `/posts/{id}/approve` | Approver/Owner; ClientApproval → Approved |
| POST | `/posts/{id}/request-changes` | Approver/Owner; `{ comment }` required; back to Draft |
| POST | `/posts/{id}/comments` | |
| GET | `/media?postId=`, `/media/{id}/content` | only media used by posts visible to the client |
| GET | `/performance` | `clientId, from, to` → social KPIs + ads summary (spend, conversions, CPA, ROAS in the client currency), each with its source label |

---

## 7. Ads: accounts, structure and reporting (`/agency/ads`, `ads.manage`)

| method | path | notes |
|---|---|---|
| GET | `/clients`, `/staff` | pickers |
| GET | `/accounts` | `clientId?` — with last-30-day totals and KPIs |
| POST | `/accounts` | `{ clientAccountId, platform, externalAccountId, name, currency, timeZone, managerUserId? }` |
| PUT | `/accounts/{id}` | stamp required |
| GET | `/accounts/{id}` | detail with daily series |
| POST | `/accounts/{id}/sync` | `{ from?, to? }` → `{ outcome: Synced|NotConfigured|AuthorizationError|Failed, rows, message }` |
| GET | `/accounts/{id}/campaigns` | campaigns with totals, KPIs and a 30-day sparkline |
| POST | `/accounts/{id}/campaigns` | naming convention enforced (`400 ads.naming_convention` with an example) |
| PUT | `/campaigns/{id}` | |
| GET / POST | `/campaigns/{id}/ad-groups`, `/ad-groups/{id}/ads` | structure mirror |
| GET | `/overview` | `from, to` — all clients in the agency currency, per platform |
| GET | `/clients/{clientId}/kpis` | `ads.manage` or `reports.manage` — `from, to, currency?` |

KPIs: CTR = clicks/impressions, CPC = spend/clicks, CPM = spend/impressions×1000, CPA = spend/conversions,
ROAS = value/spend, CVR = conversions/clicks. Each is `null` when its denominator is zero (never a fake 0).
Currency conversion uses the rate on the last day of each month (`IExchangeRateProvider`) and rounds per currency;
`fxMissing` lists pairs without a rate instead of guessing.

## 8. Ads: CSV import

| method | path | notes |
|---|---|---|
| GET | `/import/templates` | `google-ads`, `meta-ads`, `generic` with header aliases |
| GET | `/import/templates/{id}/sample.csv` | sample file |
| POST | `/accounts/{id}/import/preview` | `{ template, fileName, csv, mapping? }` → headers, header row, suggested mapping, sample, valid/total rows, date range, row errors |
| POST | `/accounts/{id}/import` | `{ template, fileName, csv, mapping, allowPartial }` → `{ rowsImported, rowsUpdated, rowsSkipped, fromDate, toDate, sourceLabel }`. Row errors without `allowPartial` → `400 import.invalid` with `errors.rows` |
| GET | `/accounts/{id}/imports` | batches |

The parser finds the header row (Google exports start with a title line), skips "Total" rows, reads a currency from
a `Cost (GBP)` style header, and reports rows in a currency different from the account's as row errors. Rows are upserted by
`(account, date, level, entity key)` inside one write transaction, so re-importing a file updates instead of
duplicating.

## 9. Ads: budgets, pacing and alerts

| method | path | notes |
|---|---|---|
| GET | `/pacing` | `month (yyyy-MM-01), clientId?` → budgets with `pacing` |
| POST / PUT / DELETE | `/budgets`, `/budgets/{id}` | `{ clientAccountId, month, platform?, campaignId?, amount, currency, overPacingThreshold, underPacingThreshold, targetCpa?, targetRoas?, notes? }` |
| GET | `/alerts` | `status?, clientId?` |
| POST | `/alerts/{id}/acknowledge`, `/alerts/{id}/resolve` | |

Pacing uses data through yesterday. `expectedToDate = budget × daysElapsed ÷ daysInMonth`,
`projectedMonthEnd = actualToDate + (last-7-day average) × remaining days`, `pacingRatio = actual ÷ expected`.
State: `Over` above the over threshold (default 1.15), `Under` below the under threshold (0.85), else `OnTrack`
(`NotStarted` / `NoBudget` otherwise). `AdsAlertJob` (daily) raises OverPacing, UnderPacing, CpaAboveTarget,
RoasBelowTarget and SpendWithoutConversions alerts, de-duplicated by `{kind}:{scope}:{yyyy-MM}`, and notifies the
account managers.

## 10. Ads: planning, creatives, experiments, naming and UTM

| method | path | notes |
|---|---|---|
| GET / PUT | `/clients/{clientId}/settings` | `{ campaignNamingTemplate, defaultUtmSource, defaultUtmMedium, lowercaseUtm }`; template tokens `{client} {platform} {objective} {audience} {geo} {yyyy} {mm} {yyyymm} {free}` |
| POST | `/clients/{clientId}/naming/check` | `{ name, values? }` → `{ matches, expected, problems }` |
| POST | `/clients/{clientId}/naming/generate` | `{ platform, objective, audience, geo, month, free }` → name |
| POST / GET | `/clients/{clientId}/utm` | build `{ baseUrl, source, medium, campaign, term?, content?, save }` → URL (existing query kept, UTM replaced, optional lower-casing); history |
| GET / POST / PUT | `/media-plans`, `/media-plans/{id}` | plan with lines (platform, objective, month, planned budget/impressions/clicks/conversions) |
| GET | `/media-plans/{id}/actuals` | planned vs actual per line |
| GET / POST / PUT | `/creatives`, `/creatives/{id}` | ad copy + media with copy-limit validation |
| POST | `/creatives/{id}/submit|approve|request-changes|record-client-approval` | same approval states as social posts |
| GET | `/copy-limits` | headline/description/primary-text limits per platform |
| GET / POST / PUT | `/experiments`, `/experiments/{id}` | A/B tests; results use a two-proportion z-test (p-value, lift, 95% CI, `significant` only when both arms reach the minimum sample) |

## 11. Error codes

Social: `social.invalid_transition`, `social.not_editable`, `social.not_deletable`, `social.not_reschedulable`,
`social.not_publishable`, `social.publishing_in_progress`, `social.variant_published`, `social.nothing_to_retry`,
`social.confirm_not_published` (409); `social.validation_failed`, `social.schedule_in_past`, `social.invalid_url`,
`social.invalid_range`, `social.range_too_long`, `social.no_queue_slots`, `social.queue_full` (400);
`social.app_credentials_required` (409), `social.oauth_state_invalid`, `social.oauth_denied`,
`social.oauth_exchange_failed` (400), `social.reply_not_configured` (409), `social.profile_exists`,
`social.duplicate_profile`, `social.competitor_exists`, `social.media_in_use` (409).
Validation issue codes (inside `issues[]`): `social.text_too_long`, `social.title_required`, `social.title_too_long`,
`social.media_required`, `social.video_required`, `social.too_many_media`, `social.too_many_videos`,
`social.mixed_media`, `social.aspect_ratio`, `social.too_many_hashtags`, `social.hashtags_above_recommended`,
`social.too_many_mentions`, `social.first_comment_unsupported`, `social.first_comment_too_long`,
`social.link_not_clickable`, `social.alt_text_too_long`, `social.empty`.

Ads: `ads.naming_convention`, `ads.client_mismatch`, `ads.currency_unsupported`, `ads.experiment_counts`,
`ads.amount_too_small` (a budget amount that rounds to 0 in its currency, e.g. 0.4 JPY) (400);
`import.invalid`, `import.mapping_required`, `import.empty`, `import.csv`, `import.template_unknown` (400).
Common: `concurrency.conflict` (409; a post edit that changes only its variants also advances the post's `concurrencyStamp`),
`<entity>.not_found` (404); an enum value that is not a member of the enum (e.g. `"network": 99`) is a 400 validation error.
