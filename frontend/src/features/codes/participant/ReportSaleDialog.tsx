import { useMutation, useQueryClient } from '@tanstack/react-query';
import { Send } from 'lucide-react';
import { useState, type FormEvent } from 'react';
import { useNavigate } from 'react-router-dom';
import { Alert, Button, Dialog, FileDrop, FormField, Input, Select, Textarea } from '@/components/ui';
import { useToast } from '@/components/ui/toastContext';
import { api } from '@/lib/api/client';
import { ApiError, errorMessage } from '@/lib/api/errors';
import { useSupportedCurrencies } from '@/lib/api/meta';
import { useAuth } from '@/lib/auth/useAuth';
import { utcToZonedLocal, zonedLocalToUtcIso } from '@/features/participant/lib/zonedTime';
import { invalidateCodes } from '../api/queries';
import type { MyCode, MyCodeSale } from '../api/types';
import { NET_HELP } from '../labels';

const PROOF_MAX_BYTES = 10 * 1024 * 1024;
const FUTURE_TOLERANCE_MS = 10 * 60_000;

type Field =
  'codeId' | 'orderReference' | 'orderDate' | 'netAmount' | 'discountAmount' | 'currency' | 'proof';

export interface ReportSaleDialogProps {
  codes: MyCode[];
  /** Preselected code. */
  codeId?: string;
  /** Editing an existing (pending / needs-info) sale. */
  sale?: MyCodeSale;
  onClose: () => void;
}

