import { api, campaign, expect, state, test } from './support/campaigns';

/**
 * Analytics: the overview dashboard keeps counted (funnel, posts, spend), estimated (reach) and measured (traffic,
 * conversions) figures in separate, labelled sections; the campaign dashboard for the journey's review campaign
 * shows exactly what the journey did (approved posts and spend within the budget).
 */
interface Section {
  key: string;
  measurement: string;
  metrics: { key: string; value: number | null; measurement: string }[];
}
type Overview = Record<string, Section> & { spendByCampaign: { campaignId: string; amount: number }[] };

test.describe.serial('analytics dashboards', () => {
  test('the overview separates counted, estimated and measured sections', async ({ as }) => {
    const overview = await (await api(state().manager)).get<Overview>('/analytics/overview');
    for (const [key, measurement] of [
      ['funnel', 'counted'],
      ['posts', 'counted'],
      ['spend', 'counted'],
      ['reach', 'estimated'],
      ['traffic', 'measured'],
      ['conversions', 'measured'],
    ] as const) {
      expect(overview[key]!.measurement, key).toBe(measurement);
      // A section never mixes kinds of figures.
      expect(
        overview[key]!.metrics.every((m) => m.measurement === measurement),
        key,
      ).toBe(true);
    }

    const page = await as(state().manager, /\/manage$/);
    await page.goto('/manage/analytics');
    await expect(page.getByRole('heading', { level: 1, name: 'Analytics' })).toBeVisible();
    const section = (title: RegExp) => page.getByRole('region', { name: title });
    await expect(section(/^Posts/)).toHaveAttribute('data-measurement', 'counted');
    await expect(section(/^Posts/)).toContainText('Counted from our records');
    await expect(section(/^Reach \(estimated\)/)).toHaveAttribute('data-measurement', 'estimated');
    await expect(section(/^Reach \(estimated\)/)).toContainText('Estimated — not measured');
    await expect(section(/^Traffic \(measured\)/)).toHaveAttribute('data-measurement', 'measured');
    await expect(section(/^Conversions/)).toHaveAttribute('data-measurement', 'measured');
  });

  test('the campaign dashboard reflects the journey: approved posts and spend within budget', async ({
    as,
  }) => {
    const review = campaign('review');
    const manager = await api(state().manager);
    const data = await manager.get<Overview>(`/analytics/campaigns/${review.id}`);
    const admin = await manager.get<{ spent: number; submissions: { approved: number } }>(
      `/admin/campaigns/${review.id}`,
    );
    const posts = Object.fromEntries(data.posts!.metrics.map((m) => [m.key, m.value]));
    expect(posts.postsApproved).toBe(admin.submissions.approved);
    const spend = data.spendByCampaign.find((s) => s.campaignId === review.id);
    expect(spend?.amount).toBe(admin.spent);
    expect(admin.spent).toBeLessThanOrEqual(10);

    const page = await as(state().manager, /\/manage$/);
    await page.goto(`/manage/analytics/campaigns/${review.id}`);
    await expect(page.getByRole('heading', { level: 1, name: review.title })).toBeVisible();
    await expect(page.getByRole('table', { name: 'Spend by campaign', exact: true })).toContainText('$10.00');
  });
});
