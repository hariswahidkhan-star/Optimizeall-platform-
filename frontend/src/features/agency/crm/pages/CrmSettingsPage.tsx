import { ArrowDown, ArrowUp, Plus, RefreshCw, Trash2 } from 'lucide-react';
import { useEffect, useState } from 'react';
import {
  Alert,
  Badge,
  Button,
  Card,
  CardBody,
  CardHeader,
  Checkbox,
  DataTable,
  ErrorState,
  FormField,
  IconButton,
  Input,
  PageHeader,
  Select,
  Skeleton,
  useToast,
  type DataTableColumn,
} from '@/components/ui';
import { FormDialog } from '@/features/agency/billing/components/FormDialog';
import { billingErrorMessage } from '@/features/agency/billing/lib';
import { Permissions } from '@/lib/auth/permissions';
import { useAuth } from '@/lib/auth/useAuth';
import { useDeleteRule, useRecomputeScores, useSaveRule, useSaveStages, useScoringRules, useStages } from '../api/hooks';
import type { ScoringCategory, ScoringRule, StageKind } from '../api/types';
import { CrmOptionsEditor, ProposalTemplatesManager } from '../components/SalesSettings';
import '@/features/agency/billing/billing.css';
import '../crm.css';

interface EditableStage {
  id: string | null;
  name: string;
  winProbability: number;
  kind: StageKind;
  isActive: boolean;
  /** Stamp of the stage as loaded (a concurrent edit answers 409 instead of being overwritten). */
  concurrencyStamp?: string;
}

function PipelineEditor({ canEdit }: { canEdit: boolean }) {
  const stages = useStages();
  const save = useSaveStages();
  const toast = useToast();
  const [rows, setRows] = useState<EditableStage[] | null>(null);
  const [error, setError] = useState<unknown>(null);
  useEffect(() => {
    if (stages.data && rows === null)
      setRows(stages.data.map((s) => ({ id: s.id, name: s.name, winProbability: s.winProbability, kind: s.kind, isActive: s.isActive, concurrencyStamp: s.concurrencyStamp })));
  }, [stages.data, rows]);
  if (stages.isError) return <ErrorState error={stages.error} onRetry={() => void stages.refetch()} />;
  if (!rows) return <Skeleton height="12rem" />;
  const open = rows.filter((r) => r.kind === 'Open');
  const closed = rows.filter((r) => r.kind !== 'Open');
  const setOpen = (next: EditableStage[]) => setRows([...next, ...closed]);
  const update = (index: number, patch: Partial<EditableStage>) => setOpen(open.map((r, i) => (i === index ? { ...r, ...patch } : r)));
  const moveRow = (index: number, delta: number) => {
    const next = [...open];
    const [row] = next.splice(index, 1);
    next.splice(index + delta, 0, row!);
    setOpen(next);
  };
  return (
    <div className="stack">
      <ol className="crm-list" aria-label="Open stages">
        {open.map((row, index) => (
          <li key={row.id ?? `new-${index}`} className="crm-card">
            <div className="crm-grid">
              <FormField label={`Stage ${index + 1} name`}>
                <Input value={row.name} disabled={!canEdit} maxLength={80} onChange={(e) => update(index, { name: e.target.value })} />
              </FormField>
              <FormField label="Win probability (%)">
                <Input type="number" min={0} max={100} value={String(row.winProbability)} disabled={!canEdit} onChange={(e) => update(index, { winProbability: Number(e.target.value) })} />
              </FormField>
            </div>
            <div className="crm-row">
              <Checkbox label="Active" checked={row.isActive} disabled={!canEdit} onChange={(e) => update(index, { isActive: e.target.checked })} />
              {canEdit && (
                <span className="crm-actions">
                  <IconButton label={`Move ${row.name} up`} icon={<ArrowUp />} size="sm" disabled={index === 0} onClick={() => moveRow(index, -1)} />
                  <IconButton label={`Move ${row.name} down`} icon={<ArrowDown />} size="sm" disabled={index === open.length - 1} onClick={() => moveRow(index, 1)} />
                  <IconButton label={`Remove ${row.name}`} icon={<Trash2 />} size="sm" onClick={() => setOpen(open.filter((_, i) => i !== index))} />
                </span>
              )}
            </div>
          </li>
        ))}
      </ol>
      <p className="crm-muted">Closing stages: {closed.map((c) => `${c.name} (${c.kind})`).join(', ')} — always last; Won counts as 100%, Lost as 0%.</p>
      {error !== null && (
        <Alert tone="danger" role="alert">
          {billingErrorMessage(error)}
        </Alert>
      )}
      {canEdit && (
        <div className="crm-actions">
          <Button variant="secondary" size="sm" leadingIcon={<Plus />} onClick={() => setOpen([...open, { id: null, name: 'New stage', winProbability: 50, kind: 'Open', isActive: true }])}>
            Add stage
          </Button>
          <Button
            size="sm"
            loading={save.isPending}
            onClick={async () => {
              setError(null);
              try {
                const saved = await save.mutateAsync(rows.map((r) => ({ ...r, id: r.id ?? undefined })));
                setRows(saved.map((s) => ({ id: s.id, name: s.name, winProbability: s.winProbability, kind: s.kind, isActive: s.isActive, concurrencyStamp: s.concurrencyStamp })));
                toast.success('Pipeline saved');
              } catch (err) {
                setError(err);
              }
            }}
          >
            Save pipeline
          </Button>
        </div>
      )}
    </div>
  );
}

