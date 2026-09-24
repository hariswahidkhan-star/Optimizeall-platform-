import { screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { json, mockFetch } from '@/test/fetchMock';
import { renderWithApp } from '@/test/render';
import type { SegmentDefinition } from '../api/types';
import { emptyDefinition } from './SegmentBuilder';
import { SegmentCount, awaitsValues } from './SegmentPages';

const preview = { count: 12, total: 60, sample: [] };

describe('Segment live count', () => {
  it('does not ask the API to count a new segment whose rule has no values yet (it answers 400)', async () => {
    const { calls } = mockFetch({ 'POST /agency/email/segments/preview': () => json(200, preview) });
    renderWithApp(<SegmentCount clientId={null} definition={emptyDefinition()} />, { withAuth: false });
    expect(
      await screen.findByText('Choose at least one value for each rule to count matching contacts.'),
    ).toBeInTheDocument();
    expect(screen.queryByText('Rules incomplete')).not.toBeInTheDocument();
    await new Promise((r) => setTimeout(r, 600));
    expect(calls.filter((c) => c.path === '/agency/email/segments/preview')).toEqual([]);
  });

  it('counts once every rule has values', async () => {
    const definition: SegmentDefinition = {
      match: 'all',
      conditions: [{ kind: 'field', field: 'country', op: 'in', values: ['AE'] }],
      groups: [],
    };
    mockFetch({ 'POST /agency/email/segments/preview': () => json(200, preview) });
    renderWithApp(<SegmentCount clientId={null} definition={definition} />, { withAuth: false });
    expect(await screen.findByText('of 60 contacts match')).toBeInTheDocument();
  });

  it('looks inside nested groups', () => {
    const nested: SegmentDefinition = {
      match: 'all',
      conditions: [{ kind: 'tag', op: 'has', value: 'vip' }],
      groups: [emptyDefinition()],
    };
    expect(awaitsValues(nested)).toBe(true);
    expect(awaitsValues({ ...nested, groups: [] })).toBe(false);
  });
});
