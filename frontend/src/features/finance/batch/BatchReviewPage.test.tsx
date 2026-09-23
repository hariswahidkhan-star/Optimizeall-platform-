import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { json, mockFetch, problem } from '@/test/fetchMock';
import { axeViolations, renderWithApp } from '@/test/render';
import { batchDetail, batchSummary, payoutItem, reconciliation, signedIn } from '../test/fixtures';
import { BatchReviewPage } from './BatchReviewPage';

function renderBatch(route = '/finance/batches/b1') {
  return renderWithApp(<BatchReviewPage />, { route, path: '/finance/batches/:batchId' });
}

const finalizedItems = [
  payoutItem({ status: 'AwaitingPayment' }),
  payoutItem({
    itemId: '22222222-2222-4222-8222-222222222222',
    user: { id: 'p2', displayName: 'Leo Martins', email: 'leo@example.com', country: 'BR' },
    amount: 22,
    status: 'AwaitingPayment',
  }),
];

describe('BatchReviewPage — finalize', () => {
  it('requires the typed reference and shows the self-finalize error', async () => {
    const user = userEvent.setup();
    const { calls } = mockFetch({
      ...signedIn,
      'GET /finance/payout-batches/b1': () => json(200, batchDetail()),
      'POST /finance/payout-batches/b1/finalize': () =>
        problem(
          403,
          'payout.self_finalize',
          'A batch must be finalized by someone other than the person who prepared it.',
        ),
    });
    renderBatch();

    await user.click(await screen.findByRole('button', { name: 'Finalize' }));
    const dialog = await screen.findByRole('alertdialog', { name: /Finalize PB-2026-09-20/ });
    expect(within(dialog).getByText('No money is sent')).toBeInTheDocument();
    const submit = within(dialog).getByRole('button', { name: 'Finalize batch' });
    expect(submit).toBeDisabled();

    const confirm = within(dialog).getByLabelText(/to confirm/);
    await user.type(confirm, 'PB-2026-09-2');
    expect(submit).toBeDisabled();
    await user.type(confirm, '0');
    expect(submit).toBeEnabled();

    await user.click(submit);
    expect(
      await within(dialog).findByText('A different finance user must finalize a batch you prepared.'),
    ).toBeInTheDocument();
    const post = calls.find((c) => c.path.endsWith('/finalize'));
    expect(post?.body).toEqual({ confirm: true, concurrencyStamp: 'batch-stamp-1' });
  });

  it('shows a refresh action on a stale concurrency stamp', async () => {
    const user = userEvent.setup();
    mockFetch({
      ...signedIn,
      'GET /finance/payout-batches/b1': () => json(200, batchDetail()),
      'POST /finance/payout-batches/b1/finalize': () =>
        problem(409, 'concurrency.conflict', 'This was changed by someone else.'),
    });
    renderBatch();
    await user.click(await screen.findByRole('button', { name: 'Finalize' }));
    const dialog = await screen.findByRole('alertdialog');
    await user.type(within(dialog).getByLabelText(/to confirm/), 'PB-2026-09-20');
    await user.click(within(dialog).getByRole('button', { name: 'Finalize batch' }));
    expect(await within(dialog).findByText('The batch changed')).toBeInTheDocument();
    expect(within(dialog).getByRole('button', { name: 'Refresh batch' })).toBeInTheDocument();
  });

  it('shows dispatch results and states that no money has been sent', async () => {
    const user = userEvent.setup();
    let detail = batchDetail();
    mockFetch({
      ...signedIn,
      'GET /finance/payout-batches/b1': () => json(200, detail),
      'POST /finance/payout-batches/b1/finalize': () => {
        detail = batchDetail(
          { status: 'Finalized', finalizedBy: { id: 'fin-me', displayName: 'Farah Finance', email: 'f@x' } },
          finalizedItems,
        );
        return json(200, {
          batch: detail.batch,
          dispatch: finalizedItems.map((i) => ({
            itemId: i.itemId,
            status: 'RequiresManualAction',
            providerReference: null,
            message: 'Pay manually and record the payment reference',
            reused: false,
          })),
        });
      },
    });
    renderBatch();
    await user.click(await screen.findByRole('button', { name: 'Finalize' }));
    const dialog = await screen.findByRole('alertdialog');
    await user.type(within(dialog).getByLabelText(/to confirm/), 'PB-2026-09-20');
    await user.click(within(dialog).getByRole('button', { name: 'Finalize batch' }));

    expect(
      await screen.findByRole('heading', { name: 'Batch finalized — no money has been sent' }),
    ).toBeInTheDocument();
    const results = screen.getByRole('list', { name: 'Dispatch results' });
    expect(within(results).getAllByText('Manual payment required')).toHaveLength(2);
    expect(await screen.findByText('Awaiting manual payment — no money has been sent')).toBeInTheDocument();
  });

  it('disables finalize for the preparer (four-eyes hint)', async () => {
    mockFetch({
      ...signedIn,
      'GET /finance/payout-batches/b1': () =>
        json(200, batchDetail({ preparedBy: { id: 'fin-me', displayName: 'Farah Finance', email: 'f@x' } })),
    });
    renderBatch();
    expect(await screen.findByRole('button', { name: 'Finalize' })).toBeDisabled();
    expect(
      screen.getByText('You prepared this batch, so a different finance user must finalize it.'),
    ).toBeInTheDocument();
  });
});

