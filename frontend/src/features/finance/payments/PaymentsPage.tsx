import { ArrowDownLeft, ArrowUpRight, FileText } from 'lucide-react';
import { useMemo, useState, type ReactNode } from 'react';
import { Link } from 'react-router-dom';
import {
  Alert,
  Badge,
  Button,
  Card,
  CardBody,
  CardHeader,
  DataTable,
  DateTime,
  EmptyState,
  FilterBar,
  KeyValueList,
  Money,
  PageHeader,
  Pagination,
  Skeleton,
  Stat,
  Timeline,
  type DataTableColumn,
  type MenuEntry,
  type Tone,
} from '@/components/ui';
import type { QueryParams } from '@/lib/api/client';
import { DownloadButton, Muted, QueryError } from '../components/common';
import { FinanceDrawer } from '../components/FinanceDrawer';
import { dateOnlyToDisplay } from '../lib/format';
import {
  INCOMING_METHODS,
  KIND_LABELS,
  PAYMENT_STATUSES,
  methodLabel,
  usePaymentDetail,
  usePayments,
  usePaymentsCapabilities,
  usePaymentsSummary,
  type CurrencyAmount,
  type PaymentHubStatus,
  type PaymentRecord,
  type PaymentsCapabilities,
} from './api';
import { PaymentDialogs, type DialogState } from './PaymentDialogs';

const STATUS_TONE: Record<PaymentHubStatus, Tone> = {
  Scheduled: 'info',
  Pending: 'warning',
  Paid: 'success',
  Failed: 'danger',
  Refunded: 'neutral',
  Voided: 'neutral',
};

export function PaymentStatusBadge({ status }: { status: PaymentHubStatus }) {
  return (
    <Badge tone={STATUS_TONE[status]} dot>
      {status}
    </Badge>
  );
}

/** One line per currency; amounts are never added across currencies. */
function PerCurrency({ values, empty = '—' }: { values: CurrencyAmount[] | undefined; empty?: string }) {
  if (!values || values.length === 0) return <>{empty}</>;
  return (
    <span className="fin-stack">
      {values.map((v) => (
        <span key={v.currency}>
          <Money amount={v.amount} currency={v.currency} />
        </span>
      ))}
    </span>
  );
}

function Kpis({ caps }: { caps: PaymentsCapabilities }) {
  const summary = usePaymentsSummary();
  if (summary.isError) return <QueryError error={summary.error} onRetry={() => summary.refetch()} />;
  const s = summary.data;
  const loading = summary.isPending;
  return (
    <div className="fin-stats" role="group" aria-label="Payment totals">
      {caps.incoming && (
        <>
          <Stat
            label="Received this month"
            loading={loading}
            value={<PerCurrency values={s?.incoming?.receivedThisMonth} empty="Nothing yet" />}
            hint={
              s?.incoming?.refundedThisMonth.length ? (
                <>
                  Refunded: <PerCurrency values={s.incoming.refundedThisMonth} />
                </>
              ) : (
                'Net of refunds and reversals'
              )
            }
          />
          <Stat
            label="Outstanding receivables"
            loading={loading}
            value={
              <PerCurrency
                values={s?.incoming?.outstanding.map((r) => ({ currency: r.currency, amount: r.total }))}
                empty="None"
              />
            }
            hint={
              s?.incoming
                ? `${s.incoming.openInvoices} open · ${s.incoming.overdueInvoices} overdue · ${s.incoming.pendingClaims} client report(s) to check`
                : undefined
            }
          />
        </>
      )}
      {caps.outgoing && (
        <>
          <Stat
            label="Payouts due"
            loading={loading}
            value={<PerCurrency values={s?.outgoing?.dueInBatches} empty="None" />}
            hint={
              s?.outgoing
                ? `${s.outgoing.awaitingPayment} awaiting payment · next cycle ${s.outgoing.nextCycle.periodKey} (pay ${dateOnlyToDisplay(s.outgoing.nextCycle.paymentDate)})`
                : undefined
            }
          />
          <Stat
            label="Paid out this month"
            loading={loading}
            value={<PerCurrency values={s?.outgoing?.paidOutThisMonth} empty="Nothing yet" />}
            hint={s?.outgoing ? `${s.outgoing.failedThisMonth} failed this month` : undefined}
          />
        </>
      )}
    </div>
  );
}

