import { Alert } from '@/components/ui';
import type { Report, ReportKpi, ReportSection } from './deliveryTypes';
import { formatDateOnly, formatKpi, MeasurementLabel } from './deliveryUi';

function Delta({ kpi }: { kpi: ReportKpi }) {
  if (kpi.value === null || kpi.previousValue === null || kpi.previousValue === undefined) return null;
  const diff = kpi.value - kpi.previousValue;
  if (diff === 0) return <span className="dl-kpi__source">No change vs. last period</span>;
  const pct = kpi.previousValue !== 0 ? ` (${diff > 0 ? '+' : ''}${Math.round((diff / Math.abs(kpi.previousValue)) * 100)}%)` : '';
  return (
    <span className="dl-kpi__source">
      {diff > 0 ? '▲' : '▼'} {formatKpi(Math.abs(diff), kpi.unit)}
      {pct} vs. last period
    </span>
  );
}

export function KpiCard({ kpi }: { kpi: ReportKpi }) {
  return (
    <li className="dl-kpi" data-measurement={kpi.measurement}>
      <span className="dl-row">
        <span>{kpi.label}</span>
        <MeasurementLabel measurement={kpi.measurement} />
      </span>
      <span className="dl-kpi__value">{formatKpi(kpi.value, kpi.unit)}</span>
      <Delta kpi={kpi} />
      <span className="dl-kpi__source">Source: {kpi.source}</span>
      {kpi.note ? <span className="dl-kpi__source">{kpi.note}</span> : null}
    </li>
  );
}

function Section({ section, audience }: { section: ReportSection; audience: 'staff' | 'client' }) {
  const empty = !section.body && section.kpis.length === 0;
  if (empty && audience === 'client') return null;
  const headingId = `report-section-${section.key}`;
  return (
    <section className="dl-report__section" aria-labelledby={headingId}>
      <h2 id={headingId}>{section.title}</h2>
      {section.body ? <p className="dl-report__body">{section.body.replace(/<!--[\s\S]*?-->/g, '').trim()}</p> : null}
      {section.kpis.length > 0 ? (
        <ul className="dl-kpis" aria-label={`${section.title} KPIs`}>
          {section.kpis.map((k) => (
            <KpiCard key={k.key} kpi={k} />
          ))}
        </ul>
      ) : null}
      {audience === 'staff' && section.providerNote ? <Alert tone="info">{section.providerNote}</Alert> : null}
      {audience === 'staff' && empty && !section.providerNote ? <p className="dl-muted">Empty section — it won't be shown to the client.</p> : null}
    </section>
  );
}

/** A client report as the client sees it (also the staff preview). Print-friendly (see delivery-shared.css). */
export function ReportView({ report, audience = 'client' }: { report: Report; audience?: 'staff' | 'client' }) {
  return (
    <article className="dl-report" aria-label={report.title}>
      <p className="dl-muted">
        {report.clientName} · {formatDateOnly(report.periodStart)} – {formatDateOnly(report.periodEnd)}
      </p>
      {report.sections.map((s) => (
        <Section key={s.key} section={s} audience={audience} />
      ))}
      <p className="dl-kpi__source">
        Measured: reported by a connected platform. Estimated: modelled or projected. Manual: entered by your account team.
      </p>
    </article>
  );
}
