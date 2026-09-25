# Optimize All — Frontend

React 18 + TypeScript (strict) + Vite single-page app with five role portals. Server state lives in TanStack Query;
routing is React Router v6 (data router). There is no UI kit: the design system is bespoke CSS (custom properties +
co-located `.css` files) in `src/components/ui`.

```
frontend/
  index.html                 meta, favicons, OG image
  public/                    favicon.svg, favicon-32.png, apple-touch-icon.png, og-image.png
  src/
    main.tsx, App.tsx        global styles, providers, router
    app/
      router.tsx             route tree (public, auth, one branch per portal, /design-system)
      providers.tsx          QueryClient, Theme, Toasts (AuthProvider sits inside the router)
      guards.tsx             RequireAuth, RedirectIfAuthenticated (+ re-exports RequirePermission)
      RequirePermission.tsx  403 guard, importable from feature route files without cycles
      portals.ts             portal registry + landing/next-path rules (canOpenPath checks route requirements)
      portalTypes.ts         PortalDefinition, PortalNavItem, PortalModule, PortalRouteHandle
      appLinks.fixture.json  web paths the backend links to (AppLinks.cs); appLinks.test.ts resolves each one
      portalContext.ts       useCurrentPortal()
      redirects.ts           safeNextPath() — open-redirect defence for ?next=
      devProxy.ts            vite/vite preview proxy rules (mirror nginx; page documents go through ../seoShell.ts)
      layouts/               PublicLayout, AuthLayout, PortalLayout, EmailVerificationBanner
    components/
      ui/                    design system (see catalog below), `index.ts` barrel
      brand/Logo.tsx         LogoMark + Logo (mark | horizontal | stacked)
      FullPageLoader, PortalOverview, ThemeToggle, ProtectedImage
      ImageUpload            POST /admin/files uploader (campaign creatives, content images)
      SafeExternalLink       new-tab link for backend-provided URLs (renders text when unsafe)
    lib/
      api/                   client.ts, errors.ts, types.ts, query.ts
      auth/                  AuthProvider, useAuth, permissions, deviceId, sessionPaths
      safeHref.ts            isSafeHref / isInternalHref / isExternalHref (link safety for API-provided hrefs)
      format/                money.ts, dates.ts, text.ts, locale.ts
      hooks/                 useMediaQuery, useDebouncedValue, storage (safe localStorage)
      theme/                 ThemeProvider, useTheme
    features/
      public/                LandingPage, FaqPage, NotFound, ForbiddenPage, RouteErrorPage,
                             landing/ (JoinPage /join/:code, CampaignLandingPage /c/:slug)
      auth/                  Login, Register, CheckEmail, VerifyEmail, ForgotPassword, ResetPassword
      participant/ reviewer/ campaigns/ finance/ admin/   routes.tsx per portal (+ pages)
      design-system/         DesignSystemPage (dev/staging only)
    styles/                  tokens.css, base.css
    test/                    setup, fetch mock, render helpers
  e2e/
    smoke/                   Playwright smoke suite (API mocked with page.route)
    support/mockApi.ts       mock helpers
    journeys/                full-stack journeys (added later)
    a11y/                    accessibility & responsive audit of every portal (see docs/ACCESSIBILITY.md)
```

## Brand & design tokens

Brand: navy `#1F2659` (primary), amber `#FCB31E` (accent), ink `#1D174C` (headings), tagline
“Discover the world of solution”. The logo is recreated as SVG in `components/brand/Logo.tsx` (two interlocking
rings — navy upper-left under/over the larger amber ring); the source PNG is in `src/assets/brand/` and is also the
OG image. Favicons in `public/` are generated from the same mark.

`src/styles/tokens.css` defines every token once with `light-dark()`. The scheme follows the OS by default; the theme
toggle (persisted in `localStorage` key `oa.theme`) sets `<html data-theme="light|dark">`, which switches
`color-scheme` and with it every token.

