import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import {
  Alert,
  Badge,
  Button,
  DataTable,
  type DataTableColumn,
  Dialog,
  ErrorState,
  Input,
  PageHeader,
  Select,
  Skeleton,
  Stat,
  Tabs,
  useToast,
} from '@/components/ui';
import type { CopyCatalog } from '@/features/admin/content/CopyEditor';
import { applyTitleTemplate } from '@/features/public/site/head';
import { api } from '@/lib/api/client';
import { errorMessage, isApiError } from '@/lib/api/errors';
import { W } from '../api';
import { AreaField, type Errors, SwitchField, TextField, toErrors } from '../shared/fields';
import '../website.css';

export interface SeoWarning {
  code: string;
  severity: 'error' | 'warning' | 'notice';
  message: string;
}

export interface SeoOverviewRow {
  path: string;
  url: string;
  status: number;
  title: string;
  titleLength: number;
  description: string | null;
  descriptionLength: number;
  canonical: string | null;
  indexable: boolean;
  robots: string;
  inSitemap: boolean;
  jsonLdTypes: string[];
  h1Count: number;
  imageCount: number;
  videoCount: number;
  lastModified: string | null;
  source: string;
  editPath: string | null;
  copyKeys: { title: string; description: string | null } | null;
  warnings: SeoWarning[];
}

export interface SeoOverview {
  siteUrl: string;
  total: number;
  indexable: number;
  withErrors: number;
  withWarnings: number;
  rows: SeoOverviewRow[];
  generatedAt: string;
}

export interface CrawlerGroup {
  key: string;
  label: string;
  description: string;
  allowedByDefault: boolean;
  allowed: boolean;
  userAgents: string[];
}

export interface SeoSettings {
  crawlerGroups: CrawlerGroup[];
  indexNowEnabled: boolean;
  indexNowKey: string | null;
  llmsTxtEnabled: boolean;
  securityContactEmail: string | null;
  updatedAt: string;
  concurrencyStamp: string;
}

const TITLE_MAX = 60;
const DESCRIPTION_MAX = 155;
const OVERVIEW_KEY = ['website', 'seo', 'overview'] as const;

const SEVERITY_TONE = { error: 'danger', warning: 'warning', notice: 'info' } as const;

function worst(row: SeoOverviewRow): SeoWarning['severity'] | null {
  if (row.warnings.some((w) => w.severity === 'error')) return 'error';
  if (row.warnings.some((w) => w.severity === 'warning')) return 'warning';
  return row.warnings.length ? 'notice' : null;
}

function Length({ value, max }: { value: number; max: number }) {
  return (
    <span className={value > max ? 'seo-over' : 'text-muted'} title={`${value} of ${max} characters`}>
      {value}/{max}
    </span>
  );
}

