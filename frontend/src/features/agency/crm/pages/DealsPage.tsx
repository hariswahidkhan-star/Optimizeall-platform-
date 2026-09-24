import { Plus } from 'lucide-react';
import { useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import {
  Button,
  Card,
  CardBody,
  DataTable,
  EmptyState,
  ErrorState,
  FilterBar,
  Money,
  PageHeader,
  Pagination,
  Skeleton,
  Tabs,
  useToast,
  type DataTableColumn,
  type SortState,
} from '@/components/ui';
import { billingErrorMessage } from '@/features/agency/billing/lib';
import { Permissions } from '@/lib/auth/permissions';
import { useAuth } from '@/lib/auth/useAuth';
import { useAssignees, useBoard, useDeals, useMoveDeal } from '../api/hooks';
import type { DealSummary, Stage } from '../api/types';
import { ArchivedFilter, BulkActionsBar } from '../components/ArchiveControls';
import { DealFormDialog, LostReasonDialog } from '../components/CrmForms';
import { KanbanBoard } from '../components/KanbanBoard';
import { DealStatusBadge, SOURCE_LABELS, SOURCE_OPTIONS } from '../lib';
import '@/features/agency/billing/billing.css';
import '../crm.css';

const listColumns: DataTableColumn<DealSummary>[] = [
  {
    id: 'title',
    header: 'Deal',
    primary: true,
    sortable: true,
    cell: (d) => (
      <Link className="ui-link bill-strong" to={`/agency/crm/deals/${d.id}`}>
        {d.title}
      </Link>
    ),
  },
  { id: 'company', header: 'Company', cell: (d) => d.companyName ?? '—' },
  { id: 'stage', header: 'Stage', cell: (d) => d.stageName },
  { id: 'status', header: 'Status', cell: (d) => <DealStatusBadge status={d.status} /> },
  { id: 'source', header: 'Source', cell: (d) => SOURCE_LABELS[d.source], hideOnMobile: true },
  { id: 'owner', header: 'Owner', cell: (d) => d.owner?.displayName ?? 'Unassigned', hideOnMobile: true },
  { id: 'value', header: 'Value', align: 'right', sortable: true, cell: (d) => <Money amount={d.value} currency={d.currency} /> },
  {
    id: 'expectedClose',
    header: 'Expected close',
    sortable: true,
    hideOnMobile: true,
    cell: (d) => d.expectedCloseDate ?? '—',
  },
  { id: 'weighted', header: 'Weighted', align: 'right', cell: (d) => <Money amount={d.weightedValue} currency={d.currency} />, hideOnMobile: true },
];

function DealList({ search, owner, source, canManage }: { search: string; owner?: string; source?: string; canManage: boolean }) {
  const [page, setPage] = useState(1);
  const [archived, setArchived] = useState(false);
  const [sort, setSort] = useState<SortState>({ id: 'created', desc: true });
  const [selected, setSelected] = useState<string[]>([]);
  const query = useDeals({ search, ownerUserId: owner, source, archived: archived || undefined, sort: sort.id, desc: sort.desc, page, pageSize: 25 });
  if (query.isError) return <ErrorState error={query.error} onRetry={() => void query.refetch()} />;
  return (
    <div className="stack">
      <div className="crm-grid">
        <ArchivedFilter value={archived} onChange={(v) => { setArchived(v); setSelected([]); setPage(1); }} />
      </div>
      <DataTable
        caption={archived ? 'Archived deals' : 'Deals'}
        columns={listColumns}
        rows={query.data?.items ?? []}
        getRowId={(d) => d.id}
        loading={query.isPending}
        sort={sort}
        onSortChange={(next) => { setSort(next); setPage(1); }}
        rowLabel={(d) => d.title}
        selectable={canManage}
        selectedIds={selected}
        onSelectionChange={setSelected}
        bulkActions={(ids) => <BulkActionsBar entity="deals" selectedIds={ids} archivedView={archived} onDone={() => setSelected([])} />}
        emptyState={
          <EmptyState
            compact
            headingLevel={3}
            title={archived ? 'No archived deals' : 'No deals match'}
            description={archived ? 'Archived deals appear here and can be restored.' : undefined}
          />
        }
      />
      {query.data && query.data.total > 25 && <Pagination page={page} pageSize={25} total={query.data.total} onPageChange={setPage} />}
    </div>
  );
}

/** Pipeline: kanban board (drag and drop or keyboard) and a sortable list. */
export function DealsPage() {
  const { hasPermission } = useAuth();
  const canManage = hasPermission(Permissions.CrmManage);
  const navigate = useNavigate();
  const toast = useToast();
  const [search, setSearch] = useState('');
  const [owner, setOwner] = useState<string | undefined>();
  const [source, setSource] = useState<string | undefined>();
  const [creating, setCreating] = useState(false);
  const [losing, setLosing] = useState<{ deal: DealSummary; stage: Stage } | null>(null);
  const board = useBoard({ search, ownerUserId: owner });
  const assignees = useAssignees();
  const move = useMoveDeal();

  const doMove = async (deal: DealSummary, stage: Stage, lostReason?: string) => {
    try {
      await move.mutateAsync({ dealId: deal.id, stageId: stage.id, lostReason, concurrencyStamp: deal.concurrencyStamp });
      toast.success(`${deal.title} moved to ${stage.name}`);
    } catch (error) {
      toast.error('Couldn’t move the deal', billingErrorMessage(error));
      throw error;
    }
  };

  return (
    <>
      <PageHeader
        title="Pipeline"
        breadcrumbs={[{ label: 'Sales CRM', to: '/agency/crm' }, { label: 'Pipeline' }]}
        description="Drag a deal to another stage, or focus its move handle and use Enter and the arrow keys."
        actions={
          canManage && (
            <Button leadingIcon={<Plus />} onClick={() => setCreating(true)}>
              New deal
            </Button>
          )
        }
      />
      <Card>
        <CardBody className="stack">
          <FilterBar
            search={search}
            onSearchChange={setSearch}
            searchLabel="Search deals"
            searchPlaceholder="Deal or company"
            filters={[
              { id: 'owner', label: 'Owner', options: (assignees.data ?? []).map((u) => ({ value: u.id, label: u.displayName })) },
              { id: 'source', label: 'Source', options: SOURCE_OPTIONS },
            ]}
            values={{ owner, source }}
            onFilterChange={(id, v) => (id === 'owner' ? setOwner(v) : setSource(v))}
            onReset={() => {
              setSearch('');
              setOwner(undefined);
              setSource(undefined);
            }}
          />
          <Tabs
            label="Pipeline views"
            tabs={[
              {
                id: 'board',
                label: 'Board',
                content: board.isError ? (
                  <ErrorState error={board.error} onRetry={() => void board.refetch()} />
                ) : !board.data ? (
                  <Skeleton height="20rem" />
                ) : (
                  <KanbanBoard
                    columns={board.data.columns}
                    canMove={canManage}
                    onMove={(deal, stage) => {
                      if (stage.kind === 'Lost') setLosing({ deal, stage });
                      else void doMove(deal, stage).catch(() => undefined);
                    }}
                  />
                ),
              },
              { id: 'list', label: 'List', content: <DealList search={search} owner={owner} source={source} canManage={canManage} /> },
            ]}
          />
        </CardBody>
      </Card>
      <DealFormDialog open={creating} onClose={() => setCreating(false)} onSaved={(deal) => navigate(`/agency/crm/deals/${deal.id}`)} />
      <LostReasonDialog
        open={losing !== null}
        dealTitle={losing?.deal.title ?? ''}
        onClose={() => setLosing(null)}
        onConfirm={async (reason) => {
          if (losing) await doMove(losing.deal, losing.stage, reason);
        }}
      />
    </>
  );
}
