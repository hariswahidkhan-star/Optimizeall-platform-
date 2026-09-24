import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { CheckCircle2, Link2, Pencil, Plus, ShieldCheck, Trash2, XCircle } from 'lucide-react';
import { useState, type FormEvent } from 'react';
import {
  Alert,
  Badge,
  Button,
  Card,
  CardBody,
  CardHeader,
  ConfirmDialog,
  DataTable,
  DateTime,
  Dialog,
  EmptyState,
  FormField,
  Input,
  Select,
  Stat,
  Textarea,
  useToast,
  type DataTableColumn,
} from '@/components/ui';
import { SafeExternalLink } from '@/components/SafeExternalLink';
import { api } from '@/lib/api/client';
import { errorMessage } from '@/lib/api/errors';
import { seoKeys, type Backlink, type BacklinkList, type BacklinkStatus, type Outreach, type OutreachStatus, type Site } from './api';
import { fieldErrors, firstError } from './common';
import { CsvImportButton } from './KeywordsPanel';

const statusTone: Record<BacklinkStatus, 'success' | 'info' | 'danger' | 'warning' | 'neutral'> = {
  Live: 'success',
  Nofollow: 'info',
  Lost: 'danger',
  Error: 'warning',
  Unchecked: 'neutral',
};
const outreachStatuses: OutreachStatus[] = ['Identified', 'Contacted', 'FollowedUp', 'Replied', 'Won', 'Lost'];
const outreachLabel: Record<OutreachStatus, string> = {
  Identified: 'Identified',
  Contacted: 'Contacted',
  FollowedUp: 'Followed up',
  Replied: 'Replied',
  Won: 'Link won',
  Lost: 'Lost',
};

