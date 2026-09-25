import { useMutation, useQueryClient } from '@tanstack/react-query';
import { Archive, CheckCircle2, Copy, Link2, Pencil, Plus, Power, XCircle } from 'lucide-react';
import { useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import {
  Alert,
  Badge,
  Button,
  Checkbox,
  ConfirmDialog,
  DataTable,
  DateTime,
  Dialog,
  EmptyState,
  ErrorState,
  FormField,
  Input,
  KeyValueList,
  Money,
  PageHeader,
  Skeleton,
  Stat,
  Tabs,
  Textarea,
  type DataTableColumn,
} from '@/components/ui';
import { useToast } from '@/components/ui/toastContext';
import { api } from '@/lib/api/client';
import { ApiError, errorMessage } from '@/lib/api/errors';
import { Permissions } from '@/lib/auth/permissions';
import { useAuth } from '@/lib/auth/useAuth';
import { rk, useRateCard } from '../api/queries';
import type { RateCard, RateCardVersion } from '../api/types';
import { AssignRateDialog } from '../components/AssignRateDialog';
import { AssignmentsTable } from '../components/AssignmentsTable';
import { CardStatusBadge, VersionStatusBadge } from '../components/badges';
import {
  RateLinesEditor,
  ratesFromVersion,
  ratesHints,
  ratesToInput,
  type RatesForm,
} from '../components/RateLinesEditor';
import { lineConditions, utcInputToIso } from '../labels';
import '../rates.css';

export function RateCardDetailPage() {
  const { cardId = '' } = useParams();
  const { hasPermission, user } = useAuth();
  const canManage = hasPermission(Permissions.RatesManage);
  const canAssign = hasPermission(Permissions.RatesAssign);
  const query = useRateCard(cardId);
  const [dialog, setDialog] = useState<'edit' | 'version' | 'archive' | 'duplicate' | 'assign' | null>(null);
  const toast = useToast();
  const queryClient = useQueryClient();
  const refresh = () => queryClient.invalidateQueries({ queryKey: rk.all });

  const activate = useMutation({
    mutationFn: (card: RateCard) =>
      api.post<RateCard>(`/admin/rate-cards/${card.id}/activate`, {
        concurrencyStamp: card.concurrencyStamp,
      }),
    onSuccess: async () => {
      toast.success('Rate card activated');
      await refresh();
    },
    onError: (e) => toast.error('Could not activate the card', errorMessage(e)),
  });
  const decide = useMutation({
    mutationFn: ({ version, approve, note }: { version: number; approve: boolean; note: string }) =>
      api.post<RateCard>(
        `/admin/rate-cards/${cardId}/versions/${version}/${approve ? 'approve' : 'reject'}`,
        approve ? { note: note || null } : { reason: note },
      ),
    onSuccess: async (_, v) => {
      toast.success(v.approve ? 'Rate increase approved' : 'Rate increase rejected');
      await refresh();
    },
    onError: (e) => toast.error('Could not record the decision', errorMessage(e)),
  });
  const [rejecting, setRejecting] = useState<number | null>(null);

  const breadcrumbs = [
    { label: 'Rate cards', to: '/manage/rate-cards' },
    { label: query.data?.name ?? 'Rate card' },
  ];
  if (query.isPending)
    return <PageHeader title={<Skeleton width={240} height={32} />} breadcrumbs={breadcrumbs} />;
  if (query.isError)
    return (
      <>
        <PageHeader title="Rate card" breadcrumbs={breadcrumbs} />
        <ErrorState error={query.error} onRetry={() => void query.refetch()} />
      </>
    );

  const card = query.data;
  const current =
    card.versions.find((v) => v.isCurrent) ?? card.versions.find((v) => v.status === 'Approved');
  const pending = card.versions.find((v) => v.status === 'PendingApproval');
  const archived = card.status === 'Archived';
  const pendingMine = pending?.createdBy?.id === user?.id;

  return (
    <>
      <PageHeader
        title={card.name}
        breadcrumbs={breadcrumbs}
        description={card.description ?? undefined}
        meta={
          <span className="cluster rt-cluster-sm">
            <CardStatusBadge status={card.status} />
            {card.kind === 'Custom' && (
              <Badge size="sm" tone="brand">
                Custom rate{card.owner ? ` · ${card.owner.displayName}` : ''}
              </Badge>
            )}
            <Badge size="sm">{card.currency}</Badge>
            <Badge size="sm">Version {card.currentVersion}</Badge>
          </span>
        }
        actions={
          <div className="cluster rt-cluster-sm">
            {canAssign && card.status === 'Active' && card.kind === 'Standard' && (
              <Button leadingIcon={<Link2 />} onClick={() => setDialog('assign')}>
                Assign
              </Button>
            )}
            {canManage && !archived && (
              <Button
                variant="secondary"
                leadingIcon={<Plus />}
                onClick={() => setDialog('version')}
                disabled={!!pending}
                title={pending ? 'A rate increase is awaiting approval' : undefined}
              >
                New version
              </Button>
            )}
            {canManage && card.status === 'Draft' && (
              <Button
                variant="secondary"
                leadingIcon={<Power />}
                loading={activate.isPending}
                onClick={() => activate.mutate(card)}
              >
                Activate
              </Button>
            )}
            {canManage && !archived && (
              <Button variant="ghost" leadingIcon={<Pencil />} onClick={() => setDialog('edit')}>
                Edit details
              </Button>
            )}
            {canManage && card.kind === 'Standard' && (
              <Button variant="ghost" leadingIcon={<Copy />} onClick={() => setDialog('duplicate')}>
                Duplicate
              </Button>
            )}
            {canManage && !archived && (
              <Button variant="ghost" leadingIcon={<Archive />} onClick={() => setDialog('archive')}>
                Archive
              </Button>
            )}
          </div>
        }
      />

      <div className="stack">
        {archived && (
          <Alert tone="warning" title="Archived">
            {card.archiveReason ?? 'This card no longer prices new submissions.'} Submissions it already
            priced keep their price.
          </Alert>
        )}
        {pending && (
          <Alert
            tone="warning"
            title={`Version ${pending.version} raises rates by up to ${pending.maxIncreasePercent ?? '?'}% and needs a second approval`}
            actions={
              canManage ? (
                <div className="cluster rt-cluster-sm">
                  <Button
                    size="sm"
                    leadingIcon={<CheckCircle2 />}
                    disabled={pendingMine}
                    title={pendingMine ? 'Someone else must approve your rate increase' : undefined}
                    loading={decide.isPending}
                    onClick={() => decide.mutate({ version: pending.version, approve: true, note: '' })}
                  >
                    Approve
                  </Button>
                  <Button
                    size="sm"
                    variant="secondary"
                    leadingIcon={<XCircle />}
                    onClick={() => setRejecting(pending.version)}
                  >
                    Reject
                  </Button>
                </div>
              ) : undefined
            }
          >
            Proposed by {pending.createdBy?.displayName ?? 'someone'}: “{pending.reason}”. Until approved,
            version {card.currentVersion} keeps pricing posts.
            {card.fourEyesRequiredAbovePercent && ` Four-eyes threshold: ${card.fourEyesThresholdPercent}%.`}
          </Alert>
        )}

        <div className="grid-auto rt-stat-grid">
          <Stat label="Rates in force" value={String(current?.lines.length ?? 0)} measurement="Count" />
          <Stat
            label="Active assignments"
            value={String(card.assignments.filter((a) => a.isActive).length)}
            measurement="Count"
          />
          <Stat
            label="Submissions priced"
            value={String(card.usedBySubmissions)}
            measurement="Count"
            hint="Locked at submission"
          />
        </div>

        <Tabs
          label="Rate card sections"
          tabs={[
            {
              id: 'rates',
              label: 'Current rates',
              content: current ? (
                <VersionRates version={current} />
              ) : (
                <p className="text-muted">No approved version yet.</p>
              ),
            },
            {
              id: 'versions',
              label: 'Versions',
              badge: card.versions.length,
              content: (
                <ol className="rt-versions">
                  {card.versions.map((v) => (
                    <li key={v.id} className="rt-version">
                      <div className="cluster rt-cluster-sm">
                        <strong>Version {v.version}</strong>
                        {v.isCurrent && (
                          <Badge size="sm" tone="success">
                            In force
                          </Badge>
                        )}
                        <VersionStatusBadge status={v.status} />
                        <span className="text-small text-muted">
                          effective <DateTime value={v.effectiveFrom} format="both" /> · by{' '}
                          {v.createdBy?.displayName ?? 'unknown'}
                        </span>
                      </div>
                      <p className="text-small">
                        “{v.reason}”{v.decisionNote && ` — decision: “${v.decisionNote}”`}
                      </p>
                      <VersionRates version={v} compact />
                    </li>
                  ))}
                </ol>
              ),
            },
            {
              id: 'assignments',
              label: 'Assignments',
              badge: card.assignments.length,
              content:
                card.assignments.length === 0 ? (
                  <EmptyState
                    title="Not assigned yet"
                    description="Assign the card to a person or a rate group."
                    headingLevel={3}
                  />
                ) : (
                  <AssignmentsTable
                    assignments={card.assignments}
                    caption="Assignments of this card"
                    show="target"
                  />
                ),
            },
          ]}
        />
      </div>

      {dialog === 'assign' && (
        <AssignRateDialog onClose={() => setDialog(null)} card={{ id: card.id, name: card.name }} />
      )}
      {dialog === 'version' && (
        <NewVersionDialog card={card} base={current} onClose={() => setDialog(null)} />
      )}
      {dialog === 'edit' && <EditCardDialog card={card} onClose={() => setDialog(null)} />}
      {dialog === 'duplicate' && <DuplicateDialog card={card} onClose={() => setDialog(null)} />}
      {dialog === 'archive' && <ArchiveDialog card={card} onClose={() => setDialog(null)} />}
      <ConfirmDialog
        open={rejecting !== null}
        onClose={() => setRejecting(null)}
        title="Reject the rate increase?"
        description="The current version keeps pricing posts."
        tone="danger"
        requireReason
        reasonMinLength={5}
        confirmLabel="Reject"
        onConfirm={async ({ reason }) => {
          await decide.mutateAsync({ version: rejecting!, approve: false, note: reason });
          setRejecting(null);
        }}
      />
    </>
  );
}

function VersionRates({ version, compact }: { version: RateCardVersion; compact?: boolean }) {
  const columns: DataTableColumn<RateCardVersion['lines'][number]>[] = [
    { id: 'conditions', header: 'Applies to', primary: true, cell: (l) => lineConditions(l) },
    {
      id: 'amount',
      header: 'Per approved post',
      align: 'right',
      cell: (l) => <Money amount={l.amount} currency={version.currency} />,
    },
    {
      id: 'label',
      header: 'Participant label',
      hideOnMobile: true,
      cell: (l) => l.label ?? <span className="text-muted">Post reward</span>,
    },
  ];
  return (
    <div className="stack rt-stack-sm">
      <DataTable
        caption={`Rates of version ${version.version}`}
        columns={columns}
        rows={version.lines}
        getRowId={(l) => l.id}
      />
      {!compact && (
        <KeyValueList
          layout="inline"
          className="rt-terms"
          items={[
            { label: 'Currency', value: version.currency },
            {
              label: 'Daily cap',
              value:
                version.dailyCapPerParticipant === null ? (
                  'None'
                ) : (
                  <Money amount={version.dailyCapPerParticipant} currency={version.currency} />
                ),
            },
            {
              label: 'Weekly cap',
              value:
                version.weeklyCapPerParticipant === null ? (
                  'None'
                ) : (
                  <Money amount={version.weeklyCapPerParticipant} currency={version.currency} />
                ),
            },
            {
              label: 'Campaign cap',
              value:
                version.campaignCapPerParticipant === null ? (
                  'None'
                ) : (
                  <Money amount={version.campaignCapPerParticipant} currency={version.currency} />
                ),
            },
            {
              label: 'Campaign bonuses',
              value: version.stackCampaignBonuses ? 'Stack on top' : 'Not added (all-inclusive)',
            },
          ]}
        />
      )}
    </div>
  );
}

function NewVersionDialog({
  card,
  base,
  onClose,
}: {
  card: RateCard;
  base: RateCardVersion | undefined;
  onClose: () => void;
}) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const [rates, setRates] = useState<RatesForm>(() => ratesFromVersion(base, card.currency));
  const [reason, setReason] = useState('');
  const [effectiveFrom, setEffectiveFrom] = useState('');
  const latest = Math.max(0, ...card.versions.map((v) => v.version));
  const hints = ratesHints(rates);
  const save = useMutation({
    mutationFn: () =>
      api.post<RateCard>(`/admin/rate-cards/${card.id}/versions`, {
        ...ratesToInput(rates),
        reason: reason.trim(),
        effectiveFrom: utcInputToIso(effectiveFrom),
        baseVersion: latest,
        confirm: true,
      }),
    onSuccess: async (updated) => {
      const v = updated.versions[0];
      toast.success(
        v?.status === 'PendingApproval'
          ? 'Saved — awaiting a second approval'
          : `Version ${v?.version ?? ''} saved`,
        v?.status === 'PendingApproval' ? 'The raise is above the four-eyes threshold.' : undefined,
      );
      await queryClient.invalidateQueries({ queryKey: rk.all });
      onClose();
    },
  });
  const conflict = save.error instanceof ApiError && save.error.status === 409;
  return (
    <Dialog
      open
      onClose={onClose}
      size="lg"
      title={`New version of ${card.name}`}
      description="Versions are immutable. Posts already submitted keep the rate they were priced with."
      dismissible={!save.isPending}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={save.isPending}>
            Cancel
          </Button>
          <Button
            onClick={() => save.mutate()}
            loading={save.isPending}
            disabled={reason.trim().length < 5 || hints.length > 0}
          >
            Save version {latest + 1}
          </Button>
        </>
      }
    >
      <div className="stack">
        {save.isError && (
          <Alert tone="danger" title={conflict ? 'Someone else changed this card' : 'Not saved'}>
            {errorMessage(save.error)}
          </Alert>
        )}
        <RateLinesEditor value={rates} onChange={setRates} idPrefix="new-version" />
        {hints.length > 0 && (
          <Alert tone="warning" title="Check the rates">
            <ul className="rt-list">
              {hints.map((h) => (
                <li key={h}>{h}</li>
              ))}
            </ul>
          </Alert>
        )}
        <FormField
          label="Effective from (UTC)"
          optional
          hint="Blank = now. Use a future time to schedule a price change."
        >
          <Input
            type="datetime-local"
            value={effectiveFrom}
            onChange={(e) => setEffectiveFrom(e.target.value)}
          />
        </FormField>
        <FormField label="Reason" required hint="At least 5 characters. Audited.">
          <Textarea rows={2} maxLength={500} value={reason} onChange={(e) => setReason(e.target.value)} />
        </FormField>
      </div>
    </Dialog>
  );
}

