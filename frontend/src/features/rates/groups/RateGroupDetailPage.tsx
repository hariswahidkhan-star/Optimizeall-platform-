import { useMutation, useQueryClient } from '@tanstack/react-query';
import { Archive, Download, Link2, Pencil, Upload, UserPlus, UserMinus } from 'lucide-react';
import { useId, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import {
  Alert,
  Badge,
  Button,
  Checkbox,
  ConfirmDialog,
  DataTable,
  DateTime,
  Dialog,
  EmptyState,
  ErrorState,
  FilterBar,
  FormField,
  PageHeader,
  Pagination,
  Skeleton,
  Stat,
  Tabs,
  Textarea,
  type DataTableColumn,
} from '@/components/ui';
import { useToast } from '@/components/ui/toastContext';
import { api } from '@/lib/api/client';
import { ApiError, errorMessage } from '@/lib/api/errors';
import { Permissions } from '@/lib/auth/permissions';
import { useAuth } from '@/lib/auth/useAuth';
import { formatNumber } from '@/lib/format/money';
import { rk, useGroupHistory, useGroupMembers, useRateGroup } from '../api/queries';
import type { BulkIssue, BulkMembersResult, CsvImportResult, RateGroup, RateGroupMember } from '../api/types';
import { AssignRateDialog } from '../components/AssignRateDialog';
import { AssignmentsTable } from '../components/AssignmentsTable';
import { PeoplePicker, type PickedPerson } from '../components/PeoplePicker';
import { GroupFields, groupFormFrom, groupToInput, type GroupForm } from './GroupFields';
import '../rates.css';

export function RateGroupDetailPage() {
  const { groupId = '' } = useParams();
  const { hasPermission } = useAuth();
  const canManage = hasPermission(Permissions.RatesManage);
  const canAssign = hasPermission(Permissions.RatesAssign);
  const query = useRateGroup(groupId);
  const [dialog, setDialog] = useState<'edit' | 'archive' | 'add' | 'import' | 'assign' | null>(null);

  const breadcrumbs = [
    { label: 'Rate groups', to: '/manage/rate-groups' },
    { label: query.data?.name ?? 'Rate group' },
  ];
  if (query.isPending)
    return <PageHeader title={<Skeleton width={240} height={32} />} breadcrumbs={breadcrumbs} />;
  if (query.isError)
    return (
      <>
        <PageHeader title="Rate group" breadcrumbs={breadcrumbs} />
        <ErrorState error={query.error} onRetry={() => void query.refetch()} />
      </>
    );
  const group = query.data;
  const manual = group.membershipMode === 'Manual';
  const archived = !!group.archivedAt;

  return (
    <>
      <PageHeader
        title={group.name}
        breadcrumbs={breadcrumbs}
        description={group.autoRule ? `Automatic: ${group.autoRule}` : (group.description ?? undefined)}
        meta={
          <span className="cluster rt-cluster-sm">
            <Badge size="sm" tone={manual ? 'neutral' : 'warning'}>
              {manual ? 'Manual membership' : 'Automatic membership'}
            </Badge>
            <Badge size="sm">Priority {group.priority}</Badge>
            {archived && <Badge size="sm">Archived</Badge>}
          </span>
        }
        actions={
          !archived ? (
            <div className="cluster rt-cluster-sm">
              {canAssign && (
                <Button leadingIcon={<Link2 />} onClick={() => setDialog('assign')}>
                  Assign rate card
                </Button>
              )}
              {canAssign && manual && (
                <>
                  <Button variant="secondary" leadingIcon={<UserPlus />} onClick={() => setDialog('add')}>
                    Add people
                  </Button>
                  <Button variant="secondary" leadingIcon={<Upload />} onClick={() => setDialog('import')}>
                    Import CSV
                  </Button>
                </>
              )}
              <Button
                variant="ghost"
                leadingIcon={<Download />}
                onClick={() =>
                  void api.download(
                    `/admin/rate-groups/${group.id}/members/export.csv`,
                    'rate-group-members.csv',
                  )
                }
              >
                Export
              </Button>
              {canManage && (
                <>
                  <Button variant="ghost" leadingIcon={<Pencil />} onClick={() => setDialog('edit')}>
                    Edit
                  </Button>
                  <Button variant="ghost" leadingIcon={<Archive />} onClick={() => setDialog('archive')}>
                    Archive
                  </Button>
                </>
              )}
            </div>
          ) : undefined
        }
      />
      <div className="stack">
        {archived && (
          <Alert tone="warning" title="Archived">
            {group.archiveReason}
          </Alert>
        )}
        <div className="grid-auto rt-stat-grid">
          <Stat
            label={manual ? 'Members' : 'People matching the rule'}
            value={formatNumber(group.memberCount ?? 0)}
            measurement="Count"
          />
          <Stat
            label="Active assignments"
            value={String(group.assignments.filter((a) => a.isActive).length)}
            measurement="Count"
          />
        </div>
        <Tabs
          label="Rate group sections"
          tabs={[
            {
              id: 'members',
              label: manual ? 'Members' : 'Matching people',
              content: <MembersTab group={group} canEdit={canAssign && manual && !archived} />,
            },
            {
              id: 'assignments',
              label: 'Rate cards',
              badge: group.assignments.length,
              content:
                group.assignments.length === 0 ? (
                  <EmptyState
                    headingLevel={3}
                    title="No rate card assigned"
                    description="Assign a card so everyone in the group gets its rates."
                  />
                ) : (
                  <AssignmentsTable
                    assignments={group.assignments}
                    caption="Rate cards assigned to this group"
                    show="card"
                  />
                ),
            },
            ...(manual
              ? [{ id: 'history', label: 'History', content: <HistoryTab groupId={group.id} /> }]
              : []),
          ]}
        />
      </div>
      {dialog === 'assign' && (
        <AssignRateDialog onClose={() => setDialog(null)} group={{ id: group.id, name: group.name }} />
      )}
      {dialog === 'add' && <AddPeopleDialog group={group} onClose={() => setDialog(null)} />}
      {dialog === 'import' && <ImportDialog group={group} onClose={() => setDialog(null)} />}
      {dialog === 'edit' && <EditGroupDialog group={group} onClose={() => setDialog(null)} />}
      {dialog === 'archive' && <ArchiveGroupDialog group={group} onClose={() => setDialog(null)} />}
    </>
  );
}

function MembersTab({ group, canEdit }: { group: RateGroup; canEdit: boolean }) {
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(25);
  const [selected, setSelected] = useState<string[]>([]);
  const [removing, setRemoving] = useState(false);
  const members = useGroupMembers(group.id, { search, page, pageSize });
  const toast = useToast();
  const queryClient = useQueryClient();
  const manual = group.membershipMode === 'Manual';

  const columns: DataTableColumn<RateGroupMember>[] = [
    {
      id: 'name',
      header: 'Person',
      primary: true,
      cell: (m) => (
        <span className="stack rt-stack-xs">
          <Link className="ui-link" to={`/admin/users/${m.userId}#rates`}>
            {m.displayName}
          </Link>
          <span className="text-small text-muted">{m.email}</span>
        </span>
      ),
    },
    { id: 'country', header: 'Country', cell: (m) => m.countryCode },
    { id: 'tier', header: 'Tier', cell: (m) => m.tier },
    {
      id: 'status',
      header: 'Account',
      cell: (m) => (
        <span className="cluster rt-cluster-sm">
          {m.status !== 'Active' && (
            <Badge size="sm" tone="warning">
              {m.status}
            </Badge>
          )}
          {m.isTestAccount && <Badge size="sm">TEST</Badge>}
          {m.status === 'Active' && !m.isTestAccount && <span className="text-muted">Active</span>}
        </span>
      ),
    },
    manual
      ? {
          id: 'added',
          header: 'Added',
          hideOnMobile: true,
          cell: (m) => (m.addedAt ? <DateTime value={m.addedAt} format="date" /> : '—'),
        }
      : {
          id: 'followers',
          header: 'Followers',
          align: 'right',
          cell: (m) => (m.followers === null ? '—' : formatNumber(m.followers)),
        },
  ];

  return (
    <div className="stack">
      <FilterBar
        search={search}
        onSearchChange={(s) => {
          setSearch(s);
          setPage(1);
        }}
        searchLabel="Search members"
        searchPlaceholder="Search by name or email…"
      />
      {members.isError ? (
        <ErrorState error={members.error} onRetry={() => void members.refetch()} />
      ) : (
        <>
          <DataTable
            caption="Group members"
            columns={columns}
            rows={members.data?.items ?? []}
            getRowId={(m) => m.userId}
            rowLabel={(m) => m.displayName}
            loading={members.isLoading}
            selectable={canEdit}
            selectedIds={selected}
            onSelectionChange={setSelected}
            bulkActions={
              canEdit
                ? (ids) => (
                    <Button
                      size="sm"
                      variant="danger"
                      leadingIcon={<UserMinus />}
                      onClick={() => setRemoving(true)}
                    >
                      Remove {ids.length} from group
                    </Button>
                  )
                : undefined
            }
            emptyState={
              <EmptyState
                headingLevel={3}
                title={search ? 'Nobody matches' : manual ? 'No members yet' : 'Nobody matches the rule yet'}
                description={manual ? 'Add people one by one, in bulk or from a CSV file.' : undefined}
              />
            }
          />
          {members.data && members.data.total > 0 && (
            <Pagination
              page={page}
              pageSize={pageSize}
              total={members.data.total}
              onPageChange={setPage}
              onPageSizeChange={(size) => {
                setPageSize(size);
                setPage(1);
              }}
            />
          )}
        </>
      )}
      <ConfirmDialog
        open={removing}
        onClose={() => setRemoving(false)}
        tone="danger"
        title={`Remove ${selected.length} ${selected.length === 1 ? 'person' : 'people'} from ${group.name}?`}
        description="Their next submissions are priced without this group's rate. Submissions already made keep their price."
        requireReason
        reasonMinLength={5}
        confirmLabel="Remove"
        onConfirm={async ({ reason }) => {
          const result = await api.post<BulkMembersResult>(`/admin/rate-groups/${group.id}/members/remove`, {
            userIds: selected,
            reason,
          });
          toast.success(`Removed ${result.removed} ${result.removed === 1 ? 'person' : 'people'}`);
          setSelected([]);
          setRemoving(false);
          await queryClient.invalidateQueries({ queryKey: rk.all });
        }}
      />
    </div>
  );
}

function HistoryTab({ groupId }: { groupId: string }) {
  const [page, setPage] = useState(1);
  const history = useGroupHistory(groupId, { page, pageSize: 25 });
  const columns: DataTableColumn<NonNullable<typeof history.data>['items'][number]>[] = [
    { id: 'at', header: 'When', cell: (e) => <DateTime value={e.at} format="both" /> },
    {
      id: 'what',
      header: 'Change',
      primary: true,
      cell: (e) => (
        <span>
          <Badge size="sm" tone={e.action === 'Added' ? 'success' : 'danger'}>
            {e.action}
          </Badge>{' '}
          {e.displayName}
        </span>
      ),
    },
    { id: 'by', header: 'By', cell: (e) => e.actor?.displayName ?? 'System' },
    { id: 'how', header: 'How', cell: (e) => e.source },
    { id: 'why', header: 'Reason', hideOnMobile: true, cell: (e) => e.reason ?? '—' },
  ];
  if (history.isError) return <ErrorState error={history.error} onRetry={() => void history.refetch()} />;
  return (
    <div className="stack">
      <DataTable
        caption="Membership history"
        columns={columns}
        rows={history.data?.items ?? []}
        getRowId={(e) => `${e.userId}-${e.at}-${e.action}`}
        loading={history.isLoading}
        emptyState={<EmptyState headingLevel={3} title="No membership changes yet" />}
      />
      {history.data && history.data.total > 25 && (
        <Pagination page={page} pageSize={25} total={history.data.total} onPageChange={setPage} />
      )}
    </div>
  );
}

function IssuesList({
  title,
  issues,
  tone,
}: {
  title: string;
  issues: BulkIssue[];
  tone: 'danger' | 'warning';
}) {
  if (issues.length === 0) return null;
  return (
    <Alert tone={tone} title={`${title} (${issues.length})`}>
      <ul className="rt-list rt-issues">
        {issues.slice(0, 200).map((i, n) => (
          <li key={`${i.row ?? ''}-${i.value}-${n}`}>
            {i.row !== null && <strong>Row {i.row}: </strong>}
            <span className="rt-mono">{i.value}</span> — {i.message}
          </li>
        ))}
      </ul>
      {issues.length > 200 && <p className="text-small">…and {issues.length - 200} more.</p>}
    </Alert>
  );
}

function AddPeopleDialog({ group, onClose }: { group: RateGroup; onClose: () => void }) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const [people, setPeople] = useState<PickedPerson[]>([]);
  const [note, setNote] = useState('');
  const [result, setResult] = useState<BulkMembersResult | null>(null);
  const add = useMutation({
    mutationFn: () =>
      api.post<BulkMembersResult>(`/admin/rate-groups/${group.id}/members`, {
        userIds: people.map((p) => p.id),
        note: note.trim() || null,
      }),
    onSuccess: async (r) => {
      setResult(r);
      toast.success(`Added ${r.added} ${r.added === 1 ? 'person' : 'people'} to ${group.name}`);
      setPeople([]);
      await queryClient.invalidateQueries({ queryKey: rk.all });
    },
  });
  return (
    <Dialog
      open
      onClose={onClose}
      size="lg"
      title={`Add people to ${group.name}`}
      description="Select several people, then add them together. People already in the group are left as they are."
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            {result ? 'Done' : 'Cancel'}
          </Button>
          <Button onClick={() => add.mutate()} loading={add.isPending} disabled={people.length === 0}>
            Add {people.length || ''} {people.length === 1 ? 'person' : 'people'}
          </Button>
        </>
      }
    >
      <div className="stack">
        {add.isError && (
          <Alert tone="danger" title="Not added">
            {errorMessage(add.error)}
          </Alert>
        )}
        {result && (
          <Alert tone="success" title="Added">
            {result.added} added · {result.unchanged} already members
          </Alert>
        )}
        {result && <IssuesList title="Not added" issues={result.rejected} tone="danger" />}
        {result && <IssuesList title="Added with warnings" issues={result.warnings} tone="warning" />}
        <PeoplePicker label="People" value={people} onChange={setPeople} />
        <FormField label="Note" optional hint="Kept with the membership and in the history.">
          <Textarea rows={2} maxLength={300} value={note} onChange={(e) => setNote(e.target.value)} />
        </FormField>
      </div>
    </Dialog>
  );
}

