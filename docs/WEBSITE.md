# Agency website & CMS

The public marketing site of Optimize All (the agency) and the CMS that runs it. The API reference is
[`docs/api/website.md`](api/website.md).

| Layer | Where |
|---|---|
| Entities | `backend/src/OptimizeAll.Domain/Website/` (catalog, blog, careers, leads, rules such as the Markdown sanitizer and slot calculation) |
| EF mapping | `backend/src/OptimizeAll.Infrastructure/Persistence/Configurations/WebsiteConfigurations.cs` (`website_*` tables) |
| API | `backend/src/OptimizeAll.Api/Modules/Website/` (registered by `AddWebsiteModule`) |
| Seeds | `Modules/Website/Seed/` — baseline (always, idempotent by slug) and demo (Order 300, once, marker `website.demo_seeded`) |
| Public site | `frontend/src/features/public/` (`routes.tsx`, `site/` chrome and helpers, `pages/`) |
| CMS | `frontend/src/features/agency/website/` (agency portal → Website) |
| Tests | `backend/tests/*/Website/`, `frontend/src/features/public/website.test.tsx`, `frontend/e2e/smoke/website.spec.ts` |

## Public pages

`/` home · `/services` and `/services/{slug}` · `/pricing` · `/industries` and `/industries/{slug}` ·
`/case-studies` and `/case-studies/{slug}` (filter with `?service=` / `?industry=`) · `/team` · `/blog` (filter with
`?category=`, `?tag=`, `?q=`) and `/blog/{slug}` · `/careers` and `/careers/{slug}` (apply with a PDF CV) ·
`/contact` · `/free-audit` · `/get-a-quote` (3 steps) · `/book-a-consultation` · `/newsletter/confirm` and
`/newsletter/unsubscribe` · `/search` · every published CMS page at `/{slug}`: the seeded `/about`, `/how-we-work`,
and the legal pages `/privacy-policy`, `/terms-of-service`, `/cookie-policy`, `/accessibility`, `/refund-policy`. The creator (participant) landing
page that used to be the home page now lives at `/creators`; `/faq` is unchanged. A CMS page cannot take the address
of a built-in route (`/services`, `/blog`, `/login`, `/verify-email`, `/reset-password`, `/lp`, `/f`, the portals…:
`CatalogAdminService.ReservedSlugs`, 400 `website.invalid`), since the built-in route would always win while the
sitemap listed the page; `pricing` and `contact` are allowed on purpose — `/pricing` and `/contact` embed that CMS page.

Every page has a unique title and description, a canonical URL, Open Graph/Twitter tags and, where it fits, JSON-LD
(Organization, WebSite, Service + Offer, FAQPage, BreadcrumbList, Article/BlogPosting, JobPosting). The header has a
keyboard-accessible services mega-menu built from the published catalog; phones get a drawer. The footer, contact
details (including a WhatsApp link), social links and the announcement bar come from site settings.

## Marketer guide

Sign in and open **Agency → Website**. What you can see depends on your role:

| Permission | Sections |
|---|---|
| `site.manage` | Everything below except the blog and careers (unless you also hold those permissions) |
| `crm.view` | Overview and Website inquiries (read only) |
| `blog.write` | Blog: write drafts, submit for review |
| `blog.publish` | Blog: review, publish, schedule, unpublish |
| `careers.manage` | Careers: jobs and applications |

**Services & packages.** Services belong to a category (the mega-menu columns). A service needs a name, a tagline and
at least one published category to appear. Packages carry a price, a currency and a billing period (one-time,
monthly, quarterly, yearly) plus an optional setup fee; tick *Custom quote* instead of entering a price for bespoke
work. Mark at most one package per service as *Most popular*. Unpublishing hides the service everywhere (menu,
pricing, sitemap, search) without deleting it. Package ids are stable, so CRM proposals and billing keep pointing at
them after you rename a package.

**Case studies and stats.** Every number must say whether it is *Measured* (from tracked data) or *Estimated*; the
site shows the label next to the figure. Anonymize the client when you do not have written permission to name them.

**Pages.** Pages are built from blocks (hero, rich text, features, stats, testimonials, case studies, FAQ, call to
action, logos, gallery, video). The editor shows a live preview with the site's own renderer. The seeded legal pages are
templates: have them reviewed by counsel before launch and keep the *Last updated* date honest.

