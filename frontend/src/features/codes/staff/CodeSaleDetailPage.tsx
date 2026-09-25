import { useMutation, useQueryClient } from '@tanstack/react-query';
import { Check, HelpCircle, RotateCcw, X } from 'lucide-react';
import { useState } from 'react';
import { useParams } from 'react-router-dom';
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
  ErrorState,
  KeyValueList,
  Money,
  PageHeader,
  SkeletonText,
  StatusBadge,
  Timeline,
} from '@/components/ui';
import { useToast } from '@/components/ui/toastContext';
import { ProtectedImage } from '@/components/ProtectedImage';
import { api } from '@/lib/api/client';
import { Permissions } from '@/lib/auth/permissions';
import { useAuth } from '@/lib/auth/useAuth';
import { formatDateTime } from '@/lib/format/dates';
import { humanize } from '@/lib/format/text';
import { invalidateCodes, useCodeSale } from '../api/queries';
import type { CodeSale, CodeSaleDecision } from '../api/types';
import { eventLabel, SaleStatusBadge, SOURCE_LABEL, VerificationBadge } from '../labels';
import '../codes.css';

type Action = 'Approve' | 'Reject' | 'RequestInfo' | 'Refund';

const CAP_LABEL: Record<string, string> = {
  daily_cap: 'Daily cap per person',
  program_cap: 'Program cap per person',
  program_budget: 'Program budget',
};

/**
 * One code sale for staff: order facts, proof, reconciliation with the brand's report, the payout that was (or would be)
 * recorded, ledger entries and history. Reviewers decide (four-eyes enforced by the API); finance refunds.
 */
