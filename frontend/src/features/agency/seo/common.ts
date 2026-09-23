import { useQuery } from '@tanstack/react-query';
import { api } from '@/lib/api/client';
import { isApiError } from '@/lib/api/errors';

/** A client organization the caller may work on (`GET …/client-options`). */
export interface ClientOption {
  id: string;
  name: string;
  slug: string;
}

/** Client picker data for an agency area (seo | pages | integrations share the same shape). */
export function useClientOptions(area: 'seo' | 'pages' | 'integrations') {
  return useQuery({
    queryKey: ['agency', area, 'client-options'],
    queryFn: () => api.get<ClientOption[]>(`/agency/${area}/client-options`),
    staleTime: 5 * 60_000,
  });
}

/** Field errors of a failed request keyed by lower-cased path (e.g. `domain`, `variants[0].blocks[1].props.headline`). */
export function fieldErrors(error: unknown): Record<string, string[]> {
  if (!isApiError(error)) return {};
  const result: Record<string, string[]> = {};
  for (const [key, messages] of Object.entries(error.errors ?? {})) {
    const k = key.replace(/^\$\.?/, '').toLowerCase();
    result[k] = [...(result[k] ?? []), ...messages];
  }
  return result;
}

export function firstError(errors: Record<string, string[]>, key: string): string | undefined {
  return errors[key.toLowerCase()]?.[0];
}

const numberFormat = new Intl.NumberFormat('en', { maximumFractionDigits: 1 });
const percentFormat = new Intl.NumberFormat('en', { style: 'percent', maximumFractionDigits: 1 });

export const fmt = {
  number: (value: number | null | undefined) => (value === null || value === undefined ? '—' : numberFormat.format(value)),
  percent: (ratio: number | null | undefined) => (ratio === null || ratio === undefined ? '—' : percentFormat.format(ratio)),
  position: (value: number | null | undefined) => (value === null || value === undefined ? 'Not ranking' : `#${value}`),
};

/** Splits a textarea into trimmed, non-empty lines. */
export function lines(value: string): string[] {
  return value
    .split(/\r?\n/)
    .map((l) => l.trim())
    .filter(Boolean);
}

/** Health-score tone: 80+ good, 50–79 needs work, below 50 poor. */
export function healthTone(score: number | null | undefined): 'success' | 'warning' | 'danger' | 'neutral' {
  if (score === null || score === undefined) return 'neutral';
  if (score >= 80) return 'success';
  if (score >= 50) return 'warning';
  return 'danger';
}
