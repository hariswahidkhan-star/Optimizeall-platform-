import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Globe, Plus, ScanSearch } from 'lucide-react';
import { useState, type FormEvent } from 'react';
import { Link } from 'react-router-dom';
import {
  Badge,
  Button,
  ButtonLink,
  DataTable,
  DateTime,
  Dialog,
  EmptyState,
  ErrorState,
  FilterBar,
  FormField,
  Input,
  PageHeader,
  Pagination,
  Select,
  useToast,
  type DataTableColumn,
} from '@/components/ui';
import { api } from '@/lib/api/client';
import type { PagedResult } from '@/lib/api/types';
import { seoKeys, type Site, type SiteInput } from './api';
import { fieldErrors, firstError, healthTone, useClientOptions } from './common';
import './seo.css';

export function SeoSitesPage() {
  const [search, setSearch] = useState('');
  const [filters, setFilters] = useState<Record<string, string | undefined>>({});
  const [page, setPage] = useState(1);
  const [creating, setCreating] = useState(false);
  const clients = useClientOptions('seo');
  const params = { search, clientId: filters.client, page, pageSize: 25 };
  const query = useQuery({
    queryKey: seoKeys.sites(params),
    queryFn: () => api.get<PagedResult<Site>>('/agency/seo/sites', { query: params }),
    placeholderData: keepPreviousData,
  });

  const columns: DataTableColumn<Site>[] = [
    {
      id: 'site',
      header: 'Site',
      primary: true,
      cell: (s) => (
        <span className="stack seo-stack-xs">
          <Link to={`sites/${s.id}`} className="ui-link seo-strong">
            {s.name}
          </Link>
          <span className="text-small text-muted">{s.baseUrl}</span>
        </span>
      ),
    },
    { id: 'client', header: 'Client', cell: (s) => s.clientName },
    {
      id: 'health',
      header: 'Health score',
      cell: (s) =>
        s.healthScore === null ? <span className="text-muted">No audit yet</span> : <Badge tone={healthTone(s.healthScore)}>{s.healthScore}/100</Badge>,
    },
    { id: 'keywords', header: 'Keywords', align: 'right', cell: (s) => s.keywordCount },
    {
      id: 'audit',
      header: 'Last audit',
      hideOnMobile: true,
      cell: (s) => (s.lastAuditAt ? <DateTime value={s.lastAuditAt} format="relative" /> : '—'),
    },
  ];

  return (
    <>
      <PageHeader
        title="SEO"
        description="Site audits, keyword rankings, backlinks, local SEO and content briefs for every client site."
        actions={
          <>
            <ButtonLink to="analyzer" variant="secondary" leadingIcon={<ScanSearch />}>
              On-page analyzer
            </ButtonLink>
            <Button leadingIcon={<Plus />} onClick={() => setCreating(true)}>
              Add site
            </Button>
          </>
        }
      />
      <div className="stack">
        <FilterBar
          search={search}
          onSearchChange={(s) => {
            setSearch(s);
            setPage(1);
          }}
          searchLabel="Search sites"
          searchPlaceholder="Search name or domain…"
          filters={[{ id: 'client', label: 'Client', options: (clients.data ?? []).map((c) => ({ value: c.id, label: c.name })) }]}
          values={filters}
          onFilterChange={(id, value) => {
            setFilters((f) => ({ ...f, [id]: value }));
            setPage(1);
          }}
          onReset={() => {
            setSearch('');
            setFilters({});
          }}
        />
        {query.isError ? (
          <ErrorState error={query.error} onRetry={() => void query.refetch()} />
        ) : (
          <>
            <DataTable
              caption="SEO sites"
              columns={columns}
              rows={query.data?.items ?? []}
              getRowId={(s) => s.id}
              loading={query.isLoading}
              emptyState={
                <EmptyState
                  icon={<Globe />}
                  title="No sites yet"
                  description="Add a client's website to run audits and track rankings."
                  action={<Button onClick={() => setCreating(true)}>Add site</Button>}
                />
              }
            />
            {query.data && query.data.total > 0 && (
              <Pagination page={page} pageSize={25} total={query.data.total} onPageChange={setPage} />
            )}
          </>
        )}
      </div>
      {creating && <SiteDialog onClose={() => setCreating(false)} />}
    </>
  );
}

