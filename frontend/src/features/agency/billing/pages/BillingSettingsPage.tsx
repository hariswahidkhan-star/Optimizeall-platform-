import { Plus } from 'lucide-react';
import { useEffect, useState } from 'react';
import {
  Alert,
  Badge,
  Button,
  Card,
  CardBody,
  CardHeader,
  Checkbox,
  ConfirmDialog,
  DataTable,
  ErrorState,
  FormField,
  Input,
  PageHeader,
  Select,
  Skeleton,
  Switch,
  Textarea,
  useToast,
  type DataTableColumn,
} from '@/components/ui';
import { useSupportedCurrencies } from '@/lib/api/meta';
import { Permissions } from '@/lib/auth/permissions';
import { useAuth } from '@/lib/auth/useAuth';
import { useBillingSettings, useDeleteTaxRate, useSaveSettings, useSaveTaxRate, useTaxRates } from '../api/hooks';
import { ServiceCatalogManager } from '../components/ServiceCatalogManager';
import type { BillingSettings, TaxRate } from '../api/types';
import { billingErrorMessage } from '../lib';
import { FormDialog } from '../components/FormDialog';
import '../billing.css';

/** "3, 7, 14" → [3, 7, 14]; invalid entries are dropped (the server validates the range). */
function parseDays(text: string): number[] {
  return text
    .split(/[,\s]+/)
    .map((v) => v.trim())
    .filter((v) => v !== '' && !Number.isNaN(Number(v)))
    .map((v) => Math.trunc(Number(v)));
}

function TaxRateDialog({ rate, open, onClose }: { rate: TaxRate | null; open: boolean; onClose: () => void }) {
  const save = useSaveTaxRate(rate?.id);
  const [name, setName] = useState('');
  const [percent, setPercent] = useState('0');
  const [inclusive, setInclusive] = useState(false);
  const [country, setCountry] = useState('');
  const [active, setActive] = useState(true);
  const [reviewed, setReviewed] = useState(true);
  useEffect(() => {
    if (!open) return;
    setName(rate?.name ?? '');
    setPercent(String(rate?.ratePercent ?? 0));
    setInclusive(rate?.inclusive ?? false);
    setCountry(rate?.countryCode ?? '');
    setActive(rate?.isActive ?? true);
    setReviewed(!(rate?.needsReview ?? false));
  }, [open, rate]);
  return (
    <FormDialog
      open={open}
      onClose={onClose}
      title={rate ? `Edit ${rate.name}` : 'New tax rate'}
      description="Changing a rate never changes existing invoices — lines keep the rate they were created with."
      submitLabel="Save rate"
      onSubmit={async () => {
        await save.mutateAsync({
          name: name.trim(),
          ratePercent: Number(percent),
          inclusive,
          countryCode: country.trim() || null,
          isActive: active,
          needsReview: !reviewed,
          notes: rate?.notes ?? null,
          concurrencyStamp: rate?.concurrencyStamp,
        });
      }}
    >
      <FormField label="Name" required>
        <Input value={name} maxLength={80} onChange={(e) => setName(e.target.value)} />
      </FormField>
      <FormField label="Rate (%)" required>
        <Input type="number" min={0} max={100} step="any" value={percent} onChange={(e) => setPercent(e.target.value)} />
      </FormField>
      <FormField label="Country" optional hint="Two-letter code, e.g. GB">
        <Input value={country} maxLength={2} onChange={(e) => setCountry(e.target.value.toUpperCase())} />
      </FormField>
      <Checkbox label="Prices include this tax" checked={inclusive} onChange={(e) => setInclusive(e.target.checked)} />
      <Checkbox label="Active" checked={active} onChange={(e) => setActive(e.target.checked)} />
      <Checkbox label="Reviewed by finance" checked={reviewed} onChange={(e) => setReviewed(e.target.checked)} />
    </FormDialog>
  );
}

