import { Card, CardBody, CardHeader } from '@/components/ui/Card';
import { DataTable, type DataTableColumn } from '@/components/ui/DataTable';
import { DateTime } from '@/components/ui/DateTime';
import { Alert } from '@/components/ui/Alert';
import { Badge } from '@/components/ui/Badge';
import { LineChart } from '@/components/ui/Charts';
import { Money } from '@/components/ui/Money';
import { Stat } from '@/components/ui/Stat';
import { formatNumber } from '@/lib/format/money';
import type { CampaignReport } from '../api/types';
import { rate } from '../shared/ui';

function Bars({ rows, label }: { rows: { key: string; opens: number; clicks: number }[]; label: string }) {
  const max = Math.max(1, ...rows.map((r) => r.opens + r.clicks));
  if (rows.length === 0) return <p className="email-muted">No data yet.</p>;
  return (
    <ul className="email-bars" aria-label={label}>
      {rows.map((r) => (
        <li key={r.key} className="email-bars__row">
          <span>{r.key}</span>
          <span className="email-bars__track" aria-hidden="true">
            <span className="email-bars__fill" style={{ width: `${((r.opens + r.clicks) / max) * 100}%`, display: 'block' }} />
          </span>
          <span className="tabular">
            {formatNumber(r.opens)} opens · {formatNumber(r.clicks)} clicks
          </span>
        </li>
      ))}
    </ul>
  );
}

