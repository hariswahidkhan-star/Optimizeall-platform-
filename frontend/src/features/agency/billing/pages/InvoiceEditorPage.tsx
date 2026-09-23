import { useEffect, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { Alert, Button, Card, CardBody, CardHeader, ErrorState, FormField, Input, PageHeader, Select, Skeleton, Textarea, useToast } from '@/components/ui';
import { isApiError } from '@/lib/api/errors';
import { useSupportedCurrencies } from '@/lib/api/meta';
import { useClientOptions, useInvoice, useSaveInvoice } from '../api/hooks';
import type { PriceLineInput } from '../api/types';
import { LineItemsEditor, LivePreviewTotals, emptyLine } from '../components/LineItemsEditor';
import { billingErrorMessage } from '../lib';
import '../billing.css';

/** Create or edit a draft invoice. Totals come from the preview API; saving re-prices everything on the server. */
export function InvoiceEditorPage() {
  const { invoiceId } = useParams();
  const navigate = useNavigate();
  const toast = useToast();
  const existing = useInvoice(invoiceId);
  const clients = useClientOptions();
  const save = useSaveInvoice(invoiceId);
  const [clientAccountId, setClientId] = useState('');
  const [currency, setCurrency] = useState('USD');
  const [terms, setTerms] = useState('');
  const [reference, setReference] = useState('');
  const [notes, setNotes] = useState('');
  const [lines, setLines] = useState<PriceLineInput[]>([emptyLine()]);
  const [error, setError] = useState<unknown>(null);
  const [loaded, setLoaded] = useState(!invoiceId);
  const currencies = useSupportedCurrencies(currency);

  useEffect(() => {
    const inv = existing.data;
    if (!inv || loaded) return;
    setClientId(inv.clientAccountId);
    setCurrency(inv.currency);
    setTerms(String(inv.paymentTermsDays));
    setReference(inv.reference ?? '');
    setNotes(inv.notes ?? '');
    setLines(
      inv.lines.map((l) => ({
        description: l.description,
        serviceSlug: l.serviceSlug,
        quantity: l.quantity,
        unitPrice: l.unitPrice,
        discountType: l.discountType,
        discountValue: l.discountValue,
        taxRateId: l.taxRateId,
      })),
    );
    setLoaded(true);
  }, [existing.data, loaded]);

  if (invoiceId && existing.isError) return <ErrorState error={existing.error} onRetry={() => void existing.refetch()} />;
  if (!loaded) return <Skeleton height="20rem" />;
  if (existing.data && existing.data.status !== 'Draft') {
    return (
      <Alert tone="info" title="This invoice is issued">
        Issued invoices can’t be edited. Open it and issue a credit note to correct it.
      </Alert>
    );
  }

  const fieldErrors = isApiError(error) ? error.errors : undefined;
  const submit = async () => {
    setError(null);
    if (!clientAccountId) {
      setError(new Error('Choose the client to invoice.'));
      return;
    }
    try {
      const saved = await save.mutateAsync({
        clientAccountId,
        currency,
        paymentTermsDays: terms === '' ? undefined : Number(terms),
        reference: reference.trim() || undefined,
        notes: notes.trim() || undefined,
        lines,
        concurrencyStamp: existing.data?.concurrencyStamp,
      });
      toast.success(invoiceId ? 'Draft saved' : 'Draft invoice created');
      navigate(`/agency/billing/invoices/${saved.id}`);
    } catch (err) {
      setError(err);
    }
  };

  return (
    <>
      <PageHeader
        title={invoiceId ? `Edit draft ${existing.data?.reference ?? ''}`.trim() : 'New invoice'}
        breadcrumbs={[{ label: 'Billing', to: '/agency/billing' }, { label: 'Invoices', to: '/agency/billing/invoices' }, { label: invoiceId ? 'Edit' : 'New' }]}
      />
      <div className="bill-two-col">
        <div className="stack">
          <Card>
            <CardHeader title="Client & terms" />
            <CardBody className="stack">
              <FormField label="Client" required>
                <Select
                  value={clientAccountId}
                  placeholder="Choose a client"
                  options={(clients.data ?? []).map((c) => ({ value: c.id, label: `${c.name} (${c.currency})` }))}
                  onChange={(e) => {
                    setClientId(e.target.value);
                    const client = clients.data?.find((c) => c.id === e.target.value);
                    if (client && !invoiceId) setCurrency(client.currency);
                  }}
                />
              </FormField>
              <FormField label="Currency" required>
                <Select value={currency} options={currencies.options} onChange={(e) => setCurrency(e.target.value)} />
              </FormField>
              <FormField label="Payment terms (days)" optional hint="Defaults to the billing settings.">
                <Input type="number" min={0} max={365} value={terms} onChange={(e) => setTerms(e.target.value)} />
              </FormField>
              <FormField label="Reference" optional hint="PO number or contract reference.">
                <Input value={reference} maxLength={100} onChange={(e) => setReference(e.target.value)} />
              </FormField>
              <FormField label="Notes" optional>
                <Textarea rows={3} maxLength={4000} value={notes} onChange={(e) => setNotes(e.target.value)} />
              </FormField>
            </CardBody>
          </Card>
          <Card>
            <CardHeader title="Line items" />
            <CardBody>
              <LineItemsEditor lines={lines} onChange={setLines} currency={currency} errors={fieldErrors} />
            </CardBody>
          </Card>
        </div>
        <Card>
          <CardHeader title="Totals" description="Calculated by the server." />
          <CardBody className="stack">
            <LivePreviewTotals lines={lines} currency={currency} previewPath="/agency/billing/invoices/preview" />
            {error !== null && (
              <Alert tone="danger" role="alert">
                {billingErrorMessage(error)}
              </Alert>
            )}
            <Button onClick={() => void submit()} loading={save.isPending}>
              {invoiceId ? 'Save draft' : 'Create draft'}
            </Button>
          </CardBody>
        </Card>
      </div>
    </>
  );
}
