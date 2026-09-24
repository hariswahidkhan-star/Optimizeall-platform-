import { expect, test } from '@playwright/test';
import { adminAt, runId, staff, watchErrors } from './support/edge';

/**
 * Lists at volume and search input in the browser: identical rows (same name, status and creation time) page through
 * without repeats or gaps, and wildcard/quote/backslash/emoji search terms match literally on both database providers.
 */
test.use({ locale: 'en-US' });

test('projects list: 60 identical projects page through exactly once, and wildcard search is literal', async ({
  page,
}) => {
  test.setTimeout(240_000);
  const errors = watchErrors(page);
  const am = await staff('am');
  const run = runId();
  const client = await am.post<{ id: string }>('/agency/clients', {
    name: `Edge lists ${run}`,
    countryCode: 'US',
    timeZone: 'America/New_York',
    currency: 'USD',
    industry: 'Testing',
    status: 'Active',
  });
  const project = (name: string) =>
    am.post<{ id: string }>('/agency/projects', {
      clientId: client.id,
      name,
      type: 'OneOffCampaign',
      status: 'Active',
    });
  const volume = 60;
  const ids = new Set<string>();
  for (let i = 0; i < volume; i++) ids.add((await project(`Volume ${run}`)).id);
  const special = [
    `${run} 100% growth`,
    `${run} 100 growth`,
    `${run} a_b`,
    `${run} axb`,
    `${run} O'Brien "Q"`,
    `${run} back\\slash`,
    `${run} café 😀`,
  ];
  for (const name of special) await project(name);

  await adminAt(page, '/agency/projects');
  const search = page.getByRole('searchbox', { name: 'Search projects' });
  const table = page.getByRole('table', { name: 'Projects' });
  const links = table.getByRole('link', { name: `Volume ${run}` });

  // Page through the tied rows with the pager, collecting each row's project id.
  await search.fill(`Volume ${run}`);
  await expect(page.getByText(`of ${volume}`, { exact: false })).toBeVisible();
  const seen: string[] = [];
  for (let pageNo = 1; pageNo <= Math.ceil(volume / 25); pageNo++) {
    if (pageNo > 1) await page.getByRole('button', { name: 'Next page' }).click();
    await expect(page.getByText(new RegExp(`Showing ${(pageNo - 1) * 25 + 1}–`))).toBeVisible();
    await expect(links).toHaveCount(Math.min(25, volume - (pageNo - 1) * 25));
    for (const href of await links.evaluateAll((els) => els.map((e) => e.getAttribute('href') ?? '')))
      seen.push(href.split('/').pop()!);
  }
  expect(new Set(seen).size, 'every project on exactly one page').toBe(volume);
  expect(seen.length).toBe(volume);
  expect([...ids].every((id) => seen.includes(id))).toBe(true);

  // Search terms are literal: % and _ are not wildcards; quotes, backslashes and emoji match as typed.
  const expectOnly = async (term: string, name: string) => {
    await search.fill(term);
    const rows = table.getByRole('link', { name: new RegExp(run) });
    await expect(rows).toHaveCount(1);
    await expect(rows).toHaveText(name);
  };
  await expectOnly(`${run} 100%`, `${run} 100% growth`);
  await expectOnly(`${run} a_b`, `${run} a_b`);
  await expectOnly(`O'Brien "Q`, `${run} O'Brien "Q"`);
  await expectOnly('back\\sl', `${run} back\\slash`);
  await expectOnly(`${run} café 😀`, `${run} café 😀`);
  await search.fill(`${run} %%`);
  await expect(page.getByText('No projects found')).toBeVisible();
  errors.expectClean('projects list');
});
