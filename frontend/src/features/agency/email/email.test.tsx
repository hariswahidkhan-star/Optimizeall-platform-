import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import axe from 'axe-core';
import { useState } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { json, makeUser, mockFetch, session, type MockRequest } from '@/test/fetchMock';
import { axeViolations, renderWithApp } from '@/test/render';
import type { Campaign, CampaignReport, Checklist, SegmentDefinition } from './api/types';
import { SendConfirmDialog } from './campaigns/SendConfirmDialog';
import { ConfirmSubscriptionPage, PreferencesPage, SignupPage, UnsubscribePage } from './public/PublicEmailPages';
import { CampaignReportView } from './reports/CampaignReportView';
import { emptyDefinition, SegmentBuilder } from './segments/SegmentBuilder';
import { TemplateEditorPage } from './templates/TemplateEditorPage';

const staff = makeUser({ id: 'staff-1', roles: ['ContentCreator'], permissions: ['email.manage', 'email.send'], timeZone: 'UTC' });

type Handler = (req: MockRequest) => Response | Promise<Response>;
function mockStaffApi(routes: Record<string, Handler>) {
  return mockFetch({ 'POST /auth/refresh': () => json(200, session(staff)), ...routes });
}

function SegmentHarness({ onChange }: { onChange: (d: SegmentDefinition) => void }) {
  const [definition, setDefinition] = useState<SegmentDefinition>(emptyDefinition());
  return (
    <SegmentBuilder
      definition={definition}
      lists={[{ value: 'list-1', label: 'Newsletter' }]}
      onChange={(d) => {
        setDefinition(d);
        onChange(d);
      }}
    />
  );
}

describe('SegmentBuilder', () => {
  it('builds nested AND/OR rules and has no accessibility violations', async () => {
    const user = userEvent.setup();
    mockStaffApi({});
    const onChange = vi.fn();
    const { container } = renderWithApp(<SegmentHarness onChange={onChange} />);
    const rule1 = await screen.findByRole('group', { name: 'Segment rules, rule 1' });

    await user.type(within(rule1).getByLabelText(/Values/), 'US, GB');
    await user.click(screen.getByRole('button', { name: 'Add group' }));
    const nested = screen.getByRole('group', { name: 'Segment rules, group 1, rule 1' });
    await user.type(within(nested).getByLabelText('Tag'), 'vip');
    await user.selectOptions(screen.getAllByLabelText('Contacts must match')[0]!, 'any');

    const last = onChange.mock.lastCall![0] as SegmentDefinition;
    expect(last.match).toBe('any');
    expect(last.conditions[0]).toMatchObject({ kind: 'field', field: 'country', op: 'in', values: ['US', 'GB'] });
    expect(last.groups[0]!.conditions[0]).toMatchObject({ kind: 'tag', op: 'has', value: 'vip' });
    expect(await axeViolations(container)).toEqual([]);
  });

  it('switches a rule to engagement with a day window', async () => {
    const user = userEvent.setup();
    mockStaffApi({});
    const onChange = vi.fn();
    renderWithApp(<SegmentHarness onChange={onChange} />);
    const rule1 = await screen.findByRole('group', { name: 'Segment rules, rule 1' });
    await user.selectOptions(within(rule1).getByLabelText('Rule type'), 'engagement');
    expect(onChange.mock.lastCall![0].conditions[0]).toMatchObject({ kind: 'engagement', event: 'opened', withinDays: 30 });
  });
});

