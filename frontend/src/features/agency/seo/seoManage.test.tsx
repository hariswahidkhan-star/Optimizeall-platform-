import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { json, mockFetch, problem } from '@/test/fetchMock';
import { axeViolations, renderWithApp } from '@/test/render';
import type { AuditIssue, AuditRule, Backlink, CitationSource, Site } from './api';
import { AuditIssueGroups, IgnoreIssueDialog } from './AuditIssueGroups';
import { BacklinkDialog } from './BacklinksPanel';
import { DirectoryDialog, RuleDialog } from './SeoSettingsPage';

const issue: AuditIssue = {
  ruleKey: 'h1.missing',
  title: 'Missing H1 heading',
  category: 'On-page',
  severity: 'Warning',
  whyItMatters: 'Headings help.',
  howToFix: 'Add one H1.',
  affectedCount: 1,
  hits: [{ url: 'https://nimbus.test/', detail: null }],
  status: 'Open',
};

const rule: AuditRule = {
  key: 'h1.missing',
  title: 'Missing H1 heading',
  category: 'On-page',
  severity: 'Warning',
  defaultSeverity: 'Warning',
  whyItMatters: 'Headings help.',
  howToFix: 'Add one H1.',
  isEnabled: true,
  isCustomized: false,
  concurrencyStamp: 'r1',
};

describe('SEO issue triage', () => {
  it('requires a reason to ignore an issue and sends it with the status', async () => {
    const user = userEvent.setup();
    const onSaved = vi.fn();
    const { calls } = mockFetch({
      'POST /agency/seo/audits/a1/issues/h1.missing/status': () =>
        json(200, { ...issue, status: 'Ignored', statusNote: 'Design choice' }),
    });
    const { container } = renderWithApp(
      <IgnoreIssueDialog auditId="a1" issue={issue} onClose={() => {}} onSaved={onSaved} />,
      { withAuth: false },
    );
    const dialog = await screen.findByRole('dialog');
    await user.click(within(dialog).getByRole('button', { name: 'Save' }));
    expect(await within(dialog).findByText('Add a short reason so the team knows why.')).toBeInTheDocument();
    expect(calls.some((c) => c.method === 'POST')).toBe(false);
    expect(await axeViolations(container)).toEqual([]);

    await user.type(within(dialog).getByLabelText(/Why is it ignored/), 'Design choice');
    await user.click(within(dialog).getByRole('button', { name: 'Save' }));
    await waitFor(() => expect(onSaved).toHaveBeenCalled());
    expect(calls.find((c) => c.method === 'POST')?.body).toEqual({
      status: 'Ignored',
      note: 'Design choice',
    });
  });

  it('marks an open issue fixed from the drill-down and explains when triage is unavailable', async () => {
    const user = userEvent.setup();
    const onChanged = vi.fn();
    mockFetch({
      'POST /agency/seo/audits/a1/issues/h1.missing/status': () => json(200, { ...issue, status: 'Fixed' }),
    });
    const { unmount } = renderWithApp(
      <AuditIssueGroups issues={[issue]} auditId="a1" onChanged={onChanged} />,
      { withAuth: false },
    );
    await user.click(screen.getByRole('button', { name: /Missing H1 heading/ }));
    await user.click(screen.getByRole('button', { name: 'Mark fixed' }));
    await waitFor(() => expect(onChanged).toHaveBeenCalledWith(expect.objectContaining({ status: 'Fixed' })));
    unmount();

    renderWithApp(
      <AuditIssueGroups
        issues={[issue]}
        auditId="a1"
        triageDisabledReason="Issues can be marked fixed or ignored once the audit has completed."
      />,
      {
        withAuth: false,
      },
    );
    await user.click(screen.getByRole('button', { name: /Missing H1 heading/ }));
    expect(
      screen.getByText('Issues can be marked fixed or ignored once the audit has completed.'),
    ).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Mark fixed' })).not.toBeInTheDocument();
  });
});

