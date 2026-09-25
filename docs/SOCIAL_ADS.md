# Social media management and paid advertising

This guide covers what the social and ads modules do, how to connect each network and ad platform, and what each
adapter really does. The endpoint reference is in [`docs/api/social-ads.md`](api/social-ads.md).

The rule behind every adapter: **the app never claims a post was published or a metric was synced unless a
provider answered.** Without credentials an adapter reports *not configured*, and the UI offers the manual workflow:
"Mark as published" with the live URL for posts, and CSV import for metrics. Every figure is labelled **Measured**
(platform API or a platform export) or **Manual** (typed in).

## 1. Where things are

| area | staff UI | code |
|---|---|---|
| Calendar (month/week/list, drag or Alt+arrow to move) | `/agency/social` | `features/agency/social/CalendarPage.tsx` |
| Composer (variants per network, live counters, previews, server validation) | `/agency/social/compose`, `/agency/social/posts/:id` | `ComposerPage.tsx`, `PostPreview.tsx` |
| Approvals, publishing queue | `/agency/social/approvals`, `/agency/social/publishing` | `WorkflowPages.tsx` |
| Profiles and connections, queue slots | `/agency/social/profiles` | `ProfilesPage.tsx` |
| Media library, hashtag sets, snippets | `/agency/social/library` | `LibraryPage.tsx` |
| Listening, inbox, competitors | `/agency/social/listening`, `/inbox`, `/competitors` | `EngagementPages.tsx` |
| Analytics and metrics CSV import | `/agency/social/analytics` | `AnalyticsPage.tsx` |
| Ads overview, accounts, campaigns | `/agency/ads`, `/agency/ads/accounts/:id` | `features/agency/ads/ReportingPages.tsx` |
| Budget pacing, alerts | `/agency/ads/pacing`, `/agency/ads/alerts` | `PacingPages.tsx` |
| CSV import wizard | `/agency/ads/import` | `ImportWizardPage.tsx` |
| Media plans, creatives, experiments, UTM and naming | `/agency/ads/media-plans`, `/creatives`, `/experiments`, `/utm` | `PlanningPages.tsx` |
| Client portal: calendar, approvals, performance | `/client/social`, `/client/social/approvals`, `/client/social/performance` | `features/client/social/ClientSocialPages.tsx` |

Backend code lives in `backend/src/OptimizeAll.Api/Modules/SocialMedia` and `Modules/Ads`. The pure rules (validation,
workflow, queue slots, sentiment, OAuth state, KPIs, pacing, naming, UTM, CSV parsing) are in
`OptimizeAll.Domain/SocialMedia` and `OptimizeAll.Domain/Ads`, so they can be unit tested without a database.

Roles: `SocialMediaManager` has `social.manage` and `social.publish`, and `AdsSpecialist` has `ads.manage`. Client
users with the Approver or Owner duty approve posts in the portal. When a client's settings enable
"Require client approval", no post reaches Scheduled without that client's approval.

## 2. Credentials: where they go

All secrets live in the encrypted integration store (`IntegrationConnection`, protected with ASP.NET Data
Protection) and are managed with `integrations.manage`. There are two kinds:

1. **Developer apps** are agency-wide rows (no client). Each network needs one app, which staff then use to connect
   many client profiles.
2. **Tokens** are stored per brand profile under the provider key `social-{network}`, for example `social-facebook`.
   They are written by the OAuth callback or by pasting a token (`POST /agency/social/profiles/{id}/token`,
   `integrations.manage`).

| provider key | settings (plain) | secrets (encrypted) | used for |
|---|---|---|---|
| `meta` | `appId` | `appSecret` | Facebook Pages + Instagram OAuth and publishing |
| `x` | `clientId` | `clientSecret` | X OAuth 2.0 (PKCE) and publishing |
| `linkedin` | `clientId` | `clientSecret` | LinkedIn OAuth (authorize URL only, see §4) |
| `tiktok` | `clientKey` | `clientSecret` | TikTok OAuth (authorize URL only) |
| `google` | `clientId` | `clientSecret` | YouTube / Google Business Profile OAuth (authorize URL only) |
| `pinterest` | `clientId` | `clientSecret` | Pinterest OAuth (authorize URL only) |
| `google-ads` | `clientId`, `loginCustomerId` (optional, MCC) | `developerToken`, `clientSecret`, `refreshToken` | Google Ads reporting (agency-wide, or per client to override) |
| `meta-ads` | none | `accessToken` (system-user token with `ads_read`) | Meta Marketing API insights |

