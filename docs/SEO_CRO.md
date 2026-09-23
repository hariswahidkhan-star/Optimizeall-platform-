# SEO toolkit, landing pages and forms (M4c)

How the SEO toolkit, the landing-page/form builder and the integrations vault work, the numbers behind them, and
what to configure in production. Endpoint reference: [`docs/api/seo-pages-integrations.md`](api/seo-pages-integrations.md).

## 1. Site crawler and audits

**Code:** `Modules/Seo/Http/SafeHttpFetcher.cs`, `Crawling/*`, `Audit/*`, `Domain/Seo/*`.

1. `POST …/sites/{id}/audits` queues an audit; `SeoAuditJob` (every minute) claims one queued audit inside a
   dialect write transaction (`IDatabaseDialect.BeginWriteTransactionAsync`), marks it `Running`, crawls, stores
   pages and issues, and completes it. A crashed worker's audit is re-queued after 20 minutes without progress; re-running an audit
   replaces its rows, so the job is idempotent.
2. The crawler reads `robots.txt` (RFC 9309: longest match wins, `Allow` beats `Disallow` on ties, `*` and `$`
   wildcards, `Crawl-delay`), discovers sitemaps (site setting → `Sitemap:` lines → `/sitemap.xml`, sitemap indexes,
   ≤ 5 000 URLs), then crawls breadth-first from the home page and sitemap URLs, same host only, up to `maxPages`
   (default 500, max 2 000) and `maxDepth`. Pages disallowed by robots.txt are never fetched (they are reported).
3. Politeness: ≤ 2 concurrent requests per host, 500 ms between batches, or the robots `Crawl-delay` (capped at 10 s).
   User agent `OptimizeAllBot/1.0`. Up to 100 external links are checked with `HEAD` (falling back to `GET`).
4. HTML is parsed with AngleSharp (title, meta, canonical, robots, headings, links, images, hreflang, JSON-LD, Open
   Graph, viewport, word count, mixed content). Near-duplicate content uses 64-bit SimHash over word shingles.

### SSRF protection (mandatory for every outbound fetch)

Every crawler, analyzer and backlink request goes through `SafeHttpFetcher`:

* Only absolute `http`/`https` URLs without credentials.
* Host names are resolved first and **every** address must be public; the check is repeated in the
  `SocketsHttpHandler.ConnectCallback` against the address actually connected to, so DNS rebinding cannot slip a
  private address in between.
* Blocked ranges: `0.0.0.0/8`, `10/8`, `100.64/10`, `127/8` (unless `Seo:Crawler:AllowLoopback` — tests only),
  `169.254/16` (cloud metadata), `172.16/12`, `192.0.0/24`, `192.0.2/24`, `192.88.99/24`, `192.168/16`, `198.18/15`,
  `198.51.100/24`, `203.0.113/24`, multicast and reserved; IPv6 `::/96`, `::1`, `64:ff9b::/96`, `64:ff9b:1::/48`,
  `100::/64`, `2001::/32`, `2001:db8::/32`, `2002::/16`, `fc00::/7`, `fe80::/10`, `fec0::/10`, `ff00::/8`, and
  IPv4-mapped IPv6 addresses are checked as IPv4.
* Redirects are followed manually (≤ 5), re-validating each hop. Bodies are capped at 2 MB, requests time out after
  15 s, and only HTML/XML/text bodies are read.

Configuration section `Seo:Crawler` (`UserAgent`, `RobotsToken`, `RequestTimeoutSeconds`, `MaxBodyBytes`,
`MaxRedirects`, `PerHostConcurrency`, `DelayMilliseconds`, `MaxCrawlDelaySeconds`, `DefaultMaxPages`,
`AbsoluteMaxPages`, `MaxExternalLinkChecks`, `MaxSitemapUrls`). Never set `AllowLoopback` outside tests.

### Rules and thresholds

38 rules (`Domain/Seo/SeoAuditRules.cs`, seeded into `seo_audit_rules` with "why it matters / how to fix" copy):

| Area | Rules (severity) |
|---|---|
| Crawlability | 4xx (E), 5xx (E), redirect chain > 1 hop (W), redirect loop (E), robots.txt missing (W) / invalid (W), blocked by robots (N), sitemap missing (W) / invalid (W) |
| Links | broken internal (E), broken external (W), orphan pages — in sitemap, not linked (W) |
| On-page | title missing (E), duplicate (W), > 60 chars (W), < 30 chars (N); meta description missing (W), duplicate (W), > 160 (N), < 70 (N); H1 missing (W), multiple H1 (N) |
| Indexability | canonical missing (N), canonical → non-200 (E), canonical cross-domain (W), noindex page in sitemap (E) |
| Content | thin content < 200 words (W), near-duplicate content — SimHash Hamming distance ≤ 3 (W) |
| Performance | HTML > 1 MB (W), server response > 1 500 ms (W) |
| Other | images without alt (W), hreflang errors (W), mixed content (E), not HTTPS (E), no structured data (N), invalid JSON-LD (E), Open Graph missing (N), viewport missing (W) |