function AgingTable({ caps }: { caps: PaymentsCapabilities }) {
  const summary = usePaymentsSummary();
  const rows = summary.data?.incoming?.outstanding ?? [];
  if (!caps.incoming || rows.length === 0) return null;
  return (
    <Card>
      <CardHeader title="Receivables by age" />
      <CardBody>
        <DataTable
          caption="Outstanding receivables by age, per currency"
          rows={rows}
          getRowId={(r) => r.currency}
          columns={[
            { id: 'currency', header: 'Currency', primary: true, cell: (r) => r.currency },
            {
              id: 'current',
              header: 'Not yet due',
              align: 'right',
              cell: (r) => <Money amount={r.current} currency={r.currency} />,
            },
            {
              id: 'd30',
              header: '1–30 days',
              align: 'right',
              cell: (r) => <Money amount={r.days1To30} currency={r.currency} />,
            },
            {
              id: 'd60',
              header: '31–60',
              align: 'right',
              cell: (r) => <Money amount={r.days31To60} currency={r.currency} />,
            },
            {
              id: 'd90',
              header: '61–90',
              align: 'right',
              cell: (r) => <Money amount={r.days61To90} currency={r.currency} />,
            },
            {
              id: 'over',
              header: '90+',
              align: 'right',
              cell: (r) => <Money amount={r.over90} currency={r.currency} />,
            },
            {
              id: 'total',
              header: 'Total',
              align: 'right',
              cell: (r) => (
                <strong>
                  <Money amount={r.total} currency={r.currency} />
                </strong>
              ),
            },
          ]}
        />
      </CardBody>
    </Card>
  );
}

interface HubAction {
  id: string;
  label: string;
  state: DialogState;
  danger?: boolean;
}

/** Menu entries (table row menu) for the actions the server allows on this row. */
function actionEntries(record: PaymentRecord, open: (state: DialogState) => void): MenuEntry[] {
  return hubActions(record).map((a) => ({
    id: a.id,
    label: a.label,
    danger: a.danger,
    onSelect: () => open(a.state),
  }));
}

/** The actions the server says the caller may take on this row now (the server enforces every rule again). */
export function hubActions(record: PaymentRecord): HubAction[] {
  const map: Record<string, Omit<HubAction, 'id'>> = {
    record_payment: { label: 'Record payment', state: { type: 'record', record } },
    mark_paid_in_full: { label: 'Mark paid in full', state: { type: 'markPaid', record } },
    send_reminder: { label: 'Send reminder now', state: { type: 'reminder', record } },
    edit: { label: 'Edit details', state: { type: 'edit', record } },
    upload_proof: { label: 'Attach proof', state: { type: 'proof', record } },
    reverse: {
      label: 'Reverse (recorded in error)',
      state: { type: 'reverse', record, kind: 'Error' },
      danger: true,
    },
    refund: { label: 'Record refund', state: { type: 'reverse', record, kind: 'Refund' }, danger: true },
    confirm_claim: { label: 'Confirm payment', state: { type: 'confirmClaim', record } },
    reject_claim: { label: 'Reject report', state: { type: 'rejectClaim', record }, danger: true },
    mark_payout_paid: { label: 'Mark paid', state: { type: 'payoutPaid', record } },
    mark_payout_failed: {
      label: 'Mark failed / returned',
      state: { type: 'payoutFailed', record },
      danger: true,
    },
  };
  const actions: HubAction[] = record.actions.filter((a) => map[a]).map((a) => ({ id: a, ...map[a]! }));
  // One bulk transfer for the whole batch: offered from any of its items the caller may mark paid.
  if (record.kind === 'PayoutItem' && record.batchId && record.actions.includes('mark_payout_paid')) {
    const reference = record.batchReference ?? '';
    actions.push({
      id: 'mark_batch_paid',
      label: `Mark batch ${reference} paid`,
      state: { type: 'batchPaid', batchId: record.batchId, batchReference: reference },
    });
  }
  return actions;
}

