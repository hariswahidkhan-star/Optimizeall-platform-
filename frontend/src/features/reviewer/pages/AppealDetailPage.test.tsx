import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { json, mockFetch, problem } from '@/test/fetchMock';
import { axeViolations, renderWithApp } from '@/test/render';
import { appealDetail, REVIEWER_ID, reviewerSession } from '../test/fixtures';
import { AppealDetailPage } from './AppealDetailPage';

function renderAppeal(decidedBy: string, extra: Parameters<typeof mockFetch>[0] = {}) {
  const mock = mockFetch({
    'POST /auth/refresh': reviewerSession(),
    'GET /review/appeals/ap1': () => json(200, appealDetail(decidedBy)),
    ...extra,
  });
  const utils = renderWithApp(<AppealDetailPage />, {
    route: '/review/appeals/ap1',
    path: '/review/appeals/:appealId',
  });
  return { ...mock, ...utils };
}

describe('AppealDetailPage', () => {
  it('shows the appeal, the original decision and who made it', async () => {
    renderAppeal('rev-9');
    expect(await screen.findByText('The disclosure is in the first comment.')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Original decision' })).toBeInTheDocument();
    expect(screen.getByText('Disclosure missing')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Campaign requirements' })).toBeInTheDocument();
  });

  it('requires an outcome and a note', async () => {
    const user = userEvent.setup();
    const { calls } = renderAppeal('rev-9');
    await user.click(await screen.findByRole('button', { name: 'Resolve appeal' }));
    expect(await screen.findByText('Choose an outcome.')).toBeInTheDocument();
    expect(screen.getByText(/Write a resolution note/)).toBeInTheDocument();
    expect(calls.some((c) => c.path.endsWith('/resolve'))).toBe(false);
  });

  it('blocks the original decider up front (four-eyes)', async () => {
    renderAppeal(REVIEWER_ID);
    expect(await screen.findByText('A different reviewer must resolve this appeal')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Resolve appeal' })).not.toBeInTheDocument();
  });

  it('explains the server’s appeal.same_reviewer 403', async () => {
    const user = userEvent.setup();
    const { calls } = renderAppeal('rev-9', {
      'POST /review/appeals/ap1/resolve': () =>
        problem(403, 'appeal.same_reviewer', 'Appeals must be resolved by someone else.'),
    });
    await user.click(await screen.findByRole('radio', { name: /Overturn and approve/ }));
    await user.type(screen.getByLabelText(/Resolution note/), 'Disclosure confirmed.');
    await user.click(screen.getByRole('button', { name: 'Resolve appeal' }));
    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('A different reviewer must resolve this appeal');
    expect(alert).toHaveTextContent('You made the original decision');
    await waitFor(() =>
      expect(calls.find((c) => c.path === '/review/appeals/ap1/resolve')?.body).toEqual({
        outcome: 'Overturned',
        note: 'Disclosure confirmed.',
        concurrencyStamp: 'ap-stamp',
      }),
    );
  });

  it('has no axe violations', async () => {
    const { container } = renderAppeal('rev-9');
    await screen.findByText('The disclosure is in the first comment.');
    expect(await axeViolations(container)).toEqual([]);
  });
});
