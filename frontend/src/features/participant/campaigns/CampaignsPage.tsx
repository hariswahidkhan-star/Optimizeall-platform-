import { RotateCcw, Search, SearchX, SlidersHorizontal } from 'lucide-react';
import { useEffect, useMemo, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { Button } from '@/components/ui/Button';
import { Card } from '@/components/ui/Card';
import { Checkbox } from '@/components/ui/Checkbox';
import { EmptyState } from '@/components/ui/EmptyState';
import { ErrorState } from '@/components/ui/ErrorState';
import { FormField } from '@/components/ui/FormField';
import { Input } from '@/components/ui/Input';
import { PageHeader } from '@/components/ui/PageHeader';
import { Pagination } from '@/components/ui/Pagination';
import { Select } from '@/components/ui/Select';
import { Skeleton } from '@/components/ui/Skeleton';
import { useAuth } from '@/lib/auth/useAuth';
import { pluralize } from '@/lib/format/text';
import { CAMPAIGN_PAGE_SIZE, useCampaigns, useCategories } from '../api/queries';
import type { CampaignFilters } from '../api/types';
import { CampaignCard } from '../components/CampaignCard';
import { platformOptions } from '../lib/labels';
import { zonedLocalToUtcIso } from '../lib/zonedTime';
import { activeFilterCount, filtersFromSearch, searchFromFilters } from './campaignFilters';
import '../participant.css';

const SORT_OPTIONS = [
  { value: 'deadline', label: 'Deadline (soonest first)' },
  { value: 'reward', label: 'Reward (highest first)' },
  { value: 'newest', label: 'Newest' },
];

/** Text inputs update the URL after typing pauses, so every keystroke doesn't hit the API or history. */
function useDebouncedField(value: string | undefined, onCommit: (value: string) => void, delay = 400) {
  const [text, setText] = useState(value ?? '');
  const [last, setLast] = useState(value ?? '');
  if ((value ?? '') !== last) {
    // External change (reset/back button): adopt it.
    setLast(value ?? '');
    setText(value ?? '');
  }
  useEffect(() => {
    if (text === last) return;
    const timer = setTimeout(() => {
      setLast(text);
      onCommit(text);
    }, delay);
    return () => clearTimeout(timer);
  }, [text, last, onCommit, delay]);
  return [text, setText] as const;
}

export function CampaignsPage() {
  const { user } = useAuth();
  const [params, setParams] = useSearchParams();
  const filters = useMemo(() => filtersFromSearch(params), [params]);
  const categories = useCategories();

  const deadlineBeforeUtc = filters.deadlineBefore
    ? zonedLocalToUtcIso(`${filters.deadlineBefore}T23:59:59`, user?.timeZone)
    : null;
  const campaigns = useCampaigns(filters, deadlineBeforeUtc);

  const update = useMemo(
    () =>
      (patch: Partial<CampaignFilters>, options: { replace?: boolean } = {}) => {
        setParams(
          (current) => {
            const next = { ...filtersFromSearch(current), page: 1, ...patch };
            return searchFromFilters(next);
          },
          { replace: options.replace ?? false },
        );
      },
    [setParams],
  );

  const commitSearch = useMemo(
    () => (value: string) => update({ search: value.trim() || undefined }, { replace: true }),
    [update],
  );
  const commitTopic = useMemo(
    () => (value: string) => update({ topic: value.trim() || undefined }, { replace: true }),
    [update],
  );
  const commitMin = useMemo(
    () => (value: string) => update({ minReward: value.trim() || undefined }, { replace: true }),
    [update],
  );
  const [search, setSearch] = useDebouncedField(filters.search, commitSearch);
  const [topic, setTopic] = useDebouncedField(filters.topic, commitTopic);
  const [minReward, setMinReward] = useDebouncedField(filters.minReward, commitMin);

  const activeCount = activeFilterCount(filters);
  // Phones: the secondary filters fold away behind a toggle (always shown from md up via CSS).
  const [moreOpen, setMoreOpen] = useState(false);
  const categoryOptions = (categories.data ?? []).map((c) => ({ value: c.id, label: c.name }));

  return (
    <div className="pp-page">
      <PageHeader
        title="Campaigns"
        description="Share approved content from your eligible profiles and get paid for every qualifying post."
      />

      <Card as="section" aria-labelledby="campaign-filters-title" flat>
        <h2 id="campaign-filters-title" className="visually-hidden">
          Filter campaigns
        </h2>
        <form
          role="search"
          className="pp-filters"
          onSubmit={(e) => {
            e.preventDefault();
            update({
              search: search.trim() || undefined,
              topic: topic.trim() || undefined,
              minReward: minReward || undefined,
            });
          }}
        >
          <FormField label="Search" className="pp-filters__wide" id="campaigns-search">
            <Input
              type="search"
              leading={<Search />}
              value={search}
              placeholder="Title or summary"
              onChange={(e) => setSearch(e.target.value)}
              autoComplete="off"
            />
          </FormField>
          <div className="pp-filters__toggle">
            <Button
              variant="secondary"
              leadingIcon={<SlidersHorizontal />}
              aria-expanded={moreOpen}
              aria-controls="campaign-more-filters"
              onClick={() => setMoreOpen((v) => !v)}
              fullWidth
            >
              {moreOpen ? 'Hide filters' : `Filters${activeCount > 0 ? ` (${activeCount})` : ''}`}
            </Button>
          </div>
          <div
            id="campaign-more-filters"
            className={moreOpen ? 'pp-filters__more is-open' : 'pp-filters__more'}
          >
            <FormField label="Platform" id="campaigns-platform">
              <Select
                value={filters.platform ?? ''}
                placeholder="All platforms"
                options={platformOptions}
                onChange={(e) => update({ platform: e.target.value || undefined })}
              />
            </FormField>
            <FormField label="Category" id="campaigns-category">
              <Select
                value={filters.categoryId ?? ''}
                placeholder={categories.isPending ? 'Loading…' : 'All categories'}
                options={categoryOptions}
                onChange={(e) => update({ categoryId: e.target.value || undefined })}
              />
            </FormField>
            <FormField label="Topic" id="campaigns-topic">
              <Input value={topic} placeholder="e.g. fitness" onChange={(e) => setTopic(e.target.value)} />
            </FormField>
            <FormField label="Minimum base reward" id="campaigns-min-reward">
              <Input
                type="number"
                inputMode="decimal"
                min={0}
                step="0.01"
                value={minReward}
                onChange={(e) => setMinReward(e.target.value)}
              />
            </FormField>
            <FormField label="Deadline on or before" id="campaigns-deadline">
              <Input
                type="date"
                value={filters.deadlineBefore ?? ''}
                onChange={(e) => update({ deadlineBefore: e.target.value || undefined })}
              />
            </FormField>
            <FormField label="Sort by" id="campaigns-sort">
              <Select
                value={filters.sort ?? 'deadline'}
                options={SORT_OPTIONS}
                onChange={(e) =>
                  update({
                    sort:
                      e.target.value === 'deadline' ? undefined : (e.target.value as CampaignFilters['sort']),
                  })
                }
              />
            </FormField>
            <div className="pp-filters__footer">
              <Checkbox
                label="Only campaigns I’m eligible for"
                checked={!!filters.eligibleOnly}
                onChange={(e) => update({ eligibleOnly: e.target.checked })}
              />
              {activeCount > 0 && (
                <Button
                  variant="ghost"
                  size="sm"
                  leadingIcon={<RotateCcw />}
                  onClick={() =>
                    setParams(
                      filters.sort ? new URLSearchParams({ sort: filters.sort }) : new URLSearchParams(),
                    )
                  }
                >
                  Clear {pluralize(activeCount, 'filter')}
                </Button>
              )}
            </div>
          </div>
        </form>
      </Card>

      <section
        aria-labelledby="campaign-results-title"
        className="pp-section"
        aria-busy={campaigns.isFetching || undefined}
      >
        <div className="pp-section__head">
          <h2 id="campaign-results-title" className="pp-section__title">
            Results
          </h2>
          <p className="pp-muted text-small" role="status" aria-live="polite">
            {campaigns.isSuccess ? pluralize(campaigns.data.total, 'campaign') + ' found' : ''}
          </p>
        </div>

        {campaigns.isPending && (
          <div className="pp-grid" aria-hidden="true">
            {Array.from({ length: 6 }, (_, i) => (
              <Skeleton key={i} height={260} />
            ))}
          </div>
        )}

        {campaigns.isError && (
          <Card flat>
            <ErrorState
              error={campaigns.error}
              title="Campaigns couldn’t be loaded"
              onRetry={() => void campaigns.refetch()}
              retrying={campaigns.isFetching}
            />
          </Card>
        )}

        {campaigns.isSuccess && campaigns.data.items.length === 0 && (
          <Card flat>
            <EmptyState
              icon={<SearchX />}
              title={activeCount > 0 ? 'No campaigns match these filters' : 'No campaigns are open right now'}
              description={
                activeCount > 0
                  ? 'Try removing a filter or searching for something else.'
                  : 'New campaigns open regularly — we’ll notify you when one matches your interests.'
              }
              action={
                activeCount > 0 ? (
                  <Button variant="secondary" onClick={() => setParams(new URLSearchParams())}>
                    Clear filters
                  </Button>
                ) : undefined
              }
            />
          </Card>
        )}

        {campaigns.isSuccess && campaigns.data.items.length > 0 && (
          <>
            <div className="pp-grid">
              {campaigns.data.items.map((c) => (
                <CampaignCard key={c.id} campaign={c} />
              ))}
            </div>
            <Pagination
              page={filters.page}
              pageSize={CAMPAIGN_PAGE_SIZE}
              total={campaigns.data.total}
              onPageChange={(page) => {
                update({ ...filters, page });
                document.getElementById('campaign-results-title')?.scrollIntoView({ block: 'start' });
              }}
              label="Campaign pages"
            />
          </>
        )}
      </section>
    </div>
  );
}
