import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { json, mockFetch } from '@/test/fetchMock';
import { axeViolations, renderWithApp } from '@/test/render';
import { NotificationBell } from './NotificationBell';

const inquiry = {
  id: 'n1',
  type: 'website.inquiry',
  title: 'New contact message from Lena (Brightpeak)',
  body: 'lena@brightpeak.test sent a contact message through the website.',
  linkUrl: '/agency/website/inquiries/abc',
  createdAt: new Date().toISOString(),
  readAt: null,
  isRead: false,
};

describe('NotificationBell', () => {
  it('shows the unread count, lists staff notifications with links and marks one read when followed', async () => {
    const user = userEvent.setup();
    const { calls } = mockFetch({
      'GET /me/notifications/unread-count': () => json(200, { count: 1 }),
      'GET /me/notifications': () =>
        json(200, { items: [inquiry], total: 1, page: 1, pageSize: 20, totalPages: 1 }),
      'POST /me/notifications/n1/read': () => json(204, null),
    });
    const { baseElement } = renderWithApp(<NotificationBell />, { withAuth: false });

    await user.click(await screen.findByRole('button', { name: 'Notifications (1 unread)' }));
    const drawer = await screen.findByRole('dialog', { name: 'Notifications' });
    const link = await within(drawer).findByRole('link', { name: inquiry.title });
    expect(link).toHaveAttribute('href', '/agency/website/inquiries/abc');
    expect(await axeViolations(baseElement)).toEqual([]);

    await user.click(link);
    await waitFor(() =>
      expect(calls.some((c) => c.method === 'POST' && c.path === '/me/notifications/n1/read')).toBe(true),
    );
  });
});
