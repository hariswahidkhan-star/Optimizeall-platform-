import { Eye, Plus, Undo2, Wallet } from 'lucide-react';
import { useState } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import {
  Button,
  ButtonLink,
  Card,
  CardBody,
  DataTable,
  DateTime,
  EmptyState,
  FilterBar,
  FormField,
  Input,
  KeyValueList,
  Money,
  PageHeader,
  Pagination,
  StatusBadge,
  statusOptions,
  type DataTableColumn,
  type MenuEntry,
} from '@/components/ui';
import { humanize } from '@/lib/format/text';
import { useLedger } from '../api/hooks';
import { EARNING_TYPES, type LedgerRow } from '../api/types';
import { DownloadButton, Muted, PersonCell, QueryError } from '../components/common';
import { FinanceDrawer } from '../components/FinanceDrawer';
import { GUID_RE, nextDay } from '../lib/format';
import { useCan } from '../lib/useCan';
import { AdjustmentDialog } from './AdjustmentDialog';
import { ReverseDialog } from './ReverseDialog';

/** Reversible per the ledger rules; Scheduled rows are offered and the server explains `ledger.in_payout_batch`. */
export function canReverse(row: LedgerRow): boolean {
  return (
    row.type !== 'Reversal' &&
    row.status !== 'Reversed' &&
    row.status !== 'Declined' &&
    row.settlementAmount > 0
  );
}

function RateCell({ row }: { row: LedgerRow }) {
  if (row.originalCurrency === row.settlementCurrency) return <Muted>—</Muted>;
  return (
    <span
      className="tabular"
      title={`1 ${row.originalCurrency} = ${row.exchangeRate} ${row.settlementCurrency}`}
    >
      {row.exchangeRate}
    </span>
  );
}

function LedgerDetail({ row }: { row: LedgerRow }) {
  const id = (v: string | null) => (v ? <code className="fin-mono">{v}</code> : '—');
  return (
    <KeyValueList
      items={[
        { label: 'Participant', value: <PersonCell user={row.user} link /> },
        { label: 'Type', value: humanize(row.type) },
        { label: 'Status', value: <StatusBadge kind="earning" status={row.status} /> },
        { label: 'Description', value: row.description },
        { label: 'Campaign', value: row.campaign?.title ?? '—' },
        {
          label: 'Original amount',
          value: <Money amount={row.originalAmount} currency={row.originalCurrency} />,
        },
        {
          label: 'Exchange rate',
          value:
            row.originalCurrency === row.settlementCurrency
              ? 'Same currency'
              : `1 ${row.originalCurrency} = ${row.exchangeRate} ${row.settlementCurrency} (stored at creation)`,
        },
        {
          label: 'Settlement amount',
          value: <Money amount={row.settlementAmount} currency={row.settlementCurrency} colored />,
        },
        { label: 'Rule version', value: row.ruleSetVersion ?? '—' },
        { label: 'Created', value: <DateTime value={row.createdAt} /> },
        { label: 'Approved', value: <DateTime value={row.approvedAt} /> },
        { label: 'Available for payout', value: <DateTime value={row.availableAt} /> },
        { label: 'Paid', value: <DateTime value={row.paidAt} /> },
        { label: 'Reversed', value: <DateTime value={row.reversedAt} /> },
        { label: 'Reason', value: row.reason ?? '—' },
        { label: 'Earning id', value: id(row.id) },
        { label: 'Submission id', value: id(row.submissionId) },
        { label: 'Payout item id', value: id(row.payoutItemId) },
        { label: 'Reverses', value: id(row.reversesEntryId) },
        { label: 'Reversed by', value: id(row.reversedByEntryId) },
        { label: 'Created by user', value: id(row.createdByUserId) },
        { label: 'Approved by user', value: id(row.approvedByUserId) },
      ]}
    />
  );
}

