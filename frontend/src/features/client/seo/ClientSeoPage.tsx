import { useQuery } from '@tanstack/react-query';
import { Search } from 'lucide-react';
import {
  BarChart,
  Badge,
  Card,
  CardBody,
  CardHeader,
  DataTable,
  DateTime,
  EmptyState,
  ErrorState,
  PageHeader,
  Skeleton,
  Stat,
  type DataTableColumn,
} from '@/components/ui';
import { api } from '@/lib/api/client';
import type { IsoDateTime } from '@/lib/api/types';
import './clientSeo.css';

export interface ClientSiteKpi {
  siteId: string;
  name: string;
  domain: string;
  healthScore: number | null;
  healthScoreChange: number | null;
  lastAuditAt: IsoDateTime | null;
  trackedKeywords: number;
  top3: number;
  top10: number;
  averagePosition: number | null;
  averagePositionChange: number | null;
  clicks: number;
  impressions: number;
  liveBacklinks: number;
  lostBacklinks: number;
  shareOfVoice: number | null;
}

export interface ClientSeoOverview {
  from: string;
  to: string;
  organizations: {
    clientAccountId: string;
    clientName: string;
    kpis: {
      sites: ClientSiteKpi[];
      leads: {
        views: number;
        submissions: number;
        conversionRate: number | null;
        pages: { pageId: string; name: string; slug: string; views: number; submissions: number; conversionRate: number | null }[];
        daily: { date: string; submissions: number }[];
      };
    };
    topKeywords: { keyword: string; position: number | null; change: number | null; siteName: string }[];
  }[];
}

const pct = new Intl.NumberFormat('en', { style: 'percent', maximumFractionDigits: 1 });
const num = new Intl.NumberFormat('en', { maximumFractionDigits: 1 });

function healthTone(score: number | null): 'success' | 'warning' | 'danger' | 'neutral' {
  if (score === null) return 'neutral';
  return score >= 80 ? 'success' : score >= 50 ? 'warning' : 'danger';
}