/** Campaign performance: delivery, engagement (machine opens separated), link heat map, devices, variants, revenue. */
export function CampaignReportView({ report }: { report: CampaignReport }) {
  const email = report.channel === 'Email';
  const linkColumns: DataTableColumn<CampaignReport['links'][number]>[] = [
    {
      id: 'url',
      header: 'Link',
      primary: true,
      cell: (l) => <span className="email-cell-stack"><span>{l.url}</span></span>,
    },
    { id: 'unique', header: 'Unique clicks', align: 'right', cell: (l) => formatNumber(l.uniqueClicks), sortable: true, sortValue: (l) => l.uniqueClicks },
    { id: 'total', header: 'Total clicks', align: 'right', cell: (l) => formatNumber(l.totalClicks), sortable: true, sortValue: (l) => l.totalClicks },
    {
      id: 'share',
      header: 'Share of clicks',
      align: 'right',
      cell: (l) => rate(report.totalClicks === 0 ? 0 : l.totalClicks / report.totalClicks),
    },
  ];

  return (
    <div className="stack">
      {report.deliveredIsEstimated && (
        <Alert tone="info" title="Delivered is estimated">
          The provider does not report deliveries for this workspace, so delivered = sent − bounces. Connect SendGrid or Mailgun webhooks for measured
          delivery.
        </Alert>
      )}
      <div className="email-grid">
        <Stat label="Sent" value={formatNumber(report.sent)} measurement="Count" hint={`${formatNumber(report.recipients)} recipients`} />
        <Stat label="Delivered" value={formatNumber(report.delivered)} measurement={report.deliveredIsEstimated ? 'Estimated' : 'Measured'} />
        {email && <Stat label="Open rate" value={rate(report.openRate)} measurement="Measured" hint={`${formatNumber(report.uniqueOpens)} unique · ${formatNumber(report.totalOpens)} total`} />}
        {email && <Stat label="Click rate" value={rate(report.clickRate)} measurement="Measured" hint={`${formatNumber(report.uniqueClicks)} unique · ${formatNumber(report.totalClicks)} total`} />}
        {email && <Stat label="Click-to-open" value={rate(report.clickToOpenRate)} measurement="Measured" />}
        <Stat label="Bounces" value={formatNumber(report.hardBounces + report.softBounces)} measurement="Count" hint={`${report.hardBounces} hard · ${report.softBounces} soft`} />
        <Stat label="Unsubscribes" value={formatNumber(report.unsubscribes)} measurement="Count" hint={rate(report.unsubscribeRate)} />
        <Stat label="Complaints" value={formatNumber(report.complaints)} measurement="Count" hint={rate(report.complaintRate)} />
        {email && (
          <Stat
            label="Machine opens"
            value={formatNumber(report.machineOpens)}
            measurement="Measured"
            hint="Apple Mail Privacy Protection and scanners — excluded from open rate"
          />
        )}
        <Stat label="Conversions" value={formatNumber(report.conversions)} measurement="Measured" />
        {report.revenue.map((r) => (
          <Stat key={r.currency} label={`Revenue (${r.currency})`} value={<Money amount={r.amount} currency={r.currency} />} measurement="Measured" />
        ))}
        {!email && report.costCurrency && (
          <Stat label="Cost" value={<Money amount={report.cost} currency={report.costCurrency} />} measurement="Estimated" hint={`${formatNumber(report.smsSegments)} segments`} />
        )}
      </div>

      {report.variants.length > 0 && (
        <Card>
          <CardHeader title="A/B test" />
          <CardBody>
            <DataTable
              caption="A/B variants"
              rows={report.variants}
              getRowId={(v) => v.key}
              columns={[
                { id: 'key', header: 'Variant', primary: true, cell: (v) => <span>{v.key} {v.winner && <Badge tone="success" size="sm">Winner</Badge>}</span> },
                { id: 'subject', header: 'Subject', cell: (v) => v.subject ?? '—' },
                { id: 'sent', header: 'Sent', align: 'right', cell: (v) => formatNumber(v.sent) },
                { id: 'open', header: 'Open rate', align: 'right', cell: (v) => rate(v.openRate) },
                { id: 'click', header: 'Click rate', align: 'right', cell: (v) => rate(v.clickRate) },
              ]}
            />
          </CardBody>
        </Card>
      )}

      {email && (
        <Card>
          <CardHeader title="Link clicks" description="Human clicks per link (scanner clicks excluded)." />
          <CardBody>
            <DataTable caption="Link clicks" rows={report.links} getRowId={(l) => l.linkId} columns={linkColumns} defaultSort={{ id: 'total', desc: true }} />
          </CardBody>
        </Card>
      )}

      {email && (
        <div className="email-two-col">
          <Card>
            <CardHeader title="Devices" />
            <CardBody>
              <Bars rows={report.devices} label="Opens and clicks by device" />
            </CardBody>
          </Card>
          <Card>
            <CardHeader title="Email clients" />
            <CardBody>
              <Bars rows={report.mailClients} label="Opens and clicks by email client" />
            </CardBody>
          </Card>
        </div>
      )}

      {email && report.timeline.length > 1 && (
        <Card>
          <CardHeader title="First 48 hours" />
          <CardBody>
            <LineChart
              title="Opens and clicks per hour"
              description="Human opens and clicks in the first 48 hours after sending started."
              labels={report.timeline.map((t) => new Date(t.hour).toLocaleString(undefined, { day: 'numeric', hour: 'numeric' }))}
              series={[
                { id: 'opens', label: 'Opens', values: report.timeline.map((t) => t.opens) },
                { id: 'clicks', label: 'Clicks', values: report.timeline.map((t) => t.clicks) },
              ]}
            />
          </CardBody>
        </Card>
      )}

      <p className="email-muted">
        {report.sendStartedAt && (
          <>
            Sending started <DateTime value={report.sendStartedAt} format="datetime" />.
          </>
        )}{' '}
        {report.completedAt && (
          <>
            Completed <DateTime value={report.completedAt} format="datetime" />.
          </>
        )}{' '}
        {report.pending > 0 && `${formatNumber(report.pending)} messages still queued. `}
        {report.skipped > 0 && `${formatNumber(report.skipped)} skipped (no consent, suppressed or frequency preference). `}
        {report.failed > 0 && `${formatNumber(report.failed)} failed.`}
      </p>
    </div>
  );
}
