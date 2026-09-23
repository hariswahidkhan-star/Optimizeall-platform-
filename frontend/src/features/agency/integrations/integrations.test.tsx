import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { json, makeUser, mockFetch, session } from '@/test/fetchMock';
import { axeViolations, renderWithApp } from '@/test/render';
import type { Connection, Provider } from './api';
import { ConnectionDialog } from './ConnectionDialog';
import { IntegrationsPage } from './IntegrationsPage';

const sendgrid: Provider = {
  key: 'sendgrid',
  name: 'SendGrid',
  category: 'Email',
  description: 'Deliver campaign and transactional email.',
  helpText: 'Create an API key with Mail Send permission.',
  docsUrl: 'https://docs.sendgrid.com/',
  settings: [{ key: 'fromEmail', label: 'From address', required: true, help: null, pattern: null, maxLength: 254, placeholder: null }],
  secrets: [
    { key: 'apiKey', label: 'API key', required: true, help: null, pattern: null, maxLength: 500, placeholder: 'SG.xxxx' },
    { key: 'webhookKey', label: 'Webhook verification key', required: false, help: null, pattern: null, maxLength: 500, placeholder: null },
  ],
  agencyWide: true,
  perClient: false,
  supportsVerification: true,
  tokensExpire: false,
};

const meta: Provider = { ...sendgrid, key: 'meta', name: 'Meta (Facebook & Instagram)', category: 'Social', agencyWide: false, perClient: true, secrets: [], settings: [] };

const connection: Connection = {
  id: 'c1',
  provider: 'sendgrid',
  providerName: 'SendGrid',
  clientAccountId: null,
  clientName: null,
  displayName: 'Agency SendGrid',
  settings: { fromEmail: 'hello@agency.test' },
  secrets: [
    { key: 'apiKey', label: 'API key', required: true, saved: true },
    { key: 'webhookKey', label: 'Webhook verification key', required: false, saved: true },
  ],
  status: 'Connected',
  statusMessage: 'Verified — API key accepted.',
  lastVerifiedAt: '2026-09-20T10:00:00Z',
  expiresAt: null,
  expiringSoon: false,
  concurrencyStamp: 'stamp-1',
  createdAt: '2026-09-01T10:00:00Z',
  updatedAt: '2026-09-20T10:00:00Z',
};

describe('integration secret fields', () => {
  it('never shows a saved secret, keeps it when left untouched, and only sends replaced or removed secrets', async () => {
    const user = userEvent.setup();
    const { calls } = mockFetch({ 'PUT /agency/integrations/connections/c1': () => json(200, connection) });
    renderWithApp(<ConnectionDialog provider={sendgrid} connection={connection} clientAccountId={null} onClose={() => {}} />, { withAuth: false });

    const dialog = await screen.findByRole('dialog');
    // Saved secrets are shown as "Saved" without any input carrying a value.
    const apiKey = within(dialog).getByRole('group', { name: 'API key' });
    expect(apiKey).toHaveTextContent('Saved ••••••••');
    expect(dialog.querySelectorAll('input[type="password"]')).toHaveLength(0);
    expect(dialog.innerHTML).not.toContain('SG.');
    // Plain settings are editable and pre-filled.
    expect(within(dialog).getByLabelText(/From address/)).toHaveValue('hello@agency.test');

    // Replace reveals an empty write-only input.
    await user.click(within(dialog).getByRole('button', { name: 'Replace api key' }));
    const input = within(dialog).getByLabelText(/API key/);
    expect(input).toHaveAttribute('type', 'password');
    expect(input).toHaveValue('');
    expect(input).toHaveAttribute('autocomplete', 'new-password');
    await user.type(input, 'SG.new-secret');

    // Optional secrets can be removed; required ones cannot.
    expect(within(dialog).getAllByRole('checkbox', { name: 'Remove this secret' })).toHaveLength(1);
    await user.click(within(dialog).getByRole('checkbox', { name: 'Remove this secret' }));
    expect(within(dialog).getByRole('group', { name: 'Webhook verification key' })).toHaveTextContent('Will be removed when you save');

    await user.click(screen.getByRole('button', { name: 'Save changes' }));
    const put = calls.find((c) => c.method === 'PUT');
    expect(put?.body).toMatchObject({
      displayName: 'Agency SendGrid',
      settings: { fromEmail: 'hello@agency.test' },
      secrets: { apiKey: 'SG.new-secret' },
      clearSecrets: ['webhookKey'],
      concurrencyStamp: 'stamp-1',
    });
  });

  it('sends no secrets at all when the user only edits settings', async () => {
    const user = userEvent.setup();
    const { calls } = mockFetch({ 'PUT /agency/integrations/connections/c1': () => json(200, connection) });
    renderWithApp(<ConnectionDialog provider={sendgrid} connection={connection} clientAccountId={null} onClose={() => {}} />, { withAuth: false });
    const from = await screen.findByLabelText(/From address/);
    await user.clear(from);
    await user.type(from, 'team@agency.test');
    await user.click(screen.getByRole('button', { name: 'Save changes' }));
    const body = calls.find((c) => c.method === 'PUT')?.body as { secrets: Record<string, string>; clearSecrets: string[] };
    expect(body.secrets).toEqual({});
    expect(body.clearSecrets).toEqual([]);
  });

  it('asks for required secrets when connecting for the first time', async () => {
    const user = userEvent.setup();
    const { calls } = mockFetch({ 'POST /agency/integrations/connections': () => json(201, connection) });
    renderWithApp(<ConnectionDialog provider={sendgrid} clientAccountId={null} onClose={() => {}} />, { withAuth: false });
    const dialog = await screen.findByRole('dialog');
    expect(within(dialog).queryByText(/Saved/)).not.toBeInTheDocument();
    const secret = within(dialog).getByLabelText(/API key/);
    expect(secret).toHaveAttribute('type', 'password');
    await user.type(within(dialog).getByLabelText(/From address/), 'hello@agency.test');
    await user.type(secret, 'SG.first');
    await user.click(screen.getByRole('button', { name: 'Save connection' }));
    expect(calls.find((c) => c.method === 'POST')?.body).toMatchObject({
      provider: 'sendgrid',
      clientAccountId: null,
      secrets: { apiKey: 'SG.first' },
    });
  });
});

describe('integrations page', () => {
  it('lists agency-wide providers with their status and has no axe violations', async () => {
    const staff = makeUser({ roles: ['Admin'], permissions: ['integrations.manage'] });
    mockFetch({
      'POST /auth/refresh': () => json(200, session(staff)),
      'GET /agency/integrations/providers': () => json(200, [sendgrid, meta]),
      'GET /agency/integrations/client-options': () => json(200, [{ id: 'cl1', name: 'Nimbus Fitness', slug: 'nimbus-fitness' }]),
      'GET /agency/integrations/connections': () => json(200, [connection]),
    });
    const { container } = renderWithApp(<IntegrationsPage />);
    const card = await screen.findByRole('article', { name: 'SendGrid' });
    expect(within(card).getByText('Connected')).toBeInTheDocument();
    expect(within(card).getByText('API key: saved · Webhook verification key: saved')).toBeInTheDocument();
    expect(within(card).getByRole('button', { name: 'Test SendGrid' })).toBeInTheDocument();
    // Per-client-only providers are not offered at agency scope.
    expect(screen.queryByRole('article', { name: 'Meta (Facebook & Instagram)' })).not.toBeInTheDocument();
    expect(await axeViolations(container)).toEqual([]);
  });
});
