# Dynamic content: audit and outcome

Product requirement: *every piece of business content must be changeable without a deploy*. This page lists what was
hard-coded, where each kind of content now lives, how editors change it, and what deliberately stays in code.

## How content is made editable

| Mechanism | What it holds | Edited in | API | Storage |
|---|---|---|---|---|
| **Site settings** (existing) | Site name, tagline, header navigation (menu + mega-menu children), header CTA, footer blurb/columns/legal links, contact details (email, phone, WhatsApp, address, hours), social profiles, trust logos, announcement bar, SEO defaults, organization schema, analytics ids, home stats | Agency → Website → Site settings | `GET/PUT /agency/website/settings` | `website_settings` (JSON document, concurrency stamp) |
| **CMS entities** (existing) | Services, service lines, packages/pricing, industries, case studies, testimonials, team, blog posts/categories, careers, CMS pages (About, How we work, Pricing and Contact blocks, legal pages) | Agency → Website → … | `/agency/website/*` | `website_*` tables |
| **Page texts** (new) | Every headline, introduction, section title, button label, checklist, process step, FAQ teaser, empty-state and SEO title/description of the built-in pages (home, services, service detail, pricing, industries, case studies, blog, team, careers, contact, free audit, quote, booking, creators page, shared CTA band, footer newsletter, copyright line, cookie banner, 404) | Agency → Website → **Page texts** (`site.manage`) | `GET/PUT /agency/website/copy` | `content_copy_entries` (overrides only) |
| **Portal texts** (new) | Help-centre (FAQ page) header and empty state, creator-portal home headings and empty states | Admin → Content → **Portal texts** (`content.manage`) | `GET/PUT /admin/content/copy` | `content_copy_entries` |
| **Email templates** (new) | The layout around every notification email (greeting, button label, sign-off), the subject/text of each notification type, and the website's newsletter-confirmation and consultation (booked / cancelled / moved) emails | Admin → Content → **Email templates** (`content.manage`) | `GET /admin/email-templates`, `GET/PUT/DELETE /admin/email-templates/{key}`, `POST …/{key}/preview` | `email_template_overrides` (overrides only) |
| **Platform content** (existing) | Participant home banners, announcements, FAQ, onboarding checklist | Admin → Content | `/admin/content/*` | `content_*` tables |
| **Platform settings** (existing) | Eligibility, fraud, review, referral and retention rules | Admin → Settings | `/admin/settings` (+ new `POST /{key}/reset`) | `system_settings` |

### Page and portal texts

* The catalog of editable keys — group (page), label, type and **shipped default** — is one JSON document kept in two
  identical copies: `frontend/src/features/public/site/siteCopy.json` (the web app's fallback, so pages render
  immediately and offline) and `backend/src/OptimizeAll.Api/Modules/Content/Copy/site-copy.json` (embedded; the API
  validates keys against it and shows defaults in the editor). `SiteCopyCatalogTests` fails if the copies differ, and
  the frontend test `site/copy.test.tsx` fails if a page reads a key that is not in the catalog.
* Only **overrides** are stored. The public endpoint `GET /api/v1/content/copy` (anonymous, `public` rate limit) returns
  just the overridden keys; `useSiteCopy()` merges them over the defaults. The shipped defaults are exactly the texts
  the pages had before, so nothing changes visually until an editor saves something. Saving the default text (or
  "Reset to default") deletes the override, so a future improvement of the default wording reaches the site.
* Types: `text` (one line, ≤ 300), `textarea` (paragraph, ≤ 5,000), `list` (one item per line, ≤ 30 items) and
  `pairs` (`Title | Text` per line — process steps, rules, FAQ teaser). Some texts take placeholders filled by the
  page (`{year}`, `{name}`); unknown placeholders are rejected.
* Server-side hygiene: tags are stripped, control characters removed, line endings normalised; React renders the text
  as text (never HTML). Every change and reset is audited (`content.copy_updated`, `content.copy_reset`), and each key
  has its own concurrency stamp: a stale edit gets `409 concurrency.conflict` and the editor offers "Reload latest
  texts" instead of overwriting.
* Adding a text: add an entry to `siteCopy.json`, copy the file over `site-copy.json`, and read it in the page with
  `copy.text('group.key')` (or `list` / `pairs`).

### Email templates

* Templates are plain text with `{{variable}}` placeholders; the catalog (`EmailTemplateCatalog`) defines each
  template's variables (with samples for the preview) and which are required — e.g. the newsletter email must keep
  `{{confirmUrl}}` and `{{unsubscribeUrl}}`, the layout must keep `{{content}}`. Unknown variables are rejected.
