import { PageHeader } from '@/components/ui';
import { CodeSalesTable } from './CodeSalesTable';
import { CodeSaleDetailPage } from './CodeSaleDetailPage';
import '../codes.css';

/** Reviewer portal: the discount-code sales review queue (pending first, oldest first). */
export function CodeSalesReviewQueuePage() {
  return (
    <>
      <PageHeader
        title="Code sales"
        description="Sales participants reported with brand discount codes. Check the order (and the brand’s report when it’s matched), then approve, reject or ask for more information. You can’t review your own sales or ones you entered."
      />
      <CodeSalesTable basePath="/review/code-sales" defaultStatus="Pending" caption="Code sales to review" />
    </>
  );
}

export function ReviewCodeSaleDetailPage() {
  return <CodeSaleDetailPage basePath="/review/code-sales" backLabel="Code sales" />;
}

/** Finance portal: approved code sales, for refunds and clawbacks. */
export function FinanceCodeSalesPage() {
  return (
    <>
      <PageHeader
        title="Code sales"
        description="Discount-code sales and their commissions. Mark an order refunded to reverse its commission — a paid one becomes a clawback against the participant’s next payout."
      />
      <CodeSalesTable basePath="/finance/code-sales" defaultStatus="Approved" caption="Code sales" />
    </>
  );
}

export function FinanceCodeSaleDetailPage() {
  return <CodeSaleDetailPage basePath="/finance/code-sales" backLabel="Code sales" />;
}

/** Campaign manager: every code sale across programs. */
export function ManageCodeSalesPage() {
  return (
    <>
      <PageHeader
        title="Code sales"
        description="Every sale reported with a discount code, across programs. Open a sale for its proof, the brand’s report and the commission."
        breadcrumbs={[{ label: 'Discount codes', to: '/manage/codes' }, { label: 'Code sales' }]}
      />
      <CodeSalesTable basePath="/manage/codes/sales" caption="Code sales" />
    </>
  );
}

export function ManageCodeSaleDetailPage() {
  return <CodeSaleDetailPage basePath="/manage/codes/sales" backLabel="Code sales" />;
}
