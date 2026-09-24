import { Download, Pencil, Plus, Upload } from 'lucide-react';
import { useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import {
  Alert,
  Button,
  Card,
  CardBody,
  CardHeader,
  Checkbox,
  DataTable,
  EmptyState,
  ErrorState,
  FormField,
  Input,
  KeyValueList,
  PageHeader,
  Pagination,
  Select,
  Skeleton,
  useToast,
  type DataTableColumn,
  type SortState,
} from '@/components/ui';
import { FormDialog } from '@/features/agency/billing/components/FormDialog';
import { billingErrorMessage } from '@/features/agency/billing/lib';
import { api } from '@/lib/api/client';
import { Permissions } from '@/lib/auth/permissions';
import { useAuth } from '@/lib/auth/useAuth';
import { useContact, useContacts, useImportContacts } from '../api/hooks';
import type { ContactSummary, ImportResult, LifecycleStage } from '../api/types';
import { ActivityPanel } from '../components/ActivityPanel';
import { ArchiveButton, ArchivedBanner, ArchivedFilter, BulkActionsBar } from '../components/ArchiveControls';
import { SavedViewsBar } from '../components/SavedViewsBar';
import { ContactFormDialog } from '../components/CrmForms';
import { CONSENT_OPTIONS, LIFECYCLE_OPTIONS, LifecycleBadge } from '../lib';
import '@/features/agency/billing/billing.css';
import '../crm.css';

const columns: DataTableColumn<ContactSummary>[] = [
  {
    id: 'name',
    header: 'Name',
    primary: true,
    sortable: true,
    cell: (c) => (
      <Link className="ui-link bill-strong" to={`/agency/crm/contacts/${c.id}`}>
        {c.displayName}
      </Link>
    ),
  },
  { id: 'email', header: 'Email', sortable: true, cell: (c) => c.email ?? '—' },
  { id: 'company', header: 'Company', cell: (c) => c.companyName ?? '—', hideOnMobile: true },
  { id: 'lifecycle', header: 'Lifecycle', sortable: true, cell: (c) => <LifecycleBadge stage={c.lifecycleStage} /> },
  { id: 'score', header: 'Score', align: 'right', sortable: true, cell: (c) => c.score },
  { id: 'owner', header: 'Owner', cell: (c) => c.owner?.displayName ?? '—', hideOnMobile: true },
];

function ImportDialog({ open, onClose }: { open: boolean; onClose: () => void }) {
  const importer = useImportContacts();
  const [file, setFile] = useState<File | null>(null);
  const [dryRun, setDryRun] = useState(true);
  const [updateExisting, setUpdateExisting] = useState(false);
  const [result, setResult] = useState<ImportResult | null>(null);
  return (
    <FormDialog
      open={open}
      onClose={() => {
        setResult(null);
        setFile(null);
        onClose();
      }}
      title="Import contacts from CSV"
      description="Columns: first_name, last_name, email, phone, job_title, company, company_domain, lifecycle_stage, consent, source, tags (separate tags with ;). Rows are deduplicated by email."
      submitLabel={dryRun ? 'Check file' : 'Import'}
      canSubmit={!!file}
      size="lg"
      onSubmit={async () => {
        if (!file) return false;
        setResult(await importer.mutateAsync({ file, dryRun, updateExisting }));
        return false;
      }}
    >
      <FormField label="CSV file" required hint="Up to 5,000 rows / 2 MB.">
        <Input type="file" accept=".csv,text/csv" onChange={(e) => setFile(e.target.files?.[0] ?? null)} />
      </FormField>
      <Checkbox label="Check only (don’t save)" checked={dryRun} onChange={(e) => setDryRun(e.target.checked)} />
      <Checkbox label="Fill blank fields of existing contacts" checked={updateExisting} onChange={(e) => setUpdateExisting(e.target.checked)} />
      {result && (
        <Alert tone={result.failed > 0 ? 'warning' : 'success'} title={result.dryRun ? 'Check complete — nothing saved' : 'Import complete'}>
          {result.created} to create, {result.updated} updated, {result.skipped} skipped, {result.failed} with errors (of {result.totalRows} rows).
          {result.rows.filter((r) => r.status === 'error' || r.status === 'skipped').length > 0 && (
            <ul>
              {result.rows
                .filter((r) => r.status === 'error' || r.status === 'skipped')
                .slice(0, 50)
                .map((r) => (
                  <li key={r.row}>
                    Row {r.row} ({r.status}): {r.errors.join(' ')}
                  </li>
                ))}
            </ul>
          )}
        </Alert>
      )}
    </FormDialog>
  );
}

export function ContactsPage() {
  const { hasPermission } = useAuth();
  const canManage = hasPermission(Permissions.CrmManage);
  const toast = useToast();
  const [search, setSearch] = useState('');
  const [lifecycleStage, setStage] = useState<string>('');
  const [consentStatus, setConsent] = useState<string>('');
  const [tag, setTag] = useState('');
  const [page, setPage] = useState(1);
  const [archived, setArchived] = useState(false);
  const [sort, setSort] = useState<SortState>({ id: 'created', desc: true });
  const [selected, setSelected] = useState<string[]>([]);
  const [creating, setCreating] = useState(false);
  const [importing, setImporting] = useState(false);
  const filters = { search: search || undefined, lifecycleStage: lifecycleStage || undefined, consentStatus: consentStatus || undefined, tag: tag || undefined };
  const query = useContacts({ ...filters, archived: archived || undefined, sort: sort.id, desc: sort.desc, page, pageSize: 25 });

  const applyView = (f: Record<string, string>) => {
    setSearch(f.search ?? '');
    setStage(f.lifecycleStage ?? '');
    setConsent(f.consentStatus ?? '');
    setTag(f.tag ?? '');
    setPage(1);
  };

  return (
    <>
      <PageHeader
        title="Contacts"
        breadcrumbs={[{ label: 'Sales CRM', to: '/agency/crm' }, { label: 'Contacts' }]}
        actions={
          <div className="crm-actions">
            <Button
              variant="secondary"
              leadingIcon={<Download />}
              onClick={async () => {
                try {
                  await api.download('/agency/crm/contacts/export.csv', 'contacts.csv', { query: filters });
                } catch (error) {
                  toast.error('Export failed', billingErrorMessage(error));
                }
              }}
            >
              Export CSV
            </Button>
            {canManage && (
              <>
                <Button variant="secondary" leadingIcon={<Upload />} onClick={() => setImporting(true)}>
                  Import CSV
                </Button>
                <Button leadingIcon={<Plus />} onClick={() => setCreating(true)}>
                  New contact
                </Button>
              </>
            )}
          </div>
        }
      />
      <Card>
        <CardBody className="stack">
          <div className="crm-grid">
            <FormField label="Search">
              <Input type="search" value={search} onChange={(e) => { setSearch(e.target.value); setPage(1); }} placeholder="Name, email or phone" />
            </FormField>
            <FormField label="Lifecycle stage">
              <Select value={lifecycleStage} options={[{ value: '', label: 'All stages' }, ...LIFECYCLE_OPTIONS]} onChange={(e) => { setStage(e.target.value as LifecycleStage | ''); setPage(1); }} />
            </FormField>
            <FormField label="Consent">
              <Select value={consentStatus} options={[{ value: '', label: 'Any consent' }, ...CONSENT_OPTIONS]} onChange={(e) => { setConsent(e.target.value); setPage(1); }} />
            </FormField>
            <FormField label="Tag">
              <Input value={tag} onChange={(e) => { setTag(e.target.value); setPage(1); }} />
            </FormField>
            <ArchivedFilter value={archived} onChange={(v) => { setArchived(v); setSelected([]); setPage(1); }} />
          </div>
          <SavedViewsBar entity="contacts" filters={filters} onApply={applyView} />
          {query.isError ? (
            <ErrorState error={query.error} onRetry={() => void query.refetch()} />
          ) : (
            <>
              <DataTable
                caption={archived ? 'Archived contacts' : 'Contacts'}
                columns={columns}
                rows={query.data?.items ?? []}
                getRowId={(c) => c.id}
                loading={query.isPending}
                sort={sort}
                onSortChange={(next) => { setSort(next); setPage(1); }}
                rowLabel={(c) => c.displayName}
                selectable={canManage}
                selectedIds={selected}
                onSelectionChange={setSelected}
                bulkActions={(ids) => (
                  <BulkActionsBar entity="contacts" selectedIds={ids} archivedView={archived} onDone={() => setSelected([])} />
                )}
                emptyState={
                  <EmptyState
                    compact
                    headingLevel={3}
                    title={archived ? 'No archived contacts' : 'No contacts match'}
                    description={archived ? 'Archived contacts appear here and can be restored.' : 'Change the filters, or add a contact.'}
                  />
                }
              />
              {query.data && query.data.total > 25 && <Pagination page={page} pageSize={25} total={query.data.total} onPageChange={setPage} />}
            </>
          )}
        </CardBody>
      </Card>
      <ContactFormDialog open={creating} onClose={() => setCreating(false)} />
      <ImportDialog open={importing} onClose={() => setImporting(false)} />
    </>
  );
}

export function ContactDetailPage() {
  const { contactId = '' } = useParams();
  const { hasPermission } = useAuth();
  const query = useContact(contactId);
  const [editing, setEditing] = useState(false);
  if (query.isError) return <ErrorState error={query.error} onRetry={() => void query.refetch()} />;
  const c = query.data;
  if (!c) return <Skeleton height="20rem" />;
  return (
    <>
      <PageHeader
        title={c.displayName}
        breadcrumbs={[{ label: 'Sales CRM', to: '/agency/crm' }, { label: 'Contacts', to: '/agency/crm/contacts' }, { label: c.displayName }]}
        meta={<LifecycleBadge stage={c.lifecycleStage} />}
        description={[c.jobTitle, c.companyName].filter(Boolean).join(' at ')}
        actions={
          hasPermission(Permissions.CrmManage) &&
          !c.archivedAt && (
            <div className="crm-actions">
              <Button variant="secondary" leadingIcon={<Pencil />} onClick={() => setEditing(true)}>
                Edit
              </Button>
              <ArchiveButton entity="contacts" id={c.id} concurrencyStamp={c.concurrencyStamp} archived={false} />
            </div>
          )
        }
      />
      {c.archivedAt && (
        <ArchivedBanner entity="contacts" id={c.id} concurrencyStamp={c.concurrencyStamp} canManage={hasPermission(Permissions.CrmManage)} />
      )}
      <div className="crm-two-col">
        <Card>
          <CardHeader title="Timeline" />
          <CardBody>
            <ActivityPanel contactId={c.id} />
          </CardBody>
        </Card>
        <div className="stack">
          <Card>
            <CardHeader title="Details" headingLevel={3} />
            <CardBody>
              <KeyValueList
                items={[
                  { label: 'Email', value: c.email ?? '—' },
                  { label: 'Phone', value: c.phone ?? '—' },
                  {
                    label: 'Company',
                    value: c.companyId ? (
                      <Link className="ui-link" to={`/agency/crm/companies/${c.companyId}`}>
                        {c.companyName}
                      </Link>
                    ) : (
                      '—'
                    ),
                  },
                  { label: 'Owner', value: c.owner?.displayName ?? 'Unassigned' },
                  { label: 'Consent', value: CONSENT_OPTIONS.find((o) => o.value === c.consentStatus)?.label ?? c.consentStatus },
                  { label: 'Source', value: c.source ?? '—' },
                  { label: 'Tags', value: c.tags.join(', ') || '—' },
                  { label: 'First touch', value: [c.firstTouch.source, c.firstTouch.medium, c.firstTouch.campaign].filter(Boolean).join(' / ') || '—' },
                ]}
              />
            </CardBody>
          </Card>
          <Card>
            <CardHeader title={`Lead score: ${c.score}`} headingLevel={3} />
            <CardBody>
              {c.scoreBreakdown.length === 0 ? (
                <p className="crm-muted">No scoring rule matches yet.</p>
              ) : (
                <ul className="crm-list">
                  {c.scoreBreakdown.map((l) => (
                    <li key={l.rule} className="crm-row">
                      <span>{l.rule}</span>
                      <span className="bill-strong">{l.points > 0 ? `+${l.points}` : l.points}</span>
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
                <p className="crm-muted">No deals.</p>
              ) : (
                <ul className="crm-list">
                  {c.deals.map((d) => (
                    <li key={d.id}>
                      <Link className="ui-link" to={`/agency/crm/deals/${d.id}`}>
                        {d.title}
                      </Link>{' '}
                      <span className="crm-muted">{d.stageName}</span>
                    </li>
                  ))}
                </ul>
              )}
            </CardBody>
          </Card>
        </div>
      </div>
      <ContactFormDialog open={editing} onClose={() => setEditing(false)} contact={c} />
    </>
  );
}
