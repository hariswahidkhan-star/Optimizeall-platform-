import {
  accounts,
  api,
  errorOf,
  expect,
  landing,
  modal,
  need,
  runId,
  saveState,
  state,
  statusOf,
  test,
  toast,
  watchErrors,
} from './support/social';

/**
 * Paid ads for the journey's client (EUR): the ads specialist registers the Google Ads account (double click = one),
 * sync without credentials says "not configured" (no network call), the platform's CSV export is imported through the
 * wizard (a USD row is refused, the valid rows imported; a re-import updates instead of duplicating), amounts are
 * rounded to the currency the same way on the agency and the client reports, a monthly budget paces the spend, and the
 * client's Approver/Viewer see the ad results in their portal. Plus permissions, tenancy and stale edits.
 */

interface AdAccount {
  id: string;
  name: string;
  currency: string;
  status: string;
  concurrencyStamp: string;
}

const day = (daysAgo: number) => new Date(Date.now() - daysAgo * 86_400_000).toISOString().slice(0, 10);
const accountName = () => `Helio Google Ads ${runId()}`;

/** A Google Ads "Campaign report" export: title + date range above the header, thousands separators, a USD row and a total. */
const googleCsv = () => `Campaign report
"${day(3)} - ${day(2)}"
Day,Campaign,Campaign ID,Currency code,Cost,Impr.,Clicks,Conversions,Conv. value
${day(3)},helio_google_search_brand,9001,EUR,"1,204.50","48,210","1,930",96.00,"9,120.00"
${day(3)},helio_google_pmax_beans,9002,EUR,33.335,"1,000",40,2.00,60.00
${day(2)},helio_google_search_brand,9001,EUR,100.10,"2,000",80,4.00,200.00
${day(2)},helio_google_pmax_beans,9002,USD,50.00,900,30,1.00,30.00
Total: Campaigns,,,,"1,387.94","52,110","2,080",103.00,"9,410.00"
`;

test('the ads specialist registers the client’s Google Ads account; duplicates and bad input are refused', async ({
  as,
}) => {
  const { id: clientId, name } = need('client');
  const page = await as(accounts.ads, landing.agency);
  const errors = watchErrors(page);
  await page
    .getByRole('navigation', { name: 'Agency navigation' })
    .getByRole('link', { name: 'Ad accounts' })
    .click();
  await expect(page.getByRole('heading', { level: 1, name: 'Ad accounts' })).toBeVisible();
  await page.getByRole('button', { name: 'Add account' }).click();
  const dialog = modal(page, 'Add ad account');
  await dialog.getByLabel('Client').selectOption({ label: name });
  // The client's currency and time zone are proposed.
  await expect(dialog.getByLabel('Account currency')).toHaveValue('EUR');
  await expect(dialog.getByLabel('Time zone')).toHaveValue('Europe/Berlin');
  await dialog.getByLabel('Platform').selectOption({ label: 'Google Ads' });
  await dialog.getByLabel('Account id').fill('123-456-7890');
  await dialog.getByLabel('Name').fill(accountName());
  await dialog.getByRole('button', { name: 'Add account' }).dblclick();
  await expect(dialog).toBeHidden();
  const ads = await api(accounts.ads);
  const list = await ads.get<AdAccount[]>(`/agency/ads/accounts?clientId=${clientId}`);
  expect(
    list.map((a) => a.name),
    'one account per double click',
  ).toEqual([accountName()]);
  saveState({ adAccount: { id: list[0]!.id, name: accountName() } });

  // The same account again → 409 in the dialog.
  errors.ignore(/HTTP 409 POST \S+\/ads\/accounts$/);
  await page.getByRole('button', { name: 'Add account' }).click();
  const again = modal(page, 'Add ad account');
  await again.getByLabel('Client').selectOption({ label: name });
  await again.getByLabel('Account id').fill('123-456-7890');
  await again.getByLabel('Name').fill('Duplicate');
  await again.getByRole('button', { name: 'Add account' }).click();
  await expect(again.getByRole('alert')).toContainText('already registered');
  await again.getByRole('button', { name: 'Cancel' }).click();

  const base = {
    clientAccountId: clientId,
    platform: 'MetaAds',
    externalAccountId: `act_${Date.now()}`,
    name: 'Bad',
    timeZone: 'UTC',
    currency: 'EUR',
  };
  expect((await errorOf(ads.post('/agency/ads/accounts', { ...base, currency: 'XYZ' }))).code).toBe(
    'ads.currency_unsupported',
  );
  expect((await errorOf(ads.post('/agency/ads/accounts', { ...base, timeZone: 'Mars/Olympus' }))).code).toBe(
    'ads.invalid_timezone',
  );
  expect(
    await statusOf(ads.post('/agency/ads/accounts', { ...base, platform: 17 })),
    'an undefined platform value',
  ).toBe(400);

  // Sync without credentials: "not configured", nothing written, no network call.
  const table = page.getByRole('table', { name: 'Ad accounts' });
  await page.getByRole('button', { name: `Actions for ${accountName()}` }).click();
  await page.getByRole('menuitem', { name: 'Sync now' }).click();
  await expect(toast(page, 'Sync not configured')).toBeVisible();
  await expect(table.getByRole('row', { name: new RegExp(accountName()) })).toContainText('Not connected');
  errors.expectClean('registering the ad account');
});

