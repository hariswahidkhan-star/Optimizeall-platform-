import '../academy.css';
import clsx from 'clsx';
import { GraduationCap, Search } from 'lucide-react';
import { useId, type ReactNode } from 'react';
import { useSearchParams } from 'react-router-dom';
import { Card } from '@/components/ui/Card';
import { EmptyState } from '@/components/ui/EmptyState';
import { ErrorState } from '@/components/ui/ErrorState';
import { Input } from '@/components/ui/Input';
import { Pagination } from '@/components/ui/Pagination';
import { Select } from '@/components/ui/Select';
import { Skeleton } from '@/components/ui/Skeleton';
import {
  CATEGORY_LABELS,
  LEVELS,
  useCategories,
  useMyCatalog,
  usePublicCatalog,
  type CatalogFilters,
  type CourseCard as CourseCardData,
  type CourseCategory,
  type CourseLevel,
} from '../api';
import { categoryClass, CourseCard } from './CourseCard';
import { stagger, useReveal } from './Motion';

const PAGE_SIZE = 24;

/** Catalog filters live in the URL (?category=Seo&level=Beginner&q=…&sort=…&page=2), so views are shareable. */
export function useCatalogFilters(): [CatalogFilters, (patch: Partial<CatalogFilters>) => void] {
  const [params, setParams] = useSearchParams();
  const filters: CatalogFilters = {
    category: (params.get('category') as CourseCategory | null) ?? '',
    level: (params.get('level') as CourseLevel | null) ?? '',
    maxMinutes: params.get('max') ? Number(params.get('max')) : '',
    search: params.get('q') ?? '',
    sort: (params.get('sort') as CatalogFilters['sort']) ?? '',
    page: Math.max(1, Number(params.get('page') ?? 1) || 1),
    pageSize: PAGE_SIZE,
  };
  const update = (patch: Partial<CatalogFilters>) => {
    const next = { ...filters, page: 1, ...patch };
    const out = new URLSearchParams();
    if (next.category) out.set('category', next.category);
    if (next.level) out.set('level', next.level);
    if (next.maxMinutes) out.set('max', String(next.maxMinutes));
    if (next.search) out.set('q', next.search);
    if (next.sort) out.set('sort', next.sort);
    if (next.page && next.page > 1) out.set('page', String(next.page));
    setParams(out, { replace: true });
  };
  return [filters, update];
}

interface Item {
  course: CourseCardData;
  progress?: { percent: number; passed: boolean } | null;
}

