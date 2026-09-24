import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { json, mockFetch, problem } from '@/test/fetchMock';
import { axeViolations, renderWithApp } from '@/test/render';
import { setViewportWidth } from '@/test/viewport';
import type { ReviewDetail } from '../api/types';
import { REVIEWER_ID, reviewDetail, reviewerSession } from '../test/fixtures';
import { WorkspacePage } from './WorkspacePage';

function renderWorkspace(
  detail: ReviewDetail = reviewDetail(),
  extra: Parameters<typeof mockFetch>[0] = {},
  perms: string[] = [],
) {
  const mock = mockFetch({
    'POST /auth/refresh': reviewerSession(perms),
    'GET /review/submissions/s1': () => json(200, detail),
    'GET /files/f1': () => new Response(new Blob(['png'], { type: 'image/png' }), { status: 200 }),
    ...extra,
  });
  const utils = renderWithApp(<WorkspacePage />, {
    route: '/review/queue/s1',
    path: '/review/queue/:submissionId',
    routes: [{ path: '/review/queue', element: <p>queue page</p> }],
  });
  return { ...mock, ...utils };
}

const decisionCalls = <T extends { method: string; path: string }>(calls: T[]) =>
  calls.filter((c) => c.method === 'POST' && c.path === '/review/submissions/s1/decision');

async function turnOffNext(user: ReturnType<typeof userEvent.setup>) {
  const toggle = screen.getByRole('switch', { name: /next submission/ });
  if (toggle.getAttribute('aria-checked') === 'true') await user.click(toggle);
}

