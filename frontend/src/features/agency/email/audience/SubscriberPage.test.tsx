import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { json, makeUser, mockFetch, session } from '@/test/fetchMock';
import { renderWithApp } from '@/test/render';
import type { SubscriberDetail } from '../api/types';
import { SubscriberPage } from './SubscriberPage';

const staff = makeUser({
  id: 'staff-1',
  roles: ['AccountManager'],
  permissions: ['email.manage'],
  timeZone: 'UTC',
});

const contact: SubscriberDetail = {
  id: 's1',
  clientAccountId: null,
  email: 'erase.me@example.com',
  phone: null,
  firstName: 'Erin',
  lastName: 'Rase',
  language: null,
  countryCode: null,
  timeZone: null,
  source: 'manual',
  status: 'Subscribed',
  emailConsent: 'Granted',
  emailConsentAt: '2026-01-01T00:00:00Z',
  smsConsent: 'Unknown',
  smsConsentAt: null,
  whatsAppConsent: 'Unknown',
  whatsAppConsentAt: null,
  frequency: 'Any',
  tags: [],
  customFields: {},
  lists: [],
  consentHistory: [],
  activity: [],
  emailSuppressed: false,
  smsSuppressed: false,
  tier: 'New',
  createdAt: '2026-01-01T00:00:00Z',
  concurrencyStamp: 'stamp-1',
};

describe('contact erasure', () => {
  it('erases the contact and leaves without refetching it (no 404 for the erased contact)', async () => {
    const user = userEvent.setup();
    let erased = false;
    const { calls } = mockFetch({
      'POST /auth/refresh': () => json(200, session(staff)),
      'GET /agency/email/subscribers/s1': () =>
        erased ? json(404, { status: 404, code: 'not_found' }) : json(200, contact),
      'DELETE /agency/email/subscribers/s1': () => {
        erased = true;
        return new Response(null, { status: 204 });
      },
    });
    renderWithApp(<SubscriberPage />, {
      route: '/agency/email/contacts/s1',
      path: '/agency/email/contacts/:id',
      routes: [{ path: '/agency/email/lists', element: <h1>Audience</h1> }],
    });
    await user.click(await screen.findByRole('button', { name: 'Erase contact' }));
    const dialog = await screen.findByRole('alertdialog', { name: 'Erase this contact?' });
    await user.type(within(dialog).getByLabelText(/to confirm/), 'ERASE');
    await user.click(within(dialog).getByRole('button', { name: 'Erase' }));

    expect(await screen.findByRole('heading', { name: 'Audience' })).toBeInTheDocument();
    await waitFor(() => expect(calls.some((c) => c.method === 'DELETE')).toBe(true));
    // Give any (wrong) refetch a chance to happen.
    await new Promise((resolve) => setTimeout(resolve, 50));
    const reads = calls.filter((c) => c.method === 'GET' && c.path === '/agency/email/subscribers/s1');
    expect(reads).toHaveLength(1);
  });
});
