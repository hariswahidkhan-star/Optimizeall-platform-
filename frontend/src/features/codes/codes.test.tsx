import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { json, makeUser, mockFetch, problem, session } from '@/test/fetchMock';
import { axeViolations, renderWithApp } from '@/test/render';
import type { CodeProgram, CodeProgramListItem, CodeSale, MyCode, MyCodeSale } from './api/types';
import { CodeProgramsPage } from './manage/CodeProgramsPage';
import { emptyPayout, payoutToInput } from './manage/ProgramForms';
import { MyCodeSalePage } from './participant/MyCodeSalePage';
import { MyCodesPage } from './participant/MyCodesPage';
import { CodeSaleDetailPage } from './staff/CodeSaleDetailPage';

const paged = <T,>(items: T[]) => ({ items, total: items.length, page: 1, pageSize: 25, totalPages: 1 });

function sessionFor(permissions: string[], roles = ['Participant']) {
  return () => json(200, session(makeUser({ id: 'u1', roles, permissions, timeZone: 'UTC' })));
}

const PARTICIPANT = ['participant.portal'];
const MANAGER = [
  'campaigns.view',
  'campaigns.manage',
  'users.view',
  'rates.view',
  'codes.view',
  'codes.manage',
  'codes.assign',
];

function makeCode(overrides: Partial<MyCode> = {}): MyCode {
  return {
    codeId: 'c1',
    code: 'GLOW-SARA15',
    programId: 'p1',
    programName: 'Summer affiliate',
    brandName: 'Glow Cosmetics',
    discountLabel: '15% off',
    description: 'Skincare',
    terms: 'No coupon sites.',
    storeUrl: 'https://glow.example.com',
    shareUrl: 'https://glow.example.com/?code=GLOW-SARA15&utm_source=optimizeall',
    shared: false,
    assignedFrom: '2026-09-01T00:00:00Z',
    assignedUntil: null,
    isActive: true,
    inactiveReason: null,
    currency: 'USD',
    yourRate: '10% of net',
    tierPerks: ['From 5 approved sales: 12% of net on further sales'],
    requireProof: false,
    maxOrderAgeDays: 60,
    programStartsAt: '2026-09-01T00:00:00Z',
    programEndsAt: null,
    stats: {
      sales: 3,
      pending: 1,
      approved: 2,
      grossSales: 250,
      commissionPending: 5,
      commissionApproved: 20,
      commissionPaid: 0,
      clicks: null,
    },
    ...overrides,
  };
}

function makeMySale(overrides: Partial<MyCodeSale> = {}): MyCodeSale {
  return {
    id: 's1',
    programId: 'p1',
    programName: 'Summer affiliate',
    brandName: 'Glow Cosmetics',
    codeId: 'c1',
    code: 'GLOW-SARA15',
    orderReference: 'GC-1001',
    orderDate: '2026-09-20T12:00:00Z',
    netAmount: 80,
    discountAmount: 14.12,
    currency: 'USD',
    productNote: null,
    proofUrl: null,
    status: 'Pending',
    source: 'Participant',
    submittedAt: '2026-09-20T13:00:00Z',
    decisionReason: null,
    estimatedCommission: 8,
    commissionAmount: null,
    programCurrency: 'USD',
    canEdit: true,
    canWithdraw: true,
    events: [
      {
        fromStatus: null,
        toStatus: 'Pending',
        action: 'submitted',
        actor: { id: 'u1', displayName: 'Sara' },
        reason: null,
        at: '2026-09-20T13:00:00Z',
      },
    ],
    concurrencyStamp: 'st1',
    ...overrides,
  };
}

function makeSale(overrides: Partial<CodeSale> = {}): CodeSale {
  return {
    id: 's1',
    program: { id: 'p1', name: 'Summer affiliate', brandName: 'Glow Cosmetics', currency: 'USD' },
    code: { id: 'c1', name: 'GLOW-SARA15' },
    person: { id: 'u2', displayName: 'Sara Khan' },
    personEmail: 'sara@example.test',
    group: null,
    orderReference: 'GC-1001',
    orderDate: '2026-09-20T12:00:00Z',
    netAmount: 80,
    discountAmount: 14.12,
    currency: 'USD',
    exchangeRate: 1,
    programNetAmount: 80,
    programDiscountAmount: 14.12,
    productNote: 'Glow set',
    proofUrl: null,
    status: 'Pending',
    source: 'Participant',
    createdBy: { id: 'u2', displayName: 'Sara Khan' },
    submittedAt: '2026-09-20T13:00:00Z',
    estimatedCommission: 8,
    commissionAmount: null,
    payoutSourceLabel: null,
    appliedCaps: [],
    payoutVersion: null,
    verification: 'Matched',
    verificationNote: 'Matches the reported sale.',
    reportedNetAmount: 80,
    reportedOrderDate: '2026-09-20T00:00:00Z',
    decidedAt: null,
    decidedBy: null,
    decisionReason: null,
    refundedAt: null,
    refundReason: null,
    isTestAccount: false,
    userStatus: 'Active',
    canDecide: true,
    cannotDecideReason: null,
    events: [],
    earnings: [],
    concurrencyStamp: 'stamp-1',
    ...overrides,
  };
}

