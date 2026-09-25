import { useMutation, useQueryClient } from '@tanstack/react-query';
import { FileUp, Plus, Sparkles, TicketPercent, UsersRound } from 'lucide-react';
import { useId, useState } from 'react';
import {
  Alert,
  Badge,
  Button,
  ConfirmDialog,
  DataTable,
  DateTime,
  Dialog,
  EmptyState,
  ErrorState,
  FilterBar,
  FormField,
  Input,
  Pagination,
  RadioGroup,
  Select,
  Textarea,
  type DataTableColumn,
  type MenuEntry,
} from '@/components/ui';
import { useToast } from '@/components/ui/toastContext';
import { api } from '@/lib/api/client';
import { errorMessage } from '@/lib/api/errors';
import { Permissions } from '@/lib/auth/permissions';
import { useAuth } from '@/lib/auth/useAuth';
import { formatDate } from '@/lib/format/dates';
import { useRateGroups } from '@/features/rates/api/queries';
import { PeoplePicker, type PickedPerson } from '@/features/rates/components/PeoplePicker';
import '@/features/rates/rates.css';
import { invalidateCodes, useDiscountCode, useProgramCodes } from '../api/queries';
import type {
  AutoAssignResult,
  CodeImportResult,
  CodeIssue,
  CodeProgram,
  DiscountCode,
  DiscountCodeDetail,
} from '../api/types';
import { CODE_STATUS_OPTIONS, CodeStatusBadge } from '../labels';

function Issues({ title, issues, tone }: { title: string; issues: CodeIssue[]; tone: 'danger' | 'warning' }) {
  if (issues.length === 0) return null;
  return (
    <Alert tone={tone} title={`${title} (${issues.length})`}>
      <ul className="dc-perks dc-issues">
        {issues.slice(0, 200).map((i, n) => (
          <li key={n}>
            {i.row !== null && <strong>Line {i.row}: </strong>}
            {i.value && <span className="dc-mono">{i.value} </span>}— {i.message}
          </li>
        ))}
      </ul>
    </Alert>
  );
}