describe('Template editor preview', () => {
  it('renders the server preview in a sandboxed iframe and switches to mobile width', async () => {
    const user = userEvent.setup();
    const { calls } = mockStaffApi({
      'POST /agency/email/templates/render': (req) =>
        json(200, {
          subject: (req.body as { subject: string }).subject.replace('{{first_name}}', 'Ada'),
          html: '<html><body><p>Hello Ada</p></body></html>',
          text: 'Hello Ada',
          sizeBytes: 2048,
          errors: [],
          warnings: ['Add alt text to every image.'],
        }),
    });
    const { container } = renderWithApp(<TemplateEditorPage />, { route: '/agency/email/templates/new', path: '/agency/email/templates/:id' });
    await user.type(await screen.findByLabelText(/^Subject/), 'Hi {{{{first_name}}');

    const frame = await screen.findByTitle('Email preview (desktop)', {}, { timeout: 3000 });
    expect(frame).toHaveAttribute('sandbox', '');
    await waitFor(() => expect(frame.getAttribute('srcdoc')).toContain('Hello Ada'));
    await waitFor(() => expect(screen.getByText('Hi Ada')).toBeInTheDocument(), { timeout: 3000 });
    expect(screen.getByText('Add alt text to every image.')).toBeInTheDocument();

    await user.click(screen.getByRole('radio', { name: 'Mobile (375 px)' }));
    expect(screen.getByTitle('Email preview (mobile)')).toHaveClass('email-preview__frame--mobile');
    const render = calls.filter((c) => c.path === '/agency/email/templates/render').at(-1)!;
    expect(render.body).toMatchObject({ subject: 'Hi {{first_name}}' });
    // axe cannot message the sandboxed srcdoc frame in jsdom, so audit the page without descending into frames.
    const results = await axe.run(container, { iframes: false, rules: { 'color-contrast': { enabled: false }, region: { enabled: false } } });
    expect(results.violations.map((v) => v.id)).toEqual([]);
  });
});

const campaign: Campaign = {
  id: 'c1',
  clientAccountId: null,
  name: 'Autumn sale',
  channel: 'Email',
  type: 'Regular',
  status: 'Draft',
  listId: 'l1',
  segmentId: null,
  templateId: null,
  senderProfileId: 's1',
  subject: 'Autumn sale',
  previewText: null,
  design: { settings: {}, blocks: [] } as unknown as Campaign['design'],
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
  abTestPercent: 0,
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
};

const checklist: Checklist = {
  items: [],
  canSend: true,
  audienceCount: 1234,
  smsSegments: null,
  smsEncoding: null,
  estimatedCost: null,
  costCurrency: null,
  requiresClientApproval: false,
};

describe('SendConfirmDialog', () => {
  it('requires the campaign name to be typed and posts the concurrency stamp', async () => {
    const user = userEvent.setup();
    const { calls } = mockStaffApi({ 'POST /agency/email/campaigns/c1/send': () => json(200, { ...campaign, status: 'Sending' }) });
    const onClose = vi.fn();
    const { container } = renderWithApp(<SendConfirmDialog open onClose={onClose} campaign={campaign} checklist={checklist} />);

    const dialog = await screen.findByRole('alertdialog', { name: /Send “Autumn sale”/ });
    expect(within(dialog).getByText('1,234')).toBeInTheDocument();
    const send = within(dialog).getByRole('button', { name: 'Send now' });
    expect(send).toBeDisabled();
    const input = within(dialog).getByLabelText(/to confirm/);
    await user.type(input, 'Autumn');
    expect(send).toBeDisabled();
    await user.type(input, ' sale');
    expect(send).toBeEnabled();
    expect(await axeViolations(container)).toEqual([]);

    await user.click(send);
    await waitFor(() => expect(onClose).toHaveBeenCalled());
    const post = calls.find((c) => c.path === '/agency/email/campaigns/c1/send')!;
    expect(post.body).toMatchObject({ confirm: true, confirmName: 'Autumn sale', concurrencyStamp: 'stamp-1' });
  });

  it('shows the server refusal and keeps the dialog open', async () => {
    const user = userEvent.setup();
    mockStaffApi({
      'POST /agency/email/campaigns/c1/send': () => json(409, { status: 409, title: 'The campaign changed. Reload it.', code: 'concurrency_conflict' }),
    });
    const onClose = vi.fn();
    renderWithApp(<SendConfirmDialog open onClose={onClose} campaign={campaign} checklist={checklist} />);
    const dialog = await screen.findByRole('alertdialog');
    await user.type(within(dialog).getByLabelText(/to confirm/), 'Autumn sale');
    await user.click(within(dialog).getByRole('button', { name: 'Send now' }));
    expect(await within(dialog).findByText('The campaign changed. Reload it.')).toBeInTheDocument();
    expect(onClose).not.toHaveBeenCalled();
  });
});

