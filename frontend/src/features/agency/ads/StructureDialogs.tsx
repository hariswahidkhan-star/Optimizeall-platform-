import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Pencil, Plus, Trash2 } from 'lucide-react';
import { useState, type FormEvent, type ReactNode } from 'react';
import {
  Alert,
  Badge,
  Button,
  Checkbox,
  ConfirmDialog,
  DataTable,
  Dialog,
  EmptyState,
  FormField,
  Input,
  Select,
  Textarea,
  type DataTableColumn,
} from '@/components/ui';
import { api } from '@/lib/api/client';
import { errorMessage, isApiError } from '@/lib/api/errors';
import {
  AD_ENTITY_STATUSES,
  adsKeys,
  type AdAccount,
  type AdEntityStatus,
  type AdGroupRow,
  type AdRow,
  type CampaignRow,
  type Creative,
} from './api';
import { money } from './shared';

function fieldError(error: unknown, field: string): string | undefined {
  if (!isApiError(error)) return undefined;
  return Object.entries(error.errors ?? {}).find(([k]) => k.toLowerCase() === field.toLowerCase())?.[1][0];
}

const statusOptions = AD_ENTITY_STATUSES.map((s) => ({ value: s, label: s }));
const numberOrNull = (v: string) => (v.trim() === '' ? null : Number(v));

function FormDialog({
  title,
  description,
  formId,
  saving,
  error,
  onClose,
  onSubmit,
  size,
  children,
}: {
  title: string;
  description?: ReactNode;
  formId: string;
  saving: boolean;
  error: unknown;
  onClose: () => void;
  onSubmit: () => void;
  size?: 'sm' | 'md' | 'lg';
  children: ReactNode;
}) {
  return (
    <Dialog
      open
      onClose={onClose}
      title={title}
      description={description}
      size={size}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" form={formId} loading={saving}>
            Save
          </Button>
        </>
      }
    >
      <form id={formId} className="stack" onSubmit={(e: FormEvent) => (e.preventDefault(), onSubmit())}>
        {error != null && (
          <Alert tone="danger" title="Could not save">
            {errorMessage(error)}
          </Alert>
        )}
        {children}
      </form>
    </Dialog>
  );
}

/** Edits an ad account's name, id, currency (until metrics exist), time zone and active flag. */
export function AccountEditDialog({ account, onClose }: { account: AdAccount; onClose: () => void }) {
  const queryClient = useQueryClient();
  const [form, setForm] = useState({
    externalAccountId: account.externalAccountId,
    name: account.name,
    currency: account.currency,
    timeZone: account.timeZone,
    isActive: account.isActive,
  });
  const save = useMutation({
    mutationFn: () =>
      api.put<AdAccount>(`/agency/ads/accounts/${account.id}`, {
        clientAccountId: account.clientAccountId,
        platform: account.platform,
        managerUserId: account.managerUserId,
        ...form,
        concurrencyStamp: account.concurrencyStamp,
      }),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: adsKeys.all });
      onClose();
    },
  });
  return (
    <FormDialog
      title="Edit ad account"
      description="Client and platform cannot change. The currency is locked once metrics exist."
      formId="ad-account-edit"
      saving={save.isPending}
      error={save.error}
      onClose={onClose}
      onSubmit={() => save.mutate()}
    >
      <FormField label="Name" required error={fieldError(save.error, 'name')}>
        <Input
          value={form.name}
          maxLength={200}
          onChange={(e) => setForm({ ...form, name: e.target.value })}
        />
      </FormField>
      <FormField label="Account id" required error={fieldError(save.error, 'externalAccountId')}>
        <Input
          value={form.externalAccountId}
          maxLength={64}
          onChange={(e) => setForm({ ...form, externalAccountId: e.target.value })}
        />
      </FormField>
      <div className="ad-stats">
        <FormField label="Currency" error={fieldError(save.error, 'currency')}>
          <Input
            value={form.currency}
            maxLength={3}
            onChange={(e) => setForm({ ...form, currency: e.target.value.toUpperCase() })}
          />
        </FormField>
        <FormField label="Time zone" error={fieldError(save.error, 'timeZone')}>
          <Input value={form.timeZone} onChange={(e) => setForm({ ...form, timeZone: e.target.value })} />
        </FormField>
      </div>
      <Checkbox
        label="Active"
        description="Inactive accounts are skipped by the daily sync but keep their reporting."
        checked={form.isActive}
        onChange={(e) => setForm({ ...form, isActive: e.target.checked })}
      />
    </FormDialog>
  );
}

