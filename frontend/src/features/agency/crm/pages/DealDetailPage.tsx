import { FilePlus2, Pencil } from 'lucide-react';
import { useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import {
  Alert,
  Button,
  ButtonLink,
  Card,
  CardBody,
  CardHeader,
  DateTime,
  EmptyState,
  ErrorState,
  FormField,
  KeyValueList,
  Money,
  PageHeader,
  Select,
  Skeleton,
  useToast,
} from '@/components/ui';
import { billingErrorMessage, formatDateOnly } from '@/features/agency/billing/lib';
import { Permissions } from '@/lib/auth/permissions';
import { useAuth } from '@/lib/auth/useAuth';
import { useDeal, useMoveDeal, useStages } from '../api/hooks';
import type { Utm } from '../api/types';
import { ActivityPanel } from '../components/ActivityPanel';
import { ArchiveButton, ArchivedBanner } from '../components/ArchiveControls';
import { DealFormDialog, LostReasonDialog } from '../components/CrmForms';
import { DealStatusBadge, ProposalStatusBadge, SOURCE_LABELS } from '../lib';
import '@/features/agency/billing/billing.css';
import '../crm.css';

function Touch({ touch }: { touch: Utm }) {
  if (!touch.source && !touch.medium && !touch.campaign) return <>—</>;
  return <>{[touch.source, touch.medium, touch.campaign].filter(Boolean).join(' / ')}</>;
}

export function DealDetailPage() {
  const { dealId = '' } = useParams();
  const { hasPermission } = useAuth();
  const canManageCrm = hasPermission(Permissions.CrmManage);
  const canPropose = hasPermission(Permissions.ProposalsManage);
  const toast = useToast();
  const query = useDeal(dealId);
  const stages = useStages();
  const move = useMoveDeal();
  const [editing, setEditing] = useState(false);
  const [losing, setLosing] = useState<string | null>(null);

  if (query.isError) return <ErrorState error={query.error} onRetry={() => void query.refetch()} />;
  const deal = query.data;
  if (!deal) return <Skeleton height="24rem" />;
  // Archived deals are read-only until restored.
  const canManage = canManageCrm && !deal.archivedAt;
  const awaitingClient = deal.proposals.some((p) => p.status === 'Sent' || p.status === 'Viewed');

  const moveTo = async (stageId: string, lostReason?: string) => {
    try {
      await move.mutateAsync({ dealId: deal.id, stageId, lostReason, concurrencyStamp: deal.concurrencyStamp });
      toast.success('Stage updated');
    } catch (error) {
      toast.error('Couldn’t move the deal', billingErrorMessage(error));
      throw error;
    }
  };

  return (
    <>
      <PageHeader
        title={deal.title}
        breadcrumbs={[{ label: 'Sales CRM', to: '/agency/crm' }, { label: 'Pipeline', to: '/agency/crm/deals' }, { label: deal.title }]}
        meta={<DealStatusBadge status={deal.status} />}
        description={
          <>
            <Money amount={deal.value} currency={deal.currency} /> · {deal.stageName} ({deal.winProbability}%)
          </>
        }
        actions={
          <div className="crm-actions">
            {canManage && (
              <Button variant="secondary" leadingIcon={<Pencil />} onClick={() => setEditing(true)}>
                Edit
              </Button>
            )}
            {canPropose && !deal.archivedAt && (
              <ButtonLink to={`/agency/proposals/new?dealId=${deal.id}`} leadingIcon={<FilePlus2 />}>
                New proposal
              </ButtonLink>
            )}
            {canManage && (
              <ArchiveButton
                entity="deals"
                id={deal.id}
                concurrencyStamp={deal.concurrencyStamp}
                archived={false}
                disabledReason={awaitingClient ? 'A proposal on this deal is waiting for the client. Withdraw it before archiving.' : undefined}
              />
            )}
          </div>
        }
      />
      {deal.archivedAt && <ArchivedBanner entity="deals" id={deal.id} concurrencyStamp={deal.concurrencyStamp} canManage={canManageCrm} />}
      {deal.status === 'Lost' && deal.lostReason && (
        <Alert tone="neutral" title="Lost">
          {deal.lostReason}
        </Alert>
      )}
      <div className="crm-two-col">
        <div className="stack">
          <Card>
            <CardHeader title="Timeline" description="Notes, calls, meetings, emails and tasks." />
            <CardBody>
              <ActivityPanel dealId={deal.id} companyId={deal.companyId ?? undefined} contactId={deal.primaryContactId ?? undefined} />
            </CardBody>
          </Card>
        </div>
        <div className="stack">
          {canManage && (
            <Card>
              <CardHeader title="Stage" headingLevel={3} />
              <CardBody>
                <FormField label="Move to stage" hint="Moving to Lost asks for a reason.">
                  <Select
                    value={deal.stageId}
                    disabled={move.isPending}
                    options={(stages.data ?? []).filter((s) => s.isActive).map((s) => ({ value: s.id, label: `${s.name} (${s.winProbability}%)` }))}
                    onChange={(e) => {
                      const stage = stages.data?.find((s) => s.id === e.target.value);
                      if (!stage) return;
                      if (stage.kind === 'Lost') setLosing(stage.id);
                      else void moveTo(stage.id).catch(() => undefined);
                    }}
                  />
                </FormField>
              </CardBody>
            </Card>
          )}
          <Card>
            <CardHeader title="Details" headingLevel={3} />
            <CardBody>
              <KeyValueList
                items={[
                  {
                    label: 'Company',
                    value: deal.companyId ? (
                      <Link className="ui-link" to={`/agency/crm/companies/${deal.companyId}`}>
                        {deal.companyName}
                      </Link>
                    ) : (
                      '—'
                    ),
                  },
                  { label: 'Owner', value: deal.owner?.displayName ?? 'Unassigned' },
                  { label: 'Weighted value', value: <Money amount={deal.weightedValue} currency={deal.currency} /> },
                  { label: 'Expected close', value: formatDateOnly(deal.expectedCloseDate) },
                  { label: 'Source', value: `${SOURCE_LABELS[deal.source]}${deal.sourceDetail ? ` · ${deal.sourceDetail}` : ''}` },
                  { label: 'First touch', value: <Touch touch={deal.firstTouch} /> },
                  { label: 'Last touch', value: <Touch touch={deal.lastTouch} /> },
                  { label: 'Services', value: deal.serviceSlugs.join(', ') || '—' },
                  { label: 'Budget', value: deal.budgetRange ?? '—' },
                  { label: 'Created', value: <DateTime value={deal.createdAt} format="date" /> },
                ]}
              />
            </CardBody>
          </Card>
          <Card>
            <CardHeader title="Contacts" headingLevel={3} />
            <CardBody>
              {deal.contacts.length === 0 ? (
                <EmptyState compact headingLevel={4} title="No contacts" />
              ) : (
                <ul className="crm-list">
                  {deal.contacts.map((c) => (
                    <li key={c.contactId}>
                      <Link className="ui-link" to={`/agency/crm/contacts/${c.contactId}`}>
                        {c.displayName}
                      </Link>{' '}
                      <span className="crm-muted">{[c.role, c.jobTitle, c.email].filter(Boolean).join(' · ')}</span>
                    </li>
                  ))}
                </ul>
              )}
            </CardBody>
          </Card>
          <Card>
            <CardHeader title="Proposals" headingLevel={3} />
            <CardBody>
              {deal.proposals.length === 0 ? (
                <EmptyState compact headingLevel={4} title="No proposals yet" />
              ) : (
                <ul className="crm-list">
                  {deal.proposals.map((p) => (
                    <li key={p.id} className="crm-row">
                      <Link className="ui-link" to={`/agency/proposals/${p.id}`}>
                        {p.number} · v{p.currentVersion}
                      </Link>
                      <ProposalStatusBadge status={p.status} />
                      <Money amount={p.total} currency={p.currency} />
                    </li>
                  ))}
                </ul>
              )}
            </CardBody>
          </Card>
        </div>
      </div>
      <DealFormDialog open={editing} onClose={() => setEditing(false)} deal={deal} />
      <LostReasonDialog
        open={losing !== null}
        dealTitle={deal.title}
        onClose={() => setLosing(null)}
        onConfirm={async (reason) => {
          if (losing) await moveTo(losing, reason);
        }}
      />
    </>
  );
}
