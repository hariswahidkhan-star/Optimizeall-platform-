import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { json, mockFetch } from '@/test/fetchMock';
import { axeViolations, renderWithApp } from '@/test/render';
import type { Earning, EarningsSummary } from '../api/types';
import { authRoutes, makeSummary, paged } from '../test/fixtures';
import { EarningsPage } from './EarningsPage';

const reversal: Earning = {
  id: 'e2',
  createdAt: '2026-09-21T10:00:00Z',
  type: 'Reversal',
  status: 'Approved',
  description: 'Reversal of post reward',
  campaign: { id: 'c1', title: 'Autumn launch' },
  submissionId: 'sub1',
  originalAmount: -10,
  originalCurrency: 'EUR',
  exchangeRate: 1.1,
  settlementAmount: -11,
  settlementCurrency: 'USD',
  ruleSetVersion: 3,
  availableAt: '2026-09-21T10:00:00Z',
  paidAt: null,
  reason: 'Post was removed before 48 hours.',
};

function renderEarnings(summary: EarningsSummary) {
  const mock = mockFetch({
    ...authRoutes,
    'GET /me/earnings/summary': () => json(200, summary),
    'GET /me/earnings': () => json(200, paged([reversal])),
    'GET /me/submissions': () => json(200, paged([])),
  });
  return { ...renderWithApp(<EarningsPage />, { route: '/app/earnings', path: '/app/earnings' }), ...mock };
}

describe('EarningsPage', () => {
  it('renders every balance bucket with an explanation', async () => {
    const user = userEvent.setup();
    const { container } = renderEarnings(makeSummary());
    const buckets = await screen.findByRole('region', { name: 'Your balance' });
    for (const [label, amount] of [
      ['Pending', '$15.00'],
      ['Approved', '$12.50'],
      ['Scheduled for payment', '$3.00'],
      ['Paid', '$20.00'],
      ['Reversed', '$4.00'],
      ['Lifetime earned', '$35.50'],
    ]) {
      expect(within(buckets).getByRole('button', { name: `What is “${label}”?` })).toBeInTheDocument();
      expect(buckets).toHaveTextContent(amount!);
    }
    expect(buckets).toHaveTextContent('$5.00 on hold until its hold period ends');

    await user.hover(within(buckets).getByRole('button', { name: 'What is “Scheduled for payment”?' }));
    expect(await screen.findByRole('tooltip')).toHaveTextContent(/payout batch/);

    // Next payout
    expect(screen.getByRole('heading', { name: 'Next payout' })).toBeInTheDocument();
    expect(screen.getByRole('progressbar', { name: 'Progress to the payout minimum' })).toHaveAttribute(
      'aria-valuetext',
      '$7.50 of $10.00',
    );

    // Ledger row: original amount, rate, settlement, rule version, reason.
    const table = await screen.findByRole('table', { name: 'Earnings history' });
    expect(table).toHaveTextContent('€10.00');
    expect(table).toHaveTextContent('1.1');
    expect(table).toHaveTextContent('$11.00');
    expect(table).toHaveTextContent('v3');
    expect(table).toHaveTextContent('Reason: Post was removed before 48 hours.');
    expect(await axeViolations(container)).toEqual([]);
  });

  it('shows multi-currency breakdowns and the hold message', async () => {
    renderEarnings(
      makeSummary({
        activeHold: true,
        holdMessage: 'Your payouts are paused. Contact support.',
        pendingByCurrency: [{ currency: 'EUR', amount: 5, converted: false }],
        byCurrency: [
          { currency: 'USD', pendingApproval: 6, approved: 12.5, scheduled: 3, paid: 20, reversed: 4 },
          { currency: 'EUR', pendingApproval: 0, approved: 2, scheduled: 0, paid: 0, reversed: 0 },
        ],
      }),
    );
    expect(await screen.findByText('Your payouts are paused. Contact support.')).toBeInTheDocument();
    expect(screen.getByText(/not converted yet/)).toBeInTheDocument();
    expect(screen.getByRole('table', { name: 'Earnings by currency' })).toHaveTextContent('EUR');
  });
});
