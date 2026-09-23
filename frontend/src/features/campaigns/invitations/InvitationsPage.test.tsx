import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { json, mockFetch } from '@/test/fetchMock';
import { renderWithApp } from '@/test/render';
import type { Invitation } from '../api/types';
import { managerSession } from '../test/fixtures';
import { InvitationsPage } from './InvitationsPage';

const invitation: Invitation = {
  id: 'i1',
  code: 'SPRING26',
  url: 'https://app.test/join/SPRING26',
  name: 'Spring newsletter',
  campaignId: null,
  campaignTitle: null,
  utmSource: 'newsletter',
  utmMedium: 'email',
  utmCampaign: 'spring',
  expiresAt: null,
  maxUses: null,
  isActive: true,
  isUsable: true,
  stats: { visits: 3, registrations: 1, remainingUses: null },
  createdAt: '2026-09-01T00:00:00Z',
  updatedAt: '2026-09-01T00:00:00Z',
};

describe('InvitationsPage', () => {
  it('previews the landing page with preview=true so no visit is counted', async () => {
    const user = userEvent.setup();
    const { fn } = mockFetch({
      'POST /auth/refresh': managerSession(),
      'GET /admin/campaigns': () => json(200, { items: [], total: 0, page: 1, pageSize: 200, totalPages: 0 }),
      'GET /marketing/invitations': () =>
        json(200, { items: [invitation], total: 1, page: 1, pageSize: 25, totalPages: 1 }),
      'GET /public/invitations/SPRING26': () =>
        json(200, {
          code: 'SPRING26',
          type: 'platform',
          headline: 'Get paid to share brands you already love',
          body: 'Join Optimize All.',
          heroImageUrl: null,
          campaign: null,
          utm: { source: 'newsletter', medium: 'email', campaign: 'spring' },
          experiment: null,
        }),
    });
    renderWithApp(<InvitationsPage />, { route: '/manage/invitations', path: '/manage/invitations' });

    await user.click(await screen.findByRole('button', { name: 'Actions for Spring newsletter' }));
    await user.click(screen.getByRole('menuitem', { name: /Preview landing page/ }));
    const dialog = await screen.findByRole('dialog', { name: 'Landing page preview' });
    expect(await within(dialog).findByText('Get paid to share brands you already love')).toBeInTheDocument();
    expect(within(dialog).getByText('Previews aren’t counted as landing page visits.')).toBeInTheDocument();

    await waitFor(() => {
      const url = fn.mock.calls
        .map(([input]) => new URL(String(input), 'http://localhost'))
        .find((u) => u.pathname === '/api/v1/public/invitations/SPRING26');
      expect(url?.searchParams.get('preview')).toBe('true');
    });
  });
});
