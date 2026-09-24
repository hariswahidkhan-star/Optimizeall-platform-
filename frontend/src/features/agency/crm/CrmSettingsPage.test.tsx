import { screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { json, makeUser, mockFetch, session } from '@/test/fetchMock';
import { renderWithApp } from '@/test/render';
import { CrmSettingsPage } from './pages/CrmSettingsPage';

function signedIn(permissions: string[]) {
  const user = makeUser({ id: 's1', roles: ['Strategist'], permissions, timeZone: 'UTC' });
  return { 'POST /auth/refresh': () => json(200, session(user)) };
}

describe('CRM settings', () => {
  it('leaves out proposal templates for CRM viewers without proposals.manage (the API refuses them)', async () => {
    const { calls } = mockFetch(signedIn(['crm.view', 'clients.view']));
    renderWithApp(<CrmSettingsPage />);
    expect(await screen.findByRole('heading', { level: 1, name: 'CRM settings' })).toBeInTheDocument();
    expect(screen.getByText('Pipeline stages')).toBeInTheDocument();
    expect(screen.queryByText('Proposal templates')).not.toBeInTheDocument();
    expect(calls.some((c) => c.path.startsWith('/agency/proposal-templates'))).toBe(false);
  });

  it('shows proposal templates to people who manage proposals', async () => {
    const { calls } = mockFetch({
      ...signedIn(['crm.view', 'crm.manage', 'proposals.manage']),
      'GET /agency/proposal-templates': () => json(200, []),
    });
    renderWithApp(<CrmSettingsPage />);
    expect(await screen.findByText('Proposal templates')).toBeInTheDocument();
    await screen.findByRole('heading', { level: 1, name: 'CRM settings' });
    await expect.poll(() => calls.some((c) => c.path.startsWith('/agency/proposal-templates'))).toBe(true);
  });
});
