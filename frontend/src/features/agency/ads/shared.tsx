import { Badge, FormField, Select, type Tone } from '@/components/ui';
import { formatMoney, formatNumber } from '@/lib/format/money';
import type { Kpis } from './api';
import { useAdsClients } from './api';

export function money(amount: number | null | undefined, currency: string): string {
  return amount == null ? '—' : formatMoney(amount, currency);
}

export function ratio(value: number | null | undefined, digits = 2): string {
  return value == null ? '—' : `${(value * 100).toFixed(digits)}%`;
}

export function times(value: number | null | undefined): string {
  return value == null ? '—' : `${value.toFixed(2)}×`;
}

export function count(value: number | null | undefined): string {
  return value == null ? '—' : formatNumber(value, { maximumFractionDigits: 1 });
}

/** KPI cells; empty (—) when the denominator is zero, never a fake 0. */
export function kpiCells(k: Kpis, currency: string) {
  return {
    ctr: ratio(k.ctr),
    cpc: money(k.cpc, currency),
    cpm: money(k.cpm, currency),
    cpa: money(k.cpa, currency),
    roas: times(k.roas),
    cvr: ratio(k.conversionRate),
  };
}

export function SourceBadge({ label }: { label: string }) {
  const tone: Tone = label === 'Measured' ? 'success' : label === 'Manual' ? 'warning' : 'neutral';
  return (
    <Badge tone={tone} size="sm" title="Measured = platform API or a platform export (CSV import); Manual = typed in">
      {label}
    </Badge>
  );
}

export function AdsClientPicker({
  value,
  onChange,
  allowAll = false,
}: {
  value: string | undefined;
  onChange: (id: string | undefined) => void;
  allowAll?: boolean;
}) {
  const clients = useAdsClients();
  return (
    <FormField label="Client" className="ad-client-picker">
      <Select
        value={value ?? ''}
        onChange={(e) => onChange(e.target.value || undefined)}
        placeholder={allowAll ? 'All clients' : 'Choose a client'}
        options={(clients.data ?? []).map((c) => ({ value: c.id, label: c.name }))}
      />
    </FormField>
  );
}

export const iso = (d: Date) => d.toISOString().slice(0, 10);
export const daysAgo = (n: number) => iso(new Date(Date.now() - n * 86_400_000));
