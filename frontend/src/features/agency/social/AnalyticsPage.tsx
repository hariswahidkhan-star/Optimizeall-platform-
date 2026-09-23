import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { BarChart3, Upload } from 'lucide-react';
import { useState } from 'react';
import {
  Alert,
  BarChart,
  Button,
  Card,
  CardBody,
  CardHeader,
  DataTable,
  DateTime,
  Dialog,
  EmptyState,
  ErrorState,
  FormField,
  Input,
  LineChart,
  PageHeader,
  RadioGroup,
  Select,
  Skeleton,
  Stat,
  Textarea,
  type DataTableColumn,
} from '@/components/ui';
import { SafeExternalLink } from '@/components/SafeExternalLink';
import { api } from '@/lib/api/client';
import { errorMessage } from '@/lib/api/errors';
import { formatNumber } from '@/lib/format/money';
import {
  NETWORK_LABELS,
  socialKeys,
  useProfiles,
  type AnalyticsResponse,
  type BestTimes,
  type ImportPreview,
  type ImportResult,
  type TopPost,
} from './api';
import { ClientPicker, NetworkChip, SourceLabel, percent, useClientParam } from './shared';
import './social.css';

const iso = (d: Date) => d.toISOString().slice(0, 10);

/** Social analytics: KPIs with their source label, trends, top posts, best times and CSV import. */
export function AnalyticsPage() {
  const [clientId, setClientId] = useClientParam();
  const [to, setTo] = useState(() => iso(new Date()));
  const [from, setFrom] = useState(() => iso(new Date(Date.now() - 29 * 86_400_000)));
  const [importing, setImporting] = useState(false);
  const params = { from, to };
  const query = useQuery({
    queryKey: socialKeys.analytics(clientId ?? '', params),
    queryFn: () => api.get<AnalyticsResponse>(`/agency/social/clients/${clientId}/analytics`, { query: params }),
    enabled: !!clientId,
  });
  const bestTimes = useQuery({
    queryKey: socialKeys.bestTimes(clientId ?? ''),
    queryFn: () => api.get<BestTimes>(`/agency/social/clients/${clientId}/best-times`),
    enabled: !!clientId,
  });
  const k = query.data?.kpis;
  const measurement = k?.sourceLabel === 'Measured' ? 'Measured' : 'Estimated';

  const topColumns: DataTableColumn<TopPost>[] = [
    {
      id: 'post',
      header: 'Post',
      primary: true,
      cell: (p) => (
        <span className="stack" style={{ gap: 2 }}>
          <span>
            <NetworkChip network={p.network} /> {p.title ?? p.postKey}
          </span>
          {p.url && <SafeExternalLink href={p.url}>Open on {NETWORK_LABELS[p.network]}</SafeExternalLink>}
        </span>
      ),
    },
    { id: 'published', header: 'Published', hideOnMobile: true, cell: (p) => (p.publishedAt ? <DateTime value={p.publishedAt} format="date" /> : '—') },
    { id: 'impressions', header: 'Impressions', align: 'right', cell: (p) => formatNumber(p.impressions) },
    { id: 'engagements', header: 'Engagements', align: 'right', cell: (p) => formatNumber(p.engagements) },
    { id: 'er', header: 'Eng. rate', align: 'right', cell: (p) => percent(p.engagementRate) },
    { id: 'source', header: 'Source', cell: (p) => <SourceLabel label={p.sourceLabel} /> },
  ];

  return (
    <>
      <PageHeader
        title="Social analytics"
        description="Every figure carries its source: Measured (network API or an export from the network) or Manual (typed in)."
        actions={
          clientId && (
            <Button variant="secondary" leadingIcon={<Upload />} onClick={() => setImporting(true)}>
              Import CSV
            </Button>
          )
        }
      />
      <div className="sm-toolbar">
        <ClientPicker value={clientId} onChange={setClientId} />
        <FormField label="From">
          <Input type="date" value={from} onChange={(e) => setFrom(e.target.value)} />
        </FormField>
        <FormField label="To">
          <Input type="date" value={to} onChange={(e) => setTo(e.target.value)} />
        </FormField>
      </div>
      {!clientId ? (
        <EmptyState icon={<BarChart3 />} title="Choose a client" />
      ) : query.isError ? (
        <ErrorState error={query.error} onRetry={() => void query.refetch()} />
      ) : !k ? (
        <Skeleton height="20rem" />
      ) : (
        <div className="stack">
          <div className="cluster">
            <span className="sm-muted">Data source:</span> <SourceLabel label={k.sourceLabel} />
          </div>
          <div className="sm-grid-stats">
            <Stat label="Impressions" value={formatNumber(k.totals.impressions)} measurement={measurement} />
            <Stat label="Reach" value={formatNumber(k.totals.reach)} measurement={measurement} hint="Sum of daily reach (upper bound)" />
            <Stat label="Engagements" value={formatNumber(k.totals.engagements)} measurement={measurement} />
            <Stat label="Engagement rate" value={percent(k.totals.engagementRate, 2)} measurement={measurement} />
            <Stat label="Clicks" value={formatNumber(k.totals.clicks)} measurement={measurement} />
            <Stat
              label="Followers growth"
              value={k.totals.followersGrowth == null ? '—' : formatNumber(k.totals.followersGrowth, { signDisplay: 'exceptZero' })}
              measurement={measurement}
            />
            <Stat label="Posts published" value={formatNumber(k.postsPublished)} measurement="Count" />
          </div>
          {(query.data?.series.length ?? 0) > 1 && (
            <div className="sm-grid-2">
              <LineChart
                title="Impressions and engagements"
                description={`Daily impressions and engagements, ${from} to ${to} (${k.sourceLabel}).`}
                labels={query.data!.series.map((s) => s.date)}
                series={[
                  { id: 'imp', label: 'Impressions', values: query.data!.series.map((s) => s.impressions) },
                  { id: 'eng', label: 'Engagements', values: query.data!.series.map((s) => s.engagements) },
                ]}
                valueFormatter={(v) => formatNumber(v)}
              />
              <LineChart
                title="Followers"
                description="Total followers across profiles per day."
                labels={query.data!.series.map((s) => s.date)}
                series={[{ id: 'f', label: 'Followers', values: query.data!.series.map((s) => s.followers) }]}
                valueFormatter={(v) => formatNumber(v)}
              />
            </div>
          )}
          <Card as="section" aria-labelledby="sm-top-posts">
            <CardHeader title="Top posts" titleId="sm-top-posts" />
            <CardBody>
              <DataTable
                caption="Top posts by engagements"
                columns={topColumns}
                rows={query.data?.topPosts ?? []}
                getRowId={(p) => `${p.postKey}-${p.profileHandle}`}
                emptyState={<EmptyState compact icon={<BarChart3 />} headingLevel={3} title="No post metrics in this period" />}
              />
            </CardBody>
          </Card>
          <Card as="section" aria-labelledby="sm-best">
            <CardHeader title="Best posting times" titleId="sm-best" description={bestTimes.data?.note} />
            <CardBody className="stack">
              {bestTimes.data?.enoughData ? (
                <BarChart
                  title="Engagement rate by posting slot"
                  description="Average engagement rate of this client's posts by local weekday and hour (at least 3 posts per slot)."
                  data={bestTimes.data.slots.map((s) => ({ label: `${s.day.slice(0, 3)} ${String(s.hour).padStart(2, '0')}:00`, value: (s.avgEngagementRate ?? 0) * 100 }))}
                  valueLabel="Engagement rate (%)"
                  valueFormatter={(v) => `${v.toFixed(2)}%`}
                />
              ) : (
                <ul className="sm-issues">
                  {(bestTimes.data?.presetGuidance ?? []).map((b) => (
                    <li key={b.network}>
                      {NETWORK_LABELS[b.network]}: {b.times.join(', ')}
                    </li>
                  ))}
                </ul>
              )}
            </CardBody>
          </Card>
          <p className="sm-muted">{k.definitions}</p>
        </div>
      )}
      {importing && clientId && <ImportDialog clientId={clientId} onClose={() => setImporting(false)} />}
    </>
  );
}