**Blog.** Writers draft and submit; publishers publish now or schedule (checked every minute). Posts use Markdown
(`##` headings, lists, links, images, code); raw HTML is removed on save. The first heading level you use becomes the
page's h2, so outlines never skip a level. Use the SEO panel for a custom title, description, social image,
canonical URL or *noindex*.

**Leads.** Contact, audit, quote and booking submissions land in **Website inquiries** with their UTM source,
referrer and landing page, and CRM receives each one as a lead (`WebsiteInquiryReceived`). Set the status and notes,
assign it to a teammate, filter by assignee, mark it closed, erase it (spam or a data-erasure request) or export CSV.
The export holds exactly what the list shows: it takes the same filters (search, type, status, assignee, dates, UTM
source, service), and the newsletter export takes the list's status and search. Over 50,000 matching inquiries (or
100,000 subscribers) the export is refused with 422 `export.too_large` and a message to narrow the filters.
**Consultations** holds the weekly availability, slot length, minimum notice, blackout days and the booking list (cancel, reschedule, mark completed or no-show). **Newsletter** lists double opt-in subscribers; only
confirmed ones are exported for sending, and every email must carry the unsubscribe link. Staff can unsubscribe an
address on request or erase it entirely. Job applications can be erased with their notes and CV. The wording of the
newsletter-confirmation and consultation emails is edited in Admin → Content → Email templates.

**Page texts.** Every headline, introduction, button label, checklist and SEO title of the built-in pages (home,
services, pricing, industries, case studies, blog, team, careers, the contact/audit/quote/booking forms, the creators
page, the footer newsletter and the cookie banner) is edited under **Page texts**, grouped by page. Lists take one item
per line; steps, rules and FAQ teasers take `Title | Text` per line. *Reset to default* restores the original wording.
See [DYNAMIC_CONTENT.md](DYNAMIC_CONTENT.md).

**Page history and scheduling.** Every save of a page is kept as a version (with an optional change note). Preview an
old version in the editor and restore it as a new version — handy for legal pages. A published page can get a
*Go live at* time; it stays hidden (and out of the sitemap) until then. **Delete page** (editor header, or the row
menu in the Pages list; `site.manage`, audited as `website.page_deleted`) removes a page and its version history after
a confirmation; unpublish instead to keep it.

**Redirects.** Changing the slug of *live* content — a published page, blog post, service, service line, case study
or industry, or publishing a landing page under a new address — records a permanent (301) redirect from the old
address to the new one, in the same transaction as the change, so links and search rankings keep working. The
sitemap lists only the new address. **Website → Redirects** (`site.manage`) lists them (automatic and manual, with
search), adds manual ones (e.g. after a site migration: old path → new path, both same-site paths) and deletes any;
every change is audited (`website.redirect_created|updated|removed|deleted`) and adding/deleting is refused while
impersonating (redirects decide where every visitor of an address lands, like the site settings). Rules:

- Chains are collapsed: renaming A → B → C leaves A → C and B → C; a manual redirect to a redirected address points at
  its final address, and one that would lead back to itself is refused (`website.redirect_loop`).
- An address that shows live content is never redirected: publishing content at a redirected address removes the
  redirect (`website.redirect_removed`), a manual redirect from a live address is refused
  (`website.redirect_source_live`), and lookups check liveness again (scheduled pages that just went live win).
- Renames of drafts, and renames that unpublish at the same time, record nothing (there is nothing live to send
  visitors to). A blog post remembers the slug it was last live under (`LastLiveSlug`), so when a post that was
  unpublished (or returned to draft) and renamed goes live again — published by hand or by the scheduler — its old
  address redirects to the new one like a rename of the live post, unless another post has taken that slug since. Built-in pages (`/`, `/services`, `/blog`, `/pricing`, …) and the portals, API and short links
  (`/agency`, `/api`, `/t`, …) are never redirect sources.
- A service line is addressed as `/services?category={slug}`; other query parameters (UTM tags) are carried over to
  the target.
