import { useEffect, useState } from 'react';
import { FormField, Input, Select, Textarea, useToast } from '@/components/ui';
import { FormDialog } from '@/features/agency/billing/components/FormDialog';
import { isApiError } from '@/lib/api/errors';
import { useSupportedCurrencies } from '@/lib/api/meta';
import { useAssignees, useCompanies, useCrmOptions, useSaveCompany, useSaveContact, useSaveDeal } from '../api/hooks';
import type { Company, CompanySize, ConsentStatus, Contact, Deal, DealSource, LifecycleStage } from '../api/types';
import { CONSENT_OPTIONS, LIFECYCLE_OPTIONS, SOURCE_OPTIONS, splitTags } from '../lib';

function fieldError(error: unknown, field: string): string | undefined {
  return isApiError(error) ? error.fieldError(field) : undefined;
}

/** Suggestions for a free-text input (agency-editable lists from CRM settings). */
function OptionList({ id, values }: { id: string; values?: string[] }) {
  return (
    <datalist id={id}>
      {(values ?? []).map((v) => (
        <option key={v} value={v} />
      ))}
    </datalist>
  );
}

function useOwnerOptions() {
  const assignees = useAssignees();
  return [{ value: '', label: 'Unassigned' }, ...(assignees.data ?? []).map((u) => ({ value: u.id, label: u.displayName }))];
}

function useCompanyOptions() {
  const companies = useCompanies({ pageSize: 200, sort: 'name', desc: false });
  return [{ value: '', label: 'No company' }, ...(companies.data?.items ?? []).map((c) => ({ value: c.id, label: c.name }))];
}

export function DealFormDialog({ open, onClose, deal, onSaved }: { open: boolean; onClose: () => void; deal?: Deal; onSaved?: (deal: Deal) => void }) {
  const save = useSaveDeal(deal?.id);
  const toast = useToast();
  const owners = useOwnerOptions();
  const companies = useCompanyOptions();
  const [title, setTitle] = useState('');
  const [value, setValue] = useState('0');
  const [currency, setCurrency] = useState('USD');
  const [companyId, setCompanyId] = useState('');
  const [ownerId, setOwnerId] = useState('');
  const [source, setSource] = useState<DealSource>('Outbound');
  const [closeDate, setCloseDate] = useState('');
  const [services, setServices] = useState('');
  const [error, setError] = useState<unknown>(null);
  const currencies = useSupportedCurrencies(currency);

  useEffect(() => {
    if (!open) return;
    setTitle(deal?.title ?? '');
    setValue(String(deal?.value ?? 0));
    setCurrency(deal?.currency ?? 'USD');
    setCompanyId(deal?.companyId ?? '');
    setOwnerId(deal?.owner?.id ?? '');
    setSource(deal?.source ?? 'Outbound');
    setCloseDate(deal?.expectedCloseDate ?? '');
    setServices(deal?.serviceSlugs.join(', ') ?? '');
    setError(null);
  }, [open, deal]);

  return (
    <FormDialog
      open={open}
      onClose={onClose}
      title={deal ? 'Edit deal' : 'New deal'}
      submitLabel={deal ? 'Save deal' : 'Create deal'}
      canSubmit={title.trim().length > 0}
      onSubmit={async () => {
        try {
          const saved = await save.mutateAsync({
            title: title.trim(),
            value: Number(value) || 0,
            currency,
            companyId: companyId || null,
            primaryContactId: deal?.primaryContactId ?? null,
            ownerUserId: ownerId || null,
            source,
            sourceDetail: deal?.sourceDetail ?? null,
            budgetRange: deal?.budgetRange ?? null,
            expectedCloseDate: closeDate || null,
            serviceSlugs: splitTags(services),
            concurrencyStamp: deal?.concurrencyStamp,
          });
          toast.success(deal ? 'Deal saved' : 'Deal created');
          onSaved?.(saved);
        } catch (err) {
          setError(err);
          throw err;
        }
      }}
    >
      <FormField label="Title" required error={fieldError(error, 'title')}>
        <Input value={title} maxLength={200} onChange={(e) => setTitle(e.target.value)} />
      </FormField>
      <FormField label="Value" hint="Estimated contract value" error={fieldError(error, 'value')}>
        <Input type="number" min={0} step="any" value={value} onChange={(e) => setValue(e.target.value)} />
      </FormField>
      <FormField label="Currency">
        <Select value={currency} options={currencies.options} onChange={(e) => setCurrency(e.target.value)} />
      </FormField>
      <FormField label="Company">
        <Select value={companyId} options={companies} onChange={(e) => setCompanyId(e.target.value)} />
      </FormField>
      <FormField label="Owner">
        <Select value={ownerId} options={owners} onChange={(e) => setOwnerId(e.target.value)} />
      </FormField>
      <FormField label="Source">
        <Select value={source} options={SOURCE_OPTIONS} onChange={(e) => setSource(e.target.value as DealSource)} />
      </FormField>
      <FormField label="Expected close" optional>
        <Input type="date" value={closeDate} onChange={(e) => setCloseDate(e.target.value)} />
      </FormField>
      <FormField label="Services of interest" optional hint="Comma-separated service slugs, e.g. seo, paid-ads">
        <Input value={services} onChange={(e) => setServices(e.target.value)} />
      </FormField>
    </FormDialog>
  );
}

