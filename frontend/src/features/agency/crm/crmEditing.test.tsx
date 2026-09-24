import { screen, waitFor, within } from '@testing-library/react';
import { useState } from 'react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { json, mockFetch, problem } from '@/test/fetchMock';
import { axeViolations, renderWithApp } from '@/test/render';
import type { CatalogItem, PriceLineInput } from '@/features/agency/billing/api/types';
import { LineItemsEditor } from '@/features/agency/billing/components/LineItemsEditor';
import type { Contact, ContactSummary, CrmOptions, Proposal } from './api/types';
import { LostReasonDialog } from './components/CrmForms';
import { CrmOptionsEditor } from './components/SalesSettings';
import { ContactDetailPage, ContactsPage } from './pages/ContactsPages';
import { ProposalDetailPage } from './pages/ProposalPages';
import { signedInSales } from './test/fixtures';

const options: CrmOptions = {
  lostReasons: ['Budget', 'Chose a competitor'],
  budgetRanges: ['<2k', '2k-5k'],
  industries: ['Retail'],
  version: 'v1',
};

function contactRow(overrides: Partial<ContactSummary> = {}): ContactSummary {
  return {
    id: 'ct1',
    firstName: 'Rachel',
    lastName: 'Nguyen',
    displayName: 'Rachel Nguyen',
    email: 'rachel@brightline.example',
    phone: null,
    jobTitle: 'CMO',
    companyId: null,
    companyName: null,
    lifecycleStage: 'Lead',
    owner: null,
    consentStatus: 'Unknown',
    tags: [],
    source: null,
    score: 12,
    createdAt: '2026-09-01T10:00:00Z',
    archivedAt: null,
    concurrencyStamp: 'stamp-ct1',
    ...overrides,
  };
}

const page = (items: ContactSummary[]) => ({ items, total: items.length, page: 1, pageSize: 25, totalPages: 1 });

