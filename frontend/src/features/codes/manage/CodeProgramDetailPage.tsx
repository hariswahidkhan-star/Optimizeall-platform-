import { useMutation, useQueryClient } from '@tanstack/react-query';
import { Archive, Pause, Pencil, Play, Plus, SlidersHorizontal } from 'lucide-react';
import { useState } from 'react';
import { useParams } from 'react-router-dom';
import {
  Badge,
  Button,
  Card,
  CardBody,
  CardHeader,
  ConfirmDialog,
  DataTable,
  ErrorState,
  KeyValueList,
  Money,
  PageHeader,
  SkeletonText,
  Stat,
  Tabs,
} from '@/components/ui';
import { useToast } from '@/components/ui/toastContext';
import { SafeExternalLink } from '@/components/SafeExternalLink';
import { api } from '@/lib/api/client';
import { Permissions } from '@/lib/auth/permissions';
import { useAuth } from '@/lib/auth/useAuth';
import { formatDate } from '@/lib/format/dates';
import { invalidateCodes, useCodeProgram } from '../api/queries';
import type { CodePayoutOverride, CodeProgram, CodeProgramStatus } from '../api/types';
import { ProgramStatusBadge } from '../labels';
import { ProgramCodesTab } from './ProgramCodesTab';
import { EditPayoutDialog, EditProgramDialog, OverrideDialog } from './ProgramDialogs';
import { ProgramReportTab } from './ProgramReportTab';
import { ProgramSalesTab } from './ProgramSalesTab';
import '../codes.css';

type Tab = 'overview' | 'codes' | 'sales' | 'report';

/** Campaign manager: one discount-code program — overview and payout rules, codes, sales, report. */
export function CodeProgramDetailPage() {
  const { programId = '' } = useParams();
  const query = useCodeProgram(programId);
  const [tab, setTab] = useState<Tab>('overview');
  if (query.isPending) return <SkeletonText lines={8} />;
  if (query.isError) return <ErrorState error={query.error} onRetry={() => void query.refetch()} />;
  const p = query.data;
  return (
    <>
      <ProgramHeader program={p} />
      <Tabs
        label="Program sections"
        value={tab}
        onValueChange={(id) => setTab(id as Tab)}
        tabs={[
          { id: 'overview', label: 'Overview', content: <OverviewTab program={p} /> },
          { id: 'codes', label: `Codes (${p.stats.codes})`, content: <ProgramCodesTab program={p} /> },
          {
            id: 'sales',
            label: `Sales${p.stats.pendingSales ? ` · ${p.stats.pendingSales} to review` : ''}`,
            content: <ProgramSalesTab program={p} />,
          },
          { id: 'report', label: 'Report', content: <ProgramReportTab program={p} /> },
        ]}
      />
    </>
  );
}

