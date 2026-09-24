# Agency website & CMS API

Module owner: `Api/Modules/Website` (entities in `Domain/Website`, tables `website_*`). Conventions follow
`docs/ARCHITECTURE.md`: JSON is camelCase, enums are strings, timestamps are UTC ISO-8601, errors are RFC 7807
problems with a stable `code` (field validation adds `errors`, keyed by camelCase path such as
`packages[0].price` or `header.menu[2].children[0].url`). Paged lists take `?page=1&pageSize=25&search=` and return
`{ "items", "total", "page", "pageSize", "totalPages" }`.

Frontend types mirror these shapes one to one: public DTOs in `frontend/src/features/public/site/api.ts`, staff DTOs
in `frontend/src/features/agency/website/api.ts`.

**Permissions.** `site.manage` (catalog, content, pages, settings, bookings, newsletter, inquiry updates),
`blog.write` / `blog.publish` (editorial workflow), `careers.manage` (jobs and applications), `crm.view` (read the
inquiry inbox). The service catalog read (`/agency/website/catalog`) is also open to `proposals.manage` and
`billing.view`, so CRM, proposals and billing can reference packages. A missing permission answers
`403 auth.forbidden` **before** body validation. Every privileged write is audited (`website.*`, `blog.*`,
`careers.*` actions).

**Concurrency.** Every editable record returns a `concurrencyStamp`; updates must send it back and answer
`409 concurrency.conflict` when someone else saved first.

**Public endpoints** are anonymous (`[AllowAnonymous]`) and rate limited with the `public` policy (120 requests per
minute per IP). They only ever return published content: an unpublished service (or a service in an unpublished
category), industry, case study, testimonial, team member, page, a draft/scheduled post or a closed job answers
`404 website.not_found` or is left out of lists.

**Content safety.** Links must be `https://` or app-relative (`/pricing`); images must be uploads
(`/api/v1/files/…`) or `https://` URLs on `Content:AllowedImageHosts`. Markdown is sanitized on save (raw HTML,
`<script>`/`<style>`/`<iframe>` blocks, comments and unsafe link schemes are removed) and rendered by the web app
without `innerHTML`. Money is `decimal` rounded to the currency (`Money.Round`) and always carries its ISO currency.