export function ContactFormDialog({
  open,
  onClose,
  contact,
  defaultCompanyId,
}: {
  open: boolean;
  onClose: () => void;
  contact?: Contact;
  defaultCompanyId?: string;
}) {
  const options = useCrmOptions();
  const save = useSaveContact(contact?.id);
  const toast = useToast();
  const owners = useOwnerOptions();
  const companies = useCompanyOptions();
  const [form, setForm] = useState({
    firstName: '',
    lastName: '',
    email: '',
    phone: '',
    jobTitle: '',
    companyId: '',
    ownerUserId: '',
    lifecycleStage: 'Lead' as LifecycleStage,
    consentStatus: 'Unknown' as ConsentStatus,
    tags: '',
    budgetRange: '',
  });
  const [error, setError] = useState<unknown>(null);
  useEffect(() => {
    if (!open) return;
    setForm({
      firstName: contact?.firstName ?? '',
      lastName: contact?.lastName ?? '',
      email: contact?.email ?? '',
      phone: contact?.phone ?? '',
      jobTitle: contact?.jobTitle ?? '',
      companyId: contact?.companyId ?? defaultCompanyId ?? '',
      ownerUserId: contact?.owner?.id ?? '',
      lifecycleStage: contact?.lifecycleStage ?? 'Lead',
      consentStatus: contact?.consentStatus ?? 'Unknown',
      tags: contact?.tags.join(', ') ?? '',
      budgetRange: contact?.budgetRange ?? '',
    });
    setError(null);
  }, [open, contact, defaultCompanyId]);
  const set = (key: keyof typeof form, value: string) => setForm((f) => ({ ...f, [key]: value }));

  return (
    <FormDialog
      open={open}
      onClose={onClose}
      title={contact ? 'Edit contact' : 'New contact'}
      submitLabel={contact ? 'Save contact' : 'Create contact'}
      canSubmit={form.firstName.trim().length > 0}
      onSubmit={async () => {
        try {
          await save.mutateAsync({
            firstName: form.firstName.trim(),
            lastName: form.lastName.trim() || null,
            email: form.email.trim() || null,
            phone: form.phone.trim() || null,
            jobTitle: form.jobTitle.trim() || null,
            companyId: form.companyId || null,
            ownerUserId: form.ownerUserId || null,
            lifecycleStage: form.lifecycleStage,
            consentStatus: form.consentStatus,
            tags: splitTags(form.tags),
            source: contact?.source ?? null,
            budgetRange: form.budgetRange.trim() || null,
            concurrencyStamp: contact?.concurrencyStamp,
          });
          toast.success(contact ? 'Contact saved' : 'Contact created');
        } catch (err) {
          setError(err);
          throw err;
        }
      }}
    >
      <FormField label="First name" required error={fieldError(error, 'firstName')}>
        <Input value={form.firstName} maxLength={100} onChange={(e) => set('firstName', e.target.value)} />
      </FormField>
      <FormField label="Last name" optional>
        <Input value={form.lastName} maxLength={100} onChange={(e) => set('lastName', e.target.value)} />
      </FormField>
      <FormField label="Email" optional error={fieldError(error, 'email')}>
        <Input type="email" value={form.email} maxLength={254} onChange={(e) => set('email', e.target.value)} />
      </FormField>
      <FormField label="Phone" optional>
        <Input type="tel" value={form.phone} maxLength={40} onChange={(e) => set('phone', e.target.value)} />
      </FormField>
      <FormField label="Job title" optional>
        <Input value={form.jobTitle} maxLength={120} onChange={(e) => set('jobTitle', e.target.value)} />
      </FormField>
      <FormField label="Company">
        <Select value={form.companyId} options={companies} onChange={(e) => set('companyId', e.target.value)} />
      </FormField>
      <FormField label="Lifecycle stage">
        <Select value={form.lifecycleStage} options={LIFECYCLE_OPTIONS} onChange={(e) => set('lifecycleStage', e.target.value)} />
      </FormField>
      <FormField label="Marketing consent">
        <Select value={form.consentStatus} options={CONSENT_OPTIONS} onChange={(e) => set('consentStatus', e.target.value)} />
      </FormField>
      <FormField label="Owner">
        <Select value={form.ownerUserId} options={owners} onChange={(e) => set('ownerUserId', e.target.value)} />
      </FormField>
      <FormField label="Budget range" optional hint="Choose a range; the list is edited under CRM settings.">
        <Input list="crm-budget-ranges" value={form.budgetRange} maxLength={60} onChange={(e) => set('budgetRange', e.target.value)} />
        <OptionList id="crm-budget-ranges" values={options.data?.budgetRanges} />
      </FormField>
      <FormField label="Tags" optional hint="Comma-separated">
        <Input value={form.tags} onChange={(e) => set('tags', e.target.value)} />
      </FormField>
    </FormDialog>
  );
}