function EditCardDialog({ card, onClose }: { card: RateCard; onClose: () => void }) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const [name, setName] = useState(card.name);
  const [description, setDescription] = useState(card.description ?? '');
  const save = useMutation({
    mutationFn: () =>
      api.put<RateCard>(`/admin/rate-cards/${card.id}`, {
        name: name.trim(),
        description: description.trim() || null,
        concurrencyStamp: card.concurrencyStamp,
      }),
    onSuccess: async () => {
      toast.success('Rate card updated');
      await queryClient.invalidateQueries({ queryKey: rk.all });
      onClose();
    },
  });
  return (
    <Dialog
      open
      onClose={onClose}
      title="Edit rate card details"
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button onClick={() => save.mutate()} loading={save.isPending} disabled={name.trim().length < 2}>
            Save
          </Button>
        </>
      }
    >
      <div className="stack">
        {save.isError && (
          <Alert tone="danger" title="Not saved">
            {errorMessage(save.error)}
          </Alert>
        )}
        <FormField label="Name" required>
          <Input value={name} maxLength={120} onChange={(e) => setName(e.target.value)} />
        </FormField>
        <FormField label="Description" optional>
          <Textarea
            rows={2}
            maxLength={500}
            value={description}
            onChange={(e) => setDescription(e.target.value)}
          />
        </FormField>
      </div>
    </Dialog>
  );
}

