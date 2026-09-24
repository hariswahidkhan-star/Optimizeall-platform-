import { Plus, Trash2 } from 'lucide-react';
import { Button, Checkbox, FormField, IconButton, Input, Select } from '@/components/ui';
import { useSupportedCurrencies } from '@/lib/api/meta';
import type { SocialPlatform } from '@/features/campaigns/api/types';
import type { ContentFormat, RateCardRatesInput, RateCardVersion } from '../api/types';
import { formatOptions, platformOptions } from '../labels';

export interface LineRow {
  key: string;
  platform: string;
  format: string;
  countryCode: string;
  amount: string;
  label: string;
}

export interface RatesForm {
  currency: string;
  dailyCap: string;
  weeklyCap: string;
  campaignCap: string;
  stackCampaignBonuses: boolean;
  lines: LineRow[];
}

let seq = 0;
const key = () => `line-${(seq += 1)}`;

export function newLine(): LineRow {
  return { key: key(), platform: '', format: '', countryCode: '', amount: '', label: '' };
}

export function emptyRates(currency = 'USD'): RatesForm {
  return {
    currency,
    dailyCap: '',
    weeklyCap: '',
    campaignCap: '',
    stackCampaignBonuses: true,
    lines: [newLine()],
  };
}

const str = (n: number | null | undefined) => (n === null || n === undefined ? '' : String(n));
const num = (s: string): number | null => {
  if (!s.trim()) return null;
  const n = Number(s);
  return Number.isFinite(n) ? n : null;
};

export function ratesFromVersion(v: RateCardVersion | undefined, fallbackCurrency = 'USD'): RatesForm {
  if (!v) return emptyRates(fallbackCurrency);
  return {
    currency: v.currency,
    dailyCap: str(v.dailyCapPerParticipant),
    weeklyCap: str(v.weeklyCapPerParticipant),
    campaignCap: str(v.campaignCapPerParticipant),
    stackCampaignBonuses: v.stackCampaignBonuses,
    lines: v.lines.map((l) => ({
      key: key(),
      platform: l.platform ?? '',
      format: l.format ?? '',
      countryCode: l.countryCode ?? '',
      amount: String(l.amount),
      label: l.label ?? '',
    })),
  };
}

export function ratesToInput(form: RatesForm): RateCardRatesInput {
  return {
    currency: form.currency,
    dailyCapPerParticipant: num(form.dailyCap),
    weeklyCapPerParticipant: num(form.weeklyCap),
    campaignCapPerParticipant: num(form.campaignCap),
    stackCampaignBonuses: form.stackCampaignBonuses,
    lines: form.lines.map((l) => ({
      platform: (l.platform || null) as SocialPlatform | null,
      format: (l.format || null) as ContentFormat | null,
      countryCode: l.countryCode.trim() ? l.countryCode.trim().toUpperCase() : null,
      amount: num(l.amount) ?? 0,
      label: l.label.trim() || null,
    })),
  };
}

/** Client hints mirroring `RateCardRules.Validate` (the server stays the authority). */
export function ratesHints(form: RatesForm): string[] {
  const hints: string[] = [];
  if (form.lines.length === 0) hints.push('Add at least one rate.');
  const seen = new Set<string>();
  form.lines.forEach((l, i) => {
    const k = `${l.platform}|${l.format}|${l.countryCode.trim().toUpperCase()}`;
    if (seen.has(k)) hints.push(`Rate ${i + 1} has the same platform, format and country as another rate.`);
    seen.add(k);
    if (l.amount.trim() === '' || Number(l.amount) < 0)
      hints.push(`Rate ${i + 1} needs an amount of 0 or more.`);
    if (l.countryCode.trim() && !/^[A-Za-z]{2}$/.test(l.countryCode.trim()))
      hints.push(`Rate ${i + 1}: use a two-letter country code.`);
  });
  return hints;
}

export interface RateLinesEditorProps {
  value: RatesForm;
  onChange: (next: RatesForm) => void;
  disabled?: boolean;
  /** Id prefix for the fields (several editors can be on one page). */
  idPrefix?: string;
}

/**
 * Editor for a rate card's rates: currency, flat fee per approved post per platform / format / country (the most
 * specific matching line wins), optional per-participant caps and whether campaign bonuses stack.
 */