/** Creates (for an account) or edits a campaign. The client's naming template is enforced for planned campaigns. */
export function CampaignDialog({
  accountId,
  campaign,
  onClose,
}: {
  accountId: string;
  campaign: CampaignRow | null;
  onClose: () => void;
}) {
  const queryClient = useQueryClient();
  const [form, setForm] = useState({
    name: campaign?.name ?? '',
    objective: campaign?.objective ?? '',
    status: (campaign?.status as AdEntityStatus) ?? 'Draft',
    budgetType: campaign?.budgetType ?? 'Daily',
    budgetAmount: campaign?.budgetAmount?.toString() ?? '',
    bidStrategy: campaign?.bidStrategy ?? '',
    startDate: campaign?.startDate ?? '',
    endDate: campaign?.endDate ?? '',
    targetingSummary: campaign?.targetingSummary ?? '',
    targetCpa: campaign?.targetCpa?.toString() ?? '',
    targetRoas: campaign?.targetRoas?.toString() ?? '',
  });
  const save = useMutation({
    mutationFn: () => {
      const body = {
        name: form.name,
        objective: form.objective || null,
        status: form.status,
        budgetType: form.budgetType,
        budgetAmount: numberOrNull(form.budgetAmount),
        bidStrategy: form.bidStrategy || null,
        startDate: form.startDate || null,
        endDate: form.endDate || null,
        targetingSummary: form.targetingSummary || null,
        targetCpa: numberOrNull(form.targetCpa),
        targetRoas: numberOrNull(form.targetRoas),
        concurrencyStamp: campaign?.concurrencyStamp,
      };
      return campaign
        ? api.put(`/agency/ads/campaigns/${campaign.id}`, body)
        : api.post(`/agency/ads/accounts/${accountId}/campaigns`, body);
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: adsKeys.all });
      onClose();
    },
  });
  const synced = campaign && campaign.source !== 'Plan';
  return (
    <FormDialog
      title={campaign ? 'Edit campaign' : 'Plan a campaign'}
      description={
        synced
          ? 'This campaign comes from the platform: the next sync may overwrite changes made here.'
          : undefined
      }
      formId="ad-campaign"
      size="lg"
      saving={save.isPending}
      error={save.error}
      onClose={onClose}
      onSubmit={() => save.mutate()}
    >
      <FormField label="Name" required error={fieldError(save.error, 'name')}>
        <Input
          value={form.name}
          maxLength={300}
          onChange={(e) => setForm({ ...form, name: e.target.value })}
        />
      </FormField>
      <div className="ad-stats">
        <FormField label="Objective" optional>
          <Input
            value={form.objective}
            maxLength={100}
            onChange={(e) => setForm({ ...form, objective: e.target.value })}
          />
        </FormField>
        <FormField label="Status">
          <Select
            value={form.status}
            onChange={(e) => setForm({ ...form, status: e.target.value as AdEntityStatus })}
            options={statusOptions}
          />
        </FormField>
        <FormField label="Budget type">
          <Select
            value={form.budgetType}
            onChange={(e) => setForm({ ...form, budgetType: e.target.value as 'Daily' | 'Lifetime' })}
            options={[
              { value: 'Daily', label: 'Daily' },
              { value: 'Lifetime', label: 'Lifetime' },
            ]}
          />
        </FormField>
        <FormField label="Budget" optional error={fieldError(save.error, 'budgetAmount')}>
          <Input
            type="number"
            min={0}
            step="0.01"
            value={form.budgetAmount}
            onChange={(e) => setForm({ ...form, budgetAmount: e.target.value })}
          />
        </FormField>
        <FormField label="Start date" optional>
          <Input
            type="date"
            value={form.startDate}
            onChange={(e) => setForm({ ...form, startDate: e.target.value })}
          />
        </FormField>
        <FormField label="End date" optional error={fieldError(save.error, 'endDate')}>
          <Input
            type="date"
            value={form.endDate}
            onChange={(e) => setForm({ ...form, endDate: e.target.value })}
          />
        </FormField>
        <FormField label="Target CPA" optional>
          <Input
            type="number"
            min={0}
            step="0.01"
            value={form.targetCpa}
            onChange={(e) => setForm({ ...form, targetCpa: e.target.value })}
          />
        </FormField>
        <FormField label="Target ROAS" optional>
          <Input
            type="number"
            min={0}
            step="0.1"
            value={form.targetRoas}
            onChange={(e) => setForm({ ...form, targetRoas: e.target.value })}
          />
        </FormField>
      </div>
      <FormField label="Bid strategy" optional>
        <Input
          value={form.bidStrategy}
          maxLength={100}
          onChange={(e) => setForm({ ...form, bidStrategy: e.target.value })}
        />
      </FormField>
      <FormField label="Targeting summary" optional>
        <Textarea
          rows={2}
          maxLength={2000}
          value={form.targetingSummary}
          onChange={(e) => setForm({ ...form, targetingSummary: e.target.value })}
        />
      </FormField>
    </FormDialog>
  );
}

