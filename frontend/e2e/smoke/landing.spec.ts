import { expect, test, type Route } from '@playwright/test';
import { authResponse, hasHorizontalScroll, mockApi, participant, problem } from '../support/mockApi';

const ok = (body: unknown) => (route: Route) =>
  route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) });

const invitation = {
  code: 'Ab12Cd34',
  type: 'campaign',
  headline: 'Share the Nimbus Fitness launch',
  body: 'Creators earn a fixed reward for every approved post.',
  heroImageUrl: null,
  campaign: {
    slug: 'nimbus-launch',
    title: 'Nimbus Fitness launch',
    summary: 'Launch campaign',
    platforms: ['Instagram', 'TikTok'],
    reward: { currency: 'USD', baseAmount: 8.5 },
    startsAt: '2026-09-01T00:00:00Z',
    endsAt: '2026-10-31T00:00:00Z',
    category: { name: 'Fitness', slug: 'fitness' },
  },
  utm: { source: null, medium: null, campaign: null },
  experiment: null,
};

const campaign = {
  slug: 'nimbus-launch',
  title: 'Nimbus Fitness launch',
  summary: 'Launch campaign',
  headline: 'Get paid to share the Nimbus launch',
  body: 'Join creators sharing the launch.',
  heroImageUrl: null,
  platforms: ['Instagram'],
  reward: { currency: 'USD', baseAmount: 6 },
  startsAt: '2026-09-01T00:00:00Z',
  endsAt: '2026-10-31T00:00:00Z',
  submissionDeadline: '2026-11-03T00:00:00Z',
  category: null,
  assets: [],
  disclosure: 'Paid partnership with Nimbus #ad',
  experiment: null,
};

test.describe('public landing pages', () => {
  test('invitation link (/join/:code) renders the invitation and leads to registration', async ({ page }) => {
    await mockApi(page, { 'GET /public/invitations/Ab12Cd34': ok(invitation) });
    await page.goto('/join/Ab12Cd34?ref=FRIEND42');
    await expect(
      page.getByRole('heading', { level: 1, name: 'Share the Nimbus Fitness launch' }),
    ).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Paid posts are always disclosed' })).toBeVisible();

    await page.getByRole('link', { name: 'Accept invitation' }).click();
    await expect(page).toHaveURL(/\/register\?invite=Ab12Cd34&ref=FRIEND42$/);
    await expect(page.getByText('You were invited')).toBeVisible();
  });

  test('expired invitation shows a friendly page', async ({ page }) => {
    await mockApi(page, {
      'GET /public/invitations/Gone1234': (route) =>
        route.fulfill(problem(404, 'not_found', 'Invitation was not found.')),
    });
    await page.goto('/join/Gone1234');
    await expect(page.getByRole('heading', { name: 'This invitation is no longer available' })).toBeVisible();
  });

  test('campaign page (/c/:slug) sends a visitor id and lets anonymous visitors join', async ({ page }) => {
    const visitorIds: string[] = [];
    await mockApi(page, {
      'GET /public/campaigns/nimbus-launch': (route) => {
        visitorIds.push(route.request().headers()['x-visitor-id'] ?? '');
        return ok(campaign)(route);
      },
    });
    await page.goto('/c/nimbus-launch');
    await expect(
      page.getByRole('heading', { level: 1, name: 'Get paid to share the Nimbus launch' }),
    ).toBeVisible();
    await expect(page.getByText('Paid partnership with Nimbus #ad')).toBeVisible();
    await expect(page.getByRole('link', { name: 'Join to take part' })).toHaveAttribute('href', '/register');

    await page.reload();
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
    expect(visitorIds.length).toBeGreaterThanOrEqual(2);
    expect(visitorIds[0]).toBeTruthy();
    expect(new Set(visitorIds).size).toBe(1); // stable per browser
  });

  test('signed-in visitors go from the campaign page to the campaign in their portal', async ({ page }) => {
    await mockApi(page, {
      'POST /auth/refresh': (route) => route.fulfill(authResponse(participant)),
      'GET /public/campaigns/nimbus-launch': ok(campaign),
    });
    await page.goto('/c/nimbus-launch');
    await expect(page.getByRole('link', { name: 'Open the campaign' })).toHaveAttribute(
      'href',
      '/app/campaigns/nimbus-launch',
    );
  });

  test('landing pages have no horizontal scroll at 360px', async ({ page }) => {
    await page.setViewportSize({ width: 360, height: 780 });
    await mockApi(page, {
      'GET /public/invitations/Ab12Cd34': ok(invitation),
      'GET /public/campaigns/nimbus-launch': ok(campaign),
    });
    for (const path of ['/join/Ab12Cd34', '/c/nimbus-launch']) {
      await page.goto(path);
      await expect(page.locator('h1').first()).toBeVisible();
      expect(await hasHorizontalScroll(page), `horizontal scroll on ${path}`).toBe(false);
    }
  });
});