describe('SEO settings dialogs', () => {
  it('saves a rule with its concurrency stamp and shows a friendly conflict message', async () => {
    const user = userEvent.setup();
    const { calls } = mockFetch({
      'PUT /agency/seo/rules/h1.missing': () =>
        problem(409, 'concurrency.conflict', 'This rule was changed by someone else. Reload and try again.'),
    });
    const { container } = renderWithApp(<RuleDialog rule={rule} onClose={() => {}} onSaved={() => {}} />, {
      withAuth: false,
    });
    const dialog = await screen.findByRole('dialog');
    await user.selectOptions(within(dialog).getByLabelText(/Severity/), 'Error');
    await user.click(within(dialog).getByRole('checkbox', { name: /Check this rule in audits/ }));
    await user.click(within(dialog).getByRole('button', { name: 'Save' }));
    expect(
      await within(dialog).findByText('This rule was changed by someone else. Reload and try again.'),
    ).toBeInTheDocument();
    expect(calls.find((c) => c.method === 'PUT')?.body).toMatchObject({
      severity: 'Error',
      isEnabled: false,
      concurrencyStamp: 'r1',
    });
    expect(await axeViolations(container)).toEqual([]);
  });

  it('adds a directory with parsed countries', async () => {
    const user = userEvent.setup();
    const created: CitationSource = {
      id: 'd1',
      key: 'custom-1',
      name: 'Local Guide',
      url: 'https://guide.example/',
      category: 'Local',
      countries: ['GB'],
      sortOrder: 5,
      isActive: true,
      isCustom: true,
      citationCount: 0,
      concurrencyStamp: 's',
    };
    const onSaved = vi.fn();
    const { calls } = mockFetch({ 'POST /agency/seo/citation-sources': () => json(200, created) });
    const { container } = renderWithApp(
      <DirectoryDialog source={null} onClose={() => {}} onSaved={onSaved} />,
      { withAuth: false },
    );
    const dialog = await screen.findByRole('dialog');
    await user.type(within(dialog).getByLabelText(/Name/), 'Local Guide');
    const url = within(dialog).getByLabelText(/Website/);
    await user.clear(url);
    await user.type(url, 'https://guide.example/');
    await user.type(within(dialog).getByLabelText(/Countries/), 'gb, us');
    expect(await axeViolations(container)).toEqual([]);
    await user.click(within(dialog).getByRole('button', { name: 'Save' }));
    await waitFor(() => expect(onSaved).toHaveBeenCalled());
    expect(calls.find((c) => c.method === 'POST')?.body).toMatchObject({
      name: 'Local Guide',
      countries: ['gb', 'us'],
      isActive: true,
    });
  });

  it('edits a backlink with PUT', async () => {
    const user = userEvent.setup();
    const site = { id: 's1', baseUrl: 'https://nimbus.test/' } as Site;
    const backlink: Backlink = {
      id: 'b1',
      siteId: 's1',
      sourceUrl: 'https://blog.test/a',
      sourceDomain: 'blog.test',
      targetUrl: 'https://nimbus.test/',
      anchorText: 'nimbus',
      rel: null,
      firstSeenAt: '2026-09-01T00:00:00Z',
      lastCheckedAt: null,
      status: 'Unchecked',
      lastStatusCode: null,
      checkMessage: null,
    };
    const { calls } = mockFetch({ 'PUT /agency/seo/backlinks/b1': () => json(200, backlink) });
    renderWithApp(<BacklinkDialog site={site} backlink={backlink} onClose={() => {}} onSaved={() => {}} />, {
      withAuth: false,
    });
    const dialog = await screen.findByRole('dialog', { name: 'Edit backlink' });
    await user.type(within(dialog).getByLabelText(/Rel attribute/), 'nofollow');
    await user.click(within(dialog).getByRole('button', { name: 'Save' }));
    await waitFor(() =>
      expect(calls.find((c) => c.method === 'PUT')?.body).toMatchObject({
        sourceUrl: 'https://blog.test/a',
        rel: 'nofollow',
      }),
    );
  });
});
