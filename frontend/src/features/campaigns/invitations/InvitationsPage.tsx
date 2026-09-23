import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Eye, Link2, Pencil, Plus, Trash2 } from 'lucide-react';
import { useState, type FormEvent } from 'react';
import {
  Alert,
  Badge,
  Button,
  Card,
  CardBody,
  Checkbox,
  ConfirmDialog,
  CopyField,
  DataTable,
  DateTime,
  Dialog,
  EmptyState,
  ErrorState,
  FilterBar,
  FormField,
  Input,
  KeyValueList,
  Money,
  PageHeader,
  Pagination,
  Select,
  Skeleton,
  useToast,
  type DataTableColumn,
} from '@/components/ui';
import { api } from '@/lib/api/client';
import { errorMessage, isApiError } from '@/lib/api/errors';
import type { PagedResult } from '@/lib/api/types';
import { useAuth } from '@/lib/auth/useAuth';
import { browserTimeZone } from '@/lib/format/dates';
import { formatNumber } from '@/lib/format/money';
import { qk, useCampaignOptions } from '../api/queries';
import type { Invitation, InvitationInput, PublicInvitationLanding } from '../api/types';
import { fieldError, fieldErrorsFrom, type FieldErrorMap } from '../shared/formErrors';
import { isoToZonedInput, zonedInputToIso } from '../shared/zonedTime';
import '../campaigns.css';

const CODE_FIELDS: Record<string, string> = {
  'invitation.campaign_not_found': 'campaignId',
  'invitation.campaign_closed': 'campaignId',
  'invitation.expiry_in_past': 'expiresAt',
};

