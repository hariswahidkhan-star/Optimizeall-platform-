import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { json, makeUser, mockFetch, session } from '@/test/fetchMock';
import { renderWithApp } from '@/test/render';
import { mockStaffApi } from '../delivery/testData';
import { MessagesPanel } from './MessagesPanel';

function sizedFile(name: string, bytes: number, type = 'image/png') {
  const file = new File(['x'], name, { type });
  Object.defineProperty(file, 'size', { value: bytes });
  return file;
}

describe('MessagesPanel composer', () => {
  it('names a file over 50 MB instead of silently sending the message without it', async () => {
    const user = userEvent.setup({ applyAccept: false });
    const { calls } = mockStaffApi({ 'GET /agency/clients/c1/threads': () => json(200, []) });
    renderWithApp(<MessagesPanel base="/agency/clients/c1" audience="staff" />, {
      route: '/agency/clients/c1',
    });
    const form = await screen.findByRole('form', { name: 'New conversation' });
    await user.type(within(form).getByLabelText(/Subject/), 'Launch video');
    await user.type(within(form).getByLabelText(/Message/), 'Attached.');
    await user.upload(within(form).getByLabelText(/Attachments/), [
      sizedFile('ok.png', 1000),
      sizedFile('video.png', 50 * 1024 * 1024 + 1),
    ]);
    expect(within(form).getByRole('alert')).toHaveTextContent('video.png is larger than 50 MB');
    expect(within(form).getByRole('button', { name: 'Start conversation' })).toBeDisabled();
    expect(calls.filter((c) => c.method === 'POST' && !c.path.startsWith('/auth/'))).toHaveLength(0);

    await user.upload(within(form).getByLabelText(/Attachments/), [sizedFile('ok.png', 1000)]);
    expect(within(form).queryByRole('alert')).not.toBeInTheDocument();
    expect(within(form).getByRole('button', { name: 'Start conversation' })).toBeEnabled();
  });

  it('refuses more than 10 attachments with a message', async () => {
    const user = userEvent.setup({ applyAccept: false });
    mockStaffApi({ 'GET /agency/clients/c1/threads': () => json(200, []) });
    renderWithApp(<MessagesPanel base="/agency/clients/c1" audience="staff" />, {
      route: '/agency/clients/c1',
    });
    const form = await screen.findByRole('form', { name: 'New conversation' });
    await user.upload(
      within(form).getByLabelText(/Attachments/),
      Array.from({ length: 11 }, (_, i) => sizedFile(`f${i}.png`, 10)),
    );
    expect(within(form).getByRole('alert')).toHaveTextContent('At most 10 attachments');
  });
});

const thread = (overrides: Record<string, unknown> = {}) => ({
  id: 'th1',
  clientId: 'c1',
  subject: 'Launch plan',
  projectId: null,
  messages: [
    {
      id: 'm1',
      author: { id: 'am-1', displayName: 'Amira Haddad', email: '' },
      fromClient: false,
      body: 'Here is the plan.',
      attachments: [],
      createdAt: '2026-09-01T10:00:00Z',
      readBy: [],
    },
  ],
  participants: [],
  isInternal: false,
  canReply: true,
  ...overrides,
});

const summary = (overrides: Record<string, unknown> = {}) => ({
  id: 'th1',
  clientId: 'c1',
  subject: 'Launch plan',
  projectId: null,
  lastMessageAt: '2026-09-01T10:00:00Z',
  messageCount: 1,
  unreadCount: 0,
  lastMessagePreview: 'Here is the plan.',
  lastAuthor: 'Amira Haddad',
  isInternal: false,
  ...overrides,
});

describe('MessagesPanel internal threads', () => {
  it('lets staff start an internal conversation and marks internal threads', async () => {
    const user = userEvent.setup();
    const { calls } = mockStaffApi({
      'GET /agency/clients/c1/threads': () =>
        json(200, [summary({ id: 'th0', subject: 'Margin notes', isInternal: true })]),
      'POST /agency/clients/c1/threads': () =>
        json(201, thread({ id: 'th2', subject: 'Pricing strategy', isInternal: true })),
      'GET /agency/clients/c1/threads/th2': () =>
        json(200, thread({ id: 'th2', subject: 'Pricing strategy', isInternal: true })),
    });
    renderWithApp(<MessagesPanel base="/agency/clients/c1" audience="staff" />, {
      route: '/agency/clients/c1',
    });
    const list = await screen.findByRole('list', { name: 'Conversations' });
    expect(within(list).getByText('Internal')).toBeInTheDocument();
    const form = screen.getByRole('form', { name: 'New conversation' });
    await user.type(within(form).getByLabelText(/Subject/), 'Pricing strategy');
    await user.type(within(form).getByLabelText(/Message/), 'Keep this between us.');
    await user.click(within(form).getByRole('checkbox', { name: /Internal/ }));
    await user.click(within(form).getByRole('button', { name: 'Start conversation' }));
    expect(await screen.findByText('Internal — not visible to the client')).toBeInTheDocument();
    const post = calls.find((c) => c.method === 'POST' && c.path === '/agency/clients/c1/threads');
    expect(post?.body).toMatchObject({ subject: 'Pricing strategy', isInternal: true });
  });

  it('never offers the client an internal option, and hides the composer for read-only members', async () => {
    const user = makeUser({
      id: 'cu-1',
      displayName: 'Vic Viewer',
      roles: ['Client'],
      permissions: ['client.portal'],
    });
    const { calls } = mockFetch({
      'POST /auth/refresh': () => json(200, session(user)),
      'GET /client/orgs/c1/threads': () => json(200, [summary()]),
      'GET /client/orgs/c1/threads/th1': () => json(200, thread({ canReply: false })),
    });
    renderWithApp(<MessagesPanel base="/client/orgs/c1" audience="client" canWrite={false} />, {
      route: '/client/messages?thread=th1',
    });
    expect(
      within(await screen.findByRole('list', { name: 'Messages, oldest first' })).getByText(
        'Here is the plan.',
      ),
    ).toBeInTheDocument();
    expect(screen.queryByRole('form', { name: 'Reply' })).not.toBeInTheDocument();
    expect(screen.queryByRole('form', { name: 'New conversation' })).not.toBeInTheDocument();
    expect(screen.queryByRole('checkbox', { name: /Internal/ })).not.toBeInTheDocument();
    expect(calls.filter((c) => c.method === 'POST' && !c.path.startsWith('/auth/'))).toHaveLength(0);
  });

  it('hides the reply composer when the API says the caller cannot reply', async () => {
    const user = makeUser({
      id: 'cu-2',
      displayName: 'Bea Billing',
      roles: ['Client'],
      permissions: ['client.portal'],
    });
    mockFetch({
      'POST /auth/refresh': () => json(200, session(user)),
      'GET /client/orgs/c1/threads': () => json(200, [summary()]),
      'GET /client/orgs/c1/threads/th1': () => json(200, thread({ canReply: false })),
    });
    renderWithApp(<MessagesPanel base="/client/orgs/c1" audience="client" />, {
      route: '/client/messages?thread=th1',
    });
    expect(
      within(await screen.findByRole('list', { name: 'Messages, oldest first' })).getByText(
        'Here is the plan.',
      ),
    ).toBeInTheDocument();
    expect(screen.queryByRole('form', { name: 'Reply' })).not.toBeInTheDocument();
  });
});
