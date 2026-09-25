import { useMutation, useQueryClient } from '@tanstack/react-query';
import { FileSpreadsheet, Plus } from 'lucide-react';
import { useId, useState } from 'react';
import { Alert, Button, DataTable, Dialog, FormField, Input, Select, Textarea } from '@/components/ui';
import { useToast } from '@/components/ui/toastContext';
import { api } from '@/lib/api/client';
import { errorMessage } from '@/lib/api/errors';
import { useSupportedCurrencies } from '@/lib/api/meta';
import { Permissions } from '@/lib/auth/permissions';
import { useAuth } from '@/lib/auth/useAuth';
import { PeoplePicker, type PickedPerson } from '@/features/rates/components/PeoplePicker';
import '@/features/rates/rates.css';
import { invalidateCodes } from '../api/queries';
import type { CodeProgram, CodeSale, SalesImportResult } from '../api/types';
import { NET_HELP } from '../labels';
import { CodeSalesTable } from '../staff/CodeSalesTable';

/** Sales of one program: list, staff-entered sales and the brand's sales-report import (reconciliation). */
export function ProgramSalesTab({ program }: { program: CodeProgram }) {
  const { hasPermission } = useAuth();
  const canManage = hasPermission(Permissions.CodesManage);
  const [dialog, setDialog] = useState<'add' | 'import' | null>(null);
  return (
    <div className="stack">
      {canManage && (
        <div className="cluster dc-cluster-sm" style={{ justifyContent: 'flex-end' }}>
          <Button
            size="sm"
            variant="secondary"
            leadingIcon={<FileSpreadsheet />}
            onClick={() => setDialog('import')}
          >
            Import brand sales report
          </Button>
          <Button size="sm" leadingIcon={<Plus />} onClick={() => setDialog('add')}>
            Add a sale
          </Button>
        </div>
      )}
      <CodeSalesTable
        basePath="/manage/codes/sales"
        programId={program.id}
        caption={`Sales of ${program.name}`}
      />
      {dialog === 'add' && <AddSaleDialog program={program} onClose={() => setDialog(null)} />}
      {dialog === 'import' && <ImportSalesDialog program={program} onClose={() => setDialog(null)} />}
    </div>
  );
}

function AddSaleDialog({ program, onClose }: { program: CodeProgram; onClose: () => void }) {
  const toast = useToast();
  const client = useQueryClient();
  const [code, setCode] = useState('');
  const [people, setPeople] = useState<PickedPerson[]>([]);
  const [orderReference, setOrderReference] = useState('');
  const [orderDate, setOrderDate] = useState(() => new Date().toISOString().slice(0, 10));
  const [net, setNet] = useState('');
  const [discount, setDiscount] = useState('');
  const [currency, setCurrency] = useState(program.currency);
  const [note, setNote] = useState('');
  const [reason, setReason] = useState('');
  const currencies = useSupportedCurrencies(currency);
  const save = useMutation({
    mutationFn: () =>
      api.post<CodeSale>(`/admin/code-programs/${program.id}/sales`, {
        code: code.trim(),
        userId: people[0]?.id ?? null,
        orderReference: orderReference.trim(),
        orderDate: `${orderDate}T12:00:00Z`,
        netAmount: Number(net),
        discountAmount: discount ? Number(discount) : 0,
        currency,
        productNote: note.trim() || null,
        reason: reason.trim(),
      }),
    onSuccess: async (sale) => {
      toast.success(
        'Sale added',
        `${sale.orderReference} for ${sale.person.displayName} — another reviewer approves it.`,
      );
      await invalidateCodes(client);
      onClose();
    },
  });
  const ready =
    code.trim().length >= 2 &&
    orderReference.trim().length >= 2 &&
    Number(net) > 0 &&
    reason.trim().length >= 5;
  return (
    <Dialog
      open
      onClose={onClose}
      size="lg"
      title="Add a sale"
      description="For orders the brand told you about directly. The sale goes to the review queue; someone other than you must approve it."
      dismissible={!save.isPending}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={save.isPending}>
            Cancel
          </Button>
          <Button onClick={() => save.mutate()} loading={save.isPending} disabled={!ready}>
            Add sale
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
        <div className="dc-form-grid">
          <FormField label="Code" required>
            <Input
              value={code}
              onChange={(e) => setCode(e.target.value)}
              className="dc-mono"
              autoComplete="off"
            />
          </FormField>
          <FormField label="Order number" required>
            <Input
              value={orderReference}
              maxLength={100}
              onChange={(e) => setOrderReference(e.target.value)}
            />
          </FormField>
          <FormField label="Order date" required>
            <Input type="date" value={orderDate} onChange={(e) => setOrderDate(e.target.value)} />
          </FormField>
        </div>
        <PeoplePicker
          label="Participant"
          single
          value={people}
          onChange={setPeople}
          hint="Leave empty to credit whoever held the (personal) code on the order date. Required for shared codes."
        />
        <div className="dc-form-grid">
          <FormField label="Order value" required hint={NET_HELP}>
            <Input type="number" min={0} step="any" value={net} onChange={(e) => setNet(e.target.value)} />
          </FormField>
          <FormField label="Discount given" optional>
            <Input
              type="number"
              min={0}
              step="any"
              value={discount}
              onChange={(e) => setDiscount(e.target.value)}
            />
          </FormField>
          <FormField label="Currency" required>
            <Select
              value={currency}
              onChange={(e) => setCurrency(e.target.value)}
              options={currencies.options}
            />
          </FormField>
        </div>
        <FormField label="Products / notes" optional>
          <Input value={note} maxLength={1000} onChange={(e) => setNote(e.target.value)} />
        </FormField>
        <FormField
          label="Why are you adding it?"
          required
          hint="Kept in the sale’s history and the audit log."
        >
          <Textarea rows={2} maxLength={500} value={reason} onChange={(e) => setReason(e.target.value)} />
        </FormField>
      </div>
    </Dialog>
  );
}