export function InvitationsPage() {
  const queryClient = useQueryClient();
  const toast = useToast();
  const campaigns = useCampaignOptions();
  const [search, setSearch] = useState('');
  const [filters, setFilters] = useState<Record<string, string | undefined>>({});
  const [page, setPage] = useState(1);
  const [editing, setEditing] = useState<Invitation | 'new' | null>(null);
  const [previewing, setPreviewing] = useState<Invitation | null>(null);
  const [removing, setRemoving] = useState<Invitation | null>(null);

  const params = {
    search,
    campaignId: filters.campaign,
    isActive: filters.active,
    page,
    pageSize: 25,
  };
  const query = useQuery({
    queryKey: qk.invitations(params),
    queryFn: () => api.get<PagedResult<Invitation>>('/marketing/invitations', { query: params }),
    placeholderData: keepPreviousData,
  });

  const columns: DataTableColumn<Invitation>[] = [
    {
      id: 'name',
      header: 'Invitation',
      primary: true,
      cell: (i) => (
        <span className="stack mg-stack-xs">
          <span className="mg-strong">{i.name}</span>
          <span className="text-small text-muted">
            {i.campaignTitle ?? 'Platform invitation'} · <code>{i.code}</code>
          </span>
        </span>
      ),
    },
    {
      id: 'state',
      header: 'State',
      cell: (i) =>
        !i.isActive ? (
          <Badge tone="neutral">Inactive</Badge>
        ) : i.isUsable ? (
          <Badge tone="success">Usable</Badge>
        ) : (
          <Badge tone="warning">Expired or used up</Badge>
        ),
    },
    { id: 'visits', header: 'Visits', align: 'right', cell: (i) => formatNumber(i.stats.visits) },
    {
      id: 'registrations',
      header: 'Registrations',
      align: 'right',
      cell: (i) => formatNumber(i.stats.registrations),
    },
    {
      id: 'remaining',
      header: 'Remaining uses',
      align: 'right',
      cell: (i) => (i.stats.remainingUses === null ? 'Unlimited' : formatNumber(i.stats.remainingUses)),
    },
    {
      id: 'expires',
      header: 'Expires',
      hideOnMobile: true,
      cell: (i) => (i.expiresAt ? <DateTime value={i.expiresAt} format="date" /> : 'Never'),
    },
    {
      id: 'utm',
      header: 'UTM',
      hideOnMobile: true,
      cell: (i) =>
        [i.utmSource, i.utmMedium, i.utmCampaign].some(Boolean) ? (
          <span className="text-small">
            {[i.utmSource, i.utmMedium, i.utmCampaign].map((v) => v ?? '—').join(' / ')}
          </span>
        ) : (
          '—'
        ),
    },
  ];

  return (
    <>
      <PageHeader
        title="Invitations & landing pages"
        description="Invite links for the platform or a specific campaign, with their own landing page and UTM tags."
        actions={
          <Button leadingIcon={<Plus />} onClick={() => setEditing('new')}>
            New invitation
          </Button>
        }
      />
      <div className="stack">
        <FilterBar
          search={search}
          onSearchChange={(s) => {
            setSearch(s);
            setPage(1);
          }}
          searchLabel="Search invitations"
          searchPlaceholder="Search name or code…"
          filters={[
            {
              id: 'campaign',
              label: 'Campaign',
              options: (campaigns.data?.items ?? []).map((c) => ({ value: c.id, label: c.title })),
            },
            {
              id: 'active',
              label: 'Active',
              options: [
                { value: 'true', label: 'Active' },
                { value: 'false', label: 'Inactive' },
              ],
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
          <ErrorState error={query.error} onRetry={() => void query.refetch()} />
        ) : (
          <>
            <DataTable
              caption="Invitations"
              columns={columns}
              rows={query.data?.items ?? []}
              getRowId={(i) => i.id}
              rowLabel={(i) => i.name}
              loading={query.isLoading}
              rowActions={(i) => [
                {
                  id: 'preview',
                  label: 'Preview landing page',
                  icon: <Eye />,
                  onSelect: () => setPreviewing(i),
                },
                { id: 'edit', label: 'Edit', icon: <Pencil />, onSelect: () => setEditing(i) },
                {
                  id: 'delete',
                  label: 'Delete or deactivate…',
                  icon: <Trash2 />,
                  danger: true,
                  onSelect: () => setRemoving(i),
                },
              ]}
              emptyState={
                <EmptyState
                  icon={<Link2 />}
                  headingLevel={2}
                  title="No invitations"
                  description="Create an invite link to share in newsletters, communities or with partners."
                />
              }
            />
            {query.data && query.data.total > 0 && (
              <Pagination page={page} pageSize={25} total={query.data.total} onPageChange={setPage} />
            )}
          </>
        )}
      </div>
      {editing && (
        <InvitationDialog
          invitation={editing === 'new' ? null : editing}
          onClose={() => setEditing(null)}
          onSaved={(saved) => {
            setEditing(null);
            void queryClient.invalidateQueries({ queryKey: ['manage', 'invitations'] });
            if (editing === 'new') setPreviewing(saved);
          }}
        />
      )}
      {previewing && <InvitationPreview invitation={previewing} onClose={() => setPreviewing(null)} />}
      <ConfirmDialog
        open={!!removing}
        onClose={() => setRemoving(null)}
        tone="danger"
        title="Remove this invitation?"
        description="Links that were never visited or used are deleted. Otherwise the link is deactivated and keeps its stats."
        confirmLabel="Remove"
        onConfirm={async () => {
          if (!removing) return;
          const result = await api.delete<{ deleted: boolean; deactivated: boolean }>(
            `/marketing/invitations/${removing.id}`,
          );
          toast.success(result.deleted ? 'Invitation deleted' : 'Invitation deactivated');
          await queryClient.invalidateQueries({ queryKey: ['manage', 'invitations'] });
        }}
      />
    </>
  );
}

function InvitationDialog({
  invitation,
  onClose,
  onSaved,
}: {
  invitation: Invitation | null;
  onClose: () => void;
  onSaved: (saved: Invitation) => void;
}) {
  const toast = useToast();
  const { user } = useAuth();
  const timeZone = user?.timeZone || browserTimeZone();
  const campaigns = useCampaignOptions();
  const [form, setForm] = useState({
    name: invitation?.name ?? '',
    campaignId: invitation?.campaignId ?? '',
    utmSource: invitation?.utmSource ?? '',
    utmMedium: invitation?.utmMedium ?? '',
    utmCampaign: invitation?.utmCampaign ?? '',
    expiresAt: isoToZonedInput(invitation?.expiresAt, timeZone),
    maxUses: invitation?.maxUses ? String(invitation.maxUses) : '',
    isActive: invitation?.isActive ?? true,
  });
  const [errors, setErrors] = useState<FieldErrorMap>({});
  const [formError, setFormError] = useState<string | null>(null);
  const set = (patch: Partial<typeof form>) => setForm((f) => ({ ...f, ...patch }));

  const save = useMutation({
    mutationFn: (body: InvitationInput) =>
      invitation
        ? api.put<Invitation>(`/marketing/invitations/${invitation.id}`, body)
        : api.post<Invitation>('/marketing/invitations', body),
    onSuccess: (saved) => {
      toast.success(invitation ? 'Invitation saved' : 'Invitation created', saved.name);
      onSaved(saved);
    },
    onError: (err) => {
      const mapped = fieldErrorsFrom(err, CODE_FIELDS);
      setErrors(mapped);
      setFormError(Object.keys(mapped).length ? null : errorMessage(err));
    },
  });

  const submit = (event: FormEvent) => {
    event.preventDefault();
    const local: FieldErrorMap = {};
    if (form.name.trim().length < 2) local.name = ['Enter a name (at least 2 characters).'];
    const maxUses = form.maxUses.trim() ? Number(form.maxUses) : null;
    if (maxUses !== null && (!Number.isInteger(maxUses) || maxUses < 1))
      local.maxuses = ['Use a whole number of 1 or more.'];
    setErrors(local);
    if (Object.keys(local).length) return;
    const trim = (s: string) => s.trim() || null;
    save.mutate({
      name: form.name.trim(),
      campaignId: form.campaignId || null,
      utmSource: trim(form.utmSource),
      utmMedium: trim(form.utmMedium),
      utmCampaign: trim(form.utmCampaign),
      expiresAt: form.expiresAt ? zonedInputToIso(form.expiresAt, timeZone) : null,
      maxUses,
      isActive: form.isActive,
    });
  };

  return (
    <Dialog
      open
      onClose={onClose}
      title={invitation ? 'Edit invitation' : 'New invitation'}
      dismissible={!save.isPending}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={save.isPending}>
            Cancel
          </Button>
          <Button type="submit" form="invitation-form" loading={save.isPending}>
            {invitation ? 'Save invitation' : 'Create invitation'}
          </Button>
        </>
      }
    >
      <form id="invitation-form" className="stack" onSubmit={submit} noValidate>
        {formError && (
          <Alert tone="danger" role="alert">
            {formError}
          </Alert>
        )}
        {invitation && <CopyField label="Invitation link" value={invitation.url} />}
        <FormField
          label="Name"
          required
          hint="For your reference, e.g. “Spring newsletter”."
          error={fieldError(errors, 'name')}
        >
          <Input value={form.name} maxLength={150} onChange={(e) => set({ name: e.target.value })} />
        </FormField>
        <FormField
          label="Campaign"
          optional
          hint="Leave empty for a platform-wide invitation. Invite-only campaigns can be reached through their invitations."
          error={fieldError(errors, 'campaignId')}
        >
          <Select
            value={form.campaignId}
            options={[
              { value: '', label: 'Platform invitation (no campaign)' },
              ...(campaigns.data?.items ?? [])
                .filter((c) => c.status !== 'Ended' && c.status !== 'Archived')
                .map((c) => ({ value: c.id, label: `${c.title} (${c.status})` })),
            ]}
            onChange={(e) => set({ campaignId: e.target.value })}
          />
        </FormField>
        <div className="mg-grid mg-grid--3">
          <FormField label="UTM source" optional error={fieldError(errors, 'utmSource')}>
            <Input
              value={form.utmSource}
              maxLength={100}
              onChange={(e) => set({ utmSource: e.target.value })}
            />
          </FormField>
          <FormField label="UTM medium" optional error={fieldError(errors, 'utmMedium')}>
            <Input
              value={form.utmMedium}
              maxLength={100}
              onChange={(e) => set({ utmMedium: e.target.value })}
            />
          </FormField>
          <FormField label="UTM campaign" optional error={fieldError(errors, 'utmCampaign')}>
            <Input
              value={form.utmCampaign}
              maxLength={100}
              onChange={(e) => set({ utmCampaign: e.target.value })}
            />
          </FormField>
        </div>
        <div className="mg-grid mg-grid--2">
          <FormField
            label="Expires"
            optional
            hint={`In ${timeZone}. Blank = never.`}
            error={fieldError(errors, 'expiresAt')}
          >
            <Input
              type="datetime-local"
              value={form.expiresAt}
              onChange={(e) => set({ expiresAt: e.target.value })}
            />
          </FormField>
          <FormField
            label="Maximum uses"
            optional
            hint="Registrations allowed. Blank = unlimited."
            error={fieldError(errors, 'maxUses')}
          >
            <Input
              type="number"
              min={1}
              step={1}
              value={form.maxUses}
              onChange={(e) => set({ maxUses: e.target.value })}
            />
          </FormField>
        </div>
        <Checkbox
          label="Active"
          checked={form.isActive}
          onChange={(e) => set({ isActive: e.target.checked })}
        />
      </form>
    </Dialog>
  );
}

