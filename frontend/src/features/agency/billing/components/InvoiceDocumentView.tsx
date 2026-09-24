import { Printer } from 'lucide-react';
import type { ReactNode } from 'react';
import { Alert, Button, Money, ScrollArea } from '@/components/ui';
import type { PublicInvoice } from '../api/types';
import { InvoiceStatusBadge, formatDateOnly } from '../lib';
import '../billing.css';

function Multiline({ text }: { text: string | null | undefined }) {
  if (!text) return null;
  return <p className="bill-pre">{text}</p>;
}

/**
 * Print-friendly invoice (client portal and the public `/i/:token` page). Everything is rendered as text by React — no
 * HTML from the API is injected.
 */
export function InvoiceDocumentView({
  invoice,
  actions,
  headingLevel = 1,
}: {
  invoice: PublicInvoice;
  actions?: ReactNode;
  /** 2 when the page already has its own h1 (the client portal); the document's sections then use h3. */
  headingLevel?: 1 | 2;
}) {
  const H = headingLevel === 1 ? 'h1' : 'h2';
  const Sub = headingLevel === 1 ? 'h2' : 'h3';
  const c = invoice.currency;
  const p = invoice.payment;
  const open = ['Issued', 'PartiallyPaid', 'Overdue'].includes(invoice.status);
  return (
    <article className="bill-document stack" aria-labelledby="invoice-title">
      <div className="bill-no-print">
        <Button variant="secondary" leadingIcon={<Printer />} onClick={() => window.print()}>
          Print or save as PDF
        </Button>
        {actions}
      </div>
      <header className="bill-document__head">
        <div>
          <p className="eyebrow">{p.companyName}</p>
          <Multiline text={p.companyAddress} />
          {p.companyTaxId && <p className="bill-muted">Tax ID: {p.companyTaxId}</p>}
        </div>
        <div>
          <H id="invoice-title">Invoice {invoice.number}</H>
          <p>
            <InvoiceStatusBadge status={invoice.status} />
          </p>
          <p className="bill-muted">
            Issued {formatDateOnly(invoice.issueDate)} · Due {formatDateOnly(invoice.dueDate)}
          </p>
          {invoice.periodStart && (
            <p className="bill-muted">
              Period {formatDateOnly(invoice.periodStart)} – {formatDateOnly(invoice.periodEnd)}
            </p>
          )}
        </div>
      </header>
      <section aria-label="Bill to">
        <Sub className="bill-strong">Bill to</Sub>
        <p>{invoice.clientName}</p>
        <Multiline text={invoice.clientAddress} />
        {invoice.clientTaxId && <p className="bill-muted">Tax ID: {invoice.clientTaxId}</p>}
      </section>
      <ScrollArea className="bill-table-scroll" label="Invoice lines">
        <table>
          <caption className="visually-hidden">Invoice lines</caption>
          <thead>
            <tr>
              <th scope="col">Description</th>
              <th scope="col" className="num">
                Qty
              </th>
              <th scope="col" className="num">
                Unit price
              </th>
              <th scope="col">Tax</th>
              <th scope="col" className="num">
                Amount
              </th>
            </tr>
          </thead>
          <tbody>
            {invoice.lines.map((l) => (
              <tr key={l.id}>
                <td>{l.description}</td>
                <td className="num">{l.quantity}</td>
                <td className="num">
                  <Money amount={l.unitPrice} currency={c} />
                </td>
                <td>{l.taxPercent > 0 ? `${l.taxName ?? 'Tax'} ${l.taxPercent}%${l.taxInclusive ? ' incl.' : ''}` : '—'}</td>
                <td className="num">
                  <Money amount={l.total} currency={c} />
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </ScrollArea>
      <dl className="bill-totals">
        <div>
          <dt>Subtotal</dt>
          <dd>
            <Money amount={invoice.totals.subtotal} currency={c} />
          </dd>
        </div>
        {invoice.totals.taxes.map((t) => (
          <div key={`${t.name}-${t.ratePercent}`}>
            <dt>
              {t.name} ({t.ratePercent}%{t.inclusive ? ', included' : ''})
            </dt>
            <dd>
              <Money amount={t.taxAmount} currency={c} />
            </dd>
          </div>
        ))}
        <div className="bill-totals__grand">
          <dt>Total</dt>
          <dd>
            <Money amount={invoice.totals.total} currency={c} />
          </dd>
        </div>
        {invoice.amountPaid > 0 && (
          <div>
            <dt>Paid</dt>
            <dd>
              <Money amount={invoice.amountPaid} currency={c} />
            </dd>
          </div>
        )}
        {invoice.amountCredited > 0 && (
          <div>
            <dt>Credited</dt>
            <dd>
              <Money amount={invoice.amountCredited} currency={c} />
            </dd>
          </div>
        )}
        <div className="bill-totals__grand">
          <dt>Balance due</dt>
          <dd>
            <Money amount={invoice.balance} currency={c} />
          </dd>
        </div>
      </dl>
      {invoice.notes && (
        <section aria-label="Notes">
          <Sub className="bill-strong">Notes</Sub>
          <Multiline text={invoice.notes} />
        </section>
      )}
      {open && (p.bankDetails || p.paymentInstructions || p.paymentLinkText) && (
        <section aria-label="How to pay">
          <Sub className="bill-strong">How to pay</Sub>
          <Multiline text={p.paymentInstructions} />
          <Multiline text={p.bankDetails} />
          <Multiline text={p.paymentLinkText} />
          <p className="bill-muted">Please quote invoice {invoice.number} as the payment reference.</p>
        </section>
      )}
      {open && !p.onlinePaymentAvailable && (
        <Alert tone="info" className="bill-no-print">
          Online card payment isn’t available — please pay by bank transfer.
        </Alert>
      )}
      <Multiline text={p.invoiceFooter} />
    </article>
  );
}
