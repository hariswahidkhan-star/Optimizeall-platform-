import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { axeViolations } from '@/test/render';
import { Alert } from './Alert';
import { Stat } from './Stat';

describe('Stat', () => {
  it('is a group named by its label that contains the value', async () => {
    const { container } = render(
      <div>
        <Stat label="Pending" value="$6.00" measurement="Measured" />
        <Stat label="Approved" value="$12.00" />
      </div>,
    );
    const pending = screen.getByRole('group', { name: 'Pending' });
    expect(pending).toHaveTextContent('$6.00');
    expect(screen.getByRole('group', { name: 'Approved' })).toHaveTextContent('$12.00');
    expect(await axeViolations(container)).toEqual([]);
  });
});

describe('Alert', () => {
  it('with a title is a polite status named by the title', () => {
    render(<Alert title="Payouts are biweekly">Every other Monday.</Alert>);
    const status = screen.getByRole('status', { name: 'Payouts are biweekly' });
    expect(status).toHaveTextContent('Every other Monday.');
  });

  it('uses role=alert for danger and honours an explicit role', () => {
    render(
      <>
        <Alert tone="danger" title="Payment failed">
          Check the details.
        </Alert>
        <Alert tone="danger" title="Heads up" role="status">
          Static note.
        </Alert>
      </>,
    );
    expect(screen.getByRole('alert', { name: 'Payment failed' })).toBeInTheDocument();
    expect(screen.getByRole('status', { name: 'Heads up' })).toBeInTheDocument();
  });

  it('without a title stays a plain callout', () => {
    const { container } = render(<Alert>Just a note.</Alert>);
    expect(container.firstElementChild).not.toHaveAttribute('role');
    expect(container.firstElementChild).not.toHaveAttribute('aria-labelledby');
  });
});