/** Edits a built-in page's search title and description (page texts), with live length counters. */
function EditMetaDialog({ row, onClose }: { row: SeoOverviewRow; onClose: () => void }) {
  const qc = useQueryClient();
  const toast = useToast();
  const catalog = useQuery({
    queryKey: ['copy-editor', `${W}/copy`],
    queryFn: () => api.get<CopyCatalog>(`${W}/copy`),
  });
  const entries = useMemo(
    () => new Map(catalog.data?.groups.flatMap((g) => g.entries).map((e) => [e.key, e]) ?? []),
    [catalog.data],
  );
  const keys = row.copyKeys!;
  const [title, setTitle] = useState<string | null>(null);
  const [description, setDescription] = useState<string | null>(null);
  const [errors, setErrors] = useState<Errors>({});

  useEffect(() => {
    if (!catalog.data || title !== null) return;
    setTitle(entries.get(keys.title)?.value ?? '');
    setDescription(keys.description ? (entries.get(keys.description)?.value ?? '') : null);
  }, [catalog.data, entries, keys, title]);

  const save = useMutation({
    mutationFn: () =>
      api.put<CopyCatalog>(`${W}/copy`, {
        changes: [
          {
            key: keys.title,
            value: title,
            concurrencyStamp: entries.get(keys.title)?.concurrencyStamp ?? null,
          },
          ...(keys.description
            ? [
                {
                  key: keys.description,
                  value: description,
                  concurrencyStamp: entries.get(keys.description)?.concurrencyStamp ?? null,
                },
              ]
            : []),
        ],
      }),
    onSuccess: (data) => {
      qc.setQueryData(['copy-editor', `${W}/copy`], data);
      void qc.invalidateQueries({ queryKey: OVERVIEW_KEY });
      void qc.invalidateQueries({ queryKey: ['content', 'copy'] });
      toast.success('Search title and description saved');
      onClose();
    },
    onError: (e) => {
      if (isApiError(e)) setErrors(toErrors(e.errors));
      toast.error(errorMessage(e));
    },
  });

  const templated = applyTitleTemplate(title ?? '', '%s | Optimize All', 'Optimize All');
  return (
    <Dialog
      open
      onClose={onClose}
      title={`Search snippet of ${row.path}`}
      description="The title (the site's name is added after it) and description search engines and social networks show for this page."
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button onClick={() => save.mutate()} loading={save.isPending} disabled={title === null}>
            Save
          </Button>
        </>
      }
    >
      {catalog.isError ? (
        <ErrorState error={catalog.error} onRetry={() => void catalog.refetch()} />
      ) : title === null ? (
        <Skeleton height={160} />
      ) : (
        <div className="cms-form">
          <TextField
            label="Search title"
            value={title}
            onChange={setTitle}
            required
            error={errors[`changes[0].value`] ?? errors[keys.title]}
            hint={
              <>
                Shown as “{templated}” — <Length value={templated.length} max={TITLE_MAX} />
              </>
            }
          />
          {keys.description && (
            <AreaField
              label="Search description"
              value={description ?? ''}
              onChange={setDescription}
              rows={3}
              error={errors[`changes[1].value`] ?? errors[keys.description]}
              hint={<Length value={(description ?? '').length} max={DESCRIPTION_MAX} />}
            />
          )}
        </div>
      )}
    </Dialog>
  );
}

