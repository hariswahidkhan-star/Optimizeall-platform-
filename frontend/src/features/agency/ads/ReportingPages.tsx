import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { BadgeDollarSign, Plus, RefreshCw, Upload } from 'lucide-react';
import { useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import {
  Alert,
  Badge,
  Button,
  ButtonLink,
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
  Select,
  Skeleton,
  Sparkline,
  Stat,
  useToast,
  type DataTableColumn,
} from '@/components/ui';
import { api } from '@/lib/api/client';
import { errorMessage } from '@/lib/api/errors';
import { useClientParam } from '../social/shared';
import {
  AD_PLATFORMS,
  PLATFORM_LABELS,
  adsKeys,
  useAdAccounts,
  useAdsClients,
  type AccountDetail,
  type AdAccount,
  type AdPlatform,
  type CampaignRow,
  type Overview,
  type OverviewRow,
} from './api';
import { AdsClientPicker, SourceBadge, count, daysAgo, kpiCells, money, ratio, times } from './shared';
import './ads.css';

function DateRange({ from, to, onFrom, onTo }: { from: string; to: string; onFrom: (v: string) => void; onTo: (v: string) => void }) {
  return (
    <>
      <FormField label="From">
        <Input type="date" value={from} onChange={(e) => onFrom(e.target.value)} />
      </FormField>
      <FormField label="To">
        <Input type="date" value={to} onChange={(e) => onTo(e.target.value)} />
      </FormField>
    </>
  );
}

/** Spend, ROAS and CPA across clients; agency totals in one reporting currency. */
export function AdsOverviewPage() {
  const [from, setFrom] = useState(daysAgo(30));
  const [to, setTo] = useState(daysAgo(1));
  const [currency, setCurrency] = useState('USD');
  const params = { from, to, currency };
  const query = useQuery({
    queryKey: adsKeys.overview(params),
    queryFn: () => api.get<Overview>('/agency/ads/overview', { query: params }),
    placeholderData: keepPreviousData,
  });
  const o = query.data;
  const columns: DataTableColumn<OverviewRow>[] = [
    { id: 'client', header: 'Client', primary: true, cell: (r) => <Link className="ui-link" to={`/agency/ads/accounts?client=${r.clientAccountId}`}>{r.clientName}</Link> },
    { id: 'spend', header: 'Spend', align: 'right', sortable: true, sortValue: (r) => r.totals.spend, cell: (r) => money(r.totals.spend, r.currency) },
    { id: 'conv', header: 'Conversions', align: 'right', cell: (r) => count(r.totals.conversions) },
    { id: 'cpa', header: 'CPA', align: 'right', cell: (r) => money(r.kpis.cpa, r.currency) },
    { id: 'roas', header: 'ROAS', align: 'right', sortable: true, sortValue: (r) => r.kpis.roas ?? -1, cell: (r) => times(r.kpis.roas) },
    {
      id: 'alerts',
      header: 'Open alerts',
      align: 'right',
      cell: (r) => (r.openAlerts > 0 ? <Badge tone="warning">{r.openAlerts}</Badge> : '0'),
    },
    { id: 'fx', header: 'Notes', hideOnMobile: true, cell: (r) => (r.fxMissing.length > 0 ? <span className="ad-warning">Missing FX: {r.fxMissing.join(', ')}</span> : '') },
  ];
  return (
    <>
      <PageHeader title="Paid ads overview" description="Spend, return and cost per acquisition across clients (each client in its own reporting currency)." />
      <div className="ad-toolbar">
        <DateRange from={from} to={to} onFrom={setFrom} onTo={setTo} />
        <FormField label="Agency totals in">
          <Select value={currency} onChange={(e) => setCurrency(e.target.value)} options={['USD', 'GBP', 'EUR', 'AED', 'PKR'].map((c) => ({ value: c, label: c }))} />
        </FormField>
      </div>
      {query.isError ? (
        <ErrorState error={query.error} onRetry={() => void query.refetch()} />
      ) : !o ? (
        <Skeleton height="18rem" />
      ) : (
        <div className="stack">
          {o.fxMissing.length > 0 && (
            <Alert tone="warning" title="Exchange rates missing">
              Amounts in {o.fxMissing.join(', ')} are left out of the agency totals until finance adds the rate.
            </Alert>
          )}
          <div className="ad-stats">
            <Stat label="Spend" value={money(o.totals.spend, o.reportingCurrency)} measurement="Measured" />
            <Stat label="Conversion value" value={money(o.totals.conversionValue, o.reportingCurrency)} measurement="Measured" />
            <Stat label="Blended ROAS" value={times(o.kpis.roas)} measurement="Measured" />
            <Stat label="CPA" value={money(o.kpis.cpa, o.reportingCurrency)} measurement="Measured" />
            <Stat label="CTR" value={ratio(o.kpis.ctr)} measurement="Measured" />
          </div>
          {o.daily.length > 1 && (
            <LineChart
              title="Daily spend"
              description={`Agency spend per day in ${o.reportingCurrency}, ${o.from} to ${o.to}.`}
              labels={o.daily.map((d) => d.date)}
              series={[{ id: 'spend', label: 'Spend', values: o.daily.map((d) => d.spend) }]}
              valueFormatter={(v) => money(v, o.reportingCurrency)}
            />
          )}
          <DataTable
            caption="Clients"
            columns={columns}
            rows={o.clients}
            getRowId={(r) => r.clientAccountId}
            defaultSort={{ id: 'spend', desc: true }}
            emptyState={<EmptyState icon={<BadgeDollarSign />} headingLevel={2} title="No ad data yet" description="Add ad accounts and import or sync their reports." />}
          />
          <p className="ad-muted">{o.definitions}</p>
        </div>
      )}
    </>
  );
}

/** Ad accounts per client, with connection/sync status and last-30-day KPIs. */
export function AdAccountsPage() {
  const [clientId, setClientId] = useClientParam();
  const accounts = useAdAccounts(clientId);
  const [adding, setAdding] = useState(false);
  const toast = useToast();
  const queryClient = useQueryClient();
  const sync = useMutation({
    mutationFn: (a: AdAccount) => api.post<{ outcome: string; message: string }>(`/agency/ads/accounts/${a.id}/sync`),
    onSuccess: (r) => {
      if (r.outcome === 'Synced') toast.success('Synced', r.message);
      else toast.info(r.outcome === 'NotConfigured' ? 'Sync not configured' : 'Sync did not complete', r.message);
      void queryClient.invalidateQueries({ queryKey: adsKeys.all });
    },
    onError: (e) => toast.error('Sync failed', errorMessage(e)),
  });
  const columns: DataTableColumn<AdAccount>[] = [
    {
      id: 'account',
      header: 'Account',
      primary: true,
      cell: (a) => (
        <span className="stack" style={{ gap: 2 }}>
          <Link className="ui-link" to={`/agency/ads/accounts/${a.id}`}>
            {a.name}
          </Link>
          <span className="ad-muted">
            {PLATFORM_LABELS[a.platform]} · {a.externalAccountId} · {a.clientName}
          </span>
        </span>
      ),
    },
    {
      id: 'status',
      header: 'Sync',
      cell: (a) => (
        <span className="stack" style={{ gap: 2 }}>
          <Badge tone={a.status === 'Connected' ? 'success' : a.status === 'Error' ? 'danger' : 'neutral'}>
            {a.status === 'NotConnected' ? (a.syncSupported ? 'Not connected' : 'CSV import only') : a.status}
          </Badge>
          {a.lastSyncMessage && <span className="ad-muted">{a.lastSyncMessage}</span>}
        </span>
      ),
    },
    { id: 'spend', header: 'Spend (30d)', align: 'right', cell: (a) => money(a.last30Days.spend, a.currency) },
    { id: 'roas', header: 'ROAS', align: 'right', cell: (a) => times(a.last30DaysKpis.roas) },
    { id: 'cpa', header: 'CPA', align: 'right', cell: (a) => money(a.last30DaysKpis.cpa, a.currency) },
    { id: 'manager', header: 'Manager', hideOnMobile: true, cell: (a) => a.managerName ?? '—' },
  ];
  return (
    <>
      <PageHeader
        title="Ad accounts"
        description="Accounts per client. Without API credentials, sync reports “not configured” — import the platform's CSV export instead."
        actions={
          <div className="cluster">
            <ButtonLink to="/agency/ads/import" variant="secondary" leadingIcon={<Upload />}>
              Import CSV
            </ButtonLink>
            <Button leadingIcon={<Plus />} onClick={() => setAdding(true)}>
              Add account
            </Button>
          </div>
        }
      />
      <div className="ad-toolbar">
        <AdsClientPicker value={clientId} onChange={setClientId} allowAll />
      </div>
      {accounts.isError ? (
        <ErrorState error={accounts.error} onRetry={() => void accounts.refetch()} />
      ) : (
        <DataTable
          caption="Ad accounts"
          columns={columns}
          rows={accounts.data ?? []}
          getRowId={(a) => a.id}
          rowLabel={(a) => a.name}
          rowActions={(a) => [{ id: 'sync', label: 'Sync now', icon: <RefreshCw />, onSelect: () => sync.mutate(a) }]}
          loading={accounts.isLoading}
          emptyState={<EmptyState icon={<BadgeDollarSign />} headingLevel={2} title="No ad accounts" />}
        />
      )}
      {adding && <AddAccountDialog defaultClient={clientId} onClose={() => setAdding(false)} />}
    </>
  );
}

function AddAccountDialog({ defaultClient, onClose }: { defaultClient?: string; onClose: () => void }) {
  const clients = useAdsClients();
  const queryClient = useQueryClient();
  const [clientId, setClientId] = useState(defaultClient ?? '');
  const [platform, setPlatform] = useState<AdPlatform>('GoogleAds');
  const [externalId, setExternalId] = useState('');
  const [name, setName] = useState('');
  const [currency, setCurrency] = useState('USD');
  const [timeZone, setTimeZone] = useState('UTC');
  const [error, setError] = useState<string | null>(null);
  const save = useMutation({
    mutationFn: () =>
      api.post<AdAccount>('/agency/ads/accounts', {
        clientAccountId: clientId,
        platform,
        externalAccountId: externalId,
        name,
        currency,
        timeZone,
      }),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: adsKeys.all });
      onClose();
    },
    onError: (e) => setError(errorMessage(e)),
  });
  return (
    <Dialog
      open
      onClose={onClose}
      title="Add ad account"
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button loading={save.isPending} disabled={!clientId || !externalId || !name} onClick={() => save.mutate()}>
            Add account
          </Button>
        </>
      }
    >
      <div className="stack">
        {error && <Alert tone="danger">{error}</Alert>}
        <FormField label="Client" required>
          <Select value={clientId} placeholder="Choose a client" onChange={(e) => {
            setClientId(e.target.value);
            const c = clients.data?.find((x) => x.id === e.target.value);
            if (c) {
              setCurrency(c.currency);
              setTimeZone(c.timeZone);
            }
          }} options={(clients.data ?? []).map((c) => ({ value: c.id, label: c.name }))} />
        </FormField>
        <FormField label="Platform" required>
          <Select value={platform} onChange={(e) => setPlatform(e.target.value as AdPlatform)} options={AD_PLATFORMS.map((p) => ({ value: p, label: PLATFORM_LABELS[p] }))} />
        </FormField>
        <FormField label="Account id" required hint="Google customer id (123-456-7890), Meta act_…, TikTok advertiser id…">
          <Input value={externalId} onChange={(e) => setExternalId(e.target.value)} />
        </FormField>
        <FormField label="Name" required>
          <Input value={name} onChange={(e) => setName(e.target.value)} />
        </FormField>
        <div className="ad-stats">
          <FormField label="Account currency" hint="As set in the ad platform">
            <Input value={currency} maxLength={3} onChange={(e) => setCurrency(e.target.value.toUpperCase())} />
          </FormField>
          <FormField label="Time zone">
            <Input value={timeZone} onChange={(e) => setTimeZone(e.target.value)} />
          </FormField>
        </div>
      </div>
    </Dialog>
  );
}

