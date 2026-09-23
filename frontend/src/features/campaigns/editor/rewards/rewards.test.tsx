import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { describe, expect, it } from 'vitest';
import { json, mockFetch, problem } from '@/test/fetchMock';
import { axeViolations, renderWithApp } from '@/test/render';
import type { RewardQuote } from '../../api/types';
import { makeRuleSet } from '../../test/fixtures';
import { emptyRules, rulesToInput, type RulesForm } from '../formModel';
import { RewardPreview } from './RewardPreview';
import { RulesBuilder } from './RulesBuilder';

function Harness({ initial = emptyRules() }: { initial?: RulesForm }) {
  const [value, setValue] = useState(initial);
  return <RulesBuilder value={value} onChange={setValue} timeZone="UTC" />;
}

describe('RulesBuilder', () => {
  it('hints that exactly one base rate is required', async () => {
    const user = userEvent.setup();
    renderWithApp(<Harness />, { withAuth: false });
    expect(await screen.findByText('Base rate', { selector: 'span' })).toBeInTheDocument();
    expect(screen.queryByText('Add exactly one base rate.')).not.toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: /Remove Base rate rule 1/ }));
    expect(screen.getByText('Add exactly one base rate.')).toBeInTheDocument();

    // Re-adding brings the base rate back and the hint disappears.
    await user.click(screen.getByRole('button', { name: 'Base rate' }));
    expect(screen.queryByText('Add exactly one base rate.')).not.toBeInTheDocument();
  });

  it('shows per-rule hints and lets overrides be added and reordered', async () => {
    const user = userEvent.setup();
    renderWithApp(<Harness />, { withAuth: false });
    await user.click(await screen.findByRole('button', { name: 'Rate override' }));
    expect(screen.getByText(/An override needs at least one condition/)).toBeInTheDocument();
    const rules = screen.getByRole('list', { name: 'Reward rules' });
    expect(within(rules).getAllByRole('listitem')).toHaveLength(2);
    await user.click(screen.getByRole('button', { name: /Move Rate override rule 2 up/ }));
    expect(within(rules).getAllByRole('listitem')[0]).toHaveTextContent('Rate override');
  });

  it('has no axe violations', async () => {
    const { container } = renderWithApp(<Harness />, { withAuth: false });
    await screen.findByRole('list', { name: 'Reward rules' });
    expect(await axeViolations(container)).toEqual([]);
  });
});

const quote: RewardQuote = {
  ruleSetId: null,
  ruleSetVersion: null,
  currency: 'USD',
  lines: [
    {
      type: 'PostReward',
      ruleId: 'r2',
      amount: 2,
      uncappedAmount: 7,
      requiresApproval: false,
      label: 'TikTok PK',
    },
    {
      type: 'QualityBonus',
      ruleId: 'r5',
      amount: 0,
      uncappedAmount: 4,
      requiresApproval: true,
      label: 'Quality bonus',
    },
  ],
  total: 2,
  appliedCaps: ['daily_cap'],
  ruleSetSummary: 'v0 USD: base 5.00; 1 override; daily cap 20.00',
};

describe('RewardPreview', () => {
  it('sends the draft scenario and renders lines, caps and the total', async () => {
    const user = userEvent.setup();
    const { calls } = mockFetch({ 'POST /admin/campaigns/c1/reward-rules/preview': () => json(200, quote) });
    const draft = rulesToInput(emptyRules(), 'UTC');
    const { container } = renderWithApp(
      <RewardPreview campaignId="c1" draft={draft} versions={[makeRuleSet()]} timeZone="UTC" />,
      { withAuth: false },
    );
    await user.selectOptions(screen.getByLabelText('Platform'), 'TikTok');
    await user.clear(screen.getByLabelText('Country'));
    await user.type(screen.getByLabelText('Country'), 'pk');
    await user.click(screen.getByRole('button', { name: 'Calculate' }));

    const result = await screen.findByTestId('reward-quote');
    const body = calls.find((c) => c.path.endsWith('/preview'))!.body as Record<string, unknown>;
    expect(body).toMatchObject({ platform: 'TikTok', countryCode: 'PK', draft });
    expect(within(result).getByText('TikTok PK')).toBeInTheDocument();
    expect(within(result).getByText('Needs approval')).toBeInTheDocument();
    expect(within(result).getByText('$7.00')).toBeInTheDocument();
    expect(within(result).getByText('Total').parentElement).toHaveTextContent('$2.00');
    expect(within(result).getByText('Daily cap per participant')).toBeInTheDocument();
    expect(await axeViolations(container)).toEqual([]);
  });

  it('prices a saved version and surfaces server rule errors', async () => {
    const user = userEvent.setup();
    const { calls } = mockFetch({
      'POST /admin/campaigns/c1/reward-rules/preview': () =>
        problem(400, 'reward.invalid_rules', 'The reward rules are invalid.', {
          errors: { rules: ['Exactly one base rate is required.'] },
        }),
    });
    renderWithApp(
      <RewardPreview
        campaignId="c1"
        draft={rulesToInput(emptyRules(), 'UTC')}
        versions={[makeRuleSet()]}
        timeZone="UTC"
      />,
      { withAuth: false },
    );
    await user.selectOptions(screen.getByLabelText('Price with'), '1');
    await user.click(screen.getByRole('button', { name: 'Calculate' }));
    expect(await screen.findByText('Exactly one base rate is required.')).toBeInTheDocument();
    const body = calls[0]!.body as Record<string, unknown>;
    expect(body.ruleSetVersion).toBe(1);
    expect(body.draft).toBeUndefined();
  });
});