| Group | Tokens |
|---|---|
| Brand scales | `--primary-50…950` (brand = 800), `--accent-50…950` (brand = 400, text-safe amber = 700 `#9A6200`) |
| Surfaces/text | `--color-bg`, `--color-surface(-2/-3)`, `--color-border(-strong)`, `--color-border-control` (form control boundaries, ≥ 3:1), `--color-heading`, `--color-text(-muted/-subtle)` |
| Interactive | `--color-primary(-hover/-active)`, `--color-on-primary`, `--color-primary-soft(-text)`, `--color-link`, `--color-accent*`, `--color-focus`, `--color-focus-halo` |
| Semantic | `--{success,warning,danger,info,neutral,brand}-{bg,fg,border}` (+ `-solid`) — all fg/bg pairs ≥ 4.5:1 |
| Charts | `--chart-1..3` (navy, amber, teal — validated for CVD separation in light and dark), `--chart-grid`, `--chart-axis` |
| Type | Inter Variable with the optical-size axis (self-hosted via `@fontsource-variable/inter/opsz.css`); `--text-display/h1/h2/h3/h4/body/small/xs/2xs`, `--text-kpi`, `--text-page-title`, `--tracking-tighter` |
| Spacing | `--space-0…24` on a 4px base |
| Radii / shadows | `--radius-xs…2xl/full` (4/6/8/10/12/16 px), `--shadow-xs/sm/md/lg/focus`, `--shadow-highlight` (dark-mode top edge) |
| Portal layout | `--portal-max` (1280), `--portal-topbar-height` (56), `--portal-sidebar-width` (248), `--portal-sidebar-collapsed` (64) |
| Layers / motion | `--z-*`, `--duration-*` (zeroed under `prefers-reduced-motion`), `--ease-*` |
| Breakpoints | sm 640, md 768, lg 1024, xl 1280 (mirrored in `lib/hooks/useMediaQuery.ts`) |

Rules: amber is for fills, highlights, focus halos and charts — text on amber is navy/ink, never white; amber-toned
text uses `--color-accent-text`. Primary buttons are navy with white text (light) / light navy with ink (dark);
the `highlight` button is amber with navy text. Use tabular numerals (`.tabular`, built into `Money`/`Stat`).

Utilities in `base.css`: `.visually-hidden`, `.skip-link`, `.container`, `.stack`, `.cluster`, `.grid-auto`,
`.eyebrow`, `.text-muted`, `.text-small`, `.ui-link`, `.tabular`.

## Component catalog (`@/components/ui`)