export function BacklinksPanel({ site }: { site: Site }) {
  const queryClient = useQueryClient();
  const toast = useToast();
  const [status, setStatus] = useState<string>('');
  const [adding, setAdding] = useState<Backlink | 'new' | null>(null);
  const [removing, setRemoving] = useState<Backlink | null>(null);
  const [removingProspect, setRemovingProspect] = useState<Outreach | null>(null);
  const [prospect, setProspect] = useState<Outreach | 'new' | null>(null);
  const params = { status: status || undefined, pageSize: 100 };
  const list = useQuery({
    queryKey: seoKeys.backlinks(site.id, params),
    queryFn: () => api.get<BacklinkList>(`/agency/seo/sites/${site.id}/backlinks`, { query: params }),
  });
  const outreach = useQuery({ queryKey: seoKeys.outreach(site.id), queryFn: () => api.get<Outreach[]>(`/agency/seo/sites/${site.id}/outreach`) });
  const refresh = () => void queryClient.invalidateQueries({ queryKey: ['agency', 'seo', 'site', site.id] });
  const check = useMutation({
    mutationFn: () => api.post<{ checked: number }>(`/agency/seo/sites/${site.id}/backlinks/check`),
    onSuccess: (r) => {
      toast.success(`${r.checked} backlink(s) checked`);
      refresh();
    },
    onError: (err) => toast.error('Check failed', errorMessage(err)),
  });
  const setOutcome = useMutation({
    mutationFn: ({ o, status }: { o: Outreach; status: OutreachStatus }) =>
      api.put<Outreach>(`/agency/seo/outreach/${o.id}`, {
        prospectUrl: o.prospectUrl, contactName: o.contactName, contactEmail: o.contactEmail, status, notes: o.notes,
        lastContactedAt: o.lastContactedAt, concurrencyStamp: o.concurrencyStamp,
      }),
    onSuccess: (o) => {
      toast.success(o.status === 'Won' ? 'Marked as won' : 'Marked as lost');
      refresh();
    },
    onError: (err) => toast.error('Could not update the prospect', errorMessage(err)),
  });
  const summary = list.data?.summary;

  const columns: DataTableColumn<Backlink>[] = [
    {
      id: 'source',
      header: 'Linking page',
      primary: true,
      cell: (b) => (
        <span className="stack seo-stack-xs">
          <SafeExternalLink href={b.sourceUrl} className="ui-link">
            {b.sourceDomain}
          </SafeExternalLink>
          <span className="text-small text-muted seo-truncate">{b.sourceUrl}</span>
        </span>
      ),
    },
    { id: 'anchor', header: 'Anchor', cell: (b) => b.anchorText ?? '—' },
    { id: 'status', header: 'Status', cell: (b) => <Badge tone={statusTone[b.status]}>{b.status}</Badge> },
    { id: 'rel', header: 'Rel', hideOnMobile: true, cell: (b) => b.rel || 'follow' },
    { id: 'checked', header: 'Last checked', hideOnMobile: true, cell: (b) => (b.lastCheckedAt ? <DateTime value={b.lastCheckedAt} format="relative" /> : 'Never') },
  ];
  const outreachColumns: DataTableColumn<Outreach>[] = [
    {
      id: 'prospect',
      header: 'Prospect',
      primary: true,
      cell: (o) => (
        <SafeExternalLink href={o.prospectUrl} className="ui-link">
          {o.prospectUrl}
        </SafeExternalLink>
      ),
    },
    { id: 'contact', header: 'Contact', cell: (o) => [o.contactName, o.contactEmail].filter(Boolean).join(' · ') || '—' },
    { id: 'status', header: 'Status', cell: (o) => <Badge tone={o.status === 'Won' ? 'success' : o.status === 'Lost' ? 'danger' : 'neutral'}>{outreachLabel[o.status]}</Badge> },
    { id: 'notes', header: 'Notes', hideOnMobile: true, cell: (o) => o.notes ?? '—' },
  ];

  return (
    <div className="stack">
      <div className="grid-auto seo-stats">
        <Stat label="Backlinks" value={summary?.total ?? '—'} measurement="Count" loading={list.isLoading} />
        <Stat label="Referring domains" value={summary?.referringDomains ?? '—'} measurement="Count" loading={list.isLoading} />
        <Stat label="Live (followed)" value={summary?.live ?? '—'} measurement="Measured" loading={list.isLoading} />
        <Stat label="Lost" value={summary?.lost ?? '—'} measurement="Measured" loading={list.isLoading} />
      </div>
      <div className="seo-toolbar">
        <Button leadingIcon={<Plus />} onClick={() => setAdding('new')}>
          Add backlink
        </Button>
        <CsvImportButton
          label="Import backlinks CSV"
          path={`/agency/seo/sites/${site.id}/backlinks/import`}
          hint="Columns: source_url, target_url, anchor, rel, first_seen (exports from Ahrefs, Semrush or Search Console links work after renaming)."
          withDate={false}
          onDone={(r) => {
            toast.success(`${r.created} backlink(s) imported`, r.errors.slice(0, 3).join(' · ') || undefined);
            refresh();
          }}
        />
        <Button variant="secondary" leadingIcon={<ShieldCheck />} onClick={() => check.mutate()} loading={check.isPending}>
          Check links now
        </Button>
        <FormField label="Status" hideLabel className="seo-inline-filter">
          <Select
            value={status}
            onChange={(e) => setStatus(e.target.value)}
            options={[{ value: '', label: 'All statuses' }, ...(['Live', 'Nofollow', 'Lost', 'Error', 'Unchecked'] as const).map((s) => ({ value: s, label: s }))]}
          />
        </FormField>
      </div>
      <DataTable
        caption="Backlinks"
        columns={columns}
        rows={list.data?.page.items ?? []}
        getRowId={(b) => b.id}
        rowLabel={(b) => b.sourceUrl}
        loading={list.isLoading}
        rowActions={(b) => [
          { id: 'edit', label: 'Edit', icon: <Pencil />, onSelect: () => setAdding(b) },
          { id: 'delete', label: 'Remove', icon: <Trash2 />, danger: true, onSelect: () => setRemoving(b) },
        ]}
        emptyState={<EmptyState compact headingLevel={3} icon={<Link2 />} title="No backlinks" description="Add links manually or import a CSV; the checker verifies them daily." />}
      />
      <Card>
        <CardHeader
          title="Link outreach"
          description="Prospects you are pitching for links."
          headingLevel={3}
          actions={
            <Button size="sm" variant="secondary" leadingIcon={<Plus />} onClick={() => setProspect('new')}>
              Add prospect
            </Button>
          }
        />
        <CardBody>
          <DataTable
            caption="Outreach prospects"
            columns={outreachColumns}
            rows={outreach.data ?? []}
            getRowId={(o) => o.id}
            rowLabel={(o) => o.prospectUrl}
            loading={outreach.isLoading}
            rowActions={(o) => [
              { id: 'edit', label: 'Edit', icon: <Pencil />, onSelect: () => setProspect(o) },
              ...(o.status === 'Won' || o.status === 'Lost'
                ? []
                : [
                    { id: 'won', label: 'Mark link won', icon: <CheckCircle2 />, onSelect: () => setOutcome.mutate({ o, status: 'Won' }) },
                    { id: 'lost', label: 'Mark lost', icon: <XCircle />, onSelect: () => setOutcome.mutate({ o, status: 'Lost' }) },
                  ]),
              { id: 'delete', label: 'Delete', icon: <Trash2 />, danger: true, onSelect: () => setRemovingProspect(o) },
            ]}
            emptyState={<EmptyState compact headingLevel={4} title="No prospects yet" />}
          />
        </CardBody>
      </Card>
      {adding && <BacklinkDialog site={site} backlink={adding === 'new' ? null : adding} onClose={() => setAdding(null)} onSaved={refresh} />}
      <ConfirmDialog
        open={removing !== null}
        onClose={() => setRemoving(null)}
        tone="danger"
        title="Remove this backlink?"
        description={removing ? `${removing.sourceUrl} will no longer be tracked or checked.` : undefined}
        confirmLabel="Remove"
        onConfirm={async () => {
          if (!removing) return;
          await api.delete(`/agency/seo/backlinks/${removing.id}`);
          toast.success('Backlink removed');
          refresh();
        }}
      />
      <ConfirmDialog
        open={removingProspect !== null}
        onClose={() => setRemovingProspect(null)}
        tone="danger"
        title="Delete this prospect?"
        description={removingProspect ? `${removingProspect.prospectUrl} and its notes are removed from outreach.` : undefined}
        confirmLabel="Delete"
        onConfirm={async () => {
          if (!removingProspect) return;
          await api.delete(`/agency/seo/outreach/${removingProspect.id}`);
          toast.success('Prospect deleted');
          refresh();
        }}
      />
      {prospect && <OutreachDialog siteId={site.id} prospect={prospect === 'new' ? null : prospect} onClose={() => setProspect(null)} onSaved={refresh} />}
    </div>
  );
}

