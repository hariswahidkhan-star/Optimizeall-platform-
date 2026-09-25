import { Ban, Copy, Download, FilePen, MinusCircle, Receipt, Send, Stamp, Trash2 } from 'lucide-react';
import { useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import {
  Alert,
  Button,
  ButtonLink,
  Card,
  CardBody,
  CardHeader,
  ConfirmDialog,
  CopyField,
  DataTable,
  DateTime,
  EmptyState,
  ErrorState,
  KeyValueList,
  Money,
  PageHeader,
  Skeleton,
  useToast,
  type DataTableColumn,
} from '@/components/ui';
import { api } from '@/lib/api/client';
import { Permissions } from '@/lib/auth/permissions';
import { useAuth } from '@/lib/auth/useAuth';
import { useDeleteInvoice, useDuplicateInvoice, useInvoice, useInvoiceAction } from '../api/hooks';
import type { Invoice, Payment, PriceLine } from '../api/types';
import { CreditNoteDialog, RecordPaymentDialog } from '../components/InvoiceDialogs';
import { TotalsList } from '../components/LineItemsEditor';
import { InvoiceStatusBadge, billingErrorMessage, formatDateOnly } from '../lib';
import '../billing.css';
import { absoluteUrl } from '@/features/public/site/head';

export function LinesTable({ lines, currency, caption }: { lines: PriceLine[]; currency: string; caption: string }) {
  const columns: DataTableColumn<PriceLine>[] = [
    { id: 'description', header: 'Description', primary: true, cell: (l) => l.description },
    { id: 'qty', header: 'Qty', align: 'right', cell: (l) => l.quantity },
    { id: 'price', header: 'Unit price', align: 'right', cell: (l) => <Money amount={l.unitPrice} currency={currency} /> },
    {
      id: 'discount',
      header: 'Discount',
      align: 'right',
      cell: (l) => (l.discountAmount > 0 ? <Money amount={l.discountAmount} currency={currency} /> : '—'),
      hideOnMobile: true,
    },
    {
      id: 'tax',
      header: 'Tax',
      cell: (l) => (l.taxPercent > 0 ? `${l.taxName ?? 'Tax'} ${l.taxPercent}%${l.taxInclusive ? ' incl.' : ''}` : '—'),
      hideOnMobile: true,
    },
    { id: 'total', header: 'Amount', align: 'right', cell: (l) => <Money amount={l.total} currency={currency} /> },
  ];
  return <DataTable caption={caption} columns={columns} rows={lines} getRowId={(l) => l.id} />;
}

const paymentColumns = (currency: string): DataTableColumn<Payment>[] => [
  { id: 'date', header: 'Paid on', primary: true, cell: (p) => formatDateOnly(p.paidOn) },
  { id: 'method', header: 'Method', cell: (p) => p.method },
  { id: 'reference', header: 'Reference', cell: (p) => p.reference },
  { id: 'by', header: 'Recorded by', cell: (p) => p.recordedBy ?? 'System', hideOnMobile: true },
  { id: 'amount', header: 'Amount', align: 'right', cell: (p) => <Money amount={p.amount} currency={currency} /> },
];

export function InvoiceDetailPage() {
  const { invoiceId = '' } = useParams();
  const navigate = useNavigate();
  const toast = useToast();
  const { hasPermission, user } = useAuth();
  const canManage = hasPermission(Permissions.BillingManage);
  const query = useInvoice(invoiceId);
  const action = useInvoiceAction(invoiceId);
  const remove = useDeleteInvoice();
  const duplicate = useDuplicateInvoice();
  const [deleting, setDeleting] = useState(false);
  const [paying, setPaying] = useState<Invoice | null>(null);
  const [crediting, setCrediting] = useState<Invoice | null>(null);
  const [sensitive, setSensitive] = useState<'void' | 'write-off' | null>(null);
  const [downloading, setDownloading] = useState(false);

  if (query.isError) return <ErrorState error={query.error} onRetry={() => void query.refetch()} />;
  const invoice = query.data;
  if (!invoice) return <Skeleton height="24rem" />;
  const open = ['Issued', 'PartiallyPaid', 'Overdue'].includes(invoice.status);
  const run = async (name: 'issue' | 'send', body?: unknown) => {
    try {
      await action.mutateAsync({ action: name, body });
      toast.success(name === 'issue' ? 'Invoice issued' : 'Invoice sent to the client');
    } catch (error) {
      toast.error(name === 'issue' ? 'Couldn’t issue the invoice' : 'Couldn’t send the invoice', billingErrorMessage(error));
    }
  };

  return (
    <>
      <PageHeader
        title={invoice.number ? `Invoice ${invoice.number}` : 'Draft invoice'}
        breadcrumbs={[{ label: 'Billing', to: '/agency/billing' }, { label: 'Invoices', to: '/agency/billing/invoices' }, { label: invoice.number ?? 'Draft' }]}
        meta={<InvoiceStatusBadge status={invoice.status} />}
        description={invoice.clientName}
        actions={
          <div className="bill-actions">
            {canManage && invoice.status === 'Draft' && (
              <>
                <ButtonLink to={`/agency/billing/invoices/${invoice.id}/edit`} variant="secondary" leadingIcon={<FilePen />}>
                  Edit
                </ButtonLink>
                <Button leadingIcon={<Stamp />} loading={action.isPending} onClick={() => void run('issue', { concurrencyStamp: invoice.concurrencyStamp, send: false })}>
                  Issue
                </Button>
                <Button variant="ghost" leadingIcon={<Trash2 />} onClick={() => setDeleting(true)}>
                  Delete draft
                </Button>
              </>
            )}
            {canManage && open && (
              <>
                <Button leadingIcon={<Receipt />} onClick={() => setPaying(invoice)}>
                  Record payment
                </Button>
                <Button variant="secondary" leadingIcon={<Send />} loading={action.isPending} onClick={() => void run('send')}>
                  Send to client
                </Button>
                <Button variant="secondary" leadingIcon={<MinusCircle />} onClick={() => setCrediting(invoice)}>
                  Credit note
                </Button>
              </>
            )}
            {canManage && (
              <Button
                variant="secondary"
                leadingIcon={<Copy />}
                loading={duplicate.isPending}
                onClick={async () => {
                  try {
                    const copy = await duplicate.mutateAsync(invoice.id);
                    toast.success('Draft copy created', 'Review it, then issue it when ready.');
                    navigate(`/agency/billing/invoices/${copy.id}/edit`);
                  } catch (error) {
                    toast.error('Couldn’t duplicate the invoice', billingErrorMessage(error));
                  }
                }}
              >
                Duplicate
              </Button>
            )}
            <Button
              variant="secondary"
              leadingIcon={<Download />}
              loading={downloading}
              onClick={async () => {
                setDownloading(true);
                try {
                  await api.download(`/agency/billing/invoices/${invoice.id}/document`, `invoice-${invoice.number ?? 'draft'}.html`);
                } catch (error) {
                  toast.error('Download failed', billingErrorMessage(error));
                } finally {
                  setDownloading(false);
                }
              }}
            >
              Download
            </Button>
          </div>
        }
      />
      <ConfirmDialog
        open={deleting}
        onClose={() => setDeleting(false)}
        tone="danger"
        title="Delete this draft invoice?"
        description="Drafts have no number yet, so nothing is lost from the numbering sequence. This can’t be undone."
        confirmLabel="Delete draft"
        onConfirm={async () => {
          try {
            await remove.mutateAsync(invoice.id);
          } catch (error) {
            throw new Error(billingErrorMessage(error));
          }
          navigate('/agency/billing/invoices');
        }}
      />
      <div className="bill-two-col">
        <div className="stack">
          {invoice.status !== 'Draft' && canManage && (
            <Alert tone="info" title="Issued invoices are locked">
              Numbers, lines and amounts can’t change once issued. Correct an amount with a credit note, void an unpaid invoice, or duplicate it
              to start a corrected draft.
            </Alert>
          )}
          {invoice.status === 'Void' && (
            <Alert tone="neutral" title="Voided">
              {invoice.voidReason}
            </Alert>
          )}
          {invoice.status === 'WrittenOff' && (
            <Alert tone="neutral" title="Written off">
              {invoice.writeOffReason}
            </Alert>
          )}
          <Card>
            <CardHeader title="Line items" />
            <CardBody>
              <LinesTable lines={invoice.lines} currency={invoice.currency} caption="Invoice lines" />
            </CardBody>
          </Card>
          <Card>
            <CardHeader title="Payments" />
            <CardBody>
              <DataTable
                caption="Payments"
                columns={paymentColumns(invoice.currency)}
                rows={invoice.payments}
                getRowId={(p) => p.id}
                emptyState={<EmptyState compact headingLevel={3} title="No payments yet" />}
              />
              {invoice.credits.length > 0 && (
                <ul className="bill-lines">
                  {invoice.credits.map((c) => (
                    <li key={`${c.creditNoteId}-${c.appliedAt}`}>
                      Credit note <span className="bill-strong">{c.creditNoteNumber}</span> applied{' '}
                      <Money amount={c.amount} currency={invoice.currency} /> on <DateTime value={c.appliedAt} format="date" />
                    </li>
                  ))}
                </ul>
              )}
            </CardBody>
          </Card>
        </div>
        <div className="stack">
          <Card>
            <CardHeader title="Summary" />
            <CardBody className="stack">
              <TotalsList preview={{ totals: invoice.totals }} currency={invoice.currency} />
              <KeyValueList
                layout="inline"
                items={[
                  { label: 'Paid', value: <Money amount={invoice.amountPaid} currency={invoice.currency} /> },
                  { label: 'Credited', value: <Money amount={invoice.amountCredited} currency={invoice.currency} /> },
                  ...(invoice.amountWrittenOff > 0
                    ? [{ label: 'Written off', value: <Money amount={invoice.amountWrittenOff} currency={invoice.currency} /> }]
                    : []),
                  { label: 'Balance due', value: <Money amount={invoice.balance} currency={invoice.currency} /> },
                  { label: 'Issued', value: formatDateOnly(invoice.issueDate) },
                  { label: 'Due', value: `${formatDateOnly(invoice.dueDate)}${invoice.daysOverdue > 0 ? ` · ${invoice.daysOverdue} days overdue` : ''}` },
                  { label: 'Terms', value: `${invoice.paymentTermsDays} days` },
                  ...(invoice.periodStart ? [{ label: 'Period', value: `${formatDateOnly(invoice.periodStart)} – ${formatDateOnly(invoice.periodEnd)}` }] : []),
                  ...(invoice.contractId
                    ? [{ label: 'Retainer', value: <Link className="ui-link" to={`/agency/contracts/${invoice.contractId}`}>View contract</Link> }]
                    : []),
                  { label: 'Reminders sent', value: invoice.reminders.length ? invoice.reminders.map((r) => r.kind).join(', ') : 'None' },
                ]}
              />
              {invoice.notes && <p className="bill-pre">{invoice.notes}</p>}
              {invoice.publicUrl && <CopyField label="Client view link" value={absoluteUrl(invoice.publicUrl)!} />}
            </CardBody>
          </Card>
          {canManage && open && (
            <Card>
              <CardHeader title="Finance controls" description="Sensitive: needs a reason and a second person (not the issuer)." headingLevel={3} />
              <CardBody className="bill-actions">
                <Button
                  variant="danger"
                  leadingIcon={<Ban />}
                  disabled={invoice.issuedByUserId === user?.id || invoice.amountPaid > 0 || invoice.amountCredited > 0}
                  onClick={() => setSensitive('void')}
                >
                  Void
                </Button>
                <Button variant="secondary" disabled={invoice.issuedByUserId === user?.id} onClick={() => setSensitive('write-off')}>
                  Write off balance
                </Button>
                {invoice.issuedByUserId === user?.id && <p className="bill-muted">You issued this invoice, so another finance user must void or write it off.</p>}
              </CardBody>
            </Card>
          )}
        </div>
      </div>

      <RecordPaymentDialog invoice={paying} onClose={() => setPaying(null)} onRefresh={() => void query.refetch()} />
      <CreditNoteDialog invoice={crediting} onClose={() => setCrediting(null)} />
      <ConfirmDialog
        open={sensitive !== null}
        onClose={() => setSensitive(null)}
        tone="danger"
        title={sensitive === 'void' ? `Void invoice ${invoice.number}?` : `Write off ${invoice.number}?`}
        description={
          sensitive === 'void'
            ? 'The invoice stays on record with status Void and its number is kept. This can’t be undone.'
            : 'The remaining balance is written off as uncollectable. This can’t be undone.'
        }
        requireReason
        reasonMinLength={5}
        confirmLabel={sensitive === 'void' ? 'Void invoice' : 'Write off'}
        onConfirm={async ({ reason }) => {
          try {
            await action.mutateAsync({
              action: sensitive!,
              body: { reason, confirm: true, concurrencyStamp: invoice.concurrencyStamp },
            });
            toast.success(sensitive === 'void' ? 'Invoice voided' : 'Balance written off');
          } catch (error) {
            throw new Error(billingErrorMessage(error));
          }
        }}
      />
    </>
  );
}
