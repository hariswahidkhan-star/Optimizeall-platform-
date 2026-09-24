import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { json, makeUser, mockFetch, session } from '@/test/fetchMock';
import { renderWithApp } from '@/test/render';
import type { Campaign, Checklist } from '../api/types';
import { CampaignEditorPage } from './CampaignEditorPage';

const staff = makeUser({
  id: 'staff-1',
  roles: ['AccountManager'],
  permissions: ['email.manage', 'email.send'],
  timeZone: 'UTC',
});

const draft = {
  id: 'c1',
  clientAccountId: null,
  name: 'Autumn sale',
  channel: 'Email',
  type: 'Regular',
  status: 'Draft',
  listId: null,
  segmentId: null,
  templateId: null,
  senderProfileId: 's1',
  subject: 'Autumn sale',
  previewText: null,
  design: { blocks: [{ type: 'text', html: '<p>Hi</p>' }] },
  topic: null,
  smsBody: null,
  whatsAppTemplateName: null,
  whatsAppTemplateLanguage: null,
  whatsAppParameters: [],
  scheduleMode: 'Immediate',
  scheduledAt: null,
  scheduledLocalTime: null,
  sendWindowStartHour: null,
  sendWindowEndHour: null,
  throttlePerMinute: 600,
  abTestPercent: 20,
  abWinnerMetric: 'OpenRate',
  abWaitHours: 4,
  abWinnerVariant: null,
  abDecidedAt: null,
  variants: [],
  approvalStatus: 'NotRequired',
  approvalNote: null,
  approvalDecidedAt: null,
  sendConfirmedAt: null,
  sendStartedAt: null,
  completedAt: null,
  pausedAt: null,
  pauseReason: null,
  cancelledAt: null,
  recipientCount: 0,
  createdAt: '2026-09-20T10:00:00Z',
  updatedAt: '2026-09-20T10:00:00Z',
  concurrencyStamp: 'stamp-1',
} as unknown as Campaign;

const checklist: Checklist = {
  items: [],
  canSend: true,
  audienceCount: 12,
  smsSegments: null,
  smsEncoding: null,
  estimatedCost: null,
  costCurrency: null,
  requiresClientApproval: false,
};

const later = <T,>(ms: number, value: T) => new Promise<T>((resolve) => setTimeout(() => resolve(value), ms));

describe('campaign editor', () => {
  it('keeps a send dialog opened right after saving, and sends the saved version', async () => {
    const user = userEvent.setup();
    let saved = false;
    const { calls } = mockFetch({
      'POST /auth/refresh': () => json(200, session(staff)),
      // After the save the server has stamp-2; its refetch is slow (a loaded server or a slow network).
      'GET /agency/email/campaigns/c1': () =>
        saved ? later(400, json(200, { ...draft, concurrencyStamp: 'stamp-2' })) : json(200, draft),
      'PUT /agency/email/campaigns/c1': () => {
        saved = true;
        return json(200, { ...draft, concurrencyStamp: 'stamp-2' });
      },
      'GET /agency/email/campaigns/c1/checklist': () => json(200, checklist),
      'GET /agency/email/lists': () => json(200, []),
      'GET /agency/email/segments': () => json(200, []),
      'GET /agency/email/senders': () =>
        json(200, [
          {
            id: 's1',
            clientAccountId: null,
            fromName: 'Brand',
            fromEmail: 'news@brand.example',
            replyTo: null,
            isDefault: true,
            verified: true,
          },
        ]),
      'GET /agency/email/templates': () => json(200, []),
      'POST /agency/email/templates/render': () =>
        json(200, {
          subject: 'Autumn sale',
          html: '<p>Hi</p>',
          text: 'Hi',
          sizeBytes: 10,
          errors: [],
          warnings: [],
        }),
      'POST /agency/email/campaigns/c1/send': () =>
        json(200, { ...draft, status: 'Scheduled', concurrencyStamp: 'stamp-3' }),
    });
    renderWithApp(<CampaignEditorPage channel="email" />, {
      route: '/agency/email/campaigns/c1',
      path: '/agency/email/campaigns/:id',
    });

    await user.click(await screen.findByRole('button', { name: 'Save draft' }));
    await waitFor(() => expect(calls.some((c) => c.method === 'PUT')).toBe(true));
    await user.click(await screen.findByRole('button', { name: 'Review & send' }));
    const dialog = await screen.findByRole('alertdialog', { name: /Send “Autumn sale”/ });

    // The slow refetch lands while the dialog is open: it must stay open.
    await waitFor(() =>
      expect(
        calls.filter((c) => c.method === 'GET' && c.path === '/agency/email/campaigns/c1').length,
      ).toBeGreaterThan(1),
    );
    await later(600, null);
    expect(screen.getByRole('alertdialog', { name: /Send “Autumn sale”/ })).toBe(dialog);

    await user.type(within(dialog).getByLabelText(/to confirm/), 'Autumn sale');
    await user.click(within(dialog).getByRole('button', { name: 'Send now' }));
    await waitFor(() => expect(calls.some((c) => c.path === '/agency/email/campaigns/c1/send')).toBe(true));
    expect(calls.find((c) => c.path === '/agency/email/campaigns/c1/send')!.body).toMatchObject({
      concurrencyStamp: 'stamp-2',
    });
  });
});
