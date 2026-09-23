import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { problem } from '@/test/fetchMock';
import { axeViolations } from '@/test/render';
import type { StaffTicket } from '../api/types';
import { json, mockAdminApi, renderAdmin } from '../test/helpers';
import { TicketDetailPage } from './TicketDetailPage';

const ticket: StaffTicket = {
  id: 't1',
  reference: 'SUP-7K2Q9X',
  subject: 'My payout is late',
  category: 'Payout',
  status: 'AwaitingStaff',
  priority: 'Normal',
  submissionId: null,
  payoutItemId: null,
  createdAt: '2026-09-20T10:00:00Z',
  updatedAt: '2026-09-22T10:00:00Z',
  resolvedAt: null,
  requester: {
    id: 'u-42',
    email: 'sara@example.com',
    displayName: 'Sara Khan',
    countryCode: 'PK',
    status: 'Active',
    tier: 'Standard',
    createdAt: '2026-08-01T00:00:00Z',
    openTicketCount: 1,
    totalTicketCount: 3,
  },
  assignedTo: null,
  messages: [
    {
      id: 'm1',
      body: 'Where is my money?',
      isInternalNote: false,
      fromStaff: false,
      authorUserId: 'u-42',
      authorName: 'Sara Khan',
      createdAt: '2026-09-20T10:00:00Z',
    },
    {
      id: 'm2',
      body: 'Payout batch is on hold for KYC.',
      isInternalNote: true,
      fromStaff: true,
      authorUserId: 'admin-1',
      authorName: 'Platform Admin',
      createdAt: '2026-09-21T10:00:00Z',
    },
    {
      id: 'm3',
      body: 'We are looking into it.',
      isInternalNote: false,
      fromStaff: true,
      authorUserId: 'admin-1',
      authorName: 'Platform Admin',
      createdAt: '2026-09-22T10:00:00Z',
    },
  ],
  concurrencyStamp: 'stamp-1',
};

const staffList = () => json(200, { items: [], total: 0, page: 1, pageSize: 200, totalPages: 0 });
const renderTicket = () => renderAdmin(<TicketDetailPage />, '/admin/support/t1', '/admin/support/:ticketId');

describe('Ticket detail', () => {
  it('labels internal notes as not visible to the participant', async () => {
    mockAdminApi({ 'GET /admin/support/tickets/t1': () => json(200, ticket), 'GET /admin/users': staffList });
    const { baseElement } = renderTicket();
    const thread = await screen.findByRole('list', { name: 'Messages, oldest first' });
    const items = within(thread).getAllByRole('listitem');
    expect(items).toHaveLength(3);
    expect(items[1]).toHaveAttribute('data-kind', 'internal');
    expect(within(items[1]!).getByText('Internal — not visible to participant')).toBeInTheDocument();
    expect(within(items[0]!).queryByText(/Internal/)).not.toBeInTheDocument();
    expect(within(items[2]!).getByText('Staff reply')).toBeInTheDocument();
    expect(screen.getByText(/including 1 internal note the participant can’t see/)).toBeInTheDocument();
    expect(await axeViolations(baseElement)).toEqual([]);
  });

  it('adds an internal note with isInternalNote=true and a clear warning', async () => {
    const user = userEvent.setup();
    const { calls } = mockAdminApi({
      'GET /admin/support/tickets/t1': () => json(200, ticket),
      'GET /admin/users': staffList,
      'POST /admin/support/tickets/t1/messages': () => json(200, ticket),
    });
    renderTicket();
    await user.click(await screen.findByRole('radio', { name: /Internal note/ }));
    expect(screen.getByText(/Notes don’t change the ticket status/)).toBeInTheDocument();
    await user.type(screen.getByRole('textbox', { name: 'Internal note' }), 'Checked with finance.');
    await user.click(screen.getByRole('button', { name: 'Add internal note' }));
    await waitFor(() =>
      expect(calls.find((c) => c.method === 'POST' && c.path.endsWith('/messages'))?.body).toEqual({
        body: 'Checked with finance.',
        isInternalNote: true,
      }),
    );
    expect(await screen.findByText('Internal note added')).toBeInTheDocument();
  });

  it('sends the concurrency stamp on update and offers a reload on 409', async () => {
    const user = userEvent.setup();
    let reads = 0;
    const { calls } = mockAdminApi({
      'GET /admin/support/tickets/t1': () =>
        json(200, ++reads === 1 ? ticket : { ...ticket, priority: 'High', concurrencyStamp: 'stamp-2' }),
      'GET /admin/users': staffList,
      'PUT /admin/support/tickets/t1': () => problem(409, 'concurrency.conflict', 'Changed by someone else.'),
    });
    renderTicket();
    await screen.findByRole('list', { name: 'Messages, oldest first' });
    await user.selectOptions(screen.getByLabelText('Status'), 'Resolved');
    await user.selectOptions(screen.getByLabelText('Assignee'), 'admin-1');
    await user.click(screen.getByRole('button', { name: 'Update ticket' }));
    await waitFor(() =>
      expect(calls.find((c) => c.method === 'PUT')?.body).toEqual({
        status: 'Resolved',
        priority: 'Normal',
        assignedToUserId: 'admin-1',
        concurrencyStamp: 'stamp-1',
      }),
    );
    expect(await screen.findByText('This ticket changed while you were viewing it')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Reload ticket' }));
    await waitFor(() => expect(screen.getByLabelText('Priority')).toHaveValue('High'));
    expect(screen.queryByText('This ticket changed while you were viewing it')).not.toBeInTheDocument();
  });
});