- How it is served: every full page load of a public address is rendered by the API (`/_document{path}`, see
  [SEO_CRO.md](SEO_CRO.md) § Rendering), which answers a moved address with a real **301** to its new address before
  anything else (one hop, query parameters carried over). nginx sends every non-file path there (`location @document`
  in `frontend/nginx/default.conf.template`); `vite` and `vite preview` do the same through the `seoShell` plugin
  (`frontend/seoShell.ts`). If the API is unreachable, the plain app shell is served (503). Inside the app, a
  not-found public page asks `GET /api/v1/public/redirects?path=` and navigates to the new address (client-side
  navigation). If you serve the web app another way, route page requests through `/_document` or accept client-side
  redirects only.

**Ordering.** Service lines, services, industries, case studies, testimonials, team members and blog categories have a
*Reorder* button: drag rows, or use the keyboard (Space to pick up, arrow keys to move, Space to drop).

**Site settings.** Navigation, footer, contact and social, announcement bar, SEO defaults (site URL, title template,
default description and image), the organization details used in structured data, analytics ids, home stats and
trust logos. Only add logos of clients who agreed to be shown.

**Images.** Upload images in any image field (JPEG, PNG, WebP or GIF up to 10 MB); they are re-encoded and served
from `/api/v1/files/…`. External image URLs must be `https://` on a host listed in `Content:AllowedImageHosts` (and
in the web server's `IMG_SRC_EXTRA`, see below).

## Forms, spam and privacy

- Every public form fetches a signed token (`GET /public/forms/token`). Submissions faster than
  `Website:MinFormFillSeconds` (default 3 s) or with a token older than 24 hours are rejected; a hidden honeypot
  field silently drops bots; the `public` rate limit (120/min per IP) applies. Tokens are single-use: a successful
  inquiry, consultation booking or job application stores the SHA-256 of the token id (`website_used_form_tokens`),
  and a replay gets 409 `website.form_already_submitted` (the site fetches a fresh token after each success; the
  newsletter form, which is idempotent per address, does not spend tokens). `UsedFormTokenCleanupJob` deletes the
  rows hourly once the token has expired. Landing-page and embedded forms (`/lp/…`, `/f/…`) work the same way: each
  rendered form carries a single-use render token (random id inside the signed token), spent in the submission's
  transaction; a replay — even concurrent, even from another network — gets 409 `forms.already_submitted`, while
  honeypot hits, validation errors and rate-limited attempts leave the token unspent.
- Consent is explicit (unticked checkbox) and stored with its text version (`forms-2026-09`, `newsletter-2026-09`,
  `careers-2026-09`) and time. Change the text in `Leads/FormGuard.cs` → bump the version.
- CVs must be real PDFs (checked by content) up to 5 MB, stored privately in the database; downloads are audited.
- Newsletter signup is double opt-in (48 h confirmation link) and never reveals whether an address is already
  subscribed. The unsubscribe token is an HMAC of the subscriber id, so it never needs storing.
- Campaign attribution (UTM, referrer, landing page) is kept in `sessionStorage` for the visit only.

## Analytics and the cookie banner

Analytics ids live in site settings. Nothing is loaded — and the banner is not even shown — until at least one id is
set. Then the banner offers *Accept all*, *Reject all* and *Customize* (Necessary / Analytics / Marketing), stored in
`localStorage` (`oa.consent`, versioned) and re-openable from **Cookie settings** in the footer. GA4 loads only with
*Analytics* consent; GTM and the Meta Pixel only with *Marketing* consent.

The web server's Content-Security-Policy is `script-src 'self'` today, so the vendor scripts are **blocked** until you
extend it. When you configure ids, add their hosts to `frontend/nginx/snippets/security-headers.conf` (shown wrapped
for reading: write the value on a single line, because a header value must not contain line breaks):

```nginx
# GA4 (+ GTM and the Meta Pixel if used). Remove the hosts of tags you do not use.
add_header Content-Security-Policy "default-src 'self';
  script-src 'self' https://www.googletagmanager.com https://connect.facebook.net;
  style-src 'self' 'unsafe-inline';
  img-src 'self' data: blob: $oa_img_src_extra https://www.google-analytics.com https://www.googletagmanager.com https://www.facebook.com;
  font-src 'self' data:;
  connect-src 'self' https://*.google-analytics.com https://*.analytics.google.com https://www.googletagmanager.com https://www.facebook.com https://connect.facebook.net;
  frame-src https://www.googletagmanager.com;
  media-src 'self' blob:; worker-src 'self' blob:; manifest-src 'self'; object-src 'none';
  base-uri 'self'; form-action 'self'; frame-ancestors 'none'" always;
```

Tags configured inside GTM may need further hosts; add only what you use. JSON-LD blocks are data (`type="application/ld+json"`),
not scripts, so they need no CSP change.

## SEO files and rendering

Full reference: [SEO_CRO.md § 9](SEO_CRO.md#9-technical-seo-of-the-public-website). In short:

- **Every public page is server-rendered.** nginx sends page requests to the API (`/_document{path}`), which answers
  with the real status (200, 301, 404, 410) and complete HTML — title, description, canonical, robots, Open Graph,
  Twitter tags, JSON-LD and the page's headings, copy and links — around the app shell (SSI includes of
  `dist/__shell/*.html`). Search engines, AI crawlers and link previews get the full page without running JavaScript;
  visitors get the React app, which boots over it without a flash or duplicate tags. `vite` and `vite preview` do the
  same through the `seoShell` plugin. If you serve the web app another way, route page requests (everything that is
  not a file, an API path or a portal) to the API's `/_document…` and fill the two includes.
- **Metadata**: built-in pages take their search title and description from **Page texts** (`{page}.seo.title`,
  `{page}.seo.description`), the home page from Site settings → SEO, content from its SEO panel. Titles stay within 60
  characters with the " | Optimize All" suffix, descriptions within 155. **Agency → Website → SEO** lists every public
  URL with what search engines see and warnings, and edits built-in page snippets in place.
- `GET /robots.txt` follows the crawler policy (Agency → Website → SEO → Crawlers & AI): search engines, AI assistants
  and reputable AI training crawlers are allowed by default, aggressive scrapers blocked; portals, sign-in pages, the
  API, personal links and search results are closed to all; it points to `/sitemap.xml`.
- `GET /sitemap.xml` is a sitemap index (pages, services, case studies, blog, careers, landing pages, images, videos)
  of published, indexable, self-canonical URLs with real `lastmod`. `/api/v1/public/sitemap.xml` still answers with the
  flat list. Absolute URLs use **Site settings → SEO → Site URL** (fallback `Email:AppBaseUrl`), so set it to the
  public origin before launch, and submit `https://<site>/sitemap.xml` in Google Search Console and Bing Webmaster Tools.
- `GET /llms.txt`, `/llms-full.txt` and `/{path}.md` describe the site for AI assistants; `/.well-known/security.txt`,
  `/humans.txt`, `/site.webmanifest` and the favicons are served too. IndexNow is available (off by default).
- The RSS feed is `GET /api/v1/public/blog/rss.xml`.
- **Videos** are self-hosted under `frontend/public/media/videos/`: add them to a CMS page with the Video block, or to a
  built-in page in `siteVideos.json` (SEO_CRO.md § 9.5).

## Seeds

- **Baseline** (`WebsiteBaselineSeeder`, runs on every start, creates only what is missing by slug): 9 service
  categories and 33 services with packages and production copy, 9 industries, the About / How we work / Pricing /
  Contact pages, 5 legal page templates, 7 blog categories, default site settings and booking availability
  (Mon–Fri 09:00–12:00 and 13:00–17:00 UTC, 30-minute slots, 12 h notice, 30 days ahead). Edits made in the CMS are never overwritten.
- **Demo** (`WebsiteDemoSeeder`, Order 300, `Demo` seed profile, once): team members, case studies,
  testimonials, blog posts in every workflow state, jobs and applications, inquiries, bookings and subscribers.

## Operations

- Scheduled posts: `BlogSchedulerJob` runs every minute through the job runner; it is idempotent and safe on several
  instances.
- Staff notifications: each website inquiry notifies users with `site.manage` or `crm.manage` (in-app, linking to
  `/agency/website/inquiries/{id}`); staff and client portals show them under the bell in the top bar.
- Booking conflicts are prevented by a unique slot key; the loser of a race gets `409 website.slot_taken` and the page
  refreshes the slot list.
- Configuration: `Website:MinFormFillSeconds` (default 3), `Content:AllowedImageHosts`, `Email:AppBaseUrl`.
