# SEO and crawlability audit — September 2026

Scope: every public URL of the website and academy as crawlers see them (the API's server-rendered documents, the
same HTML nginx serves), plus robots.txt, sitemaps, llms.txt, Markdown versions, social images and the hydrated React
pages. How the system works is described in [SEO_CRO.md § 9](SEO_CRO.md#9-technical-seo-of-the-public-website); this
file records what was measured, what was wrong and what was changed.

## Method

* **Stack:** the API from this branch on SQLite with the Baseline + Demo seed (52 courses, 710 lessons, 34 services,
  23 blog posts, 6 case studies, 3 jobs, 2 partners, 4 client landing pages, 2 public campaigns), rate limiting off.
* **Crawler** (Python, not committed): reads `/sitemap.xml` and every child sitemap, then fetches every sitemap URL
  and every internal link found in the server-rendered HTML (breadth-first from `/`) through `/_document{path}`,
  without following redirects. Per URL it records status, redirect chains, title, description, robots, canonical,
  hreflang, alternates, Open Graph and Twitter tags, JSON-LD (parsed; required properties checked on top-level nodes),
  headings and their order, links, images and `alt`, SSR text size, response size and time, ETag, Last-Modified and
  Cache-Control; then it fetches every distinct `og:image` served by the API and checks the internal link graph (depth
  from the home page, orphans, broken targets), sitemap `lastmod` against `Last-Modified`, and titles/descriptions for
  duplicates and length.
* **Rendering parity** (Playwright, Chromium, 390×844 mobile): 17 key pages loaded with JavaScript (dev server and the
  production build via `vite preview`), comparing the hydrated DOM with the server HTML: title, canonical, robots,
  description and `og:image` after hydration, h1 count, words and links present in the server HTML but not on the
  rendered page, images without `alt`, horizontal scroll, LCP element and CLS (PerformanceObserver).
* Also checked by hand: URL normalization (case, trailing slash, `//`, `/index.html`), 404/410, pagination, query
  variants, robots.txt groups against the domain's robots parser, response headers.

## Results

| Measure | Before | After |
|---|---|---|
| Public URLs crawled (sitemap + links) | 910 | 911 |
| Sitemap URLs | 868 | 874 (+6 blog topic archives) |
| Non-200 responses, broken internal links, redirect chains | 0 / 0 / 0 | 0 / 0 / 0 |
| Pages missing title / description / canonical | 0 / 0 / 0 | 0 / 0 / 0 |
| Invalid JSON-LD blocks | 0 | 0 |
| Pages whose social image is an SVG (not shown by Facebook, LinkedIn, X) | **762** (every course and lesson) | 0 |
| Indexable pages sharing the generic `og-default.png` | 122 | 0 (4 client landing pages keep their own branding) |
| Indexable pages with `og:image` width/height | 123 | 886 (the rest are uploaded cover images of unknown size) |
| Indexable pages with hreflang | 0 | 910 |
| Indexable pages with `Last-Modified` | 193 | 910 |
| Sitemap `lastmod` ≠ page `Last-Modified` (sitemap pages) | 10 | 1 (`/learn`, academy engine) |
| Article/BlogPosting JSON-LD without `image` | 9 | 0 |
| Indexable self-canonical URLs missing from the sitemaps | 6 topic archives | 0 |
| Duplicate titles / descriptions among self-canonical pages | 0 / 1 (`/blog` pages 2–3) | 0 / 0 |
| Pages deeper than 3 clicks from home | 0 | 0 |
| Server render time p50 / p95 (dev build, all URLs) | 5 / 12 ms | 7 / 28 ms |
| LCP / CLS on the production build (mobile, local) | — | 200–350 ms / 0 on all 12 pages measured |

## Findings

Severity: **High** — hurts indexing, rich results or sharing on many URLs; **Medium** — hurts some URLs or signals;
**Low** — polish. Status: **Fixed** (this branch), **Accepted** (intended), **Owner** (another workstream; requirements
at the end).

| # | Severity | URL / pattern | Issue | Fix | Status |
|---|---|---|---|---|---|
| 1 | High | `/learn/*` (762 URLs) | `og:image` / `twitter:image` was the course badge SVG. Facebook, LinkedIn, X, Slack and WhatsApp do not render SVG, so every shared course or lesson showed no image. | Generated 1200×630 PNG social cards (`/og{path}.png?v=`), used whenever a page has no raster image of its own. | Fixed |
| 2 | High | 122 indexable pages (home, listings, services, industries, CMS pages, …) | Every page shared the same generic `og-default.png`. | Per-page social cards: brand look (navy/amber, logo, Work Sans), eyebrow with type and category, the h1, description and facts. Absolute, versioned, immutable-cached URLs with width, height, type and alt. | Fixed |
| 3 | Medium | 9 articles (4 blog posts without a cover, 5 case studies) | Article/BlogPosting JSON-LD had no `image` — Google's article rich results require one. | Article-type nodes without an image get the page's social image. | Fixed |
| 4 | Medium | all indexable pages | No `hreflang`. The site is English only; explicit `en` + `x-default` removes any language ambiguity and prepares for translations. | `<link rel="alternate" hreflang="en">` and `x-default` at the canonical URL. | Fixed |
| 5 | Medium | 716 pages (lessons, most listings) | No `Last-Modified` header; listing pages' header disagreed with the sitemap `lastmod` (`/blog`, `/careers`, `/faq`, topics). Crawlers use both to schedule recrawls. | Listing pages use the sitemap's rule (page texts / settings + newest listed update); any page without its own date falls back to its sitemap `lastmod`. | Fixed (`/learn` → Owner: academy engine) |
| 6 | Medium | `/blog?category=*` (6) | Topic archives are indexable, self-canonical landing pages but were in no sitemap; page 2+ of the archive had the same title and description as page 1 (blog page 2/3 had the same description). | Topics in the blog sitemap with `Blog › Topic` breadcrumbs; paginated archives get "— page N" titles and "Page N of M." descriptions. | Fixed |
| 7 | Medium | `/llms-full.txt`, `/llms.txt` | The academy (52 courses, 710 lessons — most of the site) was barely described for AI assistants: llms-full stopped after 150 pages, so almost no academy content was in it; no guide to lessons. `/llms.txt` took ~1.5 s to build on every request. | `/llms.txt` lists every course and links a new `/llms/academy.txt` (every course, module and lesson with its summary, video/transcript flag and `.md` link, 240 KB); llms-full covers agency and course pages (lessons have their own `.md`). Outputs cached per content state. | Fixed |
| 8 | Medium | video sitemap | Learning lectures could not reach the video sitemap (contributions carried no videos) and entries without a file or player URL were possible. | `SitemapContribution` carries `SeoVideo` lists (the same shape as the academy branch's YouTube lectures) and extra images; only videos with a thumbnail and a content or player URL are listed. | Fixed (engine plugs in lectures) |
| 9 | Low | IndexNow | Only changed URLs were submitted; removed pages (unpublished, deleted, moved) stayed in Bing/Yandex until recrawled. | The job remembers the submitted URL set and also submits URLs that left the sitemaps. | Fixed |
| 10 | Low | robots.txt | Several assistant fetchers that cite sources were not named (DuckAssistBot, MistralAI-User, Meta-ExternalFetcher, YouBot), and Amazonbot (Alexa, Rufus answers) was in the blocked "aggressive scrapers" group although the site wants to be cited by assistants. | Named in the allowed "AI search and assistants" group; Amazonbot moved there. Admins can still block any group. Verified with the domain robots parser: Googlebot, Bingbot, Applebot, GPTBot, OAI-SearchBot, ChatGPT-User, ClaudeBot, Claude-SearchBot, PerplexityBot, Google-Extended, Applebot-Extended, DuckAssistBot, Meta-ExternalFetcher, Amazonbot and unknown bots may read public pages, cards, llms files, sitemaps and uploaded images, but not portals, sign-in pages, the API, search or personal links. | Fixed |
| 11 | Low | Organization JSON-LD | No `contactPoint`. | `contactPoint` (customer service, email, telephone, `/contact`, English) from the contact settings. `sameAs` already comes from the social profiles (none set in the demo data — set them in Site settings). | Fixed |
| 12 | Low | head | No `og:image:type`, no `author` meta on articles; llms.txt not discoverable from pages. | Added; the SSR footer links the sitemap, llms.txt and RSS. | Fixed |
| 13 | Low | nginx | gzip skipped RSS, Atom, Markdown, JSON-LD and `text/xml`; no routes for `/og/` and `/llms/`. | Added (`scripts/test-web-nginx.sh` covers the new routes). Brotli needs a module the stock nginx image lacks. | Fixed |
| 14 | High | every page after hydration | The web app's head manager replaces the server's `og:image` with `og-default.png` (and keeps the server's `og:image:width`). Crawlers without JavaScript (all social networks) are unaffected; Google renders JavaScript but does not use `og:image`. | See requirement B1. | Owner: brand |
| 15 | Medium | `/learn` (hydrated) | The academy page shows fewer courses than the server HTML (28 course links exist only in the server HTML). Google indexes the rendered page; courses stay discoverable through the sitemap and SSR links, but the rendered hub should link them all (or paginate with crawlable links). | See A3. | Owner: engine |
| 16 | Medium | 710 lessons | Lesson meta descriptions (and llms summaries) run the first heading into the first paragraph ("What Optimize All is Optimize All connects…"). | See A1. | Owner: engine |
| 17 | Low | 18 pages (industries, some services) | Meta descriptions of 50–65 characters (target 70–155). | See B3. | Owner: brand/content |
| 18 | Low | `/`, `/academy`, `/pricing`, `/careers` | The server HTML had text the rendered page does not show. After the two-pillar redesign the home page and `/academy` are rendered in the web app's order and words (academy first: live counts, subjects, featured courses, paths, steps, certificates; then the agency, audit, services limited as shown, trust, dual call to action); Playwright now finds every server link on the rendered page. Remaining: pricing's "what's included" links and the careers call to action. | Home and /academy fixed; rest see B2. | Fixed / Owner: brand |
| 19 | Low | 25 blog posts | `og:image` is the uploaded cover, whose size is unknown, so no `og:image:width/height`. Networks fetch the image to size it; harmless. | — | Accepted |
| 20 | Low | `/lp/*` (4), `/c/*` (2) | Not linked from the site (orphans; they are in the landing-pages sitemap). Client landing pages are reached from ads and carry the client's brand, so no breadcrumb or agency card. Public creator campaigns could be linked from `/creators`. | See B4 for campaigns. | Accepted / Owner: brand |
| 21 | Low | JobPosting | No `validThrough` when a role has no closing date — Google only needs it when the posting expires. | — | Accepted |
| 22 | Info | `/get-a-quote?service=*` (33) | Indexable query variants canonical to `/get-a-quote` (duplicate title/description by design). | — | Accepted |
| 23 | Info | performance | Server render p95 < 30 ms, HTML p50 15 KB (max 52 KB); production build LCP 200–350 ms and CLS 0 on the 12 key pages measured (mobile). Server-side HTML caching is not needed. | — | Accepted |

Unchanged and verified: status codes (200/301/404/410, managed redirects one hop), one lower-case URL form without
trailing slash (301), `/index.html` → `/`, unknown slugs and out-of-range pages 404, closed jobs 410, noindex +
`X-Robots-Tag` on private, personal-link, search and filtered pages, `noindex` Markdown versions with a canonical
`Link` header, one h1 per page, no heading skips, `lang="en"`, viewport meta, theme colours, icons and manifest from
the shell, no images without `alt`, no horizontal scroll at 390 px, prev/next on paginated archives, security headers
from nginx (CSP, nosniff, X-Frame-Options, Referrer-Policy, Permissions-Policy, COOP; HSTS to enable once HTTPS-only).

## Requirements for other workstreams

### A. Academy engine (learning pages, JSON-LD, learn sitemap)

1. **Lesson descriptions:** build `LearningSeoDto.Description` from the lesson's summary field when present, else from
   the first paragraph *after* the first heading (never "Heading + paragraph" run together); 70–155 characters, cut at
   a word boundary without an ellipsis in the middle of a sentence. These feed the meta description, the card and
   `/llms/academy.txt`.
2. **Last-Modified parity:** set `page.ModifiedAt` for `/learn` to the same value the learn sitemap uses
   (`max(course.UpdatedAt)`; the page currently uses the newest `PublishedAt`), and for course and lesson pages to the
   course version's update time (the sitemap uses `course.UpdatedAt` for both). Until then lessons fall back to the
   sitemap value automatically.
3. **Academy hub:** the rendered `/learn` page must link every published course (or paginate with real `<a href>`
   links such as `/learn?page=2`, which the resolver would then render as self-canonical archive pages), and learning
   paths when they ship.
4. **Learning paths** (new pages): render them in `SeoPageResolver.Learning.cs` with `Source = "Learning path"` (the
   social card, llms.txt "Academy" section and academy guide pick them up), BreadcrumbList `Home › Academy › Paths ›
   Path`, an `ItemList` of the courses in order (`ListItem.url` to each course), a `Course`-free page type
   (`CollectionPage` or `LearningResource` with `hasPart`), and contribute them to the `learn` sitemap.
5. **Lecture videos:** keep contributing `SeoVideo` entries per lesson (thumbnail + `player_loc` embed URL, duration,
   publication date); keep the transcript as crawlable text on the page (it is already in the lesson's `.md`). Add a
   short `description` per lecture (not the title) for the video sitemap.
6. **Social cards:** optional — set `page.Card = new SocialCard(eyebrow, title, subtitle, facts)` in course/lesson
   builders instead of relying on the meta-line parsing in `SocialCardFactory` (e.g. facts "Free course · 12 lessons ·
   Beginner · Certificate", "Lesson 3 of 8 · 9 min · Video"). Do not set `OgImage` to an SVG: a non-raster image is
   replaced by the card anyway.
7. **Course JSON-LD:** keep `name`, `description` (≤ 60 words), `provider` (Organization with `sameAs` the site URL),
   `offers` (`price: 0`, `category: "Free"`) and `hasCourseInstance` (`courseMode: "online"`, `courseWorkload`) on
   every course — Google's course info rich result needs all of them; the integration test
   `TechnicalSeoTests.AssertValidJsonLd` checks name/description/provider.

### B. Brand / public pages (web app)

1. **Keep the server's social image after hydration** (`src/features/public/site/head.ts`): on the server-rendered
   page (`onServerRenderedPage`), do not overwrite `og:image` / `twitter:image` (or read the SSR tag's value and keep
   it); after client navigation, use the page's own image or the card URL pattern `/og{path}.png` (the API serves the
   current card for any `v`). Also stop removing `og:image:*` only on client navigations while replacing the image,
   so width/height never describe a different image.
2. **Server/rendered parity:** anything the server HTML shows should also be on the rendered page (home and /academy
   now match); pricing — a "What's included" link per service to `/services/{slug}#pricing`;
   careers — the `careers.cta.*` block.
3. **Meta descriptions under 70 characters:** extend the SEO descriptions of the industries (healthcare, finance,
   real-estate, SaaS, education, hospitality, …) and the other short ones to 70–155 characters.
4. **Public creator campaigns** (`/c/{slug}`): list the open ones on `/creators` (a real link each), so they are not
   orphans; the server renderer will mirror the list.
5. **Social profiles:** fill Site settings → Social (LinkedIn, Instagram, YouTube, …) so `Organization.sameAs` links
   the brand's profiles (knowledge panel, entity recognition by assistants).
