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
      devProxy.ts            vite/vite preview proxy rules (mirror nginx) + redirectGate plugin (301 for moved public addresses)
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
| Type | Inter Variable (self-hosted via `@fontsource-variable/inter`); `--text-display/h1/h2/h3/h4/body/small/xs` |
| Spacing | `--space-0…24` on a 4px base |
| Radii / shadows | `--radius-xs…2xl/full`, `--shadow-xs/sm/md/lg/focus` |
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
| `Stat` | `label`, `value`, `measurement` Measured·Estimated·Count, `delta{value,label,positiveIsGood}`, `icon`, `hint`, `loading`. A `role="group"` named by its label (query tiles with `getByRole('group', { name: /^Pending/ })`) |
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
| `BarChart` / `LineChart` / `Sparkline` | SVG, `title` + `description` (`<title>`/`<desc>`), hidden data table, hover tooltip; ≤3 series in fixed color order |

Browse everything at **`/design-system`** (dev server, or builds with `VITE_SHOW_DESIGN_SYSTEM=true`).

## API client & auth flow

`lib/api/client.ts` — `api.get/post/put/patch/delete<T>(path, body?, { query, headers, signal })`,
`api.upload<T>(path, FormData)`, `api.download(path, fallbackFileName)`. Paths are relative to `/api/v1`
(`api.get('/me/home')`). Every request sends `credentials: 'include'` and `X-Requested-With: fetch`.

* The access token is held **in memory only** (`tokenStore`). The refresh token is the backend's HttpOnly
  `oa_refresh` cookie.
* A 401 from any non-auth endpoint triggers a **single-flight** `POST /auth/refresh` (concurrent 401s share one
  promise), then the original request is retried once. If refresh fails the token is cleared and a
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
