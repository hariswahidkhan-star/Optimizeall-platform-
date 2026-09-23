import { AlertOctagon, AlertTriangle, ChevronDown, ChevronRight, Info } from 'lucide-react';
import { useId, useState } from 'react';
import { Badge, Button, EmptyState } from '@/components/ui';
import { SafeExternalLink } from '@/components/SafeExternalLink';
import { severityOrder, severityTone, type AuditIssue, type SeoSeverity } from './api';

const severityLabel: Record<SeoSeverity, string> = { Error: 'Errors', Warning: 'Warnings', Notice: 'Notices' };
const severityIcon: Record<SeoSeverity, typeof AlertOctagon> = { Error: AlertOctagon, Warning: AlertTriangle, Notice: Info };
const PREVIEW = 20;

/** Groups issues by severity (errors first), each with an expandable URL drill-down and the fix guidance. */
export function AuditIssueGroups({ issues }: { issues: AuditIssue[] }) {
  if (issues.length === 0)
    return <EmptyState compact headingLevel={2} title="No issues found" description="Every check passed on the crawled pages." />;
  return (
    <div className="stack seo-issue-groups">
      {severityOrder.map((severity) => {
        const group = issues.filter((i) => i.severity === severity);
        if (group.length === 0) return null;
        const Icon = severityIcon[severity];
        const affected = group.reduce((sum, i) => sum + i.affectedCount, 0);
        return (
          <section key={severity} aria-labelledby={`sev-${severity}`} className="seo-issue-group">
            <h2 id={`sev-${severity}`} className="seo-issue-group__title">
              <Icon aria-hidden="true" className={`seo-sev-icon seo-sev-icon--${severity.toLowerCase()}`} />
              {severityLabel[severity]}
              <Badge tone={severityTone[severity]}>
                {group.length} {group.length === 1 ? 'issue' : 'issues'} · {affected} {affected === 1 ? 'URL' : 'URLs'}
              </Badge>
            </h2>
            <ul className="seo-issue-list">
              {group.map((issue) => (
                <IssueItem key={issue.ruleKey} issue={issue} />
              ))}
            </ul>
          </section>
        );
      })}
    </div>
  );
}

function IssueItem({ issue }: { issue: AuditIssue }) {
  const [open, setOpen] = useState(false);
  const [showAll, setShowAll] = useState(false);
  const panelId = useId();
  const hits = showAll ? issue.hits : issue.hits.slice(0, PREVIEW);
  return (
    <li className="seo-issue">
      <button type="button" className="seo-issue__toggle" aria-expanded={open} aria-controls={panelId} onClick={() => setOpen((o) => !o)}>
        {open ? <ChevronDown aria-hidden="true" /> : <ChevronRight aria-hidden="true" />}
        <span className="seo-issue__name">{issue.title}</span>
        <span className="seo-issue__count">
          {issue.affectedCount} {issue.affectedCount === 1 ? 'URL' : 'URLs'}
        </span>
        <span className="visually-hidden"> — {issue.category}</span>
      </button>
      <div id={panelId} hidden={!open} className="seo-issue__panel">
        <p>
          <strong>Why it matters:</strong> {issue.whyItMatters}
        </p>
        <p>
          <strong>How to fix:</strong> {issue.howToFix}
        </p>
        <ul className="seo-url-list" aria-label={`Affected URLs for ${issue.title}`}>
          {hits.map((hit, index) => (
            <li key={`${hit.url}-${index}`}>
              <SafeExternalLink href={hit.url}>{hit.url}</SafeExternalLink>
              {hit.detail && <span className="text-muted text-small"> — {hit.detail}</span>}
            </li>
          ))}
        </ul>
        {issue.hits.length > PREVIEW && (
          <Button variant="link" size="sm" onClick={() => setShowAll((s) => !s)}>
            {showAll ? 'Show fewer' : `Show all ${issue.hits.length} URLs`}
          </Button>
        )}
        {issue.affectedCount > issue.hits.length && (
          <p className="text-small text-muted">
            Showing the first {issue.hits.length} of {issue.affectedCount}. Export the CSV for the full list.
          </p>
        )}
      </div>
    </li>
  );
}
