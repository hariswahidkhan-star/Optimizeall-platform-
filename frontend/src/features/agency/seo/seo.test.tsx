import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { axeViolations, renderWithApp } from '@/test/render';
import type { AuditIssue } from './api';
import { AuditIssueGroups } from './AuditIssueGroups';

const issue = (ruleKey: string, severity: AuditIssue['severity'], title: string, urls: string[]): AuditIssue => ({
  ruleKey,
  title,
  category: 'On-page',
  severity,
  whyItMatters: `${title} hurts rankings.`,
  howToFix: `Fix ${title.toLowerCase()}.`,
  affectedCount: urls.length,
  hits: urls.map((url) => ({ url, detail: null })),
});

// Deliberately out of order: the component must group by severity with errors first.
const issues: AuditIssue[] = [
  issue('missing_meta_description', 'Warning', 'Missing meta description', ['https://nimbus.test/a', 'https://nimbus.test/b']),
  issue('broken_internal_link', 'Error', 'Broken internal links', ['https://nimbus.test/old']),
  issue('missing_alt', 'Notice', 'Images without alt text', ['https://nimbus.test/gallery']),
  issue('missing_title', 'Error', 'Missing title tag', ['https://nimbus.test/x', 'https://nimbus.test/y', 'https://nimbus.test/z']),
  issue(
    'thin_content',
    'Warning',
    'Thin content',
    Array.from({ length: 25 }, (_, i) => `https://nimbus.test/p${i}`),
  ),
];

describe('audit results grouping', () => {
  it('groups issues by severity (errors, warnings, notices) with issue and URL totals', () => {
    renderWithApp(<AuditIssueGroups issues={issues} />, { withAuth: false });
    const headings = screen.getAllByRole('heading', { level: 2 });
    expect(headings.map((h) => h.textContent)).toEqual([
      'Errors2 issues · 4 URLs',
      'Warnings2 issues · 27 URLs',
      'Notices1 issue · 1 URL',
    ]);
    const errors = screen.getByRole('region', { name: /Errors/ });
    expect(within(errors).getAllByRole('button').map((b) => b.textContent)).toEqual([
      expect.stringContaining('Broken internal links'),
      expect.stringContaining('Missing title tag'),
    ]);
  });

  it('expands an issue to show why it matters, how to fix and the affected URLs (first 20, then all)', async () => {
    const user = userEvent.setup();
    renderWithApp(<AuditIssueGroups issues={issues} />, { withAuth: false });
    const toggle = screen.getByRole('button', { name: /Thin content/ });
    expect(toggle).toHaveAttribute('aria-expanded', 'false');
    await user.click(toggle);
    expect(toggle).toHaveAttribute('aria-expanded', 'true');
    expect(screen.getByText('Fix thin content.')).toBeVisible();
    const list = screen.getByRole('list', { name: 'Affected URLs for Thin content' });
    expect(within(list).getAllByRole('listitem')).toHaveLength(20);
    await user.click(screen.getByRole('button', { name: 'Show all 25 URLs' }));
    expect(within(list).getAllByRole('listitem')).toHaveLength(25);
  });

  it('shows an empty state when nothing failed, and has no axe violations', async () => {
    const { container, unmount } = renderWithApp(<AuditIssueGroups issues={issues} />, { withAuth: false });
    await userEvent.setup().click(screen.getByRole('button', { name: /Broken internal links/ }));
    expect(await axeViolations(container)).toEqual([]);
    unmount();
    renderWithApp(<AuditIssueGroups issues={[]} />, { withAuth: false });
    expect(screen.getByText('No issues found')).toBeInTheDocument();
  });
});