/**
 * Renders the public landing payload (`GET /public/invitations/{code}`) as participants will see it. Note: the
 * public endpoint counts a visit each time it is loaded, so the preview is fetched on demand only.
 */
function InvitationPreview({ invitation, onClose }: { invitation: Invitation; onClose: () => void }) {
  const query = useQuery({
    queryKey: ['manage', 'invitation-preview', invitation.code],
    queryFn: () =>
      api.get<PublicInvitationLanding>(`/public/invitations/${encodeURIComponent(invitation.code)}`),
    staleTime: Infinity,
    retry: false,
  });
  const data = query.data;
  const notUsable = isApiError(query.error) && query.error.status === 404;

  return (
    <Dialog
      open
      onClose={onClose}
      title="Landing page preview"
      description={invitation.name}
      size="lg"
      footer={
        <Button variant="secondary" onClick={onClose}>
          Close
        </Button>
      }
    >
      <div className="stack">
        <CopyField label="Invitation link" value={invitation.url} />
        <p className="text-small text-muted">Loading this preview counts as one landing page visit.</p>
        {query.isLoading ? (
          <Skeleton height={220} />
        ) : notUsable ? (
          <Alert tone="warning" title="This link doesn't open a landing page right now">
            The code is inactive, expired or used up, or its campaign is not Scheduled or Active. Visitors see
            a “not found” page.
          </Alert>
        ) : query.isError ? (
          <ErrorState compact headingLevel={3} error={query.error} onRetry={() => void query.refetch()} />
        ) : data ? (
          <Card className="mg-landing">
            {data.heroImageUrl && <img className="mg-landing__hero" src={data.heroImageUrl} alt="" />}
            <CardBody className="stack">
              <p className="eyebrow">
                {data.type === 'campaign' ? 'Campaign invitation' : 'Join Optimize All'}
              </p>
              <h3 className="mg-landing__headline">{data.headline}</h3>
              <p className="mg-prose">{data.body}</p>
              {data.campaign && (
                <KeyValueList
                  items={[
                    { label: 'Campaign', value: data.campaign.title },
                    { label: 'Platforms', value: data.campaign.platforms.join(', ') || '—' },
                    {
                      label: 'Base reward',
                      value: data.campaign.reward ? (
                        <Money
                          amount={data.campaign.reward.baseAmount}
                          currency={data.campaign.reward.currency}
                        />
                      ) : (
                        '—'
                      ),
                    },
                    {
                      label: 'Runs',
                      value: (
                        <>
                          <DateTime value={data.campaign.startsAt} format="date" /> –{' '}
                          <DateTime value={data.campaign.endsAt} format="date" />
                        </>
                      ),
                    },
                    ...(data.campaign.category
                      ? [{ label: 'Category', value: data.campaign.category.name }]
                      : []),
                  ]}
                />
              )}
              {data.utm && (
                <p className="text-small text-muted">
                  UTM:{' '}
                  {[data.utm.source, data.utm.medium, data.utm.campaign].map((v) => v ?? '—').join(' / ')}
                </p>
              )}
              {data.experiment && <Badge tone="info">Experiment variant {data.experiment.key}</Badge>}
            </CardBody>
          </Card>
        ) : null}
      </div>
    </Dialog>
  );
}
