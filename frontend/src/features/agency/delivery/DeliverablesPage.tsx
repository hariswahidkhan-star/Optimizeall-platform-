import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { FileCheck2, Send, ThumbsDown, ThumbsUp, Upload } from 'lucide-react';
import { useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import {
  Alert,
  Button,
  Card,
  CardBody,
  CardHeader,
  DataTable,
  DateTime,
  EmptyState,
  ErrorState,
  FilterBar,
  FormField,
  Input,
  PageHeader,
  Pagination,
  Skeleton,
  Switch,
  Textarea,
  Timeline,
  type DataTableColumn,
} from '@/components/ui';
import { api } from '@/lib/api/client';
import { errorMessage } from '@/lib/api/errors';
import type { PagedResult } from '@/lib/api/types';
import { VersionCompare } from '../shared/VersionCompare';
import { DELIVERABLE_STATUSES, type DeliverableDetail, type DeliverableSummary } from '../shared/deliveryTypes';
import { DeliverableStatusBadge, deliverableStatusLabel, labelOf } from '../shared/deliveryUi';
import { dk } from './api';

const columns: DataTableColumn<DeliverableSummary>[] = [
  {
    id: 'title',
    header: 'Deliverable',
    primary: true,
    cell: (d) => (
      <span className="dl-list__main">
        <Link className="ui-link" to={`/agency/deliverables/${d.id}`}>
          {d.title}
        </Link>
        <span className="dl-meta">
          {d.clientName} · {d.projectName}
        </span>
      </span>
    ),
  },
  { id: 'status', header: 'Status', cell: (d) => <DeliverableStatusBadge status={d.status} /> },
  { id: 'type', header: 'Type', cell: (d) => labelOf(d.type), hideOnMobile: true },
  { id: 'version', header: 'Version', align: 'right', cell: (d) => `v${d.currentVersion}` },
  { id: 'due', header: 'Client due', cell: (d) => (d.clientDueAt ? <DateTime value={d.clientDueAt} format="relative" /> : '—'), hideOnMobile: true },
];

/** Deliverables across clients: my review queue, waiting on clients, or all. */
export function DeliverablesPage() {
  const [filters, setFilters] = useState<Record<string, string | undefined>>({ view: 'review' });
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);
  const query = { view: filters.view, status: filters.status, search, page, pageSize: 25 };
  const list = useQuery({
    queryKey: dk.deliverables(query),
    queryFn: ({ signal }) => api.get<PagedResult<DeliverableSummary>>('/agency/deliverables', { query, signal }),
    placeholderData: keepPreviousData,
  });
  return (
    <div className="dl-page">
      <PageHeader title="Deliverables" description="Internal review queue and approvals across clients." />
      <Card>
        <CardBody className="dl-page">
          <FilterBar
            search={search}
            onSearchChange={(s) => {
              setSearch(s);
              setPage(1);
            }}
            searchLabel="Search deliverables"
            filters={[
              {
                id: 'view',
                label: 'Show',
                allLabel: 'All deliverables',
                options: [
                  { value: 'review', label: 'Awaiting my review' },
                  { value: 'client', label: 'Waiting on clients' },
                  { value: 'mine', label: 'I own' },
                ],
              },
              { id: 'status', label: 'Status', options: DELIVERABLE_STATUSES.map((s) => ({ value: s, label: deliverableStatusLabel(s) })) },
            ]}
            values={filters}
            onFilterChange={(id, v) => {
              setFilters((f) => ({ ...f, [id]: v || undefined }));
              setPage(1);
            }}
            onReset={() => setFilters({})}
          />
          {list.isError ? (
            <ErrorState error={list.error} />
          ) : (
            <DataTable
              caption="Deliverables"
              columns={columns}
              rows={list.data?.items ?? []}
              getRowId={(d) => d.id}
              loading={list.isPending}
              emptyState={<EmptyState icon={<FileCheck2 aria-hidden="true" />} title="Nothing here" />}
            />
          )}
          {list.data && list.data.total > list.data.pageSize ? (
            <Pagination page={page} pageSize={list.data.pageSize} total={list.data.total} onPageChange={setPage} />
          ) : null}
        </CardBody>
      </Card>
    </div>
  );
}

