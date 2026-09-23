import { useQueryClient } from '@tanstack/react-query';
import {
  Ban,
  Banknote,
  Eye,
  FileLock2,
  ListChecks,
  PauseCircle,
  PlayCircle,
  RefreshCw,
  ShieldCheck,
  XCircle,
} from 'lucide-react';
import { useState, type ReactNode } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import {
  Alert,
  Badge,
  Button,
  Card,
  CardBody,
  CardHeader,
  ConfirmDialog,
  DataTable,
  DateTime,
  EmptyState,
  FilterBar,
  KeyValueList,
  Money,
  PageHeader,
  Pagination,
  ProgressBar,
  Skeleton,
  StatusBadge,
  Tabs,
  statusOptions,
  useToast,
  type DataTableColumn,
  type MenuEntry,
} from '@/components/ui';
import { api } from '@/lib/api/client';
import { humanize } from '@/lib/format/text';
import { humanError } from '../api/errors';
import {
  financeKeys,
  useBatch,
  useCancelBatch,
  useHoldItem,
  useMarkFailed,
  useRegenerateBatch,
} from '../api/hooks';
import type { DispatchResult, FinalizeResponse, PayoutBatchDetail, PayoutItem } from '../api/types';
import { DownloadButton, Muted, QueryError } from '../components/common';
import { dateOnlyToDisplay } from '../lib/format';
import { useCan } from '../lib/useCan';
import { BulkRecordDialog } from './BulkRecordDialog';
import { FinalizeDialog } from './FinalizeDialog';
import { ItemBreakdownDrawer } from './ItemBreakdownDrawer';
import { ReconciliationTab } from './ReconciliationTab';
import { RecordPaymentDialog } from './RecordPaymentDialog';
import { UnholdDialog } from './UnholdDialog';
import { WarningsPanel } from './WarningsPanel';

type Dialog =
  | { kind: 'finalize' }
  | { kind: 'regenerate' }
  | { kind: 'cancel' }
  | { kind: 'bulk' }
  | { kind: 'instructions' }
  | { kind: 'hold'; item: PayoutItem }
  | { kind: 'unhold'; item: PayoutItem }
  | { kind: 'record'; item: PayoutItem }
  | { kind: 'fail'; item: PayoutItem }
  | null;

function StatusExplainer({ detail }: { detail: PayoutBatchDetail }) {
  const b = detail.batch;
  switch (b.status) {
    case 'Draft':
      return (
        <Alert tone="info" title="Draft — nothing has been paid">
          Review the warnings and items, hold anything you are not comfortable paying, then a different
          finance user finalizes the batch.
        </Alert>
      );
    case 'Finalized':
      return (
        <Alert tone="warning" title="Awaiting manual payment — no money has been sent">
          Optimize All does not transfer money. Pay each participant outside the platform (bank, PayPal or
          wallet), then record the payment reference for each item.
          <div className="fin-progress">
            <ProgressBar
              value={b.paidCount}
              max={Math.max(1, b.itemCount)}
              label="Payments recorded"
              valueText={`${b.paidCount} of ${b.itemCount} items paid`}
            />
          </div>
        </Alert>
      );
    case 'Completed':
      return (
        <Alert tone="success" title="Completed">
          Every item is paid, failed, held or cancelled. Check the reconciliation tab and archive the export.
        </Alert>
      );
    case 'Cancelled':
      return (
        <Alert tone="neutral" title="Cancelled">
          {detail.cancelReason ? `Reason: ${detail.cancelReason}. ` : ''}Its earnings returned to the
          participants’ balances.
        </Alert>
      );
    default:
      return null;
  }
}

function DispatchResults({
  results,
  items,
  onDismiss,
}: {
  results: DispatchResult[];
  items: PayoutItem[];
  onDismiss: () => void;
}) {
  const byId = new Map(items.map((i) => [i.itemId, i]));
  return (
    <Card as="section" aria-labelledby="dispatch-title" className="fin-dispatch">
      <CardHeader
        titleId="dispatch-title"
        title="Batch finalized — no money has been sent"
        description="The manual payment provider cannot transfer money. Every item needs a manual payment and a recorded reference."
        actions={
          <Button size="sm" variant="ghost" onClick={onDismiss}>
            Dismiss
          </Button>
        }
      />
      <CardBody>
        <ul className="fin-results" aria-label="Dispatch results">
          {results.map((r) => {
            const item = byId.get(r.itemId);
            return (
              <li key={r.itemId} className="fin-results__row">
                <span className="fin-results__who">
                  <strong>{item?.user.displayName ?? r.itemId}</strong>
                  {item && <Money amount={item.amount} currency={item.currency} />}
                </span>
                <Badge tone={r.status === 'RequiresManualAction' ? 'warning' : 'neutral'} dot>
                  {r.status === 'RequiresManualAction' ? 'Manual payment required' : humanize(r.status)}
                </Badge>
                <span className="fin-results__message text-small">
                  {r.message}
                  {r.reused ? ' (existing attempt reused)' : ''}
                </span>
              </li>
            );
          })}
        </ul>
      </CardBody>
    </Card>
  );
}

