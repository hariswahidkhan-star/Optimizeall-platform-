import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ArrowDown, ArrowUp, History, Plus, RefreshCw, Upload } from 'lucide-react';
import { useRef, useState, type FormEvent } from 'react';
import {
  Alert,
  Badge,
  BarChart,
  Button,
  Card,
  CardBody,
  CardHeader,
  DataTable,
  Dialog,
  FormField,
  Input,
  LineChart,
  Select,
  Stat,
  Textarea,
  useToast,
  type DataTableColumn,
} from '@/components/ui';
import { api } from '@/lib/api/client';
import { errorMessage } from '@/lib/api/errors';
import {
  seoKeys,
  type ImportResult,
  type Keyword,
  type KeywordIntent,
  type ProviderRun,
  type RankingsOverview,
  type RankPoint,
  type SearchPerformance,
  type Site,
} from './api';
import { fieldErrors, firstError, fmt, lines } from './common';

const intents: KeywordIntent[] = ['Unknown', 'Informational', 'Navigational', 'Commercial', 'Transactional'];

export function KeywordsPanel({ site }: { site: Site }) {
  const queryClient = useQueryClient();
  const toast = useToast();
  const [adding, setAdding] = useState(false);
  const [historyFor, setHistoryFor] = useState<Keyword | null>(null);
  const [notice, setNotice] = useState<{ tone: 'info' | 'warning' | 'success'; title: string; body?: string } | null>(null);
  const keywords = useQuery({ queryKey: seoKeys.keywords(site.id), queryFn: () => api.get<Keyword[]>(`/agency/seo/sites/${site.id}/keywords`) });
  const overview = useQuery({
    queryKey: seoKeys.rankings(site.id),
    queryFn: () => api.get<RankingsOverview>(`/agency/seo/sites/${site.id}/rankings`),
  });
  const search = useQuery({ queryKey: seoKeys.search(site.id), queryFn: () => api.get<SearchPerformance>(`/agency/seo/sites/${site.id}/search-console`) });
  const refreshAll = () => void queryClient.invalidateQueries({ queryKey: ['agency', 'seo', 'site', site.id] });

  const refresh = useMutation({
    mutationFn: () => api.post<ProviderRun>(`/agency/seo/sites/${site.id}/ranks/refresh`),
    onSuccess: (r) => {
      setNotice(
        r.outcome === 'NotConfigured'
          ? { tone: 'warning', title: 'Rank tracking provider not configured', body: r.message ?? undefined }
          : r.outcome === 'Error'
            ? { tone: 'warning', title: 'The provider returned an error', body: r.message ?? undefined }
            : { tone: 'success', title: `${r.checked} keyword(s) checked`, body: r.message ?? undefined },
      );
      refreshAll();
    },
    onError: (err) => toast.error('Could not refresh rankings', errorMessage(err)),
  });
  const sync = useMutation({
    mutationFn: () => api.post<ProviderRun>(`/agency/seo/sites/${site.id}/search-console/sync`),
    onSuccess: (r) => {
      setNotice(
        r.outcome === 'Ok'
          ? { tone: 'success', title: `${r.checked} Search Console row(s) imported` }
          : { tone: 'warning', title: r.outcome === 'NotConfigured' ? 'Search Console not connected' : 'Search Console error', body: r.message ?? undefined },
      );
      refreshAll();
    },
  });

  const o = overview.data;
  const columns: DataTableColumn<Keyword>[] = [
    {
      id: 'keyword',
      header: 'Keyword',
      primary: true,
      sortable: true,
      sortValue: (k) => k.keyword,
      cell: (k) => (
        <span className="stack seo-stack-xs">
          <span className="seo-strong">{k.keyword}</span>
          <span className="text-small text-muted">
            {k.intent !== 'Unknown' && `${k.intent} · `}
            {k.tags.join(', ')}
          </span>
        </span>
      ),
    },
    { id: 'position', header: 'Position', sortable: true, sortValue: (k) => k.position ?? 999, align: 'right', cell: (k) => fmt.position(k.position) },
    { id: 'change', header: 'Change', align: 'right', cell: (k) => <Change value={k.change} /> },
    { id: 'volume', header: 'Volume', align: 'right', hideOnMobile: true, sortable: true, sortValue: (k) => k.searchVolume ?? -1, cell: (k) => fmt.number(k.searchVolume) },
    {
      id: 'features',
      header: 'SERP features',
      hideOnMobile: true,
      cell: (k) => (k.serpFeatures.length === 0 ? '—' : k.serpFeatures.map((f) => <Badge key={f} size="sm">{f.replace(/_/g, ' ')}</Badge>)),
    },
  ];

  return (
    <div className="stack">
      {notice && (
        <Alert tone={notice.tone} title={notice.title} onDismiss={() => setNotice(null)}>
          {notice.body}
        </Alert>
      )}
      <div className="seo-toolbar">
        <Button leadingIcon={<Plus />} onClick={() => setAdding(true)}>
          Add keywords
        </Button>
        <Button variant="secondary" leadingIcon={<RefreshCw />} loading={refresh.isPending} onClick={() => refresh.mutate()}>
          Check positions now
        </Button>
        <CsvImportButton
          label="Import ranks CSV"
          path={`/agency/seo/sites/${site.id}/ranks/import`}
          hint="Columns: keyword, position, date (or pick a date), url, domain, serp_features, search_volume. Search Console query exports work too."
          onDone={(r) => {
            setNotice({ tone: 'success', title: `Import finished: ${r.created} new, ${r.updated} updated, ${r.unchanged} unchanged`, body: r.errors.slice(0, 5).join(' · ') || undefined });
            refreshAll();
          }}
        />
        <CsvImportButton
          label="Import Search Console CSV"
          path={`/agency/seo/sites/${site.id}/search-console/import`}
          hint="A Search Console Performance export (Queries or Pages): query, clicks, impressions, CTR, position."
          onDone={(r) => {
            setNotice({ tone: 'success', title: `Search Console import: ${r.created} new, ${r.updated} updated rows` });
            refreshAll();
          }}
        />
        <Button variant="ghost" onClick={() => sync.mutate()} loading={sync.isPending}>
          Sync Search Console
        </Button>
      </div>

      <div className="grid-auto seo-stats">
        <Stat label="Tracked keywords" value={o?.trackedKeywords ?? '—'} measurement="Count" loading={overview.isLoading} />
        <Stat label="Top 3" value={o?.distribution.top3 ?? '—'} measurement="Count" loading={overview.isLoading} />
        <Stat label="Top 10" value={o?.distribution.top10 ?? '—'} measurement="Count" loading={overview.isLoading} />
        <Stat
          label="Average position"
          value={o?.averagePosition ?? '—'}
          measurement="Measured"
          loading={overview.isLoading}
          delta={o?.averagePositionChange != null ? { value: o.averagePositionChange, display: `${o.averagePositionChange > 0 ? '+' : ''}${o.averagePositionChange}`, label: 'vs. 30 days ago', positiveIsGood: true } : undefined}
        />
        <Stat label="Clicks (28 days)" value={fmt.number(search.data?.clicks)} measurement="Measured" loading={search.isLoading} hint="From Search Console" />
      </div>

      {o && o.trend.length > 1 && (
        <Card>
          <CardBody>
            <LineChart
              title="Average position over time"
              description="Average organic position of ranking keywords per day (lower is better)."
              labels={o.trend.map((t) => t.date)}
              series={[
                { id: 'avg', label: 'Average position', values: o.trend.map((t) => t.averagePosition ?? 0) },
                { id: 'top10', label: 'Keywords in top 10', values: o.trend.map((t) => t.top10) },
              ]}
            />
          </CardBody>
        </Card>
      )}

      <div className="seo-grid-2">
        {o && (
          <Card>
            <CardHeader title="Ranking distribution" headingLevel={3} />
            <CardBody>
              <BarChart
                title="Keywords by position bucket"
                description="Number of tracked keywords in each organic position range on the latest day."
                data={[
                  { label: '1–3', value: o.distribution.top3 },
                  { label: '4–10', value: o.distribution.top10 - o.distribution.top3 },
                  { label: '11–20', value: o.distribution.top20 - o.distribution.top10 },
                  { label: '21–100', value: o.distribution.top100 - o.distribution.top20 },
                  { label: 'Not ranking', value: o.distribution.notRanking },
                ]}
                valueLabel="Keywords"
                height={200}
              />
            </CardBody>
          </Card>
        )}
        {o && (
          <Card>
            <CardHeader title="Share of voice" description="CTR-weighted visibility vs. competitors on the latest day." headingLevel={3} />
            <CardBody>
              {o.shareOfVoice.length === 0 ? (
                <p className="text-muted">Add competitor positions (provider or CSV with a domain column) to compare.</p>
              ) : (
                <ul className="seo-bars">
                  {o.shareOfVoice.map((s) => (
                    <li key={s.domain}>
                      <span>{s.domain}</span>
                      <span className="seo-bar" aria-hidden="true">
                        <span style={{ width: `${Math.round(s.share * 100)}%` }} />
                      </span>
                      <span className="tabular">{fmt.percent(s.share)}</span>
                    </li>
                  ))}
                </ul>
              )}
            </CardBody>
          </Card>
        )}
      </div>

      {o && (o.winners.length > 0 || o.losers.length > 0) && (
        <div className="seo-grid-2">
          <MoverList title="Biggest gains" movers={o.winners} />
          <MoverList title="Biggest drops" movers={o.losers} />
        </div>
      )}

      <DataTable
        caption="Tracked keywords"
        columns={columns}
        rows={keywords.data ?? []}
        getRowId={(k) => k.id}
        rowLabel={(k) => k.keyword}
        loading={keywords.isLoading}
        rowActions={(k) => [{ id: 'history', label: 'Position history', icon: <History />, onSelect: () => setHistoryFor(k) }]}
      />
      {adding && <AddKeywordsDialog siteId={site.id} onClose={() => setAdding(false)} onSaved={refreshAll} />}
      {historyFor && <HistoryDialog keyword={historyFor} onClose={() => setHistoryFor(null)} />}
    </div>
  );
}

