import { useQuery } from '@tanstack/react-query';
import { useCallback, useMemo } from 'react';
import { useSearchParams } from 'react-router-dom';
import type { MyOrganization } from '@/features/agency/shared/deliveryTypes';
import { api } from '@/lib/api/client';
import { safeStorage } from '@/lib/hooks/storage';

export const ORG_STORAGE_KEY = 'oa.clientOrg';

export const clientKeys = {
  orgs: ['client', 'orgs'] as const,
  part: (orgId: string, part: string, id?: string) => ['client', orgId, part, id] as const,
};

/**
 * The organization the client user is looking at. Selected with `?org=<clientId>` (links from notifications carry it),
 * falling back to the last choice (localStorage) and then the first organization. The API re-checks membership on every
 * request, so this is only a UI preference.
 */
export function useClientOrg() {
  const [params, setParams] = useSearchParams();
  const orgs = useQuery({
    queryKey: clientKeys.orgs,
    queryFn: ({ signal }) => api.get<MyOrganization[]>('/client/orgs', { signal }),
    staleTime: 5 * 60_000,
  });
  const requested = params.get('org') ?? safeStorage.get(ORG_STORAGE_KEY);
  const org = useMemo(
    () => orgs.data?.find((o) => o.clientId === requested) ?? orgs.data?.[0] ?? null,
    [orgs.data, requested],
  );

  const setOrg = useCallback(
    (clientId: string) => {
      safeStorage.set(ORG_STORAGE_KEY, clientId);
      setParams(
        (p) => {
          const next = new URLSearchParams(p);
          next.set('org', clientId);
          next.delete('thread');
          return next;
        },
        { replace: true },
      );
    },
    [setParams],
  );

  /** A client-portal path that keeps the selected organization. */
  const link = useCallback(
    (path: string) => (org ? `${path}${path.includes('?') ? '&' : '?'}org=${org.clientId}` : path),
    [org],
  );

  return {
    orgs: orgs.data ?? [],
    org,
    base: org ? `/client/orgs/${org.clientId}` : '',
    setOrg,
    link,
    isPending: orgs.isPending,
    error: orgs.error,
    canApprove: org?.role === 'Owner' || org?.role === 'Approver',
    isOwner: org?.role === 'Owner',
  };
}