| Component | Key props / notes |
|---|---|
| `Button` | `variant` primary·highlight·secondary·ghost·danger·link, `size` sm·md·lg, `loading` (aria-busy, ignores clicks), `leadingIcon`, `trailingIcon`, `fullWidth` |
| `ButtonLink` | Router `Link` styled as a button (same variants) — use for navigation |
| `IconButton` | **required** `label` (aria-label), `icon`, `variant`, `size` |
| `FormField` | `label`, `hint`, `error` (string or list), `required`, `optional`, `labelAside`, `id`; wires `for`, `aria-describedby`, `aria-invalid` into the child control through context |
| `Input` / `PasswordInput` / `Textarea` / `Select` | Styled natives; `invalid`; Input `leading`/`trailing`; Select `options` (flat or grouped), `placeholder` |
| `Checkbox` | `label`, `description`, `indeterminate`, `invalid` |
| `Switch` | `checked`, `onCheckedChange`, `label`, `description` (role=switch) |
| `RadioGroup` | `legend`, `value`, `onChange`, `options`, `orientation`, `variant="cards"`, `error` |
| `Card`, `CardHeader`, `CardBody`, `CardFooter` | `as`, `flat`, `interactive` (+ a link with `ui-card__link` makes the whole card clickable) |
| `Badge` / `StatusBadge` | `tone`; StatusBadge `kind` submission·earning·payout·payoutItem·campaign·socialVerification·ticket + `status` (unknown values fall back to a humanized neutral badge), optional `label` override for audience-specific wording (tone stays shared). `statusOptions(kind)` feeds filters |
| `Tabs` | `tabs[{id,label,content,badge}]`, `label`, controlled `value`/`onValueChange`; roving tabindex |
| `Dialog` | `open`, `onClose`, `title`, `description`, `footer`, `size`, `initialFocusRef`, `dismissible`, `role`; portal, focus trap, Escape, focus restore, bottom sheet on phones |
| `ConfirmDialog` | `onConfirm({reason})` (may be async; shows busy + inline error), `tone`, `requireReason`, `confirmText` (type-to-confirm) |
| `Drawer` | `open`, `onClose`, `title`, `side`, `headerContent` |
| `DropdownMenu` | `trigger` (a button element), `items` (`{id,label,icon,to|onSelect,danger,disabled,current}` / separator / label), `align`, `placement` |
| `Tooltip` | `content`, single focusable child; hover + focus, Escape |
| Toasts | `useToast().success/error/info(title, description)`; polite live region, pause on hover/focus; errors stay until dismissed |
| `Alert` | `tone`, `title`, `actions`, `onDismiss`, `role`. The danger tone is always `role="alert"` (errors after an action are announced, titled or not); other tones with a `title` are `role="status"`, named by the title (`aria-labelledby`); untitled non-danger callouts have no role. Pass `role` to override |
| `Skeleton`, `SkeletonText`, `Spinner` | decorative placeholders; Spinner has role=status unless `decorative` |
| `EmptyState` / `ErrorState` | `icon`, `title`, `description`, `action`; ErrorState takes `error` and shows the ApiError title + trace id, `onRetry` |
| `Pagination` | `page`, `pageSize`, `total`, `onPageChange`, optional `onPageSizeChange` |
| `DataTable<T>` | `columns[{id,header,cell,sortable,sortValue,align,primary,hideOnMobile}]`, `rows`, `getRowId`, `caption`, `sort`/`onSortChange` (server) or local sort, `loading`, `emptyState`, `rowActions`, `selectable`/`selectedIds`/`onSelectionChange`/`bulkActions`, `maxHeight` (sticky header); stacked cards below md; a table wider than its container scrolls inside a focusable group named by the caption |
| `ScrollArea` | `label` or `labelledBy`; a scroll container (wide tables, previews, carousels) that becomes a focusable, named group only while its content overflows — use it for any `overflow: auto` box without focusable content |
| `FilterBar` | `search`/`onSearchChange` (debounced), `filters`, `values`, `onFilterChange`, `onReset`, `actions`; active filters as removable chips |
| `Stat` | `label`, `value`, `measurement` Measured·Estimated·Count, `delta{value,display,label,positiveIsGood,neutral}` (arrow + tint + spoken "Up/Down/No change"), `trend{values,label}` (sparkline), `icon`, `hint`, `loading`. A `role="group"` named by its label (query tiles with `getByRole('group', { name: /^Pending/ })`) |
| `StatGrid` | Grid of `Stat` tiles; `strip` joins them into one hairline-divided surface; `min` column width. Two per row on phones |
| `DashboardGrid` / `DashboardCell` | 12-column dashboard grid (2 columns at md, 1 on phones); `span` 3·4·5·6·7·8·12; the last card in a cell stretches so a row shares one height |
| `MeterList` | `label`, `items[{id,label,to,percent,valueText,meta}]`, `tone`; horizontal bars whose values are always printed as text |
| `ProgressBar` / `ProgressRing` | `value`, `max`, `label`, `valueText` |
| `Stepper` | `steps[{id,title,description,status,action}]`, `label` |
| `Avatar` | `name`, `src`, `size`, `decorative` |
| `PageHeader` | `title` (the page `<h1>`), `description`, `breadcrumbs`, `actions`, `meta`, `eyebrow` |
| `Timeline` | `items[{id,title,description,timestamp,actor,tone}]`, `label` |
| `FileDrop` | `label`, `value`, `onChange`, `accept` (default PNG/JPEG/WebP), `maxSizeBytes` (8 MB), `error`; drag & drop + real file input, preview |
| `Money` | `amount`, `currency`, `signDisplay`, `compact`, `colored` — formats only, never computes |
| `DateTime` | `value`, `format` datetime·date·relative·both, `timeZone` (defaults to the user’s profile zone), `withZone`; renders `<time dateTime>` |
| `CopyField` | `label`, `value`; clipboard with fallback + polite announcement |
| `KeyValueList` | `items[{label,value}]`, `layout` stacked·inline |
| `BarChart` / `LineChart` / `Sparkline` | SVG, `title` + `description` (`<title>`/`<desc>`), hidden data table, hover tooltip; ≤3 series in fixed color order; `LineChart area` fills a gradient under the first series |