function Change({ value }: { value: number | null }) {
  if (value === null || value === 0) return <span className="text-muted">—</span>;
  const up = value > 0;
  return (
    <span className={up ? 'seo-up' : 'seo-down'}>
      {up ? <ArrowUp aria-hidden="true" /> : <ArrowDown aria-hidden="true" />}
      <span className="visually-hidden">{up ? 'up' : 'down'} </span>
      {Math.abs(value)}
    </span>
  );
}

function MoverList({ title, movers }: { title: string; movers: RankingsOverview['winners'] }) {
  return (
    <Card>
      <CardHeader title={title} headingLevel={3} />
      <CardBody>
        {movers.length === 0 ? (
          <p className="text-muted">No movement.</p>
        ) : (
          <ul className="seo-movers">
            {movers.map((m) => (
              <li key={m.keywordId}>
                <span>{m.keyword}</span>
                <span className="tabular text-muted">
                  {fmt.position(m.previous)} → {fmt.position(m.current)}
                </span>
                <Change value={m.change} />
              </li>
            ))}
          </ul>
        )}
      </CardBody>
    </Card>
  );
}

function AddKeywordsDialog({ siteId, onClose, onSaved }: { siteId: string; onClose: () => void; onSaved: () => void }) {
  const toast = useToast();
  const [text, setText] = useState('');
  const [intent, setIntent] = useState<KeywordIntent>('Unknown');
  const [tags, setTags] = useState('');
  const save = useMutation({
    mutationFn: () =>
      api.post<Keyword[]>(`/agency/seo/sites/${siteId}/keywords`, {
        keywords: lines(text),
        intent,
        tags: tags.split(',').map((t) => t.trim()).filter(Boolean),
      }),
    onSuccess: (added) => {
      toast.success(`${added.length} keyword(s) added`);
      onSaved();
      onClose();
    },
  });
  const errors = fieldErrors(save.error);
  return (
    <Dialog
      open
      onClose={onClose}
      title="Add keywords"
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" form="seo-add-keywords" loading={save.isPending} disabled={lines(text).length === 0}>
            Add
          </Button>
        </>
      }
    >
      <form
        id="seo-add-keywords"
        className="stack"
        onSubmit={(e: FormEvent) => {
          e.preventDefault();
          save.mutate();
        }}
      >
        {save.isError && <Alert tone="danger" title="Could not add keywords">{errorMessage(save.error)}</Alert>}
        <FormField label="Keywords" hint="One per line. Existing keywords are skipped." error={firstError(errors, 'keywords')}>
          <Textarea value={text} rows={6} onChange={(e) => setText(e.target.value)} />
        </FormField>
        <FormField label="Search intent">
          <Select value={intent} onChange={(e) => setIntent(e.target.value as KeywordIntent)} options={intents.map((i) => ({ value: i, label: i }))} />
        </FormField>
        <FormField label="Tags" optional hint="Comma-separated, e.g. priority, blog">
          <Input value={tags} onChange={(e) => setTags(e.target.value)} />
        </FormField>
      </form>
    </Dialog>
  );
}