describe('BatchReviewPage — record payment', () => {
  it('prevents double submission', async () => {
    const user = userEvent.setup();
    let resolve: (r: Response) => void = () => undefined;
    const { calls } = mockFetch({
      ...signedIn,
      'GET /finance/payout-batches/b1': () => json(200, batchDetail({ status: 'Finalized' }, finalizedItems)),
      'POST /finance/payout-batches/b1/items/11111111-1111-4111-8111-111111111111/record-payment': () =>
        new Promise<Response>((r) => {
          resolve = r;
        }),
    });
    renderBatch();
    await user.click(await screen.findByRole('button', { name: 'Record payment for Sara Khan' }));
    const dialog = await screen.findByRole('dialog', { name: 'Record payment' });
    await user.type(within(dialog).getByLabelText(/Payment reference/), 'TRX-100200');
    const submit = within(dialog).getByRole('button', { name: 'Record payment' });
    fireEvent.click(submit);
    fireEvent.click(submit);
    fireEvent.click(submit);
    await waitFor(() => expect(submit).toHaveAttribute('aria-busy', 'true'));
    const posts = () => calls.filter((c) => c.path.endsWith('/record-payment'));
    expect(posts()).toHaveLength(1);
    expect(posts()[0]?.body).toMatchObject({ paymentReference: 'TRX-100200' });
    resolve(json(200, { item: { ...finalizedItems[0], status: 'Paid' }, batchStatus: 'Finalized' }));
    await waitFor(() =>
      expect(screen.queryByRole('dialog', { name: 'Record payment' })).not.toBeInTheDocument(),
    );
    expect(posts()).toHaveLength(1);
  });

  it('explains a payment already recorded by someone else and offers a refresh', async () => {
    const user = userEvent.setup();
    const { calls } = mockFetch({
      ...signedIn,
      'GET /finance/payout-batches/b1': () => json(200, batchDetail({ status: 'Finalized' }, finalizedItems)),
      'POST /finance/payout-batches/b1/items/11111111-1111-4111-8111-111111111111/record-payment': () =>
        problem(409, 'payout.already_recorded', 'A payment was already recorded for this item.'),
    });
    renderBatch();
    await user.click(await screen.findByRole('button', { name: 'Record payment for Sara Khan' }));
    const dialog = await screen.findByRole('dialog', { name: 'Record payment' });
    await user.type(within(dialog).getByLabelText(/Payment reference/), 'TRX-100200');
    await user.click(within(dialog).getByRole('button', { name: 'Record payment' }));
    expect(await within(dialog).findByText('Already recorded by someone else')).toBeInTheDocument();

    const before = calls.filter((c) => c.method === 'GET' && c.path === '/finance/payout-batches/b1').length;
    await user.click(within(dialog).getByRole('button', { name: 'Refresh' }));
    await waitFor(() =>
      expect(screen.queryByRole('dialog', { name: 'Record payment' })).not.toBeInTheDocument(),
    );
    await waitFor(() =>
      expect(
        calls.filter((c) => c.method === 'GET' && c.path === '/finance/payout-batches/b1').length,
      ).toBeGreaterThan(before),
    );
  });
});

