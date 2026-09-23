import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { problem } from '@/test/fetchMock';
import { axeViolations } from '@/test/render';
import type { Delivery, Job } from '../api/types';
import { ADMIN_PERMISSIONS, json, mockAdminApi, renderAdmin } from '../test/helpers';
import { formatInterval, JobsPage } from './JobsPage';

const jobs: Job[] = [
  {
    name: 'NotificationDispatchJob',
    jobName: 'NotificationDispatchJob',
    intervalSeconds: 30,
    lastRun: {
      id: 'r1',
      jobName: 'NotificationDispatchJob',
      runKey: 'k',
      status: 'Failed',
      attempt: 1,
      startedAt: '2026-09-23T10:00:00Z',
      finishedAt: '2026-09-23T10:00:01Z',
      summary: null,
      error: 'SMTP connection refused',
      instanceId: 'i1',
    },
  },
];

const delivery = (overrides: Partial<Delivery>): Delivery => ({
  id: 'd1',
  notificationId: 'n1',
  userId: 'u1',
  userEmail: 'sara@example.com',
  type: 'submission.decision',
  title: 'Your post was approved',
  channel: 'Email',
  status: 'Failed',
  attempts: 6,
  nextAttemptAt: '2026-09-23T11:00:00Z',
  lockedUntil: null,
  lastError: '550 mailbox unavailable',
  providerMessageId: null,
  createdAt: '2026-09-23T10:00:00Z',
  sentAt: null,
  ...overrides,
});

describe('Jobs', () => {
  it('formats intervals', () => {
    expect(formatInterval(30)).toBe('Every 30 s');
    expect(formatInterval(300)).toBe('Every 5 min');
    expect(formatInterval(7200)).toBe('Every 2 h');
  });

  it('shows a clear message when "Run now" hits a held lease (409)', async () => {
    const user = userEvent.setup();
    mockAdminApi({
      'GET /admin/jobs': () => json(200, jobs),
      'POST /admin/jobs/NotificationDispatchJob/run': () =>
        problem(409, 'jobs.lease_held', 'The job is already running.'),
    });
    renderAdmin(<JobsPage />, '/admin/jobs', '/admin/jobs');
    const row = (await screen.findByText('NotificationDispatchJob')).closest('tr')!;
    // Last run error is collapsible.
    const toggle = within(row).getByRole('button', { name: 'Show error' });
    expect(toggle).toHaveAttribute('aria-expanded', 'false');
    await user.click(toggle);
    expect(within(row).getByText('SMTP connection refused')).toBeVisible();

    await user.click(within(row).getByRole('button', { name: 'Run NotificationDispatchJob now' }));
    const dialog = await screen.findByRole('alertdialog');
    await user.click(within(dialog).getByRole('button', { name: 'Run now' }));
    expect(await within(dialog).findByRole('alert')).toHaveTextContent(/already running on another instance/);
    expect(await screen.findByText('Job not started')).toBeInTheDocument();
  });

  it('hides "Run now" without settings.manage', async () => {
    mockAdminApi(
      { 'GET /admin/jobs': () => json(200, jobs) },
      ADMIN_PERMISSIONS.filter((p) => p !== 'settings.manage'),
    );
    renderAdmin(<JobsPage />, '/admin/jobs', '/admin/jobs');
    await screen.findByText('NotificationDispatchJob');
    expect(screen.queryByRole('button', { name: /Run .* now/ })).not.toBeInTheDocument();
    expect(screen.getByText(/needs the settings permission/)).toBeInTheDocument();
  });

  it('retries failed deliveries and explains skipped WhatsApp deliveries', async () => {
    const user = userEvent.setup();
    const { calls } = mockAdminApi({
      'GET /admin/jobs': () => json(200, jobs),
      'GET /admin/notifications/deliveries': () =>
        json(200, {
          items: [
            delivery({}),
            delivery({
              id: 'd2',
              channel: 'WhatsApp',
              status: 'Skipped',
              attempts: 1,
              lastError: 'WhatsApp Business credentials are not configured',
            }),
          ],
          total: 2,
          page: 1,
          pageSize: 25,
          totalPages: 1,
        }),
      'POST /admin/notifications/deliveries/d1/retry': () =>
        json(200, delivery({ status: 'Pending', attempts: 0 })),
    });
    const { baseElement } = renderAdmin(<JobsPage />, '/admin/jobs?tab=deliveries', '/admin/jobs');
    expect(await screen.findByText('WhatsApp deliveries are skipped for now')).toBeInTheDocument();
    expect(
      await screen.findByText('Skipped — credentials not configured', { selector: 'span' }),
    ).toBeInTheDocument();
    // Only failed deliveries can be retried.
    const retry = screen.getAllByRole('button', { name: /^Retry/ });
    expect(retry).toHaveLength(1);
    await user.click(retry[0]!);
    const dialog = await screen.findByRole('alertdialog');
    await user.click(within(dialog).getByRole('button', { name: 'Retry delivery' }));
    await waitFor(() =>
      expect(calls.some((c) => c.path === '/admin/notifications/deliveries/d1/retry')).toBe(true),
    );
    expect(await screen.findByText('Delivery queued for retry')).toBeInTheDocument();
    expect(await axeViolations(baseElement)).toEqual([]);
  });
});