function OverviewTab() {
  const query = useQuery({
    queryKey: OVERVIEW_KEY,
    queryFn: () => api.get<SeoOverview>(`${W}/seo/overview`),
  });
  const [filter, setFilter] = useState('issues');
  const [search, setSearch] = useState('');
  const [editing, setEditing] = useState<SeoOverviewRow | null>(null);

  const rows = useMemo(() => {
    const q = search.trim().toLowerCase();
    return (query.data?.rows ?? []).filter((r) => {
      if (filter === 'issues' && !r.warnings.some((w) => w.severity !== 'notice')) return false;
      if (filter === 'indexable' && !r.indexable) return false;
      if (filter === 'noindex' && r.indexable) return false;
      return !q || r.path.toLowerCase().includes(q) || r.title.toLowerCase().includes(q);
    });
  }, [query.data, filter, search]);

  const columns: DataTableColumn<SeoOverviewRow>[] = [
    {
      id: 'path',
      header: 'URL',
      primary: true,
      sortable: true,
      sortValue: (r) => r.path,
      cell: (r) => (
        <div className="seo-cell">
          <a href={r.path} target="_blank" rel="noreferrer">
            {r.path}
          </a>
          <span className="text-small text-muted">{r.source}</span>
        </div>
      ),
    },
    {
      id: 'title',
      header: 'Title and description',
      cell: (r) => (
        <div className="seo-cell">
          <strong>{r.title}</strong> <Length value={r.titleLength} max={TITLE_MAX} />
          <span className="text-small">{r.description ?? <em>No description</em>}</span>
          {r.description && <Length value={r.descriptionLength} max={DESCRIPTION_MAX} />}
        </div>
      ),
    },
    {
      id: 'index',
      header: 'Indexing',
      sortable: true,
      sortValue: (r) => (r.indexable ? 1 : 0),
      cell: (r) => (
        <div className="seo-cell">
          <Badge tone={r.indexable ? 'success' : 'neutral'} size="sm">
            {r.indexable ? 'Indexable' : r.status === 200 ? 'noindex' : `HTTP ${r.status}`}
          </Badge>
          {r.inSitemap && <span className="text-small text-muted">In sitemap</span>}
          <span className="text-small text-muted">{r.jsonLdTypes.join(', ') || 'No structured data'}</span>
        </div>
      ),
    },
    {
      id: 'warnings',
      header: 'Checks',
      sortable: true,
      sortValue: (r) =>
        ({ error: 3, warning: 2, notice: 1 })[worst(r) ?? 'notice'] * (r.warnings.length ? 1 : 0),
      cell: (r) =>
        r.warnings.length === 0 ? (
          <Badge tone="success" size="sm">
            All good
          </Badge>
        ) : (
          <ul className="seo-warnings">
            {r.warnings.map((w) => (
              <li key={w.code}>
                <Badge tone={SEVERITY_TONE[w.severity]} size="sm">
                  {w.severity}
                </Badge>{' '}
                {w.message}
              </li>
            ))}
          </ul>
        ),
    },
    {
      id: 'edit',
      header: <span className="visually-hidden">Edit</span>,
      cell: (r) =>
        r.copyKeys ? (
          <Button size="sm" variant="secondary" onClick={() => setEditing(r)}>
            Edit snippet
          </Button>
        ) : r.editPath ? (
          <Link to={r.editPath} className="text-small">
            Edit in {r.source}
          </Link>
        ) : null,
    },
  ];

  if (query.isError) return <ErrorState error={query.error} onRetry={() => void query.refetch()} />;
  const data = query.data;
  return (
    <div className="stack">
      <div className="grid-auto">
        <Stat label="Public URLs" value={data?.total ?? '…'} loading={!data} />
        <Stat label="Indexable" value={data?.indexable ?? '…'} loading={!data} />
        <Stat label="With errors" value={data?.withErrors ?? '…'} loading={!data} />
        <Stat label="With warnings" value={data?.withWarnings ?? '…'} loading={!data} />
      </div>
      <div className="cms-grid-2">
        <Select
          aria-label="Show"
          value={filter}
          onChange={(e) => setFilter(e.target.value)}
          options={[
            { value: 'issues', label: 'Pages with errors or warnings' },
            { value: 'all', label: 'All public URLs' },
            { value: 'indexable', label: 'Indexable pages' },
            { value: 'noindex', label: 'Not indexed' },
          ]}
        />
        <Input
          aria-label="Search URLs and titles"
          placeholder="Search URLs and titles"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
        />
      </div>
      <DataTable
        caption="Public URLs and their search metadata"
        columns={columns}
        rows={rows}
        getRowId={(r) => r.path}
        loading={!data}
        defaultSort={{ id: 'warnings', desc: true }}
        emptyState={
          <Alert tone="success" title="Nothing to fix">
            No page matches this filter.
          </Alert>
        }
      />
      {editing && <EditMetaDialog row={editing} onClose={() => setEditing(null)} />}
    </div>
  );
}

