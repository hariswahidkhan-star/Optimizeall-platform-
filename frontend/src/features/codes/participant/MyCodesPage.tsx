import { BadgePercent, Plus, Share2, TicketPercent } from 'lucide-react';
import { useState } from 'react';
import { Link } from 'react-router-dom';
import {
  Alert,
  Badge,
  Button,
  Card,
  CardBody,
  CardHeader,
  CopyField,
  DataTable,
  DateTime,
  EmptyState,
  ErrorState,
  Money,
  PageHeader,
  Pagination,
  Select,
  Skeleton,
  Stat,
} from '@/components/ui';
import { useToast } from '@/components/ui/toastContext';
import { formatDate } from '@/lib/format/dates';
import { useMyCodeSales, useMyCodes } from '../api/queries';
import type { MyCode, MyCodeSale } from '../api/types';
import { SALE_STATUS_OPTIONS, SaleStatusBadge } from '../labels';
import { ReportSaleDialog } from './ReportSaleDialog';
import '../codes.css';

function ShareButton({ code }: { code: MyCode }) {
  const toast = useToast();
  if (!code.shareUrl || typeof navigator === 'undefined' || typeof navigator.share !== 'function')
    return null;
  return (
    <Button
      variant="secondary"
      size="sm"
      leadingIcon={<Share2 />}
      onClick={async () => {
        try {
          await navigator.share({
            title: `${code.brandName}: ${code.discountLabel ?? 'discount'}`,
            text: `Use my code ${code.code}`,
            url: code.shareUrl!,
          });
        } catch (error) {
          if (!(error instanceof DOMException && error.name === 'AbortError'))
            toast.error('Couldn’t open the share sheet', 'Copy the link instead.');
        }
      }}
    >
      Share
    </Button>
  );
}

function CodeCard({ code, onReport }: { code: MyCode; onReport: () => void }) {
  const titleId = `code-${code.codeId}`;
  return (
    <Card as="article" aria-labelledby={titleId}>
      <CardBody className="stack">
        <div className="dc-code-card__top">
          <div>
            <h2 id={titleId} className="dc-code-card__brand">
              {code.brandName}
            </h2>
            <p className="dc-code-card__program">{code.programName}</p>
          </div>
          <span className="cluster dc-cluster-sm">
            <Badge tone={code.shared ? 'info' : 'brand'} size="sm">
              {code.shared ? 'Shared with your group' : 'Personal code'}
            </Badge>
            <Badge tone={code.isActive ? 'success' : 'warning'} size="sm" dot>
              {code.isActive ? 'Active' : 'Inactive'}
            </Badge>
          </span>
        </div>

        <div className="dc-token">
          <div style={{ flex: '1 1 12rem', minWidth: 0 }}>
            <span className="dc-token__label">Your code</span>
            <p className="dc-token__code" data-testid="my-code">
              {code.code}
            </p>
          </div>
          <span className="cluster dc-cluster-sm">
            <ShareButton code={code} />
            <Button
              size="sm"
              leadingIcon={<Plus />}
              onClick={onReport}
              disabled={!code.isActive && !code.inactiveReason?.startsWith('No longer')}
            >
              Report a sale
            </Button>
          </span>
        </div>
        {!code.isActive && code.inactiveReason && <Alert tone="warning">{code.inactiveReason}</Alert>}
        <CopyField label="Code" value={code.code} hideLabel />
        {code.shareUrl && (
          <CopyField label="Share link (your code is applied automatically)" value={code.shareUrl} />
        )}

        <dl className="dc-facts">
          <div>
            <dt>You earn</dt>
            <dd>{code.yourRate}</dd>
          </div>
          {code.discountLabel && (
            <div>
              <dt>Your audience gets</dt>
              <dd>{code.discountLabel}</dd>
            </div>
          )}
          <div>
            <dt>Valid</dt>
            <dd>
              {formatDate(code.assignedFrom)} –{' '}
              {code.assignedUntil
                ? formatDate(code.assignedUntil)
                : code.programEndsAt
                  ? formatDate(code.programEndsAt)
                  : 'open-ended'}
            </dd>
          </div>
          <div>
            <dt>Report orders within</dt>
            <dd>{code.maxOrderAgeDays} days</dd>
          </div>
        </dl>
        {code.tierPerks.length > 0 && (
          <div>
            <p className="text-small dc-strong" style={{ margin: 0 }}>
              Sell more, earn more
            </p>
            <ul className="dc-perks">
              {code.tierPerks.map((p) => (
                <li key={p}>{p}</li>
              ))}
            </ul>
          </div>
        )}

        <div className="dc-stats">
          <Stat label="Sales" measurement="Count" value={code.stats.sales} />
          <Stat label="Awaiting review" measurement="Count" value={code.stats.pending} />
          <Stat
            label="Approved earnings"
            value={<Money amount={code.stats.commissionApproved} currency={code.currency} />}
          />
          <Stat
            label="Paid out"
            value={<Money amount={code.stats.commissionPaid} currency={code.currency} />}
          />
          {code.stats.clicks !== null && (
            <Stat label="Link clicks" measurement="Measured" value={code.stats.clicks} />
          )}
        </div>
        {(code.terms || code.description) && (
          <details className="dc-terms">
            <summary>Program details and terms</summary>
            {code.description && <p>{code.description}</p>}
            {code.terms && <p>{code.terms}</p>}
          </details>
        )}
      </CardBody>
    </Card>
  );
}