const OUTCOME_LABEL: Record<string, string> = {
  mismatch: 'Differs',
  create: 'New sale (unclaimed use)',
  refund: 'Refund — commission reversed',
  cancel: 'Cancelled',
  flagged: 'Needs attention',
  rejected: 'Rejected row',
  ignored: 'Ignored',
};

function ImportSalesDialog({ program, onClose }: { program: CodeProgram; onClose: () => void }) {
  const toast = useToast();
  const client = useQueryClient();
  const fileId = useId();
  const [file, setFile] = useState<File | null>(null);
  const [report, setReport] = useState<SalesImportResult | null>(null);
  const run = useMutation({
    mutationFn: (dryRun: boolean) => {
      const form = new FormData();
      form.append('file', file!);
      return api.upload<SalesImportResult>(
        `/admin/code-programs/${program.id}/sales/import?dryRun=${dryRun}`,
        form,
      );
    },
    onSuccess: async (r) => {
      setReport(r);
      if (!r.dryRun) {
        toast.success(
          'Sales report imported',
          `${r.matched} matched · ${r.mismatched} differ · ${r.created} new · ${r.refunded} refunded`,
        );
        await invalidateCodes(client);
      }
    },
  });
  const checked = report?.dryRun === true;
  const changes = report
    ? report.matched + report.mismatched + report.created + report.refunded + report.cancelled
    : 0;
  return (
    <Dialog
      open
      onClose={onClose}
      size="lg"
      title={`Import ${program.brandName}’s sales report`}
      description="A CSV with order id, code, amount and date (optional status, currency, discount). Reported sales are matched (±1% amount, ±1 day), differences flagged, unclaimed uses become pending sales for the code’s holder and refunded/cancelled orders are reversed. Check the file first."
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
            disabled={!checked || changes === 0}
          >
            Apply {checked ? changes : ''} changes
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
        <FormField label="Sales report (CSV)" id={fileId} required>
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
            tone={report.rejected > 0 || report.mismatched > 0 ? 'warning' : 'success'}
            title={report.dryRun ? 'What the import will do' : 'Import finished'}
          >
            {report.rows} rows · {report.matched} matched · {report.mismatched} differ · {report.created} new
            · {report.refunded} refunded · {report.cancelled} cancelled · {report.unchanged} unchanged ·{' '}
            {report.rejected} rejected or flagged
          </Alert>
        )}
        {report && report.issues.length > 0 && (
          <DataTable
            caption="Rows needing attention"
            showCaption
            maxHeight="18rem"
            rows={report.issues}
            getRowId={(i) => `${i.row}-${i.orderReference ?? ''}`}
            columns={[
              { id: 'row', header: 'Line', cell: (i) => i.row },
              {
                id: 'order',
                header: 'Order',
                primary: true,
                cell: (i) => <span className="dc-mono">{i.orderReference ?? '—'}</span>,
              },
              { id: 'outcome', header: 'Outcome', cell: (i) => OUTCOME_LABEL[i.outcome] ?? i.outcome },
              {
                id: 'message',
                header: 'Detail',
                cell: (i) => <span className="text-small">{i.message}</span>,
              },
            ]}
          />
        )}
      </div>
    </Dialog>
  );
}