function ProgramHeader({ program: p }: { program: CodeProgram }) {
  const { hasPermission } = useAuth();
  const canManage = hasPermission(Permissions.CodesManage);
  const toast = useToast();
  const client = useQueryClient();
  const [editing, setEditing] = useState(false);
  const [statusTo, setStatusTo] = useState<CodeProgramStatus | null>(null);
  const change = useMutation({
    mutationFn: ({ status, reason }: { status: CodeProgramStatus; reason: string }) =>
      api.post<CodeProgram>(`/admin/code-programs/${p.id}/status`, {
        status,
        reason: reason || null,
        concurrencyStamp: p.concurrencyStamp,
      }),
    onSuccess: async (updated) => {
      toast.success(`Program ${updated.status.toLowerCase()}`);
      await invalidateCodes(client);
    },
  });
  return (
    <>
      <PageHeader
        eyebrow={p.brandName}
        title={p.name}
        breadcrumbs={[{ label: 'Discount codes', to: '/manage/codes' }, { label: p.name }]}
        meta={
          <span className="cluster dc-cluster-sm">
            <ProgramStatusBadge status={p.status} />
            <Badge size="sm">{p.currency}</Badge>
            <span className="text-small text-muted">
              {formatDate(p.startsAt)} – {p.endsAt ? formatDate(p.endsAt) : 'open-ended'}
            </span>
          </span>
        }
        actions={
          canManage && p.status !== 'Archived' ? (
            <span className="cluster dc-cluster-sm">
              <Button variant="secondary" leadingIcon={<Pencil />} onClick={() => setEditing(true)}>
                Edit
              </Button>
              {p.status !== 'Active' && (
                <Button leadingIcon={<Play />} onClick={() => setStatusTo('Active')}>
                  {p.status === 'Draft' ? 'Open to participants' : 'Resume'}
                </Button>
              )}
              {p.status === 'Active' && (
                <Button variant="secondary" leadingIcon={<Pause />} onClick={() => setStatusTo('Paused')}>
                  Pause
                </Button>
              )}
              <Button variant="ghost" leadingIcon={<Archive />} onClick={() => setStatusTo('Archived')}>
                Archive
              </Button>
            </span>
          ) : undefined
        }
      />
      {editing && <EditProgramDialog program={p} onClose={() => setEditing(false)} />}
      <ConfirmDialog
        open={!!statusTo}
        onClose={() => setStatusTo(null)}
        title={
          statusTo === 'Archived'
            ? 'Archive this program?'
            : statusTo === 'Paused'
              ? 'Pause this program?'
              : 'Open the program?'
        }
        description={
          statusTo === 'Archived'
            ? 'It becomes read-only. Decide every pending sale first; approved commissions are unaffected.'
            : statusTo === 'Paused'
              ? 'Participants can’t report new sales until you resume it. Pending sales can still be reviewed.'
              : 'Participants holding codes see them and can report sales.'
        }
        tone={statusTo === 'Archived' ? 'danger' : 'primary'}
        requireReason={statusTo === 'Archived'}
        reasonMinLength={5}
        confirmLabel={statusTo === 'Archived' ? 'Archive' : statusTo === 'Paused' ? 'Pause' : 'Open'}
        onConfirm={async ({ reason }) => {
          if (!statusTo) return;
          await change.mutateAsync({ status: statusTo, reason });
          setStatusTo(null);
        }}
      />
    </>
  );
}

