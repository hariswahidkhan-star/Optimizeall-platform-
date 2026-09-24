import { Archive, ArchiveRestore } from 'lucide-react';
import { useState } from 'react';
import { Alert, Button, ConfirmDialog, FormField, Input, Select, useToast } from '@/components/ui';
import { FormDialog } from '@/features/agency/billing/components/FormDialog';
import { billingErrorMessage } from '@/features/agency/billing/lib';
import { useArchiveRecord, useAssignees, useBulkAction, type CrmEntity } from '../api/hooks';
import type { CrmBulkAction, LifecycleStage } from '../api/types';
import { LIFECYCLE_OPTIONS } from '../lib';

const NOUN: Record<CrmEntity, string> = { contacts: 'contact', companies: 'company', deals: 'deal' };

/** Explains why an archived record can't be edited, with a restore button when allowed. */
export function ArchivedBanner({
  entity,
  id,
  concurrencyStamp,
  canManage,
}: {
  entity: CrmEntity;
  id: string;
  concurrencyStamp: string;
  canManage: boolean;
}) {
  return (
    <Alert tone="warning" title={`This ${NOUN[entity]} is archived`}>
      <p>It’s hidden from lists and pickers, and it can’t be edited until it’s restored. Its history is kept.</p>
      {canManage && <ArchiveButton entity={entity} id={id} concurrencyStamp={concurrencyStamp} archived />}
    </Alert>
  );
}

/** Archive (with confirmation) or restore a single record. */
export function ArchiveButton({
  entity,
  id,
  concurrencyStamp,
  archived,
  disabledReason,
}: {
  entity: CrmEntity;
  id: string;
  concurrencyStamp: string;
  archived: boolean;
  /** When set, archiving is unavailable and this explains why. */
  disabledReason?: string;
}) {
  const archive = useArchiveRecord(entity);
  const toast = useToast();
  const [confirming, setConfirming] = useState(false);
  const noun = NOUN[entity];
  if (archived)
    return (
      <Button
        variant="secondary"
        size="sm"
        leadingIcon={<ArchiveRestore />}
        loading={archive.isPending}
        onClick={async () => {
          try {
            await archive.mutateAsync({ id, archive: false, concurrencyStamp });
            toast.success(`Restored the ${noun}`);
          } catch (error) {
            toast.error('Couldn’t restore', billingErrorMessage(error));
          }
        }}
      >
        Restore
      </Button>
    );
  return (
    <>
      <Button
        variant="secondary"
        leadingIcon={<Archive />}
        onClick={() => setConfirming(true)}
        disabled={!!disabledReason}
        title={disabledReason}
        aria-describedby={disabledReason ? `${id}-archive-reason` : undefined}
      >
        Archive
      </Button>
      {disabledReason && (
        <span id={`${id}-archive-reason`} className="visually-hidden">
          {disabledReason}
        </span>
      )}
      <ConfirmDialog
        open={confirming}
        onClose={() => setConfirming(false)}
        title={`Archive this ${noun}?`}
        description={`The ${noun} is hidden from lists and pickers and becomes read-only. You can restore it any time from the Archived filter.`}
        confirmLabel="Archive"
        onConfirm={async () => {
          await archive.mutateAsync({ id, archive: true, concurrencyStamp });
          toast.success(`Archived the ${noun}`);
        }}
      />
    </>
  );
}

/** Active / Archived list filter. */
export function ArchivedFilter({ value, onChange }: { value: boolean; onChange: (archived: boolean) => void }) {
  return (
    <FormField label="Show">
      <Select
        value={value ? 'archived' : 'active'}
        options={[
          { value: 'active', label: 'Active' },
          { value: 'archived', label: 'Archived' },
        ]}
        onChange={(e) => onChange(e.target.value === 'archived')}
      />
    </FormField>
  );
}

