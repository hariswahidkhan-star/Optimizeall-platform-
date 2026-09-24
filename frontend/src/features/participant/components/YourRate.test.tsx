import { screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { axeViolations, renderWithApp } from '@/test/render';
import type { YourRate } from '../api/types';
import { YourRateCard, YourRateInline } from './YourRate';

const personal: YourRate = {
  currency: 'USD',
  minAmount: 11,
  maxAmount: 16,
  kind: 'Personal',
  validTo: '2026-09-30T00:00:00Z',
  entries: [
    { platform: 'Instagram', format: null, amount: 11 },
    { platform: 'Instagram', format: 'ShortVideo', amount: 16 },
  ],
};

describe('YourRate', () => {
  it('shows the participant their own personal deal per platform and format, with its expiry', async () => {
    const { container } = renderWithApp(<YourRateCard rate={personal} />, { withAuth: false });
    expect(screen.getByRole('heading', { name: 'Your personal rate' })).toBeInTheDocument();
    expect(screen.getByText(/You have a personal deal/)).toBeInTheDocument();
    expect(screen.getByText('Instagram · reel / short')).toBeInTheDocument();
    expect(screen.getByText(/posts made until/)).toBeInTheDocument();
    expect(await axeViolations(container)).toEqual([]);
  });

  it('calls a group rate a special rate and shows a range inline', () => {
    renderWithApp(<YourRateInline rate={{ ...personal, kind: 'Special', validTo: null }} />, {
      withAuth: false,
    });
    expect(screen.getByText(/Your rate:/)).toBeInTheDocument();
    expect(screen.queryByText(/until/)).not.toBeInTheDocument();
  });
});
