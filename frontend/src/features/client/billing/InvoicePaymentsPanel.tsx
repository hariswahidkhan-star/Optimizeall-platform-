import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useMemo, useState } from 'react';
import {
  Alert,
  Badge,
  Button,
  Card,
  CardBody,
  CardHeader,
  DataTable,
  Dialog,
  EmptyState,
  FileDrop,
  FormField,
  Input,
  Money,
  Select,
  Skeleton,
  Textarea,
  useToast,
  type Tone,
} from '@/components/ui';
import { billingErrorMessage, formatDateOnly, todayIso } from '@/features/agency/billing/lib';
import { api } from '@/lib/api/client';
import type { IsoDateTime } from '@/lib/api/types';

export type ClaimStatus = 'Pending' | 'Confirmed' | 'Rejected';

export interface ClientPaymentClaim {
  id: string;
  invoiceId: string;
  amount: number;
  currency: string;
  method: string;
  reference: string;
  paidOn: string;
  note: string | null;
  status: ClaimStatus;
  createdAt: IsoDateTime;
  reviewedAt: IsoDateTime | null;
  reviewNote: string | null;
  hasProof: boolean;
}

export interface ClientInvoicePayments {
  invoiceId: string;
  number: string | null;
  status: string;
  currency: string;
  total: number;
  amountPaid: number;
  amountCredited: number;
  balance: number;
  payments: {
    id: string;
    amount: number;
    currency: string;
    method: string;
    reference: string;
    paidOn: string;
    status: string;
    reversedAt: IsoDateTime | null;
  }[];
  claims: ClientPaymentClaim[];
  canReportPayment: boolean;
}

const METHODS = [
  { value: 'BankTransfer', label: 'Bank transfer' },
  { value: 'Card', label: 'Card' },
  { value: 'Cheque', label: 'Cheque' },
  { value: 'Cash', label: 'Cash' },
  { value: 'PayPal', label: 'PayPal' },
  { value: 'Other', label: 'Other' },
];

const CLAIM_TONE: Record<ClaimStatus, Tone> = {
  Pending: 'warning',
  Confirmed: 'success',
  Rejected: 'danger',
};
const CLAIM_LABEL: Record<ClaimStatus, string> = {
  Pending: 'Waiting for confirmation',
  Confirmed: 'Confirmed',
  Rejected: 'Not matched',
};

const key = (invoiceId: string) => ['client-billing', 'invoice-payments', invoiceId] as const;

export function useClientInvoicePayments(invoiceId: string) {
  return useQuery({
    queryKey: key(invoiceId),
    queryFn: ({ signal }) =>
      api.get<ClientInvoicePayments>(`/client/billing/invoices/${invoiceId}/payments`, { signal }),
    enabled: !!invoiceId,
  });
}

/** "I've paid" dialog: reports a transfer for the agency to confirm (nothing changes on the invoice until they do). */
export function ReportPaymentDialog({
  open,
  onClose,
  invoiceId,
  currency,
  balance,
}: {
  open: boolean;
  onClose: () => void;
  invoiceId: string;
  currency: string;
  balance: number;
}) {
  const qc = useQueryClient();
  const toast = useToast();
  // One idempotency key per opened dialog: a double click or a retry creates one report.
  // eslint-disable-next-line react-hooks/exhaustive-deps
  const requestId = useMemo(() => crypto.randomUUID(), [open]);
  const [amount, setAmount] = useState('');
  const [method, setMethod] = useState('BankTransfer');
  const [reference, setReference] = useState('');
  const [paidOn, setPaidOn] = useState(todayIso());
  const [note, setNote] = useState('');
  const [file, setFile] = useState<File | null>(null);
  const [touched, setTouched] = useState(false);
  const [error, setError] = useState<string | null>(null);
  useEffect(() => {
    if (open) {
      setAmount(String(balance));
      setMethod('BankTransfer');
      setReference('');
      setPaidOn(todayIso());
      setNote('');
      setFile(null);
      setTouched(false);
      setError(null);
    }
  }, [open, balance]);
  const submit = useMutation({
    mutationFn: async () => {
      const claim = await api.post<ClientPaymentClaim>(
        `/client/billing/invoices/${invoiceId}/payment-claims`,
        {
          requestId,
          amount: Number(amount),
          method,
          reference: reference.trim(),
          paidOn,
          note: note.trim() || undefined,
        },
      );
      if (file && !claim.hasProof) {
        const form = new FormData();
        form.append('file', file);
        await api.post(`/client/billing/payment-claims/${claim.id}/proofs`, form);
      }
      return claim;
    },
    onSettled: () => qc.invalidateQueries({ queryKey: ['client-billing'] }),
  });
  const value = Number(amount);
  const amountError = !amount || !Number.isFinite(value) || value <= 0 ? 'Enter the amount you paid.' : null;
  const referenceError =
    reference.trim().length < 3
      ? 'Enter the transfer reference from your bank (at least 3 characters).'
      : null;
  return (
    <Dialog
      open={open}
      onClose={onClose}
      title="I’ve paid this invoice"
      description="Tell us about your transfer. We’ll check our bank account and confirm it; the invoice updates once confirmed."
      dismissible={!submit.isPending}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={submit.isPending}>
            Cancel
          </Button>
          <Button
            loading={submit.isPending}
            onClick={async () => {
              setTouched(true);
              if (amountError || referenceError || submit.isPending) return;
              setError(null);
              try {
                await submit.mutateAsync();
                toast.success('Thanks! We’ll confirm your payment shortly.');
                onClose();
              } catch (e) {
                setError(billingErrorMessage(e));
              }
            }}
          >
            Send
          </Button>
        </>
      }
    >
      <div className="stack">
        <FormField label={`Amount paid (${currency})`} required error={touched ? amountError : null}>
          <Input inputMode="decimal" value={amount} onChange={(e) => setAmount(e.target.value)} />
        </FormField>
        <FormField label="How did you pay?" required>
          <Select value={method} onChange={(e) => setMethod(e.target.value)} options={METHODS} />
        </FormField>
        <FormField
          label="Transfer reference"
          hint="The reference or transaction id shown by your bank."
          required
          error={touched ? referenceError : null}
        >
          <Input maxLength={120} value={reference} onChange={(e) => setReference(e.target.value)} />
        </FormField>
        <FormField label="Date paid" required>
          <Input type="date" value={paidOn} max={todayIso()} onChange={(e) => setPaidOn(e.target.value)} />
        </FormField>
        <FormField label="Note" optional>
          <Textarea rows={2} maxLength={1000} value={note} onChange={(e) => setNote(e.target.value)} />
        </FormField>
        <FileDrop
          label="Proof of payment (optional)"
          hint="PDF, PNG, JPEG or WebP, up to 10 MB."
          value={file}
          onChange={setFile}
          accept={['application/pdf', 'image/png', 'image/jpeg', 'image/webp']}
          maxSizeBytes={10 * 1024 * 1024}
        />
        {error && (
          <Alert tone="danger" role="alert">
            {error}
          </Alert>
        )}
      </div>
    </Dialog>
  );
}

