import { screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { MyOrganization } from '@/features/agency/shared/deliveryTypes';
import { json, makeUser, mockFetch, session } from '@/test/fetchMock';
import { renderWithApp } from '@/test/render';
import { ClientBillingPage } from './ClientBillingPages';

const clientUser = makeUser({
  id: 'client-1',
  roles: ['Client'],
  permissions: ['client.portal'],
  timeZone: 'UTC',
});
const signedIn = { 'POST /auth/refresh': () => json(200, session(clientUser)) };

function org(role: MyOrganization['role']): MyOrganization {
  return {
    clientId: 'org-1',
    name: 'Nimbus Fitness',
    slug: 'nimbus-fitness',
    status: 'Active',
    role,
    logoUrl: null,
    currency: 'USD',
    timeZone: 'UTC',
  };
}

describe('Client billing page', () => {
  it('explains that billing is not available to an Approver without asking the billing API (no 403s)', async () => {
    const { calls } = mockFetch({ ...signedIn, 'GET /client/orgs': () => json(200, [org('Approver')]) });
    renderWithApp(<ClientBillingPage />);
    expect(await screen.findByText('Billing isn’t available to you')).toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 1, name: 'Billing' })).toBeInTheDocument();
    expect(calls.filter((c) => c.path.startsWith('/client/billing'))).toEqual([]);
  });

  it('loads the billing summary and invoices for a Billing member', async () => {
    mockFetch({
      ...signedIn,
      'GET /client/orgs': () => json(200, [org('Billing')]),
      'GET /client/billing/summary': () =>
        json(200, {
          outstanding: [],
          overdue: [],
          openInvoices: 0,
          proposalsAwaitingResponse: 0,
          organizations: [
            { clientAccountId: 'org-1', name: 'Nimbus Fitness', currency: 'USD', role: 'Billing' },
          ],
        }),
      'GET /client/billing/invoices': () => json(200, { items: [], page: 1, pageSize: 100, totalCount: 0 }),
    });
    renderWithApp(<ClientBillingPage />);
    expect(
      await screen.findByText('Your invoices, proposals and agreements with Optimize All.'),
    ).toBeInTheDocument();
    expect(await screen.findByText('No invoices yet')).toBeInTheDocument();
    expect(screen.queryByText('Billing isn’t available to you')).not.toBeInTheDocument();
  });
});