/** Report (or edit) a sale made with one of the participant's codes — multipart POST/PUT /me/code-sales. */
export function ReportSaleDialog({ codes, codeId, sale, onClose }: ReportSaleDialogProps) {
  const { user } = useAuth();
  const zone = user?.timeZone;
  const toast = useToast();
  const navigate = useNavigate();
  const client = useQueryClient();
  const usable = codes.filter(
    (c) => c.isActive || c.codeId === sale?.codeId || c.inactiveReason?.startsWith('No longer'),
  );
  const [code, setCode] = useState(sale?.codeId ?? codeId ?? (usable.length === 1 ? usable[0]!.codeId : ''));
  const selected = codes.find((c) => c.codeId === code);
  const [orderReference, setOrderReference] = useState(sale?.orderReference ?? '');
  const [orderDate, setOrderDate] = useState(() => utcToZonedLocal(sale?.orderDate ?? new Date(), zone));
  const [net, setNet] = useState(sale ? String(sale.netAmount) : '');
  const [discount, setDiscount] = useState(
    sale && sale.discountAmount > 0 ? String(sale.discountAmount) : '',
  );
  const [currency, setCurrency] = useState(sale?.currency ?? selected?.currency ?? 'USD');
  const [note, setNote] = useState(sale?.productNote ?? '');
  const [proof, setProof] = useState<File | null>(null);
  const [errors, setErrors] = useState<Partial<Record<Field, string>>>({});
  const currencies = useSupportedCurrencies(currency);

  const save = useMutation({
    mutationFn: (form: FormData) =>
      sale
        ? api.uploadPut<MyCodeSale>(`/me/code-sales/${sale.id}`, form)
        : api.upload<MyCodeSale>('/me/code-sales', form),
    onSuccess: async (result) => {
      toast.success(
        sale ? 'Sale updated' : 'Sale reported',
        'We’ll check it with the brand and let you know.',
      );
      await invalidateCodes(client);
      onClose();
      if (!sale) navigate(`/app/codes/sales/${result.id}`);
    },
  });
  const server = save.error instanceof ApiError ? save.error : null;
  const fieldError = (f: Field) =>
    errors[f] ?? server?.errors?.[f]?.[0] ?? server?.errors?.[f.charAt(0).toUpperCase() + f.slice(1)]?.[0];

  const onSubmit = (event: FormEvent) => {
    event.preventDefault();
    if (save.isPending) return;
    const found: Partial<Record<Field, string>> = {};
    if (!code) found.codeId = 'Choose the code the customer used.';
    if (orderReference.trim().length < 2)
      found.orderReference = 'Enter the order number from the confirmation.';
    const iso = zonedLocalToUtcIso(orderDate, zone);
    if (!iso) found.orderDate = 'Enter when the order was placed.';
    else if (new Date(iso).getTime() > Date.now() + FUTURE_TOLERANCE_MS)
      found.orderDate = 'The order date can’t be in the future.';
    const netValue = Number(net);
    if (!net || !Number.isFinite(netValue) || netValue <= 0)
      found.netAmount = 'Enter the order value (greater than 0).';
    if (discount && (!Number.isFinite(Number(discount)) || Number(discount) < 0))
      found.discountAmount = 'Enter the discount or leave it empty.';
    if (selected?.requireProof && !proof && !sale?.proofUrl)
      found.proof = 'This program needs a screenshot of the order or receipt.';
    setErrors(found);
    if (Object.keys(found).length > 0) return;
    const form = new FormData();
    if (!sale) form.append('codeId', code);
    form.append('orderReference', orderReference.trim());
    form.append('orderDate', iso!);
    form.append('netAmount', String(netValue));
    form.append('discountAmount', discount ? String(Number(discount)) : '0');
    form.append('currency', currency);
    if (note.trim()) form.append('productNote', note.trim());
    if (proof) form.append('proof', proof, proof.name);
    if (sale) form.append('concurrencyStamp', sale.concurrencyStamp);
    save.mutate(form);
  };

  return (
    <Dialog
      open
      onClose={onClose}
      size="lg"
      title={sale ? `Update sale ${sale.orderReference}` : 'Report a sale'}
      description={
        selected
          ? `${selected.brandName} · code ${selected.code} · you earn ${selected.yourRate}`
          : 'A sale made with one of your codes.'
      }
      dismissible={!save.isPending}
      footer={
        <>
          <Button variant="ghost" onClick={onClose} disabled={save.isPending}>
            Cancel
          </Button>
          <Button type="submit" form="report-sale-form" loading={save.isPending} leadingIcon={<Send />}>
            {sale ? 'Send for review' : 'Report sale'}
          </Button>
        </>
      }
    >
      <form id="report-sale-form" className="stack" onSubmit={onSubmit} noValidate>
        {save.isError && (
          <Alert tone="danger" title={sale ? 'The sale was not updated' : 'The sale was not reported'}>
            {errorMessage(save.error)}
          </Alert>
        )}
        {!sale && (
          <FormField label="Code used" required error={fieldError('codeId')}>
            <Select
              value={code}
              onChange={(e) => {
                setCode(e.target.value);
                const next = codes.find((c) => c.codeId === e.target.value);
                if (next) setCurrency(next.currency);
              }}
              placeholder="Choose a code"
              options={usable.map((c) => ({ value: c.codeId, label: `${c.code} — ${c.brandName}` }))}
            />
          </FormField>
        )}
        <div className="dc-form-grid">
          <FormField
            label="Order number"
            required
            error={fieldError('orderReference')}
            hint="From the order confirmation."
          >
            <Input
              value={orderReference}
              maxLength={100}
              onChange={(e) => setOrderReference(e.target.value)}
              autoComplete="off"
            />
          </FormField>
          <FormField label="Order date and time" required error={fieldError('orderDate')}>
            <Input type="datetime-local" value={orderDate} onChange={(e) => setOrderDate(e.target.value)} />
          </FormField>
        </div>
        <div className="dc-form-grid">
          <FormField label="Order value" required error={fieldError('netAmount')} hint={NET_HELP}>
            <Input
              type="number"
              inputMode="decimal"
              min="0"
              step="any"
              value={net}
              onChange={(e) => setNet(e.target.value)}
            />
          </FormField>
          <FormField label="Discount given" optional error={fieldError('discountAmount')}>
            <Input
              type="number"
              inputMode="decimal"
              min="0"
              step="any"
              value={discount}
              onChange={(e) => setDiscount(e.target.value)}
            />
          </FormField>
          <FormField label="Currency" required error={fieldError('currency')}>
            <Select
              value={currency}
              onChange={(e) => setCurrency(e.target.value)}
              options={currencies.options}
            />
          </FormField>
        </div>
        <FormField label="Products or notes" optional>
          <Textarea rows={2} maxLength={1000} value={note} onChange={(e) => setNote(e.target.value)} />
        </FormField>
        <FileDrop
          label={
            selected?.requireProof
              ? 'Screenshot of the order or receipt'
              : 'Screenshot of the order or receipt (optional)'
          }
          value={proof}
          onChange={setProof}
          maxSizeBytes={PROOF_MAX_BYTES}
          required={selected?.requireProof}
          error={fieldError('proof')}
          hint={
            sale?.proofUrl
              ? 'A proof is already attached; add a new one to replace it.'
              : 'PNG, JPEG or WebP, up to 10 MB. Hide personal details of the customer.'
          }
        />
      </form>
    </Dialog>
  );
}
