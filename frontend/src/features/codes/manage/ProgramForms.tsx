import { Plus, Trash2 } from 'lucide-react';
import {
  Button,
  Checkbox,
  FormField,
  IconButton,
  Input,
  RadioGroup,
  Select,
  Textarea,
} from '@/components/ui';
import type { FieldErrors } from '@/lib/api/errors';
import { useCampaignOptions } from '@/lib/api/campaignOptions';
import { useSupportedCurrencies } from '@/lib/api/meta';
import type { CodePayoutType, CodeProgram } from '../api/types';

// ------------------------------------------------------------------ program details

export interface ProgramDetailsForm {
  name: string;
  brandName: string;
  description: string;
  terms: string;
  storeUrl: string;
  discountLabel: string;
  currency: string;
  startsAt: string; // yyyy-mm-dd
  endsAt: string;
  maxOrderAgeDays: string;
  requireProof: boolean;
  campaignId: string;
}

const today = () => new Date().toISOString().slice(0, 10);

export const emptyProgramDetails = (): ProgramDetailsForm => ({
  name: '',
  brandName: '',
  description: '',
  terms: '',
  storeUrl: '',
  discountLabel: '',
  currency: 'USD',
  startsAt: today(),
  endsAt: '',
  maxOrderAgeDays: '60',
  requireProof: false,
  campaignId: '',
});

export const programDetailsFrom = (p: CodeProgram): ProgramDetailsForm => ({
  name: p.name,
  brandName: p.brandName,
  description: p.description ?? '',
  terms: p.terms ?? '',
  storeUrl: p.storeUrl ?? '',
  discountLabel: p.discountLabel ?? '',
  currency: p.currency,
  startsAt: p.startsAt.slice(0, 10),
  endsAt: p.endsAt ? p.endsAt.slice(0, 10) : '',
  maxOrderAgeDays: String(p.maxOrderAgeDays),
  requireProof: p.requireProof,
  campaignId: p.campaign?.id ?? '',
});

/** Dates are whole days: the program runs from the start of `startsAt` to the end of `endsAt` (UTC). */
export const programDetailsToInput = (f: ProgramDetailsForm) => ({
  name: f.name.trim(),
  brandName: f.brandName.trim(),
  description: f.description.trim() || null,
  terms: f.terms.trim() || null,
  storeUrl: f.storeUrl.trim() || null,
  discountLabel: f.discountLabel.trim() || null,
  currency: f.currency,
  startsAt: `${f.startsAt}T00:00:00Z`,
  endsAt: f.endsAt ? `${f.endsAt}T23:59:59Z` : null,
  maxOrderAgeDays: Number(f.maxOrderAgeDays) || 60,
  requireProof: f.requireProof,
  campaignId: f.campaignId || null,
});

const err = (errors: FieldErrors | undefined, key: string) =>
  errors?.[key]?.[0] ?? errors?.[key.charAt(0).toUpperCase() + key.slice(1)]?.[0];