/** Codes of a program: list, add, CSV import, generate, assign to people / groups, pause / retire. */
export function ProgramCodesTab({ program }: { program: CodeProgram }) {
  const { hasPermission } = useAuth();
  const canManage = hasPermission(Permissions.CodesManage);
  const canAssign = hasPermission(Permissions.CodesAssign);
  const toast = useToast();
  const client = useQueryClient();
  const [search, setSearch] = useState('');
  const [filters, setFilters] = useState<Record<string, string | undefined>>({});
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(25);
  const [dialog, setDialog] = useState<'add' | 'import' | 'generate' | 'auto' | null>(null);
  const [assigning, setAssigning] = useState<DiscountCode | null>(null);
  const [history, setHistory] = useState<string | null>(null);
  const [confirm, setConfirm] = useState<{
    code: DiscountCode;
    action: 'unassign' | 'Paused' | 'Available' | 'Retired';
  } | null>(null);
  const query = useProgramCodes(program.id, { search, status: filters.status, page, pageSize });

  const act = useMutation({
    mutationFn: async ({ code, action, reason }: { code: DiscountCode; action: string; reason: string }) =>
      action === 'unassign'
        ? api.post(`/admin/discount-codes/${code.id}/unassign`, { reason })
        : api.put(`/admin/discount-codes/${code.id}`, {
            status: action,
            validFrom: code.validFrom,
            validTo: code.validTo,
            note: code.note,
            reason: reason || null,
            concurrencyStamp: code.concurrencyStamp,
          }),
    onSuccess: async (_, v) => {
      toast.success(
        v.action === 'unassign'
          ? 'Code unassigned'
          : v.action === 'Retired'
            ? 'Code retired'
            : v.action === 'Paused'
              ? 'Code paused'
              : 'Code resumed',
      );
      await invalidateCodes(client);
    },
  });

  const columns: DataTableColumn<DiscountCode>[] = [
    {
      id: 'code',
      header: 'Code',
      primary: true,
      cell: (c) => (
        <button
          type="button"
          className="ui-link dc-mono dc-strong"
          style={{ background: 'none', border: 0, padding: 0 }}
          onClick={() => setHistory(c.id)}
        >
          {c.code}
        </button>
      ),
    },
    { id: 'status', header: 'Status', cell: (c) => <CodeStatusBadge status={c.status} /> },
    {
      id: 'holder',
      header: 'Assigned to',
      cell: (c) =>
        c.assignment ? (
          <span className="cluster dc-cluster-sm">
            {c.assignment.person?.displayName ?? c.assignment.group?.name}
            {c.assignment.target === 'Group' && (
              <Badge size="sm" tone="info">
                Shared
              </Badge>
            )}
          </span>
        ) : (
          <span className="text-muted">—</span>
        ),
    },
    {
      id: 'valid',
      header: 'Valid until',
      cell: (c) => (c.validTo ? formatDate(c.validTo) : <span className="text-muted">Program end</span>),
    },
    { id: 'sales', header: 'Sales', align: 'right', cell: (c) => c.sales },
    {
      id: 'source',
      header: 'Source',
      hideOnMobile: true,
      cell: (c) => <span className="text-small">{c.source}</span>,
    },
  ];

  const rowActions = (c: DiscountCode): MenuEntry[] => {
    const items: MenuEntry[] = [
      { id: 'history', label: 'Details and history', onSelect: () => setHistory(c.id) },
    ];
    if (canAssign && c.status !== 'Retired' && c.status !== 'Expired' && c.status !== 'Paused')
      items.push({
        id: 'assign',
        label: c.assignment ? 'Reassign…' : 'Assign…',
        onSelect: () => setAssigning(c),
      });
    if (canAssign && c.assignment)
      items.push({
        id: 'unassign',
        label: 'Unassign',
        onSelect: () => setConfirm({ code: c, action: 'unassign' }),
      });
    if (canManage && (c.status === 'Available' || c.status === 'Assigned'))
      items.push({ id: 'pause', label: 'Pause', onSelect: () => setConfirm({ code: c, action: 'Paused' }) });
    if (canManage && c.status === 'Paused')
      items.push({
        id: 'resume',
        label: 'Resume',
        onSelect: () => setConfirm({ code: c, action: 'Available' }),
      });
    if (canManage && c.status !== 'Retired')
      items.push({
        id: 'retire',
        label: 'Retire',
        danger: true,
        onSelect: () => setConfirm({ code: c, action: 'Retired' }),
      });
    return items;
  };

  return (
    <div className="stack">
      <FilterBar
        search={search}
        onSearchChange={(s) => {
          setSearch(s);
          setPage(1);
        }}
        searchLabel="Search codes"
        searchPlaceholder="Code…"
        filters={[{ id: 'status', label: 'Status', options: CODE_STATUS_OPTIONS }]}
        values={filters}
        onFilterChange={(id, value) => {
          setFilters((f) => ({ ...f, [id]: value }));
          setPage(1);
        }}
        onReset={() => {
          setSearch('');
          setFilters({});
          setPage(1);
        }}
        actions={
          <span className="cluster dc-cluster-sm">
            {canAssign && (
              <Button
                size="sm"
                variant="secondary"
                leadingIcon={<UsersRound />}
                onClick={() => setDialog('auto')}
              >
                Give a group one code each
              </Button>
            )}
            {canManage && (
              <>
                <Button
                  size="sm"
                  variant="secondary"
                  leadingIcon={<Sparkles />}
                  onClick={() => setDialog('generate')}
                >
                  Generate
                </Button>
                <Button
                  size="sm"
                  variant="secondary"
                  leadingIcon={<FileUp />}
                  onClick={() => setDialog('import')}
                >
                  Import CSV
                </Button>
                <Button size="sm" leadingIcon={<Plus />} onClick={() => setDialog('add')}>
                  Add code
                </Button>
              </>
            )}
          </span>
        }
      />
      {query.isError ? (
        <ErrorState error={query.error} onRetry={() => void query.refetch()} />
      ) : (
        <>
          <DataTable
            caption="Codes"
            columns={columns}
            rows={query.data?.items ?? []}
            getRowId={(c) => c.id}
            rowLabel={(c) => `Code ${c.code}`}
            rowActions={rowActions}
            loading={query.isLoading}
            emptyState={
              <EmptyState
                icon={<TicketPercent />}
                headingLevel={3}
                title={search || filters.status ? 'No codes match' : 'No codes yet'}
                description="Import the codes the brand sent you, add them one by one or generate them from a pattern."
              />
            }
          />
          {query.data && query.data.total > 0 && (
            <Pagination
              page={page}
              pageSize={pageSize}
              total={query.data.total}
              onPageChange={setPage}
              onPageSizeChange={(n) => {
                setPageSize(n);
                setPage(1);
              }}
            />
          )}
        </>
      )}
      {dialog === 'add' && <AddCodeDialog program={program} onClose={() => setDialog(null)} />}
      {dialog === 'import' && <ImportCodesDialog program={program} onClose={() => setDialog(null)} />}
      {dialog === 'generate' && <GenerateCodesDialog program={program} onClose={() => setDialog(null)} />}
      {dialog === 'auto' && <AutoAssignDialog program={program} onClose={() => setDialog(null)} />}
      {assigning && <AssignCodeDialog code={assigning} onClose={() => setAssigning(null)} />}
      {history && <CodeHistoryDialog codeId={history} onClose={() => setHistory(null)} />}
      <ConfirmDialog
        open={!!confirm}
        onClose={() => setConfirm(null)}
        title={
          confirm?.action === 'unassign'
            ? `Unassign ${confirm.code.code}?`
            : confirm?.action === 'Retired'
              ? `Retire ${confirm?.code.code}?`
              : confirm?.action === 'Paused'
                ? `Pause ${confirm?.code.code}?`
                : `Resume ${confirm?.code.code}?`
        }
        description={
          confirm?.action === 'unassign'
            ? 'The holder keeps sales already reported and can still report orders from while they held it. History is kept.'
            : confirm?.action === 'Retired'
              ? 'Retiring is final: the code can’t be assigned or used for new sales again.'
              : confirm?.action === 'Paused'
                ? 'No new sales can be reported with it until it is resumed.'
                : 'Sales can be reported with the code again.'
        }
        tone={confirm?.action === 'Retired' || confirm?.action === 'unassign' ? 'danger' : 'primary'}
        requireReason={confirm?.action === 'unassign' || confirm?.action === 'Retired'}
        reasonMinLength={5}
        confirmLabel={
          confirm?.action === 'unassign'
            ? 'Unassign'
            : confirm?.action === 'Retired'
              ? 'Retire code'
              : 'Confirm'
        }
        onConfirm={async ({ reason }) => {
          if (!confirm) return;
          await act.mutateAsync({ code: confirm.code, action: confirm.action, reason });
          setConfirm(null);
        }}
      />
    </div>
  );
}