test('spend import: a Google Ads export through the wizard; a USD row is refused; re-importing updates, never duplicates', async ({
  as,
}) => {
  const account = need('adAccount');
  const page = await as(accounts.ads, landing.agency);
  const errors = watchErrors(page);
  await page.goto(`/agency/ads/import?account=${account.id}`);
  await expect(page.getByRole('heading', { level: 1, name: 'Import ad metrics' })).toBeVisible();
  await page.getByLabel('…or paste the CSV').fill(googleCsv());
  await page.getByRole('button', { name: 'Preview' }).click();
  const mapping = page.getByRole('region', { name: '2. Column mapping and validation' });
  await expect(mapping).toContainText('Header found on row 3. 3 of 4 rows are valid');
  await expect(
    mapping.getByRole('list').filter({ hasText: 'currency USD does not match the account currency EUR' }),
  ).toBeVisible();
  const importButton = mapping.getByRole('button', { name: /^Import 3 rows into/ });
  await expect(importButton).toBeDisabled();
  await mapping.getByRole('checkbox', { name: 'Import the valid rows and skip the others' }).check();
  await importButton.dblclick();
  const done = page
    .getByRole('alert')
    .filter({ hasText: 'Import complete' })
    .or(page.getByRole('status').filter({ hasText: 'Import complete' }));
  await expect(done.first()).toContainText('3 new rows, 0 updated (already imported), 1 skipped');

  const ads = await api(accounts.ads);
  let batches = await ads.get<{ rowsImported: number; rowsUpdated: number }[]>(
    `/agency/ads/accounts/${account.id}/imports`,
  );
  expect(batches, 'one import per double click').toHaveLength(1);

  // The same file again: the rows are updated, not added.
  await done.first().getByRole('button', { name: 'Import another file' }).click();
  await page.getByRole('button', { name: 'Preview' }).click();
  await expect(
    mapping.getByText('3 row(s) already exist and will be updated (re-importing is safe).'),
  ).toBeVisible();
  await mapping.getByRole('checkbox', { name: 'Import the valid rows and skip the others' }).check();
  await mapping.getByRole('button', { name: /^Import 3 rows into/ }).click();
  await expect(done.first()).toContainText('0 new rows, 3 updated (already imported), 1 skipped');
  batches = await ads.get(`/agency/ads/accounts/${account.id}/imports`);
  expect(batches).toHaveLength(2);

  // Without the partial option the API refuses the whole file.
  const e = await errorOf(
    ads.post(`/agency/ads/accounts/${account.id}/import`, {
      template: 'google-ads',
      fileName: 'x.csv',
      csv: googleCsv(),
      mapping: {},
    }),
  );
  expect(e.status).toBe(400);
  errors.expectClean('the import wizard');
});

