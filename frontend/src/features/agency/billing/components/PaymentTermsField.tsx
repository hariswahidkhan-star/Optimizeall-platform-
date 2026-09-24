import { FormField, Select } from '@/components/ui';
import { useBillingSettings } from '../api/hooks';

/**
 * Payment terms picker. The choices are the agency's configured payment terms (Billing settings); an empty value means
 * "use the default" and a value that is no longer configured stays selectable so editing never silently changes it.
 */
export function PaymentTermsField({ value, onChange, label = 'Payment terms' }: { value: string; onChange: (value: string) => void; label?: string }) {
  const settings = useBillingSettings();
  const configured = settings.data?.paymentTermsOptions ?? [0, 7, 14, 30, 45, 60];
  const defaultDays = settings.data?.paymentTermsDays;
  const days = Array.from(new Set([...configured, ...(value !== '' ? [Number(value)] : [])])).sort((a, b) => a - b);
  const describe = (d: number) => (d === 0 ? 'Due on receipt' : `Net ${d} (${d} days)`);
  return (
    <FormField label={label} hint="Choices are edited under Billing settings.">
      <Select
        value={value}
        options={[
          { value: '', label: defaultDays !== undefined ? `Default: ${describe(defaultDays)}` : 'Default' },
          ...days.map((d) => ({ value: String(d), label: describe(d) })),
        ]}
        onChange={(e) => onChange(e.target.value)}
      />
    </FormField>
  );
}
