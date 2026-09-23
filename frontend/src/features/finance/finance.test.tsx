import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { json, mockFetch, problem } from '@/test/fetchMock';
import { axeViolations, renderWithApp } from '@/test/render';
import { setViewportWidth } from '@/test/viewport';
import { financeErrorMessage } from './api/errors';
import { ApiError } from '@/lib/api/errors';
import { BatchReviewPage } from './batch/BatchReviewPage';
import { parsePaymentCsv } from './batch/BulkRecordDialog';
import { ReconciliationView } from './batch/ReconciliationTab';
import { AdjustmentDialog } from './ledger/AdjustmentDialog';
import { LedgerPage } from './ledger/LedgerPage';
import { BatchesPage } from './pages/BatchesPage';
import { OverviewPage } from './pages/OverviewPage';
import { SchedulePage } from './pages/SchedulePage';
import {
  batchDetail,
  batchSummary,
  payoutItem,
  reconciliation,
  scheduleResponse,
  signedIn,
} from './test/fixtures';

const emptyPage = { items: [], total: 0, page: 1, pageSize: 25, totalPages: 0 };

describe('Prepare batch', () => {
  function renderBatches() {
    return renderWithApp(<BatchesPage />, {
      route: '/finance/batches',
      path: '/finance/batches',
      routes: [{ path: '/finance/batches/:batchId', element: <p>batch review page</p> }],
    });
  }

  it('handles the idempotent response: "Batch already exists" and opens it', async () => {
    const user = userEvent.setup();
    const { calls } = mockFetch({
      ...signedIn,
      'GET /finance/payout-batches': () => json(200, emptyPage),
      'GET /finance/payout-schedule': () => json(200, scheduleResponse()),
      'POST /finance/payout-batches/prepare': () =>
        json(200, { created: false, batch: batchSummary({ id: 'existing-1' }), exclusions: [] }),
    });
    const { router } = renderBatches();
    await user.click(await screen.findByRole('button', { name: 'Prepare batch' }));
    const dialog = await screen.findByRole('dialog', { name: 'Prepare payout batch' });
    expect(await within(dialog).findByText(/Last completed period · 2026-09-20/)).toBeInTheDocument();
    await user.type(within(dialog).getByLabelText(/Note/), 'Regular run');
    await user.click(within(dialog).getByRole('button', { name: 'Prepare batch' }));

    expect(await screen.findByText('Batch already exists')).toBeInTheDocument();
    expect(await screen.findByText('batch review page')).toBeInTheDocument();
    expect(router.state.location.pathname).toBe('/finance/batches/existing-1');
    expect(calls.find((c) => c.path.endsWith('/prepare'))?.body).toEqual({
      periodKey: '2026-09-20',
      note: 'Regular run',
    });
  });

  it('explains payout.no_eligible_earnings and payout.period_not_completed', async () => {
    const user = userEvent.setup();
    let response = problem(409, 'payout.no_eligible_earnings', 'Nothing to pay.');
    mockFetch({
      ...signedIn,
      'GET /finance/payout-batches': () => json(200, emptyPage),
      'GET /finance/payout-schedule': () => json(200, scheduleResponse()),
      'POST /finance/payout-batches/prepare': () => response,
    });
    renderBatches();
    await user.click(await screen.findByRole('button', { name: 'Prepare batch' }));
    const dialog = await screen.findByRole('dialog', { name: 'Prepare payout batch' });
    await within(dialog).findByText(/Last completed period · 2026-09-20/);
    await user.click(within(dialog).getByRole('button', { name: 'Prepare batch' }));
    expect(await within(dialog).findByRole('alert')).toHaveTextContent(
      /There is nothing to pay for this period/,
    );

    response = problem(400, 'payout.period_not_completed', 'Not yet.');
    await user.click(within(dialog).getByRole('radio', { name: /An earlier completed period/ }));
    await user.type(within(dialog).getByLabelText(/Cutoff date/), '2026-10-04');
    await user.click(within(dialog).getByRole('button', { name: 'Prepare batch' }));
    expect(await within(dialog).findByRole('alert')).toHaveTextContent(/hasn’t reached its cutoff yet/);
  });
});