describe('Contacts list: archive filter and bulk actions', () => {
  it('assigns an owner to the selected contacts in one request and is accessible', async () => {
    const user = userEvent.setup();
    const { calls } = mockFetch({
      ...signedInSales,
      'GET /agency/crm/contacts': () => json(200, page([contactRow(), contactRow({ id: 'ct2', displayName: 'Omar Ali', firstName: 'Omar' })])),
      'GET /agency/crm/views': () => json(200, []),
      'GET /agency/crm/assignees': () => json(200, [{ id: 'sales-1', displayName: 'Hassan Raza', email: 'sales@demo.optimizeall.app' }]),
      'POST /agency/crm/contacts/bulk': () => json(200, { requested: 2, updated: 2, notFound: 0 }),
    });
    const { container } = renderWithApp(<ContactsPage />, { route: '/agency/crm/contacts' });
    await user.click(await screen.findByRole('checkbox', { name: 'Select Rachel Nguyen' }));
    await user.click(screen.getByRole('checkbox', { name: 'Select Omar Ali' }));
    expect(await axeViolations(container)).toEqual([]);
    await user.click(screen.getByRole('button', { name: 'Assign owner' }));
    const dialog = await screen.findByRole('dialog', { name: /Assign owner: 2 contacts/ });
    await user.selectOptions(within(dialog).getByLabelText('Owner'), 'sales-1');
    await user.click(within(dialog).getByRole('button', { name: 'Apply' }));
    await waitFor(() =>
      expect(calls.find((c) => c.path === '/agency/crm/contacts/bulk')?.body).toEqual({
        ids: ['ct1', 'ct2'],
        action: 'assignOwner',
        ownerUserId: 'sales-1',
      }),
    );
  });

  it('switches to archived contacts and offers only Restore for them', async () => {
    const user = userEvent.setup();
    const { calls } = mockFetch({
      ...signedInSales,
      'GET /agency/crm/contacts': () => json(200, page([contactRow({ archivedAt: '2026-09-10T10:00:00Z' })])),
      'GET /agency/crm/views': () => json(200, []),
      'GET /agency/crm/assignees': () => json(200, []),
    });
    renderWithApp(<ContactsPage />, { route: '/agency/crm/contacts' });
    await user.selectOptions(await screen.findByLabelText('Show'), 'archived');
    await user.click(await screen.findByRole('checkbox', { name: 'Select Rachel Nguyen' }));
    expect(screen.getByRole('button', { name: /Restore 1 contact/ })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Assign owner' })).not.toBeInTheDocument();
    await waitFor(() => expect(calls.some((c) => c.path === '/agency/crm/contacts' && c.method === 'GET')).toBe(true));
  });
});

describe('Archived contact detail', () => {
  it('explains why it is read-only and restores with the concurrency stamp', async () => {
    const user = userEvent.setup();
    const contact: Contact = {
      ...contactRow({ archivedAt: '2026-09-10T10:00:00Z' }),
      consentChangedAt: null,
      budgetRange: null,
      scoreBreakdown: [],
      firstTouch: { source: null, medium: null, campaign: null, at: null },
      lastTouch: { source: null, medium: null, campaign: null, at: null },
      deals: [],
      engagement: {},
      updatedAt: '2026-09-10T10:00:00Z',
      concurrencyStamp: 'stamp-ct1',
    };
    const { calls } = mockFetch({
      ...signedInSales,
      'GET /agency/crm/contacts/ct1': () => json(200, contact),
      'GET /agency/crm/activities': () => json(200, page([])),
      'POST /agency/crm/contacts/ct1/restore': () => json(200, { ...contact, archivedAt: null }),
    });
    const { container } = renderWithApp(<ContactDetailPage />, { route: '/agency/crm/contacts/ct1', path: '/agency/crm/contacts/:contactId' });
    expect(await screen.findByText('This contact is archived')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Edit' })).not.toBeInTheDocument();
    expect(await axeViolations(container)).toEqual([]);
    await user.click(screen.getByRole('button', { name: 'Restore' }));
    await waitFor(() =>
      expect(calls.find((c) => c.path === '/agency/crm/contacts/ct1/restore')?.body).toEqual({ concurrencyStamp: 'stamp-ct1' }),
    );
  });
});

describe('Lost reason dialog', () => {
  it('offers the configured reasons and sends the chosen one with optional detail', async () => {
    const user = userEvent.setup();
    mockFetch({ ...signedInSales, 'GET /agency/crm/options': () => json(200, options) });
    const confirmed: string[] = [];
    renderWithApp(<LostReasonDialog open dealTitle="Brightline" onClose={() => undefined} onConfirm={async (r) => void confirmed.push(r)} />);
    const dialog = await screen.findByRole('dialog', { name: /Mark “Brightline” as lost/ });
    await user.selectOptions(await within(dialog).findByLabelText(/Reason/), 'Budget');
    await user.type(within(dialog).getByLabelText(/Details/), 'Freeze until Q2');
    expect(await axeViolations(dialog)).toEqual([]);
    await user.click(within(dialog).getByRole('button', { name: 'Mark as lost' }));
    await waitFor(() => expect(confirmed).toEqual(['Budget: Freeze until Q2']));
  });
});

describe('CRM options editor', () => {
  it('saves one entry per line with the loaded version and shows a friendly conflict message', async () => {
    const user = userEvent.setup();
    const { calls } = mockFetch({
      ...signedInSales,
      'GET /agency/crm/options': () => json(200, options),
      'PUT /agency/crm/options': () => problem(409, 'concurrency.conflict', 'These settings were changed by someone else.'),
    });
    const { container } = renderWithApp(<CrmOptionsEditor canEdit />);
    const lost = await screen.findByLabelText(/Lost reasons/);
    await user.type(lost, '\nWent in-house');
    await user.click(screen.getByRole('button', { name: 'Save options' }));
    expect(await screen.findByRole('alert')).toHaveTextContent(/Someone else changed this/);
    expect(calls.find((c) => c.method === 'PUT')?.body).toEqual({
      lostReasons: ['Budget', 'Chose a competitor', 'Went in-house'],
      budgetRanges: ['<2k', '2k-5k'],
      industries: ['Retail'],
      version: 'v1',
    });
    expect(await axeViolations(container)).toEqual([]);
  });
});

function proposal(overrides: Partial<Proposal> = {}): Proposal {
  return {
    id: 'p1',
    number: 'PR-2026-0009',
    title: 'Growth retainer',
    status: 'Draft',
    dealId: null,
    dealTitle: null,
    clientAccountId: null,
    clientName: null,
    companyId: null,
    companyName: null,
    contactId: null,
    contactName: null,
    currency: 'USD',
    currentVersion: 1,
    sentVersion: null,
    recipientName: null,
    recipientEmail: null,
    invoiceOnAcceptance: null,
    shareUrl: null,
    sentAt: null,
    viewCount: 0,
    firstViewedAt: null,
    lastViewedAt: null,
    acceptedAt: null,
    acceptedVersion: null,
    signerName: null,
    signerTitle: null,
    signerEmail: null,
    declinedAt: null,
    declineReason: null,
    version: {
      versionNumber: 1,
      title: 'Growth retainer',
      currency: 'USD',
      validUntil: '2026-10-30',
      executiveSummary: null,
      goals: null,
      scope: null,
      deliverables: null,
      timeline: null,
      terms: null,
      lines: [],
      totals: { currency: 'USD', grossTotal: 0, discountTotal: 0, subtotal: 0, taxTotal: 0, total: 0, taxes: [] },
      recurring: { oneTimeTotal: 0, monthlyTotal: 0, quarterlyTotal: 0, annualTotal: 0, monthlyRecurringValue: 0, firstInvoiceTotal: 0, firstYearValue: 0 },
      createdAt: '2026-09-20T10:00:00Z',
      sentAt: null,
      locked: false,
    },
    versions: [],
    contractIds: [],
    invoiceIds: [],
    createdAt: '2026-09-20T10:00:00Z',
    concurrencyStamp: 'stamp-p1',
    ...overrides,
  };
}

const proposalRoutes = [
  { path: '/agency/proposals', element: <p>Proposals</p> },
  { path: '/agency/proposals/:proposalId/edit', element: <p>Editor</p> },
];

describe('Proposal detail actions', () => {
  it('deletes an unsent draft after confirmation, with the stamp', async () => {
    const user = userEvent.setup();
    const { calls } = mockFetch({
      ...signedInSales,
      'GET /agency/proposals/p1': () => json(200, proposal()),
      'DELETE /agency/proposals/p1': () => json(204),
    });
    renderWithApp(<ProposalDetailPage />, { route: '/agency/proposals/p1', path: '/agency/proposals/:proposalId', routes: proposalRoutes });
    await user.click(await screen.findByRole('button', { name: 'Delete draft' }));
    const dialog = await screen.findByRole('alertdialog', { name: /Delete PR-2026-0009/ });
    expect(await axeViolations(dialog)).toEqual([]);
    await user.click(within(dialog).getByRole('button', { name: 'Delete draft' }));
    await waitFor(() => expect(calls.some((c) => c.method === 'DELETE' && c.path === '/agency/proposals/p1')).toBe(true));
  });

  it('hides delete for a sent proposal, explains the lock when accepted and duplicates it', async () => {
    const user = userEvent.setup();
    const { calls } = mockFetch({
      ...signedInSales,
      'GET /agency/proposals/p1': () => json(200, proposal({ status: 'Accepted', sentVersion: 1, acceptedAt: '2026-09-21T10:00:00Z' })),
      'POST /agency/proposals/p1/duplicate': () => json(201, proposal({ id: 'p2', number: 'PR-2026-0010' })),
    });
    renderWithApp(<ProposalDetailPage />, { route: '/agency/proposals/p1', path: '/agency/proposals/:proposalId', routes: proposalRoutes });
    expect(await screen.findByText('Accepted proposals are locked')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Delete draft' })).not.toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Duplicate' }));
    await waitFor(() => expect(calls.some((c) => c.method === 'POST' && c.path === '/agency/proposals/p1/duplicate')).toBe(true));
  });
});