describe('WorkspacePage', () => {
  it('shows requirements next to the submission with risk flags and the claim countdown', async () => {
    renderWorkspace();
    expect(await screen.findByRole('heading', { name: 'Campaign requirements' })).toBeInTheDocument();
    expect(screen.getByText('You’re reviewing this submission')).toBeInTheDocument();
    expect(screen.getByText(/Same screenshot as another submission/)).toBeInTheDocument();
    const link = screen.getByRole('link', { name: /instagram.com\/p\/abc\/\?utm_source/ });
    expect(link).toHaveAttribute('target', '_blank');
    expect(link.getAttribute('rel')).toContain('noopener');
    expect(screen.getByText('https://instagram.com/p/abc')).toBeInTheDocument();
  });

  it('requires a reason for rejections and corrections', async () => {
    const user = userEvent.setup();
    const { calls } = renderWorkspace();
    await user.click(await screen.findByRole('button', { name: /^Reject/ }));
    await turnOffNext(user);
    await user.click(screen.getByRole('button', { name: 'Confirm rejection' }));
    expect(await screen.findByText(/Explain the decision to the participant/)).toBeInTheDocument();
    expect(decisionCalls(calls)).toHaveLength(0);

    await user.click(screen.getByRole('button', { name: /^Request correction/ }));
    await user.click(screen.getByRole('button', { name: 'Send correction request' }));
    expect(await screen.findByText(/Explain the decision to the participant/)).toBeInTheDocument();
    expect(decisionCalls(calls)).toHaveLength(0);

    // A quick reason fills the (editable) text and the decision is sent with the concurrency stamp.
    await user.click(screen.getByRole('button', { name: /The link doesn’t open the post/ }));
    await user.click(screen.getByRole('button', { name: 'Send correction request' }));
    await waitFor(() => expect(decisionCalls(calls)).toHaveLength(1));
    expect(decisionCalls(calls)[0]!.body).toMatchObject({
      decision: 'RequestCorrection',
      reason: expect.stringContaining('The link doesn’t open the post'),
      concurrencyStamp: 'stamp-1',
    });
  });

  it('sends a decision only once when the button is clicked repeatedly', async () => {
    const user = userEvent.setup();
    let release!: () => void;
    const gate = new Promise<void>((resolve) => (release = resolve));
    const { calls } = renderWorkspace(reviewDetail(), {
      'POST /review/submissions/s1/decision': async () => {
        await gate;
        return json(200, {
          submissionId: 's1',
          status: 'Approved',
          decidedAt: '2026-09-23T10:00:00Z',
          decisionReason: null,
          concurrencyStamp: 'stamp-2',
          reward: null,
          earnings: [],
          liveCheckStatus: 'NotRequired',
          liveCheckDueAt: null,
        });
      },
    });
    await user.click(await screen.findByRole('button', { name: /^Approve/ }));
    await turnOffNext(user);
    const submit = screen.getByRole('button', { name: 'Confirm approval' });
    await user.click(submit);
    await user.click(submit);
    await user.click(submit);
    expect(submit).toHaveAttribute('aria-busy', 'true');
    release();
    await waitFor(() => expect(decisionCalls(calls)).toHaveLength(1));
    await screen.findByText('Submission approved');
    expect(decisionCalls(calls)).toHaveLength(1);
  });

  it('explains review.already_decided and refreshes on request', async () => {
    const user = userEvent.setup();
    const { calls } = renderWorkspace(reviewDetail(), {
      'POST /review/submissions/s1/decision': () =>
        problem(409, 'review.already_decided', 'Already decided (Rejected).'),
    });
    await user.click(await screen.findByRole('button', { name: /^Approve/ }));
    await turnOffNext(user);
    await user.click(screen.getByRole('button', { name: 'Confirm approval' }));
    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('Already decided');
    expect(alert).toHaveTextContent('Another reviewer decided this submission');
    const before = calls.filter((c) => c.path === '/review/submissions/s1' && c.method === 'GET').length;
    await user.click(within(alert).getByRole('button', { name: 'Refresh' }));
    await waitFor(() =>
      expect(
        calls.filter((c) => c.path === '/review/submissions/s1' && c.method === 'GET').length,
      ).toBeGreaterThan(before),
    );
  });

  it('maps a suspended participant error to a clear message', async () => {
    const user = userEvent.setup();
    renderWorkspace(reviewDetail(), {
      'POST /review/submissions/s1/decision': () => problem(409, 'participant.not_active', 'Not active'),
    });
    await user.click(await screen.findByRole('button', { name: /^Approve/ }));
    await turnOffNext(user);
    await user.click(screen.getByRole('button', { name: 'Confirm approval' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('Suspended participants can’t be approved');
  });

  it('keyboard shortcuts pick a decision but never fire while typing', async () => {
    const user = userEvent.setup();
    renderWorkspace();
    const approve = await screen.findByRole('button', { name: /^Approve/ });
    expect(approve).toHaveAttribute('aria-keyshortcuts', 'A');

    await user.keyboard('r');
    const reject = screen.getByRole('button', { name: /^Reject/, pressed: true });
    expect(reject).toBeInTheDocument();
    const reason = screen.getByLabelText(/Reason for rejection/);
    await waitFor(() => expect(reason).toHaveFocus());

    await user.type(reason, 'a car');
    expect(reason).toHaveValue('a car');
    expect(screen.getByRole('button', { name: /^Reject/, pressed: true })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /^Approve/, pressed: false })).toBeInTheDocument();

    reason.blur();
    await user.keyboard('a');
    expect(screen.getByRole('button', { name: /^Approve/, pressed: true })).toBeInTheDocument();
  });

  it('does not allow deciding when another reviewer holds the claim', async () => {
    renderWorkspace(reviewDetail({ claimMine: false }));
    expect(await screen.findByText('Omar Reviewer is reviewing this submission')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /^Approve/ })).not.toBeInTheDocument();
  });

  it('reverses an approved submission only after a reason and typed confirmation', async () => {
    const user = userEvent.setup();
    const approved = reviewDetail({ status: 'Approved' });
    approved.submission.claim = { claimedBy: null, claimExpiresAt: null, isMine: false, isActive: false };
    const { calls } = renderWorkspace(
      approved,
      {
        'POST /review/submissions/s1/reverse': () =>
          json(200, { submissionId: 's1', status: 'Reversed', earnings: [] }),
      },
      ['submissions.reverse'],
    );
    await user.click(await screen.findByRole('button', { name: 'Reverse approval…' }));
    const dialog = await screen.findByRole('alertdialog');
    const confirm = within(dialog).getByRole('button', { name: 'Reverse approval' });
    await user.type(within(dialog).getByLabelText(/Reason/), 'Bought followers');
    expect(confirm).toBeDisabled();
    await user.type(within(dialog).getByLabelText(/Type/), 'REVERSE');
    await user.click(confirm);
    await waitFor(() =>
      expect(calls.find((c) => c.path === '/review/submissions/s1/reverse')?.body).toEqual({
        reason: 'Bought followers',
        confirm: true,
      }),
    );
  });

  it('hides reversal without submissions.reverse', async () => {
    const approved = reviewDetail({ status: 'Approved' });
    renderWorkspace(approved);
    await screen.findByRole('heading', { name: 'Campaign requirements' });
    expect(screen.queryByRole('button', { name: 'Reverse approval…' })).not.toBeInTheDocument();
  });

  it('warns about a suspended participant and disables approval', async () => {
    const user = userEvent.setup();
    renderWorkspace(reviewDetail({ participantStatus: 'Suspended' }));
    expect(await screen.findByText('This participant is suspended')).toBeInTheDocument();
    const approve = screen.getByRole('button', { name: /^Approve/ });
    expect(approve).toBeDisabled();
    await user.keyboard('a');
    expect(screen.queryByRole('button', { name: 'Confirm approval' })).not.toBeInTheDocument();
    // Rejecting is still possible.
    await user.click(screen.getByRole('button', { name: /^Reject/ }));
    expect(screen.getByLabelText(/Reason for rejection/)).toBeInTheDocument();
  });

  it('bounds the quality bonus by the rule set maximum', async () => {
    const user = userEvent.setup();
    const { calls } = renderWorkspace(reviewDetail({ qualityBonusMax: 10 }), {
      'POST /review/submissions/s1/decision': () =>
        json(200, {
          submissionId: 's1',
          status: 'Approved',
          decidedAt: '2026-09-23T12:00:00Z',
          decisionReason: null,
          concurrencyStamp: 'stamp-2',
          reward: null,
          earnings: [],
          liveCheckStatus: 'NotRequired',
          liveCheckDueAt: null,
        }),
    });
    await user.click(await screen.findByRole('button', { name: /^Approve/ }));
    await turnOffNext(user);
    const bonus = screen.getByLabelText(/Quality bonus/);
    expect(screen.getByText(/Up to \$10\.00/)).toBeInTheDocument();
    await user.type(bonus, '12');
    await user.click(screen.getByRole('button', { name: 'Confirm approval' }));
    expect(await screen.findByText(/can be at most \$10\.00/)).toBeInTheDocument();
    expect(decisionCalls(calls)).toHaveLength(0);

    await user.clear(bonus);
    await user.type(bonus, '7.5');
    await user.click(screen.getByRole('button', { name: 'Confirm approval' }));
    await waitFor(() => expect(decisionCalls(calls)).toHaveLength(1));
    expect(decisionCalls(calls)[0]!.body).toMatchObject({ decision: 'Approve', qualityBonusAmount: 7.5 });
  });

  it('hides the quality bonus when the rule set has none', async () => {
    const user = userEvent.setup();
    renderWorkspace(reviewDetail({ qualityBonusMax: null }));
    await user.click(await screen.findByRole('button', { name: /^Approve/ }));
    expect(screen.getByRole('button', { name: /Confirm approval/ })).toBeInTheDocument();
    expect(screen.queryByLabelText(/Quality bonus/)).not.toBeInTheDocument();
  });

  it('relies on the server to refuse self-review and explains it', async () => {
    const user = userEvent.setup();
    const own = reviewDetail({ claimMine: false });
    own.submission.claim = { claimedBy: null, claimExpiresAt: null, isMine: false, isActive: false };
    own.participant.id = REVIEWER_ID;
    renderWorkspace(own, {
      'POST /review/submissions/s1/claim': () =>
        problem(403, 'review.self_review', 'You can’t review or resolve your own submissions or appeals.'),
    });
    await user.click(await screen.findByRole('button', { name: 'Claim to review' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('You can’t review your own submission');
    expect(screen.queryByRole('button', { name: /^Approve/ })).not.toBeInTheDocument();
  });

  it('uses tabs on phones and has no axe violations', async () => {
    setViewportWidth(360);
    const { container } = renderWorkspace();
    expect(await screen.findByRole('tablist', { name: 'Submission details' })).toBeInTheDocument();
    expect(await axeViolations(container)).toEqual([]);
  });

  it('has no axe violations on desktop', async () => {
    const { container } = renderWorkspace();
    await screen.findByRole('heading', { name: 'Campaign requirements' });
    expect(await axeViolations(container)).toEqual([]);
  });

  it('explains a submission withdrawn by the participant and offers no decision', async () => {
    const { container } = renderWorkspace(reviewDetail({ status: 'Withdrawn' }));
    expect(await screen.findByText('Withdrawn by participant')).toBeInTheDocument();
    expect(screen.getByText(/Nothing needs to be reviewed/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Confirm approval/ })).not.toBeInTheDocument();
    expect(await axeViolations(container)).toEqual([]);
  });
});
