import { useMutation, useQueryClient } from '@tanstack/react-query';
import { Archive, ArchiveRestore, Copy, Plus, Trash2, Workflow } from 'lucide-react';
import { useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { Badge } from '@/components/ui/Badge';
import { ButtonLink } from '@/components/ui/ButtonLink';
import { Checkbox } from '@/components/ui/Checkbox';
import { ConfirmDialog } from '@/components/ui/ConfirmDialog';
import { Card, CardBody } from '@/components/ui/Card';
import { DataTable } from '@/components/ui/DataTable';
import { DateTime } from '@/components/ui/DateTime';
import { EmptyState } from '@/components/ui/EmptyState';
import { ErrorState } from '@/components/ui/ErrorState';
import { PageHeader } from '@/components/ui/PageHeader';
import type { Tone } from '@/components/ui/tones';
import { useToast } from '@/components/ui/toastContext';
import { api } from '@/lib/api/client';
import { errorMessage } from '@/lib/api/errors';
import { formatNumber } from '@/lib/format/money';
import { humanize } from '@/lib/format/text';
import { EMAIL_API, emailKeys, useAutomations } from '../api/queries';
import type { Automation, AutomationListItem, AutomationStatus } from '../api/types';
import { useEmailWorkspace } from '../shared/workspace';

const statusTones: Record<AutomationStatus, Tone> = { Draft: 'neutral', Active: 'success', Paused: 'warning', Archived: 'neutral' };

export function AutomationStatusBadge({ status }: { status: AutomationStatus }) {
  return (
    <Badge tone={statusTones[status]} dot>
      {status}
    </Badge>
  );
}

/** Journeys of the selected workspace, with enrollment counts. */
export function AutomationsPage() {
  const { clientId, key } = useEmailWorkspace();
  const [showArchived, setShowArchived] = useState(false);
  const automations = useAutomations(clientId, showArchived);
  const [pending, setPending] = useState<{ row: AutomationListItem; action: 'archive' | 'delete' } | null>(null);
  const queryClient = useQueryClient();
  const toast = useToast();
  const navigate = useNavigate();
  const refresh = () => void queryClient.invalidateQueries({ queryKey: emailKeys.automations(key) });
  const duplicate = useMutation({
    mutationFn: (a: AutomationListItem) => api.post<Automation>(`${EMAIL_API}/automations/${a.id}/duplicate`),
    onSuccess: (a) => {
      toast.success('Journey duplicated', 'The copy is a draft: review it, then activate.');
      refresh();
      navigate(a.id);
    },
    onError: (e) => toast.error('Could not duplicate the journey', errorMessage(e)),
  });
  const restore = useMutation({
    mutationFn: (a: AutomationListItem) => api.post<Automation>(`${EMAIL_API}/automations/${a.id}/restore`),
    onSuccess: () => {
      toast.success('Journey restored', 'It is paused: activate it to enroll contacts again.');
      refresh();
    },
    onError: (e) => toast.error('Could not restore the journey', errorMessage(e)),
  });
  return (
    <>
      <PageHeader
        title="Journeys"
        description="Automated sequences started by a sign-up, tag, form, date or custom event. Ready-made journeys start as drafts: review, then activate."
        actions={
          <ButtonLink to="new" leadingIcon={<Plus />}>
            New journey
          </ButtonLink>
        }
      />
      <Checkbox label="Show archived journeys" checked={showArchived} onChange={(e) => setShowArchived(e.target.checked)} />
      <Card>
        <CardBody>
          {automations.isError ? (
            <ErrorState error={automations.error} onRetry={() => void automations.refetch()} />
          ) : (
            <DataTable
              caption="Journeys"
              rows={automations.data ?? []}
              getRowId={(a) => a.id}
              rowLabel={(a) => a.name}
              loading={automations.isPending}
              rowActions={(a) => {
                const hasHistory = a.active + a.completed + a.exited > 0;
                return [
                  { id: 'duplicate', label: 'Duplicate', icon: <Copy />, onSelect: () => duplicate.mutate(a) },
                  a.status === 'Archived'
                    ? { id: 'restore', label: 'Restore (as paused)', icon: <ArchiveRestore />, onSelect: () => restore.mutate(a) }
                    : { id: 'archive', label: 'Archive', icon: <Archive />, onSelect: () => setPending({ row: a, action: 'archive' }) },
                  {
                    id: 'delete',
                    label: 'Delete',
                    icon: <Trash2 />,
                    danger: true,
                    disabled: a.status === 'Active' || hasHistory,
                    description:
                      a.status === 'Active' ? 'Pause or archive it first.' : hasHistory ? 'Contacts entered it — archive it to keep its statistics.' : undefined,
                    onSelect: () => setPending({ row: a, action: 'delete' }),
                  },
                ];
              }}
              columns={[
                {
                  id: 'name',
                  header: 'Journey',
                  primary: true,
                  cell: (a) => (
                    <Link className="ui-link" to={a.id}>
                      {a.name}
                    </Link>
                  ),
                },
                { id: 'status', header: 'Status', cell: (a) => <AutomationStatusBadge status={a.status} /> },
                { id: 'trigger', header: 'Starts when', cell: (a) => humanize(a.trigger) },
                { id: 'active', header: 'In progress', align: 'right', cell: (a) => formatNumber(a.active) },
                { id: 'completed', header: 'Completed', align: 'right', hideOnMobile: true, cell: (a) => formatNumber(a.completed) },
                { id: 'exited', header: 'Exited', align: 'right', hideOnMobile: true, cell: (a) => formatNumber(a.exited) },
                { id: 'updated', header: 'Updated', hideOnMobile: true, cell: (a) => <DateTime value={a.updatedAt} format="relative" /> },
              ]}
              emptyState={<EmptyState icon={<Workflow />} headingLevel={2} title="No journeys yet" description="Create a welcome series or an abandoned-cart reminder." />}
            />
          )}
        </CardBody>
      </Card>
      <ConfirmDialog
        open={pending !== null}
        onClose={() => setPending(null)}
        tone="danger"
        title={pending?.action === 'delete' ? 'Delete this journey?' : 'Archive this journey?'}
        description={
          pending?.action === 'delete'
            ? 'The journey and its steps are removed permanently.'
            : 'Contacts in progress stop at their current step. You can restore it later.'
        }
        confirmLabel={pending?.action === 'delete' ? 'Delete journey' : 'Archive journey'}
        onConfirm={async () => {
          if (!pending) return;
          if (pending.action === 'delete') await api.delete(`${EMAIL_API}/automations/${pending.row.id}`);
          else await api.post(`${EMAIL_API}/automations/${pending.row.id}/archive`);
          toast.success(pending.action === 'delete' ? 'Journey deleted' : 'Journey archived');
          refresh();
        }}
      />
    </>
  );
}