function AdGroupDialog({
  campaignId,
  group,
  onClose,
  onSaved,
}: {
  campaignId: string;
  group: AdGroupRow | null;
  onClose: () => void;
  onSaved: () => void;
}) {
  const [form, setForm] = useState({
    name: group?.name ?? '',
    status: group?.status ?? ('Draft' as AdEntityStatus),
    budgetAmount: group?.budgetAmount?.toString() ?? '',
    bidStrategy: group?.bidStrategy ?? '',
    targetingSummary: group?.targetingSummary ?? '',
  });
  const save = useMutation({
    mutationFn: () => {
      const body = {
        name: form.name,
        status: form.status,
        budgetAmount: numberOrNull(form.budgetAmount),
        bidStrategy: form.bidStrategy || null,
        targetingSummary: form.targetingSummary || null,
        concurrencyStamp: group?.concurrencyStamp,
      };
      return group
        ? api.put(`/agency/ads/ad-groups/${group.id}`, body)
        : api.post(`/agency/ads/campaigns/${campaignId}/ad-groups`, body);
    },
    onSuccess: () => {
      onSaved();
      onClose();
    },
  });
  return (
    <FormDialog
      title={group ? 'Edit ad group' : 'New ad group'}
      formId="ad-group"
      saving={save.isPending}
      error={save.error}
      onClose={onClose}
      onSubmit={() => save.mutate()}
    >
      <FormField label="Name" required error={fieldError(save.error, 'name')}>
        <Input
          value={form.name}
          maxLength={300}
          onChange={(e) => setForm({ ...form, name: e.target.value })}
        />
      </FormField>
      <div className="ad-stats">
        <FormField label="Status">
          <Select
            value={form.status}
            onChange={(e) => setForm({ ...form, status: e.target.value as AdEntityStatus })}
            options={statusOptions}
          />
        </FormField>
        <FormField label="Budget" optional>
          <Input
            type="number"
            min={0}
            step="0.01"
            value={form.budgetAmount}
            onChange={(e) => setForm({ ...form, budgetAmount: e.target.value })}
          />
        </FormField>
      </div>
      <FormField label="Bid strategy" optional>
        <Input
          value={form.bidStrategy}
          maxLength={100}
          onChange={(e) => setForm({ ...form, bidStrategy: e.target.value })}
        />
      </FormField>
      <FormField label="Targeting summary" optional>
        <Textarea
          rows={2}
          maxLength={2000}
          value={form.targetingSummary}
          onChange={(e) => setForm({ ...form, targetingSummary: e.target.value })}
        />
      </FormField>
    </FormDialog>
  );
}

