import { screen, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { axeViolations, renderWithApp } from '@/test/render';
import type { ExperimentResults } from '../api/types';
import { ExperimentResultsView } from './ExperimentResultsPage';

const notEnough: ExperimentResults = {
  experimentId: 'e1',
  status: 'Running',
  metric: 'submissionRate',
  measurement: 'measured',
  variants: [
    {
      variantId: 'va',
      key: 'A',
      name: 'Control',
      weight: 50,
      assigned: 40,
      submissions: 4,
      approved: 2,
      totalSubmissions: 4,
      totalApproved: 2,
      submissionRate: 0.1,
      approvalRate: 0.5,
    },
    {
      variantId: 'vb',
      key: 'B',
      name: 'Benefit',
      weight: 50,
      assigned: 38,
      submissions: 9,
      approved: 6,
      totalSubmissions: 10,
      totalApproved: 6,
      submissionRate: 0.2368,
      approvalRate: null,
    },
  ],
  comparisons: [
    {
      variantKey: 'B',
      controlKey: 'A',
      absoluteLift: 0.1368,
      relativeLift: 1.368,
      zScore: 1.61,
      pValue: 0.0214,
      significant: false,
      note: 'Not enough data for a reliable conclusion',
    },
  ],
  method: 'Two-sided two-proportion z-test at the 5% level.',
};

describe('ExperimentResultsView', () => {
  it('shows the server numbers and never claims significance the API did not report', async () => {
    const { container } = renderWithApp(<ExperimentResultsView results={notEnough} />, { withAuth: false });
    const comparisons = await screen.findByRole('table', { name: 'Comparisons with the control variant' });
    // p < 0.05 here, but the server said not significant (too few assignments): we follow the server.
    expect(within(comparisons).getByText('Not significant')).toBeInTheDocument();
    expect(within(comparisons).queryByText(/^Significant$/)).not.toBeInTheDocument();
    expect(within(comparisons).getByText('0.0214')).toBeInTheDocument();
    expect(within(comparisons).getByText('+13.68 pp')).toBeInTheDocument();
    expect(within(comparisons).getByText('136.8%')).toBeInTheDocument();
    expect(screen.getByRole('status')).toHaveTextContent('Not enough data for a reliable conclusion');

    const variants = screen.getByRole('table', { name: 'Results per variant' });
    expect(within(variants).getByText('23.7%')).toBeInTheDocument();
    expect(within(variants).getAllByText('—').length).toBeGreaterThan(0); // null approval rate
    expect(screen.getByRole('img', { name: /Submission rate by variant/ })).toBeInTheDocument();
    expect(await axeViolations(container)).toEqual([]);
  });

  it('marks significance only when the server does', () => {
    renderWithApp(
      <ExperimentResultsView
        results={{
          ...notEnough,
          comparisons: [
            { ...notEnough.comparisons[0]!, significant: true, note: 'Statistically significant.' },
          ],
        }}
      />,
      { withAuth: false },
    );
    const comparisons = screen.getByRole('table', { name: 'Comparisons with the control variant' });
    expect(within(comparisons).getByText('Significant')).toBeInTheDocument();
    expect(screen.queryByText('No significant result')).not.toBeInTheDocument();
  });
});
