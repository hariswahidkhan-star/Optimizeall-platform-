import { Plus, Workflow } from 'lucide-react';
import { Link } from 'react-router-dom';
import { Badge } from '@/components/ui/Badge';
import { ButtonLink } from '@/components/ui/ButtonLink';
import { Card, CardBody } from '@/components/ui/Card';
import { DataTable } from '@/components/ui/DataTable';
import { DateTime } from '@/components/ui/DateTime';
import { EmptyState } from '@/components/ui/EmptyState';
import { ErrorState } from '@/components/ui/ErrorState';
import { PageHeader } from '@/components/ui/PageHeader';
import type { Tone } from '@/components/ui/tones';
import { formatNumber } from '@/lib/format/money';
import { humanize } from '@/lib/format/text';
import { useAutomations } from '../api/queries';
import type { AutomationStatus } from '../api/types';
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
  const { clientId } = useEmailWorkspace();
  const automations = useAutomations(clientId);
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
      <Card>
        <CardBody>
          {automations.isError ? (
            <ErrorState error={automations.error} onRetry={() => void automations.refetch()} />
          ) : (
            <DataTable
              caption="Journeys"
              rows={automations.data ?? []}
              getRowId={(a) => a.id}
              loading={automations.isPending}
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
    </>
  );
}
