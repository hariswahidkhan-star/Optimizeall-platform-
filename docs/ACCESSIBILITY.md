# Optimize All — Accessibility & responsive layout

Target: **WCAG 2.2 level AA** for every portal (participant, reviewer, campaign manager, finance, admin, agency staff,
client portal) and the public website, in the light **and** the dark theme, from 360 px phones to desktop.

How we know: an automated audit of every portal's representative pages at 360, 768 and 1280 px (axe-core with the
WCAG 2.0/2.1/2.2 A + AA rules, colour contrast checked in both themes, no horizontal page scroll), keyboard tests of
the shared shells, component tests with axe, and `eslint-plugin-jsx-a11y` on every file. Automated checks find roughly
a third to a half of real problems, so the patterns below are also reviewed by hand when a page is built.

## Conformance summary

| Area | Status | Where it lives |
|---|---|---|
| Colour contrast (1.4.3, 1.4.11) | Every text/background token pair ≥ 4.5:1 in both themes; the focus ring and form-control boundaries (`--color-border-control`: inputs, selects, checkboxes, radios, the switch track) ≥ 3:1 against every surface. Text contrast is audited per page in light and dark. | `src/styles/tokens.css` |
| Names, roles, labels (1.3.1, 4.1.2) | `FormField` wires `for`, `aria-describedby` (error first, then hint) and `aria-invalid` into any design-system control, including `<Input type="file">`. `IconButton` requires a label. | `components/ui/FormField.tsx`, `fieldContext.ts` |
| Landmarks & headings (1.3.1, 2.4.1, 2.4.6) | One `<main id="main">` per page, a "Skip to content" link first in every layout, one `<h1>` per page (`PageHeader`), headings in order (sections of a page are `h2`). | `PortalLayout`, `AuthLayout`, `SiteChrome` |
| Keyboard (2.1.1, 2.1.2, 2.4.3, 2.4.7) | Every control is a native button/link/input or a documented ARIA widget (tabs, menus, grid). A visible focus ring (`:focus-visible`, 2 px + halo) on every stop. Scrollable boxes without focusable content (wide tables, previews, carousels) become focusable while they overflow (`ScrollArea`). | `styles/base.css`, `components/ui/ScrollArea.tsx` |
| Dialogs, drawers, menus | Focus moves in, Tab/Shift+Tab are trapped, Escape closes, focus returns to the trigger; page scroll is locked; nested layers stack correctly. | `components/ui/useModalBehavior.ts`, `DropdownMenu.tsx` |
| Route changes (2.4.3, 4.1.3) | After a client-side navigation focus moves to `<main>` (portals and the public site) so the new page is announced; a fresh page load leaves focus alone, so the first Tab reaches the skip link. | `PortalLayout.tsx`, `SiteChrome.tsx` |
| Drag and drop (2.1.1, 2.5.7) | Every drag has a keyboard and single-pointer alternative: CRM pipeline (pick up with Enter/Space, arrows, Enter/Escape), delivery board (arrows move/reorder, a "Move to" select on phones), social calendar (Alt+arrows), `ReorderList` (handle + Move up/down buttons), `FileDrop` (a real file input). Moves are announced. | `crm/components/KanbanBoard.tsx`, `delivery/Kanban.tsx`, `social/CalendarPage.tsx`, `admin/shared/ReorderList.tsx` |
| Live announcements (4.1.3) | Toasts go to one polite live region that exists before the first toast; error toasts stay until dismissed (2.2.1), others pause on hover/focus. Async results (copy, moves, saves, filters, counts) use `role="status"`; an error shown inline after an action (`<Alert tone="danger">`, titled or not) is `role="alert"`. Ticking clocks and per-keystroke character counters are *not* live (they are read with the field instead). | `ToastProvider.tsx`, `TimerWidget.tsx`, `ComposerPage.tsx` |
| Forms & errors (3.3.1, 3.3.2, 3.3.3) | Required/optional marked in the label, inline errors associated with the control (`aria-describedby` + `aria-invalid`), server field errors mapped onto fields, form-level API errors in an `Alert` (`role="alert"` for the danger tone). | `FormField`, `features/auth/formErrors.ts` |
| Tables (1.3.1) | `DataTable` renders a real `<table>` with a caption, `scope`d headers and `aria-sort`; below 768 px it becomes a list of cards with the header as a label for every value. Tables that stay tabular on phones (documents, reports) scroll inside a focusable, named `ScrollArea`. | `components/ui/DataTable.tsx` |
| Charts (1.1.1, 1.4.1) | SVG charts have `<title>`/`<desc>`, a visually hidden data table, direct value labels, and never rely on colour alone. | `components/ui/Charts.tsx` |
| Motion (2.2.2, 2.3.3) | `prefers-reduced-motion` zeroes every duration token and animation/transition, turns smooth scrolling off and stops the testimonial carousel from auto-advancing (it also has a pause button). | `styles/tokens.css`, `styles/base.css`, `public/site/components.tsx` |
| Target size (2.5.8) | Buttons and icon buttons are ≥ 32 px (sm) / 40 px (md), nav rows 40 px, the phone bottom bar 64 px; axe's `target-size` rule runs on every audited page. | `Button.css`, `PortalLayout.css` |
| Reflow (1.4.10) | No horizontal page scroll at 360, 768 or 1280 px; long unbroken values (URLs, ids, e-mails) wrap; wide content scrolls inside its own container. | see "Responsive layout" |