### Health score

```
score = 100 × (1 − 0.7 × E − 0.3 × W) − 5 × (site-level error rules) − 2 × (site-level warning rules)
```

`E` / `W` = share of crawled pages with at least one page-level error / warning (each capped at 1). Site-level rules
are the robots.txt and sitemap rules. Notices never lower the score. The result is clamped to 0–100 and rounded.
Tones in the UI: ≥ 80 good, 50–79 needs work, < 50 poor. Audits can be diffed (new / resolved / persisting URLs per
rule).

## 2. Rankings, Search Console, backlinks, local

* **Rank snapshots** are unique per (keyword, date, domain) — CSV imports, manual entry and provider refreshes are
  upserts, so re-running the daily `RankTrackingJob` or re-importing a file never duplicates data.
* **Rank provider:** `IRankTrackingProvider` with a DataForSEO implementation (`Seo:DataForSeo:BaseUrl`, credentials
  from the `dataforseo` integration). Without credentials, refresh reports `NotConfigured` and manual/CSV entry keeps
  working.
* **Share of voice** = Σ expected CTR(position) × search volume (1 when unknown) per domain, over the site and its
  competitors, normalised to 100 %. The CTR curve is a fixed organic CTR model (28.4 % for #1 … 0.6 % for #20, 0
  beyond); it is an estimate and labelled as such.
* **Position change** = previous − current (positive = moved up); "not ranking" counts as position 101.
* **Search Console:** CSV import of the Performance export (idempotent row hash) or API sync with the
  `google-search-console` connection (`Seo:SearchConsole:BaseUrl`).
* **Backlinks:** each link is fetched through the SSRF-safe fetcher and classified Live / Nofollow / Lost / Error,
  recording the anchor found; `BacklinkCheckJob` re-checks daily.
* **Local SEO:** 18-item Google Business Profile checklist, 30 citation directories, NAP consistency (case,
  punctuation and phone formatting insensitive), review log.
* **On-page analyzer:** keyword in title/H1/first 100 words/URL/meta description, density (0.5–2.5 % ideal), headings,
  word count, Flesch reading ease (`206.835 − 1.015 × words/sentence − 84.6 × syllables/word`), alt text,
  internal/external links, schema, Open Graph; 0–100 score from weighted checks.

## 3. Landing pages and A/B testing

* Pages are edited as a **draft** (up to four variants A–D, each a list of typed blocks). **Publish** validates every
  block and writes an immutable `LandingPageVersion` (snapshot + SHA-256 content hash). The public URL
  `/lp/{clientSlug}/{pageSlug}` only ever serves the published version; drafts are never visible.
* **Content safety:** blocks are strict typed JSON; text is rendered as text (React escaping, no
  `dangerouslySetInnerHTML`); HTML tags, `javascript:`/`data:` URLs and event handlers are rejected server-side;
  images must be uploads or allow-listed hosts; videos are YouTube/Vimeo IDs rendered via `youtube-nocookie.com` and
  `player.vimeo.com?dnt=1` inside a sandboxed iframe.
* **Assignment:** visitors send a random `X-Visitor-Id` (localStorage). The server hashes it (`IPrivacyHasher`) and
  assigns a variant by weight with a deterministic hash (`VariantAssigner`), storing the assignment so it stays
  sticky even if weights change. Suspected bots (`TrackingUrl.IsSuspectedBot`) always see A and are not counted.
* **Results:** conversion = submissions ÷ unique visitors per variant. Each variant is compared with A using a
  two-sided two-proportion z-test (α = 0.05) and is only called significant with ≥ 100 visitors per variant.
  "Start a fresh experiment" issues a new experiment id so old assignments and results don't leak into a new test.

## 4. Forms: spam protection, consent, delivery

Submission pipeline (`FormSubmissionService`):

1. **Origin allow-list** — `Origin`/`Referer`/`X-Embed-Origin` must be the app origin or one of the form's
   `allowedOrigins` (https, or http on localhost).