const catalogItem: CatalogItem = {
  id: 'cat1',
  name: 'SEO retainer',
  description: 'Monthly SEO retainer',
  serviceSlug: 'seo',
  currency: 'USD',
  unitPrice: 1500,
  quantity: 1,
  recurrence: 'Monthly',
  taxRateId: null,
  taxName: null,
  sortOrder: 10,
  isActive: true,
  updatedAt: '2026-09-01T10:00:00Z',
  concurrencyStamp: 's',
};

function EditorHarness({ onLines }: { onLines: (lines: PriceLineInput[]) => void }) {
  const [lines, setLines] = useState<PriceLineInput[]>([
    { description: '', quantity: 1, unitPrice: 0, discountType: 'None', discountValue: 0, taxRateId: null, serviceSlug: null, recurrence: 'OneTime' },
  ]);
  return (
    <LineItemsEditor
      lines={lines}
      currency="USD"
      allowRecurrence
      onChange={(next) => {
        setLines(next);
        onLines(next);
      }}
    />
  );
}

describe('Line items: add from the service catalog', () => {
  it('replaces the blank line with the catalog item values', async () => {
    const user = userEvent.setup();
    mockFetch({
      ...signedInSales,
      'GET /agency/billing/tax-rates': () => json(200, []),
      'GET /agency/billing/catalog': () => json(200, [catalogItem]),
    });
    let latest: PriceLineInput[] = [];
    const { container } = renderWithApp(<EditorHarness onLines={(l) => (latest = l)} />);
    await user.click(await screen.findByRole('button', { name: 'Add from catalog' }));
    await user.click(await screen.findByRole('menuitem', { name: /SEO retainer/ }));
    expect(latest).toEqual([
      {
        description: 'Monthly SEO retainer',
        serviceSlug: 'seo',
        quantity: 1,
        unitPrice: 1500,
        discountType: 'None',
        discountValue: 0,
        taxRateId: null,
        recurrence: 'Monthly',
      },
    ]);
    expect(await axeViolations(container)).toEqual([]);
  });
});