Browse everything at **`/design-system`** (dev server, or builds with `VITE_SHOW_DESIGN_SYSTEM=true`).

## Dashboard design

Every portal home and the shared shell follow one set of rules — calm, spacious, data first (think Linear, Stripe,
Vercel). Screenshots of every dashboard before and after the redesign (desktop 1440 and phone 390, light and dark) are
in [`docs/screenshots/dashboards/`](screenshots/dashboards/).
Files are named `<portal>-<desktop|mobile>-<light|dark>-<before|after>.png` (full-page captures of the Demo seed;
the phone captures show the participant tab bar mid-page because it is fixed to the viewport):

| Dashboard | Desktop light | Desktop dark | Phone light | Phone dark |
|---|---|---|---|---|
| Participant | [before](screenshots/dashboards/participant-desktop-light-before.png) · [after](screenshots/dashboards/participant-desktop-light-after.png) | [before](screenshots/dashboards/participant-desktop-dark-before.png) · [after](screenshots/dashboards/participant-desktop-dark-after.png) | [before](screenshots/dashboards/participant-mobile-light-before.png) · [after](screenshots/dashboards/participant-mobile-light-after.png) | [before](screenshots/dashboards/participant-mobile-dark-before.png) · [after](screenshots/dashboards/participant-mobile-dark-after.png) |
| Reviewer | [before](screenshots/dashboards/reviewer-desktop-light-before.png) · [after](screenshots/dashboards/reviewer-desktop-light-after.png) | [before](screenshots/dashboards/reviewer-desktop-dark-before.png) · [after](screenshots/dashboards/reviewer-desktop-dark-after.png) | [before](screenshots/dashboards/reviewer-mobile-light-before.png) · [after](screenshots/dashboards/reviewer-mobile-light-after.png) | [before](screenshots/dashboards/reviewer-mobile-dark-before.png) · [after](screenshots/dashboards/reviewer-mobile-dark-after.png) |
| Campaign manager | [before](screenshots/dashboards/manager-desktop-light-before.png) · [after](screenshots/dashboards/manager-desktop-light-after.png) | [before](screenshots/dashboards/manager-desktop-dark-before.png) · [after](screenshots/dashboards/manager-desktop-dark-after.png) | [before](screenshots/dashboards/manager-mobile-light-before.png) · [after](screenshots/dashboards/manager-mobile-light-after.png) | [before](screenshots/dashboards/manager-mobile-dark-before.png) · [after](screenshots/dashboards/manager-mobile-dark-after.png) |
| Finance | [before](screenshots/dashboards/finance-desktop-light-before.png) · [after](screenshots/dashboards/finance-desktop-light-after.png) | [before](screenshots/dashboards/finance-desktop-dark-before.png) · [after](screenshots/dashboards/finance-desktop-dark-after.png) | [before](screenshots/dashboards/finance-mobile-light-before.png) · [after](screenshots/dashboards/finance-mobile-light-after.png) | [before](screenshots/dashboards/finance-mobile-dark-before.png) · [after](screenshots/dashboards/finance-mobile-dark-after.png) |
| Admin | [before](screenshots/dashboards/admin-desktop-light-before.png) · [after](screenshots/dashboards/admin-desktop-light-after.png) | [before](screenshots/dashboards/admin-desktop-dark-before.png) · [after](screenshots/dashboards/admin-desktop-dark-after.png) | [before](screenshots/dashboards/admin-mobile-light-before.png) · [after](screenshots/dashboards/admin-mobile-light-after.png) | [before](screenshots/dashboards/admin-mobile-dark-before.png) · [after](screenshots/dashboards/admin-mobile-dark-after.png) |
| Agency (account manager) | [before](screenshots/dashboards/agency-desktop-light-before.png) · [after](screenshots/dashboards/agency-desktop-light-after.png) | [before](screenshots/dashboards/agency-desktop-dark-before.png) · [after](screenshots/dashboards/agency-desktop-dark-after.png) | [before](screenshots/dashboards/agency-mobile-light-before.png) · [after](screenshots/dashboards/agency-mobile-light-after.png) | [before](screenshots/dashboards/agency-mobile-dark-before.png) · [after](screenshots/dashboards/agency-mobile-dark-after.png) |
| Client portal | [before](screenshots/dashboards/client-desktop-light-before.png) · [after](screenshots/dashboards/client-desktop-light-after.png) | [before](screenshots/dashboards/client-desktop-dark-before.png) · [after](screenshots/dashboards/client-desktop-dark-after.png) | [before](screenshots/dashboards/client-mobile-light-before.png) · [after](screenshots/dashboards/client-mobile-light-after.png) | [before](screenshots/dashboards/client-mobile-dark-before.png) · [after](screenshots/dashboards/client-mobile-dark-after.png) |

