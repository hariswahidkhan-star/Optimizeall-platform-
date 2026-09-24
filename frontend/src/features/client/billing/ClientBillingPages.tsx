import { Download } from 'lucide-react';
import { useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import {
  Alert,
  Button,
  Card,
  CardBody,
  DataTable,
  EmptyState,
  ErrorState,
  FormField,
  Input,
  Money,
  PageHeader,
  ScrollArea,
  Select,
  Skeleton,
  Stat,
  Tabs,
  useToast,
  type DataTableColumn,
} from '@/components/ui';
import type { ContractSummary, InvoiceSummary } from '@/features/agency/billing/api/types';
import { InvoiceDocumentView } from '@/features/agency/billing/components/InvoiceDocumentView';
import { ContractStatusBadge, InvoiceStatusBadge, billingErrorMessage, formatDateOnly, todayIso, addDaysIso } from '@/features/agency/billing/lib';
import { AcceptProposalPanel } from '@/features/agency/crm/components/AcceptProposalPanel';
import { ProposalDocumentView } from '@/features/agency/crm/components/ProposalDocumentView';
import { ProposalStatusBadge } from '@/features/agency/crm/lib';
import { api } from '@/lib/api/client';
import { isApiError } from '@/lib/api/errors';
import { InvoicePaymentsPanel } from './InvoicePaymentsPanel';
import {
  type ClientProposalSummary,
  useClientBillingSummary,
  useClientContracts,
  useClientInvoice,
  useClientInvoices,
  useClientProposal,
  useClientProposals,
  useClientStatement,
  useRespondToProposal,
} from './api';
import '@/features/agency/billing/billing.css';

/** Friendly explanation when the member's duty doesn't include billing (API answers 403 client.insufficient_role). */
function BillingError({ error, onRetry }: { error: unknown; onRetry: () => void }) {
  if (isApiError(error) && error.status === 403)
    return (
      <Alert tone="info" title="Billing isn’t available to you">
        Invoices, proposals and contracts are visible to people with the Billing or Owner role in your organization. Ask your
        organization’s owner if you need access.
      </Alert>
    );
  return <ErrorState error={error} onRetry={onRetry} />;
}

function Amounts({ values }: { values: { currency: string; amount: number }[] }) {
  if (values.length === 0) return <>—</>;
  return (
    <span className="bill-amounts">
      {values.map((v) => (
        <Money key={v.currency} amount={v.amount} currency={v.currency} />
      ))}
    </span>
  );
}

const invoiceColumns: DataTableColumn<InvoiceSummary>[] = [
  { id: 'number', header: 'Invoice', primary: true, cell: (i) => <Link className="ui-link bill-strong" to={`/client/billing/invoices/${i.id}`}>{i.number}</Link> },
  { id: 'status', header: 'Status', cell: (i) => <InvoiceStatusBadge status={i.status} /> },
  { id: 'issued', header: 'Issued', cell: (i) => formatDateOnly(i.issueDate), hideOnMobile: true },
  { id: 'due', header: 'Due', cell: (i) => formatDateOnly(i.dueDate) },
  { id: 'total', header: 'Total', align: 'right', cell: (i) => <Money amount={i.total} currency={i.currency} /> },
  { id: 'balance', header: 'Balance', align: 'right', cell: (i) => <Money amount={i.balance} currency={i.currency} /> },
];

const proposalColumns: DataTableColumn<ClientProposalSummary>[] = [
  { id: 'title', header: 'Proposal', primary: true, cell: (p) => <Link className="ui-link bill-strong" to={`/client/billing/proposals/${p.id}`}>{p.number} · {p.title}</Link> },
  { id: 'status', header: 'Status', cell: (p) => <ProposalStatusBadge status={p.status} /> },
  { id: 'valid', header: 'Valid until', cell: (p) => formatDateOnly(p.validUntil) },
  { id: 'mrr', header: 'Monthly', align: 'right', cell: (p) => <Money amount={p.monthlyRecurringValue} currency={p.currency} /> },
  { id: 'total', header: 'First invoice', align: 'right', cell: (p) => <Money amount={p.total} currency={p.currency} /> },
];

const contractColumns: DataTableColumn<ContractSummary>[] = [
  { id: 'title', header: 'Contract', primary: true, cell: (c) => `${c.number} · ${c.title}` },
  { id: 'status', header: 'Status', cell: (c) => <ContractStatusBadge status={c.status} /> },
  { id: 'billing', header: 'Billing', cell: (c) => c.billingFrequency },
  { id: 'next', header: 'Next invoice', cell: (c) => formatDateOnly(c.nextInvoiceDate), hideOnMobile: true },
  { id: 'period', header: 'Per period', align: 'right', cell: (c) => <Money amount={c.amountPerPeriod} currency={c.currency} /> },
];

function InvoicesTab() {
  const query = useClientInvoices({ pageSize: 100 });
  if (query.isError) return <BillingError error={query.error} onRetry={() => void query.refetch()} />;
  return <DataTable caption="Invoices" columns={invoiceColumns} rows={query.data?.items ?? []} getRowId={(i) => i.id} loading={query.isPending} emptyState={<EmptyState compact headingLevel={3} title="No invoices yet" />} />;
}

function ProposalsTab() {
  const query = useClientProposals();
  if (query.isError) return <BillingError error={query.error} onRetry={() => void query.refetch()} />;
  return <DataTable caption="Proposals" columns={proposalColumns} rows={query.data ?? []} getRowId={(p) => p.id} loading={query.isPending} emptyState={<EmptyState compact headingLevel={3} title="No proposals" />} />;
}

function ContractsTab() {
  const query = useClientContracts();
  if (query.isError) return <BillingError error={query.error} onRetry={() => void query.refetch()} />;
  return <DataTable caption="Contracts" columns={contractColumns} rows={query.data ?? []} getRowId={(c) => c.id} loading={query.isPending} emptyState={<EmptyState compact headingLevel={3} title="No contracts" />} />;
}

function StatementTab({ organizations }: { organizations: { clientAccountId: string; name: string; currency: string }[] }) {
  const [clientAccountId, setClient] = useState(organizations[0]?.clientAccountId ?? '');
  const [from, setFrom] = useState(addDaysIso(todayIso(), -365));
  const [to, setTo] = useState(todayIso());
  const query = useClientStatement({ clientAccountId, from, to }, !!clientAccountId);
  return (
    <div className="stack">
      <div className="bill-grid">
        {organizations.length > 1 && (
          <FormField label="Organization">
            <Select value={clientAccountId} options={organizations.map((o) => ({ value: o.clientAccountId, label: o.name }))} onChange={(e) => setClient(e.target.value)} />
          </FormField>
        )}
        <FormField label="From">
          <Input type="date" value={from} max={to} onChange={(e) => setFrom(e.target.value)} />
        </FormField>
        <FormField label="To">
          <Input type="date" value={to} min={from} onChange={(e) => setTo(e.target.value)} />
        </FormField>
      </div>
      {query.isError ? (
        <BillingError error={query.error} onRetry={() => void query.refetch()} />
      ) : !query.data ? (
        <Skeleton height="8rem" />
      ) : (
        <div className="bill-document">
          <ScrollArea className="bill-table-scroll" label="Statement">
            <table>
              <caption>
                Statement of account · {query.data.clientName} · {query.data.currency}
              </caption>
              <thead>
                <tr>
                  <th scope="col">Date</th>
                  <th scope="col">Type</th>
                  <th scope="col">Reference</th>
                  <th scope="col" className="num">Charges</th>
                  <th scope="col" className="num">Payments & credits</th>
                  <th scope="col" className="num">Balance</th>
                </tr>
              </thead>
              <tbody>
                <tr>
                  <td colSpan={5}>Opening balance</td>
                  <td className="num"><Money amount={query.data.openingBalance} currency={query.data.currency} /></td>
                </tr>
                {query.data.lines.map((l, i) => (
                  <tr key={`${l.reference}-${i}`}>
                    <td>{formatDateOnly(l.date)}</td>
                    <td>{l.type}</td>
                    <td>{l.reference}</td>
                    <td className="num">{l.debit ? <Money amount={l.debit} currency={query.data.currency} /> : ''}</td>
                    <td className="num">{l.credit ? <Money amount={l.credit} currency={query.data.currency} /> : ''}</td>
                    <td className="num"><Money amount={l.balance} currency={query.data.currency} /></td>
                  </tr>
                ))}
                <tr>
                  <td colSpan={5} className="bill-strong">Closing balance</td>
                  <td className="num bill-strong"><Money amount={query.data.closingBalance} currency={query.data.currency} /></td>
                </tr>
              </tbody>
            </table>
          </ScrollArea>
        </div>
      )}
    </div>
  );
}

/** Client portal billing home: balances, invoices, proposals to review, contracts and the statement of account. */
export function ClientBillingPage() {
  const summary = useClientBillingSummary();
  if (summary.isError) {
    return (
      <>
        <PageHeader title="Billing" />
        <BillingError error={summary.error} onRetry={() => void summary.refetch()} />
      </>
    );
  }
  const s = summary.data;
  return (
    <>
      <PageHeader title="Billing" description="Your invoices, proposals and agreements with Optimize All." />
      <div className="stack">
        <div className="bill-grid">
          <Stat label="Outstanding" loading={summary.isPending} value={<Amounts values={s?.outstanding ?? []} />} hint={s ? `${s.openInvoices} open invoice(s)` : undefined} />
          <Stat label="Overdue" loading={summary.isPending} value={<Amounts values={s?.overdue ?? []} />} />
          <Stat label="Proposals to review" measurement="Count" loading={summary.isPending} value={s?.proposalsAwaitingResponse ?? '—'} />
        </div>
        <Card>
          <CardBody>
            <Tabs
              label="Billing"
              tabs={[
                { id: 'invoices', label: 'Invoices', content: <InvoicesTab /> },
                { id: 'proposals', label: 'Proposals', content: <ProposalsTab />, badge: s?.proposalsAwaitingResponse ? String(s.proposalsAwaitingResponse) : undefined },
                { id: 'contracts', label: 'Contracts', content: <ContractsTab /> },
                { id: 'statement', label: 'Statement', content: s ? <StatementTab organizations={s.organizations} /> : <Skeleton height="6rem" /> },
              ]}
            />
          </CardBody>
        </Card>
      </div>
    </>
  );
}

export function ClientInvoicePage() {
  const { invoiceId = '' } = useParams();
  const toast = useToast();
  const query = useClientInvoice(invoiceId);
  const [busy, setBusy] = useState<'download' | 'pay' | null>(null);
  if (query.isError) return <BillingError error={query.error} onRetry={() => void query.refetch()} />;
  if (!query.data) return <Skeleton height="30rem" />;
  const invoice = query.data;
  return (
    <>
      <PageHeader title={`Invoice ${invoice.number}`} breadcrumbs={[{ label: 'Billing', to: '/client/billing' }, { label: invoice.number ?? 'Invoice' }]} />
      <InvoiceDocumentView
        invoice={invoice}
        headingLevel={2}
        actions={
          <>
            <Button
              variant="secondary"
              leadingIcon={<Download />}
              loading={busy === 'download'}
              onClick={async () => {
                setBusy('download');
                try {
                  await api.download(`/client/billing/invoices/${invoiceId}/document`, `invoice-${invoice.number}.html`);
                } catch (error) {
                  toast.error('Download failed', billingErrorMessage(error));
                } finally {
                  setBusy(null);
                }
              }}
            >
              Download
            </Button>
            {invoice.payment.onlinePaymentAvailable && invoice.balance > 0 && (
              <Button
                loading={busy === 'pay'}
                onClick={async () => {
                  setBusy('pay');
                  try {
                    const result = await api.post<{ available: boolean; redirectUrl: string | null; message: string }>(`/client/billing/invoices/${invoiceId}/pay`, {});
                    if (result.available && result.redirectUrl) window.location.assign(result.redirectUrl);
                    else toast.info('Online payment unavailable', result.message);
                  } catch (error) {
                    toast.error('Online payment unavailable', billingErrorMessage(error));
                  } finally {
                    setBusy(null);
                  }
                }}
              >
                Pay online
              </Button>
            )}
          </>
        }
      />
      <div className="bill-no-print">
        <InvoicePaymentsPanel invoiceId={invoiceId} />
      </div>
    </>
  );
}

export function ClientProposalPage() {
  const { proposalId = '' } = useParams();
  const query = useClientProposal(proposalId);
  const respond = useRespondToProposal(proposalId);
  if (query.isError) return <BillingError error={query.error} onRetry={() => void query.refetch()} />;
  if (!query.data) return <Skeleton height="30rem" />;
  const p = query.data;
  return (
    <>
      <PageHeader title={p.title} breadcrumbs={[{ label: 'Billing', to: '/client/billing' }, { label: p.number }]} meta={<ProposalStatusBadge status={p.status} />} />
      <div className="stack">
        <ProposalDocumentView version={p.version} number={p.number} agencyName={p.agencyName} preparedFor={p.preparedFor} headingLevel={2} />
        <div className="bill-document bill-no-print">
          <AcceptProposalPanel proposal={p} onAccept={(body) => respond.accept.mutateAsync(body)} onDecline={(body) => respond.decline.mutateAsync(body)} />
        </div>
      </div>
    </>
  );
}
