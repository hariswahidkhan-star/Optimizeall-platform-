import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { RotateCcw, ScrollText, Search } from 'lucide-react';
import { useEffect, useId, useState, type ComponentProps, type FormEvent } from 'react';
import { Button } from '@/components/ui/Button';
import { Card, CardBody } from '@/components/ui/Card';
import { EmptyState } from '@/components/ui/EmptyState';
import { FormField } from '@/components/ui/FormField';
import { Input } from '@/components/ui/Input';
import { PageHeader } from '@/components/ui/PageHeader';
import { Pagination } from '@/components/ui/Pagination';
import { SkeletonText } from '@/components/ui/Skeleton';
import { api } from '@/lib/api/client';
import type { PagedResult } from '@/lib/api/types';
import type { AuditLogEntry } from '../api/types';
import { dateInputToIso, ExportCsvButton, QueryError } from '../shared/common';
import { mapFieldErrors } from '../shared/errors';
import { useListParams } from '../shared/useListParams';
import { AuditEntry } from './AuditEntry';

const FILTER_KEYS = ['action', 'entityType', 'entityId', 'actorUserId', 'from', 'to'] as const;
type FilterKey = (typeof FILTER_KEYS)[number];

const ACTION_SUGGESTIONS = [
  'admin.',
  'admin.user_suspended',
  'admin.user_reactivated',
  'admin.user_roles_changed',
  'admin.user_tier_changed',
  'admin.staff_created',
  'admin.setting_changed',
  'admin.job_run_requested',
  'content.',
  'support.',
  'notification.delivery_retried',
  'auth.',
  'payout.',
  'submission.',
];

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export function AuditLogPage() {
  const list = useListParams(FILTER_KEYS, { pageSize: 25 });
  const listId = useId();
  const [draft, setDraft] = useState<Record<FilterKey, string>>(() => blank(list.filters));
  const [errors, setErrors] = useState<Partial<Record<FilterKey, string>>>({});

  const filterKey = JSON.stringify(list.filters);
  useEffect(() => {
    setDraft(blank(JSON.parse(filterKey) as Record<string, string | undefined>));
  }, [filterKey]);

  const apiQuery = {
    search: list.search,
    action: list.filters.action,
    entityType: list.filters.entityType,
    entityId: list.filters.entityId,
    actorUserId: list.filters.actorUserId,
    from: dateInputToIso(list.filters.from ?? ''),
    to: dateInputToIso(list.filters.to ?? '', true),
  };
  const query = { ...apiQuery, page: list.page, pageSize: list.pageSize };

  const audit = useQuery({
    queryKey: ['admin', 'audit', query],
    queryFn: ({ signal }) => api.get<PagedResult<AuditLogEntry>>('/admin/audit-logs', { query, signal }),
    placeholderData: keepPreviousData,
  });

  const server = mapFieldErrors(audit.error, FILTER_KEYS);

  const apply = (event: FormEvent) => {
    event.preventDefault();
    const found: Partial<Record<FilterKey, string>> = {};
    if (draft.actorUserId && !GUID.test(draft.actorUserId.trim()))
      found.actorUserId = 'Enter a user id (GUID).';
    if (draft.from && draft.to && draft.from > draft.to)
      found.to = 'The end date must be on or after the start date.';
    setErrors(found);
    if (Object.keys(found).length) return;
    list.update(Object.fromEntries(FILTER_KEYS.map((k) => [k, draft[k].trim() || undefined])));
  };

  const field = (key: FilterKey, label: string, extra: Partial<ComponentProps<typeof Input>> = {}) => (
    <FormField label={label} error={errors[key] ?? server.fields[key]}>
      <Input value={draft[key]} onChange={(e) => setDraft({ ...draft, [key]: e.target.value })} {...extra} />
    </FormField>
  );

  return (
    <>
      <PageHeader
        title="Audit log"
        description="An append-only record of sensitive actions: who did what, when, why, and what changed."
        actions={
          <ExportCsvButton path="/admin/audit-logs/export.csv" query={apiQuery} fileName="audit-log.csv" />
        }
      />
      <Card>
        <CardBody>
          <form className="stack" onSubmit={apply} noValidate aria-label="Audit log filters" role="search">
            <div className="admin-filter-grid">
              {field('action', 'Action', {
                list: listId,
                placeholder: 'e.g. admin. or admin.user_suspended',
                autoComplete: 'off',
              })}
              <datalist id={listId}>
                {ACTION_SUGGESTIONS.map((a) => (
                  <option key={a} value={a} />
                ))}
              </datalist>
              {field('entityType', 'Entity type', { placeholder: 'e.g. User, SystemSetting' })}
              {field('entityId', 'Entity id')}
              {field('actorUserId', 'Actor user id', { placeholder: 'GUID' })}
              {field('from', 'From', { type: 'date' })}
              {field('to', 'To', { type: 'date' })}
            </div>
            <p className="text-small text-muted">
              Action matches by prefix — “admin.” finds every admin action. Dates are whole days in UTC.
            </p>
            <div className="cluster">
              <Button type="submit" leadingIcon={<Search />}>
                Apply filters
              </Button>
              <Button
                variant="ghost"
                leadingIcon={<RotateCcw />}
                onClick={() => {
                  setErrors({});
                  list.reset();
                }}
              >
                Reset
              </Button>
            </div>
          </form>
        </CardBody>
      </Card>
      <Card as="section" aria-labelledby="audit-results">
        <CardBody className="stack">
          <h2 id="audit-results" className="visually-hidden">
            Audit entries
          </h2>
          {audit.isPending ? (
            <SkeletonText lines={8} />
          ) : audit.isError ? (
            <QueryError error={audit.error} onRetry={() => void audit.refetch()} />
          ) : audit.data.items.length === 0 ? (
            <EmptyState icon={<ScrollText />} headingLevel={3} title="No entries match these filters" />
          ) : (
            <>
              <ul className="admin-audit-list" aria-busy={audit.isFetching || undefined}>
                {audit.data.items.map((entry) => (
                  <AuditEntry key={entry.id} entry={entry} />
                ))}
              </ul>
              <Pagination
                page={list.page}
                pageSize={list.pageSize}
                total={audit.data.total}
                onPageChange={(page) => list.update({ page }, false)}
                onPageSizeChange={(pageSize) => list.update({ pageSize })}
                label="Audit log pages"
              />
            </>
          )}
        </CardBody>
      </Card>
    </>
  );
}

function blank(values: Record<string, string | undefined>): Record<FilterKey, string> {
  return Object.fromEntries(FILTER_KEYS.map((k) => [k, values[k] ?? ''])) as Record<FilterKey, string>;
}
