# SEO toolkit, landing pages, forms and technical SEO of the website (M4c)

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
  `/lp/{clientSlug}/{pageSlug}` only ever serves the published version; drafts are never visible. The URL slug is part
  of the published snapshot too: renaming a published page in the draft keeps it live at its old address (and "View
  live"/`publicPath` keep pointing there) until the rename is published; meanwhile no other page of the client can
  take either address (409 `landing.slug_taken`). The public page writes the page's title, description and Open Graph
  image (absolute URL) as `og:*`/`twitter:card` tags; the server renders the same tags without JavaScript, and published landing pages without *noindex* are listed in
  the `landing-pages` sitemap (§ 9).
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

## 9. Technical SEO of the public website

The agency website (home, services, pricing, industries, case studies, blog, careers, team, about, contact and the
lead forms, FAQ, creators, legal and other CMS pages, client landing pages `/lp/…` and public campaign pages `/c/…`)
is built for search engines, AI assistants and link unfurlers first. **Code:** `backend/src/OptimizeAll.Api/Modules/
Website/SiteSeo/` (server rendering, sitemaps, robots, llms.txt, SEO settings, overview, IndexNow),
`frontend/seoShell.ts` + `frontend/src/app/seoShellCore.ts` (shell fragments, dev/preview rendering),
`frontend/nginx/default.conf.template` (production routing), `frontend/src/features/public/site/head.ts` (head manager).
**Tests:** `backend/tests/*/Website/TechnicalSeo*Tests.cs`, `frontend/src/**/seoShellCore.test.ts`, `head.test.tsx`,
and the E2E suite `frontend/e2e/j-seo/` (`E2E_SUITE=j-seo scripts/e2e-journeys.sh`, with `E2E_WEB_SERVER=nginx` for the
production configuration).

### 9.1 Rendering: complete HTML without JavaScript

The site is a React SPA, so every public page is also **rendered on the server** — without a Node server and without
changing the React app:

```
browser / crawler ──GET /services/seo──▶ nginx (web)
   nginx: not a static file, not a portal ──▶ API  GET /_document/services/seo      (rewrite, proxy_pass)
   API:   SeoPageResolver → status 200/301/404/410 + <head> (title, description, canonical, robots, OG, Twitter,
          article dates, prev/next, JSON-LD) + <div id="root"><div id="oa-ssr"> header nav, breadcrumbs, the page's
          headings/copy/links/images/videos, footer links </div></div>
          + <!--# include virtual="/__shell/head.html" -->  and  <!--# include virtual="/__shell/body.html" -->
   nginx: ssi on → includes dist/__shell/head.html (the build's <script>/<link> tags, icons, manifest, viewport)
   ◀── one HTML document, real status code
browser: loads the app bundle; React renders into #root (createRoot replaces the server copy) and takes over.
```

* **Shell fragments.** `vite build` runs the `seoShell` plugin, which splits the built `index.html` into
  `dist/__shell/head.html` (everything in `<head>` except `<meta charset>`, `<title>` and the region between
  `<!-- oa:seo-defaults -->` and `<!-- /oa:seo-defaults -->`) and `dist/__shell/body.html` (the body without the empty
  `#root` and the "needs JavaScript" notice). nginx serves `/__shell/` only as an internal location.
* **Same path in development and tests.** `vite` and `vite preview` use the same plugin as a middleware: page requests
  go to the API's `/_document…` and the two directives are filled exactly as nginx does (dev: from the transformed
  `index.html`; preview: from `dist/__shell`). If the API is not running, the plain SPA is served.
  `scripts/serve-web-nginx.sh` runs the real nginx configuration against a local build (used by the E2E suite with
  `E2E_WEB_SERVER=nginx` and by CI).
* **No flash, no duplicates.** The server copy lives inside `#root` and is hidden for script-capable browsers by
  `@media (scripting: enabled) { #oa-ssr { display: none } }` (inline style in the document), so visitors see the React
  page only, while crawlers without JavaScript and visitors with scripts off see the server copy (minimal inline styles).
  Head tags carry `data-oa-head data-oa-ssr`: the head manager updates title, description, canonical, robots, Open Graph
  and Twitter tags **in place**; the server's JSON-LD is kept while the visitor is on the page the server rendered (it
  can be richer than the page payload, e.g. ItemList on listings) and replaced by the page's own after the first
  client-side navigation, together with server-only tags (prev/next, `og:image:*`, `article:*`).
