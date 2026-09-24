# API: SEO toolkit, Landing pages & forms, Integrations

All routes are under `/api/v1`. JSON is camelCase, enums are strings, timestamps are UTC ISO-8601, `DateOnly`
values are `yyyy-MM-dd`. Errors are RFC 7807 problem responses `{ status, title, code, traceId, errors? }`; field
errors use camelCase paths (`domain`, `variants[0].blocks[2].props.headline`, `schema.steps[0].fields[1].key`,
`settings.pageId`, `secrets.apiKey`). List endpoints take `?page=&pageSize=&search=` and return
`{ items, total, page, pageSize, totalPages }`. Editable resources carry `concurrencyStamp`; a stale stamp returns
`409 concurrency.conflict`. For landing pages, forms and the template library (`PUT /landing-pages/{id}`,
`PUT /forms/{id}`, `PUT /admin/templates/{key}`, `PUT /admin/form-templates/{key}`) the stamp is required: a missing
stamp is treated as stale (409), as in the website CMS.

**Tenancy.** Every staff route resolves the resource's client through `IClientScope`. A client outside the caller's
scope answers **404** (not 403), so ids cannot be probed. `GET …/client-options` lists the clients the caller may
pick in each area.

**Permissions.** `seo.manage` (SEO), `forms.manage` (landing pages & forms), `integrations.manage` (integrations),
`reports.manage` (read SEO KPIs for reports), `client.portal` (client portal). No new permissions were added.

**Audit.** Every privileged change writes an audit entry (`seo.*`, `landing.*`, `form.*`, `integration.*`).
Integration secrets are never written to the audit log (only the names of the secrets replaced/cleared).

---

## 1. SEO toolkit — `seo.manage`, prefix `/agency/seo`

### Sites

| Method & path | Notes |
|---|---|
| `GET /client-options` | `[{ id, name, slug }]` |
| `GET /sites?clientId=&search=` | paged; each row has `healthScore`, `lastAuditAt`, `keywordCount` |
| `GET /sites/{id}` | |
| `POST /sites` | `{ clientAccountId, name, domain, protocol?: https\|http, sitemapUrl?, targetCountry (ISO-2), targetLanguage (`en`, `en-GB`), competitors[] (≤ 10 domains), maxPages? (1–2000), maxDepth? (1–20) }`. `domain` accepts a host or URL and is normalised to a host (optional `:port`); one site per domain per client (`409 seo.site_exists`). Every fetch still goes through the SSRF policy, so private/internal addresses are never crawled. |
| `PUT /sites/{id}` | same fields + `concurrencyStamp` |
| `DELETE /sites/{id}` · `POST /sites/{id}/restore` | archive / restore (history is kept) |

### Site audits

| Method & path | Notes |
|---|---|
| `POST /sites/{siteId}/audits` | queues a crawl (`202`, `status: Queued`). One queued/running audit per site (`409 seo.audit_in_progress`); archived sites cannot be audited (`409 seo.site_archived`). Processed by `SeoAuditJob` (every minute, claims one audit at a time under a write transaction so two workers never run the same audit). |
| `GET /sites/{siteId}/audits` | history with `healthScore`, error/warning/notice counts, `pagesCrawled` |
| `GET /audits/{id}` | `{ audit, siteName, siteBaseUrl, previousAuditId, issues: [{ ruleKey, title, category, severity, whyItMatters, howToFix, affectedCount, hits: [{ url, detail }] }] }` — URL lists are capped per issue at crawl time; `affectedCount` is the full count |
| `POST /audits/{id}/cancel` | queued audits only (`409 seo.audit_not_queued`) |
| `GET /audits/{id}/pages?search=&status=` | crawled pages: status code, title, words, bytes, response ms, depth, indexable |
| `GET /audits/{id}/diff?against={auditId}` | new / resolved / persisting issues by rule (default: previous completed audit) |
| `GET /audits/{id}/export.csv` | one row per issue × URL (`severity,rule,title,url,detail`) |

### Keywords, rankings and Search Console

