import type { QueueFilters } from './api/reviewApi';

const STATUSES = new Set(['Pending', 'UnderReview']);
const FLAGGED = new Set(['true', 'false']);

/** Reads queue filters from the URL, dropping values the API would reject. */
export function parseQueueParams(params: URLSearchParams): QueueFilters {
  const status = params.get('status') ?? undefined;
  const minRisk = params.get('minRisk') ?? undefined;
  const flagged = params.get('flagged') ?? undefined;
  const page = Number(params.get('page'));
  return {
    status: status && STATUSES.has(status) ? status : undefined,
    campaignId: params.get('campaignId') || undefined,
    platform: params.get('platform') || undefined,
    minRisk: minRisk && /^\d{1,5}$/.test(minRisk) ? minRisk : undefined,
    flagged: flagged && FLAGGED.has(flagged) ? flagged : undefined,
    assignedToMe: params.get('mine') === '1',
    sort: params.get('sort') === 'risk' ? 'risk' : 'oldest',
    page: Number.isInteger(page) && page > 1 ? page : 1,
    pageSize: 25,
  };
}

/** Writes one filter change into the URL params; any filter change returns to page 1. */
export function withQueueParam(
  params: URLSearchParams,
  key: string,
  value: string | undefined,
): URLSearchParams {
  const next = new URLSearchParams(params);
  if (value === undefined || value === '') next.delete(key);
  else next.set(key, value);
  if (key !== 'page') next.delete('page');
  if (key === 'sort' && value === 'oldest') next.delete('sort');
  return next;
}
