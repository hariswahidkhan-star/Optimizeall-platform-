import { useCallback, useMemo } from 'react';
import { useSearchParams } from 'react-router-dom';

/**
 * List state (search, filters, page, sort) kept in the URL so a filtered list survives navigating to a detail page
 * and back, and can be shared as a link.
 */
export function useListParams(filterKeys: readonly string[], defaults: { pageSize?: number } = {}) {
  const [params, setParams] = useSearchParams();
  const keysKey = filterKeys.join('|');

  const state = useMemo(() => {
    const filters: Record<string, string | undefined> = {};
    for (const key of keysKey.split('|')) filters[key] = params.get(key) || undefined;
    return {
      search: params.get('search') ?? '',
      page: Math.max(1, Number(params.get('page')) || 1),
      pageSize: Number(params.get('pageSize')) || defaults.pageSize || 25,
      sort: params.get('sort') || undefined,
      desc: params.get('desc') === 'true',
      filters,
    };
  }, [params, keysKey, defaults.pageSize]);

  const update = useCallback(
    (changes: Record<string, string | number | boolean | undefined>, resetPage = true) => {
      setParams(
        (current) => {
          const next = new URLSearchParams(current);
          for (const [key, value] of Object.entries(changes)) {
            if (value === undefined || value === '' || value === false) next.delete(key);
            else next.set(key, String(value));
          }
          if (resetPage && !('page' in changes)) next.delete('page');
          return next;
        },
        { replace: true },
      );
    },
    [setParams],
  );

  const reset = useCallback(() => setParams(new URLSearchParams(), { replace: true }), [setParams]);

  return { ...state, update, reset };
}
