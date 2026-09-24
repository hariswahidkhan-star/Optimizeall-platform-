import { Printer } from 'lucide-react';
import type { ReactNode } from 'react';
import { Button, Money, ScrollArea } from '@/components/ui';
import { formatDateOnly } from '@/features/agency/billing/lib';
import type { ProposalVersion } from '../api/types';
import '@/features/agency/billing/billing.css';

const SECTIONS: { key: keyof ProposalVersion; title: string }[] = [
  { key: 'executiveSummary', title: 'Executive summary' },
  { key: 'goals', title: 'Goals' },
  { key: 'scope', title: 'Scope' },
  { key: 'deliverables', title: 'Deliverables' },
  { key: 'timeline', title: 'Timeline' },
  { key: 'terms', title: 'Terms' },
];

const RECURRENCE_LABEL: Record<string, string> = { OneTime: 'One-time', Monthly: 'Monthly', Quarterly: 'Quarterly', Annually: 'Annually' };

/** Print-friendly proposal version (staff preview, public `/p/:token` page and client portal). Text only — no API HTML. */
export function ProposalDocumentView({
  version,
  number,
  agencyName,
  preparedFor,
  headingLevel = 1,
  actions,
}: {
  version: ProposalVersion;
  number: string;
  agencyName: string;
  preparedFor?: string | null;
  headingLevel?: 1 | 2;
  actions?: ReactNode;
}) {
  const c = version.currency;
  const H = headingLevel === 1 ? 'h1' : 'h2';
  const Sub = headingLevel === 1 ? 'h2' : 'h3';
  return (
    <article className="bill-document stack" aria-labelledby={`proposal-${number}`}>
      <div className="bill-no-print">
        <Button variant="secondary" leadingIcon={<Printer />} onClick={() => window.print()}>
          Print or save as PDF
        </Button>
        {actions}
      </div>
      <header className="bill-document__head">
        <div>
          <p className="eyebrow">{agencyName}</p>
          <H id={`proposal-${number}`}>{version.title}</H>
          {preparedFor && <p>Prepared for {preparedFor}</p>}
        </div>
        <div className="bill-muted">
          <p>
            Proposal {number} · version {version.versionNumber}
          </p>
          <p>Valid until {formatDateOnly(version.validUntil)}</p>
        </div>
      </header>
      {SECTIONS.map(({ key, title }) => {
        const text = version[key] as string | null;
        if (!text) return null;
        return (
          <section key={key} aria-label={title}>
            <Sub className="bill-strong">{title}</Sub>
            <p className="bill-pre">{text}</p>
          </section>
        );
      })}
      <section aria-label="Investment">
        <Sub className="bill-strong">Investment</Sub>
        <ScrollArea className="bill-table-scroll" label="Proposal pricing">
          <table>
            <caption className="visually-hidden">Proposal pricing</caption>
            <thead>
              <tr>
                <th scope="col">Item</th>
                <th scope="col">Billing</th>
                <th scope="col" className="num">
                  Qty
                </th>
                <th scope="col" className="num">
                  Price
                </th>
                <th scope="col" className="num">
                  Total
                </th>
              </tr>
            </thead>
            <tbody>
              {version.lines.map((l) => (
                <tr key={l.id}>
                  <td>
                    {l.description}
                    {l.discountAmount > 0 && (
                      <span className="bill-muted">
                        {' '}
                        (discount <Money amount={l.discountAmount} currency={c} />)
                      </span>
                    )}
                  </td>
                  <td>{RECURRENCE_LABEL[l.recurrence] ?? l.recurrence}</td>
                  <td className="num">{l.quantity}</td>
                  <td className="num">
                    <Money amount={l.unitPrice} currency={c} />
                  </td>
                  <td className="num">
                    <Money amount={l.total} currency={c} />
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </ScrollArea>
        <dl className="bill-totals">
          {version.totals.taxes.map((t) => (
            <div key={`${t.name}-${t.ratePercent}`}>
              <dt>
                {t.name} ({t.ratePercent}%{t.inclusive ? ', included' : ''})
              </dt>
              <dd>
                <Money amount={t.taxAmount} currency={c} />
              </dd>
            </div>
          ))}
          <div>
            <dt>One-time</dt>
            <dd>
              <Money amount={version.recurring.oneTimeTotal} currency={c} />
            </dd>
          </div>
          {version.recurring.monthlyTotal > 0 && (
            <div>
              <dt>Monthly</dt>
              <dd>
                <Money amount={version.recurring.monthlyTotal} currency={c} />
              </dd>
            </div>
          )}
          {version.recurring.quarterlyTotal > 0 && (
            <div>
              <dt>Quarterly</dt>
              <dd>
                <Money amount={version.recurring.quarterlyTotal} currency={c} />
              </dd>
            </div>
          )}
          {version.recurring.annualTotal > 0 && (
            <div>
              <dt>Annually</dt>
              <dd>
                <Money amount={version.recurring.annualTotal} currency={c} />
              </dd>
            </div>
          )}
          <div className="bill-totals__grand">
            <dt>First invoice</dt>
            <dd>
              <Money amount={version.recurring.firstInvoiceTotal} currency={c} />
            </dd>
          </div>
          <div>
            <dt>First-year value</dt>
            <dd>
              <Money amount={version.recurring.firstYearValue} currency={c} />
            </dd>
          </div>
        </dl>
      </section>
    </article>
  );
}
