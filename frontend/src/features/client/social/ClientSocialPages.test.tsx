import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { json, makeUser, mockFetch, session } from '@/test/fetchMock';
import { axeViolations, renderWithApp } from '@/test/render';
import { summary } from '@/features/agency/social/test/fixtures';
import { ClientApprovalsPage, type Organization } from './ClientSocialPages';

const clientSession = () =>
  json(200, session(makeUser({ roles: ['Client'], permissions: ['client.portal'], displayName: 'Casey Client', timeZone: 'UTC' })));

function org(overrides: Partial<Organization> = {}): Organization {
  return { id: 'c1', name: 'Nimbus Fitness', currency: 'USD', timeZone: 'America/New_York', role: 'Approver', canApprove: true, ...overrides };
}

const post = {
  id: 'post1',
  clientAccountId: 'c1',
  clientName: 'Nimbus Fitness',
  title: 'Launch day',
  status: 'ClientApproval',
  scheduledAt: '2026-10-01T14:00:00Z',
  campaignId: null,
  autoAppendUtm: false,
  isEvergreen: false,
  evergreenIntervalDays: 30,
  evergreenMaxRepeats: 3,
  evergreenRepeatCount: 0,
  recycledFromPostId: null,
  recycleNumber: null,
  publishedAt: null,
  failureReason: null,
  requiresClientApproval: true,
  isValid: true,
  allowedActions: ['clientApprove', 'clientRequestChanges'],
  variants: [
    {
      id: 'v1',
      profileId: 'p-x',
      profileHandle: 'nimbusfit',
      profileName: 'Nimbus Fitness',
      network: 'X',
      text: 'Launch day is here!',
      title: null,
      mediaIds: [],
      altTexts: [],
      link: null,
      effectiveLink: null,
      firstComment: null,
      hashtags: [],
      mentions: [],
      publishStatus: 'Pending',
      attempts: 0,
      nextAttemptAt: null,
      failureKind: 'None',
      failureReason: null,
      externalPostId: null,
      publishedUrl: null,
      publishedAt: null,
      publishedManually: false,
      validation: { isValid: true, issues: [] },
    },
  ],
  comments: [],
  createdByName: 'Sofia Social',
  createdAt: '2026-09-01T00:00:00Z',
  updatedAt: '2026-09-01T00:00:00Z',
  concurrencyStamp: 'stamp-9',
};

function routes(organization: Organization) {
  return mockFetch({
    'POST /auth/refresh': clientSession,
    'GET /client/social/organizations': () => json(200, [organization]),
    'GET /client/social/approvals': () => json(200, [summary({ status: 'ClientApproval', scheduledAt: post.scheduledAt })]),
    'GET /client/social/posts/post1': () => json(200, post),
    'GET /client/social/media': () => json(200, []),
    'POST /client/social/posts/post1/approve': () => json(200, { ...post, status: 'Approved', allowedActions: [] }),
    'POST /client/social/posts/post1/request-changes': () => json(200, { ...post, status: 'ChangesRequested', allowedActions: [] }),
  });
}

describe('ClientApprovalsPage', () => {
  it('lets an approver review the preview and approve with the concurrency stamp', async () => {
    const user = userEvent.setup();
    const { calls } = routes(org());
    const { container } = renderWithApp(<ClientApprovalsPage />, { route: '/client/social/approvals', path: '/client/social/approvals' });

    const list = await screen.findByRole('list', { name: 'Posts awaiting approval' }, { timeout: 5000 });
    expect(within(list).getByRole('heading', { name: 'Launch day' })).toBeInTheDocument();
    expect(await axeViolations(container)).toEqual([]);

    await user.click(within(list).getByRole('button', { name: 'Review' }));
    const dialog = await screen.findByRole('dialog', { name: 'Launch day' }, { timeout: 5000 });
    expect(await within(dialog).findByRole('figure', { name: 'X preview' }, { timeout: 5000 })).toHaveTextContent('Launch day is here!');
    // Requesting changes needs a comment.
    expect(within(dialog).getByRole('button', { name: 'Request changes' })).toBeDisabled();
    expect(await axeViolations(document.body)).toEqual([]);

    await user.click(within(dialog).getByRole('button', { name: 'Approve' }));
    await waitFor(() => expect(calls.some((c) => c.path === '/client/social/posts/post1/approve')).toBe(true), { timeout: 5000 });
    expect(calls.find((c) => c.path === '/client/social/posts/post1/approve')!.body).toMatchObject({ concurrencyStamp: 'stamp-9' });
  });

  it('sends the comment when requesting changes', async () => {
    const user = userEvent.setup();
    const { calls } = routes(org());
    renderWithApp(<ClientApprovalsPage />, { route: '/client/social/approvals', path: '/client/social/approvals' });
    await user.click(await screen.findByRole('button', { name: 'Review' }, { timeout: 5000 }));
    const dialog = await screen.findByRole('dialog', { name: 'Launch day' }, { timeout: 5000 });
    await user.type(await within(dialog).findByLabelText(/^Comment/, {}, { timeout: 5000 }), 'Please use the new logo');
    await user.click(within(dialog).getByRole('button', { name: 'Request changes' }));
    await waitFor(() => expect(calls.some((c) => c.path === '/client/social/posts/post1/request-changes')).toBe(true), { timeout: 5000 });
    expect(calls.find((c) => c.path === '/client/social/posts/post1/request-changes')!.body).toMatchObject({ comment: 'Please use the new logo' });
  });

  it('is view-only for a Viewer: no approve or request-changes actions', async () => {
    const user = userEvent.setup();
    routes(org({ role: 'Viewer', canApprove: false }));
    renderWithApp(<ClientApprovalsPage />, { route: '/client/social/approvals', path: '/client/social/approvals' });
    expect(await screen.findByText('View only', {}, { timeout: 5000 })).toBeInTheDocument();
    await user.click(await screen.findByRole('button', { name: 'Review' }, { timeout: 5000 }));
    const dialog = await screen.findByRole('dialog', { name: 'Launch day' }, { timeout: 5000 });
    await within(dialog).findByRole('figure', { name: 'X preview' }, { timeout: 5000 });
    expect(within(dialog).queryByRole('button', { name: 'Approve' })).not.toBeInTheDocument();
    expect(within(dialog).queryByRole('button', { name: 'Request changes' })).not.toBeInTheDocument();
  });
});