const ACTIONS: Record<CrmEntity, { value: CrmBulkAction; label: string }[]> = {
  contacts: [
    { value: 'assignOwner', label: 'Assign owner' },
    { value: 'setLifecycle', label: 'Set lifecycle stage' },
    { value: 'addTag', label: 'Add tag' },
    { value: 'removeTag', label: 'Remove tag' },
    { value: 'archive', label: 'Archive' },
  ],
  companies: [
    { value: 'assignOwner', label: 'Assign owner' },
    { value: 'addTag', label: 'Add tag' },
    { value: 'removeTag', label: 'Remove tag' },
    { value: 'archive', label: 'Archive' },
  ],
  deals: [
    { value: 'assignOwner', label: 'Assign owner' },
    { value: 'archive', label: 'Archive' },
  ],
};

/**
 * Toolbar shown while rows are selected: one action for all selected records. In the Archived view only Restore is
 * offered (archived records are read-only).
 */
export function BulkActionsBar({
  entity,
  selectedIds,
  archivedView,
  onDone,
}: {
  entity: CrmEntity;
  selectedIds: string[];
  archivedView: boolean;
  onDone: () => void;
}) {
  const bulk = useBulkAction(entity);
  const assignees = useAssignees();
  const toast = useToast();
  const [action, setAction] = useState<CrmBulkAction | null>(null);
  const [owner, setOwner] = useState('');
  const [stage, setStage] = useState<LifecycleStage>('Lead');
  const [tag, setTag] = useState('');
  const count = selectedIds.length;
  const noun = count === 1 ? NOUN[entity] : entity;

  const run = async (chosen: CrmBulkAction) => {
    const result = await bulk.mutateAsync({
      ids: selectedIds,
      action: chosen,
      ownerUserId: chosen === 'assignOwner' ? owner || null : undefined,
      lifecycleStage: chosen === 'setLifecycle' ? stage : undefined,
      tag: chosen === 'addTag' || chosen === 'removeTag' ? tag : undefined,
    });
    const skipped = result.requested - result.updated;
    toast.success(
      `Updated ${result.updated} of ${result.requested}`,
      skipped > 0 ? `${skipped} ${skipped === 1 ? 'was' : 'were'} already in that state or can’t take this action.` : undefined,
    );
    onDone();
  };

  if (archivedView)
    return (
      <Button
        size="sm"
        leadingIcon={<ArchiveRestore />}
        loading={bulk.isPending}
        onClick={async () => {
          try {
            await run('restore');
          } catch (error) {
            toast.error('Couldn’t restore', billingErrorMessage(error));
          }
        }}
      >
        Restore {count} {noun}
      </Button>
    );

  return (
    <>
      <div className="crm-actions" role="group" aria-label={`Bulk actions for ${count} selected ${noun}`}>
        {ACTIONS[entity].map((a) => (
          <Button key={a.value} size="sm" variant={a.value === 'archive' ? 'danger' : 'secondary'} onClick={() => setAction(a.value)}>
            {a.label}
          </Button>
        ))}
      </div>
      <FormDialog
        open={action !== null}
        onClose={() => setAction(null)}
        title={`${ACTIONS[entity].find((a) => a.value === action)?.label ?? ''}: ${count} ${noun}`}
        description={
          action === 'archive'
            ? 'Archived records are hidden from lists and become read-only. Deals with a proposal waiting for the client, and companies that are clients, are skipped.'
            : undefined
        }
        submitLabel={action === 'archive' ? 'Archive' : 'Apply'}
        tone={action === 'archive' ? 'danger' : 'primary'}
        canSubmit={!((action === 'addTag' || action === 'removeTag') && !tag.trim())}
        onSubmit={async () => {
          if (action) await run(action);
        }}
      >
        {action === 'assignOwner' && (
          <FormField label="Owner">
            <Select
              value={owner}
              options={[{ value: '', label: 'Unassigned' }, ...(assignees.data ?? []).map((u) => ({ value: u.id, label: u.displayName }))]}
              onChange={(e) => setOwner(e.target.value)}
            />
          </FormField>
        )}
        {action === 'setLifecycle' && (
          <FormField label="Lifecycle stage">
            <Select value={stage} options={LIFECYCLE_OPTIONS} onChange={(e) => setStage(e.target.value as LifecycleStage)} />
          </FormField>
        )}
        {(action === 'addTag' || action === 'removeTag') && (
          <FormField label="Tag" required>
            <Input value={tag} maxLength={40} onChange={(e) => setTag(e.target.value)} />
          </FormField>
        )}
      </FormDialog>
    </>
  );
}
