import { Plus } from 'lucide-react';
import { useMemo, useState } from 'react';
import { useParams } from 'react-router-dom';
import {
  Alert,
  Button,
  ButtonLink,
  Card,
  CardBody,
  CardHeader,
  DataTable,
  DateTime,
  KeyValueList,
  Money,
  PageHeader,
  Skeleton,
  Stat,
  StatusBadge,
} from '@/components/ui';
import { humanize } from '@/lib/format/text';
import { useLedger, useUserBalance } from '../api/hooks';
import { QueryError } from '../components/common';
import { dateOnlyToDisplay } from '../lib/format';
import { useCan } from '../lib/useCan';
import { AdjustmentDialog } from './AdjustmentDialog';

/** `/finance/users/{id}/balance` — the participant's balance buckets, next payout and recent ledger entries. */
export function UserBalancePage() {
  const { userId = '' } = useParams();
  const can = useCan();
  const balance = useUserBalance(userId);
  const recent = useLedger({ userId, page: 1, pageSize: 10 });
  const [adjusting, setAdjusting] = useState(false);
  const person = recent.data?.items[0]?.user;
  const picked = useMemo(
    () => ({ id: userId, label: person ? `${person.displayName} (${person.email})` : undefined }),
    [userId, person],
  );

  const title = person ? person.displayName : 'Participant balance';
  const s = balance.data;

  return (
    <>
      <PageHeader
        breadcrumbs={[{ label: 'Ledger', to: '/finance/ledger' }, { label: 'Balance' }]}
        title={title}
        description={person ? person.email : `User ${userId}`}
        actions={
          <>
            {can.adjust && (
              <Button leadingIcon={<Plus />} onClick={() => setAdjusting(true)}>
                New adjustment
              </Button>
            )}
            <ButtonLink variant="secondary" to={`/finance/ledger?userId=${userId}`}>
              All ledger entries
            </ButtonLink>
          </>
        }
      />
      <div className="stack fin-page">
        {balance.isPending ? (
          <Skeleton height={200} />
        ) : balance.isError ? (
          <QueryError error={balance.error} onRetry={() => balance.refetch()} compact={false} />
        ) : s ? (
          <>
            {s.activeHold && (
              <Alert tone="warning" title="Payout hold active">
                This participant’s payouts are on hold. {s.holdMessage}
              </Alert>
            )}
            <div className="fin-stats">
              <Stat
                label="Available for next payout"
                value={<Money amount={s.availableForNextPayout} currency={s.currency} />}
              />
              <Stat
                label="Pending"
                value={<Money amount={s.pending} currency={s.currency} />}
                hint="Submissions in review + pending approval"
              />
              <Stat label="Approved" value={<Money amount={s.approved} currency={s.currency} />} />
              <Stat
                label="On hold (reversal buffer)"
                value={<Money amount={s.onHold} currency={s.currency} />}
              />
              <Stat
                label="Scheduled"
                value={<Money amount={s.scheduled} currency={s.currency} />}
                hint="In a payout batch"
              />
              <Stat label="Paid" value={<Money amount={s.paid} currency={s.currency} />} />
              <Stat label="Reversed" value={<Money amount={s.reversed} currency={s.currency} />} />
              <Stat
                label="Lifetime earned"
                value={<Money amount={s.lifetimeEarned} currency={s.currency} />}
              />
            </div>
            <Card as="section" aria-labelledby="next-payout-title">
              <CardHeader titleId="next-payout-title" title="Next payout" />
              <CardBody>
                <KeyValueList
                  items={[
                    { label: 'Period', value: s.nextPayout.periodKey },
                    { label: 'Cutoff', value: <DateTime value={s.nextPayout.cutoffAt} withZone /> },
                    { label: 'Payment date', value: dateOnlyToDisplay(s.nextPayout.paymentDate) },
                    {
                      label: 'Minimum payout',
                      value: <Money amount={s.nextPayout.minimumPayoutAmount} currency={s.currency} />,
                    },
                    { label: 'Meets minimum', value: s.nextPayout.meetsMinimum ? 'Yes' : 'No' },
                    {
                      label: 'Estimated amount',
                      value: <Money amount={s.nextPayout.estimatedAmount} currency={s.currency} />,
                    },
                  ]}
                />
              </CardBody>
            </Card>
            {(s.byCurrency.length > 0 || s.pendingByCurrency.length > 0) && (
              <Card as="section" aria-labelledby="by-currency-title">
                <CardHeader
                  titleId="by-currency-title"
                  title="By original currency"
                  description="Totals of ledger entries in the currency they were earned in."
                />
                <CardBody className="stack">
                  <DataTable
                    caption="Balances by original currency"
                    rows={s.byCurrency}
                    getRowId={(r) => r.currency}
                    columns={[
                      { id: 'c', header: 'Currency', primary: true, cell: (r) => r.currency },
                      {
                        id: 'pa',
                        header: 'Pending approval',
                        align: 'right',
                        cell: (r) => <Money amount={r.pendingApproval} currency={r.currency} />,
                      },
                      {
                        id: 'a',
                        header: 'Approved',
                        align: 'right',
                        cell: (r) => <Money amount={r.approved} currency={r.currency} />,
                      },
                      {
                        id: 's',
                        header: 'Scheduled',
                        align: 'right',
                        cell: (r) => <Money amount={r.scheduled} currency={r.currency} />,
                      },
                      {
                        id: 'p',
                        header: 'Paid',
                        align: 'right',
                        cell: (r) => <Money amount={r.paid} currency={r.currency} />,
                      },
                      {
                        id: 'r',
                        header: 'Reversed',
                        align: 'right',
                        cell: (r) => <Money amount={r.reversed} currency={r.currency} />,
                      },
                    ]}
                  />
                  {s.pendingByCurrency.some((p) => !p.converted) && (
                    <Alert tone="info">
                      Some pending rewards could not be converted (no exchange rate):{' '}
                      {s.pendingByCurrency
                        .filter((p) => !p.converted)
                        .map((p) => (
                          <Money key={p.currency} amount={p.amount} currency={p.currency} />
                        ))}
                    </Alert>
                  )}
                </CardBody>
              </Card>
            )}
          </>
        ) : null}
        <Card as="section" aria-labelledby="recent-title">
          <CardHeader titleId="recent-title" title="Recent ledger entries" />
          <CardBody>
            {recent.isError ? (
              <QueryError error={recent.error} onRetry={() => recent.refetch()} />
            ) : (
              <DataTable
                caption="Recent ledger entries"
                rows={recent.data?.items ?? []}
                loading={recent.isPending}
                getRowId={(r) => r.id}
                columns={[
                  { id: 'd', header: 'Created', cell: (r) => <DateTime value={r.createdAt} />, nowrap: true },
                  { id: 'desc', header: 'Description', primary: true, cell: (r) => r.description },
                  { id: 't', header: 'Type', cell: (r) => humanize(r.type) },
                  {
                    id: 'st',
                    header: 'Status',
                    cell: (r) => <StatusBadge kind="earning" status={r.status} size="sm" />,
                  },
                  {
                    id: 'amt',
                    header: 'Settlement',
                    align: 'right',
                    cell: (r) => (
                      <Money amount={r.settlementAmount} currency={r.settlementCurrency} colored />
                    ),
                  },
                ]}
              />
            )}
          </CardBody>
        </Card>
      </div>
      <AdjustmentDialog open={adjusting} onClose={() => setAdjusting(false)} user={picked} />
    </>
  );
}