describe('BatchReviewPage — bulk record', () => {
  it('sends the edited table and renders per-item results', async () => {
    const user = userEvent.setup();
    const { calls } = mockFetch({
      ...signedIn,
      'GET /finance/payout-batches/b1': () => json(200, batchDetail({ status: 'Finalized' }, finalizedItems)),
      'POST /finance/payout-batches/b1/record-payments': () =>
        json(200, [
          { itemId: finalizedItems[0]!.itemId, status: 'recorded', message: 'Payment recorded.' },
          {
            itemId: finalizedItems[1]!.itemId,
            status: 'already_recorded',
            message: 'A payment was already recorded for this item.',
          },
        ]),
    });
    renderBatch();
    await user.click(await screen.findByRole('button', { name: 'Record payments' }));
    const dialog = await screen.findByRole('dialog', { name: 'Record payments in bulk' });
    await user.type(await within(dialog).findByLabelText(/Sara Khan/), 'TRX-1');
    await user.type(within(dialog).getByLabelText(/Leo Martins/), 'TRX-2');
    await user.click(within(dialog).getByRole('button', { name: 'Record 2 payments' }));

    const results = await within(dialog).findByRole('region', { name: 'Bulk record results' });
    expect(within(results).getByText('1 recorded · 1 already recorded · 0 not recorded')).toBeInTheDocument();
    expect(within(results).getByText('Recorded')).toBeInTheDocument();
    expect(within(results).getByText('Already recorded')).toBeInTheDocument();
    const post = calls.find((c) => c.path.endsWith('/record-payments'));
    expect(post?.body).toEqual([
      expect.objectContaining({ itemId: finalizedItems[0]!.itemId, paymentReference: 'TRX-1' }),
      expect.objectContaining({ itemId: finalizedItems[1]!.itemId, paymentReference: 'TRX-2' }),
    ]);
  });
});

describe('BatchReviewPage — payment instructions', () => {
  it('asks for an audited confirmation and downloads with confirm=true', async () => {
    const user = userEvent.setup();
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);
    const { fn } = mockFetch({
      ...signedIn,
      'GET /finance/payout-batches/b1': () => json(200, batchDetail({ status: 'Finalized' }, finalizedItems)),
      'GET /finance/payout-batches/b1/payment-instructions.csv': () =>
        new Response('a,b\n', { status: 200, headers: { 'Content-Type': 'text/csv' } }),
    });
    renderBatch();
    await user.click(await screen.findByRole('button', { name: 'Payment instructions' }));
    const dialog = await screen.findByRole('alertdialog', { name: 'Download payment instructions?' });
    expect(within(dialog).getByText('This download is audited')).toBeInTheDocument();
    const urls = () => fn.mock.calls.map(([input]) => String(input));
    expect(urls().some((u) => u.includes('payment-instructions'))).toBe(false);

    await user.click(within(dialog).getByRole('button', { name: 'Download (audited)' }));
    await waitFor(() =>
      expect(urls()).toContain('/api/v1/finance/payout-batches/b1/payment-instructions.csv?confirm=true'),
    );
    await waitFor(() => expect(click).toHaveBeenCalled());
  });
});

describe('BatchReviewPage — reconciliation tab and a11y', () => {
  it('renders the reconciliation tab route', async () => {
    mockFetch({
      ...signedIn,
      'GET /finance/payout-batches/b1': () => json(200, batchDetail({ status: 'Finalized' }, finalizedItems)),
      'GET /finance/payout-batches/b1/reconciliation': () => json(200, reconciliation()),
    });
    renderWithApp(<BatchReviewPage tab="reconciliation" />, {
      route: '/finance/batches/b1/reconciliation',
      path: '/finance/batches/:batchId/reconciliation',
    });
    expect(await screen.findByText('Balanced')).toBeInTheDocument();
  });

  it('has no axe violations on a draft batch', async () => {
    mockFetch({ ...signedIn, 'GET /finance/payout-batches/b1': () => json(200, batchDetail()) });
    const { container } = renderBatch();
    await screen.findByRole('heading', { name: 'PB-2026-09-20', level: 1 });
    expect(screen.getByText('Below minimum')).toBeInTheDocument();
    expect(await axeViolations(container)).toEqual([]);
  });

  it('keeps summary values from the server (no client totals)', async () => {
    mockFetch({
      ...signedIn,
      'GET /finance/payout-batches/b1': () => json(200, batchDetail({ totalAmount: 999.99, itemCount: 2 })),
    });
    renderBatch();
    expect(await screen.findByText('$999.99')).toBeInTheDocument();
    expect(batchSummary().totalAmount).toBe(47);
  });
});