export function CodeSaleDetailPage({ basePath, backLabel }: { basePath: string; backLabel: string }) {
  const { saleId = '' } = useParams();
  const query = useCodeSale(saleId);
  const { hasPermission } = useAuth();
  const toast = useToast();
  const client = useQueryClient();
  const [action, setAction] = useState<Action | null>(null);
  const decide = useMutation({
    mutationFn: ({
      decision,
      reason,
      sale,
    }: {
      decision: CodeSaleDecision;
      reason: string;
      sale: CodeSale;
    }) =>
      api.post<CodeSale>(`/admin/code-sales/${sale.id}/decision`, {
        decision,
        reason: reason || null,
        concurrencyStamp: sale.concurrencyStamp,
      }),
    onSuccess: async (sale) => {
      toast.success(
        sale.status === 'Approved'
          ? 'Sale approved'
          : sale.status === 'Rejected'
            ? 'Sale rejected'
            : 'Information requested',
      );
      client.setQueryData(['codes', 'sale', sale.id], sale);
      await invalidateCodes(client);
    },
  });
  const refund = useMutation({
    mutationFn: ({ reason, sale }: { reason: string; sale: CodeSale }) =>
      api.post<CodeSale>(`/admin/code-sales/${sale.id}/refund`, { reason, confirm: true }),
    onSuccess: async (sale) => {
      toast.success(sale.status === 'Refunded' ? 'Refunded — commission reversed' : 'Sale cancelled');
      client.setQueryData(['codes', 'sale', sale.id], sale);
      await invalidateCodes(client);
    },
  });

  if (query.isPending) return <SkeletonText lines={8} />;
  if (query.isError) return <ErrorState error={query.error} onRetry={() => void query.refetch()} />;
  const s = query.data;
  const canRefund =
    hasPermission(Permissions.SalesReverse) &&
    (s.status === 'Approved' || s.status === 'Pending' || s.status === 'NeedsInfo');
  const open = s.status === 'Pending' || s.status === 'NeedsInfo';
  const currency = s.program.currency;

  return (
    <>
      <PageHeader
        title={`Order ${s.orderReference}`}
        description={`${s.program.brandName} · ${s.program.name}`}
        breadcrumbs={[{ label: backLabel, to: basePath }, { label: s.orderReference }]}
        meta={
          <span className="cluster dc-cluster-sm">
            <SaleStatusBadge status={s.status} size="md" />
            <VerificationBadge verification={s.verification} />
          </span>
        }
        actions={
          <span className="cluster dc-cluster-sm">
            {open && s.canDecide && (
              <>
                <Button leadingIcon={<Check />} onClick={() => setAction('Approve')}>
                  Approve
                </Button>
                {s.status === 'Pending' && (
                  <Button
                    variant="secondary"
                    leadingIcon={<HelpCircle />}
                    onClick={() => setAction('RequestInfo')}
                  >
                    Request info
                  </Button>
                )}
                <Button variant="secondary" leadingIcon={<X />} onClick={() => setAction('Reject')}>
                  Reject
                </Button>
              </>
            )}
            {canRefund && (
              <Button variant="ghost" leadingIcon={<RotateCcw />} onClick={() => setAction('Refund')}>
                {s.status === 'Approved' ? 'Mark refunded' : 'Mark cancelled'}
              </Button>
            )}
          </span>
        }
      />
      <div className="stack">
        {open && !s.canDecide && s.cannotDecideReason && <Alert tone="info">{s.cannotDecideReason}</Alert>}
        {s.isTestAccount && (
          <Alert tone="warning">This is a test account: its earnings are never paid out.</Alert>
        )}
        {s.userStatus !== 'Active' && (
          <Alert tone="danger">
            The participant’s account is {s.userStatus.toLowerCase()}: no commission can be granted.
          </Alert>
        )}
        {s.verification === 'Mismatch' && s.verificationNote && (
          <Alert tone="warning" title="Differs from the brand’s report">
            {s.verificationNote}
          </Alert>
        )}
        <div className="dc-grid-2">
          <div className="stack">
            <Card as="section" aria-labelledby="sale-order">
              <CardHeader titleId="sale-order" title="Order" />
              <CardBody className="stack">
                <KeyValueList
                  items={[
                    {
                      label: 'Participant',
                      value: `${s.person.displayName}${s.personEmail ? ` (${s.personEmail})` : ''}`,
                    },
                    { label: 'Code', value: <span className="dc-mono">{s.code.name}</span> },
                    {
                      label: 'Shared code',
                      value: s.group ? `Yes — group “${s.group.name}”` : 'No (personal)',
                    },
                    { label: 'Order date', value: formatDateTime(s.orderDate) },
                    {
                      label: 'Order value (net)',
                      value: <Money amount={s.netAmount} currency={s.currency} />,
                    },
                    {
                      label: 'Discount given',
                      value: <Money amount={s.discountAmount} currency={s.currency} />,
                    },
                    ...(s.currency !== currency
                      ? [
                          {
                            label: `In ${currency}`,
                            value: (
                              <span>
                                <Money amount={s.programNetAmount} currency={currency} /> (rate{' '}
                                {s.exchangeRate})
                              </span>
                            ),
                          },
                        ]
                      : []),
                    {
                      label: 'Source',
                      value:
                        SOURCE_LABEL[s.source] +
                        (s.createdBy && s.source !== 'Participant' ? ` · ${s.createdBy.displayName}` : ''),
                    },
                    { label: 'Reported', value: formatDateTime(s.submittedAt) },
                    ...(s.productNote ? [{ label: 'Products / notes', value: s.productNote }] : []),
                  ]}
                />
                {s.proofUrl ? (
                  <ProtectedImage src={s.proofUrl} alt="Proof of the order" className="dc-proof" />
                ) : (
                  <p className="text-small text-muted">No proof attached.</p>
                )}
              </CardBody>
            </Card>
            <Card as="section" aria-labelledby="sale-payout">
              <CardHeader titleId="sale-payout" title="Commission" />
              <CardBody className="stack">
                <KeyValueList
                  items={[
                    {
                      label: s.commissionAmount !== null ? 'Recorded' : 'Estimate (before caps)',
                      value:
                        (s.commissionAmount ?? s.estimatedCommission) !== null ? (
                          <Money amount={s.commissionAmount ?? s.estimatedCommission} currency={currency} />
                        ) : (
                          '—'
                        ),
                    },
                    ...(s.payoutSourceLabel ? [{ label: 'Payout source', value: s.payoutSourceLabel }] : []),
                    ...(s.appliedCaps.length > 0
                      ? [
                          {
                            label: 'Limits applied',
                            value: s.appliedCaps.map((c) => CAP_LABEL[c] ?? c).join(', '),
                          },
                        ]
                      : []),
                    ...(s.decidedBy
                      ? [
                          {
                            label: 'Decided by',
                            value: `${s.decidedBy.displayName} · ${formatDateTime(s.decidedAt!)}`,
                          },
                        ]
                      : []),
                    ...(s.decisionReason ? [{ label: 'Reason', value: s.decisionReason }] : []),
                    ...(s.refundReason ? [{ label: 'Refund', value: s.refundReason }] : []),
                  ]}
                />
                {s.earnings.length > 0 && (
                  <DataTable
                    caption="Ledger entries"
                    showCaption
                    rows={s.earnings}
                    getRowId={(e) => e.id}
                    columns={[
                      { id: 'type', header: 'Type', primary: true, cell: (e) => humanize(e.type) },
                      {
                        id: 'status',
                        header: 'Status',
                        cell: (e) => <StatusBadge kind="earning" status={e.status} />,
                      },
                      {
                        id: 'amount',
                        header: 'Amount',
                        align: 'right',
                        cell: (e) => <Money amount={e.amount} currency={e.currency} colored />,
                      },
                      { id: 'at', header: 'Recorded', cell: (e) => <DateTime value={e.createdAt} /> },
                    ]}
                  />
                )}
              </CardBody>
            </Card>
          </div>
          <div className="stack">
            <Card as="section" aria-labelledby="sale-verification">
              <CardHeader titleId="sale-verification" title="Brand report" />
              <CardBody className="stack">
                {s.verification === 'Unverified' ? (
                  <p className="text-small text-muted" style={{ margin: 0 }}>
                    Not in an imported sales report yet.
                  </p>
                ) : (
                  <KeyValueList
                    layout="inline"
                    items={[
                      { label: 'Result', value: <VerificationBadge verification={s.verification} /> },
                      ...(s.reportedNetAmount !== null
                        ? [
                            {
                              label: 'Reported value',
                              value: <Money amount={s.reportedNetAmount} currency={s.currency} />,
                            },
                          ]
                        : []),
                      ...(s.reportedOrderDate
                        ? [{ label: 'Reported date', value: formatDateTime(s.reportedOrderDate) }]
                        : []),
                      ...(s.verificationNote ? [{ label: 'Note', value: s.verificationNote }] : []),
                    ]}
                  />
                )}
              </CardBody>
            </Card>
            <Card as="section" aria-labelledby="sale-history">
              <CardHeader titleId="sale-history" title="History" />
              <CardBody>
                <Timeline
                  label="Sale history"
                  items={s.events.map((e, i) => ({
                    id: `${i}`,
                    title: eventLabel(e.action),
                    description: e.reason ?? undefined,
                    timestamp: e.at,
                    actor: e.actor?.displayName,
                  }))}
                />
              </CardBody>
            </Card>
            {s.isTestAccount && (
              <Badge tone="warning" size="sm">
                Test account
              </Badge>
            )}
          </div>
        </div>
      </div>

      <ConfirmDialog
        open={action === 'Approve'}
        onClose={() => setAction(null)}
        title="Approve this sale?"
        description="The commission is recorded in the ledger (caps and the program budget apply) and paid with the next payout."
        confirmLabel="Approve sale"
        tone="primary"
        onConfirm={async () => {
          await decide.mutateAsync({ decision: 'Approve', reason: '', sale: s });
          setAction(null);
        }}
      />
      <ConfirmDialog
        open={action === 'Reject'}
        onClose={() => setAction(null)}
        title="Reject this sale?"
        description="The participant sees your reason. The order number can be reported again afterwards."
        confirmLabel="Reject sale"
        tone="danger"
        requireReason
        reasonLabel="Reason (shown to the participant)"
        reasonMinLength={5}
        onConfirm={async ({ reason }) => {
          await decide.mutateAsync({ decision: 'Reject', reason, sale: s });
          setAction(null);
        }}
      />
      <ConfirmDialog
        open={action === 'RequestInfo'}
        onClose={() => setAction(null)}
        title="Ask for more information"
        description="The sale goes back to the participant, who can update it and send it back for review."
        confirmLabel="Send request"
        tone="primary"
        requireReason
        reasonLabel="What do you need?"
        reasonMinLength={5}
        onConfirm={async ({ reason }) => {
          await decide.mutateAsync({ decision: 'RequestInfo', reason, sale: s });
          setAction(null);
        }}
      />
      <ConfirmDialog
        open={action === 'Refund'}
        onClose={() => setAction(null)}
        title={s.status === 'Approved' ? 'Mark the order refunded?' : 'Mark the order cancelled?'}
        description={
          s.status === 'Approved'
            ? 'Its commission is reversed in the ledger; if it was already paid, the amount is deducted from the participant’s next payout.'
            : 'The pending sale is closed without a commission.'
        }
        confirmLabel={s.status === 'Approved' ? 'Refund and reverse' : 'Cancel sale'}
        tone="danger"
        requireReason
        reasonMinLength={5}
        onConfirm={async ({ reason }) => {
          await refund.mutateAsync({ reason, sale: s });
          setAction(null);
        }}
      />
    </>
  );
}