function DuplicateDialog({ card, onClose }: { card: RateCard; onClose: () => void }) {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [name, setName] = useState(`${card.name} (copy)`);
  const save = useMutation({
    mutationFn: () => api.post<RateCard>(`/admin/rate-cards/${card.id}/duplicate`, { name: name.trim() }),
    onSuccess: async (copy) => {
      await queryClient.invalidateQueries({ queryKey: rk.all });
      onClose();
      navigate(`/manage/rate-cards/${copy.id}`);
    },
  });
  return (
    <Dialog
      open
      onClose={onClose}
      title="Duplicate rate card"
      description="Copies the rates in force into a new draft card."
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button onClick={() => save.mutate()} loading={save.isPending} disabled={name.trim().length < 2}>
            Duplicate
          </Button>
        </>
      }
    >
      <div className="stack">
        {save.isError && (
          <Alert tone="danger" title="Not duplicated">
            {errorMessage(save.error)}
          </Alert>
        )}
        <FormField label="Name of the copy" required>
          <Input value={name} maxLength={120} onChange={(e) => setName(e.target.value)} />
        </FormField>
      </div>
    </Dialog>
  );
}

function ArchiveDialog({ card, onClose }: { card: RateCard; onClose: () => void }) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const active = card.assignments.filter((a) => a.isActive).length;
  const [endAssignments, setEndAssignments] = useState(false);
  return (
    <ConfirmDialog
      open
      onClose={onClose}
      tone="danger"
      title={`Archive ${card.name}?`}
      description="It stops pricing new submissions. Submissions it already priced keep their price."
      requireReason
      reasonMinLength={5}
      confirmLabel="Archive"
      onConfirm={async ({ reason }) => {
        await api.post(`/admin/rate-cards/${card.id}/archive`, {
          reason,
          endAssignments,
          concurrencyStamp: card.concurrencyStamp,
        });
        toast.success('Rate card archived');
        await queryClient.invalidateQueries({ queryKey: rk.all });
        onClose();
      }}
    >
      {active > 0 && (
        <Checkbox
          label={`Also end its ${active} active assignment${active === 1 ? '' : 's'}`}
          description="Required: a card that is still assigned can't be archived."
          checked={endAssignments}
          onChange={(e) => setEndAssignments(e.target.checked)}
        />
      )}
    </ConfirmDialog>
  );
}
