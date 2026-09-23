import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { json, mockFetch, problem } from '@/test/fetchMock';
import { axeViolations, renderWithApp } from '@/test/render';
import { ADS_PERMISSIONS, clients, staffSession } from '../social/test/fixtures';
import type { AdAccount, Budget, ImportPreview } from './api';
import { ImportWizardPage } from './ImportWizardPage';
import { PacingPage } from './PacingPages';

const zeroTotals = { spend: 0, impressions: 0, clicks: 0, conversions: 0, conversionValue: 0, reach: 0, videoViews: 0 };
const noKpis = { ctr: null, cpc: null, cpm: null, cpa: null, roas: null, conversionRate: null, frequency: null };

const account: AdAccount = {
  id: 'acc1',
  clientAccountId: 'c1',
  clientName: 'Nimbus Fitness',
  platform: 'GoogleAds',
  externalAccountId: '123-456-7890',
  name: 'Nimbus Search',
  currency: 'USD',
  timeZone: 'America/New_York',
  status: 'NotConnected',
  statusMessage: null,
  syncSupported: false,
  lastSyncedAt: null,
  lastSyncMessage: null,
  managerUserId: null,
  managerName: null,
  isActive: true,
  last30Days: zeroTotals,
  last30DaysKpis: noKpis,
  concurrencyStamp: 'a1',
};

const templates = [
  { id: 'google-ads', name: 'Google Ads report export', platform: 'GoogleAds', headerAliases: {}, sampleUrl: '/agency/ads/import/templates/google-ads/sample.csv' },
  { id: 'generic', name: 'Generic template', platform: null, headerAliases: {}, sampleUrl: '/agency/ads/import/templates/generic/sample.csv' },
];

function preview(overrides: Partial<ImportPreview> = {}): ImportPreview {
  return {
    headers: ['Day', 'Campaign', 'Cost', 'Clicks', 'Impr.', 'Conversions'],
    headerRow: 3,
    mapping: { date: 'Day', campaign: 'Campaign', spend: 'Cost', clicks: 'Clicks', impressions: 'Impr.' },
    targetFields: ['date', 'campaign', 'spend', 'clicks', 'impressions', 'conversions'],
    requiredFields: ['date', 'spend'],
    sample: [
      { rowNumber: 4, date: '2026-09-01', level: 'Campaign', entity: 'Brand', currency: 'USD', spend: 120.5, impressions: 1000, clicks: 40, conversions: 3, conversionValue: 0 },
    ],
    rowsTotal: 3,
    validRows: 2,
    existingRows: 0,
    fromDate: '2026-09-01',
    toDate: '2026-09-02',
    errors: ['Row 6: "abc" is not a number (Cost).'],
    warnings: [],
    ...overrides,
  };
}

describe('ImportWizardPage', () => {
  it('previews, lets the mapping be adjusted, and imports only after partial import is accepted', async () => {
    const user = userEvent.setup();
    const { calls } = mockFetch({
      'POST /auth/refresh': staffSession(ADS_PERMISSIONS, 'AdsSpecialist'),
      'GET /agency/ads/accounts': () => json(200, [account]),
      'GET /agency/ads/import/templates': () => json(200, templates),
      'POST /agency/ads/accounts/acc1/import/preview': (req) => {
        const body = req.body as { mapping?: Record<string, string> };
        return json(200, body.mapping?.conversions ? preview({ mapping: body.mapping }) : preview());
      },
      'POST /agency/ads/accounts/acc1/import': () =>
        json(200, { batchId: 'b1', rowsTotal: 3, rowsImported: 2, rowsUpdated: 0, rowsSkipped: 1, errors: [], fromDate: '2026-09-01', toDate: '2026-09-02', sourceLabel: 'Measured' }),
    });
    const { container } = renderWithApp(<ImportWizardPage />, { route: '/agency/ads/import?account=acc1', path: '/agency/ads/import' });

    await screen.findByRole('radio', { name: 'Google Ads report export' });
    const previewButton = screen.getByRole('button', { name: 'Preview' });
    expect(previewButton).toBeDisabled();
    await user.click(screen.getByLabelText(/or paste the CSV/));
    await user.paste('Day,Campaign,Cost\n2026-09-01,Brand,120.50');
    await user.click(previewButton);

    expect(await screen.findByText(/Header found on row 3\. 2 of 3 rows are valid/)).toBeInTheDocument();
    expect(screen.getByText('Row 6: "abc" is not a number (Cost).')).toBeInTheDocument();
    expect(screen.getByLabelText('spend (required)')).toHaveValue('Cost');

    // Map the conversions column and re-check.
    await user.selectOptions(screen.getByLabelText('conversions'), 'Conversions');
    await user.click(screen.getByRole('button', { name: 'Re-check with this mapping' }));
    await waitFor(() => {
      const last = calls.filter((c) => c.path.endsWith('/import/preview')).at(-1)!.body as { mapping?: Record<string, string> };
      expect(last.mapping?.conversions).toBe('Conversions');
    });

    const importButton = screen.getByRole('button', { name: 'Import 2 rows into Nimbus Search' });
    expect(importButton).toBeDisabled();
    await user.click(screen.getByRole('checkbox', { name: 'Import the valid rows and skip the others' }));
    expect(importButton).toBeEnabled();
    expect(await axeViolations(container)).toEqual([]);

    await user.click(importButton);
    expect(await screen.findByText(/2 new rows, 0 updated \(already imported\), 1 skipped/)).toBeInTheDocument();
    expect(screen.getByText(/Source label: Measured/)).toBeInTheDocument();
    const sent = calls.find((c) => c.path === '/agency/ads/accounts/acc1/import')!.body as { allowPartial: boolean; template: string; mapping: Record<string, string> };
    expect(sent).toMatchObject({ allowPartial: true, template: 'google-ads' });
    expect(sent.mapping.conversions).toBe('Conversions');
  });

  it('shows the per-row errors when the server rejects the import', async () => {
    const user = userEvent.setup();
    mockFetch({
      'POST /auth/refresh': staffSession(ADS_PERMISSIONS, 'AdsSpecialist'),
      'GET /agency/ads/accounts': () => json(200, [account]),
      'GET /agency/ads/import/templates': () => json(200, templates),
      'POST /agency/ads/accounts/acc1/import/preview': () => json(200, preview({ errors: [], validRows: 3 })),
      'POST /agency/ads/accounts/acc1/import': () =>
        problem(400, 'import.invalid', 'The file has rows that cannot be imported.', { errors: { rows: ['Row 5: missing date.'] } }),
    });
    renderWithApp(<ImportWizardPage />, { route: '/agency/ads/import?account=acc1', path: '/agency/ads/import' });
    await user.click(await screen.findByLabelText(/or paste the CSV/));
    await user.paste('Day,Cost\n2026-09-01,1');
    await user.click(screen.getByRole('button', { name: 'Preview' }));
    await user.click(await screen.findByRole('button', { name: 'Import 3 rows into Nimbus Search' }));
    expect(await screen.findByText('Nothing was imported')).toBeInTheDocument();
    expect(screen.getByText('Row 5: missing date.')).toBeInTheDocument();
  });
});

