import { useMutation, useQueryClient } from '@tanstack/react-query';
import { Pencil, Undo2 } from 'lucide-react';
import { useState } from 'react';
import { useParams } from 'react-router-dom';
import {
  Alert,
  Button,
  Card,
  CardBody,
  CardHeader,
  ConfirmDialog,
  ErrorState,
  KeyValueList,
  Money,
  PageHeader,
  SkeletonText,
  Timeline,
} from '@/components/ui';
import { useToast } from '@/components/ui/toastContext';
import { ProtectedImage } from '@/components/ProtectedImage';
import { api } from '@/lib/api/client';
import { formatDateTime } from '@/lib/format/dates';
import { invalidateCodes, useMyCodeSale, useMyCodes } from '../api/queries';
import type { MyCodeSale } from '../api/types';
import { eventLabel, SaleStatusBadge, SOURCE_LABEL } from '../labels';
import { ReportSaleDialog } from './ReportSaleDialog';
import '../codes.css';

/** Participant: one reported sale — facts, status, what the reviewer said, timeline; edit or withdraw while pending. */
export function MyCodeSalePage() {
  const { saleId = '' } = useParams();
  const query = useMyCodeSale(saleId);
  const codes = useMyCodes();
  const toast = useToast();
  const client = useQueryClient();
  const [editing, setEditing] = useState(false);
  const [withdrawing, setWithdrawing] = useState(false);
  const withdraw = useMutation({
    mutationFn: (reason: string) =>
      api.post<MyCodeSale>(`/me/code-sales/${saleId}/withdraw`, { reason: reason || null }),
    onSuccess: async () => {
      toast.success('Sale withdrawn');
      await invalidateCodes(client);
    },
  });

  if (query.isPending)
    return (
      <div className="pp-page">
        <SkeletonText lines={6} />
      </div>
    );
  if (query.isError) return <ErrorState error={query.error} onRetry={() => void query.refetch()} />;
  const s = query.data;
  return (
    <div className="pp-page">
      <PageHeader
        title={`Order ${s.orderReference}`}
        description={`${s.brandName} · ${s.programName}`}
        breadcrumbs={[{ label: 'My discount codes', to: '/app/codes' }, { label: s.orderReference }]}
        meta={<SaleStatusBadge status={s.status} size="md" />}
        actions={
          s.canEdit ? (
            <span className="cluster dc-cluster-sm">
              <Button
                variant="secondary"
                leadingIcon={<Pencil />}
                onClick={() => setEditing(true)}
                disabled={!codes.data}
              >
                {s.status === 'NeedsInfo' ? 'Add the information' : 'Edit'}
              </Button>
              <Button variant="ghost" leadingIcon={<Undo2 />} onClick={() => setWithdrawing(true)}>
                Withdraw
              </Button>
            </span>
          ) : undefined
        }
      />
      {s.status === 'NeedsInfo' && s.decisionReason && (
        <Alert tone="warning" title="The review team needs more information">
          {s.decisionReason}
        </Alert>
      )}
      {s.status === 'Rejected' && s.decisionReason && (
        <Alert tone="danger" title="Not approved">
          {s.decisionReason}
        </Alert>
      )}
      {(s.status === 'Refunded' || s.status === 'Cancelled') && (
        <Alert
          tone="neutral"
          title={s.status === 'Refunded' ? 'The order was refunded' : 'The order was cancelled'}
        >
          {s.decisionReason ?? 'The brand reported the order refunded.'}{' '}
          {s.status === 'Refunded' &&
            'Its commission was reversed; if it was already paid, it is deducted from your next payout.'}
        </Alert>
      )}
      {s.status === 'Approved' && s.commissionAmount !== null && (
        <Alert tone="success" title="Approved">
          You earned <Money amount={s.commissionAmount} currency={s.programCurrency} />. It is paid with your
          regular payouts.
        </Alert>
      )}
      <div className="dc-grid-2">
        <Card as="section" aria-labelledby="sale-facts">
          <CardHeader titleId="sale-facts" title="Sale" />
          <CardBody className="stack">
            <KeyValueList
              items={[
                { label: 'Code', value: <span className="dc-mono">{s.code}</span> },
                { label: 'Order date', value: formatDateTime(s.orderDate) },
                { label: 'Order value', value: <Money amount={s.netAmount} currency={s.currency} /> },
                { label: 'Discount given', value: <Money amount={s.discountAmount} currency={s.currency} /> },
                {
                  label: s.status === 'Approved' ? 'You earned' : 'Estimated commission',
                  value:
                    (s.commissionAmount ?? s.estimatedCommission) !== null ? (
                      <Money
                        amount={s.commissionAmount ?? s.estimatedCommission}
                        currency={s.programCurrency}
                      />
                    ) : (
                      '—'
                    ),
                },
                { label: 'Reported', value: formatDateTime(s.submittedAt) },
                { label: 'Source', value: SOURCE_LABEL[s.source] },
                ...(s.productNote ? [{ label: 'Products / notes', value: s.productNote }] : []),
              ]}
            />
            {s.proofUrl && <ProtectedImage src={s.proofUrl} alt="Proof of the order" className="dc-proof" />}
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
                actor: e.actor ? 'You' : 'Review team',
              }))}
            />
          </CardBody>
        </Card>
      </div>
      {editing && codes.data && (
        <ReportSaleDialog codes={codes.data} sale={s} onClose={() => setEditing(false)} />
      )}
      <ConfirmDialog
        open={withdrawing}
        onClose={() => setWithdrawing(false)}
        title="Withdraw this sale?"
        description="It won’t be reviewed or paid. You can report the order again later."
        confirmLabel="Withdraw sale"
        tone="danger"
        onConfirm={async ({ reason }) => {
          await withdraw.mutateAsync(reason);
          setWithdrawing(false);
        }}
      />
    </div>
  );
}
