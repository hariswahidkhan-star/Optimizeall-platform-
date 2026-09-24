import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { Link } from 'react-router-dom';
import {
  Alert,
  Badge,
  Button,
  ConfirmDialog,
  DataTable,
  DateTime,
  Dialog,
  FormField,
  Input,
  Textarea,
  type DataTableColumn,
} from '@/components/ui';
import { useToast } from '@/components/ui/toastContext';
import { api } from '@/lib/api/client';
import { errorMessage } from '@/lib/api/errors';
import { Permissions } from '@/lib/auth/permissions';
import { useAuth } from '@/lib/auth/useAuth';
import { rk } from '../api/queries';
import type { RateAssignment } from '../api/types';
import { isoToUtcInput, utcInputToIso } from '../labels';
import { LevelBadge } from './badges';

export interface AssignmentsTableProps {
  assignments: RateAssignment[];
  caption: string;
  /** Which columns to show: who it applies to, which card, or both. */
  show?: 'target' | 'card' | 'both';
}

function Window({ a }: { a: RateAssignment }) {
  if (!a.validFrom && !a.validTo) return <span className="text-muted">Open-ended</span>;
  return (
    <span className="text-small">
      {a.validFrom ? <DateTime value={a.validFrom} format="date" /> : 'Now'} →{' '}
      {a.validTo ? <DateTime value={a.validTo} format="date" /> : 'no end'}
    </span>
  );
}

/** Assignments with their precedence level, window and state; rates.assign holders can change the window or end them. */
export function AssignmentsTable({ assignments, caption, show = 'both' }: AssignmentsTableProps) {
  const { hasPermission } = useAuth();
  const canAssign = hasPermission(Permissions.RatesAssign);
  const [ending, setEnding] = useState<RateAssignment | null>(null);
  const [editing, setEditing] = useState<RateAssignment | null>(null);
  const toast = useToast();
  const queryClient = useQueryClient();

  const columns: DataTableColumn<RateAssignment>[] = [
    { id: 'level', header: 'Level', cell: (a) => <LevelBadge level={a.level} /> },
    ...(show !== 'card'
      ? [
          {
            id: 'target',
            header: 'Applies to',
            primary: true,
            cell: (a: RateAssignment) =>
              a.person ? (
                <Link className="ui-link" to={`/admin/users/${a.person.id}#rates`}>
                  {a.person.displayName}
                </Link>
              ) : a.group ? (
                <Link className="ui-link" to={`/manage/rate-groups/${a.group.id}`}>
                  {a.group.name}
                </Link>
              ) : (
                '—'
              ),
          } satisfies DataTableColumn<RateAssignment>,
        ]
      : []),
    ...(show !== 'target'
      ? [
          {
            id: 'card',
            header: 'Rate card',
            primary: show === 'card',
            cell: (a: RateAssignment) =>
              a.card.kind === 'Custom' ? (
                <span>
                  {a.card.name}{' '}
                  <Badge size="sm" tone="brand">
                    Custom
                  </Badge>
                </span>
              ) : (
                <Link className="ui-link" to={`/manage/rate-cards/${a.card.id}`}>
                  {a.card.name}
                </Link>
              ),
          } satisfies DataTableColumn<RateAssignment>,
        ]
      : []),
    { id: 'campaign', header: 'Where', cell: (a) => a.campaign?.title ?? 'Every campaign' },
    { id: 'window', header: 'Window (UTC)', cell: (a) => <Window a={a} /> },
    {
      id: 'state',
      header: 'State',
      cell: (a) =>
        a.isActive ? (
          <Badge size="sm" tone="success" dot>
            Active
          </Badge>
        ) : (
          <Badge size="sm" tone="neutral" title={a.endReason ?? undefined}>
            {a.endedAt ? 'Ended' : 'Inactive'}
          </Badge>
        ),
    },
  ];

  return (
    <>
      <DataTable
        caption={caption}
        columns={columns}
        rows={assignments}
        getRowId={(a) => a.id}
        rowLabel={(a) => `${a.card.name} → ${a.person?.displayName ?? a.group?.name ?? ''}`}
        rowActions={
          canAssign
            ? (a) =>
                a.isActive
                  ? [
                      { id: 'window', label: 'Change window', onSelect: () => setEditing(a) },
                      { id: 'end', label: 'End assignment', danger: true, onSelect: () => setEnding(a) },
                    ]
                  : [
                      {
                        id: 'ended',
                        label: 'No actions',
                        description: a.endReason ?? 'This assignment is no longer active.',
                        disabled: true,
                      },
                    ]
            : undefined
        }
      />
      <ConfirmDialog
        open={ending !== null}
        onClose={() => setEnding(null)}
        tone="danger"
        title="End this assignment?"
        description="It stops applying to new submissions now. Submissions already made keep their price."
        requireReason
        reasonMinLength={5}
        confirmLabel="End assignment"
        onConfirm={async ({ reason }) => {
          await api.post(`/admin/rate-assignments/${ending!.id}/end`, {
            reason,
            concurrencyStamp: ending!.concurrencyStamp,
          });
          toast.success('Assignment ended');
          await queryClient.invalidateQueries({ queryKey: rk.all });
          setEnding(null);
        }}
      />
      {editing && <WindowDialog assignment={editing} onClose={() => setEditing(null)} />}
    </>
  );
}

function WindowDialog({ assignment, onClose }: { assignment: RateAssignment; onClose: () => void }) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const started = !!assignment.validFrom && new Date(assignment.validFrom) <= new Date();
  const [from, setFrom] = useState(isoToUtcInput(assignment.validFrom));
  const [to, setTo] = useState(isoToUtcInput(assignment.validTo));
  const [reason, setReason] = useState('');
  const save = useMutation({
    mutationFn: () =>
      api.put(`/admin/rate-assignments/${assignment.id}`, {
        validFrom: utcInputToIso(from),
        validTo: utcInputToIso(to),
        reason: reason.trim(),
        concurrencyStamp: assignment.concurrencyStamp,
      }),
    onSuccess: async () => {
      toast.success('Assignment window updated');
      await queryClient.invalidateQueries({ queryKey: rk.all });
      onClose();
    },
  });
  return (
    <Dialog
      open
      onClose={onClose}
      title="Change the assignment window"
      description={`${assignment.card.name} → ${assignment.person?.displayName ?? assignment.group?.name ?? ''}`}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button onClick={() => save.mutate()} loading={save.isPending} disabled={reason.trim().length < 5}>
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
        <FormField
          label="Valid from (UTC)"
          optional
          hint={started ? 'Already started — the start can no longer change.' : undefined}
        >
          <Input
            type="datetime-local"
            value={from}
            disabled={started}
            onChange={(e) => setFrom(e.target.value)}
          />
        </FormField>
        <FormField label="Valid until (UTC)" optional hint="Extend or shorten the deal. Blank = no end.">
          <Input type="datetime-local" value={to} onChange={(e) => setTo(e.target.value)} />
        </FormField>
        <FormField label="Reason" required>
          <Textarea rows={2} maxLength={500} value={reason} onChange={(e) => setReason(e.target.value)} />
        </FormField>
      </div>
    </Dialog>
  );
}