export function ProgramDetailsFields({
  value,
  onChange,
  errors,
  currencyLocked,
}: {
  value: ProgramDetailsForm;
  onChange: (v: ProgramDetailsForm) => void;
  errors?: FieldErrors;
  currencyLocked?: boolean;
}) {
  const set = <K extends keyof ProgramDetailsForm>(key: K, v: ProgramDetailsForm[K]) =>
    onChange({ ...value, [key]: v });
  const currencies = useSupportedCurrencies(value.currency);
  const campaigns = useCampaignOptions('');
  return (
    <div className="stack">
      <div className="dc-form-grid">
        <FormField label="Brand / company" required error={err(errors, 'brandName')}>
          <Input value={value.brandName} maxLength={120} onChange={(e) => set('brandName', e.target.value)} />
        </FormField>
        <FormField label="Program name" required error={err(errors, 'name')}>
          <Input value={value.name} maxLength={120} onChange={(e) => set('name', e.target.value)} />
        </FormField>
      </div>
      <div className="dc-form-grid">
        <FormField
          label="Store / landing page"
          optional
          error={err(errors, 'storeUrl')}
          hint="Share links point here with the code."
        >
          <Input
            type="url"
            value={value.storeUrl}
            placeholder="https://"
            onChange={(e) => set('storeUrl', e.target.value)}
          />
        </FormField>
        <FormField label="What customers get" optional hint="e.g. 15% off the summer collection">
          <Input
            value={value.discountLabel}
            maxLength={120}
            onChange={(e) => set('discountLabel', e.target.value)}
          />
        </FormField>
      </div>
      <div className="dc-form-grid">
        <FormField
          label="Currency"
          required
          error={err(errors, 'currency')}
          hint={currencyLocked ? 'Locked: the program has sales.' : 'Commissions, caps and budget.'}
        >
          <Select
            value={value.currency}
            disabled={currencyLocked}
            onChange={(e) => set('currency', e.target.value)}
            options={currencies.options}
          />
        </FormField>
        <FormField label="Starts" required error={err(errors, 'startsAt')}>
          <Input type="date" value={value.startsAt} onChange={(e) => set('startsAt', e.target.value)} />
        </FormField>
        <FormField label="Ends" optional error={err(errors, 'endsAt')}>
          <Input type="date" value={value.endsAt} onChange={(e) => set('endsAt', e.target.value)} />
        </FormField>
        <FormField label="Report orders within (days)" required error={err(errors, 'maxOrderAgeDays')}>
          <Input
            type="number"
            min={1}
            max={365}
            value={value.maxOrderAgeDays}
            onChange={(e) => set('maxOrderAgeDays', e.target.value)}
          />
        </FormField>
      </div>
      <FormField
        label="Linked campaign"
        optional
        hint="Optional: its tracking links count clicks for conversion reporting."
      >
        <Select
          value={value.campaignId}
          onChange={(e) => set('campaignId', e.target.value)}
          placeholder="No campaign"
          options={(campaigns.data ?? []).map((c) => ({ value: c.id, label: c.title }))}
        />
      </FormField>
      <FormField label="Description" optional>
        <Textarea
          rows={2}
          maxLength={2000}
          value={value.description}
          onChange={(e) => set('description', e.target.value)}
        />
      </FormField>
      <FormField
        label="Terms for participants"
        optional
        hint="Shown with the code: where it may be shared, self-purchases, reporting deadline…"
      >
        <Textarea
          rows={3}
          maxLength={4000}
          value={value.terms}
          onChange={(e) => set('terms', e.target.value)}
        />
      </FormField>
      <Checkbox
        label="Require a screenshot of the order or receipt with every reported sale"
        checked={value.requireProof}
        onChange={(e) => set('requireProof', e.target.checked)}
      />
    </div>
  );
}

// ------------------------------------------------------------------ payout rules

export interface TierForm {
  thresholdSales: string;
  rateType: '' | 'flat' | 'percent';
  rate: string;
  bonus: string;
}

export interface PayoutForm {
  payoutType: CodePayoutType;
  flatAmount: string;
  percent: string;
  tiers: TierForm[];
  dailyCap: string;
  programCap: string;
  budget: string;
}

export const emptyPayout = (): PayoutForm => ({
  payoutType: 'PercentOfNet',
  flatAmount: '',
  percent: '10',
  tiers: [],
  dailyCap: '',
  programCap: '',
  budget: '',
});

const str = (n: number | null) => (n === null ? '' : String(n));

export const payoutFrom = (p: CodeProgram): PayoutForm => ({
  payoutType: p.payoutType,
  flatAmount: str(p.flatAmount),
  percent: str(p.percent),
  tiers: p.tiers.map((t) => ({
    thresholdSales: String(t.thresholdSales),
    rateType: t.flatAmount !== null ? 'flat' : t.percent !== null ? 'percent' : '',
    rate: str(t.flatAmount ?? t.percent),
    bonus: str(t.bonusAmount),
  })),
  dailyCap: str(p.dailyCapPerPerson),
  programCap: str(p.programCapPerPerson),
  budget: str(p.budgetAmount),
});

const num = (s: string) => (s.trim() === '' ? null : Number(s));

export const payoutToInput = (f: PayoutForm) => ({
  payoutType: f.payoutType,
  flatAmount: f.payoutType === 'FlatPerSale' ? num(f.flatAmount) : null,
  percent: f.payoutType === 'PercentOfNet' ? num(f.percent) : null,
  tiers: f.tiers.map((t) => ({
    thresholdSales: Number(t.thresholdSales),
    flatAmount: t.rateType === 'flat' ? num(t.rate) : null,
    percent: t.rateType === 'percent' ? num(t.rate) : null,
    bonusAmount: num(t.bonus),
  })),
  dailyCapPerPerson: num(f.dailyCap),
  programCapPerPerson: num(f.programCap),
  budgetAmount: num(f.budget),
});

