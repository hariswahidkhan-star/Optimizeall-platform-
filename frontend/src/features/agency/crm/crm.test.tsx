import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { json, mockFetch, problem } from '@/test/fetchMock';
import { axeViolations, renderWithApp } from '@/test/render';
import type { PreviewResponse } from '@/features/agency/billing/api/types';
import { DealsPage } from './pages/DealsPage';
import { ProposalBuilderPage } from './pages/ProposalPages';
import { PublicProposalPage } from './PublicProposalPage';
import { TOKEN, board, emptyTotals, priceLine, publicProposal, signedInSales } from './test/fixtures';

const emptyPage = { items: [], total: 0, page: 1, pageSize: 25, totalPages: 0 };

describe('Pipeline kanban', () => {
  function renderBoard(moveResponse: () => Response = () => json(200, {})) {
    const fetch = mockFetch({
      ...signedInSales,
      'GET /agency/crm/deals/board': () => json(200, board()),
      'GET /agency/crm/assignees': () => json(200, []),
      'GET /agency/crm/deals': () => json(200, emptyPage),
      'POST /agency/crm/deals/d1/move': moveResponse,
    });
    renderWithApp(<DealsPage />, { route: '/agency/crm/deals' });
    return fetch;
  }

  it('moves a deal with the keyboard: pick up, arrow to a stage, drop', async () => {
    const user = userEvent.setup();
    const { calls } = renderBoard();
    const handle = await screen.findByRole('button', { name: 'Move Brightline Dental' });
    handle.focus();
    await user.keyboard('{Enter}');
    expect(handle).toHaveAttribute('aria-pressed', 'true');
    expect(screen.getByText(/Picked up Brightline Dental in New/)).toBeInTheDocument();
    await user.keyboard('{ArrowRight}');
    expect(screen.getByText(/move to Qualified\?/)).toBeInTheDocument();
    await user.keyboard('{Enter}');
    await waitFor(() => expect(calls.some((c) => c.method === 'POST' && c.path.endsWith('/move'))).toBe(true));
    expect(calls.find((c) => c.path.endsWith('/move'))?.body).toEqual({ stageId: 's-qual', concurrencyStamp: 'stamp-d1' });
  });

  it('Escape cancels a keyboard move and moving to Lost asks for a reason first', async () => {
    const user = userEvent.setup();
    const { calls } = renderBoard();
    const handle = await screen.findByRole('button', { name: 'Move Brightline Dental' });
    handle.focus();
    await user.keyboard('{Enter}{ArrowRight}{Escape}');
    expect(handle).toHaveAttribute('aria-pressed', 'false');
    expect(calls.some((c) => c.path.endsWith('/move'))).toBe(false);

    await user.keyboard('{Enter}{ArrowRight}{ArrowRight}{ArrowRight}{Enter}');
    const dialog = await screen.findByRole('dialog', { name: /Mark “Brightline Dental” as lost/ });
    await user.click(within(dialog).getByRole('button', { name: 'Mark as lost' }));
    expect(await within(dialog).findByText('Say why the deal was lost.')).toBeInTheDocument();
    expect(calls.some((c) => c.path.endsWith('/move'))).toBe(false);
    await user.type(within(dialog).getByLabelText(/Lost reason/), 'Chose an in-house team');
    await user.click(within(dialog).getByRole('button', { name: 'Mark as lost' }));
    await waitFor(() => expect(calls.find((c) => c.path.endsWith('/move'))?.body).toEqual({
      stageId: 's-lost', lostReason: 'Chose an in-house team', concurrencyStamp: 'stamp-d1',
    }));
  });

  it('has no axe violations', async () => {
    renderBoard();
    await screen.findByRole('button', { name: 'Move Brightline Dental' });
    expect(await axeViolations(document.body)).toEqual([]);
  });
});

describe('Proposal builder', () => {
  it('shows totals from the preview API, never computed in the browser', async () => {
    const user = userEvent.setup();
    const preview: PreviewResponse = {
      lines: [priceLine({ unitPrice: 999, total: 1234.56 })],
      totals: { ...emptyTotals(), grossTotal: 999, subtotal: 999, taxTotal: 235.56, total: 1234.56, taxes: [{ name: 'VAT', ratePercent: 20, inclusive: false, taxableAmount: 999, taxAmount: 235.56 }] },
      recurring: { oneTimeTotal: 0, monthlyTotal: 1234.56, quarterlyTotal: 0, annualTotal: 0, monthlyRecurringValue: 1234.56, firstInvoiceTotal: 1234.56, firstYearValue: 14814.72 },
    };
    const { calls } = mockFetch({
      ...signedInSales,
      'GET /agency/billing/clients': () => json(200, []),
      'GET /agency/billing/tax-rates': () => json(200, []),
      'GET /meta/currencies': () => json(200, [{ code: 'USD', minorUnits: 2 }]),
      'POST /agency/proposals/preview': () => json(200, preview),
    });
    renderWithApp(<ProposalBuilderPage />, { route: '/agency/proposals/new', path: '/agency/proposals/new' });
    expect(await screen.findByText('Describe every line to see totals.')).toBeInTheDocument();
    await user.type(screen.getByLabelText(/Line 1 description/), 'SEO retainer');
    const price = screen.getByLabelText(/Unit price/);
    await user.clear(price);
    await user.type(price, '999');

    const totals = await screen.findByLabelText('Totals');
    await waitFor(() => expect(totals).toHaveTextContent('$1,234.56'));
    expect(totals).toHaveTextContent('VAT');
    expect(totals).toHaveTextContent('$14,814.72');
    const last = calls.filter((c) => c.path === '/agency/proposals/preview').at(-1);
    expect(last?.body).toMatchObject({ currency: 'USD', lines: [{ description: 'SEO retainer', unitPrice: 999, recurrence: 'Monthly' }] });
  });

  it('explains server validation errors from the preview', async () => {
    const user = userEvent.setup();
    mockFetch({
      ...signedInSales,
      'GET /agency/billing/clients': () => json(200, []),
      'GET /agency/billing/tax-rates': () => json(200, []),
      'GET /meta/currencies': () => json(200, [{ code: 'USD', minorUnits: 2 }]),
      'POST /agency/proposals/preview': () => problem(400, 'billing.invalid_quantity', 'Quantity must be greater than 0.'),
    });
    renderWithApp(<ProposalBuilderPage />, { route: '/agency/proposals/new', path: '/agency/proposals/new' });
    await user.type(await screen.findByLabelText(/Line 1 description/), 'Bad line');
    expect(await screen.findByText('Quantity must be greater than 0.')).toBeInTheDocument();
  });
});