function ImportDialog({ group, onClose }: { group: RateGroup; onClose: () => void }) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const fileId = useId();
  const [file, setFile] = useState<File | null>(null);
  const [report, setReport] = useState<CsvImportResult | null>(null);
  const run = useMutation({
    mutationFn: (dryRun: boolean) => {
      const form = new FormData();
      form.append('file', file!);
      return api.upload<CsvImportResult>(
        `/admin/rate-groups/${group.id}/members/import?dryRun=${dryRun}`,
        form,
      );
    },
    onSuccess: async (r) => {
      setReport(r);
      if (!r.dryRun) {
        toast.success(`Imported ${r.added} ${r.added === 1 ? 'person' : 'people'}`);
        await queryClient.invalidateQueries({ queryKey: rk.all });
      }
    },
  });
  const validated = report?.dryRun === true;
  return (
    <Dialog
      open
      onClose={onClose}
      size="lg"
      title={`Import members into ${group.name}`}
      description="A CSV with an “email” (or “userId”) column. Up to 10,000 rows and 2 MB. Check it first, then import."
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            {report && !report.dryRun ? 'Done' : 'Cancel'}
          </Button>
          <Button
            variant="secondary"
            onClick={() => run.mutate(true)}
            loading={run.isPending && run.variables}
            disabled={!file}
          >
            Check file
          </Button>
          <Button
            onClick={() => run.mutate(false)}
            loading={run.isPending && !run.variables}
            disabled={!validated || (report?.valid ?? 0) === 0}
          >
            Import {validated ? report!.valid : ''} valid rows
          </Button>
        </>
      }
    >
      <div className="stack">
        {run.isError && (
          <Alert tone="danger" title="The file could not be processed">
            {errorMessage(run.error)}
          </Alert>
        )}
        <FormField label="CSV file" id={fileId} required hint="Example: email,name — one person per row.">
          <input
            id={fileId}
            type="file"
            accept=".csv,text/csv"
            className="rt-file"
            onChange={(e) => {
              setFile(e.target.files?.[0] ?? null);
              setReport(null);
            }}
          />
        </FormField>
        {report && (
          <Alert
            tone={report.rejected.length > 0 ? 'warning' : 'success'}
            title={report.dryRun ? 'Validation report' : 'Import finished'}
          >
            {report.rows} rows · {report.dryRun ? `${report.valid} will be added` : `${report.added} added`} ·{' '}
            {report.alreadyMembers} already members · {report.rejected.length} rejected
          </Alert>
        )}
        {report && <IssuesList title="Rejected rows" issues={report.rejected} tone="danger" />}
        {report && <IssuesList title="Warnings" issues={report.warnings} tone="warning" />}
      </div>
    </Dialog>
  );
}