/** One account: KPIs, daily trend and the campaigns table with KPIs and spend sparklines. */
export function AdAccountDetailPage() {
  const { id = '' } = useParams();
  const [from, setFrom] = useState(daysAgo(30));
  const [to, setTo] = useState(daysAgo(1));
  const params = { from, to };
  const query = useQuery({
    queryKey: adsKeys.account(id, params),
    queryFn: () => api.get<AccountDetail>(`/agency/ads/accounts/${id}`, { query: params }),
    placeholderData: keepPreviousData,
  });
  const d = query.data;
  const currency = d?.account.currency ?? 'USD';
  const columns: DataTableColumn<CampaignRow>[] = [
    {
      id: 'name',
      header: 'Campaign',
      primary: true,
      cell: (c) => (
        <span className="stack" style={{ gap: 2 }}>
          <strong>{c.name}</strong>
          <span className="ad-muted">
            {c.status} · {c.objective ?? 'no objective'} · {c.source}
            {!c.namingCompliant && <span className="ad-warning"> · naming off-template</span>}
          </span>
        </span>
      ),
    },
    {
      id: 'trend',
      header: 'Spend trend',
      cell: (c) => <Sparkline values={c.spendSparkline} label={`${c.name}: daily spend over the selected period`} />,
    },
    { id: 'spend', header: 'Spend', align: 'right', sortable: true, sortValue: (c) => c.totals.spend, cell: (c) => money(c.totals.spend, c.currency) },
    { id: 'clicks', header: 'Clicks', align: 'right', cell: (c) => count(c.totals.clicks) },
    { id: 'ctr', header: 'CTR', align: 'right', cell: (c) => kpiCells(c.kpis, c.currency).ctr },
    { id: 'cpc', header: 'CPC', align: 'right', cell: (c) => kpiCells(c.kpis, c.currency).cpc },
    { id: 'conv', header: 'Conv.', align: 'right', cell: (c) => count(c.totals.conversions) },
    { id: 'cpa', header: 'CPA', align: 'right', cell: (c) => kpiCells(c.kpis, c.currency).cpa },
    { id: 'roas', header: 'ROAS', align: 'right', sortable: true, sortValue: (c) => c.kpis.roas ?? -1, cell: (c) => kpiCells(c.kpis, c.currency).roas },
    { id: 'source', header: 'Source', cell: (c) => <SourceBadge label={c.sourceLabel} /> },
  ];
  return (
    <>
      <PageHeader
        title={d?.account.name ?? 'Ad account'}
        breadcrumbs={[{ label: 'Ad accounts', to: '/agency/ads/accounts' }, { label: d?.account.name ?? 'Account' }]}
        meta={d && <Badge>{PLATFORM_LABELS[d.account.platform]} · {d.account.currency}</Badge>}
        actions={
          <ButtonLink to={`/agency/ads/import?account=${id}`} variant="secondary" leadingIcon={<Upload />}>
            Import CSV
          </ButtonLink>
        }
      />
      <div className="ad-toolbar">
        <DateRange from={from} to={to} onFrom={setFrom} onTo={setTo} />
      </div>
      {query.isError ? (
        <ErrorState error={query.error} onRetry={() => void query.refetch()} />
      ) : !d ? (
        <Skeleton height="20rem" />
      ) : (
        <div className="stack">
          <div className="cluster">
            <span className="ad-muted">Data source:</span> <SourceBadge label={d.sourceLabel} />
            {d.account.lastSyncedAt && (
              <span className="ad-muted">
                Last synced <DateTime value={d.account.lastSyncedAt} format="relative" />
              </span>
            )}
          </div>
          <div className="ad-stats">
            <Stat label="Spend" value={money(d.totals.spend, currency)} measurement="Measured" />
            <Stat label="Impressions" value={count(d.totals.impressions)} measurement="Measured" />
            <Stat label="CTR" value={ratio(d.kpis.ctr)} measurement="Measured" />
            <Stat label="CPC" value={money(d.kpis.cpc, currency)} measurement="Measured" />
            <Stat label="CPM" value={money(d.kpis.cpm, currency)} measurement="Measured" />
            <Stat label="Conversions" value={count(d.totals.conversions)} measurement="Measured" />
            <Stat label="CPA" value={money(d.kpis.cpa, currency)} measurement="Measured" />
            <Stat label="ROAS" value={times(d.kpis.roas)} measurement="Measured" />
            <Stat label="Conversion rate" value={ratio(d.kpis.conversionRate)} measurement="Measured" />
            <Stat label="Frequency" value={d.kpis.frequency == null ? '—' : d.kpis.frequency.toFixed(2)} measurement="Estimated" hint="Impressions ÷ summed daily reach" />
          </div>
          {d.daily.length > 1 && (
            <Card as="section" aria-labelledby="ad-daily">
              <CardHeader title="Daily spend and conversion value" titleId="ad-daily" />
              <CardBody>
                <LineChart
                  title="Daily spend and conversion value"
                  description={`Spend and conversion value per day in ${currency}.`}
                  labels={d.daily.map((p) => p.date)}
                  series={[
                    { id: 'spend', label: 'Spend', values: d.daily.map((p) => p.spend) },
                    { id: 'value', label: 'Conversion value', values: d.daily.map((p) => p.conversionValue) },
                  ]}
                  valueFormatter={(v) => money(v, currency)}
                />
              </CardBody>
            </Card>
          )}
          <DataTable
            caption="Campaigns"
            columns={columns}
            rows={d.campaigns}
            getRowId={(c) => c.id}
            defaultSort={{ id: 'spend', desc: true }}
            emptyState={<EmptyState compact icon={<BadgeDollarSign />} headingLevel={3} title="No campaigns yet" />}
          />
        </div>
      )}
    </>
  );
}