**Colour.** Neutrals are a cool slate with a whisper of the brand navy; dark mode is a near-black graphite (not a
saturated navy) whose surfaces get lighter with elevation (`bg < surface < surface-2 < surface-3`). Colour is
reserved for meaning: deltas (success/danger tint), status badges, the amber active-nav icon and chart series. Icons
in KPI tiles and quick links are neutral. Every text/background pair stays ≥ 4.5:1 in both themes (the a11y suite
runs axe in light and dark).

**Type.** Inter with its optical-size axis, so page titles and KPI figures get Inter Display's tighter shapes
automatically. Page titles use `--text-page-title` (24–28 px, semibold, `--tracking-tighter`); card titles are 15 px
semibold; labels 13 px medium muted; section labels in the sidebar 11 px uppercase. Figures are always tabular.

**Surfaces.** Borders separate, shadows only hint (`--shadow-xs` plus a faint top highlight in dark mode). Cards are
12 px radius, controls 8 px. Lists inside cards are hairline-divided rows that run edge to edge (`CardBody flush`)
instead of boxed items; row titles are quiet links that underline on hover.

**Shell.** The sidebar and top bar sit on the page background, divided from the content by a hairline. Nav items are
grouped under section labels (`app/navGroups.ts` — labels only name runs of consecutive items, the nav order never
changes), the active item is a raised white pill with an amber icon plus `aria-current`, and the sidebar folds into a
64 px icon rail on desktop (remembered in `localStorage` key `oa.sidebar`; link names stay in the accessibility tree
and show as tooltips). Below lg the same grouped nav opens in the drawer; participants keep the bottom tab bar, whose
active tab gets a pill behind the icon and a heavier label. The top bar holds the search field (left), then portal
switcher, theme, notifications and account.

**Page anatomy.** `PageHeader` (eyebrow, title, one-line description, primary action) → a KPI strip (`StatGrid
strip`) → the work: a `DashboardGrid` with the main list on the left (7–8 columns) and supporting panels on the right
(4–5) → secondary sections under a small `ui-dash-head` heading → quick links. Pages use `ui-dash` for vertical
rhythm (24 px, 32 px from lg).