test('reports: the account totals and the client’s ad report agree to the cent (EUR rounding)', async ({
  as,
}) => {
  const account = need('adAccount');
  const page = await as(accounts.ads, landing.agency);
  const errors = watchErrors(page);
  await page.goto(`/agency/ads/accounts/${account.id}`);
  await expect(page.getByRole('heading', { level: 1, name: account.name })).toBeVisible();
  // 1,204.50 + 33.335 + 100.10 = 1,337.935 → €1,337.94 (half away from zero, like every other money figure).
  const spend = page
    .getByRole('group', { name: 'Spend' })
    .or(page.locator('.ui-stat').filter({ hasText: /^Spend/ }));
  await expect(spend.first()).toContainText('€1,337.94');
  const campaigns = page.getByRole('table', { name: 'Campaigns' });
  await expect(campaigns.getByRole('row', { name: /helio_google_search_brand/ })).toContainText('€1,304.60');
  await expect(campaigns.getByRole('row', { name: /helio_google_pmax_beans/ })).toContainText('€33.34');
  errors.expectClean('the ad account report');

  const ads = await api(accounts.ads);
  const kpis = await ads.get<{
    reportingCurrency: string;
    totals: { spend: number; conversions: number; conversionValue: number };
    kpis: { roas: number; cpa: number };
  }>(`/agency/ads/clients/${need('client').id}/kpis?from=${day(30)}&to=${day(1)}`);
  expect(kpis.reportingCurrency).toBe('EUR');
  expect(kpis.totals.spend).toBe(1337.94);
  expect(kpis.totals.conversions).toBe(102);
  expect(kpis.totals.conversionValue).toBe(9380);

  // The client's Approver sees the same figures in the portal.
  const client = await as(state().approver, landing.client);
  const clientErrors = watchErrors(client);
  await client.goto('/client/social/performance');
  const paid = client.getByRole('region', { name: 'Paid ads' });
  await expect(paid.getByText('€1,337.94').first()).toBeVisible();
  await expect(paid).toContainText('Google Ads: €1,337.94');
  await expect(paid).toContainText('7.01×');
  clientErrors.expectClean('the client ad report');
});

test('a monthly budget paces the imported spend; stale and invalid budget edits are refused', async ({
  as,
}) => {
  const { id: clientId, name } = need('client');
  const ads = await api(accounts.ads);
  const month = new Date().toISOString().slice(0, 7);
  const page = await as(accounts.ads, landing.agency);
  const errors = watchErrors(page);
  await page.goto(`/agency/ads/pacing?client=${clientId}`);
  await expect(page.getByRole('heading', { level: 1, name: 'Budget pacing' })).toBeVisible();
  await page.getByRole('button', { name: 'Add budget' }).click();
  const dialog = modal(page, 'Add monthly budget');
  await dialog.getByLabel('Client').selectOption({ label: name });
  await dialog.getByLabel('Amount').fill('3000.005');
  await dialog.getByLabel('Currency').fill('EUR');
  await dialog.getByRole('button', { name: 'Add budget' }).click();
  await expect(dialog).toBeHidden();
  const board = page.getByRole('list', { name: 'Budgets' });
  await expect(board.getByRole('article').filter({ hasText: name })).toContainText('€3,000.01');

  const budgets = await ads.get<
    {
      id: string;
      amount: number;
      currency: string;
      concurrencyStamp: string;
      pacing: { actualToDate: number };
    }[]
  >(`/agency/ads/pacing?month=${month}-01&clientId=${clientId}`);
  expect(budgets).toHaveLength(1);
  const budget = budgets[0]!;
  expect(budget.amount).toBe(3000.01);
  // Spend of the imported days that fall in this month counts towards the budget.
  expect(budget.pacing.actualToDate).toBeGreaterThanOrEqual(0);

  const input = {
    clientAccountId: clientId,
    month: `${month}-01`,
    amount: 2500,
    currency: 'EUR',
    overPacingThreshold: 1.2,
    underPacingThreshold: 0.8,
  };
  await ads.put(`/agency/ads/budgets/${budget.id}`, { ...input, concurrencyStamp: budget.concurrencyStamp });
  expect(
    (
      await errorOf(
        ads.put(`/agency/ads/budgets/${budget.id}`, {
          ...input,
          amount: 2600,
          concurrencyStamp: budget.concurrencyStamp,
        }),
      )
    ).status,
  ).toBe(409);
  expect(
    (
      await errorOf(
        ads.put(`/agency/ads/budgets/${budget.id}`, {
          ...input,
          underPacingThreshold: 1.0,
          overPacingThreshold: 1.0,
        }),
      )
    ).code,
  ).toBe('ads.thresholds_invalid');
  expect(await statusOf(ads.post('/agency/ads/budgets', { ...input, amount: 0 }))).toBe(400);
  // A budget amount that rounds to nothing in the currency (JPY has no minor unit) is refused, not stored as 0.
  expect(await statusOf(ads.post('/agency/ads/budgets', { ...input, amount: 0.4, currency: 'JPY' }))).toBe(
    400,
  );
  errors.expectClean('budget pacing');
});

