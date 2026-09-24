import { Pencil, Plus, UserPlus } from 'lucide-react';
import { useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import {
  Button,
  Card,
  CardBody,
  CardHeader,
  DataTable,
  EmptyState,
  ErrorState,
  FilterBar,
  KeyValueList,
  Money,
  PageHeader,
  Pagination,
  Skeleton,
  type DataTableColumn,
  type SortState,
} from '@/components/ui';
import { Permissions } from '@/lib/auth/permissions';
import { useAuth } from '@/lib/auth/useAuth';
import { useCompanies, useCompany } from '../api/hooks';
import type { CompanySummary } from '../api/types';
import { ActivityPanel } from '../components/ActivityPanel';
import { ArchiveButton, ArchivedBanner, ArchivedFilter, BulkActionsBar } from '../components/ArchiveControls';
import { CompanyFormDialog, ContactFormDialog } from '../components/CrmForms';
import { LifecycleBadge } from '../lib';
import '@/features/agency/billing/billing.css';
import '../crm.css';

const columns: DataTableColumn<CompanySummary>[] = [
  {
    id: 'name',
    header: 'Company',
    primary: true,
    sortable: true,
    cell: (c) => (
      <Link className="ui-link bill-strong" to={`/agency/crm/companies/${c.id}`}>
        {c.name}
      </Link>
    ),
  },
  { id: 'domain', header: 'Domain', cell: (c) => c.domain ?? '—' },
  { id: 'industry', header: 'Industry', sortable: true, cell: (c) => c.industry ?? '—', hideOnMobile: true },
  { id: 'contacts', header: 'Contacts', align: 'right', cell: (c) => c.contacts },
  { id: 'deals', header: 'Open deals', align: 'right', cell: (c) => c.openDeals },
  { id: 'owner', header: 'Owner', cell: (c) => c.owner?.displayName ?? '—', hideOnMobile: true },
];

export function CompaniesPage() {
  const { hasPermission } = useAuth();
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);
  const [creating, setCreating] = useState(false);
  const [archived, setArchived] = useState(false);
  const [sort, setSort] = useState<SortState>({ id: 'created', desc: true });
  const [selected, setSelected] = useState<string[]>([]);
  const canManage = hasPermission(Permissions.CrmManage);
  const query = useCompanies({ search, archived: archived || undefined, sort: sort.id, desc: sort.desc, page, pageSize: 25 });
  return (
    <>
      <PageHeader
        title="Companies"
        breadcrumbs={[{ label: 'Sales CRM', to: '/agency/crm' }, { label: 'Companies' }]}
        actions={
          hasPermission(Permissions.CrmManage) && (
            <Button leadingIcon={<Plus />} onClick={() => setCreating(true)}>
              New company
            </Button>
          )
        }
      />
      <Card>
        <CardBody className="stack">
          <div className="crm-grid">
            <FilterBar search={search} onSearchChange={(v) => { setSearch(v); setPage(1); }} searchLabel="Search companies" searchPlaceholder="Name or domain" />
            <ArchivedFilter value={archived} onChange={(v) => { setArchived(v); setSelected([]); setPage(1); }} />
          </div>
          {query.isError ? (
            <ErrorState error={query.error} onRetry={() => void query.refetch()} />
          ) : (
            <>
              <DataTable
                caption={archived ? 'Archived companies' : 'Companies'}
                columns={columns}
                rows={query.data?.items ?? []}
                getRowId={(c) => c.id}
                loading={query.isPending}
                sort={sort}
                onSortChange={(next) => { setSort(next); setPage(1); }}
                rowLabel={(c) => c.name}
                selectable={canManage}
                selectedIds={selected}
                onSelectionChange={setSelected}
                bulkActions={(ids) => (
                  <BulkActionsBar entity="companies" selectedIds={ids} archivedView={archived} onDone={() => setSelected([])} />
                )}
                emptyState={
                  <EmptyState
                    compact
                    headingLevel={3}
                    title={archived ? 'No archived companies' : 'No companies'}
                    description={archived ? 'Archived companies appear here and can be restored.' : undefined}
                  />
                }
              />
              {query.data && query.data.total > 25 && <Pagination page={page} pageSize={25} total={query.data.total} onPageChange={setPage} />}
            </>
          )}
        </CardBody>
      </Card>
      <CompanyFormDialog open={creating} onClose={() => setCreating(false)} />
    </>
  );
}