function OverviewTab({ program: p }: { program: CodeProgram }) {
  const { hasPermission } = useAuth();
  const canManage = hasPermission(Permissions.CodesManage) && p.status !== 'Archived';
  const toast = useToast();
  const client = useQueryClient();
  const [payout, setPayout] = useState(false);
  const [override, setOverride] = useState(false);
  const [ending, setEnding] = useState<CodePayoutOverride | null>(null);
  const end = useMutation({
    mutationFn: ({ o, reason }: { o: CodePayoutOverride; reason: string }) =>
      api.post(`/admin/code-programs/${p.id}/overrides/${o.id}/end`, { reason }),
    onSuccess: async () => {
      toast.success('Override ended');
      await invalidateCodes(client);
    },
  });
  const c = p.currency;
  return (
    <div className="stack">
      <div className="dc-stats">
        <Stat
          label="Codes assigned"
          measurement="Count"
          value={`${p.stats.assignedCodes} / ${p.stats.codes}`}
        />
        <Stat label="Available codes" measurement="Count" value={p.stats.availableCodes} />
        <Stat label="Sales to review" measurement="Count" value={p.stats.pendingSales} />
        <Stat label="Approved sales" measurement="Count" value={p.stats.approvedSales} />
        <Stat
          label="Commission recorded"
          value={<Money amount={p.stats.commissionApproved} currency={c} />}
        />
        {p.stats.budgetRemaining !== null && (
          <Stat label="Budget left" value={<Money amount={p.stats.budgetRemaining} currency={c} />} />
        )}
      </div>
      <div className="dc-grid-2">
        <Card as="section" aria-labelledby="payout-rules">
          <CardHeader
            titleId="payout-rules"
            title="Payout rules"
            description={`Version ${p.payoutVersion}. Applied when a sale is approved.`}
            actions={
              canManage ? (
                <Button
                  size="sm"
                  variant="secondary"
                  leadingIcon={<SlidersHorizontal />}
                  onClick={() => setPayout(true)}
                >
                  Change
                </Button>
              ) : undefined
            }
          />
          <CardBody className="stack">
            <KeyValueList
              items={[
                {
                  label: 'Per approved sale',
                  value:
                    p.payoutType === 'FlatPerSale' ? (
                      <Money amount={p.flatAmount} currency={c} />
                    ) : (
                      `${p.percent}% of the order value (net)`
                    ),
                },
                ...p.tiers.map((t) => ({
                  label: `From ${t.thresholdSales} sales`,
                  value: [
                    t.flatAmount !== null ? `${t.flatAmount} ${c} per further sale` : null,
                    t.percent !== null ? `${t.percent}% of net on further sales` : null,
                    t.bonusAmount !== null ? `bonus ${t.bonusAmount} ${c}` : null,
                  ]
                    .filter(Boolean)
                    .join(', '),
                })),
                {
                  label: 'Daily cap per person',
                  value:
                    p.dailyCapPerPerson !== null ? (
                      <Money amount={p.dailyCapPerPerson} currency={c} />
                    ) : (
                      'None'
                    ),
                },
                {
                  label: 'Cap per person',
                  value:
                    p.programCapPerPerson !== null ? (
                      <Money amount={p.programCapPerPerson} currency={c} />
                    ) : (
                      'None'
                    ),
                },
                {
                  label: 'Program budget',
                  value:
                    p.budgetAmount !== null ? <Money amount={p.budgetAmount} currency={c} /> : 'Unlimited',
                },
              ]}
            />
          </CardBody>
        </Card>
        <Card as="section" aria-labelledby="program-info">
          <CardHeader titleId="program-info" title="Program" />
          <CardBody className="stack">
            <KeyValueList
              layout="inline"
              items={[
                { label: 'Customers get', value: p.discountLabel ?? '—' },
                {
                  label: 'Store',
                  value: p.storeUrl ? (
                    <SafeExternalLink href={p.storeUrl}>{p.storeUrl}</SafeExternalLink>
                  ) : (
                    '—'
                  ),
                },
                { label: 'Report within', value: `${p.maxOrderAgeDays} days` },
                { label: 'Proof', value: p.requireProof ? 'Required' : 'Optional' },
                ...(p.campaign ? [{ label: 'Campaign', value: p.campaign.name }] : []),
                ...(p.client ? [{ label: 'Client', value: p.client.name }] : []),
              ]}
            />
            {p.terms && (
              <p className="text-small text-muted" style={{ whiteSpace: 'pre-wrap', margin: 0 }}>
                {p.terms}
              </p>
            )}
          </CardBody>
        </Card>
      </div>
      <Card as="section" aria-labelledby="overrides">
        <CardHeader
          titleId="overrides"
          title="Negotiated payouts"
          description="Per-person or per-rate-group rates that replace the program rate (a person’s beats a group’s)."
          actions={
            canManage ? (
              <Button size="sm" variant="secondary" leadingIcon={<Plus />} onClick={() => setOverride(true)}>
                Add override
              </Button>
            ) : undefined
          }
        />
        <CardBody>
          <DataTable
            caption="Payout overrides"
            rows={p.overrides}
            getRowId={(o) => o.id}
            rowActions={(o) =>
              canManage && o.isActive
                ? [{ id: 'end', label: 'End override', danger: true, onSelect: () => setEnding(o) }]
                : []
            }
            columns={[
              {
                id: 'who',
                header: 'For',
                primary: true,
                cell: (o) => (
                  <span className="cluster dc-cluster-sm">
                    {o.person?.displayName ?? o.group?.name}
                    <Badge size="sm" tone={o.target === 'Group' ? 'info' : 'brand'}>
                      {o.target === 'Group' ? 'Rate group' : 'Person'}
                    </Badge>
                  </span>
                ),
              },
              { id: 'rate', header: 'Payout', cell: (o) => o.description },
              {
                id: 'reason',
                header: 'Reason',
                hideOnMobile: true,
                cell: (o) => <span className="text-small">{o.reason}</span>,
              },
              {
                id: 'state',
                header: 'State',
                cell: (o) =>
                  o.isActive ? (
                    <Badge tone="success" size="sm">
                      Active
                    </Badge>
                  ) : (
                    <span className="text-small text-muted">
                      Ended {o.endedAt ? formatDate(o.endedAt) : ''}
                    </span>
                  ),
              },
            ]}
            emptyState={<p className="text-small text-muted">Everyone is paid the program rate.</p>}
          />
        </CardBody>
      </Card>
      {payout && <EditPayoutDialog program={p} onClose={() => setPayout(false)} />}
      {override && <OverrideDialog program={p} onClose={() => setOverride(false)} />}
      <ConfirmDialog
        open={!!ending}
        onClose={() => setEnding(null)}
        title="End this override?"
        description="Sales approved from now on are paid the program rate (or the group’s override)."
        tone="danger"
        requireReason
        reasonMinLength={5}
        confirmLabel="End override"
        onConfirm={async ({ reason }) => {
          if (!ending) return;
          await end.mutateAsync({ o: ending, reason });
          setEnding(null);
        }}
      />
    </div>
  );
}
