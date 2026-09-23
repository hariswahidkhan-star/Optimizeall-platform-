import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { CheckCircle2, FileCheck2, MessageSquareWarning } from 'lucide-react';
import { useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import {
  Alert,
  Badge,
  Button,
  Card,
  CardBody,
  CardHeader,
  DateTime,
  EmptyState,
  ErrorState,
  FormField,
  Skeleton,
  Tabs,
  Textarea,
  Timeline,
} from '@/components/ui';
import { VersionCompare } from '@/features/agency/shared/VersionCompare';
import type { DeliverableDetail, DeliverableSummary } from '@/features/agency/shared/deliveryTypes';
import { DeliverableStatusBadge, labelOf } from '@/features/agency/shared/deliveryUi';
import { api } from '@/lib/api/client';
import { errorMessage, isApiError } from '@/lib/api/errors';
import { ClientShell } from './ClientShell';
import { ScorePicker } from './FeedbackPage';
import { clientKeys } from './useClientOrg';

function DeliverableList({ base, orgId, view, link }: { base: string; orgId: string; view: string; link: (p: string) => string }) {
  const list = useQuery({
    queryKey: clientKeys.part(orgId, 'deliverables', view),
    queryFn: ({ signal }) => api.get<DeliverableSummary[]>(`${base}/deliverables`, { query: { view }, signal }),
  });
  if (list.isPending) return <Skeleton height={160} />;
  if (list.isError) return <ErrorState error={list.error} />;
  if (list.data.length === 0)
    return <EmptyState icon={<FileCheck2 aria-hidden="true" />} title={view === 'awaiting' ? "You're all caught up" : 'Nothing here yet'} />;
  return (
    <ul className="dl-list" aria-label="Deliverables">
      {list.data.map((d) => (
        <li key={d.id} className="dl-list__item">
          <span className="dl-list__main">
            <Link className="dl-list__title ui-link" to={link(`/client/approvals/${d.id}`)}>
              {d.title}
            </Link>
            <span className="dl-meta">
              {d.projectName} · {labelOf(d.type)} · version {d.currentVersion}
              {d.clientDueAt && d.status === 'ClientReview' ? (
                <>
                  {' · review by '}
                  <DateTime value={d.clientDueAt} format="date" />
                </>
              ) : null}
            </span>
          </span>
          <span className="dl-row">
            {d.isOverdue ? <Badge tone="danger">Overdue</Badge> : null}
            <DeliverableStatusBadge status={d.status} audience="client" />
          </span>
        </li>
      ))}
    </ul>
  );
}

export function ApprovalsPage() {
  const [view, setView] = useState('awaiting');
  return (
    <ClientShell title="Approvals" description="Review work from your agency team. Approvers and Owners can approve or request changes.">
      {({ base, org, link }) => (
        <Card>
          <CardBody>
            <Tabs
              label="Approval filters"
              value={view}
              onValueChange={setView}
              tabs={[
                { id: 'awaiting', label: 'Awaiting review', content: view === 'awaiting' ? <DeliverableList base={base} orgId={org.clientId} view="awaiting" link={link} /> : null },
                { id: 'approved', label: 'Approved', content: view === 'approved' ? <DeliverableList base={base} orgId={org.clientId} view="approved" link={link} /> : null },
                { id: 'all', label: 'All', content: view === 'all' ? <DeliverableList base={base} orgId={org.clientId} view="all" link={link} /> : null },
              ]}
            />
          </CardBody>
        </Card>
      )}
    </ClientShell>
  );
}

function Csat({ base, id, onDone }: { base: string; id: string; onDone: () => void }) {
  const [score, setScore] = useState<number | null>(null);
  const [comment, setComment] = useState('');
  const rate = useMutation({
    mutationFn: () => api.post(`${base}/deliverables/${id}/csat`, { score, comment: comment || null }),
    onSuccess: onDone,
  });
  return (
    <form
      className="dl-form"
      aria-label="Rate this deliverable"
      onSubmit={(e) => {
        e.preventDefault();
        if (score !== null) rate.mutate();
      }}
    >
      {rate.error ? <Alert tone="danger">{errorMessage(rate.error)}</Alert> : null}
      <ScorePicker legend="How satisfied are you with this deliverable?" min={1} max={5} value={score} onChange={setScore} lowLabel="very dissatisfied" highLabel="very satisfied" />
      <FormField label="Anything we could do better?" optional>
        <Textarea rows={2} value={comment} onChange={(e) => setComment(e.target.value)} maxLength={2000} />
      </FormField>
      <div className="dl-row">
        <Button type="submit" variant="secondary" disabled={score === null} loading={rate.isPending}>
          Send rating
        </Button>
      </div>
    </form>
  );
}

function Detail({ base, orgId, id }: { base: string; orgId: string; id: string }) {
  const qc = useQueryClient();
  const [comment, setComment] = useState('');
  const [rated, setRated] = useState(false);
  const key = clientKeys.part(orgId, 'deliverable', id);
  const detail = useQuery({
    queryKey: key,
    queryFn: ({ signal }) => api.get<DeliverableDetail>(`${base}/deliverables/${id}`, { signal }),
  });
  const decide = useMutation({
    mutationFn: ({ action, version }: { action: 'approve' | 'request-changes' | 'comments'; version: number }) =>
      api.post<DeliverableDetail>(
        `${base}/deliverables/${id}/${action}`,
        action === 'comments' ? { version, body: comment } : { version, comment: comment || null },
      ),
    onSuccess: (d) => {
      qc.setQueryData(key, d);
      setComment('');
      void qc.invalidateQueries({ queryKey: clientKeys.part(orgId, 'deliverables') });
      void qc.invalidateQueries({ queryKey: clientKeys.part(orgId, 'home') });
    },
    onError: (error) => {
      // A newer version or someone else's decision: show the latest state.
      if (isApiError(error) && error.status === 409) void detail.refetch();
    },
  });
  if (detail.isPending) return <Skeleton height={300} />;
  if (detail.isError) return <ErrorState error={detail.error} />;
  const d = detail.data;
  const s = d.deliverable;
  const can = (a: string) => d.allowedActions.includes(a as never);
  const reviewedVersion = s.currentVersion;
  return (
    <>
      <div className="dl-row">
        <DeliverableStatusBadge status={s.status} audience="client" />
        <span className="dl-meta">
          {s.projectName} · {labelOf(s.type)} · version {s.currentVersion}
        </span>
        {s.clientDueAt && s.status === 'ClientReview' ? (
          <span className="dl-meta">
            Please review by <DateTime value={s.clientDueAt} format="datetime" />
          </span>
        ) : null}
      </div>
      {d.approvedVersion ? (
        <Alert tone="success" title="Approved" icon={<CheckCircle2 aria-hidden="true" />}>
          Version {d.approvedVersion} was {d.autoApproved ? 'approved automatically' : `approved by ${d.approvedByName ?? 'your team'}`}
          {s.approvedAt ? (
            <>
              {' '}
              on <DateTime value={s.approvedAt} format="datetime" />
            </>
          ) : null}
          .
        </Alert>
      ) : null}
      {d.description ? <p>{d.description}</p> : null}
      <VersionCompare versions={d.versions} comments={d.comments} audience="client" />
      <div className="dl-grid dl-grid--wide">
        <Card as="section" aria-label="Your decision">
          <CardHeader title={can('approve') ? `Your decision on version ${reviewedVersion}` : 'Comments'} headingLevel={2} />
          <CardBody className="dl-form">
            {decide.error ? (
              <Alert tone="danger" title="Couldn't save">
                {errorMessage(decide.error)}
              </Alert>
            ) : null}
            {can('comment') ? (
              <>
                <FormField label="Comment" hint={can('requestChanges') ? 'Required when requesting changes.' : undefined}>
                  <Textarea rows={3} value={comment} onChange={(e) => setComment(e.target.value)} maxLength={4000} />
                </FormField>
                <div className="dl-row">
                  {can('approve') ? (
                    <Button
                      leadingIcon={<CheckCircle2 aria-hidden="true" />}
                      loading={decide.isPending && decide.variables?.action === 'approve'}
                      onClick={() => decide.mutate({ action: 'approve', version: reviewedVersion })}
                    >
                      Approve version {reviewedVersion}
                    </Button>
                  ) : null}
                  {can('requestChanges') ? (
                    <Button
                      variant="danger"
                      leadingIcon={<MessageSquareWarning aria-hidden="true" />}
                      disabled={!comment.trim()}
                      loading={decide.isPending && decide.variables?.action === 'request-changes'}
                      onClick={() => decide.mutate({ action: 'request-changes', version: reviewedVersion })}
                    >
                      Request changes
                    </Button>
                  ) : null}
                  <Button
                    variant="secondary"
                    disabled={!comment.trim()}
                    loading={decide.isPending && decide.variables?.action === 'comments'}
                    onClick={() => decide.mutate({ action: 'comments', version: reviewedVersion })}
                  >
                    Add comment only
                  </Button>
                </div>
              </>
            ) : (
              <Alert tone="info" title="View only">
                Your role in this organization is view-only. An Approver or Owner can approve or request changes.
              </Alert>
            )}
            {can('rate') && !rated ? <Csat base={base} id={id} onDone={() => setRated(true)} /> : null}
            {rated ? <Alert tone="success">Thanks for rating this deliverable.</Alert> : null}
          </CardBody>
        </Card>
        <Card as="section" aria-label="History">
          <CardHeader title="History" headingLevel={2} />
          <CardBody>
            {d.history.length === 0 ? (
              <p className="dl-muted">No decisions yet.</p>
            ) : (
              <Timeline
                label="Decision history"
                items={d.history.map((h) => ({
                  id: h.id,
                  title: `${labelOf(h.decision)} · version ${h.versionNumber}`,
                  description: h.comment ?? undefined,
                  timestamp: h.createdAt,
                  actor: h.userName ?? undefined,
                  tone: h.decision.includes('Changes') ? 'danger' : h.decision.includes('Approved') ? 'success' : 'info',
                }))}
              />
            )}
          </CardBody>
        </Card>
      </div>
    </>
  );
}

export function ApprovalDetailPage() {
  const { deliverableId = '' } = useParams();
  return (
    <ClientShell title="Review deliverable" breadcrumbs={[{ label: 'Approvals', to: '/client/approvals' }, { label: 'Review' }]}>
      {({ base, org }) => <Detail key={org.clientId + deliverableId} base={base} orgId={org.clientId} id={deliverableId} />}
    </ClientShell>
  );
}