function AddCodeDialog({ program, onClose }: { program: CodeProgram; onClose: () => void }) {
  const toast = useToast();
  const client = useQueryClient();
  const [code, setCode] = useState('');
  const [validTo, setValidTo] = useState('');
  const [note, setNote] = useState('');
  const save = useMutation({
    mutationFn: () =>
      api.post<DiscountCode>(`/admin/code-programs/${program.id}/codes`, {
        code: code.trim(),
        validTo: validTo ? `${validTo}T23:59:59Z` : null,
        note: note.trim() || null,
      }),
    onSuccess: async (c) => {
      toast.success('Code added', c.code);
      await invalidateCodes(client);
      onClose();
    },
  });
  return (
    <Dialog
      open
      onClose={onClose}
      title="Add a code"
      description={`A code ${program.brandName} gave you. Codes are unique in the program (not case-sensitive).`}
      dismissible={!save.isPending}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={save.isPending}>
            Cancel
          </Button>
          <Button onClick={() => save.mutate()} loading={save.isPending} disabled={code.trim().length < 2}>
            Add code
          </Button>
        </>
      }
    >
      <div className="stack">
        {save.isError && (
          <Alert tone="danger" title="Not added">
            {errorMessage(save.error)}
          </Alert>
        )}
        <FormField label="Code" required hint="Letters, digits, - and _.">
          <Input
            value={code}
            maxLength={64}
            onChange={(e) => setCode(e.target.value)}
            className="dc-mono"
            autoComplete="off"
          />
        </FormField>
        <FormField label="Valid until" optional hint="Leave empty to follow the program’s end date.">
          <Input type="date" value={validTo} onChange={(e) => setValidTo(e.target.value)} />
        </FormField>
        <FormField label="Note" optional>
          <Input value={note} maxLength={300} onChange={(e) => setNote(e.target.value)} />
        </FormField>
      </div>
    </Dialog>
  );
}

