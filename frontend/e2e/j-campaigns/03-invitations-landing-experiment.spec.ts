import {
  API_URL,
  api,
  campaign,
  campaignBody,
  expect,
  field,
  modal,
  refused,
  state,
  test,
} from './support/campaigns';

/**
 * Growth tools around the campaign: an invitation link created in the UI (visits counted, preview not counted), the
 * public campaign landing page an anonymous visitor sees (headline, reward teaser, disclosure, uploaded image), an
 * A/B landing-page experiment (sticky variant per visitor, both variants served) and the invitation refusals (ended
 * campaign, expiry in the past, deactivated link → friendly 404).
 */
test.describe.serial('invitation link, public landing page and A/B experiment', () => {
  let code = '';
  let experimentId = '';

  test('the manager creates a campaign invitation link with UTM tags', async ({ as }) => {
    const glow = campaign('glow');
    const page = await as(state().manager, /\/manage$/);
    await page.goto('/manage/invitations');
    await expect(page.getByRole('heading', { level: 1, name: 'Invitations & landing pages' })).toBeVisible();
    await page.getByRole('button', { name: 'New invitation' }).click();
    const dialog = modal(page, 'New invitation');
    await field(dialog, 'Name').fill(`Glow newsletter ${state().runId}`);
    await field(dialog, 'Campaign').selectOption({ label: `${glow.title} (Active)` });
    await field(dialog, 'UTM source').fill('newsletter');
    await field(dialog, 'UTM medium').fill('email');
    await dialog.getByRole('button', { name: 'Create invitation' }).click();

    const preview = modal(page, 'Landing page preview');
    await expect(preview).toBeVisible();
    await expect(preview).toContainText(`Glow up with Aurora ${state().runId}`);
    const link = await preview.getByLabel('Invitation link').inputValue();
    expect(link).toMatch(/\/join\/[A-Za-z0-9]{8}$/);
    code = link.split('/').pop()!;
  });

  test('an anonymous visitor opens the invitation and the public landing page', async ({ anonymous }) => {
    const glow = campaign('glow');
    const visitor = await anonymous();
    await visitor.goto(`/join/${code}`);
    await expect(
      visitor.getByRole('heading', { level: 1, name: `Glow up with Aurora ${state().runId}` }),
    ).toBeVisible();
    await expect(visitor.getByRole('link', { name: 'Accept invitation' })).toHaveAttribute(
      'href',
      new RegExp(`/register.*${code}`),
    );

    await visitor.goto(`/c/${glow.slug}`);
    const main = visitor.getByRole('main');
    await expect(
      main.getByRole('heading', { level: 1, name: `Glow up with Aurora ${state().runId}` }),
    ).toBeVisible();
    await expect(main).toContainText('Share your routine and get paid for every approved post.');
    await expect(main).toContainText('$6.00'); // the reward teaser follows the current rules (v2 base rate)
    await expect(main).toContainText('#ad');
    const gallery = main.getByRole('region', { name: 'Content you’ll share' });
    const image = gallery.locator('figure', { hasText: 'Hero bottle shot' }).locator('img'); // named by its caption
    await image.scrollIntoViewIfNeeded(); // loading="lazy"
    await expect
      .poll(() => image.evaluate((img: HTMLImageElement) => img.complete && img.naturalWidth))
      .toBe(400);

    // Visits are counted for the link; a staff preview is not.
    const manager = await api(state().manager);
    const list = await manager.get<{ items: { code: string; stats: { visits: number } }[] }>(
      `/marketing/invitations?campaignId=${glow.id}`,
    );
    expect(list.items.find((i) => i.code === code)!.stats.visits).toBe(1);
    await manager.get(`/public/invitations/${code}?preview=true`);
    const after = await manager.get<{ items: { code: string; stats: { visits: number } }[] }>(
      `/marketing/invitations?campaignId=${glow.id}`,
    );
    expect(after.items.find((i) => i.code === code)!.stats.visits).toBe(1);
  });

  test('the manager runs an A/B landing-page experiment; each visitor keeps their variant', async ({
    as,
    anonymous,
  }) => {
    const glow = campaign('glow');
    const page = await as(state().manager, /\/manage$/);
    await page.goto('/manage/experiments');
    await page.getByRole('button', { name: 'New experiment' }).click();
    const dialog = modal(page, 'New experiment');
    await field(dialog, 'Campaign').selectOption({ label: glow.title });
    await dialog.getByLabel(/^Name/).first().fill(`Glow headline test ${state().runId}`);
    await dialog.getByRole('radio', { name: /Landing page/ }).check();
    const variants = dialog.getByRole('list', { name: 'Variants' }).getByRole('listitem');
    await field(variants.nth(0), 'Landing headline').fill('Headline A: glow and earn');
    await field(variants.nth(1), 'Landing headline').fill('Headline B: get paid to glow');
    await dialog.getByRole('button', { name: 'Create draft' }).click();
    await expect(dialog).toHaveCount(0);

    const name = `Glow headline test ${state().runId}`;
    await page.getByRole('button', { name: `Actions for ${name}` }).click();
    await page.getByRole('menuitem', { name: 'Start…' }).click();
    await modal(page, 'Start this experiment?').getByRole('button', { name: 'Start experiment' }).click();
    await expect(page.getByRole('row', { name: new RegExp(name) })).toContainText('Running');

    const experiments = await (
      await api(state().manager)
    ).get<{ items: { id: string; name: string }[] }>(`/marketing/experiments?campaignId=${glow.id}`);
    experimentId = experiments.items.find((e) => e.name === name)!.id;

    // Anonymous visitors: each gets a sticky variant; across visitors both variants are served.
    const seen = new Set<string>();
    for (let i = 0; i < 12 && seen.size < 2; i++) {
      const visitor = await anonymous();
      await visitor.goto(`/c/${glow.slug}`);
      const h1 = visitor.getByRole('main').getByRole('heading', { level: 1 });
      await expect(h1).toHaveText(/^Headline [AB]:/);
      const first = await h1.textContent();
      await visitor.reload();
      await expect(h1).toHaveText(first!);
      seen.add(first!.slice(0, 10));
    }
    expect([...seen].sort()).toEqual(['Headline A', 'Headline B']);

    const results = await (
      await api(state().manager)
    ).get<{
      measurement: string;
      variants: { key: string; assigned: number }[];
    }>(`/marketing/experiments/${experimentId}/results`);
    expect(results.measurement).toBe('measured');
    expect(results.variants.every((v) => v.assigned > 0)).toBe(true);

    await page.goto(`/manage/experiments/${experimentId}`);
    await expect(page.getByRole('heading', { level: 1 })).toContainText(name);
    await expect(page.getByText('Not enough data for a reliable conclusion').first()).toBeVisible();
  });

  test('refusals: ended campaign, past expiry, deactivated link', async ({ anonymous }) => {
    const manager = await api(state().manager);
    const ended = await refused(
      manager.post('/marketing/invitations', { name: 'Ended link', campaignId: campaign('orbit').id }),
    );
    expect([ended.status, ended.code]).toEqual([400, 'invitation.campaign_closed']);
    // A link created while its campaign ran can still be renamed or switched off after the campaign ended.
    const running = await manager.post<{ id: string }>(
      '/admin/campaigns',
      campaignBody({ title: `Short run ${state().runId}` }),
    );
    await manager.post(`/admin/campaigns/${running.id}/publish`);
    const link = await manager.post<{ id: string }>('/marketing/invitations', {
      name: 'Short run link',
      campaignId: running.id,
    });
    await manager.post(`/admin/campaigns/${running.id}/end`, { reason: 'Short run is over' });
    const switchedOff = await manager.put<{ isActive: boolean; name: string }>(
      `/marketing/invitations/${link.id}`,
      {
        name: 'Short run link (closed)',
        campaignId: running.id,
        isActive: false,
      },
    );
    expect(switchedOff).toMatchObject({ isActive: false, name: 'Short run link (closed)' });
    // …but it can't be re-activated for the ended campaign.
    const reactivate = await refused(
      manager.put(`/marketing/invitations/${link.id}`, {
        name: 'Short run link',
        campaignId: running.id,
        isActive: true,
      }),
    );
    expect([reactivate.status, reactivate.code]).toEqual([400, 'invitation.campaign_closed']);

    const past = await refused(
      manager.post('/marketing/invitations', {
        name: 'Past link',
        campaignId: campaign('glow').id,
        expiresAt: new Date(Date.now() - 60_000).toISOString(),
      }),
    );
    expect([past.status, past.code]).toEqual([400, 'invitation.expiry_in_past']);

    const invitations = await manager.get<{ items: { id: string; code: string }[] }>(
      `/marketing/invitations?campaignId=${campaign('glow').id}`,
    );
    const id = invitations.items.find((i) => i.code === code)!.id;
    const removed = await manager.delete<{ deleted: boolean; deactivated: boolean }>(
      `/marketing/invitations/${id}`,
    );
    expect(removed).toEqual({ deleted: false, deactivated: true }); // it was visited, so it keeps its stats

    expect((await fetch(`${API_URL}/api/v1/public/invitations/${code}`)).status).toBe(404);
    const visitor = await anonymous();
    await visitor.goto(`/join/${code}`);
    await expect(
      visitor.getByRole('heading', { name: 'This invitation is no longer available' }),
    ).toBeVisible();
  });
});