/** Payment history of one invoice in the client portal, with the client's own payment reports. */
export function InvoicePaymentsPanel({ invoiceId }: { invoiceId: string }) {
  const query = useClientInvoicePayments(invoiceId);
  const [reporting, setReporting] = useState(false);
  if (query.isError) return <Alert tone="danger">{billingErrorMessage(query.error)}</Alert>;
  if (!query.data) return <Skeleton height="8rem" />;
  const d = query.data;
  const pendingClaims = d.claims.filter((c) => c.status === 'Pending');
  return (
    <Card>
      <CardHeader
        title="Payments"
        actions={
          d.canReportPayment ? (
            <Button size="sm" onClick={() => setReporting(true)}>
              I’ve paid
            </Button>
          ) : undefined
        }
      />
      <CardBody>
        <div className="stack">
          {pendingClaims.length > 0 && (
            <Alert tone="info" role="status">
              We’re checking{' '}
              {pendingClaims.length === 1
                ? 'the payment you reported'
                : `${pendingClaims.length} payments you reported`}
              . The invoice updates as soon as we confirm it.
            </Alert>
          )}
          <DataTable
            caption="Payments received"
            rows={d.payments}
            getRowId={(p) => p.id}
            columns={[
              { id: 'date', header: 'Date', primary: true, cell: (p) => formatDateOnly(p.paidOn) },
              {
                id: 'amount',
                header: 'Amount',
                align: 'right',
                cell: (p) => <Money amount={p.amount} currency={p.currency} />,
              },
              { id: 'reference', header: 'Reference', cell: (p) => p.reference },
              {
                id: 'status',
                header: 'Status',
                cell: (p) => <Badge tone={p.status === 'Paid' ? 'success' : 'neutral'}>{p.status}</Badge>,
              },
            ]}
            emptyState={<EmptyState compact headingLevel={3} title="No payments received yet" />}
          />
          {d.claims.length > 0 && (
            <DataTable
              caption="Payments you reported"
              showCaption
              rows={d.claims}
              getRowId={(c) => c.id}
              columns={[
                { id: 'date', header: 'Date paid', primary: true, cell: (c) => formatDateOnly(c.paidOn) },
                {
                  id: 'amount',
                  header: 'Amount',
                  align: 'right',
                  cell: (c) => <Money amount={c.amount} currency={c.currency} />,
                },
                { id: 'reference', header: 'Reference', cell: (c) => c.reference },
                {
                  id: 'status',
                  header: 'Status',
                  cell: (c) => (
                    <span className="stack">
                      <Badge tone={CLAIM_TONE[c.status]}>{CLAIM_LABEL[c.status]}</Badge>
                      {c.status === 'Rejected' && c.reviewNote && (
                        <span className="text-small">{c.reviewNote}</span>
                      )}
                    </span>
                  ),
                },
              ]}
            />
          )}
        </div>
      </CardBody>
      <ReportPaymentDialog
        open={reporting}
        onClose={() => setReporting(false)}
        invoiceId={invoiceId}
        currency={d.currency}
        balance={d.balance}
      />
    </Card>
  );
}
