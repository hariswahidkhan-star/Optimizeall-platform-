import { useMutation } from '@tanstack/react-query';
import { AlertOctagon, AlertTriangle, CheckCircle2, ChevronDown, ChevronRight, EyeOff, Info, RotateCcw } from 'lucide-react';
import { useId, useState, type FormEvent } from 'react';
import { Alert, Badge, Button, Dialog, EmptyState, FormField, Select, Textarea, useToast } from '@/components/ui';
import { SafeExternalLink } from '@/components/SafeExternalLink';
import { api } from '@/lib/api/client';
import { errorMessage } from '@/lib/api/errors';
import { severityOrder, severityTone, type AuditIssue, type IssueStatus, type SeoSeverity } from './api';

const severityLabel: Record<SeoSeverity, string> = { Error: 'Errors', Warning: 'Warnings', Notice: 'Notices' };
const severityIcon: Record<SeoSeverity, typeof AlertOctagon> = { Error: AlertOctagon, Warning: AlertTriangle, Notice: Info };
const statusLabel: Record<IssueStatus, string> = { Open: 'Open', Fixed: 'Marked fixed', Ignored: 'Ignored' };
const PREVIEW = 20;

type StatusFilter = 'all' | 'open' | 'done';

interface Props {
  issues: AuditIssue[];
  /** When set, each issue can be triaged (mark fixed, ignore with a note, reopen). */
  auditId?: string;
  /** Triage is only possible on completed audits; explains why otherwise. */
  triageDisabledReason?: string;
  onChanged?: (issue: AuditIssue) => void;
}

/** Groups issues by severity (errors first), each with an expandable URL drill-down, the fix guidance and triage actions. */
export function AuditIssueGroups({ issues, auditId, triageDisabledReason, onChanged }: Props) {
  const [filter, setFilter] = useState<StatusFilter>('all');
  if (issues.length === 0)
    return <EmptyState compact headingLevel={2} title="No issues found" description="Every check passed on the crawled pages." />;
  const statusOf = (i: AuditIssue): IssueStatus => i.status ?? 'Open';
  const visible = issues.filter((i) => filter === 'all' || (filter === 'open' ? statusOf(i) === 'Open' : statusOf(i) !== 'Open'));
  const handled = issues.filter((i) => statusOf(i) !== 'Open').length;
  return (
    <div className="stack seo-issue-groups">
      {auditId && (
        <div className="seo-toolbar">
          <FormField label="Show issues" className="seo-inline-filter">
            <Select
              value={filter}
              onChange={(e) => setFilter(e.target.value as StatusFilter)}
              options={[
                { value: 'all', label: `All issues (${issues.length})` },
                { value: 'open', label: `Still open (${issues.length - handled})` },
                { value: 'done', label: `Fixed or ignored (${handled})` },
              ]}
            />
          </FormField>
        </div>
      )}
      {visible.length === 0 && <EmptyState compact headingLevel={2} title="Nothing here" description="No issue matches this filter." />}
      {severityOrder.map((severity) => {
        const group = visible.filter((i) => i.severity === severity);
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
                <IssueItem key={issue.ruleKey} issue={issue} auditId={auditId} disabledReason={triageDisabledReason} onChanged={onChanged} />
              ))}
            </ul>
          </section>
        );
      })}
    </div>
  );
}