describe('payout form mapping', () => {
  it('maps tiers with a new rate and/or a bonus to the API shape', () => {
    const form = {
      ...emptyPayout(),
      percent: '10',
      dailyCap: '150',
      tiers: [
        { thresholdSales: '5', rateType: 'percent' as const, rate: '12', bonus: '' },
        { thresholdSales: '10', rateType: '' as const, rate: '', bonus: '25' },
      ],
    };
    expect(payoutToInput(form)).toEqual({
      payoutType: 'PercentOfNet',
      flatAmount: null,
      percent: 10,
      tiers: [
        { thresholdSales: 5, flatAmount: null, percent: 12, bonusAmount: null },
        { thresholdSales: 10, flatAmount: null, percent: null, bonusAmount: 25 },
      ],
      dailyCapPerPerson: 150,
      programCapPerPerson: null,
      budgetAmount: null,
    });
  });
});

describe('MyCodesPage', () => {
  it('shows the code, share link, rate and stats, and reports a sale as multipart', async () => {
    const user = userEvent.setup();
    const { calls } = mockFetch({
      'POST /auth/refresh': sessionFor(PARTICIPANT),
      'GET /me/codes': () => json(200, [makeCode()]),
      'GET /me/code-sales': () => json(200, paged([makeMySale()])),
      'GET /meta/currencies': () =>
        json(200, [
          { code: 'USD', minorUnits: 2 },
          { code: 'EUR', minorUnits: 2 },
        ]),
      'POST /me/code-sales': () => json(201, makeMySale({ id: 's9', orderReference: 'GC-2002' })),
    });
    const { container } = renderWithApp(<MyCodesPage />, {
      route: '/app/codes',
      path: '/app/codes',
      routes: [{ path: '/app/codes/sales/:saleId', element: <p>Sale page</p> }],
    });
    expect(await screen.findByTestId('my-code')).toHaveTextContent('GLOW-SARA15');
    expect(screen.getByRole('heading', { name: 'Glow Cosmetics' })).toBeInTheDocument();
    expect(screen.getByText('10% of net')).toBeInTheDocument();
    expect(screen.getByDisplayValue(/code=GLOW-SARA15/)).toBeInTheDocument();
    expect(screen.getByText(/From 5 approved sales/)).toBeInTheDocument();
    const sales = await screen.findByRole('table', { name: 'Your reported sales' });
    expect(within(sales).getByRole('link', { name: 'GC-1001' })).toHaveAttribute(
      'href',
      '/app/codes/sales/s1',
    );
    expect(await axeViolations(container)).toEqual([]);

    await user.click(screen.getAllByRole('button', { name: 'Report a sale' })[0]!);
    const dialog = await screen.findByRole('dialog', { name: 'Report a sale' });
    // Client validation first.
    await user.click(within(dialog).getByRole('button', { name: 'Report sale' }));
    expect(
      await within(dialog).findByText('Enter the order number from the confirmation.'),
    ).toBeInTheDocument();
    expect(within(dialog).getByText('Enter the order value (greater than 0).')).toBeInTheDocument();
    expect(calls.some((c) => c.method === 'POST' && c.path === '/me/code-sales')).toBe(false);

    await user.type(within(dialog).getByLabelText(/^Order number/), 'GC-2002');
    await user.type(within(dialog).getByLabelText(/^Order value/), '64.5');
    await user.type(within(dialog).getByLabelText(/^Discount given/), '11.4');
    await user.click(within(dialog).getByRole('button', { name: 'Report sale' }));
    await screen.findByText('Sale page');
    const post = calls.find((c) => c.method === 'POST' && c.path === '/me/code-sales')!;
    const form = post.body as FormData;
    expect(form.get('codeId')).toBe('c1');
    expect(form.get('orderReference')).toBe('GC-2002');
    expect(form.get('netAmount')).toBe('64.5');
    expect(form.get('discountAmount')).toBe('11.4');
    expect(form.get('currency')).toBe('USD');
    expect(String(form.get('orderDate'))).toMatch(/Z$/);
  });

  it('shows the API error when the order was already reported', async () => {
    const user = userEvent.setup();
    mockFetch({
      'POST /auth/refresh': sessionFor(PARTICIPANT),
      'GET /me/codes': () => json(200, [makeCode({ shared: true })]),
      'GET /me/code-sales': () => json(200, paged([])),
      'GET /meta/currencies': () => json(200, [{ code: 'USD', minorUnits: 2 }]),
      'POST /me/code-sales': () =>
        problem(
          409,
          'code_sale.duplicate_order',
          'This order was already reported by another member of your group (the first report counts).',
        ),
    });
    renderWithApp(<MyCodesPage />, { route: '/app/codes', path: '/app/codes' });
    expect(await screen.findByText('Shared with your group')).toBeInTheDocument();
    expect(await screen.findByText('No sales reported yet')).toBeInTheDocument();
    await user.click(screen.getAllByRole('button', { name: 'Report a sale' })[0]!);
    const dialog = await screen.findByRole('dialog', { name: 'Report a sale' });
    await user.type(within(dialog).getByLabelText(/^Order number/), 'GC-1');
    await user.type(within(dialog).getByLabelText(/^Order value/), '10');
    await user.click(within(dialog).getByRole('button', { name: 'Report sale' }));
    expect(await within(dialog).findByText(/another member of your group/)).toBeInTheDocument();
  });

  it('explains when there are no codes', async () => {
    mockFetch({
      'POST /auth/refresh': sessionFor(PARTICIPANT),
      'GET /me/codes': () => json(200, []),
      'GET /me/code-sales': () => json(200, paged([])),
    });
    renderWithApp(<MyCodesPage />, { route: '/app/codes', path: '/app/codes' });
    expect(await screen.findByText('No codes yet')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Report a sale' })).not.toBeInTheDocument();
  });
});

describe('MyCodeSalePage', () => {
  it('shows what the reviewer asked for and withdraws', async () => {
    const user = userEvent.setup();
    const { calls } = mockFetch({
      'POST /auth/refresh': sessionFor(PARTICIPANT),
      'GET /me/code-sales/s1': () =>
        json(200, makeMySale({ status: 'NeedsInfo', decisionReason: 'Attach the confirmation email' })),
      'GET /me/codes': () => json(200, [makeCode()]),
      'POST /me/code-sales/s1/withdraw': () =>
        json(200, makeMySale({ status: 'Withdrawn', canEdit: false, canWithdraw: false })),
    });
    renderWithApp(<MyCodeSalePage />, { route: '/app/codes/sales/s1', path: '/app/codes/sales/:saleId' });
    expect(await screen.findByText('Attach the confirmation email')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Add the information' })).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Withdraw' }));
    const confirm = await screen.findByRole('alertdialog');
    await user.click(within(confirm).getByRole('button', { name: 'Withdraw sale' }));
    await waitFor(() =>
      expect(calls.some((c) => c.method === 'POST' && c.path === '/me/code-sales/s1/withdraw')).toBe(true),
    );
  });
});

