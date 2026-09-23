import { useQuery } from '@tanstack/react-query';
import { useMemo } from 'react';
import type { SelectOption } from '@/components/ui/Select';
import { api } from './client';

/** `GET /meta/currencies` — `Money.SupportedCurrencies` with their minor-unit digits. */
export interface SupportedCurrency {
  code: string;
  minorUnits: number;
}

/** `GET /meta/eligibility-defaults` — platform-wide social-profile minimums (admin settings). */
export interface EligibilityDefaults {
  minAccountAgeDays: number;
  minFollowers: number;
}

export const metaKeys = {
  currencies: () => ['meta', 'currencies'] as const,
  eligibilityDefaults: () => ['meta', 'eligibility-defaults'] as const,
};

export const metaApi = {
  currencies: (signal?: AbortSignal) => api.get<SupportedCurrency[]>('/meta/currencies', { signal }),
  eligibilityDefaults: (signal?: AbortSignal) =>
    api.get<EligibilityDefaults>('/meta/eligibility-defaults', { signal }),
};

/**
 * Currencies the platform accepts, from the API (the single source of truth — never hard-code the list).
 * `options` always contains `current` (e.g. a saved value) so a select never silently drops it while loading
 * or if the list changes.
 */
export function useSupportedCurrencies(current?: string | null) {
  const query = useQuery({
    queryKey: metaKeys.currencies(),
    queryFn: ({ signal }) => metaApi.currencies(signal),
    staleTime: Infinity,
    gcTime: Infinity,
  });
  const data = query.data;
  const options = useMemo<SelectOption[]>(() => {
    const codes = (data ?? []).map((c) => c.code);
    if (current && !codes.includes(current)) codes.unshift(current);
    return codes.map((code) => ({ value: code, label: code }));
  }, [data, current]);
  return {
    currencies: data ?? [],
    options,
    isPending: query.isPending,
    isError: query.isError,
    error: query.error,
    refetch: query.refetch,
  };
}

/** Platform eligibility minimums a new campaign starts from (null while loading). */
export function useEligibilityDefaults(enabled = true) {
  return useQuery({
    queryKey: metaKeys.eligibilityDefaults(),
    queryFn: ({ signal }) => metaApi.eligibilityDefaults(signal),
    staleTime: 5 * 60_000,
    enabled,
  });
}
