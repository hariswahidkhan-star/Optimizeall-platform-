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
page that used to be the home page now lives at `/creators`; `/faq` is unchanged.

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
action, logos, gallery). The editor shows a live preview with the site's own renderer. The seeded legal pages are
templates: have them reviewed by counsel before launch and keep the *Last updated* date honest.

**Blog.** Writers draft and submit; publishers publish now or schedule (checked every minute). Posts use Markdown
(`##` headings, lists, links, images, code); raw HTML is removed on save. The first heading level you use becomes the
page's h2, so outlines never skip a level. Use the SEO panel for a custom title, description, social image,
canonical URL or *noindex*.

**Leads.** Contact, audit, quote and booking submissions land in **Website inquiries** with their UTM source,
referrer and landing page, and CRM receives each one as a lead (`WebsiteInquiryReceived`). Set the status and notes,
assign it to a teammate, filter by assignee, mark it closed, erase it (spam or a data-erasure request) or export CSV.
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
*Go live at* time; it stays hidden (and out of the sitemap) until then.

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
  rows hourly once the token has expired.
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

- `GET /robots.txt` is generated by the API: it disallows every signed-in area (`/app`, `/agency`, `/client`,
  `/admin`, `/finance`, `/review`, `/manage`) and `/api` except the sitemap, and points to it.
- `GET /api/v1/public/sitemap.xml` lists every published, indexable URL with `lastmod`; noindex, unpublished and
  scheduled content is left out. Absolute URLs use **Site settings → SEO → Site URL** (fallback `Email:AppBaseUrl`),
  so set it to the public origin before launch.
- `frontend/nginx/default.conf.template` proxies `/robots.txt` and `/sitemap.xml` to the API. If you serve the web
  app another way, route both paths to the API the same way (`/robots.txt` → API `/robots.txt`, `/sitemap.xml` → API
  `/api/v1/public/sitemap.xml`). Submit the sitemap URL in Google Search Console.
- The RSS feed is `GET /api/v1/public/blog/rss.xml`.

**The site is a client-rendered SPA.** Google and Bing execute JavaScript and see the full page, titles, meta tags
and JSON-LD (the head manager writes them after the data loads); the sitemap guarantees discovery. Crawlers and link
previews that do not run JavaScript (some social networks, older bots) only see the app shell's default title,
description and `og-image.png`. If link previews for individual pages matter, add prerendering (for example a
build-time prerender of the public routes, or a prerender service in nginx keyed on bot user agents) — not included
in this workstream.

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
- Staff notifications: each website inquiry notifies users with `site.manage` (in-app, linking to
  `/agency/website/inquiries/{id}`).
- Booking conflicts are prevented by a unique slot key; the loser of a race gets `409 website.slot_taken` and the page
  refreshes the slot list.
- Configuration: `Website:MinFormFillSeconds` (default 3), `Content:AllowedImageHosts`, `Email:AppBaseUrl`.