describe('CodeSaleDetailPage', () => {
  it('lets a reviewer approve with the concurrency stamp', async () => {
    const user = userEvent.setup();
    const approved = makeSale({
      status: 'Approved',
      commissionAmount: 8,
      canDecide: false,
      payoutSourceLabel: 'Summer affiliate v1 · Program rate · 10% of net',
    });
    let decided = false;
    const { calls } = mockFetch({
      'POST /auth/refresh': sessionFor(['submissions.review', 'sales.review'], ['Reviewer']),
      'GET /admin/code-sales/s1': () => json(200, decided ? approved : makeSale()),
      'POST /admin/code-sales/s1/decision': () => {
        decided = true;
        return json(200, approved);
      },
    });
    const { container } = renderWithApp(
      <CodeSaleDetailPage basePath="/review/code-sales" backLabel="Code sales" />,
      {
        route: '/review/code-sales/s1',
        path: '/review/code-sales/:saleId',
      },
    );
    expect(await screen.findByRole('heading', { level: 1, name: 'Order GC-1001' })).toBeInTheDocument();
    expect(screen.getAllByText('Matched by brand').length).toBeGreaterThan(0);
    expect(await axeViolations(container)).toEqual([]);
    await user.click(screen.getByRole('button', { name: 'Approve' }));
    const confirm = await screen.findByRole('alertdialog');
    await user.click(within(confirm).getByRole('button', { name: 'Approve sale' }));
    await waitFor(() => {
      const call = calls.find((c) => c.path === '/admin/code-sales/s1/decision');
      expect(call?.body).toEqual({ decision: 'Approve', reason: null, concurrencyStamp: 'stamp-1' });
    });
    expect(await screen.findByText(/Summer affiliate v1/)).toBeInTheDocument();
  });

  it('hides decisions when the reviewer may not decide and offers refunds to finance', async () => {
    mockFetch({
      'POST /auth/refresh': sessionFor(['codes.view', 'sales.reverse'], ['Finance']),
      'GET /admin/code-sales/s1': () =>
        json(
          200,
          makeSale({
            status: 'Approved',
            canDecide: false,
            cannotDecideReason: 'This sale is Approved.',
            commissionAmount: 8,
            earnings: [
              {
                id: 'e1',
                type: 'SaleCommission',
                status: 'Approved',
                amount: 8,
                currency: 'USD',
                rateSourceLabel: 'Program rate',
                createdAt: '2026-09-21T00:00:00Z',
              },
            ],
          }),
        ),
    });
    renderWithApp(<CodeSaleDetailPage basePath="/finance/code-sales" backLabel="Code sales" />, {
      route: '/finance/code-sales/s1',
      path: '/finance/code-sales/:saleId',
    });
    expect(await screen.findByRole('button', { name: 'Mark refunded' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Approve' })).not.toBeInTheDocument();
    expect(screen.getByRole('table', { name: 'Ledger entries' })).toBeInTheDocument();
  });

  it('explains four-eyes when the reviewer entered the sale', async () => {
    mockFetch({
      'POST /auth/refresh': sessionFor(['codes.view', 'codes.manage', 'sales.review'], ['Admin']),
      'GET /admin/code-sales/s1': () =>
        json(
          200,
          makeSale({
            canDecide: false,
            source: 'Admin',
            cannotDecideReason:
              'You entered or imported this sale, so someone else must review it (four-eyes).',
          }),
        ),
    });
    renderWithApp(<CodeSaleDetailPage basePath="/manage/codes/sales" backLabel="Code sales" />, {
      route: '/manage/codes/sales/s1',
      path: '/manage/codes/sales/:saleId',
    });
    expect(await screen.findByText(/four-eyes/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Approve' })).not.toBeInTheDocument();
  });
});

describe('CodeProgramsPage', () => {
  const item: CodeProgramListItem = {
    id: 'p1',
    name: 'Summer affiliate',
    brandName: 'Glow Cosmetics',
    status: 'Active',
    currency: 'USD',
    startsAt: '2026-09-01T00:00:00Z',
    endsAt: null,
    payoutSummary: '10% of net · budget 5,000.00 USD',
    codes: 12,
    assignedCodes: 4,
    pendingSales: 3,
    approvedSales: 7,
    commissionApproved: 81.5,
    updatedAt: '2026-09-01T00:00:00Z',
  };

  it('lists programs and creates one with its payout rules', async () => {
    const user = userEvent.setup();
    const { calls } = mockFetch({
      'POST /auth/refresh': sessionFor(MANAGER, ['CampaignManager']),
      'GET /admin/code-programs': () => json(200, paged([item])),
      'GET /meta/currencies': () => json(200, [{ code: 'USD', minorUnits: 2 }]),
      'GET /campaigns/options': () => json(200, []),
      'POST /admin/code-programs': () =>
        json(201, { id: 'p2', name: 'Autumn', brandName: 'Aurora' } as Partial<CodeProgram>),
    });
    renderWithApp(<CodeProgramsPage />, {
      route: '/manage/codes',
      path: '/manage/codes',
      routes: [{ path: '/manage/codes/:programId', element: <p>Program page</p> }],
    });
    const table = await screen.findByRole('table', { name: 'Discount-code programs' });
    expect(within(table).getByRole('link', { name: 'Glow Cosmetics — Summer affiliate' })).toHaveAttribute(
      'href',
      '/manage/codes/p1',
    );
    expect(within(table).getByText('10% of net · budget 5,000.00 USD')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'New program' }));
    const dialog = await screen.findByRole('dialog', { name: 'New discount-code program' });
    await user.type(within(dialog).getByLabelText(/^Brand \/ company/), 'Aurora');
    await user.type(within(dialog).getByLabelText(/^Program name/), 'Autumn');
    await user.click(within(dialog).getByRole('button', { name: 'Add tier' }));
    await user.type(within(dialog).getByLabelText(/^After \(approved sales\)/), '5');
    await user.type(within(dialog).getByLabelText(/^One-off bonus/), '20');
    await user.click(within(dialog).getByRole('button', { name: 'Create program' }));
    await screen.findByText('Program page');
    const post = calls.find((c) => c.method === 'POST' && c.path === '/admin/code-programs')!;
    expect(post.body).toMatchObject({
      name: 'Autumn',
      brandName: 'Aurora',
      currency: 'USD',
      activate: true,
      payout: {
        payoutType: 'PercentOfNet',
        percent: 10,
        tiers: [{ thresholdSales: 5, bonusAmount: 20, flatAmount: null, percent: null }],
      },
    });
  });
});