Configuration (`appsettings` / environment):

```jsonc
"SocialMedia": {
  "PublicApiBaseUrl": "https://api.example.com",      // Meta fetches media from here: must be public https
  "OAuthRedirectUri": null,                            // default {public URL}/agency/social/connect/callback
  "OAuthStateSecret": null,                            // default: derived from Jwt:SigningKey (HKDF)
  "GraphApiBaseUrl": "https://graph.facebook.com/v20.0",
  "XApiBaseUrl": "https://api.x.com",
  "InstagramPollSeconds": 5, "InstagramMaxPolls": 24, "MaxPublishAttempts": 4
},
"Ads": {
  "GoogleAdsApiVersion": "v21", "BackfillDays": 90, "RefreshDays": 3,
  "MetaGraphApiBaseUrl": "https://graph.facebook.com/v20.0",
  "MetaConversionActionTypes": ["omni_purchase", "purchase", "offsite_conversion.fb_pixel_purchase", "lead"]
}
```

Every provider's OAuth redirect URI must be registered exactly as the web page
`https://<app>/agency/social/connect/callback`. That page posts `code` and `state` to the authenticated API. The
state is HMAC-signed, expires after 15 minutes, and is bound to the user and profile that started the flow.

## 3. Social networks

### Meta Graph API: Facebook Pages and Instagram (implemented)

1. Create a Meta app (type *Business*) at developers.facebook.com. Add *Facebook Login for Business* and the
   *Instagram Graph API*. Save the App ID and App Secret as provider `meta`.
2. Register the redirect URI above. Request these permissions (they need App Review for clients other than your own):
   `pages_show_list, pages_read_engagement, pages_manage_posts, pages_manage_engagement, read_insights,
   instagram_basic, instagram_content_publish, instagram_manage_comments, instagram_manage_insights,
   business_management`.
3. Instagram must be a Professional (Business or Creator) account linked to a Facebook Page.
4. In *Profiles*, add the profile, set its **external id** (Page id, or the Instagram business account id), and press
   **Connect**. The callback exchanges the code for a long-lived user token, finds the page and stores its page
   token.

What the adapters do:

- **Facebook:**
  - text or link posts: `POST /{page}/feed`
  - one photo: `POST /{page}/photos`
  - several photos: unpublished photos, then `attached_media`
  - video by URL: `POST /{page}/videos` with `file_url`
  - the first comment: `POST /{post}/comments`
  - the permalink is read back.
- **Instagram:**
  - creates a media container (image, video/REELS or a carousel of children)
  - polls `status_code` until `FINISHED` (5 s × 24 by default)
  - then calls `POST /{ig}/media_publish` and reads the permalink
  - adds the first comment.
  - Meta fetches media itself, so every item needs a public https URL: either media added by URL, or an upload
    marked public and served by `GET /api/v1/files/{id}` under `SocialMedia:PublicApiBaseUrl`. Private uploads have
    no public URL, and the variant fails with a clear message instead of being sent without its media.
- **Errors:**
  - Graph error code 190 means an expired or revoked token. The profile and credential are flagged `Error` and
    there is no retry; reconnect the profile.
  - Codes 4/17/32/613 are rate limits and get retried with backoff.
- **Insights:** `SocialMetricsSyncJob` (daily) reads page and post insights. The results are labelled Measured.

### X API v2 (implemented, text only)

1. Create a project and app in the X developer portal with OAuth 2.0 enabled (type *Web App*). Scopes:
   `tweet.read tweet.write users.read offline.access`. Save the Client ID and Secret as provider `x`.
2. Connect a profile. The flow uses PKCE; the refresh token is stored and refreshed automatically before it expires.

What the adapter does:

- `POST /2/tweets` with the text; X counts every URL as 23 characters, and the composer does the same.
- The first comment is posted as a reply.
- A 429 response is a rate limit (retried); 401/403 are authorization errors.
- **Media upload is not implemented.** A variant with media fails with `NotSupported` and a clear message. Post it
  on X yourself, then use "Mark as published".

### LinkedIn Community Management API (not configured)

