import { Ban, Pencil, Plus, Send } from 'lucide-react';
import { useEffect, useState } from 'react';
import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom';
import {
  Alert,
  Button,
  ButtonLink,
  Card,
  CardBody,
  CardHeader,
  Checkbox,
  ConfirmDialog,
  CopyField,
  DataTable,
  DateTime,
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
import { useClientOptions } from '@/features/agency/billing/api/hooks';
import type { PriceLineInput } from '@/features/agency/billing/api/types';
import { FormDialog } from '@/features/agency/billing/components/FormDialog';
import { LineItemsEditor, LivePreviewTotals, emptyLine } from '@/features/agency/billing/components/LineItemsEditor';
import { addDaysIso, billingErrorMessage, formatDateOnly, todayIso } from '@/features/agency/billing/lib';
import { isApiError } from '@/lib/api/errors';
import { useSupportedCurrencies } from '@/lib/api/meta';
import { useDeal, useProposal, useProposals, useSaveProposal, useSendProposal, useWithdrawProposal } from '../api/hooks';
import type { Proposal, ProposalRequest, ProposalSummary } from '../api/types';
import { ProposalDocumentView } from '../components/ProposalDocumentView';
import { ProposalStatusBadge } from '../lib';
import '@/features/agency/billing/billing.css';
import '../crm.css';

const STATUS_OPTIONS = ['Draft', 'Sent', 'Viewed', 'Accepted', 'Declined', 'Expired', 'Withdrawn'].map((s) => ({ value: s, label: s }));

const columns: DataTableColumn<ProposalSummary>[] = [
  {
    id: 'number',
    header: 'Proposal',
    primary: true,
    cell: (p) => (
      <Link className="ui-link bill-strong" to={`/agency/proposals/${p.id}`}>
        {p.number} · {p.title}
      </Link>
    ),
  },
  { id: 'for', header: 'For', cell: (p) => p.clientName ?? p.companyName ?? p.dealTitle ?? '—' },
  { id: 'status', header: 'Status', cell: (p) => <ProposalStatusBadge status={p.status} /> },
  { id: 'version', header: 'Version', align: 'right', cell: (p) => `v${p.currentVersion}`, hideOnMobile: true },
  { id: 'views', header: 'Views', align: 'right', cell: (p) => p.viewCount, hideOnMobile: true },
  { id: 'valid', header: 'Valid until', cell: (p) => formatDateOnly(p.validUntil), hideOnMobile: true },
  { id: 'mrr', header: 'MRR', align: 'right', cell: (p) => <Money amount={p.monthlyRecurringValue} currency={p.currency} /> },
  { id: 'total', header: 'First invoice', align: 'right', cell: (p) => <Money amount={p.total} currency={p.currency} /> },
];

export function ProposalsPage() {
  const [search, setSearch] = useState('');
  const [status, setStatus] = useState<string | undefined>();
  const [page, setPage] = useState(1);
  const query = useProposals({ search, status, page, pageSize: 25 });
  return (
    <>
      <PageHeader
        title="Proposals"
        description="Build, version and send proposals. Clients accept online with a typed signature."
        actions={
          <ButtonLink to="/agency/proposals/new" leadingIcon={<Plus />}>
            New proposal
          </ButtonLink>
        }
      />
      <Card>
        <CardBody className="stack">
          <FilterBar
            search={search}
            onSearchChange={(v) => { setSearch(v); setPage(1); }}
            searchLabel="Search proposals"
            filters={[{ id: 'status', label: 'Status', options: STATUS_OPTIONS }]}
            values={{ status }}
            onFilterChange={(_, v) => { setStatus(v); setPage(1); }}
            onReset={() => { setSearch(''); setStatus(undefined); }}
          />
          {query.isError ? (
            <ErrorState error={query.error} onRetry={() => void query.refetch()} />
          ) : (
            <>
              <DataTable caption="Proposals" columns={columns} rows={query.data?.items ?? []} getRowId={(p) => p.id} loading={query.isPending} emptyState={<EmptyState compact headingLevel={3} title="No proposals yet" />} />
              {query.data && query.data.total > 25 && <Pagination page={page} pageSize={25} total={query.data.total} onPageChange={setPage} />}
            </>
          )}
        </CardBody>
      </Card>
    </>
  );
}

const SECTION_FIELDS: { key: 'executiveSummary' | 'goals' | 'scope' | 'deliverables' | 'timeline' | 'terms'; label: string }[] = [
  { key: 'executiveSummary', label: 'Executive summary' },
  { key: 'goals', label: 'Goals' },
  { key: 'scope', label: 'Scope' },
  { key: 'deliverables', label: 'Deliverables' },
  { key: 'timeline', label: 'Timeline' },
  { key: 'terms', label: 'Terms' },
];

type Sections = Record<(typeof SECTION_FIELDS)[number]['key'], string>;

/** New proposal or edit (a sent proposal is saved as a new version). Totals are live from the preview API. */
export function ProposalBuilderPage() {
  const { proposalId } = useParams();
  const [params] = useSearchParams();
  const dealId = params.get('dealId') ?? undefined;
  const navigate = useNavigate();
  const toast = useToast();
  const existing = useProposal(proposalId);
  const deal = useDeal(proposalId ? '' : dealId ?? '');
  const clients = useClientOptions();
  const save = useSaveProposal(proposalId);
  const [title, setTitle] = useState('');
  const [currency, setCurrency] = useState('USD');
  const [validUntil, setValidUntil] = useState(addDaysIso(todayIso(), 30));
  const [clientAccountId, setClientId] = useState('');
  const [recipientName, setRecipientName] = useState('');
  const [recipientEmail, setRecipientEmail] = useState('');
  const [invoiceOnAcceptance, setInvoiceOnAcceptance] = useState(true);
  const [sections, setSections] = useState<Sections>({ executiveSummary: '', goals: '', scope: '', deliverables: '', timeline: '', terms: '' });
  const [lines, setLines] = useState<PriceLineInput[]>([emptyLine('Monthly')]);
  const [loaded, setLoaded] = useState(!proposalId && !dealId);
  const [error, setError] = useState<unknown>(null);
  const currencies = useSupportedCurrencies(currency);

  useEffect(() => {
    if (loaded) return;
    const p = existing.data;
    if (p) {
      const v = p.version;
      setTitle(p.title);
      setCurrency(p.currency);
      setValidUntil(v.validUntil < todayIso() ? addDaysIso(todayIso(), 30) : v.validUntil);
      setClientId(p.clientAccountId ?? '');
      setRecipientName(p.recipientName ?? '');
      setRecipientEmail(p.recipientEmail ?? '');
      setInvoiceOnAcceptance(p.invoiceOnAcceptance ?? true);
      setSections({
        executiveSummary: v.executiveSummary ?? '',
        goals: v.goals ?? '',
        scope: v.scope ?? '',
        deliverables: v.deliverables ?? '',
        timeline: v.timeline ?? '',
        terms: v.terms ?? '',
      });
      setLines(
        v.lines.map((l) => ({
          description: l.description,
          serviceSlug: l.serviceSlug,
          packageSlug: l.packageSlug,
          quantity: l.quantity,
          unitPrice: l.unitPrice,
          discountType: l.discountType,
          discountValue: l.discountValue,
          taxRateId: l.taxRateId,
          recurrence: l.recurrence,
        })),
      );
      setLoaded(true);
    } else if (!proposalId && deal.data) {
      setTitle(`${deal.data.title} proposal`);
      setCurrency(deal.data.currency);
      const primary = deal.data.contacts.find((c) => c.primary);
      setRecipientName(primary?.displayName ?? '');
      setRecipientEmail(primary?.email ?? '');
      setClientId(deal.data.clientAccountId ?? '');
      setLoaded(true);
    } else if (!proposalId && deal.isError) {
      setLoaded(true);
    }
  }, [existing.data, deal.data, deal.isError, loaded, proposalId]);

  if (proposalId && existing.isError) return <ErrorState error={existing.error} onRetry={() => void existing.refetch()} />;
  if (!loaded) return <Skeleton height="30rem" />;
  const p = existing.data;
  const locked = p && (p.status === 'Accepted' || p.status === 'Withdrawn');
  const willVersion = p && p.version.sentAt !== null;
  const fieldErrors = isApiError(error) ? error.errors : undefined;

  const submit = async () => {
    setError(null);
    const body: ProposalRequest = {
      title: title.trim(),
      dealId: p?.dealId ?? dealId ?? null,
      clientAccountId: clientAccountId || null,
      currency,
      validUntil,
      ...sections,
      recipientName: recipientName.trim() || undefined,
      recipientEmail: recipientEmail.trim() || undefined,
      invoiceOnAcceptance,
      lines,
      concurrencyStamp: p?.concurrencyStamp,
    };
    try {
      const saved = await save.mutateAsync(body);
      toast.success(willVersion ? `Saved as version ${saved.currentVersion}` : 'Proposal saved');
      navigate(`/agency/proposals/${saved.id}`);
    } catch (err) {
      setError(err);
    }
  };

  return (
    <>
      <PageHeader
        title={p ? `Edit ${p.number}` : 'New proposal'}
        breadcrumbs={[{ label: 'Proposals', to: '/agency/proposals' }, { label: p ? p.number : 'New' }]}
      />
      {locked && (
        <Alert tone="info" title="Locked">
          This proposal is {p.status.toLowerCase()} and can’t be changed.
        </Alert>
      )}
      {willVersion && !locked && (
        <Alert tone="info" title="Saving creates a new version">
          Version {p.currentVersion} was sent to the client. Your changes become version {p.currentVersion + 1}; send it when ready.
        </Alert>
      )}
      <div className="bill-two-col">
        <div className="stack">
          <Card>
            <CardHeader title="Proposal" />
            <CardBody className="stack">
              <FormField label="Title" required error={fieldErrors?.title?.[0]}>
                <Input value={title} maxLength={200} disabled={!!locked} onChange={(e) => setTitle(e.target.value)} />
              </FormField>
              <div className="crm-grid">
                <FormField label="Currency">
                  <Select value={currency} options={currencies.options} disabled={!!locked} onChange={(e) => setCurrency(e.target.value)} />
                </FormField>
                <FormField label="Valid until" required error={fieldErrors?.validUntil?.[0]}>
                  <Input type="date" value={validUntil} min={todayIso()} disabled={!!locked} onChange={(e) => setValidUntil(e.target.value)} />
                </FormField>
                <FormField label="Existing client" optional hint="Leave empty for a prospect (the client is created on acceptance).">
                  <Select
                    value={clientAccountId}
                    disabled={!!locked}
                    options={[{ value: '', label: 'Prospect (no client yet)' }, ...(clients.data ?? []).map((c) => ({ value: c.id, label: c.name }))]}
                    onChange={(e) => setClientId(e.target.value)}
                  />
                </FormField>
              </div>
              <div className="crm-grid">
                <FormField label="Recipient name" optional>
                  <Input value={recipientName} maxLength={150} disabled={!!locked} onChange={(e) => setRecipientName(e.target.value)} />
                </FormField>
                <FormField label="Recipient email" optional error={fieldErrors?.recipientEmail?.[0]}>
                  <Input type="email" value={recipientEmail} maxLength={254} disabled={!!locked} onChange={(e) => setRecipientEmail(e.target.value)} />
                </FormField>
              </div>
              <Checkbox
                label="Create the first invoice on acceptance"
                description="One-time items plus the first period of each recurring item. Recurring items become a retainer contract."
                checked={invoiceOnAcceptance}
                disabled={!!locked}
                onChange={(e) => setInvoiceOnAcceptance(e.target.checked)}
              />
            </CardBody>
          </Card>
          <Card>
            <CardHeader title="Content" />
            <CardBody className="stack">
              {SECTION_FIELDS.map((s) => (
                <FormField key={s.key} label={s.label} optional>
                  <Textarea rows={s.key === 'terms' ? 4 : 3} maxLength={20000} value={sections[s.key]} disabled={!!locked} onChange={(e) => setSections({ ...sections, [s.key]: e.target.value })} />
                </FormField>
              ))}
            </CardBody>
          </Card>
          <Card>
            <CardHeader title="Pricing" description="Line items from service packages or custom lines." />
            <CardBody>
              <LineItemsEditor lines={lines} onChange={setLines} currency={currency} allowRecurrence disabled={!!locked} errors={fieldErrors} />
            </CardBody>
          </Card>
        </div>
        <Card>
          <CardHeader title="Totals" description="Live from the server." />
          <CardBody className="stack">
            <LivePreviewTotals lines={lines} currency={currency} previewPath="/agency/proposals/preview" showRecurring />
            {error !== null && (
              <Alert tone="danger" role="alert">
                {billingErrorMessage(error)}
              </Alert>
            )}
            {!locked && (
              <Button onClick={() => void submit()} loading={save.isPending} disabled={!title.trim()}>
                {willVersion ? `Save as version ${(p?.currentVersion ?? 0) + 1}` : p ? 'Save proposal' : 'Create proposal'}
              </Button>
            )}
          </CardBody>
        </Card>
      </div>
    </>
  );
}

function SendDialog({ proposal, open, onClose }: { proposal: Proposal; open: boolean; onClose: () => void }) {
  const send = useSendProposal(proposal.id);
  const toast = useToast();
  const [email, setEmail] = useState(true);
  const [message, setMessage] = useState('');
  const [link, setLink] = useState<string | null>(null);
  useEffect(() => {
    if (open) {
      setEmail(!!proposal.recipientEmail);
      setMessage('');
      setLink(null);
    }
  }, [open, proposal.recipientEmail]);
  return (
    <FormDialog
      open={open}
      onClose={onClose}
      title={`Send ${proposal.number} v${proposal.currentVersion}`}
      description="Creates an unguessable link to a public proposal page where the client can accept or decline."
      submitLabel={link ? 'Send again' : 'Send proposal'}
      onSubmit={async () => {
        const result = await send.mutateAsync({ concurrencyStamp: proposal.concurrencyStamp, email, message: message.trim() || undefined });
        setLink(result.shareUrl);
        toast.success(result.emailed ? `Emailed to ${proposal.recipientEmail}` : 'Proposal published');
        return false;
      }}
    >
      <Checkbox
        label={proposal.recipientEmail ? `Email it to ${proposal.recipientEmail}` : 'Email it (add a recipient email first)'}
        checked={email}
        disabled={!proposal.recipientEmail}
        onChange={(e) => setEmail(e.target.checked)}
      />
      {email && (
        <FormField label="Message" optional>
          <Textarea rows={4} maxLength={2000} value={message} onChange={(e) => setMessage(e.target.value)} />
        </FormField>
      )}
      {link && <CopyField label="Proposal link" value={link} />}
    </FormDialog>
  );
}

export function ProposalDetailPage() {
  const { proposalId = '' } = useParams();
  const [version, setVersion] = useState<number | undefined>();
  const query = useProposal(proposalId, version);
  const withdraw = useWithdrawProposal(proposalId);
  const toast = useToast();
  const [sending, setSending] = useState(false);
  const [withdrawing, setWithdrawing] = useState(false);
  if (query.isError) return <ErrorState error={query.error} onRetry={() => void query.refetch()} />;
  const p = query.data;
  if (!p) return <Skeleton height="30rem" />;
  const editable = p.status !== 'Accepted' && p.status !== 'Withdrawn';
  const sendable = ['Draft', 'Sent', 'Viewed'].includes(p.status);
  return (
    <>
      <PageHeader
        title={`${p.number} · ${p.title}`}
        breadcrumbs={[{ label: 'Proposals', to: '/agency/proposals' }, { label: p.number }]}
        meta={<ProposalStatusBadge status={p.status} />}
        actions={
          <div className="crm-actions">
            {editable && (
              <ButtonLink to={`/agency/proposals/${p.id}/edit`} variant="secondary" leadingIcon={<Pencil />}>
                Edit
              </ButtonLink>
            )}
            {sendable && (
              <Button leadingIcon={<Send />} onClick={() => setSending(true)}>
                {p.sentVersion === p.currentVersion ? 'Resend' : 'Send'}
              </Button>
            )}
            {editable && p.status !== 'Draft' && (
              <Button variant="ghost" leadingIcon={<Ban />} onClick={() => setWithdrawing(true)}>
                Withdraw
              </Button>
            )}
          </div>
        }
      />
      <div className="bill-two-col">
        <Card>
          <CardBody>
            <ProposalDocumentView version={p.version} number={p.number} agencyName="Optimize All" preparedFor={p.clientName ?? p.companyName ?? p.recipientName} headingLevel={2} />
          </CardBody>
        </Card>
        <div className="stack">
          <Card>
            <CardHeader title="Status" headingLevel={3} />
            <CardBody className="stack">
              <KeyValueList
                items={[
                  { label: 'Deal', value: p.dealId ? <Link className="ui-link" to={`/agency/crm/deals/${p.dealId}`}>{p.dealTitle}</Link> : '—' },
                  { label: 'Recipient', value: [p.recipientName, p.recipientEmail].filter(Boolean).join(' · ') || '—' },
                  { label: 'Sent', value: p.sentAt ? <DateTime value={p.sentAt} /> : 'Not yet' },
                  {
                    label: 'Views',
                    value: (
                      <>
                        {p.viewCount}
                        {p.lastViewedAt && (
                          <>
                            {' '}· last <DateTime value={p.lastViewedAt} format="relative" />
                          </>
                        )}
                      </>
                    ),
                  },
                  ...(p.acceptedAt
                    ? [{ label: 'Accepted', value: <>{p.signerName} ({p.signerTitle}) · v{p.acceptedVersion} · <DateTime value={p.acceptedAt} /></> }]
                    : []),
                  ...(p.declinedAt ? [{ label: 'Declined', value: p.declineReason ?? '—' }] : []),
                  ...(p.invoiceIds.length > 0
                    ? [{ label: 'Invoice', value: <Link className="ui-link" to={`/agency/billing/invoices/${p.invoiceIds[0]}`}>Open first invoice</Link> }]
                    : []),
                  ...(p.contractIds.length > 0
                    ? [{ label: 'Retainers', value: p.contractIds.map((id, i) => <Link key={id} className="ui-link" to={`/agency/contracts/${id}`}>{i > 0 ? ', ' : ''}Contract {i + 1}</Link>) }]
                    : []),
                ]}
              />
              {p.shareUrl && <CopyField label="Client link" value={p.shareUrl} />}
            </CardBody>
          </Card>
          <Card>
            <CardHeader title="Version history" headingLevel={3} />
            <CardBody>
              <ol className="crm-list" aria-label="Versions">
                {[...p.versions].reverse().map((v) => (
                  <li key={v.versionNumber} className="crm-row">
                    <Button size="sm" variant={v.versionNumber === p.version.versionNumber ? 'secondary' : 'ghost'} aria-pressed={v.versionNumber === p.version.versionNumber} onClick={() => setVersion(v.versionNumber === p.currentVersion ? undefined : v.versionNumber)}>
                      Version {v.versionNumber}
                    </Button>
                    <span className="crm-muted">
                      <Money amount={v.total} currency={v.currency} /> · {v.sentAt ? 'sent' : 'not sent'}
                      {v.locked ? ' · accepted' : ''}
                    </span>
                  </li>
                ))}
              </ol>
            </CardBody>
          </Card>
        </div>
      </div>
      <SendDialog proposal={p} open={sending} onClose={() => setSending(false)} />
      <ConfirmDialog
        open={withdrawing}
        onClose={() => setWithdrawing(false)}
        tone="danger"
        title={`Withdraw ${p.number}?`}
        description="The client link stops working and the proposal can no longer be accepted."
        confirmLabel="Withdraw"
        onConfirm={async () => {
          try {
            await withdraw.mutateAsync({ concurrencyStamp: p.concurrencyStamp });
            toast.success('Proposal withdrawn');
          } catch (error) {
            throw new Error(billingErrorMessage(error));
          }
        }}
      />
    </>
  );
}
