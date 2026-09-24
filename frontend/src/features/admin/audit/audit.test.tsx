import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { axeViolations } from '@/test/render';
import type { AuditLogEntry } from '../api/types';
import { changedPaths, diffLines, JsonDiff } from '../shared/JsonDiff';
import { json, mockAdminApi, renderAdmin } from '../test/helpers';
import { AuditEntry } from './AuditEntry';
import { AuditLogPage } from './AuditLogPage';

const entry: AuditLogEntry = {
  id: 7,
  createdAt: '2026-09-23T10:00:00Z',
  actorUserId: 'admin-1',
  actorEmail: 'admin@optimizeall.local',
  actorDisplayName: 'Platform Admin',
  actorType: 'Admin',
  action: 'admin.setting_changed',
  entityType: 'SystemSetting',
  entityId: 'referral.program',
  before: { value: { enabled: true, referrerRewardAmount: 5, currency: 'USD' } },
  after: { value: { enabled: true, referrerRewardAmount: 7.5, currency: 'USD', note: 'new' } },
  reason: 'Q4 growth push',
  ipAddress: '127.0.0.1',
  correlationId: 'corr-1',
};

describe('JSON diff', () => {
  it('lists changed paths, including nested and added keys', () => {
    expect(changedPaths(entry.before, entry.after)).toEqual(['value.referrerRewardAmount', 'value.note']);
    expect(changedPaths({ tier: 'Standard' }, { tier: 'Gold' })).toEqual(['tier']);
    expect(changedPaths(1, 1)).toEqual([]);
  });

  it('pretty-prints and flags only the changed lines', () => {
    const lines = diffLines({ a: 1, b: { c: 2, d: 3 } }, { a: 1, b: { c: 2, d: 4 } });
    expect(lines.map((l) => l.text)).toEqual([
      '{',
      '  "a": 1,',
      '  "b": {',
      '    "c": 2,',
      '    "d": 3',
      '  }',
      '}',
    ]);
    expect(lines.filter((l) => l.changed).map((l) => l.text.trim())).toEqual(['"d": 3']);
  });

  it('renders before/after panels with highlighted keys', () => {
    const { container } = render(<JsonDiff before={entry.before} after={entry.after} />);
    expect(screen.getByText('Changed:')).toBeInTheDocument();
    expect(screen.getByText('value.referrerRewardAmount')).toBeInTheDocument();
    const before = screen.getByRole('figure', { name: 'Before' });
    const after = screen.getByRole('figure', { name: 'After' });
    const changed = (fig: HTMLElement) =>
      [...fig.querySelectorAll('[data-changed]')].map((n) => n.textContent?.replace(' (changed)', '').trim());
    expect(changed(before)).toEqual(['"referrerRewardAmount": 5,']);
    expect(changed(after)).toEqual(['"referrerRewardAmount": 7.5,', '"note": "new"']);
    expect(container.querySelectorAll('.visually-hidden')).toHaveLength(3);
  });

  it('marks everything as new when there is no "before"', () => {
    render(<JsonDiff before={null} after={{ tier: 'Gold' }} />);
    expect(screen.getByText('No data recorded.')).toBeInTheDocument();
    const after = screen.getByRole('figure', { name: 'After' });
    expect(after.querySelectorAll('[data-changed]').length).toBeGreaterThan(0);
  });
});

describe('Audit log page', () => {
  it('expands an entry to show the diff and sends filters to the API', async () => {
    const user = userEvent.setup();
    const { fn } = mockAdminApi({
      'GET /admin/audit-logs': () =>
        json(200, { items: [entry], total: 1, page: 1, pageSize: 25, totalPages: 1 }),
    });
    const { baseElement } = renderAdmin(
      <AuditLogPage />,
      '/admin/audit?action=admin.&from=2026-09-01',
      '/admin/audit',
    );
    const toggle = await screen.findByRole('button', { name: /admin\.setting_changed/ });
    expect(toggle).toHaveAttribute('aria-expanded', 'false');
    expect(screen.getByText('Q4 growth push')).toBeInTheDocument();

    const url = new URL(
      String(fn.mock.calls.find((c) => String(c[0]).includes('/admin/audit-logs'))?.[0]),
      'http://x',
    );
    expect(url.searchParams.get('action')).toBe('admin.');
    expect(url.searchParams.get('from')).toBe('2026-09-01T00:00:00Z');

    await user.click(toggle);
    expect(toggle).toHaveAttribute('aria-expanded', 'true');
    const after = screen.getByRole('figure', { name: 'After' });
    expect(within(after).getAllByText(/\(changed\)/)).toHaveLength(2);
    expect(screen.getByText('corr-1')).toBeInTheDocument();
    expect(await axeViolations(baseElement)).toEqual([]);
  });

  it('validates the actor id and date range before searching', async () => {
    const user = userEvent.setup();
    mockAdminApi({
      'GET /admin/audit-logs': () => json(200, { items: [], total: 0, page: 1, pageSize: 25, totalPages: 0 }),
    });
    renderAdmin(<AuditLogPage />, '/admin/audit', '/admin/audit');
    await screen.findByText('No entries match these filters');
    await user.type(screen.getByLabelText('Actor user id'), 'not-a-guid');
    await user.click(screen.getByRole('button', { name: 'Apply filters' }));
    expect(screen.getByLabelText('Actor user id')).toHaveAttribute('aria-invalid', 'true');
  });
});

describe('Audit entry — impersonation', () => {
  it('reads "impersonator as user" for actions taken while viewing as someone else', () => {
    render(
      <ul>
        <AuditEntry
          entry={{
            ...entry,
            actorUserId: 'u-42',
            actorEmail: 'jane@example.com',
            actorDisplayName: 'Jane Doe',
            actorType: 'impersonation',
            action: 'impersonation.request',
            impersonatorUserId: 'admin-1',
            impersonatorDisplayName: 'Platform Admin',
          }}
        />
      </ul>,
    );
    expect(screen.getByText(/Platform Admin as Jane Doe/)).toBeInTheDocument();
  });
});