function RecordDrawer({
  record,
  onClose,
  onAction,
}: {
  record: PaymentRecord | null;
  onClose: () => void;
  onAction: (state: DialogState) => void;
}) {
  const detail = usePaymentDetail(record);
  const current = detail.data?.record ?? record;
  const buttons = current ? hubActions(current) : [];
  let body: ReactNode;
  if (detail.isError) body = <QueryError error={detail.error} onRetry={() => detail.refetch()} />;
  else if (!detail.data || !current) body = <Skeleton height="16rem" />;
  else {
    const d = detail.data;
    body = (
      <div className="stack">
        <KeyValueList
          items={[
            {
              label: 'Status',
              value: (
                <>
                  <PaymentStatusBadge status={current.status} /> <Muted>{current.sourceStatus}</Muted>
                </>
              ),
            },
            { label: 'Type', value: KIND_LABELS[current.kind] },
            { label: current.party.type === 'client' ? 'Client' : 'Participant', value: current.party.name },
            { label: 'Amount', value: <Money amount={current.amount} currency={current.currency} /> },
            { label: 'Method', value: methodLabel(current.method) },
            { label: 'Reference', value: current.reference ?? '—' },
            {
              label:
                current.kind === 'InvoiceDue'
                  ? 'Due date'
                  : current.kind === 'PayoutItem'
                    ? 'Scheduled payment date'
                    : 'Date',
              value: dateOnlyToDisplay(current.date),
            },
            ...(current.paidAt ? [{ label: 'Paid at', value: <DateTime value={current.paidAt} /> }] : []),
            ...(current.invoiceId
              ? [
                  {
                    label: 'Invoice',
                    value: (
                      <Link className="ui-link" to={`/agency/billing/invoices/${current.invoiceId}`}>
                        {current.invoiceNumber ?? 'Open invoice'}
                      </Link>
                    ),
                  },
                ]
              : []),
            ...(current.invoiceBalance !== null && current.invoiceId
              ? [
                  {
                    label: 'Invoice balance',
                    value: <Money amount={current.invoiceBalance} currency={current.currency} />,
                  },
                ]
              : []),
            ...(current.batchId
              ? [
                  {
                    label: 'Payout batch',
                    value: (
                      <Link className="ui-link" to={`/finance/batches/${current.batchId}`}>
                        {current.batchReference}
                      </Link>
                    ),
                  },
                ]
              : []),
            ...(current.recordedBy
              ? [
                  {
                    label: current.kind === 'PaymentClaim' ? 'Reported by' : 'Recorded by',
                    value: current.recordedBy,
                  },
                ]
              : []),
            ...(current.notes ? [{ label: 'Notes', value: current.notes }] : []),
            ...(current.reversalReason
              ? [
                  {
                    label: current.kind === 'PayoutItem' ? 'Failure / hold reason' : 'Reason',
                    value: current.reversalReason,
                  },
                ]
              : []),
          ]}
        />
        {d.invoicePayments.length > 0 && (
          <section className="stack" aria-labelledby="pay-drawer-payments">
            <h3 id="pay-drawer-payments" className="fin-subheading">
              Payments on this invoice
            </h3>
            <ul className="fin-stack fin-plain-list">
              {d.invoicePayments.map((p) => (
                <li key={p.id}>
                  <Money amount={p.amount} currency={p.currency} signDisplay="exceptZero" /> ·{' '}
                  {methodLabel(p.method)} · {p.reference} · {dateOnlyToDisplay(p.paidOn)}
                  {p.reversalOfPaymentId && (
                    <Muted> ({p.reversalKind === 'Refund' ? 'refund' : 'reversal'})</Muted>
                  )}
                  {p.reversedAt && <Muted> (reversed)</Muted>}
                </li>
              ))}
            </ul>
          </section>
        )}
        {d.proofs.length > 0 && (
          <section className="stack" aria-labelledby="pay-drawer-proofs">
            <h3 id="pay-drawer-proofs" className="fin-subheading">
              Proof of payment
            </h3>
            <ul className="fin-stack fin-plain-list">
              {d.proofs.map((f) => (
                <li key={f.id}>
                  <DownloadButton
                    path={f.url.replace(/^\/api\/v1/, '')}
                    fileName={f.fileName}
                    variant="ghost"
                  >
                    <FileText aria-hidden="true" /> {f.fileName}
                  </DownloadButton>
                </li>
              ))}
            </ul>
          </section>
        )}
        {d.reminders.length > 0 && (
          <section className="stack" aria-labelledby="pay-drawer-reminders">
            <h3 id="pay-drawer-reminders" className="fin-subheading">
              Reminders sent
            </h3>
            <ul className="fin-stack fin-plain-list">
              {d.reminders.map((r) => (
                <li key={r.kind}>
                  <DateTime value={r.sentAt} /> ·{' '}
                  {r.manual ? `sent by ${r.sentBy ?? 'staff'}` : `scheduled (${r.kind})`}
                </li>
              ))}
            </ul>
          </section>
        )}
        {d.history.length > 0 && (
          <Timeline
            label="History"
            items={d.history.map((h, i) => ({
              id: `${h.at}-${i}`,
              title: h.action,
              timestamp: h.at,
              actor: h.actor ?? undefined,
              description: h.reason ?? undefined,
            }))}
          />
        )}
      </div>
    );
  }
  return (
    <FinanceDrawer
      open={!!record}
      onClose={onClose}
      title={record ? `${KIND_LABELS[record.kind]} — ${record.party.name}` : 'Payment'}
      description={record ? <Money amount={record.amount} currency={record.currency} /> : undefined}
      footer={
        buttons.length > 0 ? (
          <div className="cluster">
            {buttons.map((b) => (
              <Button
                key={b.id}
                size="sm"
                variant={b.danger ? 'danger' : 'secondary'}
                onClick={() => onAction(b.state)}
              >
                {b.label}
              </Button>
            ))}
          </div>
        ) : undefined
      }
    >
      {body}
    </FinanceDrawer>
  );
}