- Needs a LinkedIn app with the *Community Management API* product (approval required).
- Scopes: `w_organization_social r_organization_social rw_organization_admin`.
- Save it as provider `linkedin`. **Connect** then builds the authorize URL.
- The code exchange and publisher (`POST /rest/posts` with an organization URN) are not implemented yet. The
  publisher reports *not configured*, and posts use the manual workflow.

### TikTok Content Posting API (not configured)

- Needs a TikTok for Developers app with *Login Kit* and *Content Posting API* (audited for public posting).
- Scopes: `user.info.basic,video.upload,video.publish`, with PKCE.
- Save it as provider `tiktok` (`clientKey`/`clientSecret`).
- The publisher (`/v2/post/publish/video/init/` with `PULL_FROM_URL`) is not implemented, so posts use the manual
  workflow.

### YouTube Data API and Google Business Profile (not configured)

- Create a Google Cloud OAuth client (web) and save it as provider `google`.
- YouTube: enable the *YouTube Data API v3* (scope `youtube.upload`).
- Google Business Profile: request access to the *Business Profile APIs* (scope `business.manage`).
- Uploads (`videos.insert` resumable upload) and `localPosts.create` are not implemented, so they use the manual
  workflow.
- The composer still validates YouTube's required title (100 characters) and video, and Business Profile's
  1500-character limit and no-video rule.

### Pinterest (not configured)

- Needs a Pinterest app, saved as provider `pinterest`.
- Scopes: `boards:read,pins:read,pins:write,user_accounts:read`.
- The pin publisher is not implemented yet.

### Listening and inbox

- The listening and inbox provider interfaces (`ISocialListeningProvider`, `ISocialInboxProvider`) ship with
  *not configured* implementations. **Sync** says so and imports nothing.
- Mentions and messages can be logged by hand (source Manual).
- Replies are recorded as sent manually. Asking to send a reply through the API without a provider returns
  `409 social.reply_not_configured`.
- Sentiment comes from a transparent lexicon scorer (negation, intensifiers, emoji). It shows the matched words and
  can be overridden.

## 4. Ad platforms

### Google Ads API (implemented: reporting)

1. In a Google Ads manager (MCC) account, apply for a **developer token** (API Center). *Basic* access is enough for
   reporting.
2. Create a Google Cloud OAuth client and enable the Google Ads API. Generate a **refresh token** for a user with
   access to the client accounts (scope `https://www.googleapis.com/auth/adwords`, for example with the OAuth
   Playground and your own client).
3. Save provider `google-ads`:
   - settings: `clientId`, and `loginCustomerId` (the MCC id, digits only) if you access through a manager
   - secrets: `developerToken`, `clientSecret`, `refreshToken`
   - Agency-wide is typical. A client-specific row overrides it for that client.
4. Add the ad account with its **customer id** (`123-456-7890`) and currency.

The adapter:

- refreshes the access token
- calls `POST /v21/customers/{id}/googleAds:searchStream` with GAQL for `segments.date, campaign.id, campaign.name,
  customer.currency_code, metrics.cost_micros, impressions, clicks, conversions, conversions_value, video_views`
- converts micros to money.

Sync:

- `AdsSyncJob` runs every 6 hours: the first sync backfills 90 days, later syncs refresh the last 3.
- Rows are upserted, so syncing twice never doubles.
- A currency different from the account's aborts the sync with a message rather than mixing currencies.
- Authorization errors flag the credential as `Error`.

### Meta Marketing API (implemented: reporting)

1. In Business Manager, create a **system user** with access to the ad accounts. Generate a token with `ads_read`
   (and `read_insights`).
2. Save provider `meta-ads` with the secret `accessToken`.
3. Add the ad account with its id (`act_…` or digits).

The adapter:

- calls `GET /act_{id}/insights?level=campaign&time_increment=1` with spend, impressions, clicks, reach, actions and
  action_values, following paging
- counts conversions from the first matching action type in `Ads:MetaConversionActionTypes`.

### TikTok Ads, LinkedIn Ads, Microsoft Ads, Snapchat (not configured)

These report *not configured* on sync. Use the CSV import with the **generic** template: columns `date, campaign,
ad group, ad, spend, impressions, clicks, conversions, conversion value, reach, video views, currency`, with the aliases
listed by `GET /agency/ads/import/templates`. To add an API adapter later, implement `IAdsReportingProvider` for the
platform and register it; the registry uses the last registration per platform.