**Data display.** KPI tiles show one figure, its measurement tag (Count · Measured · Estimated, never hidden), and
where the data exists a delta vs the previous period and a sparkline. The manager and admin homes fetch the analytics
overview for the previous 30 days and compare metric by metric (`lib/format/delta.ts`): counts and money as a relative
change (money only in the same currency), percentages in points (“+4.2 pts”); spend is neutral grey and costs are
lower-is-better. Sparklines come from the overview's daily timeseries. The direction is never colour alone: arrow,
tint and the words “Up/Down/No change” for screen readers.

**States.** Loading placeholders keep the final layout (a KPI-strip-sized block plus card-sized blocks) so nothing
shifts; empty states show an icon tile over soft concentric rings (an echo of the logo) with a title, one line and,
where useful, the next action; errors sit in a card with a retry.

**Per portal.**

| Portal | Home |
|---|---|
| Participant `/app` | Greeting + unread/support shortcuts, banners, earnings strip (pending · approved · paid · lifetime), submissions needing attention beside the next-payout panel, recommendations, achievements, announcements |
| Reviewer `/review` | “Start reviewing” action, workload strip (pending · under review · oldest · my decisions), secondary counts, quick links to each queue |
| Campaign manager `/manage` | Last-30-days strip with deltas and sparklines, active campaigns table, submissions area chart beside spend-by-campaign meters |
| Finance `/finance` | Strip (cutoff countdown · approvals · holds · batches awaiting payment), current period beside the schedule, latest batches beside reconciliation |
| Admin `/admin` | Last-30-days strip with deltas and sparklines, “Needs attention” tiles with footer links, quick links |
| Agency `/agency` | My work strip, tasks beside the timer, internal reviews beside client approvals, today's meetings, then Accounts (client health, overdue by client, utilization meters) and the agency snapshot for admins; registered area tiles last |
| Client `/client` | Onboarding, at-a-glance strip, approvals beside latest report and meetings, projects beside messages, NPS survey, recent deliverables beside the account team |

## API client & auth flow

`lib/api/client.ts` — `api.get/post/put/patch/delete<T>(path, body?, { query, headers, signal })`,
`api.upload<T>(path, FormData)`, `api.download(path, fallbackFileName)`. Paths are relative to `/api/v1`
(`api.get('/me/home')`). Every request sends `credentials: 'include'` and `X-Requested-With: fetch`.

* The access token is held **in memory only** (`tokenStore`). The refresh token is the backend's HttpOnly
  `oa_refresh` cookie.
* A 401 from any non-auth endpoint triggers a **single-flight** `POST /auth/refresh` (concurrent 401s share one
  promise), then the original request is retried once. A refresh answered `401 auth.refresh_race` (another tab
  rotated the cookie a moment earlier; the session is intact) is retried once after 250 ms. Signing out posts a
  `signed-out` message on the `optimizeall-auth` `BroadcastChannel` (`lib/auth/crossTab.ts`), and every other tab of
  the app leaves for `/login?signedOut=1` at once. If refresh fails the token is cleared and a
  `session-expired` event fires; `AuthProvider` then navigates to `/login?expired=1&next=<path>` and sets the
  transient `sessionExpired` flag, which `RequireAuth` uses for its own redirect in the same render (so a forced
  sign-out, e.g. after a suspension, always shows "Your session has expired"). The flag clears once `/login` is shown
  or on sign-in; ordinary anonymous visits redirect to `/login?next=<path>`.
* Errors are `ApiError { status, code, title, errors?, traceId? }` parsed from RFC 7807 (`code`, `traceId`, `errors`
  extensions). ASP.NET validation keys are normalized to camelCase (`Email` → `email`). Network failures are
  `status 0 / code "network_error"`.
* `lib/api/meta.ts` — `useSupportedCurrencies(current?)` (`GET /meta/currencies`; use it for every currency picker, never
  hard-code the list) and `useEligibilityDefaults()`. `lib/api/campaignOptions.ts` — `useCampaignOptions(search?)`
  (`GET /campaigns/options`: `campaigns.view`, `campaigns.manage`, `ledger.view` or `submissions.review`) for staff
  campaign filters/pickers.
* `lib/api/query.ts` — QueryClient defaults: never retry 4xx, up to 2 retries otherwise, 30 s stale time.