function HistoryDialog({ keyword, onClose }: { keyword: Keyword; onClose: () => void }) {
  const history = useQuery({
    queryKey: ['agency', 'seo', 'keyword', keyword.id, 'history'],
    queryFn: () => api.get<RankPoint[]>(`/agency/seo/keywords/${keyword.id}/history`),
  });
  const points = history.data ?? [];
  const domains = [...new Set(points.map((p) => p.domain))].slice(0, 3);
  const dates = [...new Set(points.map((p) => p.date))].sort();
  return (
    <Dialog open onClose={onClose} title={`Position history: ${keyword.keyword}`} size="lg">
      {history.isLoading ? (
        <p>Loading…</p>
      ) : dates.length < 2 ? (
        <p className="text-muted">At least two days of positions are needed for a chart.</p>
      ) : (
        <LineChart
          title={`Positions for “${keyword.keyword}”`}
          description="Organic position per day (lower is better); 101 means not ranking."
          labels={dates}
          series={domains.map((d) => ({
            id: d,
            label: d,
            values: dates.map((date) => points.find((p) => p.domain === d && p.date === date)?.position ?? 101),
          }))}
        />
      )}
    </Dialog>
  );
}

/** A file button that uploads a CSV (with an optional date for files without a date column). */
export function CsvImportButton({
  label,
  path,
  hint,
  onDone,
  withDate = true,
}: {
  label: string;
  path: string;
  hint: string;
  onDone: (result: ImportResult) => void;
  withDate?: boolean;
}) {
  const [open, setOpen] = useState(false);
  const [file, setFile] = useState<File | null>(null);
  const [date, setDate] = useState('');
  const inputRef = useRef<HTMLInputElement>(null);
  const upload = useMutation({
    mutationFn: () => {
      const form = new FormData();
      form.append('file', file!);
      if (withDate && date) form.append('date', date);
      return api.upload<ImportResult>(path, form);
    },
    onSuccess: (r) => {
      onDone(r);
      setOpen(false);
      setFile(null);
    },
  });
  return (
    <>
      <Button variant="secondary" leadingIcon={<Upload />} onClick={() => setOpen(true)}>
        {label}
      </Button>
      <Dialog
        open={open}
        onClose={() => setOpen(false)}
        title={label}
        description={hint}
        initialFocusRef={inputRef}
        footer={
          <>
            <Button variant="secondary" onClick={() => setOpen(false)}>
              Cancel
            </Button>
            <Button onClick={() => upload.mutate()} loading={upload.isPending} disabled={!file}>
              Import
            </Button>
          </>
        }
      >
        <div className="stack">
          {upload.isError && <Alert tone="danger" title="Import failed">{errorMessage(upload.error)}</Alert>}
          <FormField label="CSV file">
            <Input ref={inputRef} type="file" accept=".csv,text/csv" onChange={(e) => setFile(e.target.files?.[0] ?? null)} />
          </FormField>
          {withDate && (
            <FormField label="Date for rows without a date column" optional>
              <Input type="date" value={date} onChange={(e) => setDate(e.target.value)} />
            </FormField>
          )}
        </div>
      </Dialog>
    </>
  );
}