function SalesSection({ codes }: { codes: MyCode[] }) {
  const [status, setStatus] = useState('');
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(10);
  const query = useMyCodeSales({ status: status || undefined, page, pageSize });
  const currencyOf = (s: MyCodeSale) => s.programCurrency;
  return (
    <Card as="section" aria-labelledby="my-code-sales">
      <CardHeader
        titleId="my-code-sales"
        title="Your reported sales"
        description="Every sale is checked against the brand’s records before it’s paid."
        actions={
          <Select
            aria-label="Filter by status"
            size="sm"
            value={status}
            onChange={(e) => {
              setStatus(e.target.value);
              setPage(1);
            }}
            placeholder="All statuses"
            options={SALE_STATUS_OPTIONS}
          />
        }
      />
      <CardBody className="stack">
        {query.isError ? (
          <ErrorState error={query.error} onRetry={() => void query.refetch()} />
        ) : (
          <>
            <DataTable
              caption="Your reported sales"
              rows={query.data?.items ?? []}
              loading={query.isLoading}
              getRowId={(s) => s.id}
              columns={[
                {
                  id: 'order',
                  header: 'Order',
                  primary: true,
                  cell: (s) => (
                    <Link className="ui-link dc-strong" to={`/app/codes/sales/${s.id}`}>
                      {s.orderReference}
                    </Link>
                  ),
                },
                { id: 'code', header: 'Code', cell: (s) => <span className="dc-mono">{s.code}</span> },
                { id: 'date', header: 'Order date', cell: (s) => <DateTime value={s.orderDate} /> },
                {
                  id: 'value',
                  header: 'Order value',
                  align: 'right',
                  cell: (s) => <Money amount={s.netAmount} currency={s.currency} />,
                },
                { id: 'status', header: 'Status', cell: (s) => <SaleStatusBadge status={s.status} /> },
                {
                  id: 'earned',
                  header: 'You earn',
                  align: 'right',
                  cell: (s) =>
                    s.commissionAmount !== null ? (
                      <Money amount={s.commissionAmount} currency={currencyOf(s)} />
                    ) : s.estimatedCommission !== null &&
                      (s.status === 'Pending' || s.status === 'NeedsInfo') ? (
                      <span className="text-muted">
                        ~<Money amount={s.estimatedCommission} currency={currencyOf(s)} />
                      </span>
                    ) : (
                      '—'
                    ),
                },
              ]}
              emptyState={
                <EmptyState
                  compact
                  headingLevel={3}
                  icon={<BadgePercent />}
                  title={status ? 'No sales with this status' : 'No sales reported yet'}
                  description={
                    codes.length > 0 ? 'When someone buys with your code, report the order here.' : undefined
                  }
                />
              }
            />
            {query.data && query.data.total > pageSize && (
              <Pagination
                page={page}
                pageSize={pageSize}
                total={query.data.total}
                onPageChange={setPage}
                onPageSizeChange={(n) => {
                  setPageSize(n);
                  setPage(1);
                }}
              />
            )}
          </>
        )}
      </CardBody>
    </Card>
  );
}

/** Participant portal: my discount codes (copy, share, terms, stats) and the sales I reported. */
export function MyCodesPage() {
  const query = useMyCodes();
  const [reporting, setReporting] = useState<{ codeId?: string } | null>(null);
  const codes = query.data ?? [];
  return (
    <div className="pp-page">
      <PageHeader
        title="My discount codes"
        description="Share your brand codes, report the orders placed with them and earn a commission on every approved sale."
        actions={
          codes.length > 0 ? (
            <Button leadingIcon={<Plus />} onClick={() => setReporting({})}>
              Report a sale
            </Button>
          ) : undefined
        }
      />
      {query.isPending ? (
        <Card>
          <CardBody>
            <Skeleton height={192} />
          </CardBody>
        </Card>
      ) : query.isError ? (
        <ErrorState error={query.error} onRetry={() => void query.refetch()} />
      ) : codes.length === 0 ? (
        <EmptyState
          icon={<TicketPercent />}
          title="No codes yet"
          description="When a brand’s discount code is assigned to you (or to your group), it appears here with your share link and earnings."
        />
      ) : (
        <div className="dc-codes">
          {codes.map((c) => (
            <CodeCard key={c.codeId} code={c} onReport={() => setReporting({ codeId: c.codeId })} />
          ))}
        </div>
      )}
      <SalesSection codes={codes} />
      {reporting && (
        <ReportSaleDialog codes={codes} codeId={reporting.codeId} onClose={() => setReporting(null)} />
      )}
    </div>
  );
}
