import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { json, mockFetch, problem } from '@/test/fetchMock';
import { axeViolations, renderWithApp } from '@/test/render';
import type { AdminCampaign } from '../api/types';
import { MANAGER_PERMISSIONS, makeCampaign, makeRuleSet, managerSession } from '../test/fixtures';
import { CampaignEditorPage } from './CampaignEditorPage';

function setup(
  campaign: AdminCampaign | (() => AdminCampaign),
  extra: Parameters<typeof mockFetch>[0] = {},
  permissions = MANAGER_PERMISSIONS,
) {
  const get = typeof campaign === 'function' ? campaign : () => campaign;
  const mocks = mockFetch({
    'POST /auth/refresh': managerSession(permissions),
    'GET /admin/campaigns/c1': () => json(200, get()),
    'GET /admin/campaigns/c1/reward-rules': () => json(200, [makeRuleSet()]),
    'GET /admin/campaign-categories': () => json(200, []),
    'GET /marketing/templates': () =>
      json(200, { items: [], total: 0, page: 1, pageSize: 200, totalPages: 0 }),
    ...extra,
  });
  const view = renderWithApp(<CampaignEditorPage />, {
    route: '/manage/campaigns/c1',
    path: '/manage/campaigns/:campaignId',
  });
  return { ...mocks, ...view };
}