/** Create or edit a site. */
export function SiteDialog({ site, onClose }: { site?: Site; onClose: () => void }) {
  const queryClient = useQueryClient();
  const toast = useToast();
  const clients = useClientOptions('seo');
  const [form, setForm] = useState({
    clientAccountId: site?.clientAccountId ?? '',
    name: site?.name ?? '',
    domain: site?.domain ?? '',
    protocol: site?.protocol ?? 'https',
    sitemapUrl: site?.sitemapUrl ?? '',
    targetCountry: site?.targetCountry ?? 'US',
    targetLanguage: site?.targetLanguage ?? 'en',
    competitors: site?.competitors.join(', ') ?? '',
    maxPages: String(site?.maxPages ?? 500),
  });
  const set = (key: keyof typeof form) => (value: string) => setForm((f) => ({ ...f, [key]: value }));
  const save = useMutation({
    mutationFn: () => {
      const body: SiteInput = {
        clientAccountId: form.clientAccountId,
        name: form.name,
        domain: form.domain,
        protocol: form.protocol,
        sitemapUrl: form.sitemapUrl || null,
        targetCountry: form.targetCountry,
        targetLanguage: form.targetLanguage,
        competitors: form.competitors.split(/[,\n]/).map((c) => c.trim()).filter(Boolean),
        maxPages: Number(form.maxPages) || null,
        concurrencyStamp: site?.concurrencyStamp,
      };
      return site ? api.put<Site>(`/agency/seo/sites/${site.id}`, body) : api.post<Site>('/agency/seo/sites', body);
    },
    onSuccess: (saved) => {
      toast.success(site ? 'Site updated' : 'Site added', saved.name);
      void queryClient.invalidateQueries({ queryKey: seoKeys.all });
      onClose();
    },
  });
  const errors = fieldErrors(save.error);
  const submit = (e: FormEvent) => {
    e.preventDefault();
    save.mutate();
  };

  return (
    <Dialog
      open
      onClose={onClose}
      title={site ? 'Edit site' : 'Add a site'}
      description="Audits crawl this domain; rankings are tracked for its target market."
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" form="seo-site-form" loading={save.isPending}>
            {site ? 'Save changes' : 'Add site'}
          </Button>
        </>
      }
    >
      <form id="seo-site-form" className="stack" onSubmit={submit} noValidate>
        {save.isError && Object.keys(errors).length === 0 && <p role="alert" className="seo-error">{(save.error as Error).message}</p>}
        <FormField label="Client" required error={firstError(errors, 'clientAccountId')}>
          <Select
            value={form.clientAccountId}
            onChange={(e) => set('clientAccountId')(e.target.value)}
            placeholder="Choose a client"
            disabled={!!site}
            options={(clients.data ?? []).map((c) => ({ value: c.id, label: c.name }))}
          />
        </FormField>
        <FormField label="Site name" required error={firstError(errors, 'name')}>
          <Input value={form.name} onChange={(e) => set('name')(e.target.value)} />
        </FormField>
        <div className="seo-grid-2">
          <FormField label="Domain" required hint="e.g. www.example.com" error={firstError(errors, 'domain')}>
            <Input value={form.domain} onChange={(e) => set('domain')(e.target.value)} inputMode="url" />
          </FormField>
          <FormField label="Protocol">
            <Select
              value={form.protocol}
              onChange={(e) => set('protocol')(e.target.value)}
              options={[
                { value: 'https', label: 'https' },
                { value: 'http', label: 'http' },
              ]}
            />
          </FormField>
        </div>
        <FormField label="Sitemap URL" optional hint="Leave empty to use robots.txt or /sitemap.xml." error={firstError(errors, 'sitemapUrl')}>
          <Input value={form.sitemapUrl} onChange={(e) => set('sitemapUrl')(e.target.value)} inputMode="url" />
        </FormField>
        <div className="seo-grid-2">
          <FormField label="Target country" required hint="Two-letter code, e.g. GB" error={firstError(errors, 'targetCountry')}>
            <Input value={form.targetCountry} maxLength={2} onChange={(e) => set('targetCountry')(e.target.value.toUpperCase())} />
          </FormField>
          <FormField label="Target language" required hint="e.g. en" error={firstError(errors, 'targetLanguage')}>
            <Input value={form.targetLanguage} onChange={(e) => set('targetLanguage')(e.target.value)} />
          </FormField>
        </div>
        <FormField label="Competitors" optional hint="Comma-separated domains (share of voice)." error={firstError(errors, 'competitors')}>
          <Input value={form.competitors} onChange={(e) => set('competitors')(e.target.value)} />
        </FormField>
        <FormField label="Page limit per audit" hint="Default 500 pages." error={firstError(errors, 'maxPages')}>
          <Input type="number" min={1} max={2000} value={form.maxPages} onChange={(e) => set('maxPages')(e.target.value)} />
        </FormField>
      </form>
    </Dialog>
  );
}
