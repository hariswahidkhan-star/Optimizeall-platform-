import { act, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { describe, expect, it } from 'vitest';
import { json, mockFetch } from '@/test/fetchMock';
import { axeViolations, renderWithApp } from '@/test/render';
import type { Block, FormSchema, PublicForm } from './api';
import { FormRenderer } from './FormRenderer';
import { evaluate, isVisible, submittableValues, validateFields } from './formLogic';
import { BlockList } from './PageBuilderPage';

const initialBlocks: Block[] = [
  { id: 'hero-1', type: 'hero', props: { headline: 'Launch faster', align: 'center', theme: 'brand' } },
  { id: 'features-1', type: 'features', props: { heading: 'Why teams switch', items: [{ title: 'Fast', body: 'Really fast.' }] } },
  { id: 'cta-1', type: 'cta', props: { heading: 'Ready?', buttonLabel: 'Start', buttonHref: '#form', style: 'primary' } },
];

function Harness({ onBlocks }: { onBlocks?: (b: Block[]) => void }) {
  const [blocks, setBlocks] = useState(initialBlocks);
  const [selected, setSelected] = useState<string | null>('hero-1');
  return (
    <BlockList
      blocks={blocks}
      selectedId={selected}
      onSelect={setSelected}
      onChange={(next) => {
        setBlocks(next);
        onBlocks?.(next);
      }}
    />
  );
}

const blockButtons = () =>
  within(screen.getByRole('list', { name: 'Blocks' }))
    .getAllByRole('button')
    .filter((b) => b.classList.contains('pb-block-list__select'));

describe('landing-page builder block list', () => {
  it('reorders blocks with Alt+Arrow keys, keeps focus on the moved block and announces the move', async () => {
    const user = userEvent.setup();
    let latest: Block[] = initialBlocks;
    renderWithApp(<Harness onBlocks={(b) => (latest = b)} />, { withAuth: false });

    const features = blockButtons()[1];
    expect(features).toHaveTextContent('2. Features');
    features.focus();
    await user.keyboard('{Alt>}{ArrowUp}{/Alt}');

    expect(latest.map((b) => b.id)).toEqual(['features-1', 'hero-1', 'cta-1']);
    const moved = blockButtons()[0];
    expect(moved).toHaveTextContent('1. Features');
    await waitFor(() => expect(moved).toHaveFocus());
    expect(screen.getByRole('status')).toHaveTextContent('Features moved to position 1 of 3.');

    // Alt+ArrowDown twice moves it to the end; a further press at the end is a no-op.
    await user.keyboard('{Alt>}{ArrowDown}{/Alt}');
    await user.keyboard('{Alt>}{ArrowDown}{/Alt}');
    await user.keyboard('{Alt>}{ArrowDown}{/Alt}');
    expect(latest.map((b) => b.id)).toEqual(['hero-1', 'cta-1', 'features-1']);
    await waitFor(() => expect(blockButtons()[2]).toHaveFocus());

    // Plain arrow keys (without Alt) do not reorder.
    await user.keyboard('{ArrowUp}');
    expect(latest.map((b) => b.id)).toEqual(['hero-1', 'cta-1', 'features-1']);
  });

  it('adds a block of the chosen type after the selected block using only the keyboard', async () => {
    const user = userEvent.setup();
    let latest: Block[] = initialBlocks;
    renderWithApp(<Harness onBlocks={(b) => (latest = b)} />, { withAuth: false });

    await user.selectOptions(screen.getByLabelText('New block type'), 'faq');
    screen.getByRole('button', { name: 'Add block' }).focus();
    await user.keyboard('{Enter}');

    expect(latest.map((b) => b.type)).toEqual(['hero', 'faq', 'features', 'cta']);
    expect(blockButtons()[1]).toHaveTextContent('2. FAQ');
    await waitFor(() => expect(blockButtons()[1]).toHaveFocus());
    expect(screen.getByRole('status')).toHaveTextContent('FAQ block added at position 2 of 4.');
  });

  it('moves and removes blocks with the labelled buttons', async () => {
    const user = userEvent.setup();
    let latest: Block[] = initialBlocks;
    renderWithApp(<Harness onBlocks={(b) => (latest = b)} />, { withAuth: false });

    expect(screen.getByRole('button', { name: 'Move Hero up' })).toBeDisabled();
    await user.click(screen.getByRole('button', { name: 'Move Hero down' }));
    expect(latest.map((b) => b.id)).toEqual(['features-1', 'hero-1', 'cta-1']);
    await user.click(screen.getByRole('button', { name: 'Remove Call to action' }));
    expect(latest.map((b) => b.id)).toEqual(['features-1', 'hero-1']);
  });

  it('has no axe violations', async () => {
    const { container } = renderWithApp(<Harness />, { withAuth: false });
    expect(await axeViolations(container)).toEqual([]);
  });
});

const schema: FormSchema = {
  steps: [
    {
      id: 'about',
      title: 'About you',
      fields: [
        { key: 'name', type: 'text', label: 'Full name', required: true },
        { key: 'email', type: 'email', label: 'Work email', required: true },
        {
          key: 'interest',
          type: 'select',
          label: 'What do you need?',
          required: true,
          options: [
            { value: 'quote', label: 'A quote' },
            { value: 'demo', label: 'A demo' },
          ],
        },
        { key: 'budget', type: 'number', label: 'Monthly budget', required: true, showIf: { field: 'interest', operator: 'equals', value: 'quote' } },
        { key: 'budget_notes', type: 'text', label: 'Budget notes', showIf: { field: 'budget', operator: 'greaterThan', value: '1000' } },
        { key: 'campaign', type: 'hidden', label: 'Campaign', urlParam: 'utm_campaign' },
      ],
    },
  ],
};

const publicForm: PublicForm = {
  id: 'f1',
  name: 'Contact',
  schema,
  submitLabel: 'Send',
  successMessage: 'Thanks — we will reply within one business day.',
  redirectUrl: null,
  consentText: null,
  consentVersion: 1,
  captcha: null,
  token: 'signed-token',
};

describe('form conditional logic', () => {
  it('evaluates operators like the server (case-insensitive, numeric comparisons, lists)', () => {
    expect(evaluate({ field: 'x', operator: 'equals', value: 'Quote' }, ['quote'])).toBe(true);
    expect(evaluate({ field: 'x', operator: 'notEquals', value: 'quote' }, [])).toBe(true);
    expect(evaluate({ field: 'x', operator: 'in', values: ['a', 'b'] }, ['B'])).toBe(true);
    expect(evaluate({ field: 'x', operator: 'contains', value: 'seo' }, ['Local SEO'])).toBe(true);
    expect(evaluate({ field: 'x', operator: 'greaterThan', value: '10' }, ['9'])).toBe(false);
    expect(evaluate({ field: 'x', operator: 'lessThan', value: '10' }, ['9'])).toBe(true);
    expect(evaluate({ field: 'x', operator: 'isEmpty' }, [])).toBe(true);
  });

  it('collapses chained conditions when the controlling field is hidden', () => {
    const [, , , budget, notes] = schema.steps[0].fields;
    const values = { interest: 'demo', budget: '5000' };
    expect(isVisible(budget, values, schema)).toBe(false);
    // budget has a value but is hidden, so budget_notes (budget > 1000) is hidden too.
    expect(isVisible(notes, values, schema)).toBe(false);
    expect(isVisible(notes, { ...values, interest: 'quote' }, schema)).toBe(true);
  });

  it('skips hidden fields in validation and strips them from the submitted values', () => {
    const fields = schema.steps[0].fields;
    const values = { name: 'Ada', email: 'ada@example.com', interest: 'demo', budget: '5000', budget_notes: 'stale' };
    expect(validateFields(fields, values, {}, schema)).toEqual({});
    expect(validateFields(fields, { ...values, interest: 'quote', budget: '' }, {}, schema)).toHaveProperty('budget');
    const submitted = submittableValues(schema, values);
    expect(submitted).not.toHaveProperty('budget');
    expect(submitted).not.toHaveProperty('budget_notes');
    expect(submitted).toMatchObject({ name: 'Ada', interest: 'demo' });
  });

  it('shows dependent fields as answers change and submits only visible values', async () => {
    const user = userEvent.setup();
    const { calls } = mockFetch({
      'POST /public/forms/f1/submissions': () => json(200, { ok: true, message: 'Thanks — we will reply within one business day.', redirectUrl: null }),
    });
    window.history.replaceState(null, '', '/lp/acme/offer?utm_campaign=spring');
    const { container } = renderWithApp(<FormRenderer form={publicForm} landingPageId="p1" variantKey="B" />, { withAuth: false });
    window.history.replaceState(null, '', '/');

    expect(screen.queryByLabelText(/Monthly budget/)).not.toBeInTheDocument();
    await user.selectOptions(screen.getByLabelText(/What do you need/), 'quote');
    const budget = await screen.findByLabelText(/Monthly budget/);
    expect(screen.queryByLabelText(/Budget notes/)).not.toBeInTheDocument();
    await user.type(budget, '2500');
    expect(await screen.findByLabelText(/Budget notes/)).toBeInTheDocument();

    // Switching away hides both dependants again.
    await user.selectOptions(screen.getByLabelText(/What do you need/), 'demo');
    expect(screen.queryByLabelText(/Monthly budget/)).not.toBeInTheDocument();
    expect(screen.queryByLabelText(/Budget notes/)).not.toBeInTheDocument();

    await user.type(screen.getByLabelText(/Full name/), 'Ada Lovelace');
    await user.type(screen.getByLabelText(/Work email/), 'ada@example.com');
    expect(await axeViolations(container)).toEqual([]);
    await user.click(screen.getByRole('button', { name: 'Send' }));

    expect(await screen.findByText('Thanks — we will reply within one business day.')).toBeInTheDocument();
    const post = calls.find((c) => c.method === 'POST');
    const body = post?.body as { values: Record<string, unknown>; token: string; landingPageId: string; variantKey: string; utmCampaign: string; hp: string };
    expect(body.values).toEqual({ name: 'Ada Lovelace', email: 'ada@example.com', interest: 'demo', campaign: 'spring' });
    expect(body).toMatchObject({ token: 'signed-token', landingPageId: 'p1', variantKey: 'B', utmCampaign: 'spring', hp: '' });
  });

  it('blocks submission and reports required fields without calling the API', async () => {
    const user = userEvent.setup();
    const { calls } = mockFetch({});
    renderWithApp(<FormRenderer form={publicForm} />, { withAuth: false });
    await act(async () => {
      await user.click(screen.getByRole('button', { name: 'Send' }));
    });
    expect(await screen.findAllByText(/required|Enter/i)).not.toHaveLength(0);
    expect(calls.filter((c) => c.method === 'POST')).toHaveLength(0);
  });
});