describe('CampaignEditorPage', () => {
  it('requires a reason before saving reward rules as a new version', async () => {
    const user = userEvent.setup();
    const { calls } = setup(makeCampaign(), {
      'POST /admin/campaigns/c1/reward-rules': () =>
        json(201, makeRuleSet({ id: 'rs2', version: 2, summary: 'v2 USD: base 6.00' })),
    });
    await user.click(await screen.findByRole('tab', { name: 'Rewards' }));
    expect(screen.getByText(/Existing submissions keep the version/)).toBeInTheDocument();

    const amount = screen.getAllByLabelText(/^Amount \(USD\)/)[0]!;
    await user.clear(amount);
    await user.type(amount, '6');
    await user.click(screen.getByRole('button', { name: 'Save as new version' }));

    const dialog = await screen.findByRole('alertdialog', { name: /Save reward rules as a new version/ });
    expect(within(dialog).getByText(/Existing submissions keep their original version/)).toBeInTheDocument();
    await user.click(within(dialog).getByRole('button', { name: 'Save new version' }));
    expect(within(dialog).getByText(/Enter a reason/)).toBeInTheDocument();
    expect(calls.some((c) => c.method === 'POST' && c.path.endsWith('/reward-rules'))).toBe(false);

    await user.type(within(dialog).getByLabelText(/Reason for the change/), 'Rate increase for launch');
    await user.click(within(dialog).getByRole('button', { name: 'Save new version' }));
    await screen.findByText('Reward rules saved as version 2');
    const post = calls.find((c) => c.method === 'POST' && c.path.endsWith('/reward-rules'))!;
    expect(post.body).toMatchObject({
      confirm: true,
      reason: 'Rate increase for launch',
      currency: 'USD',
      rules: [{ type: 'BaseRate', amount: 6 }],
      baseVersion: 1,
    });

    // The next change builds on the version just saved.
    await user.clear(amount);
    await user.type(amount, '7');
    await user.click(screen.getByRole('button', { name: 'Save as new version' }));
    const again = await screen.findByRole('alertdialog', { name: /Save reward rules as a new version/ });
    await user.type(within(again).getByLabelText(/Reason for the change/), 'Second increase');
    await user.click(within(again).getByRole('button', { name: 'Save new version' }));
    await waitFor(() =>
      expect(calls.filter((c) => c.method === 'POST' && c.path.endsWith('/reward-rules'))).toHaveLength(2),
    );
    expect(
      calls.filter((c) => c.method === 'POST' && c.path.endsWith('/reward-rules'))[1]!.body,
    ).toMatchObject({
      baseVersion: 2,
    });
  });

  it('tells the manager when someone else saved newer reward rules in the meantime', async () => {
    const user = userEvent.setup();
    setup(makeCampaign(), {
      'POST /admin/campaigns/c1/reward-rules': () =>
        problem(
          409,
          'reward.version_conflict',
          'Someone else saved new reward rules (version 2) after you opened version 1. Reload to see them, then make your change again.',
        ),
    });
    await user.click(await screen.findByRole('tab', { name: 'Rewards' }));
    const amount = screen.getAllByLabelText(/^Amount \(USD\)/)[0]!;
    await user.clear(amount);
    await user.type(amount, '6');
    await user.click(screen.getByRole('button', { name: 'Save as new version' }));
    const dialog = await screen.findByRole('alertdialog', { name: /Save reward rules as a new version/ });
    await user.type(within(dialog).getByLabelText(/Reason for the change/), 'Rate increase for launch');
    await user.click(within(dialog).getByRole('button', { name: 'Save new version' }));
    expect(await within(dialog).findByRole('alert')).toHaveTextContent(/Someone else saved new reward rules/);
  });

  it('offers a reload when the save hits a concurrency conflict', async () => {
    const user = userEvent.setup();
    let current = makeCampaign();
    const { calls } = setup(() => current, {
      'PUT /admin/campaigns/c1': () =>
        problem(
          409,
          'concurrency.conflict',
          'This campaign was changed by someone else. Reload and try again.',
        ),
    });
    const title = await screen.findByLabelText('Title');
    await user.clear(title);
    await user.type(title, 'My edit');
    await user.click(screen.getByRole('button', { name: 'Save changes' }));

    expect(await screen.findByText('Someone else changed this campaign')).toBeInTheDocument();
    const put = calls.find((c) => c.method === 'PUT')!;
    expect(put.body).toMatchObject({ title: 'My edit', concurrencyStamp: 'stamp-1' });

    current = makeCampaign({ title: 'Their edit', concurrencyStamp: 'stamp-2' });
    await user.click(screen.getByRole('button', { name: 'Reload latest version' }));
    await waitFor(() => expect(screen.getByLabelText('Title')).toHaveValue('Their edit'));
    expect(screen.queryByText('Someone else changed this campaign')).not.toBeInTheDocument();
  });

  it('maps PascalCase server field errors onto the fields', async () => {
    const user = userEvent.setup();
    setup(makeCampaign(), {
      'PUT /admin/campaigns/c1': () =>
        problem(400, 'validation_failed', 'One or more validation errors occurred.', {
          errors: { Title: ['The Title field must be at least 3 characters.'] },
        }),
    });
    const title = await screen.findByLabelText('Title');
    await user.clear(title);
    await user.type(title, 'ab');
    await user.click(screen.getByRole('button', { name: 'Save changes' }));
    await waitFor(() => expect(screen.getByLabelText('Title')).toHaveAttribute('aria-invalid', 'true'));
    expect(screen.getAllByText('The Title field must be at least 3 characters.').length).toBeGreaterThan(0);
  });

  it('shows a business error on the field the server names', async () => {
    const user = userEvent.setup();
    setup(makeCampaign(), {
      'PUT /admin/campaigns/c1': () =>
        problem(409, 'campaign.slug_taken', 'That slug is already used by another campaign.', {
          errors: { slug: ['That slug is already used by another campaign.'] },
        }),
    });
    const title = await screen.findByLabelText('Title');
    await user.type(title, '!');
    await user.click(screen.getByRole('button', { name: 'Save changes' }));
    await waitFor(() => expect(screen.getByLabelText(/Slug/)).toHaveAttribute('aria-invalid', 'true'));
  });

  it('keeps the new stamp when the campaign saved but the disclosure overrides were refused', async () => {
    const user = userEvent.setup();
    let disclosureCalls = 0;
    const { calls } = setup(makeCampaign(), {
      'PUT /admin/campaigns/c1': (req) =>
        json(
          200,
          makeCampaign({ summary: (req.body as { summary: string }).summary, concurrencyStamp: 'stamp-2' }),
        ),
      'PUT /admin/campaigns/c1/disclosures': () => {
        disclosureCalls += 1;
        return disclosureCalls === 1
          ? problem(
              400,
              'campaign.duplicate_disclosure',
              'Each platform/country combination can only have one disclosure.',
            )
          : json(200, [{ id: 'd1', platform: 'Instagram', countryCode: null, text: '#ad IG' }]);
      },
    });
    await user.type(await screen.findByLabelText(/^Summary/), ' More.');
    await user.click(screen.getByRole('tab', { name: 'Content' }));
    await user.click(screen.getByRole('button', { name: 'Add override' }));
    await user.type(screen.getByLabelText('Disclosure text'), '#ad IG');
    await user.click(screen.getByRole('button', { name: 'Save changes' }));
    expect(
      (await screen.findAllByText('Each platform/country combination can only have one disclosure.')).length,
    ).toBeGreaterThan(0);

    // The campaign fields were saved (stamp-2): saving again must not be reported as someone else's change.
    await user.click(screen.getByRole('button', { name: 'Save changes' }));
    await waitFor(() => expect(disclosureCalls).toBe(2));
    const puts = calls.filter((c) => c.method === 'PUT' && c.path === '/admin/campaigns/c1');
    expect(puts.map((c) => (c.body as { concurrencyStamp: string }).concurrencyStamp)).toEqual([
      'stamp-1',
      'stamp-2',
    ]);
    expect(screen.queryByText('Someone else changed this campaign')).not.toBeInTheDocument();
  });

  it('uses the platform eligibility defaults from the API', async () => {
    const user = userEvent.setup();
    setup(makeCampaign(), {
      'GET /meta/eligibility-defaults': () => json(200, { minAccountAgeDays: 120, minFollowers: 50 }),
    });
    await user.click(await screen.findByRole('tab', { name: /Targeting/ }));
    const age = await screen.findByLabelText(/Minimum account age/);
    await waitFor(() => expect(age).toHaveAttribute('placeholder', 'Default: 120'));
    expect(screen.getByText('Blank = the platform default, currently 120 days.')).toBeInTheDocument();
  });

  it('asks for a reason when the budget changes', async () => {
    const user = userEvent.setup();
    const { calls } = setup(makeCampaign(), {
      'PUT /admin/campaigns/c1': (req) =>
        json(
          200,
          makeCampaign({ budgetAmount: (req.body as AdminCampaign).budgetAmount, concurrencyStamp: 's2' }),
        ),
    });
    await user.click(await screen.findByRole('tab', { name: 'Rewards' }));
    const budget = screen.getByLabelText(/Campaign budget/);
    await user.clear(budget);
    await user.type(budget, '1500');
    await user.click(screen.getByRole('button', { name: 'Save changes' }));
    const dialog = await screen.findByRole('alertdialog', { name: /Change the campaign budget/ });
    await user.type(within(dialog).getByLabelText(/Reason/), 'Extra budget approved');
    await user.click(within(dialog).getByRole('button', { name: 'Save with new budget' }));
    await screen.findByText('Campaign saved');
    expect(calls.find((c) => c.method === 'PUT')!.body).toMatchObject({
      budgetAmount: 1500,
      budgetCurrency: 'USD',
      confirm: true,
      reason: 'Extra budget approved',
    });
  });

  it('shows a publish checklist mirroring the server validation', async () => {
    const user = userEvent.setup();
    setup(makeCampaign({ platforms: [], postingInstructions: '', assets: [] }));
    await user.click(await screen.findByRole('button', { name: 'Publish' }));
    const dialog = await screen.findByRole('dialog', { name: 'Publish campaign' });
    const list = within(dialog).getByRole('list', { name: 'Readiness checklist' });
    expect(within(list).getByText('At least one platform is chosen').parentElement).toHaveTextContent(
      'missing',
    );
    expect(within(list).getByText('A base reward rate is saved').parentElement).toHaveTextContent('done');
    expect(within(dialog).getByRole('button', { name: /Schedule campaign|Publish now/ })).toBeDisabled();
  });

  it('publishes a ready campaign and lists server problems when it refuses', async () => {
    const user = userEvent.setup();
    setup(makeCampaign(), {
      'POST /admin/campaigns/c1/publish': () =>
        problem(400, 'campaign.incomplete', 'The campaign is not ready to publish.', {
          errors: { campaign: ['The submission deadline has already passed.'] },
        }),
    });
    await user.click(await screen.findByRole('button', { name: 'Publish' }));
    const dialog = await screen.findByRole('dialog', { name: 'Publish campaign' });
    await user.click(within(dialog).getByRole('button', { name: 'Schedule campaign' }));
    expect(
      await within(dialog).findByText('The submission deadline has already passed.'),
    ).toBeInTheDocument();
  });

  it('disables publishing and rule changes without the permissions', async () => {
    setup(
      makeCampaign(),
      {},
      MANAGER_PERMISSIONS.filter((p) => p !== 'campaigns.publish' && p !== 'rewards.edit'),
    );
    await waitFor(() => expect(screen.getByRole('button', { name: 'Publish' })).toBeDisabled());
    await userEvent.setup().click(screen.getByRole('tab', { name: 'Rewards' }));
    expect(screen.getByText(/changing them needs the rewards.edit permission/)).toBeInTheDocument();
    expect(screen.getByLabelText(/Campaign budget/)).toBeDisabled();
  });

  it('has no axe violations', async () => {
    const { container } = setup(makeCampaign());
    await screen.findByLabelText('Title');
    expect(await axeViolations(container)).toEqual([]);
  });
});