* **Portals and sign-in pages** (`/app`, `/admin`, `/agency`, `/client`, `/review`, `/finance`, `/manage`, `/login`,
  `/register`, `/check-email`, `/verify-email`, `/forgot-password`, `/reset-password`, `/auth`, `/design-system`) are
  served by nginx as the plain app shell with `X-Robots-Tag: noindex, nofollow` (the API renders the same for them in
  dev). `/index.html` itself carries `X-Robots-Tag: noindex`.
* **Failure mode.** If the API is unreachable nginx answers `503` + `Retry-After` with the app shell (visitors still get
  the app; crawlers retry instead of indexing an empty page). A rendering exception in the API answers `503` with the
  shell as well (logged). The Render web service's health check is nginx's `/healthz`, so a starting API never takes the
  web service down.
* **Caching.** Documents are `Cache-Control: no-cache`; nginx hides the API's ETag and does not forward conditional
  headers, because the same page embeds new asset names after a web deploy (a 304 would revive deleted bundles).
  Sitemaps, robots.txt and llms.txt have strong ETags (304 on `If-None-Match`) and `public` max-age (300 s / 3600 s).
* **Rate limit.** Documents and SEO files use the `documents` policy (600/min per IP, `RateLimiting:DocumentsPerMinute`),
  exempt from the global 300/min limiter: crawlers fetch many pages from few addresses.

### 9.2 Status codes, URLs and redirects

| Case | Answer |
|---|---|
| Published page | `200` |
| Unknown path, unpublished/scheduled content, unknown slug, blog page beyond the last | `404`, `noindex, follow`, `X-Robots-Tag`, helpful links (home, services, case studies, pricing, blog, contact, search); the app then shows its own 404 page |
| Job opening that existed but closed | `410 Gone` |
| Trailing slash, duplicate slashes, upper case (not in personal token links), `/index.html` | `301` to the one lower-case form, query string kept |
| Managed redirect (slug changes) | `301`/`308` (or `410`) from `ISeoRedirectLookup` — the hook the Website slug-redirect manager implements (`services.AddScoped<ISeoRedirectLookup, …>()`; the default has no redirects) |
| Other host name | optional `301` to the Site URL's host with `Website:Seo:CanonicalHostRedirect=true` (off by default — enable it once DNS for the Site URL is live; otherwise redirect `example.com ↔ www` at the proxy) |

### 9.3 Metadata rules

* **Titles** ≤ 60 characters including the suffix of the title template (Site settings → SEO, default
  `%s | Optimize All`; a title that already names the site is used as is). **Descriptions** 70–155 characters; longer
  ones are cut at a word boundary with "…" (server and payloads use the same `SeoText.Clamp`).
* **Built-in pages** take their title and description from **Page texts** (`{page}.seo.title` /
  `{page}.seo.description`, e.g. `services.seo.title` "Marketing Services: SEO, Ads, Social & Web"); the home page from
  Site settings → SEO (default title and description); `/faq` from the help-centre portal copy. All are editable, and
  the shipped copy meets the rules (the integration and E2E tests fail otherwise).
* **Content** (services, industries, case studies, blog posts, CMS pages, landing pages) uses its SEO fields (title,
  description, social image, canonical URL, noindex) with defaults: name/title, summary/tagline/excerpt, hero or cover
  image. An editor's *noindex* gives `noindex, nofollow` and removes the URL from sitemaps and llms.txt; a canonical URL
  pointing elsewhere also removes it from the sitemaps.