function Browser({
  filters,
  update,
  data,
  isPending,
  isError,
  error,
  refetch,
  linkFor,
  headingLevel,
}: {
  filters: CatalogFilters;
  update: (patch: Partial<CatalogFilters>) => void;
  data?: { items: Item[]; total: number };
  isPending: boolean;
  isError: boolean;
  error: unknown;
  refetch: () => void;
  linkFor: (slug: string) => string;
  headingLevel: 2 | 3;
}) {
  const categories = useCategories();
  const searchId = useId();
  // Re-arm the reveal when the page of results changes (filters, search, pagination).
  const gridRef = useReveal<HTMLUListElement>(data?.items.map((i) => i.course.id).join(','));
  const counts = new Map(categories.data?.map((c) => [c.category, c.courseCount]) ?? []);
  const shown = categories.data ? categories.data.map((c) => c.category) : (Object.keys(CATEGORY_LABELS) as CourseCategory[]);
  const total = categories.data?.reduce((sum, c) => sum + c.courseCount, 0);

  return (
    <div className="lx-catalog">
      <div className="lx-catalog__tabs" role="group" aria-label="Filter by category">
        <button
          type="button"
          className={clsx('lx-chip', !filters.category && 'is-active')}
          aria-pressed={!filters.category}
          onClick={() => update({ category: '' })}
        >
          All courses{total !== undefined ? <span className="lx-chip__count">{total}</span> : null}
        </button>
        {shown.map((c) => (
          <button
            key={c}
            type="button"
            className={clsx('lx-chip', categoryClass(c), filters.category === c && 'is-active')}
            aria-pressed={filters.category === c}
            onClick={() => update({ category: c })}
          >
            {CATEGORY_LABELS[c]}
            {counts.has(c) ? <span className="lx-chip__count">{counts.get(c)}</span> : null}
          </button>
        ))}
      </div>
      <form className="lx-catalog__filters" role="search" aria-label="Filter courses" onSubmit={(e) => e.preventDefault()}>
        <div className="lx-catalog__search">
          <label htmlFor={searchId} className="visually-hidden">
            Search courses
          </label>
          <Input
            id={searchId}
            type="search"
            placeholder="Search courses, skills or badges"
            leading={<Search aria-hidden="true" />}
            defaultValue={filters.search}
            onChange={(e) => update({ search: e.target.value })}
          />
        </div>
        <Select
          aria-label="Level"
          value={filters.level}
          onChange={(e) => update({ level: e.target.value as CourseLevel | '' })}
          options={[{ value: '', label: 'All levels' }, ...LEVELS.map((l) => ({ value: l, label: l }))]}
        />
        <Select
          aria-label="Duration"
          value={String(filters.maxMinutes || '')}
          onChange={(e) => update({ maxMinutes: e.target.value ? Number(e.target.value) : '' })}
          options={[
            { value: '', label: 'Any length' },
            { value: '60', label: 'Up to 1 hour' },
            { value: '120', label: 'Up to 2 hours' },
            { value: '240', label: 'Up to 4 hours' },
          ]}
        />
        <Select
          aria-label="Sort by"
          value={filters.sort ?? ''}
          onChange={(e) => update({ sort: e.target.value as CatalogFilters['sort'] })}
          options={[
            { value: '', label: 'Featured' },
            { value: 'newest', label: 'Newest' },
            { value: 'title', label: 'Title A–Z' },
            { value: 'duration', label: 'Shortest first' },
            { value: 'level', label: 'Level' },
          ]}
        />
      </form>

      {isError && (
        <Card flat>
          <ErrorState error={error} title="The course catalog isn’t available right now" onRetry={refetch} />
        </Card>
      )}
      {isPending && !data && (
        <div className="lx-grid" aria-busy="true">
          <span className="visually-hidden" role="status">
            Loading courses…
          </span>
          {[1, 2, 3, 4, 5, 6].map((n) => (
            <Skeleton key={n} height={260} radius="var(--radius-xl)" />
          ))}
        </div>
      )}
      {data && data.items.length === 0 && (
        <Card flat>
          <EmptyState
            icon={<GraduationCap />}
            title="No courses match these filters"
            description="Try another category or level, or clear the search."
            headingLevel={headingLevel}
          />
        </Card>
      )}
      {data && data.items.length > 0 && (
        <>
          <ul className="lx-grid lx-reveal" aria-label="Courses" ref={gridRef}>
            {data.items.map((item, i) => (
              <li key={item.course.id} style={stagger(i)}>
                <CourseCard course={item.course} to={linkFor(item.course.slug)} headingLevel={headingLevel} progress={item.progress} />
              </li>
            ))}
          </ul>
          {data.total > PAGE_SIZE && (
            <Pagination
              page={filters.page ?? 1}
              pageSize={PAGE_SIZE}
              total={data.total}
              onPageChange={(page) => update({ page })}
              label="Course pages"
            />
          )}
        </>
      )}
    </div>
  );
}

/** Public academy catalog (anonymous). */
export function PublicCatalog({ linkFor, headingLevel = 2 }: { linkFor: (slug: string) => string; headingLevel?: 2 | 3 }) {
  const [filters, update] = useCatalogFilters();
  const q = usePublicCatalog(filters);
  return (
    <Browser
      filters={filters}
      update={update}
      data={q.data ? { items: q.data.items.map((course) => ({ course })), total: q.data.total } : undefined}
      isPending={q.isPending}
      isError={q.isError}
      error={q.error}
      refetch={() => void q.refetch()}
      linkFor={linkFor}
      headingLevel={headingLevel}
    />
  );
}

/** Portal catalog with the learner's progress on each card. */
export function MyCatalog({ linkFor, headingLevel = 2 }: { linkFor: (slug: string) => string; headingLevel?: 2 | 3 }): ReactNode {
  const [filters, update] = useCatalogFilters();
  const q = useMyCatalog(filters);
  return (
    <Browser
      filters={filters}
      update={update}
      data={
        q.data
          ? {
              items: q.data.items.map((c) => ({
                course: c.course,
                progress: c.enrolled ? { percent: c.progressPercent, passed: c.passed } : null,
              })),
              total: q.data.total,
            }
          : undefined
      }
      isPending={q.isPending}
      isError={q.isError}
      error={q.error}
      refetch={() => void q.refetch()}
      linkFor={linkFor}
      headingLevel={headingLevel}
    />
  );
}
