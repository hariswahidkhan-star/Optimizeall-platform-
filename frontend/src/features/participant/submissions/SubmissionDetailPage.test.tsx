import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { json, mockFetch, problem } from '@/test/fetchMock';
import { axeViolations, renderWithApp } from '@/test/render';
import type { SubmissionDetail } from '../api/types';
import { authRoutes, makeSubmission } from '../test/fixtures';
import { SubmissionDetailPage } from './SubmissionDetailPage';

function renderDetail(submission: SubmissionDetail, extra: Record<string, () => Response> = {}) {
  const mock = mockFetch({
    ...authRoutes,
    'GET /me/submissions/sub1': () => json(200, submission),
    ...extra,
  });
  const result = renderWithApp(<SubmissionDetailPage />, {
    route: '/app/submissions/sub1',
    path: '/app/submissions/:id',
  });
  return { ...result, ...mock };
}

describe('SubmissionDetailPage', () => {
  it('shows the resubmit form only when canEdit', async () => {
    renderDetail(
      makeSubmission({
        status: 'NeedsCorrection',
        decisionReason: 'Add #ad to the first line.',
        canEdit: true,
        timeline: [
          {
            action: 'submitted',
            fromStatus: null,
            toStatus: 'Pending',
            reason: null,
            actor: 'You',
            at: '2026-09-20T11:00:00Z',
          },
          {
            action: 'correction_requested',
            fromStatus: 'UnderReview',
            toStatus: 'NeedsCorrection',
            reason: 'Add #ad to the first line.',
            actor: 'Reviewer',
            at: '2026-09-21T11:00:00Z',
          },
        ],
      }),
    );
    expect(await screen.findByRole('heading', { name: 'Edit & resubmit' })).toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: 'Appeal this decision' })).not.toBeInTheDocument();
    expect(screen.getByText('The reviewer asked for a correction')).toBeInTheDocument();
    const timeline = screen.getByRole('list', { name: 'Submission status history' });
    expect(timeline).toHaveTextContent('Correction requested');
    expect(timeline).toHaveTextContent('Reviewer');
  });

  it('shows the appeal form only when canAppeal and enforces the minimum length', async () => {
    const user = userEvent.setup();
    let current = makeSubmission({
      status: 'Rejected',
      decisionReason: 'Not public.',
      canAppeal: true,
      appealDeadline: '2026-10-05T11:00:00Z',
    });
    const { calls } = renderDetail(current, {
      'GET /me/submissions/sub1': () => json(200, current),
      'POST /me/submissions/sub1/appeal': () => {
        current = makeSubmission({
          status: 'Rejected',
          appeal: {
            id: 'ap1',
            status: 'Open',
            decisionAppealed: 'Rejected',
            reason: 'x'.repeat(25),
            resolutionNote: null,
            createdAt: '2026-09-22T00:00:00Z',
            resolvedAt: null,
          },
        });
        return json(200, current);
      },
    });
    expect(await screen.findByRole('heading', { name: 'Appeal this decision' })).toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: 'Edit & resubmit' })).not.toBeInTheDocument();
    expect(screen.getByText(/You can appeal until/)).toBeInTheDocument();

    await user.type(screen.getByLabelText(/Why should the decision/), 'Too short');
    await user.click(screen.getByRole('button', { name: 'Submit appeal' }));
    expect(await screen.findByText('Explain your appeal in at least 20 characters.')).toBeInTheDocument();
    expect(calls.some((c) => c.path.endsWith('/appeal'))).toBe(false);

    await user.type(screen.getByLabelText(/Why should the decision/), ' — the post is public, see the link.');
    await user.click(screen.getByRole('button', { name: 'Submit appeal' }));
    await waitFor(() => expect(calls.some((c) => c.path.endsWith('/appeal'))).toBe(true));
    expect(await screen.findByRole('heading', { name: 'Your appeal' })).toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: 'Appeal this decision' })).not.toBeInTheDocument();
  });

  it('hides both forms otherwise and has no axe violations', async () => {
    const { container } = renderDetail(
      makeSubmission({
        status: 'Approved',
        liveCheck: { status: 'Pending', dueAt: '2026-09-22T10:00:00Z', checkedAt: null },
        earnings: [
          {
            id: 'e1',
            type: 'PostReward',
            amount: 5,
            currency: 'USD',
            status: 'PendingApproval',
            createdAt: '2026-09-21T00:00:00Z',
          },
        ],
      }),
    );
    expect(await screen.findByRole('heading', { name: 'Live check' })).toBeInTheDocument();
    expect(screen.getByText('Scheduled')).toBeInTheDocument();
    expect(screen.getByText('Post reward')).toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: 'Edit & resubmit' })).not.toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: 'Appeal this decision' })).not.toBeInTheDocument();
    expect(await axeViolations(container)).toEqual([]);
  });

  it('withdraws an undecided submission after confirmation', async () => {
    const user = userEvent.setup();
    let current = makeSubmission({ status: 'Pending', canWithdraw: true });
    const { calls, container } = renderDetail(current, {
      'GET /me/submissions/sub1': () => json(200, current),
      'POST /me/submissions/sub1/withdraw': () => {
        current = makeSubmission({
          status: 'Withdrawn',
          canWithdraw: false,
          timeline: [
            ...current.timeline,
            {
              action: 'withdrawn',
              fromStatus: 'Pending',
              toStatus: 'Withdrawn',
              reason: 'Wrong link',
              actor: 'You',
              at: '2026-09-21T09:00:00Z',
            },
          ],
        });
        return json(200, current);
      },
    });
    await user.click(await screen.findByRole('button', { name: 'Withdraw submission' }));
    const dialog = await screen.findByRole('alertdialog', { name: 'Withdraw this submission?' });
    expect(await axeViolations(container)).toEqual([]);
    // Cancelling sends nothing.
    await user.click(screen.getByRole('button', { name: 'Keep it' }));
    expect(dialog).not.toBeInTheDocument();
    expect(calls.some((c) => c.path.endsWith('/withdraw'))).toBe(false);

    await user.click(screen.getByRole('button', { name: 'Withdraw submission' }));
    await user.type(await screen.findByLabelText(/Reason/), 'Wrong link');
    await user.click(screen.getByRole('button', { name: 'Withdraw' }));
    await waitFor(() => expect(calls.some((c) => c.path.endsWith('/withdraw'))).toBe(true));
    expect(calls.find((c) => c.path.endsWith('/withdraw'))?.body).toEqual({ confirm: true, reason: 'Wrong link' });
    expect(await screen.findByText('You withdrew this submission')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Withdraw submission' })).not.toBeInTheDocument();
    expect(screen.getByRole('list', { name: 'Submission status history' })).toHaveTextContent('Withdrawn');
  });

  it('shows the conflict inside the dialog when a reviewer decided first', async () => {
    const user = userEvent.setup();
    renderDetail(makeSubmission({ status: 'UnderReview', canWithdraw: true }), {
      'POST /me/submissions/sub1/withdraw': () =>
        problem(409, 'submission.not_withdrawable', 'This submission can no longer be withdrawn because it was already decided (Approved).'),
    });
    await user.click(await screen.findByRole('button', { name: 'Withdraw submission' }));
    await user.click(await screen.findByRole('button', { name: 'Withdraw' }));
    expect(await screen.findByText(/can no longer be withdrawn/)).toBeInTheDocument();
    expect(screen.getByRole('alertdialog')).toBeInTheDocument();
  });

  it('does not offer withdrawal for decided submissions', async () => {
    renderDetail(makeSubmission({ status: 'Approved', canWithdraw: false }));
    expect(await screen.findByRole('heading', { name: 'Live check' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Withdraw submission' })).not.toBeInTheDocument();
  });
});