function budget(overrides: Omit<Partial<Budget>, 'pacing'> & { pacing?: Partial<Budget['pacing']> } = {}): Budget {
  const { pacing, ...rest } = overrides;
  return {
    id: 'b1',
    clientAccountId: 'c1',
    clientName: 'Nimbus Fitness',
    month: '2026-09-01',
    platform: 'GoogleAds',
    campaignId: null,
    campaignName: null,
    scopeLabel: 'Google Ads',
    amount: 9000,
    currency: 'USD',
    overPacingThreshold: 1.15,
    underPacingThreshold: 0.85,
    targetCpa: 40,
    targetRoas: null,
    notes: null,
    actualCpa: 52.5,
    actualRoas: null,
    conversions: 90,
    fxMissing: [],
    concurrencyStamp: 'b',
    ...rest,
    pacing: {
      budget: 9000,
      actualToDate: 6300,
      expectedToDate: 4500,
      pacingRatio: 1.4,
      projectedMonthEnd: 12600,
      projectedVsBudget: 1.4,
      daysElapsed: 15,
      daysInMonth: 30,
      dailyRunRate: 420,
      state: 'Over',
      ...pacing,
    },
  };
}

describe('PacingPage', () => {
  it('shows each budget with its pacing state, expected-to-date and projection', async () => {
    mockFetch({
      'POST /auth/refresh': staffSession(ADS_PERMISSIONS, 'AdsSpecialist'),
      'GET /agency/ads/clients': () => json(200, clients),
      'GET /agency/ads/pacing': () =>
        json(200, [
          budget(),
          budget({
            id: 'b2',
            clientName: 'Wanderly Travel',
            currency: 'GBP',
            amount: 3000,
            targetCpa: null,
            fxMissing: ['AED→GBP'],
            pacing: { budget: 3000, actualToDate: 1500, expectedToDate: 1500, pacingRatio: 1, projectedMonthEnd: 3000, state: 'OnTrack' },
          }),
        ]),
    });
    const { container } = renderWithApp(<PacingPage />, { route: '/agency/ads/pacing', path: '/agency/ads/pacing' });

    const board = await screen.findByRole('list', { name: 'Budgets' });
    const [nimbus, wanderly] = within(board).getAllByRole('article');
    expect(within(nimbus!).getByText('Over pacing')).toBeInTheDocument();
    expect(within(nimbus!).getByText('140%')).toBeInTheDocument();
    expect(within(nimbus!).getAllByText(/6,300/).length).toBeGreaterThan(0);
    expect(within(nimbus!).getByText('$4,500.00')).toBeInTheDocument();
    expect(within(nimbus!).getByText(/12,600/)).toBeInTheDocument();
    expect(within(nimbus!).getByText('15 of 30')).toBeInTheDocument();
    expect(within(nimbus!).getByText(/52\.50.*\(.*40\.00\)/)).toBeInTheDocument();

    expect(within(wanderly!).getByText('On track')).toBeInTheDocument();
    expect(within(wanderly!).getByText('100%')).toBeInTheDocument();
    expect(within(wanderly!).getByText('Missing exchange rate: AED→GBP')).toBeInTheDocument();

    expect(await axeViolations(container)).toEqual([]);
  });
});
