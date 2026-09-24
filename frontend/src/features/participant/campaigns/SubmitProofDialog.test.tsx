import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { json, mockFetch, problem } from '@/test/fetchMock';
import { renderWithApp } from '@/test/render';
import { authRoutes, makeCampaign, makeSubmission } from '../test/fixtures';
import { applyVariants } from './CampaignDetailPage';
import { SubmitProofDialog } from './SubmitProofDialog';

const screenshot = () => new File([new Uint8Array(2048)], 'shot.png', { type: 'image/png' });

function renderDialog(handler: () => Response | Promise<Response>, variantId: string | null = null) {
  const mock = mockFetch({ ...authRoutes, 'POST /me/submissions': handler });
  const result = renderWithApp(
    <SubmitProofDialog
      open
      onClose={() => undefined}
      campaign={makeCampaign()}
      experimentVariantId={variantId}
    />,
    {
      route: '/app/campaigns/autumn-launch',
      path: '/app/campaigns/:slug',
      routes: [{ path: '/app/submissions/:id', element: <p>Submission detail</p> }],
    },
  );
  return { ...result, ...mock };
}

async function fillValid(user: ReturnType<typeof userEvent.setup>) {
  const dialog = screen.getByRole('dialog');
  await user.type(within(dialog).getByLabelText(/Link to your post/), 'https://www.instagram.com/p/abc123/');
  const input = dialog.querySelector('input[type=file]') as HTMLInputElement;
  await user.upload(input, screenshot());
}

describe('SubmitProofDialog', () => {
  it('validates required fields without calling the API', async () => {
    const user = userEvent.setup();
    const { calls } = renderDialog(() => json(201, makeSubmission()));
    const dialog = await screen.findByRole('dialog', { name: 'Submit proof of your post' });
    // The only eligible profile is preselected; the platform comes from it.
    expect(within(dialog).getByLabelText(/Profile you posted from/)).toHaveValue('sa1');
    await user.click(screen.getByRole('button', { name: 'Submit proof' }));
    expect(await within(dialog).findByText('Paste the public link to your post.')).toBeInTheDocument();
    expect(within(dialog).getByText('This campaign needs a screenshot of your post.')).toBeInTheDocument();
    await user.type(within(dialog).getByLabelText(/Link to your post/), 'not a url');
    await user.click(screen.getByRole('button', { name: 'Submit proof' }));
    expect(
      await within(dialog).findByText('Enter the full link, starting with https://'),
    ).toBeInTheDocument();
    expect(calls.filter((c) => c.path === '/me/submissions')).toHaveLength(0);
    // The screenshot is described as evidence, not proof, and the live-hours rule is stated.
    expect(within(dialog).getByText(/evidence for the reviewer, not proof on its own/)).toBeInTheDocument();
    expect(within(dialog).getByText(/public for at least 48 hours/)).toBeInTheDocument();
  });

  it('maps a duplicate URL error onto the link field and keeps the form', async () => {
    const user = userEvent.setup();
    renderDialog(() =>
      problem(
        409,
        'submission.duplicate_url',
        'This post has already been submitted. Each post can only be claimed once.',
      ),
    );
    await screen.findByRole('dialog');
    await fillValid(user);
    await user.click(screen.getByRole('button', { name: 'Submit proof' }));
    const link = screen.getByLabelText(/Link to your post/);
    await waitFor(() => expect(link).toHaveAttribute('aria-invalid', 'true'));
    expect(link).toHaveAccessibleDescription(expect.stringContaining('already been submitted'));
    expect(link).toHaveValue('https://www.instagram.com/p/abc123/');
    expect(screen.getByText('shot.png')).toBeInTheDocument();
  });

  it('clears the server error of the link once the link is edited, and shows a new one after resubmitting', async () => {
    const user = userEvent.setup();
    renderDialog(() =>
      problem(
        409,
        'submission.duplicate_url',
        'This post has already been submitted. Each post can only be claimed once.',
      ),
    );
    await screen.findByRole('dialog');
    await fillValid(user);
    await user.click(screen.getByRole('button', { name: 'Submit proof' }));
    const link = screen.getByLabelText(/Link to your post/);
    await waitFor(() => expect(link).toHaveAttribute('aria-invalid', 'true'));
    // The error is shown on the field only: a persistent error toast would cover the dialog's "Submit proof" button.
    expect(screen.queryByText('Your proof wasn’t submitted')).not.toBeInTheDocument();

    await user.clear(link);
    await user.type(link, 'https://www.instagram.com/p/other456/');
    expect(link).not.toHaveAttribute('aria-invalid', 'true');
    expect(link).not.toHaveAccessibleDescription(expect.stringContaining('already been submitted'));

    await user.click(screen.getByRole('button', { name: 'Submit proof' }));
    await waitFor(() => expect(link).toHaveAttribute('aria-invalid', 'true'));
    expect(link).toHaveAccessibleDescription(expect.stringContaining('already been submitted'));
  });

  it('sends one multipart request even when submitted twice, with UTC time and variant id', async () => {
    const user = userEvent.setup();
    let resolve!: (r: Response) => void;
    const { calls } = renderDialog(() => new Promise<Response>((r) => (resolve = r)), 'variant-b');
    await screen.findByRole('dialog');
    await fillValid(user);
    const form = document.getElementById('submit-proof-form')!;
    await user.click(screen.getByRole('button', { name: 'Submit proof' }));
    await waitFor(() =>
      expect(screen.getByRole('button', { name: 'Submit proof' })).toHaveAttribute('aria-busy', 'true'),
    );
    await user.click(screen.getByRole('button', { name: 'Submit proof' }));
    fireEvent.submit(form);
    expect(calls.filter((c) => c.path === '/me/submissions')).toHaveLength(1);

    const body = calls.find((c) => c.path === '/me/submissions')!.body as FormData;
    expect(body.get('platform')).toBe('Instagram');
    expect(body.get('socialAccountId')).toBe('sa1');
    expect(body.get('experimentVariantId')).toBe('variant-b');
    expect(String(body.get('postedAt'))).toMatch(/Z$/);
    expect(body.get('screenshot')).toBeInstanceOf(File);
    resolve(json(201, makeSubmission()));
    await waitFor(() => expect(screen.getByText('Proof submitted')).toBeInTheDocument());
    expect(await screen.findByText('Submission detail')).toBeInTheDocument();
  });
});

describe('applyVariants', () => {
  it('overlays title, instructions and asset and picks the attributed variant', () => {
    const view = applyVariants(makeCampaign(), [
      {
        experimentId: 'e1',
        element: 'Title',
        variantId: 'v-title',
        key: 'B',
        title: 'New title',
        instructions: null,
        asset: null,
        landingHeadline: null,
        landingBody: null,
      },
      {
        experimentId: 'e2',
        element: 'Instructions',
        variantId: 'v-instr',
        key: 'A',
        title: null,
        instructions: 'Do this',
        asset: null,
        landingHeadline: null,
        landingBody: null,
      },
    ]);
    expect(view.title).toBe('New title');
    expect(view.instructions).toBe('Do this');
    expect(view.experimentVariantId).toBe('v-instr');
  });
});
