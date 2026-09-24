import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { json, mockFetch, problem } from '@/test/fetchMock';
import { axeViolations, renderWithApp } from '@/test/render';
import { ADS_PERMISSIONS, clients, staffSession } from '../social/test/fixtures';
import type { AdAccount, Budget, CampaignRow } from './api';
import { BudgetDialog } from './PacingPages';
import { AccountEditDialog, CampaignDialog } from './StructureDialogs';

const zeroTotals = {
  spend: 0,
  impressions: 0,
  clicks: 0,
  conversions: 0,
  conversionValue: 0,
  reach: 0,
  videoViews: 0,
};
const noKpis = {
  ctr: null,
  cpc: null,
  cpm: null,
  cpa: null,
  roas: null,
  conversionRate: null,
  frequency: null,
};

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

const campaign: CampaignRow = {
  id: 'cmp1',
  adAccountId: 'acc1',
  externalId: null,
  name: 'Brand',
  objective: 'conversions',
  status: 'Draft',
  budgetType: 'Daily',
  budgetAmount: 50,
  currency: 'USD',
  bidStrategy: null,
  startDate: null,
  endDate: null,
  targetingSummary: null,
  targetCpa: null,
  targetRoas: null,
  source: 'Plan',
  namingCompliant: true,
  totals: zeroTotals,
  kpis: noKpis,
  spendSparkline: [],
  sourceLabel: 'Manual',
  concurrencyStamp: 'c-st',
};

describe('ads edit dialogs', () => {
  it('deactivates an account, sending its immutable client and platform with the stamp', async () => {
    const user = userEvent.setup();
    const { calls } = mockFetch({
      'PUT /agency/ads/accounts/acc1': () => json(200, { ...account, isActive: false }),
    });
    const { container } = renderWithApp(<AccountEditDialog account={account} onClose={() => {}} />, {
      withAuth: false,
    });
    const dialog = await screen.findByRole('dialog', { name: 'Edit ad account' });
    await user.click(within(dialog).getByRole('checkbox', { name: /Active/ }));
    expect(await axeViolations(container)).toEqual([]);
    await user.click(within(dialog).getByRole('button', { name: 'Save' }));
    await waitFor(() =>
      expect(calls.find((c) => c.method === 'PUT')?.body).toMatchObject({
        clientAccountId: 'c1',
        platform: 'GoogleAds',
        isActive: false,
        concurrencyStamp: 'a1',
      }),
    );
  });

  it('edits a campaign and surfaces naming-convention errors from the API', async () => {
    const user = userEvent.setup();
    mockFetch({
      'PUT /agency/ads/campaigns/cmp1': () =>
        problem(400, 'ads.naming_convention', 'Name does not follow the template.', {
          errors: { name: ['Missing the {client} token.'] },
        }),
    });
    const { container } = renderWithApp(
      <CampaignDialog accountId="acc1" campaign={campaign} onClose={() => {}} />,
      { withAuth: false },
    );
    const dialog = await screen.findByRole('dialog', { name: 'Edit campaign' });
    const name = within(dialog).getByLabelText(/^Name/);
    await user.clear(name);
    await user.type(name, 'brand search');
    await user.click(within(dialog).getByRole('button', { name: 'Save' }));
    expect(await within(dialog).findByText('Missing the {client} token.')).toBeInTheDocument();
    expect(await axeViolations(container)).toEqual([]);
  });

  it('edits a budget with PUT and keeps its client locked', async () => {
    const user = userEvent.setup();
    const budget: Budget = {
      id: 'b1',
      clientAccountId: 'c1',
      clientName: 'Nimbus Fitness',
      month: '2026-09-01',
      platform: null,
      campaignId: null,
      campaignName: null,
      scopeLabel: 'All platforms',
      amount: 5000,
      currency: 'USD',
      overPacingThreshold: 1.15,
      underPacingThreshold: 0.85,
      targetCpa: null,
      targetRoas: null,
      notes: null,
      pacing: {
        budget: 5000,
        actualToDate: 0,
        expectedToDate: 0,
        pacingRatio: null,
        projectedMonthEnd: 0,
        projectedVsBudget: null,
        daysElapsed: 0,
        daysInMonth: 30,
        dailyRunRate: 0,
        state: 'NotStarted',
      },
      actualCpa: null,
      actualRoas: null,
      conversions: 0,
      fxMissing: [],
      concurrencyStamp: 'b-st',
    };
    const { calls } = mockFetch({
      'POST /auth/refresh': staffSession(ADS_PERMISSIONS, 'AdsSpecialist'),
      'GET /agency/ads/clients': () => json(200, clients),
      'PUT /agency/ads/budgets/b1': () => json(200, budget),
    });
    renderWithApp(<BudgetDialog budget={budget} month="2026-09" onClose={() => {}} />);
    const dialog = await screen.findByRole('dialog', { name: 'Edit budget' });
    expect(within(dialog).getByLabelText(/Client/)).toBeDisabled();
    const amount = within(dialog).getByLabelText(/Amount/);
    await user.clear(amount);
    await user.type(amount, '6000');
    await user.click(within(dialog).getByRole('button', { name: 'Save' }));
    await waitFor(() =>
      expect(calls.find((c) => c.method === 'PUT')?.body).toMatchObject({
        amount: 6000,
        month: '2026-09-01',
        concurrencyStamp: 'b-st',
      }),
    );
  });
});