function AdDialog({
  groupId,
  ad,
  creatives,
  onClose,
  onSaved,
}: {
  groupId: string;
  ad: AdRow | null;
  creatives: Creative[];
  onClose: () => void;
  onSaved: () => void;
}) {
  const [form, setForm] = useState({
    name: ad?.name ?? '',
    status: ad?.status ?? ('Draft' as AdEntityStatus),
    creativeId: ad?.creativeId ?? '',
  });
  const save = useMutation({
    mutationFn: () => {
      const body = {
        name: form.name,
        status: form.status,
        creativeId: form.creativeId || null,
        concurrencyStamp: ad?.concurrencyStamp,
      };
      return ad
        ? api.put(`/agency/ads/ads/${ad.id}`, body)
        : api.post(`/agency/ads/ad-groups/${groupId}/ads`, body);
    },
    onSuccess: () => {
      onSaved();
      onClose();
    },
  });
  return (
    <FormDialog
      title={ad ? 'Edit ad' : 'New ad'}
      formId="ad-ad"
      saving={save.isPending}
      error={save.error}
      onClose={onClose}
      onSubmit={() => save.mutate()}
    >
      <FormField label="Name" required error={fieldError(save.error, 'name')}>
        <Input
          value={form.name}
          maxLength={300}
          onChange={(e) => setForm({ ...form, name: e.target.value })}
        />
      </FormField>
      <FormField label="Status">
        <Select
          value={form.status}
          onChange={(e) => setForm({ ...form, status: e.target.value as AdEntityStatus })}
          options={statusOptions}
        />
      </FormField>
      <FormField label="Creative" optional hint="From the client's creative library.">
        <Select
          value={form.creativeId}
          placeholder="No creative"
          onChange={(e) => setForm({ ...form, creativeId: e.target.value })}
          options={creatives.map((c) => ({ value: c.id, label: `${c.name} (${c.status})` }))}
        />
      </FormField>
    </FormDialog>
  );
}

type Removal = { kind: 'group'; row: AdGroupRow } | { kind: 'ad'; row: AdRow };