function RuleDialog({ open, rule, onClose }: { open: boolean; rule: ScoringRule | null; onClose: () => void }) {
  const save = useSaveRule(rule?.id);
  const [name, setName] = useState('');
  const [category, setCategory] = useState<ScoringCategory>('Fit');
  const [field, setField] = useState('industry');
  const [matchValue, setMatch] = useState('');
  const [points, setPoints] = useState('10');
  const [max, setMax] = useState('');
  const [active, setActive] = useState(true);
  useEffect(() => {
    if (!open) return;
    setName(rule?.name ?? '');
    setCategory(rule?.category ?? 'Fit');
    setField(rule?.field ?? 'industry');
    setMatch(rule?.matchValue ?? '');
    setPoints(String(rule?.points ?? 10));
    setMax(rule?.maxOccurrences ? String(rule.maxOccurrences) : '');
    setActive(rule?.isActive ?? true);
  }, [open, rule]);
  return (
    <FormDialog
      open={open}
      onClose={onClose}
      title={rule ? 'Edit scoring rule' : 'New scoring rule'}
      submitLabel="Save rule"
      canSubmit={name.trim().length >= 2}
      onSubmit={async () => {
        await save.mutateAsync({
          name: name.trim(),
          category,
          field: field.trim(),
          matchValue: category === 'Fit' ? matchValue.trim() : null,
          points: Number(points),
          maxOccurrences: category === 'Engagement' && max ? Number(max) : null,
          isActive: active,
          concurrencyStamp: rule?.concurrencyStamp,
        });
      }}
    >
      <FormField label="Name" required>
        <Input value={name} maxLength={120} onChange={(e) => setName(e.target.value)} />
      </FormField>
      <FormField label="Kind">
        <Select value={category} options={[{ value: 'Fit', label: 'Fit (who they are)' }, { value: 'Engagement', label: 'Engagement (what they did)' }]} onChange={(e) => setCategory(e.target.value as ScoringCategory)} />
      </FormField>
      <FormField label={category === 'Fit' ? 'Attribute' : 'Event type'} hint={category === 'Fit' ? 'industry, companySize, budgetRange, country, source or lifecycleStage' : 'e.g. form_submitted, email_clicked, meeting_booked'}>
        <Input value={field} maxLength={60} onChange={(e) => setField(e.target.value)} />
      </FormField>
      {category === 'Fit' && (
        <FormField label="Matches" hint="Comma-separated values, or * for any value">
          <Input value={matchValue} maxLength={500} onChange={(e) => setMatch(e.target.value)} />
        </FormField>
      )}
      <FormField label="Points" hint="Negative points lower the score">
        <Input type="number" min={-500} max={500} value={points} onChange={(e) => setPoints(e.target.value)} />
      </FormField>
      {category === 'Engagement' && (
        <FormField label="Count at most" optional hint="Times the event can add points">
          <Input type="number" min={1} max={100} value={max} onChange={(e) => setMax(e.target.value)} />
        </FormField>
      )}
      <Checkbox label="Active" checked={active} onChange={(e) => setActive(e.target.checked)} />
    </FormDialog>
  );
}