export function BacklinkDialog({
  site,
  backlink,
  onClose,
  onSaved,
}: {
  site: Site;
  backlink?: Backlink | null;
  onClose: () => void;
  onSaved: () => void;
}) {
  const [form, setForm] = useState({
    sourceUrl: backlink?.sourceUrl ?? '',
    targetUrl: backlink?.targetUrl ?? site.baseUrl,
    anchorText: backlink?.anchorText ?? '',
    rel: backlink?.rel ?? '',
  });
  const save = useMutation({
    mutationFn: () => {
      const body = { ...form, anchorText: form.anchorText || null, rel: form.rel || null };
      return backlink ? api.put<Backlink>(`/agency/seo/backlinks/${backlink.id}`, body) : api.post<Backlink>(`/agency/seo/sites/${site.id}/backlinks`, body);
    },
    onSuccess: () => {
      onSaved();
      onClose();
    },
  });
  const errors = fieldErrors(save.error);
  return (
    <Dialog
      open
      onClose={onClose}
      title={backlink ? 'Edit backlink' : 'Add a backlink'}
      description={backlink ? 'Changing a URL resets the link check; it is verified again on the next check.' : undefined}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" form="seo-backlink" loading={save.isPending}>
            {backlink ? 'Save' : 'Add'}
          </Button>
        </>
      }
    >
      <form id="seo-backlink" className="stack" onSubmit={(e: FormEvent) => (e.preventDefault(), save.mutate())}>
        {save.isError && <Alert tone="danger" title={backlink ? 'Could not save the backlink' : 'Could not add the backlink'}>{errorMessage(save.error)}</Alert>}
        <FormField label="Linking page URL" required error={firstError(errors, 'sourceUrl')}>
          <Input value={form.sourceUrl} inputMode="url" onChange={(e) => setForm({ ...form, sourceUrl: e.target.value })} />
        </FormField>
        <FormField label="Target URL on the site" required error={firstError(errors, 'targetUrl')}>
          <Input value={form.targetUrl} inputMode="url" onChange={(e) => setForm({ ...form, targetUrl: e.target.value })} />
        </FormField>
        <FormField label="Anchor text" optional>
          <Input value={form.anchorText} onChange={(e) => setForm({ ...form, anchorText: e.target.value })} />
        </FormField>
        <FormField label="Rel attribute" optional hint="Leave empty for a followed link (e.g. nofollow, ugc, sponsored).">
          <Input value={form.rel} onChange={(e) => setForm({ ...form, rel: e.target.value })} />
        </FormField>
      </form>
    </Dialog>
  );
}

