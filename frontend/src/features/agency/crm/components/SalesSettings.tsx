import { Pencil, Plus, Trash2 } from 'lucide-react';
import { useEffect, useState } from 'react';
import {
  Badge,
  Button,
  Checkbox,
  ConfirmDialog,
  DataTable,
  EmptyState,
  ErrorState,
  FormField,
  Input,
  Select,
  Skeleton,
  Textarea,
  useToast,
  type DataTableColumn,
} from '@/components/ui';
import { FormDialog } from '@/features/agency/billing/components/FormDialog';
import type { PriceLineInput } from '@/features/agency/billing/api/types';
import { LineItemsEditor, emptyLine } from '@/features/agency/billing/components/LineItemsEditor';
import { billingErrorMessage } from '@/features/agency/billing/lib';
import { useSupportedCurrencies } from '@/lib/api/meta';
import { useCrmOptions, useDeleteProposalTemplate, useProposalTemplates, useSaveCrmOptions, useSaveProposalTemplate } from '../api/hooks';
import type { ProposalTemplate } from '../api/types';

/** One entry per line → trimmed list without blanks. */
function toList(text: string): string[] {
  return text
    .split('\n')
    .map((v) => v.trim())
    .filter(Boolean);
}

/** Agency-editable CRM option lists: lost reasons, budget ranges and industry suggestions. */
export function CrmOptionsEditor({ canEdit }: { canEdit: boolean }) {
  const options = useCrmOptions();
  const save = useSaveCrmOptions();
  const toast = useToast();
  const [lost, setLost] = useState('');
  const [budgets, setBudgets] = useState('');
  const [industries, setIndustries] = useState('');
  const [error, setError] = useState<unknown>(null);
  useEffect(() => {
    if (!options.data) return;
    setLost(options.data.lostReasons.join('\n'));
    setBudgets(options.data.budgetRanges.join('\n'));
    setIndustries(options.data.industries.join('\n'));
  }, [options.data]);
  if (options.isError) return <ErrorState error={options.error} onRetry={() => void options.refetch()} />;
  if (!options.data) return <Skeleton height="10rem" />;
  return (
    <form
      className="stack"
      onSubmit={async (e) => {
        e.preventDefault();
        setError(null);
        try {
          await save.mutateAsync({
            lostReasons: toList(lost),
            budgetRanges: toList(budgets),
            industries: toList(industries),
            version: options.data!.version,
          });
          toast.success('Options saved');
        } catch (err) {
          setError(err);
        }
      }}
    >
      <div className="crm-grid">
        <FormField label="Lost reasons" hint="One per line. Offered when a deal is moved to Lost.">
          <Textarea rows={7} value={lost} disabled={!canEdit} onChange={(e) => setLost(e.target.value)} />
        </FormField>
        <FormField label="Budget ranges" hint="One per line. Offered on contacts and deals; fit scoring rules match these values.">
          <Textarea rows={7} value={budgets} disabled={!canEdit} onChange={(e) => setBudgets(e.target.value)} />
        </FormField>
        <FormField label="Industries" hint="One per line. Suggested on companies (other values are still accepted).">
          <Textarea rows={7} value={industries} disabled={!canEdit} onChange={(e) => setIndustries(e.target.value)} />
        </FormField>
      </div>
      {error !== null && <p role="alert" className="crm-error">{billingErrorMessage(error)}</p>}
      {canEdit && (
        <div>
          <Button type="submit" loading={save.isPending}>
            Save options
          </Button>
        </div>
      )}
    </form>
  );
}

const SECTIONS = [
  { key: 'executiveSummary', label: 'Executive summary' },
  { key: 'goals', label: 'Goals' },
  { key: 'scope', label: 'Scope' },
  { key: 'deliverables', label: 'Deliverables' },
  { key: 'timeline', label: 'Timeline' },
  { key: 'terms', label: 'Terms' },
] as const;

type SectionKey = (typeof SECTIONS)[number]['key'];

function linesOf(t: ProposalTemplate | null): PriceLineInput[] {
  if (!t || t.lines.length === 0) return [emptyLine()];
  return t.lines.map((l) => ({ ...l }));
}