| Method & path | Notes |
|---|---|
| `GET /sites/{siteId}/keywords?search=&tag=` | latest position, previous position, change, best position, volume, difficulty, intent, tags |
| `POST /sites/{siteId}/keywords` | `{ keywords: string[] (1–500, each ≤ 200 chars), intent, tags[] (≤ 10), targetUrl? }` — duplicates (normalised) are skipped |
| `PUT /keywords/{id}` · `DELETE /keywords/{id}` | `PUT { intent, searchVolume?, difficulty? (0–100), targetUrl?, tags[], isTracked, concurrencyStamp }` |
| `POST /keywords/{id}/ranks` | manual snapshot `{ date (not in the future), position (1–1000) or null (not ranking), url?, domain? (a competitor's domain for share of voice), serpFeatures[] }` — upsert on (keyword, date, domain) |
| `GET /keywords/{id}/history?from=&to=` | daily positions (own domain + competitors) |
| `POST /sites/{siteId}/ranks/import` | multipart CSV (`file`, ≤ 5 MB): columns `keyword`/`query`, `position`/`avg_position`, optional `date` (else `?date=`/today), `url`, `domain`. Header names are case-insensitive. Unknown keywords are created. Upsert — re-importing the same file changes nothing. Returns `{ rowsRead, created, updated, unchanged, keywordsCreated, errors[] }`. |
| `POST /sites/{siteId}/ranks/refresh` | pulls today's positions from the configured rank provider (DataForSEO credentials from the Integrations vault) for tracked keywords without a snapshot today. Returns `{ outcome, message, count }` — `outcome` reports `NotConfigured` when no provider is connected (manual/CSV entry keeps working). `RankTrackingJob` does the same daily for all sites. |
| `GET /sites/{siteId}/rankings?from=&to=` | `{ trackedKeywords, distribution {top3, top10, top20, top100, notRanking}, averagePosition, averagePositionChange (positive = improved), trend[], winners[], losers[], shareOfVoice[], serpFeatures, providerStatus }` |
| `POST /sites/{siteId}/search-console/import` | multipart Search Console Performance export: `query`, `clicks`, `impressions`, optional `page`, `ctr`, `position`, `date` (or `?date=`). Idempotent (row hash). |
| `POST /sites/{siteId}/search-console/sync` | pulls the last 28 days from the Search Console API using the `google-search-console` connection |
| `GET /sites/{siteId}/search-console?from=&to=` | totals, daily series, top queries and pages |

### On-page analyzer, backlinks, outreach

| Method & path | Notes |
|---|---|
| `POST /analyze` | `{ url? \| html? \| text?, keyword }` (exactly one source) — fetches `url` through the SSRF-safe fetcher (`seo.url_blocked` for private/internal targets, `seo.fetch_failed` otherwise) **or** analyses pasted `html` (≤ 2 MB) / plain `text`. Returns score 0–100 and checks: keyword in title / H1 / first 100 words / URL / meta description, density, heading structure, word count, Flesch reading ease, alt text, internal/external links, schema, OG tags. |
| `GET /sites/{siteId}/backlinks?status=` · `POST /sites/{siteId}/backlinks` | `{ sourceUrl, targetUrl?, anchorText? }` |
| `POST /sites/{siteId}/backlinks/import` | CSV (≤ 5 MB) `source_url`, `target_url`, `anchor`, `first_seen` — deduplicated by link hash; `POST …/backlinks` answers `409 seo.backlink_exists` for a duplicate |
| `DELETE /backlinks/{id}` | |
| `POST /sites/{siteId}/backlinks/check` | re-checks every backlink now (also daily via `BacklinkCheckJob`): Live / Nofollow / Lost / Error, with the anchor actually found |
| `GET /sites/{siteId}/outreach` · `POST …/outreach` · `PUT /outreach/{id}` · `DELETE /outreach/{id}` | prospect pipeline Identified → Contacted → FollowedUp → Replied → Won/Lost |

### Local SEO, reviews, content briefs

| Method & path | Notes |
|---|---|
| `GET /sites/{siteId}/local` | Google Business Profile checklist (18 items), NAP profile, and the 30-directory citation tracker with NAP consistency per citation |
| `PUT /sites/{siteId}/local/profile` | `{ businessName, address?, phone?, website?, completedChecklist: string[] (checklist keys), concurrencyStamp }` — the canonical NAP citations are compared against |
| `PUT /sites/{siteId}/local/citations/{sourceId}` | `{ status, listingUrl?, listedName?, listedAddress?, listedPhone?, notes? }` — NAP compared case/punctuation-insensitively |
| `GET/POST /sites/{siteId}/reviews`, `PUT/DELETE /reviews/{id}` | manual review log (platform, rating 1–5, author, text, reply status) with average rating |
| `GET/POST /sites/{siteId}/briefs`, `GET/PUT/DELETE /briefs/{id}` | content briefs (target keyword, secondary keywords, intent, outline, word target, competitors, notes, status) |
| `GET /briefs/{id}/export.md` | Markdown brief |
| `POST /briefs/{id}/handoff` | `{ markdown, taskCreated: false }` — the Projects module exposes no task-creation contract in this build, so the brief is returned for copy/paste |

### Reporting KPIs

`GET /agency/seo/clients/{clientId}/kpis?from=&to=` — `seo.manage` **or** `reports.manage`. Per site: health score and
change, tracked keywords, top 3 / top 10, average position and change, clicks, impressions, live and lost backlinks,
share of voice; plus landing-page leads (views, submissions, conversion rate, per page and per day). The Projects
module has no `IClientReportSection` extension point in this build, so report generation should read this endpoint.