function CrawlersTab() {
  const qc = useQueryClient();
  const toast = useToast();
  const query = useQuery({
    queryKey: ['website', 'seo', 'settings'],
    queryFn: () => api.get<SeoSettings>(`${W}/seo/settings`),
  });
  const [draft, setDraft] = useState<SeoSettings | null>(null);
  const [errors, setErrors] = useState<Errors>({});
  useEffect(() => {
    if (query.data && !draft) setDraft(query.data);
  }, [query.data, draft]);

  const save = useMutation({
    mutationFn: (s: SeoSettings) =>
      api.put<SeoSettings>(`${W}/seo/settings`, {
        crawlerGroups: Object.fromEntries(s.crawlerGroups.map((g) => [g.key, g.allowed])),
        indexNowEnabled: s.indexNowEnabled,
        llmsTxtEnabled: s.llmsTxtEnabled,
        securityContactEmail: s.securityContactEmail || null,
        concurrencyStamp: query.data!.concurrencyStamp,
      }),
    onSuccess: (saved) => {
      qc.setQueryData(['website', 'seo', 'settings'], saved);
      setDraft(saved);
      setErrors({});
      toast.success('SEO settings saved');
    },
    onError: (e) => {
      if (isApiError(e)) setErrors(toErrors(e.errors));
      toast.error(errorMessage(e));
    },
  });

  if (query.isError) return <ErrorState error={query.error} onRetry={() => void query.refetch()} />;
  if (!draft) return <Skeleton height={320} />;
  const setGroup = (key: string, allowed: boolean) =>
    setDraft({
      ...draft,
      crawlerGroups: draft.crawlerGroups.map((g) => (g.key === key ? { ...g, allowed } : g)),
    });

  return (
    <div className="cms-form">
      <p className="text-muted">
        Decide which crawlers may read the public website. Signed-in areas, the API, personal links and search
        results are always closed to every crawler. Changes update <a href="/robots.txt">robots.txt</a>{' '}
        immediately and are recorded in the audit log.
      </p>
      {draft.crawlerGroups.map((g) => (
        <fieldset key={g.key} className="cms-fieldset">
          <legend>{g.label}</legend>
          <SwitchField
            label={g.allowed ? `Allowed to crawl the public site` : `Blocked from the whole site`}
            checked={g.allowed}
            onChange={(v) => setGroup(g.key, v)}
            description={
              <>
                {g.description} {g.allowedByDefault ? 'Allowed by default.' : 'Blocked by default.'}
                <br />
                <span className="text-small text-muted">User agents: {g.userAgents.join(', ')}</span>
              </>
            }
          />
        </fieldset>
      ))}
      <fieldset className="cms-fieldset">
        <legend>AI assistants and search engines</legend>
        <SwitchField
          label="Publish llms.txt and Markdown page versions"
          checked={draft.llmsTxtEnabled}
          onChange={(v) => setDraft({ ...draft, llmsTxtEnabled: v })}
          description={
            <>
              A Markdown guide to the site for AI assistants at <a href="/llms.txt">/llms.txt</a> and{' '}
              <a href="/llms-full.txt">/llms-full.txt</a>.
            </>
          }
        />
        <SwitchField
          label="Notify IndexNow when pages change"
          checked={draft.indexNowEnabled}
          onChange={(v) => setDraft({ ...draft, indexNowEnabled: v })}
          description="Bing, Yandex and other IndexNow search engines are told about new and updated pages every 10 minutes. Needs the public https site URL in Site settings."
        />
        {draft.indexNowKey && (
          <p className="text-small text-muted">IndexNow key file: /{draft.indexNowKey}.txt</p>
        )}
        <TextField
          label="Security contact email"
          value={draft.securityContactEmail ?? ''}
          onChange={(v) => setDraft({ ...draft, securityContactEmail: v })}
          error={errors.securityContactEmail}
          hint="Published in /.well-known/security.txt. Defaults to the contact email in Site settings."
        />
      </fieldset>
      <div>
        <Button onClick={() => save.mutate(draft)} loading={save.isPending}>
          Save SEO settings
        </Button>
      </div>
    </div>
  );
}

/** Agency → Website → SEO: every public URL's search metadata with warnings, and the crawler / AI bot policy. */
export function SeoAdminPage() {
  return (
    <div className="stack">
      <PageHeader
        title="SEO"
        description="What search engines, social networks and AI assistants see for every public page — titles, descriptions, canonical URLs, indexing and structured data — plus which crawlers may read the site."
      />
      <Tabs
        label="SEO sections"
        tabs={[
          { id: 'pages', label: 'Pages', content: <OverviewTab /> },
          { id: 'crawlers', label: 'Crawlers & AI', content: <CrawlersTab /> },
        ]}
      />
    </div>
  );
}
