import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { DateTime } from './DateTime';

describe('DateTime with calendar dates', () => {
  it.each(['Etc/GMT+12', 'America/Los_Angeles', 'Asia/Kathmandu', 'Pacific/Kiritimati'])(
    'shows a date-only value as the same day in %s',
    (timeZone) => {
      render(
        <p data-testid="d">
          <DateTime value="2026-09-01" format="date" timeZone={timeZone} />
          {' – '}
          <DateTime value="2026-09-30" timeZone={timeZone} />
        </p>,
      );
      const text = screen.getByTestId('d').textContent ?? '';
      expect(text).toMatch(/Sep 1, 2026 – Sep 30, 2026/);
      const times = screen.getByTestId('d').querySelectorAll('time');
      expect(times[0]?.getAttribute('datetime')).toBe('2026-09-01');
      expect(times[1]?.getAttribute('datetime')).toBe('2026-09-30');
    },
  );

  it('still converts instants to the zone', () => {
    render(<DateTime value="2026-09-01T02:00:00Z" format="date" timeZone="America/Los_Angeles" />);
    expect(screen.getByText(/Aug 31, 2026/)).toBeInTheDocument();
  });
});