### Client portal — `client.portal`

`GET /client/seo/overview?from=&to=` (default last 30 days) — for each client organization the user belongs to:
the KPI block above plus the top 10 keywords with position and change. Nothing else from the SEO toolkit is exposed
to clients.

---

## 2. Landing pages & forms — `forms.manage`, prefix `/agency/pages`

### Landing pages

| Method & path | Notes |
|---|---|
| `GET /client-options` · `GET /templates` | templates: `{ key, name, category, description, metaTitle, metaDescription, formTemplateKey, blocks[] }` |
| `POST /images` | multipart image upload (JPEG/PNG/WebP/GIF, metadata stripped) → `{ id, url }` for image blocks |
| `GET /landing-pages?clientId=&status=&search=` | paged |
| `POST /landing-pages` | `{ clientAccountId, name, slug?, templateKey?, formId?, createForm = true }` — with a template that has a form, a new form is created from the matching form template unless `formId` is given |
| `GET /landing-pages/{id}` · `PUT /landing-pages/{id}` | `PUT { name, slug, metaTitle?, metaDescription?, ogImageUrl?, noIndex, experimentEnabled, variants: [{ key: A–D, name, weight 1–100, blocks[] }], concurrencyStamp }` saves the **draft** |
| `POST /landing-pages/{id}/publish` | validates and writes an immutable `LandingPageVersion` snapshot (content hash); the public URL serves only published versions. Publishing a new slug records a 301 redirect from the old `/lp/{client}/{slug}` address (Website → Redirects, [website.md](website.md#redirects-sitemanage)) |
| `POST /landing-pages/{id}/unpublish` · `DELETE /landing-pages/{id}` (archive) · `POST /landing-pages/{id}/restore` | |
| `GET /landing-pages/{id}/versions` · `GET /landing-pages/{id}/versions/{versionId}` · `POST …/versions/{versionId}/restore` | restore copies a snapshot into the draft |
| `POST /landing-pages/{id}/experiment/reset` | starts a new experiment id (fresh assignments and results) |
| `GET /landing-pages/{id}/analytics?from=&to=` | views, unique visitors, submissions, conversion rate, per-variant results with lift and a two-proportion z-test p-value, daily series |

**Blocks** (`{ id, type, props }`, 1–60 per variant, strict JSON — unknown properties are rejected):
`hero`, `text`, `image`, `video`, `features`, `testimonials`, `pricing`, `faq`, `countdown`, `form`, `cta`, `logos`,
`spacer`. Text is plain text: markup, `javascript:` URLs, event-handler attributes and `<script>/<iframe>/<object>`
are rejected. Links may be `https://`, `http://`, site-relative paths, `#anchors`, `mailto:` or `tel:`. Images must be
uploaded files or pass the image URL allow-list. Videos accept YouTube or Vimeo URLs only and are rendered with
`youtube-nocookie.com` / `player.vimeo.com?dnt=1`. Form blocks must reference an active form of the same client.

### Forms

| Method & path | Notes |
|---|---|
| `GET /form-templates` | contact, quote request, newsletter, webinar registration |
| `GET /staff-options` | staff who can be notified (in the client's scope) |
| `GET /forms?clientId=&status=` · `POST /forms` `{ clientAccountId, name, templateKey? }` | |
| `GET /forms/{id}` · `PUT /forms/{id}` · `DELETE /forms/{id}` (archive) | `PUT { name, status, schema, submitLabel, successMessage, redirectUrl? (https), notifyUserIds[], autoresponderEnabled, autoresponderSubject?, autoresponderBody?, allowedOrigins[], consentText?, captcha: None\|HCaptcha\|Turnstile, minFillSeconds 0–60, concurrencyStamp }`. Changing `consentText` creates a new consent version; each submission stores the version and text it was given. |
| `GET /forms/{id}/submissions?from=&to=&landingPageId=` · `GET /forms/{id}/submissions/{sid}` | |
| `GET /forms/{id}/submissions/export.csv` | one column per field (CSV-injection safe) |
| `GET /forms/{id}/submissions/{sid}/files/{fileId}` | attachment download (`Content-Disposition: attachment`, `nosniff`) |
| `GET /forms/{id}/embed` | `{ formUrl, iframeSnippet, allowedOrigins, frameAncestors, guidance }` |

**Schema:** `{ steps: [{ id, title?, description?, fields: [...] }] }` (≤ 10 steps, ≤ 60 fields). Field
`{ key, type, label, required, placeholder?, helpText?, options?, validation?, showIf?, urlParam?, defaultValue?, width? }`,
types `text email phone number select multiselect checkbox radio date textarea file hidden consent`. `validation`:
`minLength maxLength min max pattern patternMessage accept (pdf|image) maxSizeMb (≤ 10) minChoices maxChoices`.
`showIf: { field, operator: equals|notEquals|contains|in|isEmpty|isNotEmpty|greaterThan|lessThan, value?, values? }`
may only reference an earlier field; a field whose controller is hidden is treated as empty, so chains collapse. The
server re-evaluates visibility: hidden fields are never required and their values are dropped.

### Public (anonymous) — `[AllowAnonymous]`, `Public` rate-limit policy

| Method & path | Notes |
|---|---|
| `GET /public/lp/{clientSlug}/{pageSlug}` | the published version with the visitor's variant. Send `X-Visitor-Id` (random id kept in `localStorage`) for sticky A/B assignment; bots are neither assigned nor counted. Returns blocks plus the public definition of each form used. `404` unless published. |
| `GET /public/forms/{formId}` | form definition + signed render token. When framed, send `X-Embed-Origin`; it must be in `allowedOrigins` (or the app origin) → else `403 forms.origin_not_allowed`. |
| `POST /public/forms/{formId}/submissions` | JSON `{ values, hp, token, captchaToken?, landingPageId?, variantKey?, utmSource…utmContent, referrer? }` or multipart (`payload` JSON part + one file part per file field). Checks in order: origin allow-list (Origin/Referer/X-Embed-Origin), honeypot (`hp` must be empty — bots get a fake success), signed min-fill-time token (`forms.token_invalid`, `forms.too_fast`; single use — a replay, even concurrent or from another network, is `409 forms.already_submitted`, while honeypot hits, validation errors and rate-limited attempts do not spend it), per-IP limit (5 per form and 20 overall per 10 minutes, `429 forms.rate_limited`), CAPTCHA when configured, schema validation, file magic-byte checks (PDF/JPEG/PNG/WebP/GIF). Success `{ ok, message, redirectUrl }`. |

After commit a `FormSubmitted` event is published exactly once (claimed via `EventPublishedAt`; `FormEventRetryJob`
retries unpublished submissions every 5 minutes) so CRM lead capture and automations can react. Staff notifications
use `INotificationService` (email), and autoresponders go through an outbox processed by `FormEmailDispatchJob`.
Autoresponder text supports `{{field_key}}` and `{{form}}` placeholders.

---

## 3. Integrations — `integrations.manage`, prefix `/agency/integrations`

| Method & path | Notes |
|---|---|
| `GET /providers` | 22 providers: `{ key, name, category, description, helpText, docsUrl, settings[], secrets[], agencyWide, perClient, supportsVerification, tokensExpire }` |
| `GET /client-options` | |
| `GET /connections?clientId=` or `?scope=agency` | secrets appear **only** as `{ key, label, required, saved }` |
| `GET /connections/{id}` | |
| `POST /connections` | `{ provider, clientAccountId? (null = agency-wide), displayName, settings{}, secrets{}, expiresAt? }` — validated against the provider descriptor (required keys, patterns, lengths, no unknown keys). One active connection per provider and scope (`409 integrations.exists`). |
| `PUT /connections/{id}` | `{ displayName, settings{}, secrets{} (only the ones to replace; empty keeps the saved value), clearSecrets[] (optional secrets only), expiresAt?, concurrencyStamp }` — changes reset status to `Unverified` |
| `POST /connections/{id}/test` | runs the provider verifier (DataForSEO, SendGrid, Mailgun, Stripe, Twilio, Google Search Console); others report "saved — verified on first use". Records `status`, `statusMessage`, `lastVerifiedAt`. |
| `POST /connections/{id}/disconnect` | wipes secrets, keeps the record |
| `DELETE /connections/{id}` | |

Secrets are encrypted with the credential vault (Data Protection), are write-only, and never appear in responses,
logs or audit entries. `IntegrationExpiryJob` (daily, idempotent per connection/expiry date) notifies staff with
`integrations.manage` 14 days before `expiresAt`; responses carry `expiringSoon`.

---

## 4. Frontend wiring (for the lead)

* Agency areas export `nav`, `routes`, `opensWith` from `features/agency/{seo,pages,integrations}/routes.tsx`;
  the client area from `features/client/seo/routes.tsx`. No edits to `portals.ts` were needed.
* Public routes are exported as `publicRoutes` from `features/agency/pages/publicRoutes.tsx`
  (`lp/:client/:slug`, `f/:formId`). Mount them in `app/router.tsx` as children of the root route **outside**
  `PublicLayout`, so client pages and embedded forms render without the platform header/footer:

  ```tsx
  import { publicRoutes as landingRoutes } from '@/features/agency/pages/publicRoutes';
  // routes[0].children:
  ...landingRoutes,
  ```
* `AppLinks`: notification links point at `/agency/pages/forms/{formId}/submissions` (`FormLinks.Submissions`) and
  `/agency/integrations`; mirror them in the shared link helper if one is added.