export function RateLinesEditor({ value, onChange, disabled, idPrefix = 'rates' }: RateLinesEditorProps) {
  const set = (patch: Partial<RatesForm>) => onChange({ ...value, ...patch });
  const setLine = (k: string, patch: Partial<LineRow>) =>
    set({ lines: value.lines.map((l) => (l.key === k ? { ...l, ...patch } : l)) });
  const currencies = useSupportedCurrencies(value.currency).options;

  return (
    <div className="stack">
      <div className="rt-grid rt-grid--4">
        <FormField
          label="Currency"
          id={`${idPrefix}-currency`}
          hint="Converted to each campaign's currency when a post is priced."
        >
          <Select
            value={value.currency}
            options={currencies}
            disabled={disabled}
            onChange={(e) => set({ currency: e.target.value })}
          />
        </FormField>
        <FormField
          label="Daily cap per participant"
          optional
          id={`${idPrefix}-daily`}
          hint="Per campaign, on top of its caps"
        >
          <Input
            type="number"
            min={0}
            step="any"
            inputMode="decimal"
            value={value.dailyCap}
            disabled={disabled}
            onChange={(e) => set({ dailyCap: e.target.value })}
          />
        </FormField>
        <FormField label="Weekly cap per participant" optional id={`${idPrefix}-weekly`}>
          <Input
            type="number"
            min={0}
            step="any"
            inputMode="decimal"
            value={value.weeklyCap}
            disabled={disabled}
            onChange={(e) => set({ weeklyCap: e.target.value })}
          />
        </FormField>
        <FormField label="Campaign cap per participant" optional id={`${idPrefix}-campaign`}>
          <Input
            type="number"
            min={0}
            step="any"
            inputMode="decimal"
            value={value.campaignCap}
            disabled={disabled}
            onChange={(e) => set({ campaignCap: e.target.value })}
          />
        </FormField>
      </div>
      <Checkbox
        label="Campaign bonuses stack on these rates"
        description="First-post and time-limited bonuses are added on top. Untick for all-inclusive fees (quality bonuses stay possible)."
        checked={value.stackCampaignBonuses}
        disabled={disabled}
        onChange={(e) => set({ stackCampaignBonuses: e.target.checked })}
      />

      <fieldset className="rt-lines">
        <legend className="rt-legend">Rates per approved post</legend>
        <p className="text-small text-muted">
          Leave platform, format or country blank to match any. The most specific matching rate prices a post.
        </p>
        <ol className="rt-lines__list">
          {value.lines.map((line, index) => {
            const n = index + 1;
            return (
              <li key={line.key} className="rt-line">
                <FormField label={`Rate ${n} platform`} id={`${idPrefix}-l${n}-platform`}>
                  <Select
                    value={line.platform}
                    placeholder="Any platform"
                    options={platformOptions}
                    disabled={disabled}
                    onChange={(e) => setLine(line.key, { platform: e.target.value })}
                  />
                </FormField>
                <FormField label={`Rate ${n} format`} id={`${idPrefix}-l${n}-format`}>
                  <Select
                    value={line.format}
                    placeholder="Any format"
                    options={formatOptions}
                    disabled={disabled}
                    onChange={(e) => setLine(line.key, { format: e.target.value })}
                  />
                </FormField>
                <FormField label={`Rate ${n} country`} optional id={`${idPrefix}-l${n}-country`}>
                  <Input
                    value={line.countryCode}
                    maxLength={2}
                    placeholder="Any"
                    autoCapitalize="characters"
                    disabled={disabled}
                    onChange={(e) => setLine(line.key, { countryCode: e.target.value })}
                  />
                </FormField>
                <FormField label={`Rate ${n} amount`} required id={`${idPrefix}-l${n}-amount`}>
                  <Input
                    type="number"
                    min={0}
                    step="any"
                    inputMode="decimal"
                    value={line.amount}
                    disabled={disabled}
                    trailing={value.currency}
                    onChange={(e) => setLine(line.key, { amount: e.target.value })}
                  />
                </FormField>
                <FormField
                  label={`Rate ${n} label`}
                  optional
                  id={`${idPrefix}-l${n}-label`}
                  hint="Shown to the participant"
                >
                  <Input
                    value={line.label}
                    maxLength={150}
                    placeholder="Post reward"
                    disabled={disabled}
                    onChange={(e) => setLine(line.key, { label: e.target.value })}
                  />
                </FormField>
                <div className="rt-line__actions">
                  <IconButton
                    label={`Remove rate ${n}`}
                    icon={<Trash2 />}
                    variant="ghost"
                    disabled={disabled || value.lines.length === 1}
                    onClick={() => set({ lines: value.lines.filter((l) => l.key !== line.key) })}
                  />
                </div>
              </li>
            );
          })}
        </ol>
        <div>
          <Button
            variant="secondary"
            size="sm"
            leadingIcon={<Plus />}
            disabled={disabled || value.lines.length >= 100}
            onClick={() => set({ lines: [...value.lines, newLine()] })}
          >
            Add rate
          </Button>
        </div>
      </fieldset>
    </div>
  );
}