const report: CampaignReport = {
  id: 'c1',
  clientAccountId: null,
  name: 'Autumn sale',
  channel: 'Email',
  type: 'AbTest',
  status: 'Sent',
  subject: 'Autumn sale',
  sendStartedAt: '2026-09-20T10:00:00Z',
  completedAt: '2026-09-20T10:05:00Z',
  recipients: 1000,
  sent: 1000,
  pending: 0,
  failed: 0,
  skipped: 0,
  cancelled: 0,
  delivered: 990,
  deliveredIsEstimated: true,
  hardBounces: 8,
  softBounces: 2,
  uniqueOpens: 420,
  totalOpens: 600,
  machineOpens: 150,
  machineOnlyOpeners: 90,
  uniqueClicks: 85,
  totalClicks: 120,
  unsubscribes: 3,
  complaints: 1,
  conversions: 12,
  revenue: [{ currency: 'USD', amount: 1440.5 }],
  openRate: 0.4242,
  clickRate: 0.0859,
  clickToOpenRate: 0.2024,
  bounceRate: 0.01,
  unsubscribeRate: 0.003,
  complaintRate: 0.001,
  smsSegments: 0,
  cost: 0,
  costCurrency: null,
  links: [
    { linkId: 'k1', url: 'https://shop.example/sale', position: 0, uniqueClicks: 70, totalClicks: 100 },
    { linkId: 'k2', url: 'https://shop.example/terms', position: 1, uniqueClicks: 15, totalClicks: 20 },
  ],
  devices: [{ key: 'Mobile', opens: 300, clicks: 60 }],
  mailClients: [{ key: 'Gmail', opens: 200, clicks: 40 }],
  variants: [
    { key: 'A', subject: 'Autumn sale', sent: 100, uniqueOpens: 45, uniqueClicks: 9, openRate: 0.45, clickRate: 0.09, winner: true },
    { key: 'B', subject: 'Last chance', sent: 100, uniqueOpens: 38, uniqueClicks: 7, openRate: 0.38, clickRate: 0.07, winner: false },
  ],
  timeline: [
    { hour: '2026-09-20T10:00:00Z', opens: 200, clicks: 40 },
    { hour: '2026-09-20T11:00:00Z', opens: 120, clicks: 25 },
  ],
};

describe('CampaignReportView', () => {
  it('shows rates, estimated delivery, machine opens, links and the A/B winner', async () => {
    mockStaffApi({});
    const { container } = renderWithApp(<CampaignReportView report={report} />);
    expect(await screen.findByText('Delivered is estimated')).toBeInTheDocument();
    expect(screen.getByText('42.4%')).toBeInTheDocument();
    expect(screen.getByText('8.6%')).toBeInTheDocument();
    expect(screen.getByText('Machine opens')).toBeInTheDocument();
    const links = screen.getByRole('table', { name: 'Link clicks' });
    expect(within(links).getByText('https://shop.example/sale')).toBeInTheDocument();
    const variants = screen.getByRole('table', { name: 'A/B variants' });
    expect(within(variants).getByText('Last chance')).toBeInTheDocument();
    expect(await axeViolations(container)).toEqual([]);
  });
});

