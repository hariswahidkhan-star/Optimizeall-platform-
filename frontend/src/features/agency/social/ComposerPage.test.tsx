import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { json, mockFetch } from '@/test/fetchMock';
import { axeViolations, renderWithApp } from '@/test/render';
import { ComposerPage } from './ComposerPage';
import { clients, presets, profile, staffSession } from './test/fixtures';

const createdPost = {
  id: 'new1',
  clientAccountId: 'c1',
  clientName: 'Nimbus Fitness',
  title: 'Autumn launch',
  status: 'Draft',
  scheduledAt: null,
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
  requiresClientApproval: false,
  isValid: true,
  allowedActions: ['submit'],
  variants: [],
  comments: [],
  createdByName: 'Sofia Social',
  createdAt: '2026-09-01T00:00:00Z',
  updatedAt: '2026-09-01T00:00:00Z',
  concurrencyStamp: 'n1',
};

function routes(validate: (body: unknown) => Response) {
  return mockFetch({
    'POST /auth/refresh': staffSession(),
    'GET /agency/social/clients': () => json(200, clients),
    'GET /agency/social/presets': () => json(200, presets),
    'GET /agency/social/clients/c1/profiles': () => json(200, [profile(), profile({ id: 'p-ig', network: 'Instagram', networkLabel: 'Instagram', handle: 'nimbus.ig' })]),
    'GET /agency/social/clients/c1/media': () => json(200, { items: [], total: 0, page: 1, pageSize: 100, totalPages: 0 }),
    'GET /agency/social/clients/c1/hashtag-sets': () => json(200, [{ id: 'h1', name: 'Core', hashtags: ['#Nimbus', '#Fit'], concurrencyStamp: 's' }]),
    'GET /agency/social/clients/c1/snippets': () => json(200, []),
    'GET /agency/social/clients/c1/campaigns': () => json(200, []),
    'POST /agency/social/validate': (req) => validate(req.body),
    'POST /agency/social/posts': () => json(201, createdPost),
    'GET /agency/social/posts/new1': () => json(200, createdPost),
  });
}

describe('ComposerPage', () => {
  it('shows live per-network counters, the preview and server-side validation', async () => {
    const user = userEvent.setup();
    const { calls } = routes(() =>
      json(200, {
        isValid: false,
        variants: [
          {
            network: 'X',
            finalText: 'x',
            textLength: 290,
            maxTextLength: 280,
            titleLength: null,
            maxTitleLength: null,
            hashtagCount: 0,
            mentionCount: 0,
            mediaCount: 0,
            isValid: false,
            issues: [{ network: 'X', field: 'text', severity: 'Error', code: 'social.text_too_long', message: 'X: text is 290 characters; the limit is 280.' }],
          },
        ],
      }),
    );
    const { container } = renderWithApp(<ComposerPage />, { route: '/agency/social/compose?client=c1', path: '/agency/social/compose' });

    await user.click(await screen.findByRole('checkbox', { name: 'X · @nimbusfit' }));
    const text = await screen.findByLabelText('X text');
    await user.type(text, 'Hello https://example.com/some/very/long/path');
    expect(screen.getAllByText('29 / 280 characters').length).toBeGreaterThan(0); // "Hello " (6) + URL counted as 23

    await user.clear(text);
    await user.click(text);
    await user.paste('a'.repeat(290));
    expect(screen.getAllByText('290 / 280 characters (over the limit)').length).toBeGreaterThan(0);
    const preview = screen.getByRole('figure', { name: 'X preview' });
    expect(within(preview).getByText(/10 over the limit/)).toBeInTheDocument();

    expect(await screen.findByText(/X: text is 290 characters; the limit is 280\./, {}, { timeout: 3000 })).toBeInTheDocument();
    expect(screen.getByText('Fix before submitting')).toBeInTheDocument();
    await waitFor(() => expect(calls.some((c) => c.path === '/agency/social/validate')).toBe(true));
    const body = calls.filter((c) => c.path === '/agency/social/validate').at(-1)!.body as { variants: { profileId: string; text: string }[] };
    expect(body.variants[0]).toMatchObject({ profileId: 'p-x' });

    // Inserting a hashtag set adds its tags to the variant.
    await user.selectOptions(screen.getByLabelText('Insert a hashtag set'), 'h1');
    expect(screen.getByLabelText('Hashtags')).toHaveValue('#Nimbus #Fit');

    expect(await axeViolations(container)).toEqual([]);
  });

  it('saves a draft with every selected network variant', async () => {
    const user = userEvent.setup();
    const { calls } = routes(() => json(200, { isValid: true, variants: [] }));
    renderWithApp(<ComposerPage />, {
      route: '/agency/social/compose?client=c1',
      path: '/agency/social/compose',
      routes: [{ path: '/agency/social/posts/:id', element: <p>Post page</p> }],
    });
    await user.type(await screen.findByLabelText(/Internal title/), 'Autumn launch');
    await user.click(await screen.findByRole('checkbox', { name: 'X · @nimbusfit' }));
    await user.click(screen.getByRole('checkbox', { name: 'Instagram · @nimbus.ig' }));
    await user.type(screen.getByLabelText('X text'), 'Autumn is here');
    await user.click(screen.getByRole('button', { name: 'Save draft' }));
    await waitFor(() => expect(calls.some((c) => c.method === 'POST' && c.path === '/agency/social/posts')).toBe(true));
    const post = calls.find((c) => c.method === 'POST' && c.path === '/agency/social/posts')!.body as {
      title: string;
      clientAccountId: string;
      variants: { profileId: string; text: string }[];
    };
    expect(post.title).toBe('Autumn launch');
    expect(post.clientAccountId).toBe('c1');
    expect(post.variants.map((v) => v.profileId)).toEqual(['p-x', 'p-ig']);
    expect(post.variants[0]!.text).toBe('Autumn is here');
  });
});
