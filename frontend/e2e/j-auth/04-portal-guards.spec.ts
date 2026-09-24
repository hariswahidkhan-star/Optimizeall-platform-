import { expect, test } from '@playwright/test';
import type { Credentials } from '../journeys/support/fixtures';
import {
  FORBIDDEN,
  accounts,
  landing,
  raw,
  signedIn,
  statusOf,
  ApiSession,
  watchErrors,
} from './support/auth';

/**
 * Route guards for every portal: each role deep-links into a page of every other portal and gets the 403 page (never
 * a blank screen, a crash or a redirect loop), while its own page opens; the API refuses the same data with 403, and
 * signed-out visitors get 401 from the API and the sign-in page (with the way back) in the browser.
 */
const pages = {
  participant: { ui: '/app/earnings', api: '/me/earnings' },
  reviewer: { ui: '/review/queue', api: '/review/queue' },
  manager: { ui: '/manage/campaigns', api: '/admin/campaigns' },
  finance: { ui: '/finance/ledger', api: '/finance/ledger' },
  admin: { ui: '/admin/settings', api: '/admin/settings' },
  agency: { ui: '/agency/crm/deals', api: '/agency/crm/deals' },
  client: { ui: '/client', api: '/client/orgs' },
} as const;
type Area = keyof typeof pages;
const areas = Object.keys(pages) as Area[];

const roles: { name: string; user: Credentials; landing: RegExp; allowed: Area[] }[] = [
  { name: 'participant', user: accounts.participant, landing: landing.participant, allowed: ['participant'] },
  { name: 'reviewer', user: accounts.reviewer, landing: landing.reviewer, allowed: ['reviewer'] },
  { name: 'campaign manager', user: accounts.manager, landing: landing.manager, allowed: ['manager'] },
  { name: 'finance', user: accounts.finance, landing: landing.finance, allowed: ['finance'] },
  { name: 'agency account manager', user: accounts.am, landing: landing.agency, allowed: ['agency'] },
  { name: 'client user', user: accounts.nimbusApprover, landing: landing.client, allowed: ['client'] },
  // Admins hold every staff permission and the participant portal, but are no client's member.
  {
    name: 'admin',
    user: accounts.admin,
    landing: landing.admin,
    allowed: ['participant', 'reviewer', 'manager', 'finance', 'admin', 'agency'],
  },
];

for (const role of roles) {
  test(`${role.name}: other portals answer 403 in the browser and the API`, async ({ browser }) => {
    const { context, page } = await signedIn(browser, role.user, role.landing);
    const errors = watchErrors(page);
    const api = await ApiSession.login(role.user.email, role.user.password);
    for (const area of areas) {
      const { ui, api: endpoint } = pages[area];
      const allowed = role.allowed.includes(area);
      await page.goto(ui);
      const heading = page.getByRole('heading', { level: 1 }).first();
      await expect(heading, `${role.name} → ${ui}`).toBeVisible();
      if (allowed) {
        await expect(heading, `${role.name} → ${ui}`).not.toHaveText(FORBIDDEN);
      } else {
        await expect(heading, `${role.name} → ${ui}`).toHaveText(FORBIDDEN);
        await expect(page.getByText('403')).toBeVisible();
        errors.ignore(new RegExp(`HTTP 403 GET .*${endpoint.replace(/\//g, '\\/')}`));
      }
      await expect(page, `${role.name} stays on ${ui}`).toHaveURL(new RegExp(`${ui.replace(/\//g, '\\/')}$`));
      expect(await statusOf(api.get(endpoint)), `${role.name} → GET ${endpoint}`).toBe(allowed ? 200 : 403);
    }
    errors.expectClean(`${role.name} across portals`);
    await context.close();
  });
}

test('signed-out visitors get 401 from the API and the sign-in page (with the way back) in the browser', async ({
  page,
}) => {
  for (const area of areas) {
    const { ui, api } = pages[area];
    expect((await raw('GET', api)).status, `anonymous GET ${api}`).toBe(401);
    await page.goto(ui);
    await expect(page, `anonymous ${ui}`).toHaveURL(`/login?next=${encodeURIComponent(ui)}`);
    await expect(page.getByRole('button', { name: 'Sign in', exact: true })).toBeVisible();
  }
  // A garbage bearer token is refused too, not treated as anonymous-but-allowed.
  expect((await raw('GET', '/auth/me', { token: 'not.a.jwt' })).status).toBe(401);
});