- TikTok: Marketing API `report/integrated/get`
- LinkedIn: Marketing API `adAnalytics` with `pivot=CAMPAIGN`, `timeGranularity=DAILY`

### CSV import (all platforms)

- Export a *daily* report: Google Ads "Campaigns" report with the *Day* segment, or Meta Ads Manager with
  *Breakdown by Day*.
- In the wizard: pick the account and template, preview, adjust the column mapping, then import.
- The parser skips report title lines and "Total" rows, and reads a currency from headers like `Cost (GBP)`.
- Rows with errors block the import unless you tick "Import the valid rows and skip the others".
- Re-importing the same days **updates** rows (`rowsUpdated`) instead of duplicating them.
- Imported rows are labelled Measured (source `CsvImport`), because they are the platform's own figures.

## 5. Behaviour worth knowing

- **Exactly-once publishing:** posts and variants are claimed with conditional updates, and only the claimant calls the
  provider. A claim interrupted for more than 15 minutes becomes **Unknown**. It is never re-sent automatically; check
  the network, then retry with "I confirm it was not published" or mark it as published.
- **Retries:** transient errors retry after 2, 10 and 30 minutes (4 attempts in total). Every attempt is logged with
  its provider response in the post's publishing history.
- **Queue:** per-profile weekly slots in the client's time zone. **Add to queue** takes the next slot not already
  used by that profile.
- **Evergreen:** a published evergreen post is re-queued every N days, up to M times. Each copy has a unique
  `(source, number)` key, so the job is idempotent.
- **Awareness days and best times:** awareness days are seeded with their sources. Best times come from your own
  published posts' engagement once there is enough data, and otherwise from the network preset (source shown).
- **KPIs:** a ratio whose denominator is zero shows "—", never 0. Multi-currency totals convert at the month-end
  rate and list any missing pair instead of guessing.
- **Pacing:**
  - Uses data through yesterday.
  - Expected to date = budget × elapsed days ÷ days in month.
  - Projection = actual + 7-day run-rate × remaining days.
  - Alerts are de-duplicated per kind, scope and month.
- **Naming convention:** set a template per client (for example `{client}_{platform}_{objective}_{geo}_{yyyymm}`).
  Creating a campaign with a non-matching name is refused with an example of the expected name.
- **UTM builder:** keeps the URL's existing query, replaces `utm_*`, and can force lower case. The social composer can
  append the campaign's UTM to links automatically.

## 6. Demo data

The demo seeder (`SocialAdsDemoSeeder`, profile `Demo`, order 300, idempotent) ensures four clients:

| slug | name | profile | country | currency |
|---|---|---|---|---|
| `nimbus-fitness` | Nimbus Fitness | SaaS fitness app | US | USD |
| `wanderly-travel` | Wanderly Travel | travel | GB | GBP |
| `aurora-skincare` | Aurora Skincare | e-commerce beauty | AE | AED |
| `karachi-eats` | Karachi Eats | restaurant group | PK | PKR |

It also creates two staff users, both with the password `Demo#2026!pass`:

- `social@demo.optimizeall.app` (SocialMediaManager)
- `ads@demo.optimizeall.app` (AdsSpecialist)

Seeded content:

- posts in every state, including published posts with Manual and imported metrics
- listening, inbox and competitor samples
- ad accounts with 90 days of imported daily metrics
- budgets that produce pacing alerts, a media plan, creatives, an experiment and UTM links

No demo post claims to be published by an API. Demo published posts are marked as published manually.

The baseline seeder (always, order 60) stores the network presets (limits, specs and recommended times, each with a
source) and the awareness-day calendar.

## 7. Known limitations

- X media upload is not implemented; X variants with media fail with `NotSupported`.
- LinkedIn, TikTok, YouTube, Pinterest and Google Business Profile publishing are not implemented. These networks
  use the manual workflow. OAuth code exchange exists for Meta and X only.
- Video is referenced by URL, not uploaded to the library.
- Social reach is summed over days, so it is an upper bound on unique reach.
- Ads reporting adapters exist for Google Ads and Meta. The other platforms use CSV import.
- The listening and inbox providers are not configured by default.