Contents: [Public site](#public-site) · [Public forms](#public-forms) · [SEO files](#seo-files) ·
[Catalog](#services--packages-sitemanage) · [Content](#industries-case-studies-testimonials-team-sitemanage) ·
[Pages](#pages-sitemanage) · [Settings](#site-settings-sitemanage) · [Blog](#blog) · [Careers](#careers-careersmanage) ·
[Leads](#inquiries-bookings-newsletter) · [Images](#images) · [Events](#domain-events) · [Error codes](#error-codes)

---

## Public site

| Method & path | Returns |
|---|---|
| `GET /api/v1/public/site` | Site name, header menu + CTA, footer, contact, social, trust logos, announcement bar, default SEO, analytics ids, `serviceMenu` (published categories → services for the mega-menu), consent texts and versions, `bookingEnabled`. |
| `GET /api/v1/public/home` | `serviceCategories`, `featuredCaseStudies`, `testimonials`, `industries`, `latestPosts`, `pricingTeaser`, `stats`, `trustLogos`, `seo`, `jsonLd` (Organization + WebSite). |
| `GET /api/v1/public/services` | Published categories with their published services (`startingPrice` = cheapest active priced package). |
| `GET /api/v1/public/services/{slug}` | Service detail: hero, overview Markdown, problems solved, deliverables, process steps, tools, KPIs, FAQs, active `packages`, related services, case studies, testimonials, `seo`, `jsonLd` (Service with Offers, FAQPage, BreadcrumbList). |
| `GET /api/v1/public/pricing` | Every published service with its active packages. |
| `GET /api/v1/public/industries` · `/industries/{slug}` | Industry cards / detail with challenges, recommended services, case studies, FAQs and JSON-LD. |
| `GET /api/v1/public/case-studies?service=&industry=` · `/case-studies/{slug}` | Cards filtered by service and/or industry slug / detail with challenge, strategy, execution, metrics (each `measurement: Measured \| Estimated`), testimonial, gallery, JSON-LD. |
| `GET /api/v1/public/testimonials` | Up to 50 published testimonials. |
| `GET /api/v1/public/team` | Published team members. |
| `GET /api/v1/public/pages/{slug}` | CMS page: `blocks` (see [Pages](#pages-sitemanage)), `kind` (`Standard \| Legal`), `updatedAt`, `seo`. |
| `GET /api/v1/public/search?q=` | `{ query, services, posts, caseStudies }` (each hit `{ kind, slug, title, summary, url }`); fewer than 2 characters returns empty lists. |
| `GET /api/v1/public/blog?page=&pageSize=&category=&tag=&search=` | `{ items: PostCard[], total, page, pageSize, categories (with postCount), tags }`. `pageSize` 1–50 (default 9). |
| `GET /api/v1/public/blog/{slug}` | Post with sanitized `bodyMarkdown`, author, related posts, `seo`, `jsonLd` (BlogPosting + BreadcrumbList). |
| `GET /api/v1/public/blog/rss.xml` | RSS 2.0 feed of the 30 latest posts (`application/rss+xml`). |
| `GET /api/v1/public/careers` · `/careers/{slug}` | Open jobs / job detail with requirements, benefits, optional salary range and JobPosting JSON-LD. |
| `GET /api/v1/public/redirects?path=/old-page` | Where a moved public address lives now: `{ location, statusCode: 301 }` (other query parameters of `path` carried over), else 404 `website.redirect_not_found`. The web app asks this when a public page is not found and navigates client-side. |
| `GET\|HEAD /api/v1/public/redirects/gate` | For the web server (not the app): the request target in `X-Original-URI` → `301` + `Location` when moved, else an empty `404` (serve the app shell). nginx and the Vite dev/preview server ask it before serving the shell. `tracking` rate-limit policy. |

Example (`GET /public/services/seo`, trimmed):

```json
{
  "id": "0192…", "slug": "seo", "name": "Search engine optimization", "tagline": "Rank for what buyers search.",
  "categorySlug": "search", "categoryName": "Search & content",
  "packages": [
    { "id": "0192…", "name": "Growth", "price": 2400.00, "currency": "USD", "billingPeriod": "Monthly",
      "setupFee": null, "features": ["…"], "isMostPopular": true, "isCustomQuote": false }
  ],
  "faqs": [{ "question": "How long does SEO take?", "answer": "…" }],
  "seo": { "title": "SEO services", "description": "…", "ogImageUrl": null, "canonicalUrl": null, "noIndex": false },
  "jsonLd": [{ "@context": "https://schema.org", "@type": "Service", "name": "Search engine optimization", "offers": [ … ] }]
}
```

`billingPeriod`: `OneTime | Monthly | Quarterly | Yearly`. A custom-quote package has `price: null`.

## Public forms

Every form body extends a common envelope:

```json
{
  "nickname": "",                 // honeypot: must stay empty (hidden field)
  "formToken": "CfDJ8…",          // from GET /public/forms/token
  "consent": true,
  "consentVersion": "forms-2026-09",
  "utm": { "source": "google", "medium": "cpc", "campaign": "q4", "term": null, "content": null },
  "referrer": "https://www.google.com/",
  "landingPath": "/services/seo"
}
```

Anti-spam: the token is signed by ASP.NET Data Protection and carries its issue time. A submission sooner than
`Website:MinFormFillSeconds` (default 3) after issue answers `400 website.form_too_fast`; a token older than 24 h or
tampered with answers `400 website.form_expired`. A filled honeypot answers exactly like a success but stores and
publishes nothing. Consent must be `true` with the current version (`400 website.consent_required`). The consent
version and timestamp are stored with the record. Plus the `public` rate limit.

| Method & path | Body (besides the envelope) | Success |
|---|---|---|
| `GET /api/v1/public/forms/token` | — | `{ token, minFillSeconds, budgetRanges[], timelines[] }` (options are `{ value, label }`). |
| `POST /api/v1/public/inquiries/contact` | `name`, `email`, `phone?`, `company?`, `website?`, `message` (10–5000), `serviceSlugs?` | `202 { reference, message }` |
| `POST /api/v1/public/inquiries/audit` | contact details (`website` required), `goals`, `budgetRange`, `serviceSlugs?`, `competitors?`, `message?` | `202 { reference, message }` |
| `POST /api/v1/public/inquiries/quote` | contact details, `serviceSlugs?`, `packageIds?` (at least one of the two), `budgetRange`, `timeline`, `message` | `202 { reference, message }` |
| `GET /api/v1/public/consultations/slots?from=&days=14` | — | `{ timeZone, slotMinutes, enabled, slots: ["2026-09-28T15:00:00Z", …] }` (free UTC instants honouring weekly availability, blackouts, minimum notice and max days ahead). |
| `POST /api/v1/public/consultations` | contact details, `slotStart` (UTC), `visitorTimeZone` (IANA), `notes?`, `serviceSlugs?` | `201 { reference, slotStart, slotEnd, visitorTimeZone, message }` · `409 website.slot_taken` · `400 website.booking_disabled` |
| `POST /api/v1/public/newsletter/subscribe` | `email`, `source?` (consent version `newsletter-2026-09`) | `202 { status, message }` — always the same answer, whether new, pending or already confirmed (no enumeration). Emails a single-use confirmation link valid 48 h. |
| `POST /api/v1/public/newsletter/confirm` | `{ token }` | `{ status: "Confirmed", message }` · `400 website.newsletter_invalid_token` |
| `POST /api/v1/public/newsletter/unsubscribe` | `{ token }` (HMAC token from every newsletter link) | `{ status: "Unsubscribed", message }` (idempotent) |
| `POST /api/v1/public/careers/{slug}/applications` | multipart: `name`, `email`, `phone?`, `portfolioUrl?`, `coverLetter?`, `cv` (PDF ≤ 5 MB, checked by magic bytes and trailer), envelope fields (consent version `careers-2026-09`) | `202 { reference, message }` |

Budget ranges, timelines and quote packages are validated against the server's lists; package ids must be active
packages of published services. Every accepted inquiry or booking is stored as a `WebsiteInquiry` and published once
as `WebsiteInquiryReceived` after commit (see [events](#domain-events)).

## SEO files

| Path | Notes |
|---|---|
| `GET /robots.txt` | Served by the API (nginx proxies it). Disallows every signed-in portal (`/app`, `/agency`, `/client`, `/admin`, `/finance`, `/review`, `/manage`) and `/api` except the sitemap; points to the sitemap. |
| `GET /api/v1/public/sitemap.xml` (also `/sitemap.xml` via nginx) | Every published, indexable URL: home, index pages, services, industries, case studies, blog posts, CMS pages and open jobs, with `lastmod`. `noIndex`, unpublished and scheduled content is never listed. Absolute URLs use `seo.siteUrl` from site settings, else `Email:AppBaseUrl`. |

## Services & packages (`site.manage`)

Base path `/api/v1/agency/website`.

| Method & path | Notes |
|---|---|
| `GET /service-categories` | All categories with `serviceCount`. |
| `POST /service-categories` · `PUT /service-categories/{id}` | `{ slug?, name, description?, icon?, sortOrder, isPublished, concurrencyStamp (PUT) }`. The slug is generated from the name when empty; `409 website.slug_taken`. |
| `DELETE /service-categories/{id}` | `409 website.category_in_use` while it has services. |
| `POST /service-categories/reorder` | `{ ids: [...] }` — the full list in the new order (`400 website.reorder_unknown / reorder_duplicates`). |
| `GET /services?categoryId=&search=&page=&pageSize=` | Summaries. |
| `GET /services/{id}` | Full service with packages. |
| `POST /services` · `PUT /services/{id}` | `{ categoryId, slug?, name, tagline, heroTitle?, heroBody?, overviewMarkdown?, problemsSolved[], deliverables[], processSteps[{ title, description }], tools[], kpis[], faqs[{ question, answer }], relatedServiceIds[], icon?, heroImageUrl?, ctaLabel?, ctaUrl?, seo, isPublished, isFeatured }` |
| `DELETE /services/{id}` | `409 website.in_use` while other records still reference it. |
| `POST /services/reorder` | `{ ids }` within a category. |
| `POST /services/{serviceId}/packages` · `PUT …/packages/{id}` · `DELETE …/packages/{id}` | `{ name, description?, price? (≥ 0, rounded), currency (ISO 4217), billingPeriod, setupFee?, features[], isMostPopular, isCustomQuote, isActive, sortOrder }`. A custom-quote package has no price; others need one. |
| `GET /catalog?includeUnpublished=false` | `site.manage`, `crm.view`, `proposals.manage` or `billing.view`. Stable `ServiceInfo[]` (service id/slug/name + packages with price, currency and billing period) for other modules; C# consumers use `IServiceCatalog`. |

## Industries, case studies, testimonials, team (`site.manage`)

| Method & path | Notes |
|---|---|
| `GET /industries` · `GET /industries/{id}` · `POST` · `PUT /{id}` · `DELETE /{id}` | `{ slug?, name, summary, bodyMarkdown?, challenges[], icon?, heroImageUrl?, serviceIds[], faqs[], isPublished, sortOrder, seo }` |
| `GET /case-studies` · `GET /case-studies/{id}` · `POST` · `PUT /{id}` · `DELETE /{id}` | `{ slug?, title, clientName, clientAnonymized, summary, industryId?, serviceIds[], challengeMarkdown?, strategyMarkdown?, executionMarkdown?, metrics: [{ label, value, measurement: Measured \| Estimated, context? }], testimonialQuote?, testimonialAuthor?, testimonialRole?, coverImageUrl?, galleryImageUrls[], seo, isPublished, isFeatured, sortOrder }`. Every metric must say whether it is measured or estimated. |
| `GET /testimonials` · `POST` · `PUT /{id}` · `DELETE /{id}` · `POST /testimonials/reorder` | `{ quote, authorName, authorRole?, company?, rating? (1–5), avatarUrl?, serviceId?, isPublished, isFeatured, sortOrder }` |
| `GET /team` · `POST` · `PUT /{id}` · `DELETE /{id}` · `POST /team/reorder` | `{ slug?, name, role, bio?, photoUrl?, expertise[], socialLinks: [{ label, url }], isPublished, sortOrder }`. Team members are also the blog authors. |

## Pages (`site.manage`)

| Method & path | Notes |
|---|---|
| `GET /pages` · `GET /pages/{id}` | Summaries / full page. |
| `POST /pages` · `PUT /pages/{id}` | `{ slug?, title, summary?, kind: Standard \| Legal, blocks: [{ type, data }], seo, isPublished, sortOrder }`. Reserved slugs (routes the web app owns, e.g. `services`, `blog`, `careers`) are rejected. |
| `DELETE /pages/{id}` | Deletes the page and its versions (audited `website.page_deleted`). No reason is required. |
| `POST /pages/preview` | Validates and normalizes `blocks` without saving (the editor's live preview uses the same renderer as the site). |

Block types (`type` → `data`), all validated server-side (links, images, lengths, at most 40 blocks):
`hero` (eyebrow, title, subtitle, primary/secondary CTA, imageUrl) · `richText` (markdown) · `features`
(title, items[icon, title, text]) · `stats` (items[label, value, measurement, context]) · `testimonials` (title, ids
or featured) · `caseStudies` (title, ids or featured) · `faq` (title, items[question, answer]) · `cta` (title, text,
button) · `logos` (title, items[name, imageUrl, url]) · `gallery` (images[url, alt]).

Legal pages (privacy policy, terms, cookie policy, accessibility, refund policy) are seeded as templates marked for
review by counsel; the web app shows their `updatedAt`.

Renaming the slug of live content (pages, posts, services, service lines, case studies, industries — and publishing
a renamed landing page) records a 301 redirect from the old address in the same transaction; see
[Redirects](#redirects-sitemanage).

## Redirects (`site.manage`)

| Method & path | Notes |
|---|---|
| `GET /redirects?search=&source=Automatic\|Manual&page=&pageSize=` | `{ items: [{ id, fromPath, toPath, source, contentType?, contentId?, createdAt, updatedAt }], total, page, pageSize }`, newest first. |
| `POST /redirects` | `{ fromPath, toPath }` — same-site paths (`/old-page`, `/services?category=old-line`). Normalized (lower case, no trailing slash). `400 website.invalid_redirect` (built-in page, portal/API path, not a same-site path), `409 website.redirect_exists`, `409 website.redirect_source_live` (the address shows published content), `409 website.redirect_loop`. A target that is itself redirected is replaced by its final address; redirects to `fromPath` are re-pointed at the target. Denied while impersonating. |
| `DELETE /redirects/{id}` | `204`. Denied while impersonating. |

Every change is audited (`website.redirect_created`, `website.redirect_updated` when a chain is collapsed,
`website.redirect_removed` when live content claims the address again, `website.redirect_deleted`).

## Site settings (`site.manage`)

| Method & path | Notes |
|---|---|
| `GET /settings` | `{ settings, updatedAt, concurrencyStamp }` |
| `PUT /settings` | `{ settings, concurrencyStamp }`. Validates every field: header menu (≤ 10 items, ≤ 40 sub-items each), header CTA, footer columns (≤ 6 × 15 links) and legal links, contact (email, phone, WhatsApp in E.164), social profiles (known platforms, `https://`), trust logos (≤ 24), announcement bar, SEO defaults (`siteUrl` must be an https origin, `titleTemplate` must contain `%s`, X handle), organization schema, analytics ids (`G-XXXXXXX`, `GTM-XXXXXX`, 10–20 digit Meta Pixel id) and home stats (≤ 8, each measured or estimated). |

## Blog

Base path `/api/v1/agency/website/blog`; `blog.write` or `blog.publish` unless noted. Workflow:
`Draft → InReview → (Scheduled →) Published → Archived`. Writers create posts, edit Draft/InReview posts and submit
them; publishers can do everything, including editing live posts. Detail responses include `can` flags
(`edit`, `submit`, `publish`, `schedule`, `unpublish`, `returnToDraft`, `delete`) so the UI hides what the caller cannot do.

| Method & path | Notes |
|---|---|
| `GET /posts?status=&categoryId=&search=&page=&pageSize=` | Summaries. |
| `GET /posts/{id}` · `POST /posts` · `PUT /posts/{id}` | `{ slug?, title, excerpt?, bodyMarkdown, coverImageUrl?, coverImageAlt?, authorId? (team member), categoryIds[], tags[], relatedPostIds[], seo }`. Reading time is computed. |
| `DELETE /posts/{id}` | Writers may delete only their own drafts; publishers any post. |
| `POST /posts/{id}/submit` | Draft → InReview (`400 blog.incomplete` when title, body or excerpt are missing). |
| `POST /posts/{id}/publish` | `blog.publish`. Publishes now. |
| `POST /posts/{id}/schedule` | `blog.publish`. `{ publishAt, note?, concurrencyStamp }` (future UTC). A recurring job (every minute) publishes due posts with a conditional per-row update, so it is safe on several instances. |
| `POST /posts/{id}/unpublish` · `POST /posts/{id}/return-to-draft` | `blog.publish`. |
| `GET /categories` · `POST` · `PUT /categories/{id}` · `DELETE /categories/{id}` | `{ slug?, name, description? }` |
| `GET /authors` | Team members for the author picker (`{ id, name, role }`). |

Invalid moves answer `409 blog.invalid_transition`; a writer calling a publisher action gets `403`.

## Careers (`careers.manage`)

Base path `/api/v1/agency/website/careers`.

| Method & path | Notes |
|---|---|
| `GET /jobs` · `GET /jobs/{id}` · `POST /jobs` · `PUT /jobs/{id}` | `{ slug?, title, department, location, countryCode?, workplace: OnSite \| Hybrid \| Remote, employmentType, summary, descriptionMarkdown, requirements[], benefits[], salaryMin?, salaryMax?, salaryCurrency, salaryPeriod?, status: Draft \| Open \| Closed, closesAt? }` |
| `DELETE /jobs/{id}` | `409 careers.job_has_applications` — close the job instead. |
| `GET /applications?jobOpeningId=&stage=&search=&page=&pageSize=` | Summaries. |
| `GET /applications/{id}` | Detail with notes timeline and stage history. |
| `POST /applications/{id}/move` | `{ stage: New \| Screening \| Interview \| Offer \| Hired \| Rejected, note?, concurrencyStamp }` |
| `POST /applications/{id}/notes` | `{ body }` |
| `GET /applications/{id}/cv` | The PDF (`Content-Disposition: attachment`, `no-store`); every download is audited. |

## Inquiries, bookings, newsletter

Base path `/api/v1/agency/website`.

| Method & path | Permission | Notes |
|---|---|---|
| `GET /overview` | any website permission | Dashboard numbers: inquiries in the last 30 days vs the previous 30, new inquiries, upcoming consultations, confirmed/pending subscribers, new applications, published and in-review posts, and inquiries by type, source (UTM) and day. |
| `GET /inquiries?type=&status=&from=&to=&service=&utmSource=&search=&page=&pageSize=` | `site.manage` or `crm.view` | Inbox. `type`: `Contact \| Audit \| Quote \| Consultation`; `status`: `New \| InProgress \| Qualified \| Converted \| Closed \| Spam`. |
| `GET /inquiries/export.csv` (same filters) | `site.manage` or `crm.view` | CSV (formula-injection safe), audited. |
| `GET /inquiries/{id}` | `site.manage` or `crm.view` | Full payload, UTM, referrer, landing page, consent record. |
| `PUT /inquiries/{id}` | `site.manage` | `{ status, assignedToUserId?, staffNotes?, concurrencyStamp }` |
| `GET /bookings/settings` · `PUT /bookings/settings` | `site.manage` | `{ timeZone (IANA), slotMinutes 15–240, minNoticeHours, maxDaysAhead, weeklyAvailability: [{ day, start "09:00", end "17:00" }], isEnabled, concurrencyStamp }` |
| `POST /bookings/blackouts` · `DELETE /bookings/blackouts/{id}` | `site.manage` | `{ date, reason? }` (`409 website.blackout_exists`). |
| `GET /bookings?status=&from=&to=&page=&pageSize=` | `site.manage` | `status`: `Confirmed \| Cancelled \| Completed \| NoShow`. |
| `GET /bookings/slots?from=&days=` | `site.manage` | Free slots, for rescheduling. |
| `POST /bookings/{id}/cancel` | `site.manage` | `{ reason, notifyVisitor, concurrencyStamp }` — frees the slot. |
| `POST /bookings/{id}/reschedule` | `site.manage` | `{ slotStart, notifyVisitor, concurrencyStamp }` (`409 website.slot_taken`). |
| `POST /bookings/{id}/status` | `site.manage` | `{ status: Completed \| NoShow, concurrencyStamp }` |
| `GET /newsletter/subscribers?status=&search=&page=&pageSize=` | `site.manage` | `status`: `Pending \| Confirmed \| Unsubscribed`. |
| `GET /newsletter/subscribers/export.csv?status=` | `site.manage` | CSV of confirmed (or filtered) subscribers with consent version and timestamps; audited. |

## Images

`POST /api/v1/agency/website/images` — `site.manage`, `blog.write`, `blog.publish` or `careers.manage`. Multipart
`file` (JPEG/PNG/WebP/GIF, ≤ 10 MB, re-encoded and stripped of metadata by the file service). Returns
`201` with the stored file (`{ id, url, contentType, sizeBytes, width, height, … }`); the `url` (`/api/v1/files/{id}`) is public and accepted by
every image field. This exists so blog writers without `content.manage` can add cover and inline images.

## Domain events

Published through `IEventPublisher` after the transaction commits (types in `Domain/Events/AgencyEvents.cs`):

- `WebsiteInquiryReceived` — once per accepted contact, audit, quote or consultation booking (never for honeypot
  hits). Carries the inquiry id, type, contact details, message, service slugs, package ids, budget, timeline, UTM
  and landing page. CRM subscribes to create leads; the website module's own handler notifies `site.manage` staff.
- `NewsletterSubscribed` — once, when a subscriber confirms (double opt-in), not on signup.

## Error codes

`website.not_found` (404) · `website.slug_taken` (409) · `website.in_use`, `website.category_in_use` (409) ·
`website.invalid` (400, with `errors`) · `website.reorder_unknown`, `website.reorder_duplicates` (400) ·
`website.form_too_fast`, `website.form_expired`, `website.consent_required` (400) · `website.slot_taken` (409) ·
`website.booking_disabled` (400) · `website.booking_not_active` (409) · `website.blackout_exists` (409) ·
`website.newsletter_invalid_token` (400) · `blog.invalid`, `blog.incomplete` (400) · `blog.invalid_transition` (409) ·
`blog.publish_required` (403) · `careers.invalid` (400) · `careers.job_has_applications` (409) ·
`concurrency.conflict` (409) · `auth.forbidden` (403) · `website.redirect_not_found` (404) ·
`website.invalid_redirect` (400) · `website.redirect_exists`, `website.redirect_source_live`, `website.redirect_loop`,
`website.redirects_busy` (409).
