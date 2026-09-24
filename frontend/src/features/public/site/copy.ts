import { useQuery } from '@tanstack/react-query';
import { useMemo } from 'react';
import { api } from '@/lib/api/client';
import catalog from './siteCopy.json';

/**
 * Editable page copy (CMS). Every marketing text on the public site and the portal home reads its words through
 * {@link useSiteCopy}: the shipped defaults live in `siteCopy.json` (identical to the backend catalog), and the API
 * returns only the keys an editor has overridden in the admin panel (Website → Page copy, Admin → Content → Portal copy).
 * Pages render the defaults immediately and swap in overrides once they load, so nothing flashes or breaks offline.
 */

export type CopyType = 'text' | 'textarea' | 'list' | 'pairs';

export interface CopyCatalogEntry {
  key: string;
  label: string;
  type: CopyType;
  default: string;
  placeholders?: string[];
}

export interface CopyCatalogGroup {
  id: string;
  label: string;
  scope: 'Website' | 'Portal';
  entries: CopyCatalogEntry[];
}

export const COPY_GROUPS = catalog.groups as CopyCatalogGroup[];

export const COPY_DEFAULTS: Readonly<Record<string, string>> = Object.fromEntries(
  COPY_GROUPS.flatMap((g) => g.entries.map((e) => [e.key, e.default])),
);

export interface PublicCopy {
  values: Record<string, string>;
  updatedAt: string | null;
}

export interface CopyPair {
  title: string;
  text: string;
}

export interface SiteCopy {
  /** The text for a key, with `{placeholder}` values filled in. */
  text: (key: string, vars?: Record<string, string | number>) => string;
  /** A list key as items (one per line). */
  list: (key: string) => string[];
  /** A pairs key as `{ title, text }` items (one per line, `Title | Text`). */
  pairs: (key: string) => CopyPair[];
}

export const COPY_QUERY_KEY = ['content', 'copy'] as const;

function fill(value: string, vars?: Record<string, string | number>): string {
  if (!vars) return value;
  return value.replace(/\{([A-Za-z][A-Za-z0-9]*)\}/g, (match, name: string) => (name in vars ? String(vars[name]) : match));
}

export function splitLines(value: string): string[] {
  return value
    .split('\n')
    .map((l) => l.trim())
    .filter(Boolean);
}

export function splitPairs(value: string): CopyPair[] {
  return splitLines(value).map((line) => {
    const i = line.indexOf('|');
    return i < 0 ? { title: line, text: '' } : { title: line.slice(0, i).trim(), text: line.slice(i + 1).trim() };
  });
}

/** Builds the accessor over a set of overrides (pure; exported for tests and the editor preview). */
export function makeSiteCopy(overrides: Record<string, string>): SiteCopy {
  const raw = (key: string) => {
    const value = overrides[key] ?? COPY_DEFAULTS[key];
    if (value === undefined) {
      if (import.meta.env.DEV) console.warn(`Unknown site copy key "${key}"`);
      return '';
    }
    return value;
  };
  return {
    text: (key, vars) => fill(raw(key), vars),
    list: (key) => splitLines(raw(key)),
    pairs: (key) => splitPairs(raw(key)),
  };
}

const DEFAULT_COPY = makeSiteCopy({});

/** Page copy with editor overrides applied (defaults until the overrides load, or if they cannot be loaded). */
export function useSiteCopy(): SiteCopy {
  const query = useQuery({
    queryKey: COPY_QUERY_KEY,
    queryFn: ({ signal }) => api.get<PublicCopy>('/content/copy', { signal }),
    staleTime: 5 * 60_000,
    retry: false,
  });
  const values = query.data?.values;
  return useMemo(() => (values && Object.keys(values).length > 0 ? makeSiteCopy(values) : DEFAULT_COPY), [values]);
}