2. **Honeypot** — the hidden `company_website` input (`hp`); bots that fill it get a normal-looking success and
   nothing is stored.
3. **Minimum fill time** — the form definition carries a Data-Protection-signed render token; submissions faster
   than `minFillSeconds` (default 3) or with an invalid/expired (24 h) token are rejected.
4. **Rate limit** — per hashed IP: 5 per form and 20 overall per 10 minutes (plus the `Public` rate-limit policy).
5. **CAPTCHA (optional)** — hCaptcha or Cloudflare Turnstile, keys from the `hcaptcha` / `turnstile` integration of
   the client; verification fails closed.
6. **Validation** — the schema is re-evaluated server-side, including conditional visibility (hidden fields are
   dropped, never required). Regex patterns run with a 100 ms timeout. Files: ≤ 5 per submission, ≤ 10 MB, type
   detected from magic bytes (PDF, JPEG, PNG, WebP, GIF), stored outside the web root under
   `Storage:RootPath/form-uploads` with random names; downloads are staff-only with `Content-Disposition: attachment`.
7. **Consent** — each submission stores the consent version and exact text shown.

After the transaction commits, `FormSubmitted` is published once (claimed via `EventPublishedAt`, retried by
`FormEventRetryJob`), staff are notified by email, and autoresponders are sent from an outbox by
`FormEmailDispatchJob`.

## 5. Content Security Policy guidance (production)

The SPA's CSP must allow what the public pages need — and nothing more:

| Need | Directive |
|---|---|
| Embedding `/f/{formId}` on client sites | serve `/f/*` with `Content-Security-Policy: frame-ancestors 'self' <allowed origins>` (the embed endpoint returns the exact value per form) and **no** `X-Frame-Options: DENY` on that path. Keep `frame-ancestors 'self'` everywhere else. |
| Video blocks | `frame-src https://www.youtube-nocookie.com https://player.vimeo.com` |
| hCaptcha | `script-src https://js.hcaptcha.com https://*.hcaptcha.com; frame-src https://*.hcaptcha.com; connect-src https://*.hcaptcha.com` |
| Turnstile | `script-src https://challenges.cloudflare.com; frame-src https://challenges.cloudflare.com` |
| Images in blocks | `img-src 'self'` plus any host on the image URL allow-list |

The embedded form posts `{ type: 'oa-form:height', formId, height }` to the parent origin (never `*`) so the host page
can resize the iframe:

```html
<script>
  window.addEventListener('message', (e) => {
    if (e.origin !== 'https://app.optimizeall.app' || e.data?.type !== 'oa-form:height') return;
    document.querySelector(`iframe[data-oa-form="${e.data.formId}"]`).style.height = e.data.height + 'px';
  });
</script>
```

## 6. Integrations vault

Credentials live in `integration_connections`, encrypted by the credential vault. The API is write-only for secrets
(responses expose only `saved: true/false`), secrets are never logged or audited, and disconnecting wipes them.
Verifiers exist for DataForSEO, SendGrid, Mailgun, Stripe, Twilio and Google Search Console (named `HttpClient`
"integrations", 15 s timeout); other providers are verified by the adapters that use them. `IntegrationExpiryJob`
warns managers 14 days before a token's `expiresAt`, once per connection and expiry date.

## 7. Demo data

`AgencyToolkitDemoSeeder` (Demo profile, order 300, marker `demo.seo_pages.seeded`) seeds, for Nimbus Fitness (US),
Wanderly Travel (GB), Aurora Skincare (AE) and Karachi Eats (PK): SEO sites with a completed audit, tracked keywords
with 30 days of rank history, Search Console rows, backlinks and outreach, local profiles and citations, content
briefs, published landing pages with forms and submissions, and an A/B test. Demo staff:
`seo@demo.optimizeall.app` (SEO specialist) and `designer@demo.optimizeall.app` (designer), password `Demo#2026!pass`.
The baseline seeder (order 300/310) loads audit rules, citation directories, 6 landing-page templates and 4 form
templates in every environment.

## 8. Known limitations

* Registrable-domain detection uses a compact public-suffix list (common multi-part suffixes such as `co.uk`,
  `com.au`, `com.pk`), not the full PSL.
* Brief hand-off returns Markdown (`taskCreated: false`); the Projects module has no task-creation contract yet.
* SEO KPIs are served by `GET /agency/seo/clients/{id}/kpis` because there is no `IClientReportSection` extension.
* The crawler does not execute JavaScript; client-rendered sites are audited on their server HTML.
* No EF migrations were added (per instructions); the schema comes from the model (`EnsureCreated` in tests).
