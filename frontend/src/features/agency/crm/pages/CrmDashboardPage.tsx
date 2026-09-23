import { CalendarCheck, Percent, TrendingUp, Users } from 'lucide-react';
import { Link } from 'react-router-dom';
import { BarChart, ButtonLink, Card, CardBody, CardHeader, DataTable, EmptyState, ErrorState, Money, PageHeader, Stat, type DataTableColumn } from '@/components/ui';
import { useCrmDashboard } from '../api/hooks';
import type { CrmDashboard, CurrencyValue } from '../api/types';
import { ActivityItem } from '../components/ActivityPanel';
import { SOURCE_LABELS } from '../lib';
import '@/features/agency/billing/billing.css';
import '../crm.css';

function Values({ values }: { values: CurrencyValue[] }) {
  if (values.length === 0) return <>—</>;
  return (
    <span className="bill-amounts">
      {values.map((v) => (
        <Money key={v.currency} amount={v.amount} currency={v.currency} />
      ))}
    </span>
  );
}

type PipelineRow = CrmDashboard['pipeline'][number];
const pipelineColumns: DataTableColumn<PipelineRow>[] = [
  { id: 'stage', header: 'Stage', primary: true, cell: (r) => r.stageName },
  { id: 'deals', header: 'Deals', align: 'right', cell: (r) => r.count },
  { id: 'prob', header: 'Win %', align: 'right', cell: (r) => `${r.winProbability}%` },
  { id: 'value', header: 'Value', align: 'right', cell: (r) => <Values values={r.value} /> },
  { id: 'weighted', header: 'Weighted', align: 'right', cell: (r) => <Values values={r.weighted} /> },
];

export function CrmDashboardPage() {
  const query = useCrmDashboard();
  const d = query.data;
  return (
    <>
      <PageHeader
        title="Sales CRM"
        description="Pipeline value, weighted forecast and what needs your attention today."
        actions={
          <div className="crm-actions">
            <ButtonLink to="/agency/crm/deals" variant="secondary">
              Pipeline
            </ButtonLink>
            <ButtonLink to="/agency/crm/contacts" variant="secondary">
              Contacts
            </ButtonLink>
            <ButtonLink to="/agency/crm/companies" variant="secondary">
              Companies
            </ButtonLink>
            <ButtonLink to="/agency/crm/settings" variant="ghost">
              Settings
            </ButtonLink>
          </div>
        }
      />
      {query.isError ? (
        <ErrorState error={query.error} onRetry={() => void query.refetch()} />
      ) : (
        <div className="stack">
          <div className="crm-grid">
            <Stat label="Open pipeline" icon={<TrendingUp />} loading={query.isPending} value={<Values values={d?.openValue ?? []} />} hint={d ? `${d.openDeals} open deals` : undefined} />
            <Stat label="Weighted forecast" measurement="Estimated" loading={query.isPending} value={<Values values={d?.weightedForecast ?? []} />} hint="Value × stage win probability" />
            <Stat label="Win rate (90 days)" icon={<Percent />} loading={query.isPending} value={d ? `${d.winRate}%` : '—'} hint={d ? `${d.wonLast90Days} won · ${d.lostLast90Days} lost` : undefined} />
            <Stat label="New leads (30 days)" icon={<Users />} measurement="Count" loading={query.isPending} value={d?.newLeadsLast30Days ?? '—'} />
          </div>
          <div className="crm-two-col">
            <div className="stack">
              <Card>
                <CardHeader title="Pipeline by stage" />
                <CardBody>
                  <DataTable caption="Pipeline by stage" columns={pipelineColumns} rows={d?.pipeline ?? []} getRowId={(r) => r.stageId} loading={query.isPending} />
                </CardBody>
              </Card>
              <Card>
                <CardHeader title="Lead sources (90 days)" />
                <CardBody>
                  {d && d.leadSources.length > 0 ? (
                    <BarChart
                      title="Deals by source"
                      description="Number of deals created in the last 90 days, by lead source."
                      data={d.leadSources.map((s) => ({ label: SOURCE_LABELS[s.source], value: s.deals }))}
                      valueLabel="Deals"
                    />
                  ) : (
                    <EmptyState compact headingLevel={3} title="No new deals in the last 90 days" />
                  )}
                </CardBody>
              </Card>
            </div>
            <Card>
              <CardHeader
                title="Due today"
                description={d && d.overdueTasks > 0 ? `${d.overdueTasks} overdue task(s)` : undefined}
                actions={
                  <Link className="ui-link" to="/agency/crm/tasks">
                    All my tasks
                  </Link>
                }
              />
              <CardBody>
                {d && d.dueToday.length > 0 ? (
                  <ul className="crm-list" aria-label="Activities due today">
                    {d.dueToday.map((a) => (
                      <ActivityItem key={a.id} activity={a} showLinks />
                    ))}
                  </ul>
                ) : (
                  <EmptyState compact headingLevel={3} icon={<CalendarCheck />} title="Nothing due today" />
                )}
              </CardBody>
            </Card>
          </div>
        </div>
      )}
    </>
  );
}