## Responsive layout

Breakpoints: sm 640, md 768, lg 1024, xl 1280 (`tokens.css`, mirrored in `lib/hooks/useMediaQuery.ts`).

* **Portal shell** — the sidebar becomes a drawer below 1024 px (opened by "Open navigation", a focus-trapped dialog);
  the participant portal adds a bottom tab bar below 768 px.
* **Tables** — `DataTable` stacks rows into cards below 768 px. A table that must stay tabular (invoice/proposal lines,
  statements, reconciliation, CMS markdown tables, automation step results) sits in a `ScrollArea`, so it scrolls
  horizontally inside the page instead of widening it.
* **Page grids** — a page-level CSS grid uses `grid-template-columns: minmax(0, 1fr)` so a wide child (tabs, a board, a
  table) cannot blow the column out.
* **Horizontal scrollers** (tab lists, kanban boards, card strips) set `position: relative`, so visually hidden text
  inside them (absolutely positioned) cannot extend the page's scroll width.
* **Long values** — table cards, link buttons and field values use `overflow-wrap: anywhere`.

## Components to reach for

| Need | Use |
|---|---|
| A labelled form control with hint and error | `FormField` + `Input` / `Select` / `Textarea` / `Checkbox` / `Input type="file"` (never a raw `<input>` inside `FormField`: it would not get the id, description or `aria-invalid`) |
| A scroll container | `ScrollArea` with `label` or `labelledBy` |
| A modal, drawer, menu or tooltip | `Dialog` / `ConfirmDialog` / `Drawer` / `DropdownMenu` / `Tooltip` |
| An async result message | `useToast()`, or a `role="status"` element that exists before its text changes |
| A heading for an empty/error state that *is* the page | `EmptyState` / `ErrorState` with `headingLevel={1}` |
| A document embedded under a page heading | `InvoiceDocumentView` / `ProposalDocumentView` with `headingLevel={2}` |

## How to run the checks

Component tests (jsdom + axe; colour contrast and the `region` rule are off there because jsdom has no layout — the
Playwright audit covers them):

```bash
cd frontend
npm test                     # every vitest suite; helpers: axeViolations() in src/test/render.tsx
npx vitest run src/components/ui/a11y.test.tsx
npm run lint                 # includes eslint-plugin-jsx-a11y
```

The accessibility & responsive audit (Playwright, full stack, Demo seed, read-only):

```bash
# builds the API and the app, starts both on a fresh SQLite database with the Demo seed, runs the suite, tears down
E2E_SUITE=a11y E2E_DB_PROVIDER=sqlite scripts/e2e-journeys.sh

# against a stack that is already running (e.g. scripts/dev-start.sh or your own ports)
cd frontend
E2E_SUITE=a11y E2E_BASE_URL=http://localhost:5173 E2E_API_URL=http://localhost:5080 npx playwright test
E2E_SUITE=a11y … npx playwright test pages.spec.ts -g "client"      # one portal
```

`frontend/e2e/a11y/`:

* `pages.spec.ts` — for each portal page set (`support/a11y.ts → portalSets`) and each width (360 / 768 / 1280 px):
  signs in once, visits every page (detail pages are found by following the first matching link from their list),
  asserts no horizontal page scroll and no axe violations (WCAG 2.0/2.1/2.2 A + AA, including `target-size`), and
  repeats the contrast check in the dark theme at 360 and 1280 px. Failures are soft, so one run lists every problem.
  After the first page load it moves between pages with client-side navigations (as people do), which also keeps the
  run well below the API's per-IP limits on session refreshes and public endpoints.
* `keyboard.spec.ts` — skip links (public site and portals), a visible focus indicator on every stop of the portal
  chrome, menus (Escape, focus return), the phone navigation drawer (focus trap, Escape, focus restore, axe with the
  drawer open, 24 px target), three create dialogs (focus in, trap, Escape, restore, axe inside), the toast live
  region, and reduced motion (duration tokens are 0, opening a drawer runs no animation, no smooth scrolling).