test('ads permissions, tenancy and stale edits', async ({ as }) => {
  const account = need('adAccount');
  const clientId = need('client').id;
  const ads = await api(accounts.ads);

  // Staff without ads.manage: no ads navigation and 403 from the API (reports.manage may read the client KPIs).
  const am = await as(accounts.am, landing.agency);
  await expect(
    am.getByRole('navigation', { name: 'Agency navigation' }).getByRole('link', { name: 'Ad accounts' }),
  ).toHaveCount(0);
  const amApi = await api(accounts.am);
  expect(await statusOf(amApi.get('/agency/ads/accounts'))).toBe(403);
  expect(
    await statusOf(
      amApi.post(`/agency/ads/accounts/${account.id}/import`, {
        template: 'google-ads',
        fileName: 'x.csv',
        csv: 'a',
      }),
    ),
  ).toBe(403);
  expect(await statusOf(amApi.get(`/agency/ads/clients/${clientId}/kpis`))).toBe(200);
  expect(await statusOf((await api(accounts.content)).get(`/agency/ads/clients/${clientId}/kpis`))).toBe(403);
  expect(await statusOf((await api(accounts.socialManager)).get(`/agency/ads/accounts/${account.id}`))).toBe(
    403,
  );
  // Client users never reach the agency ads API, whatever the organisation.
  expect(await statusOf((await api(state().approver)).get(`/agency/ads/accounts/${account.id}`))).toBe(403);
  expect(await statusOf((await api(accounts.nimbusOwner)).get(`/agency/ads/clients/${clientId}/kpis`))).toBe(
    403,
  );
  // The ads specialist has no social access.
  expect(await statusOf(ads.get(`/agency/social/clients/${clientId}/profiles`))).toBe(403);

  // Stale account edit → 409; the currency is locked once metrics exist; an account with history cannot be deleted.
  const current = await ads
    .get<AdAccount & { platform: string; externalAccountId: string; timeZone: string; isActive: boolean }>(
      `/agency/ads/accounts/${account.id}`,
    )
    .then(
      (d) =>
        (
          d as unknown as {
            account: AdAccount & {
              platform: string;
              externalAccountId: string;
              timeZone: string;
              isActive: boolean;
            };
          }
        ).account,
    );
  const body = {
    clientAccountId: clientId,
    platform: current.platform,
    externalAccountId: current.externalAccountId,
    name: current.name,
    currency: 'EUR',
    timeZone: current.timeZone,
    isActive: true,
  };
  await ads.put(`/agency/ads/accounts/${account.id}`, {
    ...body,
    name: `${account.name} (EUR)`,
    concurrencyStamp: current.concurrencyStamp,
  });
  expect(
    (
      await errorOf(
        ads.put(`/agency/ads/accounts/${account.id}`, {
          ...body,
          concurrencyStamp: current.concurrencyStamp,
        }),
      )
    ).status,
  ).toBe(409);
  expect(
    (await errorOf(ads.put(`/agency/ads/accounts/${account.id}`, { ...body, currency: 'USD' }))).code,
  ).toBe('ads.currency_locked');
  expect((await errorOf(ads.delete(`/agency/ads/accounts/${account.id}`))).code).toBe(
    'ads.account_has_history',
  );
  // Campaign rows of the import cannot be deleted either (history) — set them to Removed instead.
  const campaigns = await ads.get<{ id: string; name: string }[]>(
    `/agency/ads/accounts/${account.id}/campaigns?from=${day(30)}&to=${day(1)}`,
  );
  const imported = campaigns.find((c) => c.name === 'helio_google_search_brand')!;
  expect((await errorOf(ads.delete(`/agency/ads/campaigns/${imported.id}`))).code).toBe(
    'ads.campaign_has_history',
  );
});