const DIRECTION_OPTIONS = [
  { value: 'Incoming', label: 'Incoming (clients)' },
  { value: 'Outgoing', label: 'Outgoing (payouts)' },
];

export function PaymentsPage() {
  const caps = usePaymentsCapabilities();
  const [search, setSearch] = useState('');
  const [filters, setFilters] = useState<Record<string, string | undefined>>({});
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(25);
  const [selected, setSelected] = useState<PaymentRecord | null>(null);
  const [dialog, setDialog] = useState<DialogState>(null);

  const params: QueryParams = useMemo(
    () => ({ search, page, pageSize, ...filters }),
    [search, page, pageSize, filters],
  );
  const list = usePayments(params);

  if (caps.isError) {
    return (
      <>
        <PageHeader title="Payments" />
        <QueryError error={caps.error} onRetry={() => caps.refetch()} />
      </>
    );
  }
  if (!caps.data) return <Skeleton height="30rem" />;
  const can = caps.data;

  const filterDefs = [
    ...(can.incoming && can.outgoing
      ? [{ id: 'direction', label: 'Direction', options: DIRECTION_OPTIONS, allLabel: 'All directions' }]
      : []),
    {
      id: 'status',
      label: 'Status',
      options: PAYMENT_STATUSES.map((s) => ({ value: s, label: s })),
      allLabel: 'All statuses',
    },
    {
      id: 'kind',
      label: 'Type',
      options: (Object.keys(KIND_LABELS) as (keyof typeof KIND_LABELS)[])
        .filter((k) => (k === 'PayoutItem' ? can.outgoing : can.incoming))
        .map((k) => ({ value: k, label: KIND_LABELS[k] })),
      allLabel: 'All types',
    },
    ...(can.incoming
      ? [{ id: 'method', label: 'Method', options: INCOMING_METHODS, allLabel: 'Any method' }]
      : []),
    ...(can.incoming
      ? [
          {
            id: 'overdueOnly',
            label: 'Overdue',
            options: [{ value: 'true', label: 'Overdue invoices only' }],
            allLabel: 'Any due date',
          },
        ]
      : []),
  ];

  const columns: DataTableColumn<PaymentRecord>[] = [
    {
      id: 'party',
      header: 'Party',
      primary: true,
      cell: (r) => (
        <button type="button" className="fin-linkbutton ui-link" onClick={() => setSelected(r)}>
          {r.direction === 'Incoming' ? (
            <ArrowDownLeft aria-label="Incoming" />
          ) : (
            <ArrowUpRight aria-label="Outgoing" />
          )}{' '}
          {r.party.name}
        </button>
      ),
    },
    { id: 'kind', header: 'Type', cell: (r) => KIND_LABELS[r.kind], hideOnMobile: true },
    { id: 'status', header: 'Status', cell: (r) => <PaymentStatusBadge status={r.status} /> },
    {
      id: 'amount',
      header: 'Amount',
      align: 'right',
      cell: (r) => <Money amount={r.amount} currency={r.currency} />,
    },
    { id: 'method', header: 'Method', cell: (r) => methodLabel(r.method), hideOnMobile: true },
    {
      id: 'reference',
      header: 'Reference',
      cell: (r) => r.reference ?? <Muted>—</Muted>,
      hideOnMobile: true,
    },
    {
      id: 'date',
      header: 'Date',
      nowrap: true,
      cell: (r) => (
        <>
          {dateOnlyToDisplay(r.date)}
          {r.daysOverdue > 0 && (
            <>
              {' '}
              <Badge tone="danger" size="sm">
                {r.daysOverdue} d overdue
              </Badge>
            </>
          )}
        </>
      ),
    },
    {
      id: 'link',
      header: 'Invoice / batch',
      hideOnMobile: true,
      cell: (r) => r.invoiceNumber ?? r.batchReference ?? <Muted>—</Muted>,
    },
  ];

  const exportQuery: QueryParams = { search, ...filters };

  return (
    <>
      <PageHeader
        title="Payments"
        description="Every payment in one place: money received from clients and payouts to participants. Record, correct, reverse or mark payments as paid by hand."
        actions={
          <DownloadButton path="/admin/payments/export.csv" fileName="payments.csv" query={exportQuery}>
            Export CSV
          </DownloadButton>
        }
      />
      <div className="stack">
        <Kpis caps={can} />
        <AgingTable caps={can} />
        <FilterBar
          search={search}
          onSearchChange={(v) => {
            setSearch(v);
            setPage(1);
          }}
          searchLabel="Search payments"
          searchPlaceholder="Client, participant, reference, invoice or batch"
          filters={filterDefs}
          values={filters}
          onFilterChange={(id, value) => {
            setFilters((f) => ({ ...f, [id]: value }));
            setPage(1);
          }}
          onReset={() => {
            setFilters({});
            setSearch('');
            setPage(1);
          }}
        />
        {!can.manageIncoming && can.incoming && (
          <Alert tone="info">
            You can see client payments but not change them (needs the “manage billing” permission).
          </Alert>
        )}
        {list.isError ? (
          <QueryError error={list.error} onRetry={() => list.refetch()} />
        ) : (
          <>
            <DataTable
              caption="Payments"
              columns={columns}
              rows={list.data?.items ?? []}
              getRowId={(r) => r.key}
              loading={list.isPending}
              rowLabel={(r) => `${KIND_LABELS[r.kind]} ${r.party.name}`}
              rowActions={(r) => [
                { id: 'details', label: 'View details', onSelect: () => setSelected(r) },
                ...actionEntries(r, setDialog),
              ]}
              emptyState={<EmptyState compact headingLevel={3} title="No payments match these filters" />}
            />
            {list.data && list.data.total > 0 && (
              <Pagination
                page={page}
                pageSize={pageSize}
                total={list.data.total}
                onPageChange={setPage}
                onPageSizeChange={(s) => {
                  setPageSize(s);
                  setPage(1);
                }}
              />
            )}
          </>
        )}
      </div>
      <RecordDrawer record={selected} onClose={() => setSelected(null)} onAction={setDialog} />
      <PaymentDialogs state={dialog} onClose={() => setDialog(null)} />
    </>
  );
}
