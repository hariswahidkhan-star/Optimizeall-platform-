import { Alert, Badge, DataTable, Money, type DataTableColumn } from '@/components/ui';
import type { ExplainCandidate, RateExplanation } from '../api/types';
import { formatLabel } from '../labels';
import { LevelBadge } from './badges';

const OUTCOME: Record<string, { label: string; tone: 'success' | 'neutral' | 'warning' }> = {
  Won: { label: 'Applies', tone: 'success' },
  Outranked: { label: 'Outranked', tone: 'warning' },
  NotApplicable: { label: 'Not applicable', tone: 'neutral' },
};

/**
 * "Explain this rate": every candidate rate for one post with the rule that decided it, the precedence table, the
 * currency conversion and (with a campaign) the full quote including caps and bonuses.
 */
export function RateExplanationView({ explanation }: { explanation: RateExplanation }) {
  const e = explanation;
  const columns: DataTableColumn<ExplainCandidate>[] = [
    {
      id: 'outcome',
      header: 'Result',
      cell: (c) => (
        <Badge size="sm" tone={OUTCOME[c.outcome]?.tone ?? 'neutral'} dot>
          {OUTCOME[c.outcome]?.label ?? c.outcome}
        </Badge>
      ),
    },
    { id: 'level', header: 'Level', cell: (c) => <LevelBadge level={c.level} /> },
    {
      id: 'source',
      header: 'Source',
      primary: true,
      cell: (c) => (
        <span className="stack rt-stack-xs">
          <span>
            {c.cardName}
            {c.version !== null && <span className="text-muted"> v{c.version}</span>}
          </span>
          {c.groupName && (
            <span className="text-small text-muted">
              via {c.groupName} (priority {c.priority})
            </span>
          )}
        </span>
      ),
    },
    {
      id: 'rate',
      header: 'Rate',
      align: 'right',
      cell: (c) =>
        c.amount !== null && c.currency ? (
          <span className="stack rt-stack-xs">
            <Money amount={c.amount} currency={c.currency} />
            <span className="text-small text-muted">{c.lineConditions}</span>
          </span>
        ) : (
          '—'
        ),
    },
    { id: 'why', header: 'Why', cell: (c) => <span className="text-small">{c.reason}</span> },
  ];
  const quote = e.quote;
  return (
    <div className="stack">
      <Alert tone={e.winner === 'CampaignRules' ? 'info' : 'success'} title="Result">
        {e.summary}
      </Alert>
      <p className="text-small text-muted">
        {e.platform}
        {e.format ? ` · ${formatLabel(e.format)}` : ' · any format'} · {e.countryCode} · {e.tier} tier
        {e.followers !== null && ` · ${e.followers.toLocaleString()} verified followers`}
        {e.campaign && ` · ${e.campaign.title}`}
        {e.campaignPolicy === 'CampaignRatesOnly' && ' · campaign uses campaign rates only'}
        {e.maxMultiplier !== null && ` · personal rates ≤ ${e.maxMultiplier}× campaign rate`}
      </p>
      {e.candidates.length > 0 ? (
        <DataTable
          caption="Candidate rates"
          columns={columns}
          rows={e.candidates}
          getRowId={(c) => c.assignmentId}
        />
      ) : (
        <p className="text-muted">No rate card, group or deal is assigned to this person.</p>
      )}
      {e.conversion && e.conversion.fromCurrency !== e.conversion.toCurrency && (
        <p className="text-small">
          Converted <Money amount={e.conversion.cardAmount} currency={e.conversion.fromCurrency} /> at{' '}
          {e.conversion.rate} → <Money amount={e.conversion.amount} currency={e.conversion.toCurrency} />{' '}
          (exchange rate in force now; locked when a post is submitted).
        </p>
      )}
      {e.conversionError && (
        <Alert tone="danger" title="Can't be priced">
          {e.conversionError}
        </Alert>
      )}
      {quote && (
        <div className="rt-quote">
          <h4 className="rt-h4">Price of one approved post</h4>
          <ul className="rt-quote__lines">
            {quote.lines.map((l) => (
              <li key={`${l.type}-${l.ruleId}`}>
                <span>
                  {l.label}
                  {l.fromPersonalRate && (
                    <Badge size="sm" tone="brand">
                      Personal rate
                    </Badge>
                  )}
                </span>
                <Money amount={l.amount} currency={quote.currency} />
              </li>
            ))}
            <li className="rt-quote__total">
              <span>Total</span>
              <Money amount={quote.total} currency={quote.currency} />
            </li>
          </ul>
          {quote.appliedCaps.length > 0 && (
            <p className="text-small text-muted">Limits applied: {quote.appliedCaps.join(', ')}</p>
          )}
        </div>
      )}
      <details className="rt-precedence">
        <summary>Precedence (highest first)</summary>
        <ol>
          {e.precedence.map((p) => (
            <li key={p.level} className={p.level === e.winner ? 'rt-precedence__winner' : undefined}>
              {p.label}
            </li>
          ))}
        </ol>
        <p className="text-small text-muted">
          Within a level: the most specific rate (platform + format + country) wins, then the higher priority,
          then the older assignment.
        </p>
      </details>
    </div>
  );
}