`AuthProvider` (inside the router) restores the session on load via a silent refresh (guards show a full-page
skeleton meanwhile), schedules a proactive refresh 60 s before `expiresAt`, and exposes
`status, user, permissions, hasPermission, hasAnyPermission, login, logout, register, refreshUser`.
Registration sends a random per-browser `deviceId` (localStorage `oa.deviceId`).

After sign-in the user goes to `next` when they may open it (validated by `safeNextPath` + portal/section
permissions), otherwise to the first portal whose landing requirement they meet: admin (`settings.manage` or
`content.manage`), finance, manage, review, participant. Access to a portal only needs its `requires` permission;
the stricter landing rule keeps e.g. reviewers (who have `users.view`) from landing in Admin.

Auth pages map server field errors onto fields (`features/auth/formErrors.ts#mapServerErrors`); codes without field
details are routed (`auth.terms_required` → terms checkbox, `auth.invalid_timezone` → time zone,
`auth.weak_password` → password). `features/auth/passwordPolicy.ts` mirrors the backend policy for instant feedback.

## Portals — adding a page

Each portal owns `src/features/<portal>/routes.tsx`, exporting:

```ts
export const nav: PortalNavItem[] = [
  { to: '', label: 'Overview', icon: LayoutDashboard },
  { to: 'ledger', label: 'Ledger', icon: BookOpenText, description: '…', requires: { anyOf: [Permissions.LedgerView] } },
];
export const routes: RouteObject[] = [
  { index: true, element: <PortalOverview /> },
  { path: 'ledger', element: <LedgerPage /> },
];
```

To add or replace a page:
1. Build the page in `features/<portal>/` (use `PageHeader` for the `<h1>`, React Query + `api` for data).
2. Point the route at it in `routes.tsx`. Nested routes are fine.
3. Declare the section's permission on the **route**: `handle: { requires }` (the same permission the API controller
   authorizes with). The router wraps every route with `handle.requires` — at any depth — in `RequirePermission`
   (403 page); put it on the parent of list + detail routes so detail pages are guarded like their list.
   `canOpenPath` (post-login `next`) checks the same requirements.
4. Add/adjust the `nav` item with the **same** `requires` (it hides the item). Mark up to four participant items
   `mobilePrimary` for the phone bottom bar (`shortLabel` for a compact label).
5. Each routes file exports `portalRequires`, the portal entry requirement: any permission that opens one of its
   sections (e.g. finance includes `rewards.approve_bonus` for Pending approvals and `payouts.hold` for Holds; admin
   includes `support.manage`, `jobs.view`, `analytics.view`, `campaigns.manage`). Where a section needs a permission
   on top of the portal's, write it as `allOf` (manager growth pages: `campaigns.manage` + `marketing.manage`).
   `app/portalRoutes.test.ts` walks every portal and fails when a route requirement is not covered by the portal entry,
   a nav item disagrees with its route, or a detail route is guarded differently from its list.
6. Paths are relative to the portal base (`/app`, `/review`, `/manage`, `/finance`, `/admin`).

`PortalOverview` builds a staff portal's landing page from its nav.

### Links, images and public pages

* Hrefs that come from the API go through `SafeExternalLink` / `isSafeHref` (`lib/safeHref.ts`): absolute
  `http(s)` URLs and single-`/` app paths only; anything else renders as text. Use `isInternalHref` to decide between
  a router link and a new-tab link (banner CTAs, onboarding actions, notification links).
* Backend notification/email links come from `AppLinks.cs`; `app/appLinks.fixture.json` lists their patterns and
  `app/appLinks.test.ts` asserts each resolves to a real route. When a route moves, update both.
* Images: offer `ImageUpload` (`components/ImageUpload.tsx`) next to every image URL field. External image URLs only
  work for hosts in the API's `Content:AllowedImageHosts` and the nginx `IMG_SRC_EXTRA` (CSP `img-src`).