function OutreachDialog({ siteId, prospect, onClose, onSaved }: { siteId: string; prospect: Outreach | null; onClose: () => void; onSaved: () => void }) {
  const [form, setForm] = useState({
    prospectUrl: prospect?.prospectUrl ?? '',
    contactName: prospect?.contactName ?? '',
    contactEmail: prospect?.contactEmail ?? '',
    status: prospect?.status ?? ('Identified' as OutreachStatus),
    notes: prospect?.notes ?? '',
  });
  const save = useMutation({
    mutationFn: () => {
      const body = { ...form, contactName: form.contactName || null, contactEmail: form.contactEmail || null, notes: form.notes || null, concurrencyStamp: prospect?.concurrencyStamp };
      return prospect ? api.put(`/agency/seo/outreach/${prospect.id}`, body) : api.post(`/agency/seo/sites/${siteId}/outreach`, body);
    },
    onSuccess: () => {
      onSaved();
      onClose();
    },
  });
  const errors = fieldErrors(save.error);
  return (
    <Dialog
      open
      onClose={onClose}
      title={prospect ? 'Edit prospect' : 'Add a prospect'}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" form="seo-outreach" loading={save.isPending}>
            Save
          </Button>
        </>
      }
    >
      <form id="seo-outreach" className="stack" onSubmit={(e: FormEvent) => (e.preventDefault(), save.mutate())}>
        {save.isError && <Alert tone="danger" title="Could not save">{errorMessage(save.error)}</Alert>}
        <FormField label="Prospect URL" required error={firstError(errors, 'prospectUrl')}>
          <Input value={form.prospectUrl} inputMode="url" onChange={(e) => setForm({ ...form, prospectUrl: e.target.value })} />
        </FormField>
        <div className="seo-grid-2">
          <FormField label="Contact name" optional>
            <Input value={form.contactName} onChange={(e) => setForm({ ...form, contactName: e.target.value })} />
          </FormField>
          <FormField label="Contact email" optional error={firstError(errors, 'contactEmail')}>
            <Input type="email" value={form.contactEmail} onChange={(e) => setForm({ ...form, contactEmail: e.target.value })} />
          </FormField>
        </div>
        <FormField label="Status">
          <Select
            value={form.status}
            onChange={(e) => setForm({ ...form, status: e.target.value as OutreachStatus })}
            options={outreachStatuses.map((s) => ({ value: s, label: outreachLabel[s] }))}
          />
        </FormField>
        <FormField label="Notes" optional>
          <Textarea value={form.notes} rows={3} onChange={(e) => setForm({ ...form, notes: e.target.value })} />
        </FormField>
      </form>
    </Dialog>
  );
}