/** Client portal: search visibility and landing-page leads (last 30 days). */
export function ClientSeoPage() {
  const query = useQuery({ queryKey: ['client', 'seo', 'overview'], queryFn: () => api.get<ClientSeoOverview>('/client/seo/overview') });

  return (
    <>
      <PageHeader title="SEO & leads" description="How your websites perform in search and how many leads your landing pages bring in." />
      {query.isError ? (
        <ErrorState error={query.error} onRetry={() => void query.refetch()} />
      ) : !query.data ? (
        <Skeleton height="20rem" />
      ) : query.data.organizations.every((o) => o.kpis.sites.length === 0 && o.kpis.leads.pages.length === 0) ? (
        <EmptyState icon={<Search />} title="Nothing to report yet" description="Your agency hasn’t set up SEO tracking or landing pages for you yet." />
      ) : (
        <div className="stack">
          <p className="text-small text-muted">
            Period: <DateTime value={query.data.from} format="date" /> – <DateTime value={query.data.to} format="date" />
          </p>
          {query.data.organizations.map((org) => (
            <section key={org.clientAccountId} className="stack" aria-labelledby={`cseo-${org.clientAccountId}`}>
              {query.data.organizations.length > 1 && <h2 id={`cseo-${org.clientAccountId}`}>{org.clientName}</h2>}
              {query.data.organizations.length === 1 && (
                <h2 id={`cseo-${org.clientAccountId}`} className="visually-hidden">
                  {org.clientName}
                </h2>
              )}
              {org.kpis.sites.map((site) => (
                <Card key={site.siteId} as="section" aria-labelledby={`cseo-site-${site.siteId}`}>
                  <CardHeader
                    title={site.name}
                    titleId={`cseo-site-${site.siteId}`}
                    headingLevel={3}
                    description={site.domain}
                    actions={
                      site.healthScore !== null ? <Badge tone={healthTone(site.healthScore)}>Health {site.healthScore}/100</Badge> : <Badge tone="neutral">No audit yet</Badge>
                    }
                  />
                  <CardBody>
                    <div className="grid-auto cseo-stats">
                      <Stat
                        label="Site health"
                        value={site.healthScore === null ? '—' : `${site.healthScore}/100`}
                        measurement="Measured"
                        delta={site.healthScoreChange ? { value: site.healthScoreChange, display: `${site.healthScoreChange > 0 ? '+' : ''}${site.healthScoreChange}`, label: 'since previous audit' } : undefined}
                      />
                      <Stat label="Keywords in top 10" value={`${site.top10} of ${site.trackedKeywords}`} measurement="Count" hint={`${site.top3} in the top 3`} />
                      <Stat
                        label="Average position"
                        value={site.averagePosition === null ? '—' : num.format(site.averagePosition)}
                        measurement="Measured"
                        delta={
                          site.averagePositionChange
                            ? { value: site.averagePositionChange, display: `${site.averagePositionChange > 0 ? '+' : ''}${num.format(site.averagePositionChange)}`, label: 'positions', positiveIsGood: true }
                            : undefined
                        }
                        hint="Lower is better"
                      />
                      <Stat label="Search clicks" value={num.format(site.clicks)} measurement="Measured" hint={`${num.format(site.impressions)} impressions`} />
                      <Stat label="Live backlinks" value={site.liveBacklinks} measurement="Count" hint={site.lostBacklinks ? `${site.lostBacklinks} lost` : undefined} />
                      {site.shareOfVoice !== null && <Stat label="Share of voice" value={pct.format(site.shareOfVoice)} measurement="Estimated" />}
                    </div>
                    {site.lastAuditAt && (
                      <p className="text-small text-muted">
                        Last audit <DateTime value={site.lastAuditAt} format="relative" />
                      </p>
                    )}
                  </CardBody>
                </Card>
              ))}
              {org.topKeywords.length > 0 && <TopKeywords keywords={org.topKeywords} />}
              <Card as="section" aria-labelledby={`cseo-leads-${org.clientAccountId}`}>
                <CardHeader title="Landing-page leads" titleId={`cseo-leads-${org.clientAccountId}`} headingLevel={3} />
                <CardBody>
                  <div className="grid-auto cseo-stats">
                    <Stat label="Visitors" value={num.format(org.kpis.leads.views)} measurement="Measured" />
                    <Stat label="Leads" value={num.format(org.kpis.leads.submissions)} measurement="Measured" />
                    <Stat label="Conversion rate" value={org.kpis.leads.conversionRate === null ? '—' : pct.format(org.kpis.leads.conversionRate)} measurement="Measured" />
                  </div>
                  {org.kpis.leads.daily.length > 0 && (
                    <BarChart
                      title="Leads per day"
                      description="Form submissions from landing pages and embedded forms per day."
                      data={org.kpis.leads.daily.map((d) => ({ label: d.date.slice(5), value: d.submissions }))}
                      valueLabel="Leads"
                    />
                  )}
                </CardBody>
              </Card>
            </section>
          ))}
        </div>
      )}
    </>
  );
}

function TopKeywords({ keywords }: { keywords: ClientSeoOverview['organizations'][number]['topKeywords'] }) {
  const columns: DataTableColumn<(typeof keywords)[number]>[] = [
    { id: 'kw', header: 'Keyword', primary: true, cell: (k) => k.keyword },
    { id: 'pos', header: 'Position', align: 'right', cell: (k) => (k.position === null ? 'Not ranking' : `#${k.position}`) },
    {
      id: 'change',
      header: 'Change',
      align: 'right',
      cell: (k) =>
        !k.change ? (
          '—'
        ) : (
          <span className={k.change > 0 ? 'cseo-up' : 'cseo-down'}>
            {k.change > 0 ? `Up ${k.change}` : `Down ${-k.change}`}
          </span>
        ),
    },
    { id: 'site', header: 'Site', hideOnMobile: true, cell: (k) => k.siteName },
  ];
  return <DataTable caption="Top keywords" showCaption columns={columns} rows={keywords} getRowId={(k) => `${k.siteName}:${k.keyword}`} />;
}