export function PayoutFields({
  value,
  onChange,
  currency,
  errors,
}: {
  value: PayoutForm;
  onChange: (v: PayoutForm) => void;
  currency: string;
  errors?: FieldErrors;
}) {
  const set = <K extends keyof PayoutForm>(key: K, v: PayoutForm[K]) => onChange({ ...value, [key]: v });
  const setTier = (i: number, patch: Partial<TierForm>) =>
    set(
      'tiers',
      value.tiers.map((t, j) => (j === i ? { ...t, ...patch } : t)),
    );
  return (
    <div className="stack">
      <RadioGroup
        legend="Commission per approved sale"
        value={value.payoutType}
        variant="cards"
        orientation="horizontal"
        onChange={(v) => set('payoutType', v as CodePayoutType)}
        options={[
          {
            value: 'PercentOfNet',
            label: 'Percentage of the order value',
            description: 'Net of the discount.',
          },
          { value: 'FlatPerSale', label: 'Flat amount per sale' },
        ]}
      />
      {value.payoutType === 'PercentOfNet' ? (
        <FormField label="Percent" required error={err(errors, 'percent')}>
          <Input
            type="number"
            min={0}
            max={100}
            step="any"
            value={value.percent}
            onChange={(e) => set('percent', e.target.value)}
          />
        </FormField>
      ) : (
        <FormField label={`Amount (${currency})`} required error={err(errors, 'flatAmount')}>
          <Input
            type="number"
            min={0}
            step="any"
            value={value.flatAmount}
            onChange={(e) => set('flatAmount', e.target.value)}
          />
        </FormField>
      )}
      <fieldset className="stack" style={{ border: 0, padding: 0, margin: 0 }}>
        <legend className="dc-strong">Tiers</legend>
        <p className="text-small text-muted" style={{ margin: 0 }}>
          Reward top sellers: from a number of approved sales on, pay a new rate per further sale and/or a
          one-off bonus.
        </p>
        {err(errors, 'tiers') && (
          <p className="text-small" style={{ color: 'var(--danger-fg)', margin: 0 }}>
            {err(errors, 'tiers')}
          </p>
        )}
        {value.tiers.map((t, i) => (
          <div key={i} className="dc-tier-row">
            <FormField
              label="After (approved sales)"
              error={err(errors, `tiers[${i}].thresholdSales`) ?? err(errors, `tiers[${i}]`)}
            >
              <Input
                type="number"
                min={1}
                value={t.thresholdSales}
                onChange={(e) => setTier(i, { thresholdSales: e.target.value })}
              />
            </FormField>
            <FormField label="New rate">
              <Select
                value={t.rateType}
                onChange={(e) => setTier(i, { rateType: e.target.value as TierForm['rateType'] })}
                options={[
                  { value: '', label: 'Keep the rate' },
                  { value: 'percent', label: 'Percent of net' },
                  { value: 'flat', label: `Flat (${currency})` },
                ]}
              />
            </FormField>
            {t.rateType && (
              <FormField label={t.rateType === 'percent' ? 'Percent' : `Amount (${currency})`}>
                <Input
                  type="number"
                  min={0}
                  step="any"
                  value={t.rate}
                  onChange={(e) => setTier(i, { rate: e.target.value })}
                />
              </FormField>
            )}
            <FormField label={`One-off bonus (${currency})`} optional>
              <Input
                type="number"
                min={0}
                step="any"
                value={t.bonus}
                onChange={(e) => setTier(i, { bonus: e.target.value })}
              />
            </FormField>
            <IconButton
              label={`Remove tier ${i + 1}`}
              icon={<Trash2 />}
              variant="ghost"
              onClick={() =>
                set(
                  'tiers',
                  value.tiers.filter((_, j) => j !== i),
                )
              }
            />
          </div>
        ))}
        <div>
          <Button
            variant="secondary"
            size="sm"
            leadingIcon={<Plus />}
            onClick={() =>
              set('tiers', [...value.tiers, { thresholdSales: '', rateType: '', rate: '', bonus: '' }])
            }
          >
            Add tier
          </Button>
        </div>
      </fieldset>
      <div className="dc-form-grid">
        <FormField
          label={`Daily cap per person (${currency})`}
          optional
          error={err(errors, 'dailyCapPerPerson')}
        >
          <Input
            type="number"
            min={0}
            step="any"
            value={value.dailyCap}
            onChange={(e) => set('dailyCap', e.target.value)}
          />
        </FormField>
        <FormField label={`Cap per person (${currency})`} optional error={err(errors, 'programCapPerPerson')}>
          <Input
            type="number"
            min={0}
            step="any"
            value={value.programCap}
            onChange={(e) => set('programCap', e.target.value)}
          />
        </FormField>
        <FormField label={`Program budget (${currency})`} optional error={err(errors, 'budgetAmount')}>
          <Input
            type="number"
            min={0}
            step="any"
            value={value.budget}
            onChange={(e) => set('budget', e.target.value)}
          />
        </FormField>
      </div>
    </div>
  );
}