* Notification emails are rendered as **layout(type template)**: the type template turns the platform's title and
  body into the email's subject and text; the layout adds the greeting, the button (`{{action}}`) and the sign-off.
  The notification-settings link is always appended by code, so no edit can remove it. The HTML part is generated
  from the text with every literal and value HTML-encoded (links only through the encoded button).
* The defaults reproduce the previous emails word for word (unit test `EmailTemplateTests`).
* The editor shows a live preview with sample values (the HTML version in a sandboxed iframe), inserts variables at
  the cursor, and resets to the default. Concurrency and audit work as for page texts
  (`content.email_template_updated`, `content.email_template_reset`).

### CMS pages: versioning and scheduling

* Every save of a CMS page (including the legal pages) stores an immutable revision (`website_page_revisions`) with
  its author, time and an optional change note. Pages that existed before get their original content recorded as
  version 0 the first time they are edited.
* The page editor lists the history; **Preview** shows an old version in the preview pane, **Restore** copies its
  content into a new version (publishing state and schedule stay as they are). API:
  `GET /agency/website/pages/{id}/revisions`, `GET …/revisions/{version}`, `POST …/revisions/{version}/restore`.
* A published page can have a **Go live at** time (`publishAt`); until then it answers 404 and is left out of the
  sitemap.

## Audit: what was hard-coded, and where it went

| Area | Hard-coded before | Now |
|---|---|---|
| Home page | Hero eyebrow/headline/lead/buttons/proof points, audit panel, all section eyebrows/titles/intros/links, the 4 process steps, creators section, newsletter heading | Page texts → *Home page* (45 keys; 229 keys in all) |
| Services pages | Hero, CTA band, service-detail section titles, buttons, "Results we move", "Tools and platforms", pricing note, CTA title (`{name}`) | Page texts → *Services pages* |
| Pricing | Hero, retainer/one-time toggle labels, empty state, CTA band | Page texts → *Pricing page* |
| Industries / case studies | Heroes, detail titles (`{name}`), filters' empty state, results note, challenge/strategy/execution headings, CTA titles | Page texts → *Industries and case studies* |
| Blog | Hero, empty state, newsletter title, related-articles title | Page texts → *Blog* |
| Team / careers | Heroes, "Want to join us?", open roles, empty state, job detail headings, application form title, CTA | Page texts → *Team and careers* |
| Contact / audit / quote / booking | Heroes, success messages, submit buttons, "What's included", "What happens next", "On the call" lists, empty slots message | Page texts → *Contact, audit, quote and booking* |
| Creators page (`/creators`) | Hero, trust points, payout card, 3 steps, 4 rules, FAQ teaser, CTA, browser title | Page texts → *Creators program page* |
| Shared chrome | CTA band defaults and buttons, "not found" message, footer newsletter title/text, copyright line, newsletter success title, cookie banner text and category descriptions, 404 page | Page texts → *Shared site sections* |
| Help centre (`/faq`) | Eyebrow, headline, introduction, empty state, browser title | Portal texts → *Help centre* |
| Creator portal home | Page introduction, verify-email and add-profile card titles, checklist title, recommendations/attention empty states, next-payout title and explanation | Portal texts → *Creator portal home* |
| SEO | Titles/descriptions of every built-in page | Page texts (`*.seo.title`, `*.seo.description`); CMS entities keep their own SEO panel |
| Navigation, footer, contact, social, announcement bar | (already dynamic) | Site settings |
| Pricing, services, FAQ answers, testimonials, team, legal pages, about | (already dynamic) | CMS entities / CMS pages (legal pages now versioned) |
| Emails | Notification email layout ("Hi …", "Open in Optimize All", "— Optimize All") and every notification's wording; newsletter confirmation; consultation booked/cancelled/moved | Email templates |
| Participant banners, announcements, FAQ, onboarding | (already dynamic) | Admin → Content |

## Deliberately kept in code

* **Structural UI labels**: form field labels, validation messages, button verbs of generic actions (Save, Cancel,
  Next, Back), table headers, ARIA labels, status names. They are interface, not business content.
* **Consent texts and their versions** (`Leads/FormGuard.cs`, `ConsentTexts`): each stored consent references a text
  version, so changing the wording must bump the version in code (legal traceability). The texts are already served by
  the API (`/public/site` → `consent`) and are not duplicated in the frontend.
* **Staff portals' operational copy** (reviewer workspace, finance, agency delivery): task instructions for trained
  staff, not marketing content.
* **Authentication emails** (verification, password reset, "someone tried to register") are sent by the Auth module,
  which is owned by another workstream; they can adopt `EmailTemplateService.RenderAsync` with new catalog keys the
  same way the website emails did.
* **Email sent by other modules through their own channels** (proposals, invoices, landing-page form autoresponders)
  already have their own per-record editable texts in those modules.
