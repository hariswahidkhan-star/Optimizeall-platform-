import { Pause, Play, Plus, Repeat, Trash2, XCircle } from 'lucide-react';
import { useEffect, useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import {
  Alert,
  Button,
  ButtonLink,
  Card,
  CardBody,
  CardHeader,
  Checkbox,
  ConfirmDialog,
  DataTable,
  EmptyState,
  ErrorState,
  FilterBar,
  FormField,
  Input,
  KeyValueList,
  Money,
  PageHeader,
  Pagination,
  Select,
  Skeleton,
  Textarea,
  useToast,
  type DataTableColumn,
} from '@/components/ui';
import { useClientOptions, useContract, useContractAction, useContracts, useDeleteContract, useSaveContract } from '@/features/agency/billing/api/hooks';
import { PaymentTermsField } from '@/features/agency/billing/components/PaymentTermsField';
import type { BillingFrequency, ContractSummary, InvoiceSummary, PriceLineInput } from '@/features/agency/billing/api/types';
import { LineItemsEditor, LivePreviewTotals, emptyLine } from '@/features/agency/billing/components/LineItemsEditor';
import { LinesTable } from '@/features/agency/billing/pages/InvoiceDetailPage';
import { ContractStatusBadge, InvoiceStatusBadge, billingErrorMessage, formatDateOnly, todayIso } from '@/features/agency/billing/lib';
import { isApiError } from '@/lib/api/errors';
import { useSupportedCurrencies } from '@/lib/api/meta';
import '@/features/agency/billing/billing.css';
import '../crm.css';

const FREQUENCIES = [
  { value: 'Monthly', label: 'Monthly' },
  { value: 'Quarterly', label: 'Quarterly' },
  { value: 'Annually', label: 'Annually' },
];

const columns: DataTableColumn<ContractSummary>[] = [
  {
    id: 'title',
    header: 'Contract',
    primary: true,
    cell: (c) => (
      <Link className="ui-link bill-strong" to={`/agency/contracts/${c.id}`}>
        {c.number} · {c.title}
      </Link>
    ),
  },
  { id: 'client', header: 'Client', cell: (c) => c.clientName },
  { id: 'status', header: 'Status', cell: (c) => <ContractStatusBadge status={c.status} /> },
  { id: 'frequency', header: 'Billing', cell: (c) => c.billingFrequency, hideOnMobile: true },
  { id: 'next', header: 'Next invoice', cell: (c) => formatDateOnly(c.nextInvoiceDate), hideOnMobile: true },
  { id: 'period', header: 'Per period', align: 'right', cell: (c) => <Money amount={c.amountPerPeriod} currency={c.currency} /> },
  { id: 'mrr', header: 'MRR', align: 'right', cell: (c) => <Money amount={c.monthlyValue} currency={c.currency} /> },
];

export function ContractsPage() {
  const [search, setSearch] = useState('');
  const [status, setStatus] = useState<string | undefined>();
  const [page, setPage] = useState(1);
  const query = useContracts({ search, status, page, pageSize: 25 });
  return (
    <>
      <PageHeader
        title="Contracts & retainers"
        description="Active retainers are invoiced automatically every billing period."
        actions={
          <ButtonLink to="/agency/contracts/new" leadingIcon={<Plus />}>
            New contract
          </ButtonLink>
        }
      />
      <Card>
        <CardBody className="stack">
          <FilterBar
            search={search}
            onSearchChange={(v) => { setSearch(v); setPage(1); }}
            searchLabel="Search contracts"
            filters={[{ id: 'status', label: 'Status', options: ['Draft', 'Active', 'Paused', 'Cancelled', 'Ended'].map((s) => ({ value: s, label: s })) }]}
            values={{ status }}
            onFilterChange={(_, v) => { setStatus(v); setPage(1); }}
            onReset={() => { setSearch(''); setStatus(undefined); }}
          />
          {query.isError ? (
            <ErrorState error={query.error} onRetry={() => void query.refetch()} />
          ) : (
            <>
              <DataTable caption="Contracts" columns={columns} rows={query.data?.items ?? []} getRowId={(c) => c.id} loading={query.isPending} emptyState={<EmptyState compact headingLevel={3} title="No contracts" description="Accepting a proposal with recurring items creates one automatically." />} />
              {query.data && query.data.total > 25 && <Pagination page={page} pageSize={25} total={query.data.total} onPageChange={setPage} />}
            </>
          )}
        </CardBody>
      </Card>
    </>
  );
}

export function ContractEditorPage() {
  const { contractId } = useParams();
  const navigate = useNavigate();
  const toast = useToast();
  const existing = useContract(contractId);
  const clients = useClientOptions();
  const save = useSaveContract(contractId);
  const [clientAccountId, setClientId] = useState('');
  const [title, setTitle] = useState('');
  const [currency, setCurrency] = useState('USD');
  const [startDate, setStart] = useState(todayIso());
  const [endDate, setEnd] = useState('');
  const [frequency, setFrequency] = useState<BillingFrequency>('Monthly');
  const [autoRenew, setAutoRenew] = useState(true);
  const [notice, setNotice] = useState('30');
  const [renewalMonths, setRenewalMonths] = useState('12');
  const [terms, setTerms] = useState('');
  const [autoIssue, setAutoIssue] = useState('');
  const [notes, setNotes] = useState('');
  const [lines, setLines] = useState<PriceLineInput[]>([emptyLine()]);
  const [loaded, setLoaded] = useState(!contractId);
  const [error, setError] = useState<unknown>(null);
  const currencies = useSupportedCurrencies(currency);

  useEffect(() => {
    const c = existing.data;
    if (!c || loaded) return;
    setClientId(c.clientAccountId);
    setTitle(c.title);
    setCurrency(c.currency);
    setStart(c.startDate);
    setEnd(c.endDate ?? '');
    setFrequency(c.billingFrequency);
    setAutoRenew(c.autoRenew);
    setNotice(String(c.noticePeriodDays));
    setRenewalMonths(String(c.renewalTermMonths));
    setTerms(String(c.paymentTermsDays));
    setAutoIssue(c.autoIssueInvoices === null ? '' : c.autoIssueInvoices ? 'yes' : 'no');
    setNotes(c.notes ?? '');
    setLines(c.lines.map((l) => ({ description: l.description, serviceSlug: l.serviceSlug, quantity: l.quantity, unitPrice: l.unitPrice, discountType: l.discountType, discountValue: l.discountValue, taxRateId: l.taxRateId })));
    setLoaded(true);
  }, [existing.data, loaded]);

  if (contractId && existing.isError) return <ErrorState error={existing.error} onRetry={() => void existing.refetch()} />;
  if (!loaded) return <Skeleton height="24rem" />;
  const fieldErrors = isApiError(error) ? error.errors : undefined;
  return (
    <>
      <PageHeader title={contractId ? 'Edit contract' : 'New contract'} breadcrumbs={[{ label: 'Contracts', to: '/agency/contracts' }, { label: contractId ? 'Edit' : 'New' }]} />
      <div className="bill-two-col">
        <div className="stack">
          <Card>
            <CardHeader title="Terms" />
            <CardBody className="crm-grid">
              <FormField label="Client" required>
                <Select
                  value={clientAccountId}
                  placeholder="Choose a client"
                  disabled={!!contractId}
                  options={(clients.data ?? []).map((c) => ({ value: c.id, label: c.name }))}
                  onChange={(e) => {
                    setClientId(e.target.value);
                    const client = clients.data?.find((c) => c.id === e.target.value);
                    if (client) setCurrency(client.currency);
                  }}
                />
              </FormField>
              <FormField label="Title" required>
                <Input value={title} maxLength={200} onChange={(e) => setTitle(e.target.value)} />
              </FormField>
              <FormField label="Currency">
                <Select value={currency} options={currencies.options} onChange={(e) => setCurrency(e.target.value)} />
              </FormField>
              <FormField label="Billing frequency">
                <Select value={frequency} options={FREQUENCIES} onChange={(e) => setFrequency(e.target.value as BillingFrequency)} />
              </FormField>
              <FormField label="Start date" required error={fieldErrors?.startDate?.[0]}>
                <Input type="date" value={startDate} onChange={(e) => setStart(e.target.value)} />
              </FormField>
              <FormField label="End date" optional error={fieldErrors?.endDate?.[0]}>
                <Input type="date" value={endDate} onChange={(e) => setEnd(e.target.value)} />
              </FormField>
              <FormField label="Notice period (days)">
                <Input type="number" min={0} max={365} value={notice} onChange={(e) => setNotice(e.target.value)} />
              </FormField>
              <PaymentTermsField value={terms} onChange={setTerms} />
              <FormField label="Generated invoices">
                <Select
                  value={autoIssue}
                  options={[
                    { value: '', label: 'Follow billing settings' },
                    { value: 'yes', label: 'Issue automatically' },
                    { value: 'no', label: 'Keep as drafts for review' },
                  ]}
                  onChange={(e) => setAutoIssue(e.target.value)}
                />
              </FormField>
              <Checkbox label="Auto-renew at the end date" checked={autoRenew} onChange={(e) => setAutoRenew(e.target.checked)} />
              {autoRenew && (
                <FormField label="Renewal term (months)">
                  <Input type="number" min={1} max={60} value={renewalMonths} onChange={(e) => setRenewalMonths(e.target.value)} />
                </FormField>
              )}
            </CardBody>
          </Card>
          <Card>
            <CardHeader title="Lines (per billing period)" />
            <CardBody className="stack">
              <LineItemsEditor lines={lines} onChange={setLines} currency={currency} errors={fieldErrors} />
              <FormField label="Notes" optional>
                <Textarea rows={3} maxLength={4000} value={notes} onChange={(e) => setNotes(e.target.value)} />
              </FormField>
            </CardBody>
          </Card>
        </div>
        <Card>
          <CardHeader title="Per period" description="Calculated by the server." />
          <CardBody className="stack">
            {/* contracts.manage roles also hold proposals.manage, whose preview prices lines with the same rules */}
            <LivePreviewTotals lines={lines} currency={currency} previewPath="/agency/proposals/preview" />
            {error !== null && (
              <Alert tone="danger" role="alert">
                {billingErrorMessage(error)}
              </Alert>
            )}
            <Button
              loading={save.isPending}
              disabled={!clientAccountId || !title.trim()}
              onClick={async () => {
                setError(null);
                try {
                  const saved = await save.mutateAsync({
                    clientAccountId,
                    title: title.trim(),
                    currency,
                    startDate,
                    endDate: endDate || null,
                    billingFrequency: frequency,
                    autoRenew,
                    renewalTermMonths: Number(renewalMonths) || 12,
                    noticePeriodDays: Number(notice) || 0,
                    paymentTermsDays: terms === '' ? undefined : Number(terms),
                    autoIssueInvoices: autoIssue === '' ? null : autoIssue === 'yes',
                    notes: notes.trim() || undefined,
                    lines,
                    concurrencyStamp: existing.data?.concurrencyStamp,
                  });
                  toast.success('Contract saved');
                  navigate(`/agency/contracts/${saved.id}`);
                } catch (err) {
                  setError(err);
                }
              }}
            >
              Save contract
            </Button>
          </CardBody>
        </Card>
      </div>
    </>
  );
}

const invoiceColumns: DataTableColumn<InvoiceSummary>[] = [
  { id: 'number', header: 'Invoice', primary: true, cell: (i) => <Link className="ui-link" to={`/agency/billing/invoices/${i.id}`}>{i.number ?? 'Draft'}</Link> },
  { id: 'status', header: 'Status', cell: (i) => <InvoiceStatusBadge status={i.status} /> },
  { id: 'due', header: 'Due', cell: (i) => formatDateOnly(i.dueDate) },
  { id: 'total', header: 'Total', align: 'right', cell: (i) => <Money amount={i.total} currency={i.currency} /> },
];

export function ContractDetailPage() {
  const { contractId = '' } = useParams();
  const query = useContract(contractId);
  const action = useContractAction(contractId);
  const remove = useDeleteContract();
  const navigate = useNavigate();
  const toast = useToast();
  const [cancelling, setCancelling] = useState(false);
  const [deleting, setDeleting] = useState(false);
  if (query.isError) return <ErrorState error={query.error} onRetry={() => void query.refetch()} />;
  const c = query.data;
  if (!c) return <Skeleton height="24rem" />;
  const run = async (name: 'activate' | 'pause' | 'resume' | 'generate-invoices', label: string) => {
    try {
      await action.mutateAsync({ action: name, body: name === 'generate-invoices' ? undefined : { concurrencyStamp: c.concurrencyStamp } });
      toast.success(label);
    } catch (error) {
      toast.error('Couldn’t update the contract', billingErrorMessage(error));
    }
  };
  const closed = c.status === 'Cancelled' || c.status === 'Ended';
  return (
    <>
      <PageHeader
        title={`${c.number} · ${c.title}`}
        breadcrumbs={[{ label: 'Contracts', to: '/agency/contracts' }, { label: c.number }]}
        meta={<ContractStatusBadge status={c.status} />}
        description={c.clientName}
        actions={
          <div className="crm-actions">
            {!closed && <ButtonLink to={`/agency/contracts/${c.id}/edit`} variant="secondary">Edit</ButtonLink>}
            {c.status === 'Draft' && <Button leadingIcon={<Play />} onClick={() => void run('activate', 'Contract activated')}>Activate</Button>}
            {c.status === 'Active' && (
              <>
                <Button variant="secondary" leadingIcon={<Repeat />} onClick={() => void run('generate-invoices', 'Due invoices generated')}>Generate due invoices</Button>
                <Button variant="secondary" leadingIcon={<Pause />} onClick={() => void run('pause', 'Contract paused')}>Pause</Button>
              </>
            )}
            {c.status === 'Paused' && <Button leadingIcon={<Play />} onClick={() => void run('resume', 'Contract resumed')}>Resume</Button>}
            {!closed && <Button variant="ghost" leadingIcon={<XCircle />} onClick={() => setCancelling(true)}>Cancel contract</Button>}
            {c.status === 'Draft' && !c.proposalId && c.invoices.length === 0 && (
              <Button variant="ghost" leadingIcon={<Trash2 />} onClick={() => setDeleting(true)}>
                Delete draft
              </Button>
            )}
          </div>
        }
      />
      {c.status === 'Draft' && c.proposalId && (
        <Alert tone="info" title="Created from an accepted proposal">
          This draft can be edited and activated, or cancelled with a reason. It can’t be deleted, so the acceptance record stays complete.
        </Alert>
      )}
      <div className="bill-two-col">
        <div className="stack">
          <Card>
            <CardHeader title="Lines per period" />
            <CardBody>
              <LinesTable lines={c.lines} currency={c.currency} caption="Contract lines" />
            </CardBody>
          </Card>
          <Card>
            <CardHeader title="Invoices" />
            <CardBody>
              <DataTable caption="Contract invoices" columns={invoiceColumns} rows={c.invoices} getRowId={(i) => i.id} emptyState={<EmptyState compact headingLevel={3} title="No invoices yet" />} />
            </CardBody>
          </Card>
        </div>
        <Card>
          <CardHeader title="Terms" headingLevel={3} />
          <CardBody>
            <KeyValueList
              items={[
                { label: 'Per period', value: <Money amount={c.totalsPerPeriod.total} currency={c.currency} /> },
                { label: 'Monthly value', value: <Money amount={c.monthlyValue} currency={c.currency} /> },
                { label: 'Billing', value: c.billingFrequency },
                { label: 'Start', value: formatDateOnly(c.startDate) },
                { label: 'End', value: c.endDate ? `${formatDateOnly(c.endDate)}${c.autoRenew ? ' (auto-renews)' : ''}` : 'Open-ended' },
                { label: 'Next invoice', value: formatDateOnly(c.nextInvoiceDate) },
                { label: 'Notice period', value: `${c.noticePeriodDays} days` },
                { label: 'Payment terms', value: `${c.paymentTermsDays} days` },
                ...(c.proposalId ? [{ label: 'From proposal', value: <Link className="ui-link" to={`/agency/proposals/${c.proposalId}`}>{c.proposalNumber} v{c.proposalVersion}</Link> }] : []),
                ...(c.cancelReason ? [{ label: 'Cancelled', value: c.cancelReason }] : []),
              ]}
            />
          </CardBody>
        </Card>
      </div>
      <ConfirmDialog
        open={deleting}
        onClose={() => setDeleting(false)}
        tone="danger"
        title={`Delete draft ${c.number}?`}
        description="The draft never billed anything, so it’s removed completely. This can’t be undone."
        confirmLabel="Delete draft"
        onConfirm={async () => {
          try {
            await remove.mutateAsync({ id: c.id, concurrencyStamp: c.concurrencyStamp });
          } catch (error) {
            throw new Error(billingErrorMessage(error));
          }
          toast.success('Draft contract deleted');
          navigate('/agency/contracts');
        }}
      />
      <ConfirmDialog
        open={cancelling}
        onClose={() => setCancelling(false)}
        tone="danger"
        title={`Cancel ${c.number}?`}
        description="No further invoices will be generated. Issued invoices are not affected."
        requireReason
        reasonMinLength={5}
        confirmLabel="Cancel contract"
        onConfirm={async ({ reason }) => {
          try {
            await action.mutateAsync({ action: 'cancel', body: { reason, confirm: true, concurrencyStamp: c.concurrencyStamp } });
            toast.success('Contract cancelled');
          } catch (error) {
            throw new Error(billingErrorMessage(error));
          }
        }}
      />
    </>
  );
}