export function BatchReviewPage({ tab = 'items' }: { tab?: 'items' | 'reconciliation' }) {
  const { batchId = '' } = useParams();
  const navigate = useNavigate();
  const can = useCan();
  const toast = useToast();
  const client = useQueryClient();
  const [search, setSearch] = useState('');
  const [itemStatus, setItemStatus] = useState<string | undefined>();
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(25);
  const [dialog, setDialog] = useState<Dialog>(null);
  const [openItemId, setOpenItemId] = useState<string | null>(null);
  const [dispatch, setDispatch] = useState<FinalizeResponse | null>(null);

  const query = useBatch(batchId, { search, itemStatus, page, pageSize });
  const hold = useHoldItem(batchId);
  const markFailed = useMarkFailed(batchId);
  const regenerate = useRegenerateBatch(batchId);
  const cancel = useCancelBatch(batchId);

  const refresh = () => client.invalidateQueries({ queryKey: financeKeys.batchRoot(batchId) });

  if (query.isPending) {
    return (
      <>
        <PageHeader
          title="Payout batch"
          breadcrumbs={[{ label: 'Payout batches', to: '/finance/batches' }, { label: 'Loading…' }]}
        />
        <div className="stack">
          <Skeleton height={140} />
          <Skeleton height={320} />
        </div>
      </>
    );
  }
  if (query.isError) {
    return (
      <>
        <PageHeader
          title="Payout batch"
          breadcrumbs={[{ label: 'Payout batches', to: '/finance/batches' }, { label: 'Not available' }]}
        />
        <QueryError error={query.error} onRetry={() => query.refetch()} compact={false} />
      </>
    );
  }

  const detail = query.data;
  const b = detail.batch;
  const isDraft = b.status === 'Draft';
  const isFinalized = b.status === 'Finalized';
  const payable = isFinalized || b.status === 'Completed';
  const selfPrepared = !!b.preparedBy && b.preparedBy.id === can.userId;
  const heldTotal = detail.totalsByStatus.find((t) => t.status === 'Held');
  const canCancel = can.prepare && (isDraft || (isFinalized && b.paidCount === 0));

  type ItemAction = { id: string; label: string; icon: ReactNode; onSelect: () => void; danger?: boolean };
  const itemActionList = (item: PayoutItem): ItemAction[] => {
    const entries: ItemAction[] = [];
    if (isDraft && can.prepare && item.status === 'Pending')
      entries.push({
        id: 'hold',
        label: 'Hold item',
        icon: <PauseCircle />,
        onSelect: () => setDialog({ kind: 'hold', item }),
      });
    if (isDraft && can.prepare && item.status === 'Held')
      entries.push({
        id: 'unhold',
        label: 'Release hold',
        icon: <PlayCircle />,
        onSelect: () => setDialog({ kind: 'unhold', item }),
      });
    if (isFinalized && can.recordPayment && item.status === 'AwaitingPayment') {
      entries.push({
        id: 'record',
        label: 'Record payment',
        icon: <Banknote />,
        onSelect: () => setDialog({ kind: 'record', item }),
      });
      entries.push({
        id: 'fail',
        label: 'Mark failed',
        icon: <XCircle />,
        danger: true,
        onSelect: () => setDialog({ kind: 'fail', item }),
      });
    }
    return entries;
  };
  const itemActions = (item: PayoutItem): MenuEntry[] => [
    { id: 'view', label: 'View breakdown', icon: <Eye />, onSelect: () => setOpenItemId(item.itemId) },
    ...itemActionList(item),
  ];

  const columns: DataTableColumn<PayoutItem>[] = [
    {
      id: 'who',
      header: 'Participant',
      primary: true,
      cell: (i) => (
        <button type="button" className="fin-linkbutton fin-person" onClick={() => setOpenItemId(i.itemId)}>
          <span className="fin-person__name ui-link">{i.user.displayName || i.user.email}</span>
          <span className="fin-person__email">{i.user.email}</span>
        </button>
      ),
    },
    { id: 'country', header: 'Country', cell: (i) => i.user.country || '—', hideOnMobile: true },
    {
      id: 'amount',
      header: 'Amount',
      align: 'right',
      cell: (i) => <Money amount={i.amount} currency={i.currency} />,
    },
    { id: 'earnings', header: 'Earnings', align: 'right', cell: (i) => i.earningCount, hideOnMobile: true },
    { id: 'dest', header: 'Destination', cell: (i) => i.destinationHint ?? <Muted>None on file</Muted> },
    {
      id: 'status',
      header: 'Status',
      cell: (i) => (
        <span className="fin-stack">
          <StatusBadge kind="payoutItem" status={i.status} />
          {(i.holdReason || i.failureReason) && <Muted>{i.holdReason ?? i.failureReason}</Muted>}
        </span>
      ),
    },
    { id: 'ref', header: 'Payment reference', cell: (i) => i.paymentReference ?? <Muted>—</Muted> },
    { id: 'paidAt', header: 'Paid at', cell: (i) => <DateTime value={i.paidAt} />, hideOnMobile: true },
    ...(isFinalized && can.recordPayment
      ? [
          {
            id: 'action',
            header: 'Payment',
            cell: (i: PayoutItem) =>
              i.status === 'AwaitingPayment' ? (
                <Button size="sm" variant="secondary" onClick={() => setDialog({ kind: 'record', item: i })}>
                  Record<span className="visually-hidden"> payment for {i.user.displayName}</span>
                </Button>
              ) : null,
          },
        ]
      : []),
  ];

  const drawerActions = (item: PayoutItem) =>
    itemActionList(item).map((entry) => (
      <Button
        key={entry.id}
        size="sm"
        variant={entry.danger ? 'danger' : entry.id === 'record' ? 'primary' : 'secondary'}
        leadingIcon={entry.icon}
        onClick={() => {
          setOpenItemId(null);
          entry.onSelect();
        }}
      >
        {entry.label}
      </Button>
    ));

  const itemsTab = (
    <div className="stack">
      <FilterBar
        search={search}
        onSearchChange={(v) => {
          setSearch(v);
          setPage(1);
        }}
        searchPlaceholder="Search participant name or email"
        searchLabel="Search items"
        filters={[{ id: 'itemStatus', label: 'Status', options: statusOptions('payoutItem') }]}
        values={{ itemStatus }}
        onFilterChange={(_, v) => {
          setItemStatus(v);
          setPage(1);
        }}
        onReset={() => {
          setSearch('');
          setItemStatus(undefined);
          setPage(1);
        }}
      />
      <DataTable
        caption={`Items of ${b.reference}`}
        columns={columns}
        rows={detail.items.items}
        getRowId={(i) => i.itemId}
        rowActions={itemActions}
        rowLabel={(i) => i.user.displayName}
        loading={query.isFetching && query.isPlaceholderData}
        emptyState={
          <EmptyState compact headingLevel={3} title="No items" description="No items match these filters." />
        }
      />
      {detail.items.total > 0 && (
        <Pagination
          page={page}
          pageSize={pageSize}
          total={detail.items.total}
          onPageChange={setPage}
          onPageSizeChange={(s) => {
            setPageSize(s);
            setPage(1);
          }}
          label="Items pages"
        />
      )}
    </div>
  );

  return (
    <>
      <PageHeader
        breadcrumbs={[{ label: 'Payout batches', to: '/finance/batches' }, { label: b.reference }]}
        title={b.reference}
        description={`Period ${b.periodKey} · ${b.currency}`}
        meta={<StatusBadge kind="payout" status={b.status} />}
        actions={
          <>
            {isDraft && can.finalize && (
              <Button
                leadingIcon={<ShieldCheck />}
                onClick={() => setDialog({ kind: 'finalize' })}
                disabled={selfPrepared || b.itemCount === 0}
                aria-describedby={selfPrepared ? 'self-prepared-hint' : undefined}
              >
                Finalize
              </Button>
            )}
            {isFinalized && can.recordPayment && (
              <Button leadingIcon={<ListChecks />} onClick={() => setDialog({ kind: 'bulk' })}>
                Record payments
              </Button>
            )}
            {payable && can.recordPayment && (
              <Button
                variant="secondary"
                leadingIcon={<FileLock2 />}
                onClick={() => setDialog({ kind: 'instructions' })}
              >
                Payment instructions
              </Button>
            )}
            <DownloadButton
              size="md"
              path={`/finance/payout-batches/${b.id}/export.csv`}
              fileName={`payout-batch-${b.reference}.csv`}
            >
              Export CSV
            </DownloadButton>
            {isDraft && can.prepare && (
              <Button
                variant="secondary"
                leadingIcon={<RefreshCw />}
                onClick={() => setDialog({ kind: 'regenerate' })}
              >
                Regenerate
              </Button>
            )}
            {canCancel && (
              <Button variant="ghost" leadingIcon={<Ban />} onClick={() => setDialog({ kind: 'cancel' })}>
                Cancel batch
              </Button>
            )}
          </>
        }
      />

      <div className="stack fin-page">
        {selfPrepared && isDraft && can.finalize && (
          <Alert tone="info" id="self-prepared-hint" title="Four-eyes rule">
            You prepared this batch, so a different finance user must finalize it.
          </Alert>
        )}
        {dispatch && (
          <DispatchResults
            results={dispatch.dispatch}
            items={detail.items.items}
            onDismiss={() => setDispatch(null)}
          />
        )}
        <StatusExplainer detail={detail} />

        <Card as="section" aria-labelledby="batch-summary-title">
          <CardHeader titleId="batch-summary-title" title="Summary" headingLevel={2} />
          <CardBody className="stack">
            <KeyValueList
              items={[
                { label: 'Payable items', value: b.itemCount },
                { label: 'Total', value: <Money amount={b.totalAmount} currency={b.currency} /> },
                {
                  label: 'Paid',
                  value: (
                    <>
                      <Money amount={b.paidAmount} currency={b.currency} />{' '}
                      <Muted>({b.paidCount} items)</Muted>
                    </>
                  ),
                },
                { label: 'Currency', value: b.currency },
                { label: 'Cutoff', value: <DateTime value={b.cutoffAt} withZone /> },
                { label: 'Payment date', value: dateOnlyToDisplay(b.paymentDate) },
                {
                  label: 'Prepared by',
                  value: (
                    <>
                      {b.preparedBy ? b.preparedBy.displayName : 'System job'} ·{' '}
                      <DateTime value={b.createdAt} />
                    </>
                  ),
                },
                {
                  label: 'Finalized by',
                  value: b.finalizedBy ? (
                    <>
                      {b.finalizedBy.displayName} · <DateTime value={b.finalizedAt} />
                    </>
                  ) : (
                    '—'
                  ),
                },
                ...(detail.notes ? [{ label: 'Notes', value: detail.notes }] : []),
              ]}
            />
            {detail.totalsByStatus.length > 0 && (
              <div>
                <h3 className="fin-subheading">Totals by item status</h3>
                <ul className="fin-totals" aria-label="Totals by item status">
                  {detail.totalsByStatus.map((t) => (
                    <li key={t.status}>
                      <StatusBadge kind="payoutItem" status={t.status} />
                      <span className="tabular">{t.count}</span>
                      <Money amount={t.amount} currency={b.currency} />
                    </li>
                  ))}
                </ul>
              </div>
            )}
          </CardBody>
        </Card>

        <WarningsPanel warnings={detail.warnings} currency={b.currency} onOpenItem={setOpenItemId} />

        <Tabs
          label="Batch sections"
          value={tab}
          onValueChange={(id) =>
            navigate(
              id === 'reconciliation'
                ? `/finance/batches/${b.id}/reconciliation`
                : `/finance/batches/${b.id}`,
              {
                replace: true,
              },
            )
          }
          tabs={[
            { id: 'items', label: 'Items', badge: detail.items.total, content: itemsTab },
            {
              id: 'reconciliation',
              label: 'Reconciliation',
              content: <ReconciliationTab batchId={b.id} reference={b.reference} />,
            },
          ]}
        />
      </div>

      <ItemBreakdownDrawer
        batchId={b.id}
        itemId={openItemId}
        onClose={() => setOpenItemId(null)}
        actions={drawerActions}
      />

      <FinalizeDialog
        open={dialog?.kind === 'finalize'}
        onClose={() => setDialog(null)}
        batch={b}
        concurrencyStamp={detail.concurrencyStamp}
        heldCount={heldTotal?.count ?? 0}
        onRefresh={refresh}
        onFinalized={(result) => {
          setDispatch(result);
          toast.success(
            `${b.reference} finalized`,
            'No money has been sent. Pay each item and record its reference.',
          );
        }}
      />

      <ConfirmDialog
        open={dialog?.kind === 'regenerate'}
        onClose={() => setDialog(null)}
        title="Regenerate this batch?"
        description="Releases every item and re-runs selection for the same period. Holds on items are cleared, and you become the preparer (so someone else must finalize)."
        requireReason
        reasonMinLength={5}
        confirmLabel="Regenerate"
        onConfirm={async ({ reason }) => {
          try {
            await regenerate.mutateAsync(reason);
            toast.success('Batch regenerated', 'Review the refreshed items and warnings.');
          } catch (e) {
            throw humanError(e);
          }
        }}
      />

      <ConfirmDialog
        open={dialog?.kind === 'cancel'}
        onClose={() => setDialog(null)}
        tone="danger"
        title={`Cancel ${b.reference}?`}
        description="All open items are cancelled and their earnings return to the participants’ balances. This cannot be undone; the period can be prepared again deliberately."
        requireReason
        reasonMinLength={5}
        confirmText={b.reference}
        confirmLabel="Cancel batch"
        cancelLabel="Keep batch"
        onConfirm={async ({ reason }) => {
          try {
            await cancel.mutateAsync(reason);
            toast.success(`${b.reference} cancelled`);
          } catch (e) {
            throw humanError(e);
          }
        }}
      />

      <ConfirmDialog
        open={dialog?.kind === 'hold'}
        onClose={() => setDialog(null)}
        title={dialog?.kind === 'hold' ? `Hold ${dialog.item.user.displayName}’s item?` : 'Hold item'}
        description="The item is excluded from payment. At finalize its earnings return to the participant’s balance for a later batch."
        requireReason
        reasonMinLength={5}
        confirmLabel="Hold item"
        onConfirm={async ({ reason }) => {
          if (dialog?.kind !== 'hold') return;
          try {
            await hold.mutateAsync({ itemId: dialog.item.itemId, reason });
            toast.success('Item held');
          } catch (e) {
            throw humanError(e);
          }
        }}
      />

      <UnholdDialog
        batchId={b.id}
        item={dialog?.kind === 'unhold' ? dialog.item : null}
        onClose={() => setDialog(null)}
      />

      <RecordPaymentDialog
        batchId={b.id}
        item={dialog?.kind === 'record' ? dialog.item : null}
        onClose={() => setDialog(null)}
        onRefresh={refresh}
      />

      <ConfirmDialog
        open={dialog?.kind === 'fail'}
        onClose={() => setDialog(null)}
        tone="danger"
        title={
          dialog?.kind === 'fail'
            ? `Mark ${dialog.item.user.displayName}’s payment as failed?`
            : 'Mark failed'
        }
        description="Use this when a transfer bounced or could not be made. The earnings return to the participant’s balance for the next batch, and they are asked to check their payout details."
        requireReason
        reasonMinLength={5}
        reasonLabel="Failure reason"
        confirmLabel="Mark failed"
        onConfirm={async ({ reason }) => {
          if (dialog?.kind !== 'fail') return;
          try {
            await markFailed.mutateAsync({ itemId: dialog.item.itemId, reason });
            toast.success('Marked as failed');
          } catch (e) {
            throw humanError(e);
          }
        }}
      />

      <BulkRecordDialog open={dialog?.kind === 'bulk'} onClose={() => setDialog(null)} batchId={b.id} />

      <PaymentInstructionsDialog
        open={dialog?.kind === 'instructions'}
        onClose={() => setDialog(null)}
        batchId={b.id}
        reference={b.reference}
      />
    </>
  );
}

/**
 * Sensitive download: the CSV contains decrypted payout destinations. Requires an explicit confirmation and sends
 * `?confirm=true`; the server audits every download.
 */
export function PaymentInstructionsDialog({
  open,
  onClose,
  batchId,
  reference,
}: {
  open: boolean;
  onClose: () => void;
  batchId: string;
  reference: string;
}) {
  const toast = useToast();
  return (
    <ConfirmDialog
      open={open}
      onClose={onClose}
      tone="danger"
      title="Download payment instructions?"
      description="This file contains the full bank, PayPal or wallet details of every item awaiting payment."
      confirmLabel="Download (audited)"
      onConfirm={async () => {
        try {
          await api.download(
            `/finance/payout-batches/${batchId}/payment-instructions.csv`,
            `payment-instructions-${reference}.csv`,
            { query: { confirm: true } },
          );
          toast.success('Payment instructions downloaded', 'This download was recorded in the audit log.');
        } catch (e) {
          throw humanError(e);
        }
      }}
    >
      <Alert tone="warning" title="This download is audited">
        Your name, the time and the items included are recorded in the audit log. Store the file only in the
        approved secure location and delete it once the payments are made.
      </Alert>
    </ConfirmDialog>
  );
}