function ImportCodesDialog({ program, onClose }: { program: CodeProgram; onClose: () => void }) {
  const toast = useToast();
  const client = useQueryClient();
  const fileId = useId();
  const [file, setFile] = useState<File | null>(null);
  const [report, setReport] = useState<CodeImportResult | null>(null);
  const run = useMutation({
    mutationFn: (dryRun: boolean) => {
      const form = new FormData();
      form.append('file', file!);
      return api.upload<CodeImportResult>(
        `/admin/code-programs/${program.id}/codes/import?dryRun=${dryRun}`,
        form,
      );
    },
    onSuccess: async (r) => {
      setReport(r);
      if (!r.dryRun) {
        toast.success(`Imported ${r.created} codes`);
        await invalidateCodes(client);
      }
    },
  });
  const checked = report?.dryRun === true;
  return (
    <Dialog
      open
      onClose={onClose}
      size="lg"
      title="Import codes from the brand"
      description="A CSV with a “code” column; optional validFrom, validTo (YYYY-MM-DD), note, and email (assigns the code to that participant). Up to 50,000 rows and 10 MB. Check the file first; duplicates are never imported."
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
            disabled={!checked || report!.valid === 0}
          >
            Import {checked ? report!.valid : ''} codes
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
        <FormField label="CSV file" id={fileId} required>
          <input
            id={fileId}
            type="file"
            accept=".csv,text/csv"
            className="dc-file"
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
            {report.rows} rows ·{' '}
            {report.dryRun ? `${report.valid} can be imported` : `${report.created} imported`} ·{' '}
            {report.duplicates} already in the program · {report.rejected.length} rejected
            {report.sample.length > 0 && (
              <>
                {' '}
                · e.g. <span className="dc-mono">{report.sample.join(', ')}</span>
              </>
            )}
          </Alert>
        )}
        {report && <Issues title="Rejected rows" issues={report.rejected} tone="danger" />}
        {report && <Issues title="Warnings" issues={report.warnings} tone="warning" />}
      </div>
    </Dialog>
  );
}

function GenerateCodesDialog({ program, onClose }: { program: CodeProgram; onClose: () => void }) {
  const toast = useToast();
  const client = useQueryClient();
  const [pattern, setPattern] = useState(
    `${
      program.brandName
        .replace(/[^A-Za-z]/g, '')
        .slice(0, 5)
        .toUpperCase() || 'CODE'
    }-????-##`,
  );
  const [count, setCount] = useState('20');
  const [preview, setPreview] = useState<CodeImportResult | null>(null);
  const run = useMutation({
    mutationFn: (dryRun: boolean) =>
      api.post<CodeImportResult>(`/admin/code-programs/${program.id}/codes/generate?dryRun=${dryRun}`, {
        pattern,
        count: Number(count),
      }),
    onSuccess: async (r) => {
      setPreview(r);
      if (!r.dryRun) {
        toast.success(`Generated ${r.created} codes`);
        await invalidateCodes(client);
        onClose();
      }
    },
  });
  return (
    <Dialog
      open
      onClose={onClose}
      title="Generate codes"
      description="For brands that let you create codes: # is a digit, ? a letter, * either (0/O and 1/I/L are never used). Make sure the brand’s checkout accepts them."
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button
            variant="secondary"
            onClick={() => run.mutate(true)}
            loading={run.isPending && run.variables}
          >
            Preview
          </Button>
          <Button
            onClick={() => run.mutate(false)}
            loading={run.isPending && !run.variables}
            disabled={!preview}
          >
            Generate {count} codes
          </Button>
        </>
      }
    >
      <div className="stack">
        {run.isError && (
          <Alert tone="danger" title="Not generated">
            {errorMessage(run.error)}
          </Alert>
        )}
        <div className="dc-form-grid">
          <FormField label="Pattern" required>
            <Input
              value={pattern}
              onChange={(e) => {
                setPattern(e.target.value);
                setPreview(null);
              }}
              className="dc-mono"
            />
          </FormField>
          <FormField label="How many" required>
            <Input
              type="number"
              min={1}
              max={5000}
              value={count}
              onChange={(e) => {
                setCount(e.target.value);
                setPreview(null);
              }}
            />
          </FormField>
        </div>
        {preview && (
          <Alert tone="info" title="Preview">
            <span className="dc-mono">{preview.sample.join(', ')}</span>…
          </Alert>
        )}
      </div>
    </Dialog>
  );
}