const SIZE_OPTIONS: { value: CompanySize; label: string }[] = [
  { value: 'Unknown', label: 'Unknown' },
  { value: 'Solo', label: '1' },
  { value: 'Micro', label: '2–10' },
  { value: 'Small', label: '11–50' },
  { value: 'Medium', label: '51–200' },
  { value: 'Large', label: '201–1,000' },
  { value: 'Enterprise', label: '1,000+' },
];

export function CompanyFormDialog({ open, onClose, company }: { open: boolean; onClose: () => void; company?: Company }) {
  const options = useCrmOptions();
  const save = useSaveCompany(company?.id);
  const toast = useToast();
  const owners = useOwnerOptions();
  const [form, setForm] = useState({ name: '', domain: '', industry: '', size: 'Unknown' as CompanySize, countryCode: '', ownerUserId: '', tags: '', customFields: '{}' });
  const [error, setError] = useState<unknown>(null);
  useEffect(() => {
    if (!open) return;
    setForm({
      name: company?.name ?? '',
      domain: company?.domain ?? '',
      industry: company?.industry ?? '',
      size: company?.size ?? 'Unknown',
      countryCode: company?.countryCode ?? '',
      ownerUserId: company?.owner?.id ?? '',
      tags: company?.tags.join(', ') ?? '',
      customFields: company?.customFields ?? '{}',
    });
    setError(null);
  }, [open, company]);
  const set = (key: keyof typeof form, value: string) => setForm((f) => ({ ...f, [key]: value }));
  return (
    <FormDialog
      open={open}
      onClose={onClose}
      title={company ? 'Edit company' : 'New company'}
      submitLabel={company ? 'Save company' : 'Create company'}
      canSubmit={form.name.trim().length > 0}
      onSubmit={async () => {
        try {
          await save.mutateAsync({
            name: form.name.trim(),
            domain: form.domain.trim() || null,
            industry: form.industry.trim() || null,
            size: form.size,
            countryCode: form.countryCode.trim() || null,
            ownerUserId: form.ownerUserId || null,
            tags: splitTags(form.tags),
            customFields: form.customFields.trim() || '{}',
            concurrencyStamp: company?.concurrencyStamp,
          });
          toast.success(company ? 'Company saved' : 'Company created');
        } catch (err) {
          setError(err);
          throw err;
        }
      }}
    >
      <FormField label="Name" required>
        <Input value={form.name} maxLength={200} onChange={(e) => set('name', e.target.value)} />
      </FormField>
      <FormField label="Website or domain" optional error={fieldError(error, 'domain')}>
        <Input value={form.domain} maxLength={300} onChange={(e) => set('domain', e.target.value)} />
      </FormField>
      <FormField label="Industry" optional hint="Pick a suggestion or type your own.">
        <Input list="crm-industries" value={form.industry} maxLength={100} onChange={(e) => set('industry', e.target.value)} />
        <OptionList id="crm-industries" values={options.data?.industries} />
      </FormField>
      <FormField label="Company size">
        <Select value={form.size} options={SIZE_OPTIONS} onChange={(e) => set('size', e.target.value)} />
      </FormField>
      <FormField label="Country" optional hint="Two-letter code">
        <Input value={form.countryCode} maxLength={2} onChange={(e) => set('countryCode', e.target.value.toUpperCase())} />
      </FormField>
      <FormField label="Owner">
        <Select value={form.ownerUserId} options={owners} onChange={(e) => set('ownerUserId', e.target.value)} />
      </FormField>
      <FormField label="Tags" optional hint="Comma-separated">
        <Input value={form.tags} onChange={(e) => set('tags', e.target.value)} />
      </FormField>
      <FormField label="Custom fields (JSON)" optional hint='A flat object, e.g. {"employees": 120}' error={fieldError(error, 'customFields')}>
        <Textarea rows={3} value={form.customFields} onChange={(e) => set('customFields', e.target.value)} />
      </FormField>
    </FormDialog>
  );
}