function ScoringRules({ canEdit }: { canEdit: boolean }) {
  const rules = useScoringRules();
  const remove = useDeleteRule();
  const recompute = useRecomputeScores();
  const toast = useToast();
  const [editing, setEditing] = useState<ScoringRule | null>(null);
  const [open, setOpen] = useState(false);
  const columns: DataTableColumn<ScoringRule>[] = [
    { id: 'name', header: 'Rule', primary: true, cell: (r) => r.name },
    { id: 'kind', header: 'Kind', cell: (r) => <Badge tone={r.category === 'Fit' ? 'brand' : 'info'}>{r.category}</Badge> },
    { id: 'when', header: 'When', cell: (r) => (r.category === 'Fit' ? `${r.field} is ${r.matchValue}` : `${r.field}${r.maxOccurrences ? ` (max ${r.maxOccurrences}×)` : ''}`) },
    { id: 'points', header: 'Points', align: 'right', cell: (r) => (r.points > 0 ? `+${r.points}` : r.points) },
    { id: 'active', header: 'Active', cell: (r) => (r.isActive ? 'Yes' : 'No') },
    {
      id: 'actions',
      header: <span className="visually-hidden">Actions</span>,
      align: 'right',
      cell: (r) =>
        canEdit && (
          <span className="crm-actions">
            <Button size="sm" variant="ghost" onClick={() => { setEditing(r); setOpen(true); }}>
              Edit<span className="visually-hidden"> {r.name}</span>
            </Button>
            <Button size="sm" variant="ghost" onClick={() => void remove.mutateAsync(r.id)}>
              Delete<span className="visually-hidden"> {r.name}</span>
            </Button>
          </span>
        ),
    },
  ];
  return (
    <div className="stack">
      {canEdit && (
        <div className="crm-actions">
          <Button size="sm" leadingIcon={<Plus />} onClick={() => { setEditing(null); setOpen(true); }}>
            Add rule
          </Button>
          <Button
            size="sm"
            variant="secondary"
            leadingIcon={<RefreshCw />}
            loading={recompute.isPending}
            onClick={async () => {
              const result = await recompute.mutateAsync();
              toast.success(`Re-scored ${result.contacts} contacts`);
            }}
          >
            Re-score all contacts
          </Button>
        </div>
      )}
      <DataTable caption="Lead scoring rules" columns={columns} rows={rules.data ?? []} getRowId={(r) => r.id} loading={rules.isPending} />
      <RuleDialog open={open} rule={editing} onClose={() => setOpen(false)} />
    </div>
  );
}

export function CrmSettingsPage() {
  const { hasPermission } = useAuth();
  const canEdit = hasPermission(Permissions.CrmManage);
  return (
    <>
      <PageHeader title="CRM settings" breadcrumbs={[{ label: 'Sales CRM', to: '/agency/crm' }, { label: 'Settings' }]} />
      <div className="stack">
        <Card>
          <CardHeader title="Pipeline stages" description="Order, names and win probabilities (used for the weighted forecast)." />
          <CardBody>
            <PipelineEditor canEdit={canEdit} />
          </CardBody>
        </Card>
        <Card>
          <CardHeader title="Lead scoring" description="Scores are recalculated whenever a contact, its company or its engagement changes." />
          <CardBody>
            <ScoringRules canEdit={canEdit} />
          </CardBody>
        </Card>
        <Card>
          <CardHeader title="Options" description="Lists offered in forms across the CRM. Changing them never rewrites existing records." />
          <CardBody>
            <CrmOptionsEditor canEdit={canEdit} />
          </CardBody>
        </Card>
        {/* The proposal-templates API (reads too) needs proposals.manage; CRM viewers such as strategists lack it. */}
        {hasPermission(Permissions.ProposalsManage) ? (
          <Card>
            <CardHeader title="Proposal templates" description="Reusable sections and price lines to start proposals from." />
            <CardBody>
              <ProposalTemplatesManager canEdit />
            </CardBody>
          </Card>
        ) : null}
      </div>
    </>
  );
}