export function CompanyDetailPage() {
  const { companyId = '' } = useParams();
  const { hasPermission } = useAuth();
  const canManage = hasPermission(Permissions.CrmManage);
  const query = useCompany(companyId);
  const [editing, setEditing] = useState(false);
  const [adding, setAdding] = useState(false);
  if (query.isError) return <ErrorState error={query.error} onRetry={() => void query.refetch()} />;
  const c = query.data;
  if (!c) return <Skeleton height="20rem" />;
  let custom: Record<string, unknown> = {};
  try {
    custom = JSON.parse(c.customFields) as Record<string, unknown>;
  } catch {
    custom = {};
  }
  return (
    <>
      <PageHeader
        title={c.name}
        breadcrumbs={[{ label: 'Sales CRM', to: '/agency/crm' }, { label: 'Companies', to: '/agency/crm/companies' }, { label: c.name }]}
        description={[c.domain, c.industry, c.countryCode].filter(Boolean).join(' · ')}
        actions={
          canManage &&
          !c.archivedAt && (
            <div className="crm-actions">
              <Button variant="secondary" leadingIcon={<UserPlus />} onClick={() => setAdding(true)}>
                Add contact
              </Button>
              <Button variant="secondary" leadingIcon={<Pencil />} onClick={() => setEditing(true)}>
                Edit
              </Button>
              <ArchiveButton
                entity="companies"
                id={c.id}
                concurrencyStamp={c.concurrencyStamp}
                archived={false}
                disabledReason={c.clientAccountId ? 'This company is a client. Change the client’s status instead.' : undefined}
              />
            </div>
          )
        }
      />
      {c.archivedAt && <ArchivedBanner entity="companies" id={c.id} concurrencyStamp={c.concurrencyStamp} canManage={canManage} />}
      <div className="crm-two-col">
        <Card>
          <CardHeader title="Timeline" description="Includes activity on the company’s contacts and deals." />
          <CardBody>
            <ActivityPanel companyId={c.id} />
          </CardBody>
        </Card>
        <div className="stack">
          <Card>
            <CardHeader title="Details" headingLevel={3} />
            <CardBody>
              <KeyValueList
                items={[
                  { label: 'Size', value: c.size },
                  { label: 'Owner', value: c.owner?.displayName ?? 'Unassigned' },
                  { label: 'Tags', value: c.tags.join(', ') || '—' },
                  { label: 'Client account', value: c.clientAccountId ? 'Yes — this company is a client' : 'Not a client yet' },
                  ...Object.entries(custom).map(([key, value]) => ({ label: key, value: String(value ?? '—') })),
                ]}
              />
            </CardBody>
          </Card>
          <Card>
            <CardHeader title="Contacts" headingLevel={3} />
            <CardBody>
              {c.contacts.length === 0 ? (
                <p className="crm-muted">No contacts yet.</p>
              ) : (
                <ul className="crm-list">
                  {c.contacts.map((p) => (
                    <li key={p.id} className="crm-row">
                      <Link className="ui-link" to={`/agency/crm/contacts/${p.id}`}>
                        {p.displayName}
                      </Link>
                      <LifecycleBadge stage={p.lifecycleStage} />
                    </li>
                  ))}
                </ul>
              )}
            </CardBody>
          </Card>
          <Card>
            <CardHeader title="Deals" headingLevel={3} />
            <CardBody>
              {c.deals.length === 0 ? (
                <p className="crm-muted">No deals yet.</p>
              ) : (
                <ul className="crm-list">
                  {c.deals.map((d) => (
                    <li key={d.id} className="crm-row">
                      <Link className="ui-link" to={`/agency/crm/deals/${d.id}`}>
                        {d.title}
                      </Link>
                      <Money amount={d.value} currency={d.currency} />
                    </li>
                  ))}
                </ul>
              )}
            </CardBody>
          </Card>
        </div>
      </div>
      <CompanyFormDialog open={editing} onClose={() => setEditing(false)} company={c} />
      <ContactFormDialog open={adding} onClose={() => setAdding(false)} defaultCompanyId={c.id} />
    </>
  );
}