function AssignCodeDialog({ code, onClose }: { code: DiscountCode; onClose: () => void }) {
  const toast = useToast();
  const client = useQueryClient();
  const [target, setTarget] = useState<'Person' | 'Group'>('Person');
  const [people, setPeople] = useState<PickedPerson[]>([]);
  const [groupId, setGroupId] = useState('');
  const [validFrom, setValidFrom] = useState('');
  const [validTo, setValidTo] = useState('');
  const [reason, setReason] = useState('');
  const groups = useRateGroups({ mode: 'Manual', page: 1, pageSize: 100 });
  const save = useMutation({
    mutationFn: () =>
      api.post<DiscountCodeDetail>(`/admin/discount-codes/${code.id}/assign`, {
        target,
        userId: target === 'Person' ? people[0]?.id : null,
        groupId: target === 'Group' ? groupId : null,
        validFrom: validFrom ? `${validFrom}T00:00:00Z` : null,
        validTo: validTo ? `${validTo}T23:59:59Z` : null,
        reassign: !!code.assignment,
        reason: reason.trim(),
      }),
    onSuccess: async () => {
      toast.success(code.assignment ? 'Code reassigned' : 'Code assigned', code.code);
      await invalidateCodes(client);
      onClose();
    },
  });
  const ready = (target === 'Person' ? people.length === 1 : !!groupId) && reason.trim().length >= 5;
  return (
    <Dialog
      open
      onClose={onClose}
      size="lg"
      title={`${code.assignment ? 'Reassign' : 'Assign'} ${code.code}`}
      description={
        code.assignment
          ? `Currently with ${code.assignment.person?.displayName ?? code.assignment.group?.name}. Their assignment ends when the new one starts; orders placed before stay theirs.`
          : 'A personal code is used by one person; a shared code by every member of a rate group (each sale goes to whoever reports it first).'
      }
      dismissible={!save.isPending}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={save.isPending}>
            Cancel
          </Button>
          <Button onClick={() => save.mutate()} loading={save.isPending} disabled={!ready}>
            {code.assignment ? 'Reassign' : 'Assign'}
          </Button>
        </>
      }
    >
      <div className="stack">
        {save.isError && (
          <Alert tone="danger" title="Not assigned">
            {errorMessage(save.error)}
          </Alert>
        )}
        <RadioGroup
          legend="Assign to"
          orientation="horizontal"
          value={target}
          onChange={(v) => setTarget(v as 'Person' | 'Group')}
          options={[
            { value: 'Person', label: 'One person (personal code)' },
            { value: 'Group', label: 'A rate group (shared code)' },
          ]}
        />
        {target === 'Person' ? (
          <PeoplePicker label="Participant" single value={people} onChange={setPeople} />
        ) : (
          <FormField label="Rate group" required hint="Manage groups under Rate groups.">
            <Select
              value={groupId}
              onChange={(e) => setGroupId(e.target.value)}
              placeholder="Choose a group"
              options={(groups.data?.items ?? []).map((g) => ({
                value: g.id,
                label: `${g.name} (${g.memberCount ?? 0})`,
              }))}
            />
          </FormField>
        )}
        <div className="dc-form-grid">
          <FormField label="From" optional hint="Default now. Orders are attributed by order date.">
            <Input type="date" value={validFrom} onChange={(e) => setValidFrom(e.target.value)} />
          </FormField>
          <FormField label="Until" optional>
            <Input type="date" value={validTo} onChange={(e) => setValidTo(e.target.value)} />
          </FormField>
        </div>
        <FormField label="Reason" required hint="Kept in the code’s history and the audit log.">
          <Textarea rows={2} maxLength={500} value={reason} onChange={(e) => setReason(e.target.value)} />
        </FormField>
      </div>
    </Dialog>
  );
}