function NewVersion({ id, onSaved }: { id: string; onSaved: (d: DeliverableDetail) => void }) {
  const [file, setFile] = useState<File | null>(null);
  const [link, setLink] = useState('');
  const [body, setBody] = useState('');
  const [notes, setNotes] = useState('');
  const save = useMutation({
    mutationFn: () => {
      const form = new FormData();
      if (file) form.append('file', file);
      if (link) form.append('linkUrl', link);
      if (body) form.append('body', body);
      if (notes) form.append('notes', notes);
      return api.upload<DeliverableDetail>(`/agency/deliverables/${id}/versions`, form);
    },
    onSuccess: (d) => {
      setFile(null);
      setLink('');
      setBody('');
      setNotes('');
      onSaved(d);
    },
  });
  return (
    <form
      className="dl-form"
      aria-label="Add a version"
      onSubmit={(e) => {
        e.preventDefault();
        save.mutate();
      }}
    >
      {save.error ? <Alert tone="danger">{errorMessage(save.error)}</Alert> : null}
      <FormField label="File" optional hint="PNG, JPEG, WebP, PDF or MP4, up to 50 MB. Hosted videos, Figma or docs: use a link.">
        <input type="file" accept="image/png,image/jpeg,image/webp,application/pdf,video/mp4" onChange={(e) => setFile(e.target.files?.[0] ?? null)} />
      </FormField>
      <FormField label="Link" optional>
        <Input type="url" value={link} onChange={(e) => setLink(e.target.value)} placeholder="https://" />
      </FormField>
      <FormField label="Text content" optional hint="Copy, captions or a blog draft.">
        <Textarea rows={4} value={body} onChange={(e) => setBody(e.target.value)} />
      </FormField>
      <FormField label="Notes for reviewers" optional>
        <Input value={notes} onChange={(e) => setNotes(e.target.value)} maxLength={4000} />
      </FormField>
      <div className="dl-row">
        <Button type="submit" leadingIcon={<Upload aria-hidden="true" />} loading={save.isPending} disabled={!file && !link && !body}>
          Add version
        </Button>
      </div>
    </form>
  );
}