function ImportDialog({ clientId, onClose }: { clientId: string; onClose: () => void }) {
  const profiles = useProfiles(clientId);
  const queryClient = useQueryClient();
  const [profileId, setProfileId] = useState('');
  const [kind, setKind] = useState('profile-daily');
  const [source, setSource] = useState('PlatformExport');
  const [csv, setCsv] = useState('');
  const [fileName, setFileName] = useState('import.csv');
  const [preview, setPreview] = useState<ImportPreview | null>(null);
  const [mapping, setMapping] = useState<Record<string, string>>({});
  const [result, setResult] = useState<ImportResult | null>(null);
  const [error, setError] = useState<string | null>(null);
  const target = profileId || profiles.data?.[0]?.id || '';
  const doPreview = useMutation({
    mutationFn: () => api.post<ImportPreview>(`/agency/social/profiles/${target}/metrics/import/preview`, { kind, fileName, csv }),
    onSuccess: (p) => {
      setPreview(p);
      setMapping(p.suggestedMapping);
    },
    onError: (e) => setError(errorMessage(e)),
  });
  const doImport = useMutation({
    mutationFn: () =>
      api.post<ImportResult>(`/agency/social/profiles/${target}/metrics/import`, {
        kind,
        fileName,
        csv,
        mapping: Object.fromEntries(Object.entries(mapping).filter(([, header]) => header)),
        source,
      }),
    onSuccess: (r) => {
      setResult(r);
      void queryClient.invalidateQueries({ queryKey: ['social', 'analytics'] });
    },
    onError: (e) => setError(errorMessage(e)),
  });
  return (
    <Dialog
      open
      size="lg"
      onClose={onClose}
      title="Import social metrics"
      description="Map the columns of a network export (or your own spreadsheet). Re-importing the same days updates them instead of adding duplicates."
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Close
          </Button>
          {!preview ? (
            <Button disabled={!csv || !target} loading={doPreview.isPending} onClick={() => doPreview.mutate()}>
              Preview
            </Button>
          ) : (
            <Button disabled={!!result} loading={doImport.isPending} onClick={() => doImport.mutate()}>
              Import
            </Button>
          )}
        </>
      }
    >
      <div className="stack">
        {error && <Alert tone="danger">{error}</Alert>}
        {result && (
          <Alert tone="success" title="Imported">
            {result.rowsImported} new, {result.rowsUpdated} updated, {result.rowsSkipped} skipped — labelled {result.sourceLabel}.
            {result.errors.length > 0 && (
              <ul className="sm-issues">
                {result.errors.slice(0, 10).map((e) => (
                  <li key={e}>{e}</li>
                ))}
              </ul>
            )}
          </Alert>
        )}
        <FormField label="Profile">
          <Select value={target} onChange={(e) => setProfileId(e.target.value)} options={(profiles.data ?? []).map((p) => ({ value: p.id, label: `${NETWORK_LABELS[p.network]} @${p.handle}` }))} />
        </FormField>
        <RadioGroup
          legend="What the file contains"
          value={kind}
          onChange={setKind}
          options={[
            { value: 'profile-daily', label: 'Daily profile metrics' },
            { value: 'post', label: 'Per-post metrics' },
          ]}
        />
        <RadioGroup
          legend="Where the numbers come from"
          value={source}
          onChange={setSource}
          options={[
            { value: 'PlatformExport', label: 'Exported from the network (labelled Measured)' },
            { value: 'Manual', label: 'Typed in by hand (labelled Manual)' },
          ]}
        />
        <FormField label="File">
          <Input
            type="file"
            accept=".csv,text/csv"
            onChange={(e) => {
              const file = e.target.files?.[0];
              if (!file) return;
              setFileName(file.name);
              void file.text().then(setCsv);
              setPreview(null);
            }}
          />
        </FormField>
        <FormField label="…or paste CSV" optional>
          <Textarea rows={4} value={csv} onChange={(e) => { setCsv(e.target.value); setPreview(null); }} />
        </FormField>
        {preview && (
          <fieldset className="stack" style={{ border: 0, padding: 0 }}>
            <legend className="sm-h3">Column mapping ({preview.rowCount} rows)</legend>
            {preview.targetFields.map((field) => (
              <FormField key={field} label={`${field}${preview.requiredFields.includes(field) ? ' (required)' : ''}`}>
                <Select
                  value={mapping[field] ?? ''}
                  placeholder="Not in file"
                  onChange={(e) => setMapping((m) => ({ ...m, [field]: e.target.value }))}
                  options={preview.headers.map((h) => ({ value: h, label: h }))}
                />
              </FormField>
            ))}
          </fieldset>
        )}
      </div>
    </Dialog>
  );
}