describe('Public email pages', () => {
  it('only unsubscribes after the button is pressed (link scanners do nothing)', async () => {
    const user = userEvent.setup();
    const { calls } = mockFetch({ 'POST /public/email/unsubscribe/tok-1': () => new Response(null, { status: 204 }) });
    const { container } = renderWithApp(<UnsubscribePage />, { route: '/email/unsubscribe/tok-1', path: '/email/unsubscribe/:token', withAuth: false });
    await screen.findByRole('heading', { name: 'Unsubscribe' });
    expect(calls).toHaveLength(0);
    expect(await axeViolations(container)).toEqual([]);
    await user.click(screen.getByRole('button', { name: 'Unsubscribe' }));
    expect(await screen.findByRole('heading', { name: 'You are unsubscribed' })).toBeInTheDocument();
    expect(calls.map((c) => `${c.method} ${c.path}`)).toEqual(['POST /public/email/unsubscribe/tok-1']);
  });

  it('saves topic and frequency preferences', async () => {
    const user = userEvent.setup();
    const prefs = {
      workspace: 'Nimbus Fitness',
      maskedEmail: 'a***@example.com',
      unsubscribedFromAll: false,
      frequency: 'Any',
      topics: [
        { listId: 'l1', name: 'Newsletter', description: 'Monthly news', subscribed: true },
        { listId: 'l2', name: 'Offers', description: null, subscribed: false },
      ],
    };
    const { calls } = mockFetch({
      'GET /public/email/preferences/tok-2': () => json(200, prefs),
      'PUT /public/email/preferences/tok-2': () => json(200, { ...prefs, frequency: 'Weekly' }),
    });
    const { container } = renderWithApp(<PreferencesPage />, { route: '/email/preferences/tok-2', path: '/email/preferences/:token', withAuth: false });
    expect(await screen.findByText(/a\*\*\*@example.com/)).toBeInTheDocument();
    expect(await axeViolations(container)).toEqual([]);
    await user.click(screen.getByRole('switch', { name: /Offers/ }));
    await user.click(screen.getByRole('radio', { name: 'At most one a week' }));
    await user.click(screen.getByRole('button', { name: 'Save preferences' }));
    expect(await screen.findByText('Your preferences were saved.')).toBeInTheDocument();
    const put = calls.find((c) => c.method === 'PUT')!;
    expect(put.body).toEqual({
      topics: [
        { listId: 'l1', subscribed: true },
        { listId: 'l2', subscribed: true },
      ],
      frequency: 'Weekly',
      unsubscribeAll: false,
    });
  });

  it('requires explicit consent on the hosted sign-up form', async () => {
    const user = userEvent.setup();
    const { calls } = mockFetch({
      'GET /public/email/forms/abc': () => json(200, { listName: 'Newsletter', workspace: 'Nimbus Fitness', consentText: 'Yes, email me news.', doubleOptIn: true }),
      'POST /public/email/forms/abc': () => json(202, { message: 'Thanks! Check your inbox to confirm.' }),
    });
    const { container } = renderWithApp(<SignupPage />, { route: '/email/subscribe/abc', path: '/email/subscribe/:formKey', withAuth: false });
    const button = await screen.findByRole('button', { name: 'Subscribe' });
    expect(await axeViolations(container)).toEqual([]);
    await user.type(screen.getByLabelText(/^Email/), 'new@example.com');
    expect(button).toBeDisabled();
    await user.click(screen.getByRole('checkbox', { name: 'Yes, email me news.' }));
    await user.click(button);
    expect(await screen.findByText('Thanks! Check your inbox to confirm.')).toBeInTheDocument();
    expect(calls.find((c) => c.method === 'POST')!.body).toMatchObject({ email: 'new@example.com', consent: true, website: '' });
  });

  it('confirms a double opt-in only when the subscriber presses the button, once', async () => {
    const user = userEvent.setup();
    const { calls } = mockFetch({ 'POST /public/email/confirm/tok-3': () => json(200, { list: 'Newsletter', workspace: 'Nimbus Fitness' }) });
    renderWithApp(<ConfirmSubscriptionPage />, { route: '/email/confirm/tok-3', path: '/email/confirm/:token', withAuth: false });
    const button = await screen.findByRole('button', { name: 'Confirm subscription' });
    expect(calls.filter((c) => c.method === 'POST')).toHaveLength(0);
    await user.click(button);
    expect(await screen.findByText(/you are subscribed to/)).toBeInTheDocument();
    expect(calls.filter((c) => c.method === 'POST')).toHaveLength(1);
  });
});