* Public landing routes in `PublicLayout`: `/join/:code` (invitation → `/register?invite=…[&ref=…]`) and `/c/:slug`
  (public campaign → register, or `/app/campaigns/:slug` when signed in). They send `X-Visitor-Id` (localStorage
  `oa.visitorId`, try/catch with an in-memory fallback) for landing-page experiments and render a friendly page on
  404.

## Testing & quality

```bash
cd frontend
npm ci
npm run dev          # http://localhost:5173, proxies /api/, /t/ and /e/ to VITE_API_PROXY_TARGET (default http://127.0.0.1:5080)
npm run typecheck    # tsc -b
npm run lint         # eslint (typescript-eslint, react-hooks, jsx-a11y)
npm run format       # prettier
npm test             # vitest + Testing Library + axe-core (jsdom)
npm run build        # tsc -b && vite build → dist/
npm run e2e          # Playwright smoke suite (desktop-chromium + mobile-chromium/Pixel 7)
```

* Unit/component tests live next to the code (`*.test.ts(x)`). Helpers: `src/test/fetchMock.ts` (`mockFetch`,
  `problem`, `session`, `makeUser`), `src/test/render.tsx` (`renderWithApp` with providers + memory router,
  `axeViolations`), `src/test/viewport.ts` (`setViewportWidth` for the matchMedia mock).
* E2E: `E2E_SUITE` picks `e2e/<suite>` (default `smoke`); `e2e/<suite>/global-setup.ts` is used when present.
  Without `E2E_BASE_URL`, Playwright builds and serves the app with `vite preview` on :5173. The smoke suite mocks
  every `/api/v1/**` call with `page.route` (`e2e/support/mockApi.ts`); `e2e/smoke/landing.spec.ts` covers the
  public `/join/:code` and `/c/:slug` pages. Full-stack journeys go in
  `e2e/journeys/*.spec.ts` and run with `E2E_SUITE=journeys E2E_BASE_URL=…`.
* Accessibility: `E2E_SUITE=a11y E2E_DB_PROVIDER=sqlite scripts/e2e-journeys.sh` audits every portal's representative
  pages at 360/768/1280 px (axe WCAG 2.2 A/AA in the light and dark theme, no horizontal scroll) plus keyboard/focus
  behaviour. Conformance, known exceptions and the patterns to use are in [`docs/ACCESSIBILITY.md`](ACCESSIBILITY.md).
* `E2E_SUITE=j-admin` (run it with `E2E_SUITE=j-admin E2E_DB_PROVIDER=sqlite scripts/e2e-journeys.sh`) walks platform
  administration end to end on the Demo seed: user search/filters, suspend/reactivate and built-in role changes (the
  person's sessions end on their next request), custom roles from every permission area with their guardrails (client/
  staff mixing, a delegated role manager who can't escalate) and the portal/nav each role lands in, test users of every
  role via the sign-in page's test accounts panel, "log in as" (banner on portal and public pages, blocked actions, exit,
  audit), settings, portal texts, email templates, CMS page versions and scheduling, announcements, FAQ order, jobs,
  notification deliveries, Ctrl+K search scoping, and negatives (403s, stale stamps, double submits, boundary values).
* `E2E_SUITE=crawl` (run it with `E2E_SUITE=crawl E2E_DB_PROVIDER=sqlite scripts/e2e-journeys.sh`) signs in as every
  demo role and visits every nav link, in-page sub-nav link and tab of its portals, the first detail page of each list
  and each page's safe primary actions (opened, then cancelled — it never submits), then every public header/footer
  link and sitemap URL. It fails on console/page errors, 4xx/5xx API calls, error boundaries, not-found pages, pages
  without an h1 and actions that do nothing; `test-results/crawl/<role>.json` lists the pages, empty states and
  actions it saw. Set `E2E_CRAWL_DEBUG=<file>` to log each page and its timing.
* Playwright uses the preinstalled Chromium (`PLAYWRIGHT_BROWSERS_PATH`); `@playwright/test` is pinned to 1.56 to
  match it.
