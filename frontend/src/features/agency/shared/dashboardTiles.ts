import type { ComponentType } from 'react';
import type { PermissionRequirement } from '@/lib/auth/permissions';

/**
 * Agency home dashboard tile registry. The delivery area owns the dashboard (`/agency`); other areas (CRM pipeline,
 * accounts receivable, SEO, ads…) contribute KPI tiles by importing this module and calling `registerDashboardTile`
 * from their `routes.tsx` (module scope, so it runs when the agency portal loads):
 *
 * ```ts
 * import { registerDashboardTile } from '@/features/agency/shared/dashboardTiles';
 * registerDashboardTile({ id: 'crm.pipeline', title: 'Pipeline', order: 20, requires: { anyOf: [Permissions.CrmView] }, Component: PipelineTile });
 * ```
 *
 * A tile renders its own content (fetch with React Query; show a Skeleton while loading, an inline error on failure) and
 * is only shown to users who meet `requires`. Tiles render inside a Card with `title` as its heading.
 */
export interface DashboardTile {
  /** Unique id, conventionally "<area>.<name>". Registering the same id again replaces the tile (hot reload safe). */
  id: string;
  title: string;
  /** Lower comes first; delivery's own sections come before registered tiles. */
  order?: number;
  requires?: PermissionRequirement;
  Component: ComponentType;
}

const tiles = new Map<string, DashboardTile>();
const listeners = new Set<() => void>();

export function registerDashboardTile(tile: DashboardTile): void {
  tiles.set(tile.id, tile);
  listeners.forEach((l) => l());
}

export function unregisterDashboardTile(id: string): void {
  if (tiles.delete(id)) listeners.forEach((l) => l());
}

/** Registered tiles in display order. */
export function dashboardTiles(): DashboardTile[] {
  return [...tiles.values()].sort((a, b) => (a.order ?? 100) - (b.order ?? 100) || a.title.localeCompare(b.title));
}

/** Subscribe to registry changes (used by the dashboard with useSyncExternalStore). */
export function subscribeDashboardTiles(listener: () => void): () => void {
  listeners.add(listener);
  return () => {
    listeners.delete(listener);
  };
}

let snapshot: DashboardTile[] = [];
let version = -1;
let current = 0;
subscribeDashboardTiles(() => {
  current++;
});

/** Stable snapshot for useSyncExternalStore. */
export function dashboardTilesSnapshot(): DashboardTile[] {
  if (version !== current) {
    snapshot = dashboardTiles();
    version = current;
  }
  return snapshot;
}