* Every page has: `<title>`, description, `robots` (`index, follow, max-image-preview:large, max-snippet:-1,
  max-video-preview:-1` or the noindex form), self-referencing absolute **canonical** (from Site settings → SEO → Site
  URL, falling back to `Email:AppBaseUrl`), `og:type/site_name/locale/title/description/url/image(+width/height/alt)`,
  `twitter:card` (`summary_large_image`), `twitter:title/description/image/image:alt`, `twitter:site` (Site settings →
  Twitter handle), `article:published_time/modified_time/section` on articles, `rel=prev/next` on the paginated blog,
  `rel=alternate` RSS and Markdown links. Social image: the page's image → the settings' default → the built-in
  `/og-default.png` (1200×630).
* **Listings:** `/blog?page=N` pages are their own canonical URLs with prev/next; `/blog?category=x` is indexable
  (title "x articles"); tag filters and searches (`?tag=`, `?q=`, `/search`) and case-study filters are
  `noindex, follow` with the unfiltered list as canonical.
* **hreflang:** the site has one language (English), so no hreflang tags are written; `<html lang="en">` and
  `og:locale` are.
* **Admin overview:** Agency → Website → **SEO** lists every public URL (sitemap URLs plus noindex and utility pages)
  with the rendered title, description, lengths, canonical, indexability, JSON-LD types, image/video counts and
  warnings (missing, duplicate, too long/short title or description, missing h1, several h1, no structured data,
  canonical elsewhere, noindex, image without alt). Built-in page snippets are edited in place; content links to its
  editor. `GET /api/v1/agency/website/seo/overview` (`site.manage`).

### 9.4 Structured data catalog

| Page | JSON-LD |
|---|---|
| Home | Organization (`@id #organization`, logo as ImageObject, sameAs from social profiles, address), WebSite (`#website`, SearchAction → `/search?q=`), WebPage, ProfessionalService (LocalBusiness) when an address is set in Site settings → Organization |
| Services list / industries / case studies / careers | BreadcrumbList, CollectionPage, ItemList |
| Service | Service (+ Offer per priced package, provider → Organization), BreadcrumbList, FAQPage |
| Pricing | BreadcrumbList, WebPage, OfferCatalog (services → Offers with UnitPriceSpecification for recurring prices) |
| Blog | BreadcrumbList, Blog, ItemList · post: BlogPosting (author Person, dates, image, publisher), BreadcrumbList |
| Case study | Article (headline, dates, image, publisher), BreadcrumbList |
| Job opening | JobPosting (employmentType, datePosted, validThrough, location or TELECOMMUTE, baseSalary), BreadcrumbList |
| Team / about / how we work | AboutPage, ItemList of Person (team) |
| Contact | ContactPage, ProfessionalService (when an address is set) |
| Creators / FAQ / CMS FAQ blocks | FAQPage |
| Any page with a video | VideoObject (name, description, thumbnailUrl, uploadDate, duration, contentUrl or embedUrl, caption track, transcript) |
| Every page but home | BreadcrumbList |

JSON-LD is data (`type="application/ld+json"`, serialized with HTML-sensitive characters escaped), so it needs no CSP
change. Shapes are validated in `TechnicalSeoTests.AssertValidJsonLd` and the E2E suite.

### 9.5 Video

Website videos are **self-hosted** files in `frontend/public/media/videos/` (served at `/media/videos/`, cached 7 days by
nginx): an MP4 (H.264/AAC) and ideally a WebM, a 16:9 poster image (JPEG/WebP, ≥ 1280×720) and WebVTT captions.

* **CMS pages:** add a **Video** block (Agency → Website → Pages): title, description, MP4/WebM file, poster, captions
  (required, WCAG 1.2.2), captions language, length in seconds, upload date, optional Markdown transcript. Paths under
  `/media/` must end in the right extension; uploads and allowed https hosts also work.
* **Built-in pages (code default):** add an entry to `frontend/src/features/public/site/siteVideos.json` and the
  identical `backend/…/SiteSeo/site-videos.json` (`path`, `title`, `description`, `mp4Url`, `webmUrl`, `posterUrl`,
  `captionsUrl`, `captionsLanguage`, `durationSeconds`, `uploadDate`, `transcript`); a unit test keeps the two identical
  and validates the entries. The web app renders it at the end of that page.