export function BillingSettingsPage() {
  const { hasPermission } = useAuth();
  const canEdit = hasPermission(Permissions.BillingSettings);
  const toast = useToast();
  const settings = useBillingSettings();
  const rates = useTaxRates(true);
  const save = useSaveSettings();
  const [form, setForm] = useState<BillingSettings | null>(null);
  const [confirming, setConfirming] = useState(false);
  const [editingRate, setEditingRate] = useState<TaxRate | null>(null);
  const [rateOpen, setRateOpen] = useState(false);
  const [deletingRate, setDeletingRate] = useState<TaxRate | null>(null);
  const [reminderText, setReminderText] = useState('');
  const [termsText, setTermsText] = useState('');
  const deleteRate = useDeleteTaxRate();
  const currencies = useSupportedCurrencies(form?.defaultCurrency);

  useEffect(() => {
    if (settings.data && !form) {
      setForm(settings.data);
      setReminderText(settings.data.reminderOffsetsDays.join(', '));
      setTermsText((settings.data.paymentTermsOptions ?? []).join(', '));
    }
  }, [settings.data, form]);

  if (settings.isError) return <ErrorState error={settings.error} onRetry={() => void settings.refetch()} />;
  if (!form) return <Skeleton height="24rem" />;
  const set = <K extends keyof BillingSettings>(key: K, value: BillingSettings[K]) => setForm({ ...form, [key]: value });
  const text = (key: keyof BillingSettings) => (form[key] as string | null) ?? '';

  const rateColumns: DataTableColumn<TaxRate>[] = [
    { id: 'name', header: 'Name', primary: true, cell: (r) => r.name },
    { id: 'rate', header: 'Rate', align: 'right', cell: (r) => `${r.ratePercent}%${r.inclusive ? ' (incl.)' : ''}` },
    { id: 'country', header: 'Country', cell: (r) => r.countryCode ?? '—' },
    {
      id: 'status',
      header: 'Status',
      cell: (r) => (
        <span className="bill-actions">
          <Badge tone={r.isActive ? 'success' : 'neutral'}>{r.isActive ? 'Active' : 'Inactive'}</Badge>
          {r.needsReview && <Badge tone="warning">Needs review</Badge>}
        </span>
      ),
    },
    {
      id: 'edit',
      header: <span className="visually-hidden">Actions</span>,
      align: 'right',
      cell: (r) =>
        canEdit ? (
          <span className="bill-actions">
            <Button
              size="sm"
              variant="ghost"
              onClick={() => {
                setEditingRate(r);
                setRateOpen(true);
              }}
            >
              Edit <span className="visually-hidden">{r.name}</span>
            </Button>
            <Button size="sm" variant="ghost" onClick={() => setDeletingRate(r)}>
              Delete <span className="visually-hidden">{r.name}</span>
            </Button>
          </span>
        ) : null,
    },
  ];

  return (
    <>
      <PageHeader title="Billing settings" breadcrumbs={[{ label: 'Billing', to: '/agency/billing' }, { label: 'Settings' }]} />
      {!canEdit && (
        <Alert tone="info" title="Read only">
          Changing billing settings needs the billing.settings permission.
        </Alert>
      )}
      <div className="stack">
        <Card>
          <CardHeader title="Numbering & terms" />
          <CardBody className="bill-grid">
            <FormField label="Invoice prefix" hint={`Next numbers look like ${form.invoicePrefix}-2026-0001`}>
              <Input value={form.invoicePrefix} disabled={!canEdit} maxLength={10} onChange={(e) => set('invoicePrefix', e.target.value.toUpperCase())} />
            </FormField>
            <FormField label="Credit note prefix">
              <Input value={form.creditNotePrefix} disabled={!canEdit} maxLength={10} onChange={(e) => set('creditNotePrefix', e.target.value.toUpperCase())} />
            </FormField>
            <FormField label="Contract prefix">
              <Input value={form.contractPrefix} disabled={!canEdit} maxLength={10} onChange={(e) => set('contractPrefix', e.target.value.toUpperCase())} />
            </FormField>
            <FormField label="Proposal prefix">
              <Input value={form.proposalPrefix} disabled={!canEdit} maxLength={10} onChange={(e) => set('proposalPrefix', e.target.value.toUpperCase())} />
            </FormField>
            <FormField label="Number digits" hint="3 to 8, e.g. 4 gives 0001">
              <Input type="number" min={3} max={8} value={String(form.numberPadding)} disabled={!canEdit} onChange={(e) => set('numberPadding', Number(e.target.value))} />
            </FormField>
            <FormField label="Default payment terms (days)">
              <Input type="number" min={0} max={365} value={String(form.paymentTermsDays)} disabled={!canEdit} onChange={(e) => set('paymentTermsDays', Number(e.target.value))} />
            </FormField>
            <FormField label="Payment terms offered" hint="Days, separated by commas (e.g. 0, 7, 14, 30). 0 means due on receipt.">
              <Input
                value={termsText}
                disabled={!canEdit}
                onChange={(e) => {
                  setTermsText(e.target.value);
                  set('paymentTermsOptions', parseDays(e.target.value));
                }}
              />
            </FormField>
            <FormField label="Reminder schedule" hint="Days relative to the due date, separated by commas (negative = before). Up to 8.">
              <Input
                value={reminderText}
                disabled={!canEdit}
                onChange={(e) => {
                  setReminderText(e.target.value);
                  set('reminderOffsetsDays', parseDays(e.target.value));
                }}
              />
            </FormField>
            <FormField label="Default currency">
              <Select value={form.defaultCurrency} options={currencies.options} disabled={!canEdit} onChange={(e) => set('defaultCurrency', e.target.value)} />
            </FormField>
            <Switch checked={form.invoiceOnAcceptance} disabled={!canEdit} onCheckedChange={(v) => set('invoiceOnAcceptance', v)} label="Invoice on proposal acceptance" description="Create the first invoice when a client accepts a proposal." />
            <Switch checked={form.autoIssueInvoices} disabled={!canEdit} onCheckedChange={(v) => set('autoIssueInvoices', v)} label="Issue generated invoices automatically" description="Otherwise retainer and acceptance invoices wait as drafts for review." />
            <Switch checked={form.remindersEnabled} disabled={!canEdit} onCheckedChange={(v) => set('remindersEnabled', v)} label="Payment reminders" description={`Days relative to due date: ${form.reminderOffsetsDays.join(', ')}`} />
          </CardBody>
        </Card>
        <Card>
          <CardHeader title="Company & payment instructions" description="Shown on invoices and in the client portal." />
          <CardBody className="stack">
            <FormField label="Company name" required>
              <Input value={form.companyName} disabled={!canEdit} maxLength={200} onChange={(e) => set('companyName', e.target.value)} />
            </FormField>
            <FormField label="Company address" optional>
              <Textarea rows={3} value={text('companyAddress')} disabled={!canEdit} maxLength={1000} onChange={(e) => set('companyAddress', e.target.value || null)} />
            </FormField>
            <FormField label="Tax ID" optional>
              <Input value={text('companyTaxId')} disabled={!canEdit} maxLength={64} onChange={(e) => set('companyTaxId', e.target.value || null)} />
            </FormField>
            <FormField label="Bank details" optional>
              <Textarea rows={3} value={text('bankDetails')} disabled={!canEdit} maxLength={2000} onChange={(e) => set('bankDetails', e.target.value || null)} />
            </FormField>
            <FormField label="Payment link text" optional hint="e.g. Pay by card at https://pay.example.com">
              <Input value={text('paymentLinkText')} disabled={!canEdit} maxLength={500} onChange={(e) => set('paymentLinkText', e.target.value || null)} />
            </FormField>
            <FormField label="Payment instructions" optional>
              <Textarea rows={3} value={text('paymentInstructions')} disabled={!canEdit} maxLength={2000} onChange={(e) => set('paymentInstructions', e.target.value || null)} />
            </FormField>
            <FormField label="Invoice footer" optional>
              <Textarea rows={2} value={text('invoiceFooter')} disabled={!canEdit} maxLength={1000} onChange={(e) => set('invoiceFooter', e.target.value || null)} />
            </FormField>
            {canEdit && (
              <div>
                <Button onClick={() => setConfirming(true)}>Save settings</Button>
              </div>
            )}
          </CardBody>
        </Card>
        <Card>
          <CardHeader
            title="Tax rates"
            description="Seeded examples are marked for review — confirm them with your accountant before use."
            actions={
              canEdit && (
                <Button
                  size="sm"
                  leadingIcon={<Plus />}
                  onClick={() => {
                    setEditingRate(null);
                    setRateOpen(true);
                  }}
                >
                  Add rate
                </Button>
              )
            }
          />
          <CardBody>
            <DataTable caption="Tax rates" columns={rateColumns} rows={rates.data ?? []} getRowId={(r) => r.id} loading={rates.isPending} />
          </CardBody>
        </Card>
        <Card>
          <CardHeader title="Service catalog" description="What you sell, with list prices. Offered as “Add from catalog” in proposals, contracts and invoices." />
          <CardBody>
            <ServiceCatalogManager canEdit={canEdit} />
          </CardBody>
        </Card>
      </div>
      <TaxRateDialog rate={editingRate} open={rateOpen} onClose={() => setRateOpen(false)} />
      <ConfirmDialog
        open={deletingRate !== null}
        onClose={() => setDeletingRate(null)}
        tone="danger"
        title={`Delete the tax rate “${deletingRate?.name ?? ''}”?`}
        description="Only a rate that nothing uses can be deleted. A rate used by documents, catalog items or templates can be deactivated instead; issued documents keep their tax either way."
        confirmLabel="Delete"
        onConfirm={async () => {
          if (!deletingRate) return;
          try {
            await deleteRate.mutateAsync(deletingRate.id);
            toast.success('Tax rate deleted');
          } catch (error) {
            throw new Error(billingErrorMessage(error));
          }
        }}
      />
      <ConfirmDialog
        open={confirming}
        onClose={() => setConfirming(false)}
        title="Save billing settings?"
        description="Numbering, terms and payment instructions apply to every new invoice. The change is audited."
        requireReason
        reasonMinLength={5}
        confirmLabel="Save settings"
        onConfirm={async ({ reason }) => {
          try {
            const saved = await save.mutateAsync({ settings: form, reason, confirm: true });
            setForm(saved);
            toast.success('Billing settings saved');
          } catch (error) {
            throw new Error(billingErrorMessage(error));
          }
        }}
      />
    </>
  );
}