function IssueItem({
  issue,
  auditId,
  disabledReason,
  onChanged,
}: {
  issue: AuditIssue;
  auditId?: string;
  disabledReason?: string;
  onChanged?: (issue: AuditIssue) => void;
}) {
  const toast = useToast();
  const [open, setOpen] = useState(false);
  const [showAll, setShowAll] = useState(false);
  const [dialog, setDialog] = useState<IssueStatus | null>(null);
  const panelId = useId();
  const hits = showAll ? issue.hits : issue.hits.slice(0, PREVIEW);
  const status = issue.status ?? 'Open';
  const quick = useMutation({
    mutationFn: (next: IssueStatus) =>
      api.post<AuditIssue>(`/agency/seo/audits/${auditId}/issues/${encodeURIComponent(issue.ruleKey)}/status`, { status: next }),
    onSuccess: (updated) => {
      toast.success(updated.status === 'Open' ? 'Issue reopened' : 'Issue marked fixed');
      onChanged?.(updated);
    },
    onError: (err) => toast.error('Could not update the issue', errorMessage(err)),
  });
  return (
    <li className="seo-issue">
      <button type="button" className="seo-issue__toggle" aria-expanded={open} aria-controls={panelId} onClick={() => setOpen((o) => !o)}>
        {open ? <ChevronDown aria-hidden="true" /> : <ChevronRight aria-hidden="true" />}
        <span className="seo-issue__name">{issue.title}</span>
        {status !== 'Open' && (
          <Badge tone={status === 'Fixed' ? 'success' : 'neutral'} size="sm">
            {statusLabel[status]}
          </Badge>
        )}
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
        {issue.statusNote && (
          <p className="text-small">
            <strong>{status === 'Ignored' ? 'Why it is ignored' : 'Note'}:</strong> {issue.statusNote}
          </p>
        )}
        {auditId && (
          <div className="seo-toolbar" role="group" aria-label={`Actions for ${issue.title}`}>
            {disabledReason ? (
              <p className="text-small text-muted">{disabledReason}</p>
            ) : status === 'Open' ? (
              <>
                <Button size="sm" variant="secondary" leadingIcon={<CheckCircle2 />} loading={quick.isPending} onClick={() => quick.mutate('Fixed')}>
                  Mark fixed
                </Button>
                <Button size="sm" variant="ghost" leadingIcon={<EyeOff />} onClick={() => setDialog('Ignored')}>
                  Ignore…
                </Button>
              </>
            ) : (
              <Button size="sm" variant="secondary" leadingIcon={<RotateCcw />} loading={quick.isPending} onClick={() => quick.mutate('Open')}>
                Reopen
              </Button>
            )}
          </div>
        )}
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
      {dialog && auditId && (
        <IgnoreIssueDialog auditId={auditId} issue={issue} onClose={() => setDialog(null)} onSaved={(updated) => onChanged?.(updated)} />
      )}
    </li>
  );
}

/** Ignoring an issue needs a reason: it is kept on later audits of the site until someone reopens it. */
export function IgnoreIssueDialog({
  auditId,
  issue,
  onClose,
  onSaved,
}: {
  auditId: string;
  issue: AuditIssue;
  onClose: () => void;
  onSaved: (issue: AuditIssue) => void;
}) {
  const [note, setNote] = useState(issue.statusNote ?? '');
  const [status, setStatus] = useState<IssueStatus>('Ignored');
  const [touched, setTouched] = useState(false);
  const save = useMutation({
    mutationFn: () =>
      api.post<AuditIssue>(`/agency/seo/audits/${auditId}/issues/${encodeURIComponent(issue.ruleKey)}/status`, { status, note: note.trim() || null }),
    onSuccess: (updated) => {
      onSaved(updated);
      onClose();
    },
  });
  const noteMissing = status === 'Ignored' && note.trim().length === 0;
  const submit = (e: FormEvent) => {
    e.preventDefault();
    setTouched(true);
    if (!noteMissing) save.mutate();
  };
  return (
    <Dialog
      open
      onClose={onClose}
      title={`Triage: ${issue.title}`}
      description="Ignored issues stay ignored on the next audits of this site until someone reopens them."
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" form="seo-ignore-issue" loading={save.isPending}>
            Save
          </Button>
        </>
      }
    >
      <form id="seo-ignore-issue" className="stack" onSubmit={submit} noValidate>
        {save.isError && (
          <Alert tone="danger" title="Could not save">
            {errorMessage(save.error)}
          </Alert>
        )}
        <FormField label="Status">
          <Select
            value={status}
            onChange={(e) => setStatus(e.target.value as IssueStatus)}
            options={[
              { value: 'Ignored', label: 'Ignore (not a problem for this site)' },
              { value: 'Fixed', label: 'Fixed' },
            ]}
          />
        </FormField>
        <FormField
          label={status === 'Ignored' ? 'Why is it ignored?' : 'Note'}
          required={status === 'Ignored'}
          optional={status !== 'Ignored'}
          error={touched && noteMissing ? 'Add a short reason so the team knows why.' : undefined}
        >
          <Textarea value={note} rows={3} maxLength={1000} onChange={(e) => setNote(e.target.value)} />
        </FormField>
      </form>
    </Dialog>
  );
}