Audited pages (about 125, each at three widths): public website (home, services, a service, industries, case studies,
a case study, pricing, team, careers, blog, a post, contact, quote, FAQ, creators, a client landing page, a public
campaign page, an invalid invoice link, sign-in, registration, password reset); participant (all 11 sections and a
campaign); reviewer (all 5 sections); campaign manager (overview, campaigns, a campaign, templates, calendar,
invitations, experiments, an experiment, analytics, achievements); finance (overview, payments, batches, a batch,
ledger, approvals, holds, FX rates, schedule); admin (overview, users, a user, roles, settings, content, support,
audit, jobs, analytics); agency — home, clients, a client, projects, a project board, tasks, deliverables, time,
reports, a report, CRM, deals, a deal, proposals, a proposal, contracts, a contract, billing, invoices, an invoice,
email (overview, campaigns, automations, an automation), social (calendar, composer, analytics, inbox), ads
(overview, alerts, pacing), SEO, landing pages and a page builder, forms and a form builder, integrations, website CMS (overview, inquiries, pages, a page editor, blog, careers, settings); client
portal (home, approvals, projects, reports, briefs, messages, brand kit, team, billing, an invoice, email, SEO, social).

To add a page, append it to the matching set in `e2e/a11y/support/a11y.ts` (a `{ path }`, or `detail(from, pattern,
name)` for a record page).

## Known exceptions

axe rules are never switched off globally. Exceptions are listed here and, when they need an axe exclusion, in
`knownExceptions` in `e2e/a11y/support/a11y.ts` (rule + selector + reason). There are currently **no** axe exclusions
in the page audit.

| What | Why it is accepted |
|---|---|
| jsdom component tests skip `color-contrast` and `region` | jsdom computes no layout or colours and renders components outside a page. Both rules run in the Playwright audit on real pages in both themes. |
| Content inside sandboxed preview iframes (email template/campaign previews, embedded video) | The HTML is authored by staff or clients (or comes from a third party) and is shown in `sandbox=""` iframes that scripts — including axe — cannot enter. The iframes themselves have titles; the source editors enforce alt text where the builder controls the markup. |
| CAPTCHA widgets on public forms | Third-party widgets (Turnstile / hCaptcha / reCAPTCHA); they provide their own accessible challenge flow. |
| Category colour in charts (amber series on white is < 3:1) | Charts never rely on colour: every series has direct value labels and a hidden data table (1.4.1, 1.4.11 are met through those). |
| Success/info toasts auto-dismiss after 5 s | The message is also reflected on the page (the saved record, the updated list); the timer pauses on hover and focus; error toasts never auto-dismiss (2.2.1). |
| Inline text links smaller than 24 px | Allowed by 2.5.8 (targets in a sentence or block of text are exempt). |
| Printed invoices/proposals | Print styles show the document only; it is the same accessible HTML document. |

## Findings from the platform-wide audit (September 2026)

Before the fixes, a crawl of 258 distinct pages across all portals (every list and detail page reachable from each
role's navigation, three levels deep) at 360/768/1280 px, with axe in the light theme and contrast again in the dark
theme, plus a keyboard pass (tab order, focus indicators, dialogs opened from "New …/Add …" buttons), found — and this
change fixed:

| Category | Before | After | Fix |
|---|---|---|---|
| Scrollable region not keyboard-focusable | 5 pages (data tables in payout reconciliation, experiment results and a contract; the CMS page preview; the participant achievements strip) | 0 | `ScrollArea` (+ `useScrollable`) in `DataTable` and wherever a box scrolls |
| Heading order (h1 → h3) | 6 pages (case studies, careers board, project board, client meetings, report editor, brand kit) | 0 | section headings raised to `h2` |
| Unlabelled controls | 7 file inputs (client messages and brand kit, delivery uploads, careers CV) | 0 | `<Input type="file">` inside `FormField` |
| Horizontal page scroll | 7 pages at 360 px, 2 at 768 px (website CMS blog/careers/settings/inquiries, proposal and client-invoice line tables, automation step results, ads alerts) | 0 | page grids `minmax(0, 1fr)`, `position: relative` scrollers, wrapped long values, `ScrollArea` tables |
| ARIA grid rows without cells | social calendar on phones | 0 | weekday headers/outside days hidden visually, not removed |
| Page has no/duplicate `h1` | public `/team` in development (the Vite proxy sent `/team` to the API), invalid invoice/proposal links (no `h1`), client invoice page (two `h1`) | 0 | Vite proxy matches `/api/`, `/t/`, `/e/` only; `headingLevel` on document and empty states |
| Focus after page load | in development (StrictMode) the portal moved focus into `<main>` on first load, skipping the skip link; the public site did not move focus after navigation at all | fixed | path-based focus management in `PortalLayout` and `SiteChrome` |
| Live-region noise | running timer ticked inside a live region (read every second); three character counters announced every keystroke | fixed | status text announces changes only |
| Error toasts | auto-dismissed after 8 s | stay until dismissed | `ToastProvider` |
| Inline error messages not announced (4.1.3) | 89 call sites render a failed action's message as an untitled `<Alert tone="danger">`, which had no role | announced (`role="alert"`) | `Alert` default role for the danger tone |
| Form-control boundaries (1.4.11, found in review — axe does not test it) | input/select/checkbox borders and the switch track at 1.5–1.8:1 | ≥ 3:1 in both themes | new `--color-border-control` token |

Colour contrast (light and dark), labels on other controls, dialog/drawer focus handling, touch-target size and
duplicate ids had no violations on any audited page.
