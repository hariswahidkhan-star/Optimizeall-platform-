import { useMutation, useQueryClient } from '@tanstack/react-query';
import { CreditCard, Plus } from 'lucide-react';
import { useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import {
  Alert,
  Badge,
  Button,
  Checkbox,
  DataTable,
  DateTime,
  Dialog,
  EmptyState,
  ErrorState,
  FilterBar,
  FormField,
  Input,
  Money,
  PageHeader,
  Pagination,
  Textarea,
  type DataTableColumn,
} from '@/components/ui';
import { useToast } from '@/components/ui/toastContext';
import { api } from '@/lib/api/client';
import { ApiError, errorMessage } from '@/lib/api/errors';
import { Permissions } from '@/lib/auth/permissions';
import { useAuth } from '@/lib/auth/useAuth';
import { rk, useRateCards } from '../api/queries';
import type { RateCard, RateCardListItem } from '../api/types';
import {
  emptyRates,
  RateLinesEditor,
  ratesHints,
  ratesToInput,
  type RatesForm,
} from '../components/RateLinesEditor';
import { CardStatusBadge } from '../components/badges';
import '../rates.css';

export function RateCardsPage() {
  const { hasPermission } = useAuth();
  const canManage = hasPermission(Permissions.RatesManage);
  const [search, setSearch] = useState('');
  const [filters, setFilters] = useState<Record<string, string | undefined>>({});
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(25);
  const [creating, setCreating] = useState(false);
  const query = useRateCards({ search, status: filters.status, currency: filters.currency, page, pageSize });

  const columns: DataTableColumn<RateCardListItem>[] = [
    {
      id: 'name',
      header: 'Rate card',
      primary: true,
      cell: (row) => (
        <span className="stack rt-stack-xs">
          <Link className="ui-link rt-strong" to={`/manage/rate-cards/${row.id}`}>
            {row.name}
          </Link>
          {row.description && <span className="text-small text-muted">{row.description}</span>}
        </span>
      ),
    },
    {
      id: 'status',
      header: 'Status',
      cell: (row) => (
        <span className="cluster rt-cluster-sm">
          <CardStatusBadge status={row.status} />
          {row.pendingApproval && (
            <Badge size="sm" tone="warning">
              Awaiting approval
            </Badge>
          )}
        </span>
      ),
    },
    {
      id: 'rates',
      header: 'Rates',
      cell: (row) =>
        row.minAmount === null ? (
          <span className="text-muted">No rates</span>
        ) : (
          <span className="stack rt-stack-xs">
            <span>
              <Money amount={row.minAmount} currency={row.currency} />
              {row.maxAmount !== null && row.maxAmount !== row.minAmount && (
                <>
                  {' – '}
                  <Money amount={row.maxAmount} currency={row.currency} />
                </>
              )}
            </span>
            <span className="text-small text-muted">
              {row.lineCount} rate{row.lineCount === 1 ? '' : 's'} · v{row.currentVersion}
            </span>
          </span>
        ),
    },
    {
      id: 'assigned',
      header: 'Active assignments',
      align: 'right',
      cell: (row) => row.activeAssignments,
    },
    {
      id: 'updated',
      header: 'Updated',
      hideOnMobile: true,
      cell: (row) => <DateTime value={row.updatedAt} format="relative" />,
    },
  ];

  const data = query.data;
  const filtered = !!(search || filters.status || filters.currency);
  return (
    <>
      <PageHeader
        title="Rate cards"
        description="Reusable per-post rates. Assign a card to a person, a rate group or a segment; every change creates a new version and posts already submitted keep their price."
        actions={
          canManage ? (
            <Button leadingIcon={<Plus />} onClick={() => setCreating(true)}>
              New rate card
            </Button>
          ) : undefined
        }
      />
      <div className="stack">
        <FilterBar
          search={search}
          onSearchChange={(s) => {
            setSearch(s);
            setPage(1);
          }}
          searchLabel="Search rate cards"
          searchPlaceholder="Search by name…"
          filters={[
            {
              id: 'status',
              label: 'Status',
              options: [
                { value: 'Draft', label: 'Draft' },
                { value: 'Active', label: 'Active' },
                { value: 'Archived', label: 'Archived' },
              ],
            },
            {
              id: 'currency',
              label: 'Currency',
              options: ['USD', 'EUR', 'GBP', 'AED', 'SAR', 'PKR', 'INR', 'JPY', 'KWD'].map((c) => ({
                value: c,
                label: c,
              })),
            },
          ]}
          values={filters}
          onFilterChange={(id, value) => {
            setFilters((f) => ({ ...f, [id]: value }));
            setPage(1);
          }}
          onReset={() => {
            setSearch('');
            setFilters({});
            setPage(1);
          }}
        />
        {query.isError ? (
          <ErrorState error={query.error} onRetry={() => void query.refetch()} retrying={query.isFetching} />
        ) : (
          <>
            <DataTable
              caption="Rate cards"
              columns={columns}
              rows={data?.items ?? []}
              getRowId={(r) => r.id}
              rowLabel={(r) => r.name}
              loading={query.isLoading}
              emptyState={
                <EmptyState
                  icon={<CreditCard />}
                  headingLevel={2}
                  title={filtered ? 'No rate cards match' : 'No rate cards yet'}
                  description={
                    filtered
                      ? 'Try another search or clear the filters.'
                      : 'Create a card (e.g. “Micro creators”) and assign it to a group of people.'
                  }
                  action={
                    canManage && !filtered ? (
                      <Button leadingIcon={<Plus />} onClick={() => setCreating(true)}>
                        New rate card
                      </Button>
                    ) : undefined
                  }
                />
              }
            />
            {data && data.total > 0 && (
              <Pagination
                page={page}
                pageSize={pageSize}
                total={data.total}
                onPageChange={setPage}
                onPageSizeChange={(size) => {
                  setPageSize(size);
                  setPage(1);
                }}
              />
            )}
          </>
        )}
      </div>
      {creating && <CreateRateCardDialog onClose={() => setCreating(false)} />}
    </>
  );
}

function CreateRateCardDialog({ onClose }: { onClose: () => void }) {
  const toast = useToast();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [name, setName] = useState('');
  const [description, setDescription] = useState('');
  const [reason, setReason] = useState('');
  const [activate, setActivate] = useState(true);
  const [rates, setRates] = useState<RatesForm>(emptyRates());
  const hints = ratesHints(rates);

  const create = useMutation({
    mutationFn: () =>
      api.post<RateCard>('/admin/rate-cards', {
        name: name.trim(),
        description: description.trim() || null,
        reason: reason.trim(),
        activate,
        ...ratesToInput(rates),
      }),
    onSuccess: async (card) => {
      toast.success('Rate card created', card.name);
      await queryClient.invalidateQueries({ queryKey: rk.all });
      onClose();
      navigate(`/manage/rate-cards/${card.id}`);
    },
  });
  const error = create.error instanceof ApiError ? create.error : null;
  const valid = name.trim().length >= 2 && reason.trim().length >= 5 && hints.length === 0;

  return (
    <Dialog
      open
      onClose={onClose}
      size="lg"
      title="New rate card"
      description="A reusable set of per-post rates in one currency."
      dismissible={!create.isPending}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={create.isPending}>
            Cancel
          </Button>
          <Button onClick={() => create.mutate()} loading={create.isPending} disabled={!valid}>
            Create rate card
          </Button>
        </>
      }
    >
      <div className="stack">
        {create.isError && (
          <Alert tone="danger" title="The rate card was not created">
            {errorMessage(create.error)}
          </Alert>
        )}
        <div className="rt-grid rt-grid--2">
          <FormField label="Name" required error={error?.fieldError('name')}>
            <Input
              value={name}
              maxLength={120}
              onChange={(e) => setName(e.target.value)}
              placeholder="Micro creators 2026"
            />
          </FormField>
          <FormField label="Description" optional>
            <Input value={description} maxLength={500} onChange={(e) => setDescription(e.target.value)} />
          </FormField>
        </div>
        <RateLinesEditor value={rates} onChange={setRates} idPrefix="new-card" />
        {hints.length > 0 && (
          <Alert tone="warning" title="Check the rates">
            <ul className="rt-list">
              {hints.map((h) => (
                <li key={h}>{h}</li>
              ))}
            </ul>
          </Alert>
        )}
        <Checkbox
          label="Activate now"
          description="Active cards can be assigned. Leave unticked to keep it as a draft."
          checked={activate}
          onChange={(e) => setActivate(e.target.checked)}
        />
        <FormField
          label="Reason"
          required
          hint="At least 5 characters. Kept in the audit log."
          error={error?.fieldError('reason')}
        >
          <Textarea value={reason} rows={2} maxLength={500} onChange={(e) => setReason(e.target.value)} />
        </FormField>
      </div>
    </Dialog>
  );
}