describe('Reconciliation view', () => {
  it('renders a balanced batch', () => {
    render(<ReconciliationView data={reconciliation()} />);
    expect(screen.getByText('Balanced')).toBeInTheDocument();
    expect(screen.getByText('None.')).toBeInTheDocument();
    expect(screen.getByText('$22.00')).toBeInTheDocument();
    expect(screen.getByText('OK')).toBeInTheDocument();
  });

  it('renders an unbalanced batch with severities and per-item discrepancies', () => {
    render(
      <ReconciliationView
        data={reconciliation({
          isBalanced: false,
          discrepancies: [
            {
              type: 'item_amount_mismatch',
              severity: 'error',
              itemId: 'i1',
              userId: 'p1',
              earningId: null,
              message: 'Item amount 25.00 ≠ sum of earnings 20.00.',
            },
            {
              type: 'duplicate_payment_reference',
              severity: 'warning',
              itemId: null,
              userId: null,
              earningId: null,
              message: 'Reference TRX-1 is used on 2 items.',
            },
          ],
          items: [{ ...reconciliation().items[0]!, ok: false, earningsTotal: 20 }],
        })}
      />,
    );
    expect(screen.getByText('Not balanced')).toBeInTheDocument();
    expect(screen.getByText('1 error found. Investigate before archiving this batch.')).toBeInTheDocument();
    expect(screen.getByText('Item amount mismatch')).toBeInTheDocument();
    expect(screen.getByText('Error')).toBeInTheDocument();
    expect(screen.getByText('Warning')).toBeInTheDocument();
    expect(screen.getByText('Discrepancy')).toBeInTheDocument();
  });
});

describe('Adjustment dialog', () => {
  it('keeps the same requestId across retries and uses a new one per opening', async () => {
    const user = userEvent.setup();
    let attempt = 0;
    const { calls } = mockFetch({
      ...signedIn,
      'GET /finance/payout-schedule': () => json(200, scheduleResponse()),
      'POST /finance/adjustments': () => {
        attempt += 1;
        return attempt === 1
          ? problem(503, 'http_503', 'Service unavailable')
          : json(200, {
              created: false,
              earning: { user: { id: 'u', displayName: 'Sara Khan', email: 's@x' } },
            });
      },
    });
    const view = renderWithApp(<AdjustmentDialog open onClose={() => undefined} />, { route: '/' });

    const dialog = await screen.findByRole('alertdialog', { name: 'New adjustment' });
    await user.type(within(dialog).getByLabelText(/user id/i), '33333333-3333-4333-8333-333333333333');
    await user.type(within(dialog).getByLabelText(/^Amount/), '-8');
    await user.type(within(dialog).getByLabelText(/^Reason/), 'Duplicate reward clawback');
    await user.click(within(dialog).getByRole('checkbox'));
    await user.click(within(dialog).getByRole('button', { name: 'Create adjustment' }));
    expect(await within(dialog).findByRole('alert')).toBeInTheDocument();
    await user.click(within(dialog).getByRole('button', { name: 'Create adjustment' }));
    expect(await screen.findByText('Adjustment already recorded')).toBeInTheDocument();

    const posts = calls.filter((c) => c.path === '/finance/adjustments');
    expect(posts).toHaveLength(2);
    const [first, second] = posts.map(
      (p) => p.body as { requestId: string; amount: number; confirm: boolean },
    );
    expect(first!.requestId).toMatch(/^[0-9a-f-]{36}$/);
    expect(second!.requestId).toBe(first!.requestId);
    expect(first).toMatchObject({ amount: -8, currency: 'USD', confirm: true });

    // Reopening the dialog starts a new request.
    view.unmount();
    renderWithApp(<AdjustmentDialog open onClose={() => undefined} />, { route: '/' });
    const again = await screen.findByRole('alertdialog', { name: 'New adjustment' });
    await user.type(within(again).getByLabelText(/user id/i), '33333333-3333-4333-8333-333333333333');
    await user.type(within(again).getByLabelText(/^Amount/), '5');
    await user.type(within(again).getByLabelText(/^Reason/), 'Goodwill credit for delay');
    await user.click(within(again).getByRole('checkbox'));
    await user.click(within(again).getByRole('button', { name: 'Create adjustment' }));
    await waitFor(() => expect(calls.filter((c) => c.path === '/finance/adjustments')).toHaveLength(3));
    const third = calls.filter((c) => c.path === '/finance/adjustments')[2]!.body as { requestId: string };
    expect(third.requestId).not.toBe(first!.requestId);
  });
});

describe('Bulk CSV parsing', () => {
  it('parses lines, skips a header and reports errors per line', () => {
    const paidAt = '2026-09-26T10:00:00.000Z';
    const parsed = parsePaymentCsv(
      [
        'itemId,paymentReference,paidAt',
        '11111111-1111-4111-8111-111111111111,TRX-1',
        '22222222-2222-4222-8222-222222222222;TRX-2;2026-09-25T08:00:00Z',
        'not-an-id,TRX-3',
        '33333333-3333-4333-8333-333333333333,x',
      ].join('\n'),
      paidAt,
    );
    expect(parsed.lines).toEqual([
      { itemId: '11111111-1111-4111-8111-111111111111', paymentReference: 'TRX-1', paidAt },
      {
        itemId: '22222222-2222-4222-8222-222222222222',
        paymentReference: 'TRX-2',
        paidAt: '2026-09-25T08:00:00.000Z',
      },
    ]);
    expect(parsed.errors).toHaveLength(2);
    expect(parsed.errors[0]).toMatch(/Line 4/);
  });
});