function EditGroupDialog({ group, onClose }: { group: RateGroup; onClose: () => void }) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const [form, setForm] = useState<GroupForm>(() => groupFormFrom(group));
  const save = useMutation({
    mutationFn: () =>
      api.put<RateGroup>(`/admin/rate-groups/${group.id}`, {
        ...groupToInput(form),
        concurrencyStamp: group.concurrencyStamp,
      }),
    onSuccess: async () => {
      toast.success('Rate group saved');
      await queryClient.invalidateQueries({ queryKey: rk.all });
      onClose();
    },
  });
  const err = save.error instanceof ApiError ? save.error : null;
  return (
    <Dialog
      open
      onClose={onClose}
      size="lg"
      title={`Edit ${group.name}`}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button
            onClick={() => save.mutate()}
            loading={save.isPending}
            disabled={form.name.trim().length < 2}
          >
            Save
          </Button>
        </>
      }
    >
      <div className="stack">
        {save.isError && (
          <Alert tone="danger" title="Not saved">
            {errorMessage(save.error)}
          </Alert>
        )}
        <GroupFields
          value={form}
          onChange={setForm}
          errors={err?.errors}
          modeLocked={group.membershipMode === 'Manual' && (group.memberCount ?? 0) > 0}
        />
      </div>
    </Dialog>
  );
}

function ArchiveGroupDialog({ group, onClose }: { group: RateGroup; onClose: () => void }) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const inUse =
    ((group.memberCount ?? 0) > 0 && group.membershipMode === 'Manual') ||
    group.assignments.some((a) => a.isActive);
  const [force, setForce] = useState(false);
  return (
    <ConfirmDialog
      open
      onClose={onClose}
      tone="danger"
      title={`Archive ${group.name}?`}
      description="The group stops pricing new submissions. Submissions already made keep their price."
      requireReason
      reasonMinLength={5}
      confirmLabel="Archive group"
      onConfirm={async ({ reason }) => {
        await api.post(`/admin/rate-groups/${group.id}/archive`, {
          reason,
          force,
          concurrencyStamp: group.concurrencyStamp,
        });
        toast.success('Rate group archived');
        await queryClient.invalidateQueries({ queryKey: rk.all });
        onClose();
      }}
    >
      {inUse && (
        <Checkbox
          label="Remove every member and end the group's assignments"
          description="Required while the group is in use. Every removal is kept in the history."
          checked={force}
          onChange={(e) => setForce(e.target.checked)}
        />
      )}
    </ConfirmDialog>
  );
}