function AutoAssignDialog({ program, onClose }: { program: CodeProgram; onClose: () => void }) {
  const toast = useToast();
  const client = useQueryClient();
  const [groupId, setGroupId] = useState('');
  const [reason, setReason] = useState('');
  const [preview, setPreview] = useState<AutoAssignResult | null>(null);
  const groups = useRateGroups({ mode: 'Manual', page: 1, pageSize: 100 });
  const run = useMutation({
    mutationFn: (dryRun: boolean) =>
      api.post<AutoAssignResult>(`/admin/code-programs/${program.id}/codes/auto-assign`, {
        groupId,
        reason: reason.trim(),
        dryRun,
      }),
    onSuccess: async (r) => {
      setPreview(r);
      if (!r.dryRun) {
        toast.success(`${r.assigned} codes assigned`);
        await invalidateCodes(client);
      }
    },
  });
  const ready = !!groupId && reason.trim().length >= 5;
  return (
    <Dialog
      open
      onClose={onClose}
      size="lg"
      title="Give every member of a group their own code"
      description="Each member without a personal code in this program gets the next available code. Members who already have one are skipped."
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            {preview && !preview.dryRun ? 'Done' : 'Cancel'}
          </Button>
          <Button
            variant="secondary"
            onClick={() => run.mutate(true)}
            loading={run.isPending && run.variables}
            disabled={!ready}
          >
            Preview
          </Button>
          <Button
            onClick={() => run.mutate(false)}
            loading={run.isPending && !run.variables}
            disabled={!ready || !preview?.dryRun}
          >
            Assign {preview?.dryRun ? preview.assigned : ''} codes
          </Button>
        </>
      }
    >
      <div className="stack">
        {run.isError && (
          <Alert tone="danger" title="Not assigned">
            {errorMessage(run.error)}
          </Alert>
        )}
        <FormField label="Rate group" required>
          <Select
            value={groupId}
            onChange={(e) => {
              setGroupId(e.target.value);
              setPreview(null);
            }}
            placeholder="Choose a group"
            options={(groups.data?.items ?? []).map((g) => ({
              value: g.id,
              label: `${g.name} (${g.memberCount ?? 0})`,
            }))}
          />
        </FormField>
        <FormField label="Reason" required>
          <Textarea rows={2} maxLength={500} value={reason} onChange={(e) => setReason(e.target.value)} />
        </FormField>
        {preview && (
          <Alert
            tone={preview.skipped > 0 ? 'warning' : 'success'}
            title={preview.dryRun ? 'Preview' : 'Done'}
          >
            {preview.members} members · {preview.assigned} {preview.dryRun ? 'will get' : 'got'} a code ·{' '}
            {preview.alreadyHadCode} already had one · {preview.skipped} skipped · {preview.availableCodes}{' '}
            codes available
          </Alert>
        )}
        {preview && <Issues title="Notes" issues={preview.issues} tone="warning" />}
      </div>
    </Dialog>
  );
}

function CodeHistoryDialog({ codeId, onClose }: { codeId: string; onClose: () => void }) {
  const query = useDiscountCode(codeId);
  const d = query.data;
  return (
    <Dialog
      open
      onClose={onClose}
      size="lg"
      title={d ? `Code ${d.code.code}` : 'Code'}
      description={d ? `${d.program.brandName} — ${d.program.name}` : undefined}
    >
      {query.isError ? (
        <ErrorState error={query.error} compact />
      ) : !d ? (
        <p>Loading…</p>
      ) : (
        <div className="stack">
          <span className="cluster dc-cluster-sm">
            <CodeStatusBadge status={d.code.status} />
            <span className="text-small text-muted">
              {d.code.sales} sales · added <DateTime value={d.code.createdAt} /> ({d.code.source})
            </span>
          </span>
          {d.code.note && <p className="text-small">{d.code.note}</p>}
          <DataTable
            caption="Assignment history"
            showCaption
            rows={d.history}
            getRowId={(a) => a.id}
            columns={[
              {
                id: 'who',
                header: 'Holder',
                primary: true,
                cell: (a) =>
                  (a.person?.displayName ?? a.group?.name ?? '—') + (a.target === 'Group' ? ' (group)' : ''),
              },
              { id: 'from', header: 'From', cell: (a) => formatDate(a.validFrom) },
              {
                id: 'to',
                header: 'Until',
                cell: (a) => (a.endedAt ? formatDate(a.endedAt) : a.validTo ? formatDate(a.validTo) : '—'),
              },
              {
                id: 'state',
                header: 'State',
                cell: (a) =>
                  a.isLive ? (
                    <Badge tone="success" size="sm">
                      Live
                    </Badge>
                  ) : (
                    <span className="text-small">{a.endReason ?? 'Ended'}</span>
                  ),
              },
              {
                id: 'by',
                header: 'By',
                hideOnMobile: true,
                cell: (a) => (
                  <span className="text-small">
                    {a.createdBy?.displayName} — {a.reason}
                  </span>
                ),
              },
            ]}
            emptyState={<EmptyState compact headingLevel={3} title="Never assigned" />}
          />
        </div>
      )}
    </Dialog>
  );
}