* The player (`SiteVideo.tsx`, and the same markup server-side) is `<video controls preload="none" playsinline>` with a
  poster, WebM + MP4 sources, a default captions `<track>`, a download link as fallback, a reserved 16:9 box (no layout
  shift) and the transcript in `<details>`. Each video adds a VideoObject and an entry in `/sitemaps/videos.xml`.
  Landing-page YouTube/Vimeo blocks get a VideoObject with `embedUrl` (YouTube thumbnails from `i.ytimg.com`).

### 9.6 Sitemaps

`/sitemap.xml` is a **sitemap index** of `/sitemaps/{pages,services,case-studies,blog,careers,landing-pages,images,
videos}.xml` (only non-empty files). Only published, indexable, self-canonical, `200` URLs are listed, on the Site URL:

* `pages`: home, the built-in pages, CMS pages, industries · `services` · `case-studies` · `blog` (posts) · `careers`
  (open jobs) · `landing-pages` (live client landing pages without noindex and public campaigns) · `images` (Google
  image extension: hero, cover, gallery, team photos) · `videos` (Google video extension: thumbnail, title,
  description, content/player URL, duration, publication date).
* `<lastmod>` is the real last change (content `UpdatedAt`; built-in pages: page texts or site settings, plus the newest
  content they list), in W3C datetime format.
* Files above `Website:Seo:SitemapMaxUrls` (default 45 000, protocol limit 50 000) are split into `{group}-2.xml`…
  Responses carry a strong ETag and Last-Modified (304 on If-None-Match).
* `/api/v1/public/sitemap.xml` still answers with one flat urlset of the same URLs (older Search Console submissions).
  Submit `https://<site>/sitemap.xml`.

### 9.7 robots.txt and the AI/search crawler policy

Generated by the API from Agency → Website → **SEO → Crawlers & AI** (audited as `website.seo_settings_updated`, denied
while impersonating). Each crawler group is written as its own robots group (a crawler obeys only the most specific
group naming it, so every group repeats the private-area rules):

| Group | User agents | Default |
|---|---|---|
| Search engines | Googlebot, Bingbot, Applebot, DuckDuckBot, YandexBot, Baiduspider | allowed |
| AI search and assistants | OAI-SearchBot, ChatGPT-User, Claude-SearchBot, Claude-User, PerplexityBot, Perplexity-User | allowed |
| AI model training | GPTBot, ClaudeBot, anthropic-ai, Google-Extended, Applebot-Extended, Meta-ExternalAgent, CCBot | allowed |
| Aggressive scrapers | Bytespider, PetalBot, Amazonbot, cohere-ai, Diffbot, ImagesiftBot, omgili | **blocked** (`Disallow: /`) |
| Everyone else (`*`) | — | allowed |

Allowed groups get: `Allow: /api/v1/public/blog/rss.xml`, `/api/v1/public/sitemap.xml`, `/api/v1/files/` (public
images); `Disallow:` every portal and sign-in page as `/x$`, `/x/` and `/x?` (never a bare prefix, so `/apple-…` pages
stay open), `/search`, `/api/`, personal links (`/p/`, `/i/`, `/email/`, `/join/`, `/f/`), `/newsletter/`, `/blog?q=`;
then `Allow: /` and `Sitemap: https://<site>/sitemap.xml`. robots.txt is a crawl policy, not access control: private
areas are protected by sign-in, and noindex is also sent as `X-Robots-Tag` on portal pages and every `/api/` response
(except uploaded images, which stay indexable).

### 9.8 llms.txt, Markdown pages and well-known files

* **`/llms.txt`** (llmstxt.org): `# Optimize All`, a `>` summary (default description), what the agency and the
  creator program are, contact email, then `## Key pages`, `## Services`, `## Industries`, `## Case studies`, `## Blog`
  (50 newest), `## Careers`, `## Creator program`, `## Optional` and `## Machine-readable` (sitemap, RSS, llms-full).
  Every entry is `- [Title](https://<site>/path.md): description`, generated from published, indexable content.
* **`/llms-full.txt`**: the Markdown of every indexable page (up to 150), each with its URL and update date.
* **Markdown page versions:** `/{path}.md` (`/index.md` for the home page) — YAML front matter (title, description,
  canonical URL, updated) plus the same content as the HTML, `noindex` with a `Link: rel=canonical` header to the HTML.
  Linked from each page with `<link rel="alternate" type="text/markdown">`.