/** Ad groups and ads of one campaign, with add/edit/delete. Synced entities can only change status (the platform owns them). */
export function CampaignStructureDialog({
  campaign,
  clientId,
  onClose,
}: {
  campaign: CampaignRow;
  clientId: string;
  onClose: () => void;
}) {
  const queryClient = useQueryClient();
  const [groupId, setGroupId] = useState<string | null>(null);
  const [editingGroup, setEditingGroup] = useState<AdGroupRow | 'new' | null>(null);
  const [editingAd, setEditingAd] = useState<AdRow | 'new' | null>(null);
  const [removing, setRemoving] = useState<Removal | null>(null);
  const groups = useQuery({
    queryKey: ['ads', 'campaign', campaign.id, 'groups'],
    queryFn: () => api.get<AdGroupRow[]>(`/agency/ads/campaigns/${campaign.id}/ad-groups`),
  });
  const ads = useQuery({
    queryKey: ['ads', 'group', groupId, 'ads'],
    queryFn: () => api.get<AdRow[]>(`/agency/ads/ad-groups/${groupId}/ads`),
    enabled: !!groupId,
  });
  const creatives = useQuery({
    queryKey: adsKeys.creatives(clientId),
    queryFn: () => api.get<Creative[]>('/agency/ads/creatives', { query: { clientId } }),
  });
  const refresh = () => {
    void queryClient.invalidateQueries({ queryKey: ['ads', 'campaign', campaign.id] });
    void queryClient.invalidateQueries({ queryKey: ['ads', 'group'] });
  };
  const planned = (source: string) => source === 'Plan';
  const groupColumns: DataTableColumn<AdGroupRow>[] = [
    {
      id: 'name',
      header: 'Ad group',
      primary: true,
      cell: (g) => (
        <Button variant="link" size="sm" onClick={() => setGroupId(g.id)} aria-pressed={groupId === g.id}>
          {g.name}
        </Button>
      ),
    },
    { id: 'status', header: 'Status', cell: (g) => <Badge>{g.status}</Badge> },
    { id: 'spend', header: 'Spend', align: 'right', cell: (g) => money(g.totals.spend, campaign.currency) },
    { id: 'source', header: 'Source', hideOnMobile: true, cell: (g) => g.source },
  ];
  const adColumns: DataTableColumn<AdRow>[] = [
    { id: 'name', header: 'Ad', primary: true, cell: (a) => a.name },
    { id: 'status', header: 'Status', cell: (a) => <Badge>{a.status}</Badge> },
    {
      id: 'creative',
      header: 'Creative',
      cell: (a) => creatives.data?.find((c) => c.id === a.creativeId)?.name ?? '—',
    },
    { id: 'spend', header: 'Spend', align: 'right', cell: (a) => money(a.totals.spend, campaign.currency) },
  ];
  const selectedGroup = groups.data?.find((g) => g.id === groupId);
  return (
    <Dialog
      open
      onClose={onClose}
      size="lg"
      title={`Structure — ${campaign.name}`}
      description="Planned ad groups and ads can be edited or deleted; synced ones keep their history — set them to Removed instead."
    >
      <div className="stack">
        <div className="cluster">
          <Button size="sm" leadingIcon={<Plus />} onClick={() => setEditingGroup('new')}>
            New ad group
          </Button>
        </div>
        <DataTable
          caption="Ad groups"
          columns={groupColumns}
          rows={groups.data ?? []}
          getRowId={(g) => g.id}
          rowLabel={(g) => g.name}
          loading={groups.isLoading}
          rowActions={(g) => [
            { id: 'edit', label: 'Edit', icon: <Pencil />, onSelect: () => setEditingGroup(g) },
            {
              id: 'delete',
              label: 'Delete',
              icon: <Trash2 />,
              danger: true,
              disabled: !planned(g.source),
              description: planned(g.source)
                ? undefined
                : 'From the platform — set status to Removed instead.',
              onSelect: () => setRemoving({ kind: 'group', row: g }),
            },
          ]}
          emptyState={<EmptyState compact headingLevel={3} title="No ad groups" />}
        />
        {selectedGroup && (
          <section className="stack" aria-label={`Ads of ${selectedGroup.name}`}>
            <div className="cluster">
              <strong>Ads of {selectedGroup.name}</strong>
              <Button
                size="sm"
                variant="secondary"
                leadingIcon={<Plus />}
                onClick={() => setEditingAd('new')}
              >
                New ad
              </Button>
            </div>
            <DataTable
              caption={`Ads of ${selectedGroup.name}`}
              columns={adColumns}
              rows={ads.data ?? []}
              getRowId={(a) => a.id}
              rowLabel={(a) => a.name}
              loading={ads.isLoading}
              rowActions={(a) => [
                { id: 'edit', label: 'Edit', icon: <Pencil />, onSelect: () => setEditingAd(a) },
                {
                  id: 'delete',
                  label: 'Delete',
                  icon: <Trash2 />,
                  danger: true,
                  disabled: !planned(a.source),
                  description: planned(a.source)
                    ? undefined
                    : 'From the platform — set status to Removed instead.',
                  onSelect: () => setRemoving({ kind: 'ad', row: a }),
                },
              ]}
              emptyState={<EmptyState compact headingLevel={4} title="No ads in this group" />}
            />
          </section>
        )}
      </div>
      {editingGroup && (
        <AdGroupDialog
          campaignId={campaign.id}
          group={editingGroup === 'new' ? null : editingGroup}
          onClose={() => setEditingGroup(null)}
          onSaved={refresh}
        />
      )}
      {editingAd && groupId && (
        <AdDialog
          groupId={groupId}
          ad={editingAd === 'new' ? null : editingAd}
          creatives={creatives.data ?? []}
          onClose={() => setEditingAd(null)}
          onSaved={refresh}
        />
      )}
      <ConfirmDialog
        open={removing !== null}
        onClose={() => setRemoving(null)}
        tone="danger"
        title={removing?.kind === 'group' ? 'Delete this ad group and its ads?' : 'Delete this ad?'}
        description={removing ? `“${removing.row.name}” is removed from the plan.` : undefined}
        confirmLabel="Delete"
        onConfirm={async () => {
          if (!removing) return;
          await api.delete(
            removing.kind === 'group'
              ? `/agency/ads/ad-groups/${removing.row.id}`
              : `/agency/ads/ads/${removing.row.id}`,
          );
          if (removing.kind === 'group' && removing.row.id === groupId) setGroupId(null);
          refresh();
        }}
      />
    </Dialog>
  );
}