function TemplateDialog({ open, template, onClose }: { open: boolean; template: ProposalTemplate | null; onClose: () => void }) {
  const save = useSaveProposalTemplate(template?.id);
  const toast = useToast();
  const [name, setName] = useState('');
  const [description, setDescription] = useState('');
  const [title, setTitle] = useState('');
  const [currency, setCurrency] = useState('USD');
  const [validFor, setValidFor] = useState('30');
  const [active, setActive] = useState(true);
  const [sections, setSections] = useState<Record<SectionKey, string>>({ executiveSummary: '', goals: '', scope: '', deliverables: '', timeline: '', terms: '' });
  const [lines, setLines] = useState<PriceLineInput[]>([emptyLine()]);
  const currencies = useSupportedCurrencies(currency);
  useEffect(() => {
    if (!open) return;
    setName(template?.name ?? '');
    setDescription(template?.description ?? '');
    setTitle(template?.proposalTitle ?? '');
    setCurrency(template?.currency ?? 'USD');
    setValidFor(String(template?.validForDays ?? 30));
    setActive(template?.isActive ?? true);
    setSections({
      executiveSummary: template?.executiveSummary ?? '',
      goals: template?.goals ?? '',
      scope: template?.scope ?? '',
      deliverables: template?.deliverables ?? '',
      timeline: template?.timeline ?? '',
      terms: template?.terms ?? '',
    });
    setLines(linesOf(template));
  }, [open, template]);
  const filled = lines.filter((l) => l.description.trim());
  return (
    <FormDialog
      open={open}
      onClose={onClose}
      size="lg"
      title={template ? `Edit template “${template.name}”` : 'New proposal template'}
      description="Using a template copies its sections and lines into a new proposal. Changing it later never changes existing proposals."
      submitLabel="Save template"
      canSubmit={name.trim().length >= 2}
      onSubmit={async () => {
        await save.mutateAsync({
          name: name.trim(),
          description: description.trim() || null,
          proposalTitle: title.trim() || null,
          currency,
          validForDays: Number(validFor) || 30,
          ...Object.fromEntries(SECTIONS.map((s) => [s.key, sections[s.key].trim() || null])),
          lines: filled.map((l) => ({ ...l, description: l.description.trim() })),
          sortOrder: template?.sortOrder ?? 100,
          isActive: active,
          concurrencyStamp: template?.concurrencyStamp,
        });
        toast.success('Template saved');
      }}
    >
      <div className="crm-grid">
        <FormField label="Template name" required>
          <Input value={name} maxLength={120} onChange={(e) => setName(e.target.value)} />
        </FormField>
        <FormField label="Default proposal title" optional>
          <Input value={title} maxLength={200} onChange={(e) => setTitle(e.target.value)} />
        </FormField>
        <FormField label="Currency of the prices">
          <Select value={currency} options={currencies.options} onChange={(e) => setCurrency(e.target.value)} />
        </FormField>
        <FormField label="Valid for (days)">
          <Input type="number" min={1} max={365} value={validFor} onChange={(e) => setValidFor(e.target.value)} />
        </FormField>
      </div>
      <FormField label="Description" optional>
        <Input value={description} maxLength={1000} onChange={(e) => setDescription(e.target.value)} />
      </FormField>
      {SECTIONS.map((sec) => (
        <FormField key={sec.key} label={sec.label} optional>
          <Textarea rows={3} maxLength={20000} value={sections[sec.key]} onChange={(e) => setSections({ ...sections, [sec.key]: e.target.value })} />
        </FormField>
      ))}
      <LineItemsEditor lines={lines} onChange={setLines} currency={currency} allowRecurrence />
      <Checkbox label="Active (offered when creating proposals)" checked={active} onChange={(e) => setActive(e.target.checked)} />
    </FormDialog>
  );
}

/** Manage reusable proposal templates. */
export function ProposalTemplatesManager({ canEdit }: { canEdit: boolean }) {
  const templates = useProposalTemplates(true);
  const remove = useDeleteProposalTemplate();
  const toast = useToast();
  const [editing, setEditing] = useState<ProposalTemplate | null>(null);
  const [open, setOpen] = useState(false);
  const [deleting, setDeleting] = useState<ProposalTemplate | null>(null);
  const columns: DataTableColumn<ProposalTemplate>[] = [
    { id: 'name', header: 'Template', primary: true, cell: (t) => t.name },
    { id: 'lines', header: 'Lines', align: 'right', cell: (t) => t.lines.length },
    { id: 'currency', header: 'Currency', cell: (t) => t.currency },
    { id: 'status', header: 'Status', cell: (t) => (t.isActive ? <Badge tone="success">Active</Badge> : <Badge tone="neutral">Inactive</Badge>) },
  ];
  if (templates.isError) return <ErrorState error={templates.error} onRetry={() => void templates.refetch()} />;
  return (
    <div className="stack">
      {canEdit && (
        <div>
          <Button
            variant="secondary"
            leadingIcon={<Plus />}
            onClick={() => {
              setEditing(null);
              setOpen(true);
            }}
          >
            New template
          </Button>
        </div>
      )}
      <DataTable
        caption="Proposal templates"
        columns={columns}
        rows={templates.data ?? []}
        getRowId={(t) => t.id}
        loading={templates.isPending}
        rowLabel={(t) => t.name}
        rowActions={
          canEdit
            ? (t) => [
                {
                  id: 'edit',
                  label: 'Edit',
                  icon: <Pencil />,
                  onSelect: () => {
                    setEditing(t);
                    setOpen(true);
                  },
                },
                { id: 'delete', label: 'Delete', icon: <Trash2 />, danger: true, onSelect: () => setDeleting(t) },
              ]
            : undefined
        }
        emptyState={<EmptyState compact headingLevel={3} title="No proposal templates" description="Create one to start proposals faster." />}
      />
      <TemplateDialog open={open} template={editing} onClose={() => setOpen(false)} />
      <ConfirmDialog
        open={deleting !== null}
        onClose={() => setDeleting(null)}
        tone="danger"
        title={`Delete the template “${deleting?.name ?? ''}”?`}
        description="Proposals created from it are not affected."
        confirmLabel="Delete"
        onConfirm={async () => {
          if (!deleting) return;
          try {
            await remove.mutateAsync(deleting.id);
            toast.success('Template deleted');
          } catch (error) {
            toast.error('Couldn’t delete the template', billingErrorMessage(error));
            throw error;
          }
        }}
      />
    </div>
  );
}