* Both can be switched off in SEO settings (then 404). Also served: `/.well-known/security.txt` (RFC 9116, contact from
  SEO settings or the site's contact email, expires in 180 days), `/humans.txt`, `/site.webmanifest` (icons 192/512 +
  maskable), `/favicon.ico` (16/32/48), `favicon.svg`, `favicon-32.png`, `apple-touch-icon.png`, `og-default.png`.

### 9.9 IndexNow

Off by default. When enabled in SEO settings, a key is generated and served at `/{key}.txt`, and `IndexNowJob`
(every 10 minutes, named lock, idempotent) submits the sitemap URLs changed since the last successful submission to
`Website:Seo:IndexNowEndpoint` (default `https://api.indexnow.org/indexnow`, shared by Bing, Yandex, Seznam, Naver).
It only runs when the Site URL is a public https origin.

### 9.10 Performance (Core Web Vitals)

* Route-level code splitting (`lazyPage()` / lazy public routes); vendor chunks `react` and `query`; hashed assets
  `immutable` for a year; HTML `no-cache`; gzip for HTML, CSS, JS, JSON, XML, SVG; `/media/` cached 7 days.
* Fonts are self-hosted (`@fontsource-variable/inter`, `font-display: swap`) — no third-party preconnect is needed.
* The server-rendered head preloads the page's LCP image (`<link rel="preload" as="image" fetchpriority="high">`),
  server images carry `width`/`height`, the first is `fetchpriority="high"`, the rest `loading="lazy"`; videos are
  `preload="none"` in a reserved 16:9 box.
* Layout stability: the self-hosted Inter (Latin) font is preloaded from the built `index.html` (the `seoShell` plugin
  adds `<link rel="preload" as="font">`), so headlines never re-wrap when the font swaps in; the website's `<main>` is
  at least one screen tall, so the footer never jumps down inside the viewport while a page loads its content.
* Measured by `e2e/j-seo/06-performance.spec.ts` with PerformanceObserver (Lighthouse is not available offline), now vs
  the previous bare-shell serving; results in `test-results/j-seo/web-vitals.json`. CLS must stay < 0.1.

Measured on 2026-09-24 (Chromium, 1280×800, local `vite preview` + API on a heavily loaded shared machine; LCP and FCP
are the same element here — the hero heading — and vary by ±150 ms between runs):

| Page | CLS before | CLS after | LCP before | LCP after | TTFB after |
|---|---|---|---|---|---|
| `/` | 0.179 | 0.000 | 492 ms | 484 ms | 14 ms |
| `/services` | 0.013 | 0.000 | 616 ms | 388 ms | 11 ms |
| `/services/seo` | 0.501 | 0.000 | 428 ms | 320 ms | 30 ms |
| `/pricing` | 0.041 | 0.036 | 468 ms | 476 ms | 20 ms |
| `/blog` | 0.007 | 0.006 | 536 ms | 392 ms | 12 ms |
| `/about` | 0.526 | 0.000 | 368 ms | 460 ms | 12 ms |

"Before" is the site as it was (bare app shell for every URL, no font preload, footer inside the first screen); "after"
is the server-rendered document plus the CLS fixes. Server rendering adds 5–20 ms of TTFB; for crawlers and visitors
without JavaScript the whole page is in the first response.

### 9.11 Configuration

| Setting | Default | Purpose |
|---|---|---|
| Site settings → SEO → Site URL | `Email:AppBaseUrl` | Origin of canonical URLs, sitemaps, robots, llms.txt |
| `Website:Seo:SitemapMaxUrls` | 45000 | URLs per sitemap file before splitting |
| `Website:Seo:CanonicalHostRedirect` | false | 301 page requests on other host names to the Site URL's host |
| `Website:Seo:IndexNowEndpoint` | `https://api.indexnow.org/indexnow` | IndexNow submission URL |
| `RateLimiting:DocumentsPerMinute` | 600 | Per-IP limit for documents and SEO files |
