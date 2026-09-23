import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Building2, Plus } from 'lucide-react';
import { useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import {
  Alert,
  Button,
  Card,
  CardBody,
  DataTable,
  Dialog,
  EmptyState,
  ErrorState,
  FilterBar,
  FormField,
  Input,
  PageHeader,
  Pagination,
  Select,
  type DataTableColumn,
} from '@/components/ui';
import { countryOptions, timeZoneOptions } from '@/features/auth/localeOptions';
import { api } from '@/lib/api/client';
import { errorMessage, isApiError } from '@/lib/api/errors';
import { useSupportedCurrencies } from '@/lib/api/meta';
import type { PagedResult } from '@/lib/api/types';
import { Permissions } from '@/lib/auth/permissions';
import { useAuth } from '@/lib/auth/useAuth';
import { CLIENT_STATUSES, type ClientDetail, type ClientSummary } from '../shared/deliveryTypes';
import { ClientStatusBadge } from '../shared/deliveryUi';
import { dk, useStaff } from './api';

const columns: DataTableColumn<ClientSummary>[] = [
  {
    id: 'name',
    header: 'Client',
    primary: true,
    cell: (c) => (
      <Link className="ui-link" to={`/agency/clients/${c.id}`}>
        {c.name}
      </Link>
    ),
  },
  { id: 'status', header: 'Status', cell: (c) => <ClientStatusBadge status={c.status} /> },
  { id: 'industry', header: 'Industry', cell: (c) => c.industry ?? '—', hideOnMobile: true },
  { id: 'am', header: 'Account manager', cell: (c) => c.accountManager?.displayName ?? '—' },
  { id: 'projects', header: 'Active projects', align: 'right', cell: (c) => c.activeProjects },
  { id: 'currency', header: 'Currency', cell: (c) => c.currency, hideOnMobile: true },
];

export function NewClientDialog({ open, onClose }: { open: boolean; onClose: () => void }) {
  const qc = useQueryClient();
  const navigate = useNavigate();
  const staff = useStaff();
  const currencies = useSupportedCurrencies('USD');
  const [form, setForm] = useState({
    name: '',
    industry: '',
    website: '',
    countryCode: 'US',
    timeZone: 'UTC',
    currency: 'USD',
    accountManagerUserId: '',
  });
  const set = (key: keyof typeof form) => (value: string) => setForm((f) => ({ ...f, [key]: value }));
  const create = useMutation({
    mutationFn: () =>
      api.post<ClientDetail>('/agency/clients', {
        ...form,
        industry: form.industry || null,
        website: form.website || null,
        accountManagerUserId: form.accountManagerUserId || null,
      }),
    onSuccess: (client) => {
      void qc.invalidateQueries({ queryKey: ['delivery', 'clients'] });
      onClose();
      navigate(`/agency/clients/${client.id}`);
    },
  });
  const fieldError = (key: string) => (isApiError(create.error) ? create.error.errors?.[key] : undefined);
  return (
    <Dialog
      open={open}
      onClose={onClose}
      title="New client"
      description="Creates the account with its onboarding checklist and an empty brand kit."
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button form="new-client-form" type="submit" loading={create.isPending}>
            Create client
          </Button>
        </>
      }
    >
      <form
        id="new-client-form"
        className="dl-form"
        onSubmit={(e) => {
          e.preventDefault();
          create.mutate();
        }}
      >
        {create.error && !fieldError('name') ? <Alert tone="danger">{errorMessage(create.error)}</Alert> : null}
        <FormField label="Name" required error={fieldError('name')}>
          <Input value={form.name} onChange={(e) => set('name')(e.target.value)} required minLength={2} maxLength={200} />
        </FormField>
        <div className="dl-form__row">
          <FormField label="Industry" optional>
            <Input value={form.industry} onChange={(e) => set('industry')(e.target.value)} maxLength={100} />
          </FormField>
          <FormField label="Website" optional error={fieldError('website')}>
            <Input type="url" value={form.website} onChange={(e) => set('website')(e.target.value)} placeholder="https://" />
          </FormField>
        </div>
        <div className="dl-form__row">
          <FormField label="Country">
            <Select value={form.countryCode} onChange={(e) => set('countryCode')(e.target.value)} options={countryOptions()} />
          </FormField>
          <FormField label="Time zone">
            <Select value={form.timeZone} onChange={(e) => set('timeZone')(e.target.value)} options={timeZoneOptions(form.timeZone)} />
          </FormField>
          <FormField label="Currency" hint="Used for proposals, invoices and budgets.">
            <Select value={form.currency} onChange={(e) => set('currency')(e.target.value)} options={currencies.options} />
          </FormField>
        </div>
        <FormField label="Account manager" optional>
          <Select
            value={form.accountManagerUserId}
            onChange={(e) => set('accountManagerUserId')(e.target.value)}
            placeholder="Unassigned"
            options={(staff.data ?? []).map((s) => ({ value: s.id, label: s.displayName }))}
          />
        </FormField>
      </form>
    </Dialog>
  );
}

export function ClientsPage() {
  const { hasPermission } = useAuth();
  const [search, setSearch] = useState('');
  const [filters, setFilters] = useState<Record<string, string | undefined>>({});
  const [page, setPage] = useState(1);
  const [creating, setCreating] = useState(false);
  const query = { search, status: filters.status, accountManager: filters.am, page, pageSize: 25, sort: 'name' };
  const clients = useQuery({
    queryKey: dk.clients(query),
    queryFn: ({ signal }) => api.get<PagedResult<ClientSummary>>('/agency/clients', { query, signal }),
    placeholderData: keepPreviousData,
  });

  return (
    <div className="dl-page">
      <PageHeader
        title="Clients"
        description="Client accounts, their teams, onboarding and health."
        actions={
          hasPermission(Permissions.ClientsManage) ? (
            <Button leadingIcon={<Plus aria-hidden="true" />} onClick={() => setCreating(true)}>
              New client
            </Button>
          ) : null
        }
      />
      <Card>
        <CardBody className="dl-page">
          <FilterBar
            search={search}
            onSearchChange={(s) => {
              setSearch(s);
              setPage(1);
            }}
            searchLabel="Search clients"
            searchPlaceholder="Name, slug or industry…"
            filters={[
              { id: 'status', label: 'Status', options: CLIENT_STATUSES.map((s) => ({ value: s, label: s })) },
              { id: 'am', label: 'Account manager', allLabel: 'Anyone', options: [{ value: 'me', label: 'Me' }] },
            ]}
            values={filters}
            onFilterChange={(id, value) => {
              setFilters((f) => ({ ...f, [id]: value || undefined }));
              setPage(1);
            }}
            onReset={() => {
              setFilters({});
              setSearch('');
            }}
          />
          {clients.isError ? (
            <ErrorState error={clients.error} onRetry={() => void clients.refetch()} />
          ) : (
            <DataTable
              caption="Clients"
              columns={columns}
              rows={clients.data?.items ?? []}
              getRowId={(c) => c.id}
              loading={clients.isPending}
              emptyState={<EmptyState icon={<Building2 aria-hidden="true" />} title="No clients found" />}
            />
          )}
          {clients.data && clients.data.total > clients.data.pageSize ? (
            <Pagination page={page} pageSize={clients.data.pageSize} total={clients.data.total} onPageChange={setPage} />
          ) : null}
        </CardBody>
      </Card>
      {creating ? <NewClientDialog open onClose={() => setCreating(false)} /> : null}
    </div>
  );
}