export function LedgerPage() {
  const can = useCan();
  const navigate = useNavigate();
  const [params, setParams] = useSearchParams();
  const [search, setSearch] = useState('');
  const [type, setType] = useState<string | undefined>();
  const [status, setStatus] = useState<string | undefined>();
  const [from, setFrom] = useState('');
  const [to, setTo] = useState('');
  const [campaignId, setCampaignId] = useState('');
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(25);
  const [selected, setSelected] = useState<LedgerRow | null>(null);
  const [reversing, setReversing] = useState<LedgerRow | null>(null);
  const [adjusting, setAdjusting] = useState(false);
  const userId = params.get('userId') ?? '';

  const filters = {
    search,
    type,
    status,
    userId: GUID_RE.test(userId) ? userId : undefined,
    campaignId: GUID_RE.test(campaignId.trim()) ? campaignId.trim() : undefined,
    from: from || undefined,
    to: to ? nextDay(to) : undefined,
  };
  const query = useLedger({ ...filters, page, pageSize });

  const reset = () => {
    setSearch('');
    setType(undefined);
    setStatus(undefined);
    setFrom('');
    setTo('');
    setCampaignId('');
    setParams({});
    setPage(1);
  };

  const rowActions = (row: LedgerRow): MenuEntry[] => [
    { id: 'view', label: 'View details', icon: <Eye />, onSelect: () => setSelected(row) },
    {
      id: 'balance',
      label: 'Participant balance',
      icon: <Wallet />,
      onSelect: () => navigate(`/finance/ledger/users/${row.user.id}`),
    },
    ...(can.adjust && canReverse(row)
      ? [
          {
            id: 'reverse',
            label: 'Reverse earning',
            icon: <Undo2 />,
            danger: true,
            onSelect: () => setReversing(row),
          },
        ]
      : []),
  ];

  const columns: DataTableColumn<LedgerRow>[] = [
    {
      id: 'created',
      header: 'Created',
      cell: (r) => (
        <button type="button" className="fin-linkbutton ui-link" onClick={() => setSelected(r)}>
          <DateTime value={r.createdAt} />
          <span className="visually-hidden"> — view details</span>
        </button>
      ),
      nowrap: true,
    },
    { id: 'user', header: 'Participant', primary: true, cell: (r) => <PersonCell user={r.user} link /> },
    { id: 'type', header: 'Type', cell: (r) => humanize(r.type) },
    {
      id: 'status',
      header: 'Status',
      cell: (r) => <StatusBadge kind="earning" status={r.status} size="sm" />,
    },
    {
      id: 'desc',
      header: 'Description',
      cell: (r) => (
        <span className="fin-stack">
          <span>{r.description}</span>
          {r.campaign && <Muted>{r.campaign.title}</Muted>}
        </span>
      ),
      hideOnMobile: true,
    },
    {
      id: 'original',
      header: 'Original',
      align: 'right',
      cell: (r) => <Money amount={r.originalAmount} currency={r.originalCurrency} />,
      hideOnMobile: true,
    },
    { id: 'rate', header: 'Rate', align: 'right', cell: (r) => <RateCell row={r} />, hideOnMobile: true },
    {
      id: 'settlement',
      header: 'Settlement',
      align: 'right',
      cell: (r) => <Money amount={r.settlementAmount} currency={r.settlementCurrency} colored />,
    },
  ];

  const exportQuery = { ...filters };

  return (
    <>
      <PageHeader
        title="Ledger"
        description="Every earning, adjustment and reversal. Entries are immutable: corrections are new entries."
        actions={
          <>
            {can.adjust && (
              <Button leadingIcon={<Plus />} onClick={() => setAdjusting(true)}>
                New adjustment
              </Button>
            )}
            <DownloadButton
              size="md"
              path="/finance/ledger/export.csv"
              fileName="ledger.csv"
              query={exportQuery}
            >
              Export CSV
            </DownloadButton>
          </>
        }
      />
      <Card>
        <CardBody className="stack">
          <FilterBar
            search={search}
            onSearchChange={(v) => {
              setSearch(v);
              setPage(1);
            }}
            searchPlaceholder="Email, name, description or an exact id"
            searchLabel="Search the ledger"
            filters={[
              {
                id: 'type',
                label: 'Type',
                options: EARNING_TYPES.map((t) => ({ value: t, label: humanize(t) })),
              },
              { id: 'status', label: 'Status', options: statusOptions('earning') },
            ]}
            values={{ type, status }}
            onFilterChange={(id, v) => {
              if (id === 'type') setType(v);
              else setStatus(v);
              setPage(1);
            }}
            onReset={reset}
          />
          <div className="fin-filter-extra">
            <FormField label="Created from">
              <Input
                type="date"
                size="sm"
                value={from}
                onChange={(e) => {
                  setFrom(e.target.value);
                  setPage(1);
                }}
              />
            </FormField>
            <FormField label="Created to (inclusive)">
              <Input
                type="date"
                size="sm"
                value={to}
                min={from || undefined}
                onChange={(e) => {
                  setTo(e.target.value);
                  setPage(1);
                }}
              />
            </FormField>
            <FormField
              label="Campaign id"
              error={
                campaignId.trim() && !GUID_RE.test(campaignId.trim()) ? 'Enter a full campaign id.' : null
              }
            >
              <Input
                size="sm"
                value={campaignId}
                spellCheck={false}
                onChange={(e) => {
                  setCampaignId(e.target.value);
                  setPage(1);
                }}
              />
            </FormField>
            {userId && (
              <div className="fin-filter-extra__chip">
                <span className="text-small">
                  Participant: <code className="fin-mono">{userId}</code>
                </span>
                <Button size="sm" variant="ghost" onClick={() => setParams({})}>
                  Clear participant
                </Button>
                <ButtonLink size="sm" variant="link" to={`/finance/ledger/users/${userId}`}>
                  View balance
                </ButtonLink>
              </div>
            )}
          </div>
          {query.isError ? (
            <QueryError error={query.error} onRetry={() => query.refetch()} />
          ) : (
            <>
              <DataTable
                caption="Ledger entries"
                columns={columns}
                rows={query.data?.items ?? []}
                getRowId={(r) => r.id}
                loading={query.isPending}
                rowActions={rowActions}
                rowLabel={(r) => `${r.user.displayName} ${r.description}`}
                emptyState={<EmptyState compact headingLevel={3} title="No ledger entries match" />}
              />
              {query.data && query.data.total > 0 && (
                <Pagination
                  page={page}
                  pageSize={pageSize}
                  total={query.data.total}
                  onPageChange={setPage}
                  onPageSizeChange={(s) => {
                    setPageSize(s);
                    setPage(1);
                  }}
                  label="Ledger pages"
                />
              )}
            </>
          )}
        </CardBody>
      </Card>

      <FinanceDrawer
        open={!!selected}
        onClose={() => setSelected(null)}
        title="Ledger entry"
        description={selected ? `${humanize(selected.type)} · ${selected.user.displayName}` : undefined}
        footer={
          selected && (
            <div className="cluster">
              <ButtonLink size="sm" variant="secondary" to={`/finance/ledger/users/${selected.user.id}`}>
                Participant balance
              </ButtonLink>
              {can.adjust && canReverse(selected) && (
                <Button
                  size="sm"
                  variant="danger"
                  leadingIcon={<Undo2 />}
                  onClick={() => {
                    setReversing(selected);
                    setSelected(null);
                  }}
                >
                  Reverse
                </Button>
              )}
            </div>
          )
        }
      >
        {selected && <LedgerDetail row={selected} />}
      </FinanceDrawer>

      <ReverseDialog row={reversing} onClose={() => setReversing(null)} />
      <AdjustmentDialog open={adjusting} onClose={() => setAdjusting(false)} />
    </>
  );
}