describe('Error messages', () => {
  it('maps every documented code to a human message', () => {
    const err = (code: string, status = 409) => new ApiError({ status, code, title: 'raw title' });
    expect(financeErrorMessage(err('payout.self_finalize', 403))).toBe(
      'A different finance user must finalize a batch you prepared.',
    );
    expect(financeErrorMessage(err('ledger.in_payout_batch'))).toMatch(/part of a payout batch/);
    expect(financeErrorMessage(err('payout.settlement_currency_in_use'))).toMatch(/settlement currency/);
    expect(financeErrorMessage(err('something.new'))).toBe('raw title');
    expect(financeErrorMessage(err('http_403', 403))).toMatch(/permission/);
  });
});

describe('Accessibility and responsive layout', () => {
  it('overview has no axe violations', async () => {
    mockFetch({
      ...signedIn,
      'GET /finance/payout-schedule': () => json(200, scheduleResponse()),
      'GET /finance/payout-batches': () =>
        json(200, { ...emptyPage, items: [batchSummary({ status: 'Finalized' })], total: 1 }),
      'GET /finance/payout-batches/b1/reconciliation': () => json(200, reconciliation()),
      'GET /finance/pending-earnings': () => json(200, { ...emptyPage, total: 3 }),
      'GET /finance/holds': () => json(200, { ...emptyPage, total: 1 }),
    });
    const { container } = renderWithApp(<OverviewPage />, { route: '/finance' });
    expect(await screen.findByText('Current payout period')).toBeInTheDocument();
    await screen.findAllByText('Balanced');
    expect(await axeViolations(container)).toEqual([]);
  });

  it('ledger has no axe violations', async () => {
    mockFetch({
      ...signedIn,
      'GET /finance/ledger': () =>
        json(200, {
          ...emptyPage,
          total: 1,
          items: [
            {
              id: 'e1',
              createdAt: '2026-09-20T10:00:00Z',
              user: { id: 'p1', displayName: 'Sara Khan', email: 'sara@example.com' },
              type: 'PostReward',
              status: 'Approved',
              description: 'Post reward',
              campaign: { id: 'c1', title: 'Spring launch' },
              submissionId: null,
              originalAmount: 10,
              originalCurrency: 'EUR',
              exchangeRate: 1.1,
              exchangeRateId: null,
              settlementAmount: 11,
              settlementCurrency: 'USD',
              ruleSetVersion: 3,
              availableAt: null,
              approvedAt: null,
              approvedByUserId: null,
              createdByUserId: null,
              payoutItemId: null,
              paidAt: null,
              reversedAt: null,
              reversesEntryId: null,
              reversedByEntryId: null,
              reason: null,
              concurrencyStamp: 's',
            },
          ],
        }),
    });
    const { container } = renderWithApp(<LedgerPage />, { route: '/finance/ledger' });
    expect(await screen.findByText('$11.00')).toBeInTheDocument();
    expect(screen.getByText('€10.00')).toBeInTheDocument();
    expect(await axeViolations(container)).toEqual([]);
  });

  it('schedule page has no axe violations', async () => {
    mockFetch({ ...signedIn, 'GET /finance/payout-schedule': () => json(200, scheduleResponse()) });
    const { container } = renderWithApp(<SchedulePage />, { route: '/finance/schedule' });
    expect(await screen.findByText('Upcoming periods')).toBeInTheDocument();
    expect(await axeViolations(container)).toEqual([]);
  });

  it('renders batch items as cards with a record action on a phone', async () => {
    setViewportWidth(360);
    mockFetch({
      ...signedIn,
      'GET /finance/payout-batches/b1': () =>
        json(200, batchDetail({ status: 'Finalized' }, [payoutItem({ status: 'AwaitingPayment' })])),
    });
    const { container } = renderWithApp(<BatchReviewPage />, {
      route: '/finance/batches/b1',
      path: '/finance/batches/:batchId',
    });
    expect(await screen.findByRole('button', { name: 'Record payment for Sara Khan' })).toBeInTheDocument();
    expect(container.querySelector('table.ui-table')).toBeNull();
    expect(await axeViolations(container)).toEqual([]);
  });
});