/** Asks why a deal was lost before moving it to the Lost stage (the API requires a reason). */
const OTHER = '__other__';

/**
 * Asks why a deal was lost: one of the agency's configured reasons (CRM settings) plus optional detail, or a free-text
 * reason.
 */
export function LostReasonDialog({ open, dealTitle, onClose, onConfirm }: { open: boolean; dealTitle: string; onClose: () => void; onConfirm: (reason: string) => Promise<void> }) {
  const options = useCrmOptions();
  const reasons = options.data?.lostReasons ?? [];
  const [choice, setChoice] = useState('');
  const [detail, setDetail] = useState('');
  const [touched, setTouched] = useState(false);
  useEffect(() => {
    if (open) {
      setChoice('');
      setDetail('');
      setTouched(false);
    }
  }, [open]);
  const reason = choice === OTHER || choice === '' ? detail.trim() : detail.trim() ? `${choice}: ${detail.trim()}` : choice;
  const error = reason.length === 0 ? 'Say why the deal was lost.' : null;
  return (
    <FormDialog
      open={open}
      onClose={onClose}
      title={`Mark “${dealTitle}” as lost`}
      submitLabel="Mark as lost"
      tone="danger"
      onSubmit={async () => {
        setTouched(true);
        if (error) return false;
        await onConfirm(reason.slice(0, 500));
      }}
    >
      {reasons.length > 0 && (
        <FormField label="Reason" required error={touched && !choice && !detail.trim() ? error : null}>
          <Select
            value={choice}
            placeholder="Choose a reason"
            options={[...reasons.map((r) => ({ value: r, label: r })), { value: OTHER, label: 'Other (describe below)' }]}
            onChange={(e) => setChoice(e.target.value)}
          />
        </FormField>
      )}
      <FormField
        label={reasons.length > 0 ? 'Details' : 'Lost reason'}
        required={reasons.length === 0 || choice === OTHER}
        optional={reasons.length > 0 && choice !== OTHER}
        error={touched && (reasons.length === 0 || choice === OTHER) ? error : null}
      >
        <Textarea rows={3} maxLength={450} value={detail} onChange={(e) => setDetail(e.target.value)} />
      </FormField>
    </FormDialog>
  );
}