/** Deliverable review workspace: side-by-side versions, pinned comments, internal approval / send to client. */
export function DeliverableReviewPage() {
  const { deliverableId = '' } = useParams();
  const qc = useQueryClient();
  const [comment, setComment] = useState('');
  const [internalOnly, setInternalOnly] = useState(true);
  const detail = useQuery({
    queryKey: dk.deliverable(deliverableId),
    queryFn: ({ signal }) => api.get<DeliverableDetail>(`/agency/deliverables/${deliverableId}`, { signal }),
  });
  const onSaved = (d: DeliverableDetail) => {
    qc.setQueryData(dk.deliverable(deliverableId), d);
    void qc.invalidateQueries({ queryKey: ['delivery', 'deliverables'] });
    void qc.invalidateQueries({ queryKey: dk.dashboard });
  };
  const action = useMutation({
    mutationFn: ({ path, body }: { path: string; body: object }) => api.post<DeliverableDetail>(`/agency/deliverables/${deliverableId}/${path}`, body),
    onSuccess: (d) => {
      onSaved(d);
      setComment('');
    },
  });
  if (detail.isPending) return <Skeleton height={300} />;
  if (detail.isError) return <ErrorState error={detail.error} onRetry={() => void detail.refetch()} />;
  const d = detail.data;
  const s = d.deliverable;
  const can = (a: string) => d.allowedActions.includes(a as never);
  const version = s.currentVersion;
  const act = (path: string, extra: object = {}) => action.mutate({ path, body: { version, comment: comment || null, ...extra } });
  return (
    <div className="dl-page">
      <PageHeader
        title={s.title}
        breadcrumbs={[
          { label: 'Deliverables', to: '/agency/deliverables' },
          { label: s.projectName, to: `/agency/projects/${s.projectId}` },
          { label: s.title },
        ]}
        meta={
          <span className="dl-row">
            <DeliverableStatusBadge status={s.status} />
            <span className="dl-meta">
              {s.clientName} · {labelOf(s.type)} · v{s.currentVersion}
              {s.clientDueAt ? (
                <>
                  {' · client feedback due '}
                  <DateTime value={s.clientDueAt} format="relative" />
                </>
              ) : null}
            </span>
          </span>
        }
      />
      {action.error ? <Alert tone="danger">{errorMessage(action.error)}</Alert> : null}
      {d.approvedVersion ? (
        <Alert tone="success" title={d.autoApproved ? 'Approved automatically' : 'Approved by the client'}>
          Version {d.approvedVersion}
          {d.approvedByName ? ` approved by ${d.approvedByName}` : ''}
          {s.approvedAt ? (
            <>
              {' '}
              on <DateTime value={s.approvedAt} format="datetime" />
            </>
          ) : null}
          .
        </Alert>
      ) : null}
      <div className="dl-two-col">
        <div className="dl-page">
          {d.description ? <p>{d.description}</p> : null}
          <VersionCompare versions={d.versions} comments={d.comments} audience="staff" />
        </div>
        <div className="dl-page">
          <Card as="section" aria-label="Review">
            <CardHeader title="Review" headingLevel={2} />
            <CardBody className="dl-form">
              <FormField label="Comment" hint={`Pinned to version ${version}.`}>
                <Textarea rows={3} value={comment} onChange={(e) => setComment(e.target.value)} maxLength={4000} />
              </FormField>
              <Switch checked={internalOnly} onCheckedChange={setInternalOnly} label="Internal only" description="Internal comments are never shown to the client." />
              <div className="dl-row">
                <Button
                  variant="secondary"
                  disabled={!comment.trim() || version === 0}
                  loading={action.isPending && action.variables?.path === 'comments'}
                  onClick={() => action.mutate({ path: 'comments', body: { version, body: comment, isInternal: internalOnly } })}
                >
                  Add comment
                </Button>
                {can('submit') ? (
                  <Button leadingIcon={<Send aria-hidden="true" />} onClick={() => act('submit')}>
                    Submit for internal review
                  </Button>
                ) : null}
                {can('internalApprove') ? (
                  <Button leadingIcon={<ThumbsUp aria-hidden="true" />} onClick={() => act('internal-approve')}>
                    Approve &amp; send to client
                  </Button>
                ) : null}
                {can('internalRequestChanges') ? (
                  <Button variant="danger" leadingIcon={<ThumbsDown aria-hidden="true" />} disabled={!comment.trim()} onClick={() => act('internal-request-changes')}>
                    Request changes
                  </Button>
                ) : null}
                {can('publish') ? (
                  <Button variant="highlight" onClick={() => act('publish')}>
                    Mark published / delivered
                  </Button>
                ) : null}
              </div>
            </CardBody>
          </Card>
          {can('addVersion') ? (
            <Card as="section" aria-label="New version">
              <CardHeader title="New version" headingLevel={2} />
              <CardBody>
                <NewVersion id={deliverableId} onSaved={onSaved} />
              </CardBody>
            </Card>
          ) : null}
          <Card as="section" aria-label="History">
            <CardHeader title="History" headingLevel={2} />
            <CardBody>
              {d.history.length === 0 ? (
                <p className="dl-muted">No decisions yet.</p>
              ) : (
                <Timeline
                  label="Approval history"
                  items={d.history.map((h) => ({
                    id: h.id,
                    title: `${labelOf(h.decision)} · v${h.versionNumber}`,
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
      </div>
    </div>
  );
}