describe('Public proposal page', () => {
  function renderPage(acceptResponse: () => Response) {
    const fetch = mockFetch({
      [`GET /public/proposals/${TOKEN}`]: () => json(200, publicProposal()),
      [`POST /public/proposals/${TOKEN}/accept`]: acceptResponse,
      'POST /auth/refresh': () => problem(401, 'auth.invalid_refresh', 'No session'),
    });
    renderWithApp(<PublicProposalPage />, { route: `/p/${TOKEN}`, path: '/p/:token' });
    return fetch;
  }

  it('validates the typed signature and terms before sending, then accepts', async () => {
    const user = userEvent.setup();
    const { calls } = renderPage(() =>
      json(200, {
        proposal: publicProposal({ status: 'Accepted', canRespond: false, signerName: 'Rachel Nguyen', acceptedAt: '2026-09-23T10:00:00Z' }),
        clientAccountCreated: true,
        invitationSent: true,
        contractsCreated: 1,
        invoiceCreated: true,
      }),
    );
    const form = await screen.findByRole('form', { name: 'Accept proposal' });
    await user.click(within(form).getByRole('button', { name: 'Accept proposal' }));
    expect(within(form).getByText('Type your full name.')).toBeInTheDocument();
    expect(within(form).getByText('Enter your job title.')).toBeInTheDocument();
    expect(within(form).getByText('Please confirm that you agree to the terms.')).toBeInTheDocument();
    expect(calls.some((c) => c.path.endsWith('/accept'))).toBe(false);

    await user.type(within(form).getByLabelText(/Full name/), 'Rachel Nguyen');
    await user.type(within(form).getByLabelText(/Job title/), 'Practice Manager');
    await user.click(within(form).getByRole('checkbox', { name: /I agree to the terms/ }));
    await user.click(within(form).getByRole('button', { name: 'Accept proposal' }));
    expect(await screen.findByText(/Accepted by Rachel Nguyen/)).toBeInTheDocument();
    expect(calls.find((c) => c.path.endsWith('/accept'))?.body).toEqual({
      version: 2, fullName: 'Rachel Nguyen', title: 'Practice Manager', agreeToTerms: true,
    });
  });

  it('explains a conflict when a newer version was sent', async () => {
    const user = userEvent.setup();
    renderPage(() => problem(409, 'proposal.version_mismatch', 'Newer version.'));
    const form = await screen.findByRole('form', { name: 'Accept proposal' });
    await user.type(within(form).getByLabelText(/Full name/), 'Rachel Nguyen');
    await user.type(within(form).getByLabelText(/Job title/), 'Practice Manager');
    await user.click(within(form).getByRole('checkbox', { name: /I agree/ }));
    await user.click(within(form).getByRole('button', { name: 'Accept proposal' }));
    expect(await within(form).findByRole('alert')).toHaveTextContent(/A newer version of this proposal was sent/);
  });

  it('shows expired proposals without the form and has no axe violations', async () => {
    mockFetch({
      [`GET /public/proposals/${TOKEN}`]: () => json(200, publicProposal({ expired: true, canRespond: false, status: 'Expired' })),
      'POST /auth/refresh': () => problem(401, 'auth.invalid_refresh', 'No session'),
    });
    const { container } = renderWithApp(<PublicProposalPage />, { route: `/p/${TOKEN}`, path: '/p/:token' });
    expect(await screen.findByText('This proposal has expired')).toBeInTheDocument();
    expect(screen.queryByRole('form', { name: 'Accept proposal' })).not.toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 1, name: 'Growth retainer' })).toBeInTheDocument();
    expect(await axeViolations(container)).toEqual([]);
  });

  it('rejects malformed tokens without calling the API', async () => {
    const { calls } = mockFetch({ 'POST /auth/refresh': () => problem(401, 'auth.invalid_refresh', 'No session') });
    renderWithApp(<PublicProposalPage />, { route: '/p/short', path: '/p/:token' });
    expect(await screen.findByText('This proposal link isn’t valid')).toBeInTheDocument();
    expect(calls.some((c) => c.path.startsWith('/public/'))).toBe(false);
  });
});
