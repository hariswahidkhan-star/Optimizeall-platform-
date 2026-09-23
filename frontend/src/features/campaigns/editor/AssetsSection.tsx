import { useMutation, useQueryClient } from '@tanstack/react-query';
import { ArrowDown, ArrowUp, FileImage, Link2, Pencil, Plus, Trash2, Type } from 'lucide-react';
import { useState, type FormEvent } from 'react';
import {
  Alert,
  Badge,
  Button,
  ConfirmDialog,
  Dialog,
  EmptyState,
  FormField,
  IconButton,
  Input,
  Select,
  Textarea,
  useToast,
} from '@/components/ui';
import { api } from '@/lib/api/client';
import { errorMessage } from '@/lib/api/errors';
import { qk, useTemplateOptions } from '../api/queries';
import type { AssetInput, CampaignAsset, CampaignAssetType } from '../api/types';
import { fieldError, fieldErrorsFrom, type FieldErrorMap } from '../shared/formErrors';
import { platformOptions } from '../shared/labels';
import { ImageUpload } from './sections';

const ASSET_CODE_FIELDS: Record<string, string> = {
  'campaign.asset_url_invalid': 'url',
  'campaign.asset_url_required': 'url',
  'campaign.asset_file_invalid': 'url',
  'campaign.asset_body_required': 'body',
  'campaign.asset_platform_invalid': 'platform',
  'campaign.template_not_found': 'templateId',
};

const TYPE_ICONS: Record<string, typeof FileImage> = { Image: FileImage, Caption: Type, Link: Link2 };

interface AssetDraft {
  id: string | null;
  type: CampaignAssetType;
  title: string;
  url: string;
  fileId: string;
  body: string;
  platform: string;
  templateId: string;
}

function draftFrom(asset?: CampaignAsset, type: CampaignAssetType = 'Caption'): AssetDraft {
  return {
    id: asset?.id ?? null,
    type: asset?.type ?? type,
    title: asset?.title ?? '',
    url: asset?.fileId ? '' : (asset?.url ?? ''),
    fileId: asset?.fileId ?? '',
    body: asset?.body ?? '',
    platform: asset?.platform ?? '',
    templateId: asset?.templateId ?? '',
  };
}

/**
 * Content assets of a saved campaign. Each change is saved immediately through the assets endpoints; order is
 * changed with keyboard-accessible up/down buttons and persisted with `/assets/reorder`.
 */
export function AssetsSection({
  campaignId,
  assets,
  disabled,
}: {
  campaignId: string;
  assets: CampaignAsset[];
  disabled?: boolean;
}) {
  const queryClient = useQueryClient();
  const toast = useToast();
  const [editing, setEditing] = useState<AssetDraft | null>(null);
  const [removing, setRemoving] = useState<CampaignAsset | null>(null);
  const [announcement, setAnnouncement] = useState('');
  const sorted = [...assets].sort((a, b) => a.sortOrder - b.sortOrder);
  const templates = useTemplateOptions();

  const refresh = () => queryClient.invalidateQueries({ queryKey: qk.campaign(campaignId) });

  const reorder = useMutation({
    mutationFn: (ids: string[]) =>
      api.post<CampaignAsset[]>(`/admin/campaigns/${campaignId}/assets/reorder`, { assetIds: ids }),
    onSuccess: () => refresh(),
    onError: (err) => toast.error('Could not reorder assets', errorMessage(err)),
  });

  const move = (index: number, delta: number) => {
    const target = index + delta;
    if (target < 0 || target >= sorted.length) return;
    const ids = sorted.map((a) => a.id);
    [ids[index], ids[target]] = [ids[target]!, ids[index]!];
    const moved = sorted[index]!;
    setAnnouncement(`${moved.title} moved to position ${target + 1} of ${sorted.length}.`);
    reorder.mutate(ids);
  };

  return (
    <div className="stack">
      <div className="cluster mg-space-between">
        <p className="text-muted text-small">
          Assets are saved immediately. Participants see them in this order.
        </p>
        <Button
          size="sm"
          leadingIcon={<Plus />}
          disabled={disabled}
          onClick={() => setEditing(draftFrom(undefined, 'Caption'))}
        >
          Add asset
        </Button>
      </div>
      <p className="visually-hidden" aria-live="polite">
        {announcement}
      </p>
      {sorted.length === 0 ? (
        <EmptyState
          compact
          headingLevel={3}
          icon={<FileImage />}
          title="No assets yet"
          description="Add captions, links or images participants can use. A campaign needs assets or posting instructions to publish."
        />
      ) : (
        <ol className="mg-assets" aria-label="Campaign assets">
          {sorted.map((asset, index) => {
            const Icon = TYPE_ICONS[asset.type] ?? FileImage;
            const template = templates.data?.items.find((t) => t.id === asset.templateId);
            return (
              <li key={asset.id} className="mg-asset">
                <span className="mg-asset__icon" aria-hidden="true">
                  <Icon />
                </span>
                {asset.type === 'Image' && asset.url && (
                  <img className="mg-asset__thumb" src={asset.url} alt="" />
                )}
                <div className="mg-asset__main">
                  <p className="mg-asset__title">
                    <span className="visually-hidden">Position {index + 1}: </span>
                    {asset.title}
                  </p>
                  <div className="cluster mg-cluster-sm">
                    <Badge size="sm">{asset.type}</Badge>
                    {asset.platform && (
                      <Badge size="sm" tone="info">
                        {asset.platform}
                      </Badge>
                    )}
                    {asset.templateId && (
                      <Badge size="sm" tone="brand">
                        Template: {template?.name ?? 'linked'}
                      </Badge>
                    )}
                  </div>
                  {asset.body && <p className="mg-asset__body">{asset.body}</p>}
                  {asset.url && asset.type !== 'Image' && (
                    <p className="mg-asset__body mg-break">{asset.url}</p>
                  )}
                </div>
                <div className="mg-asset__actions cluster mg-cluster-sm">
                  <IconButton
                    size="sm"
                    variant="ghost"
                    label={`Move ${asset.title} up`}
                    icon={<ArrowUp />}
                    disabled={disabled || index === 0 || reorder.isPending}
                    onClick={() => move(index, -1)}
                  />
                  <IconButton
                    size="sm"
                    variant="ghost"
                    label={`Move ${asset.title} down`}
                    icon={<ArrowDown />}
                    disabled={disabled || index === sorted.length - 1 || reorder.isPending}
                    onClick={() => move(index, 1)}
                  />
                  <IconButton
                    size="sm"
                    variant="ghost"
                    label={`Edit ${asset.title}`}
                    icon={<Pencil />}
                    disabled={disabled}
                    onClick={() => setEditing(draftFrom(asset))}
                  />
                  <IconButton
                    size="sm"
                    variant="ghost"
                    label={`Delete ${asset.title}`}
                    icon={<Trash2 />}
                    disabled={disabled}
                    onClick={() => setRemoving(asset)}
                  />
                </div>
              </li>
            );
          })}
        </ol>
      )}

      {editing && (
        <AssetDialog
          campaignId={campaignId}
          draft={editing}
          onClose={() => setEditing(null)}
          onSaved={() => {
            setEditing(null);
            void refresh();
          }}
        />
      )}
      <ConfirmDialog
        open={!!removing}
        onClose={() => setRemoving(null)}
        tone="danger"
        title="Delete this asset?"
        description={removing ? `“${removing.title}” will be removed from the campaign.` : undefined}
        confirmLabel="Delete asset"
        onConfirm={async () => {
          if (!removing) return;
          await api.delete(`/admin/campaigns/${campaignId}/assets/${removing.id}`);
          toast.success('Asset deleted');
          await refresh();
        }}
      />
    </div>
  );
}

function AssetDialog({
  campaignId,
  draft: initial,
  onClose,
  onSaved,
}: {
  campaignId: string;
  draft: AssetDraft;
  onClose: () => void;
  onSaved: () => void;
}) {
  const toast = useToast();
  const [draft, setDraft] = useState(initial);
  const [errors, setErrors] = useState<FieldErrorMap>({});
  const [formError, setFormError] = useState<string | null>(null);
  const templates = useTemplateOptions();
  const set = (patch: Partial<AssetDraft>) => setDraft((d) => ({ ...d, ...patch }));

  const save = useMutation({
    mutationFn: (body: AssetInput) =>
      draft.id
        ? api.put<CampaignAsset>(`/admin/campaigns/${campaignId}/assets/${draft.id}`, body)
        : api.post<CampaignAsset>(`/admin/campaigns/${campaignId}/assets`, body),
    onSuccess: () => {
      toast.success(draft.id ? 'Asset updated' : 'Asset added');
      onSaved();
    },
    onError: (err) => {
      const mapped = fieldErrorsFrom(err, ASSET_CODE_FIELDS);
      setErrors(mapped);
      setFormError(Object.keys(mapped).length > 0 ? null : errorMessage(err));
    },
  });

  const submit = (event: FormEvent) => {
    event.preventDefault();
    const local: FieldErrorMap = {};
    if (!draft.title.trim()) local.title = ['Enter a title.'];
    if (draft.type === 'Caption' && !draft.body.trim()) local.body = ['Enter the caption text.'];
    if ((draft.type === 'Link' || draft.type === 'Video' || draft.type === 'Document') && !draft.url.trim())
      local.url = ['Enter an https URL.'];
    if (draft.type === 'Image' && !draft.url.trim() && !draft.fileId)
      local.url = ['Upload an image or enter its URL.'];
    setErrors(local);
    if (Object.keys(local).length > 0) return;
    save.mutate({
      type: draft.type,
      title: draft.title.trim(),
      url: draft.fileId ? null : draft.url.trim() || null,
      fileId: draft.fileId || null,
      body: draft.body.trim() || null,
      platform: (draft.platform || null) as AssetInput['platform'],
      templateId: draft.templateId || null,
    });
  };

  const templateOptions = [
    { value: '', label: 'No template' },
    ...(templates.data?.items ?? [])
      .filter((t) => !t.isArchived || t.id === draft.templateId)
      .map((t) => ({ value: t.id, label: `${t.name}${t.platform ? ` · ${t.platform}` : ''}` })),
  ];

  return (
    <Dialog
      open
      onClose={onClose}
      title={draft.id ? 'Edit asset' : 'Add asset'}
      size="md"
      dismissible={!save.isPending}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={save.isPending}>
            Cancel
          </Button>
          <Button type="submit" form="asset-form" loading={save.isPending}>
            {draft.id ? 'Save asset' : 'Add asset'}
          </Button>
        </>
      }
    >
      <form id="asset-form" className="stack" onSubmit={submit} noValidate>
        {formError && (
          <Alert tone="danger" role="alert">
            {formError}
          </Alert>
        )}
        <FormField label="Type" required error={fieldError(errors, 'type')}>
          <Select
            value={draft.type}
            onChange={(e) => set({ type: e.target.value as CampaignAssetType })}
            options={[
              { value: 'Caption', label: 'Caption' },
              { value: 'Link', label: 'Link' },
              { value: 'Image', label: 'Image' },
              { value: 'Video', label: 'Video (link)' },
              { value: 'Document', label: 'Document (link)' },
            ]}
          />
        </FormField>
        <FormField label="Title" required error={fieldError(errors, 'title')}>
          <Input value={draft.title} maxLength={200} onChange={(e) => set({ title: e.target.value })} />
        </FormField>
        {draft.type === 'Caption' && (
          <FormField
            label="Caption"
            required
            hint={`${draft.body.length}/10000`}
            error={fieldError(errors, 'body')}
          >
            <Textarea
              value={draft.body}
              rows={5}
              maxLength={10000}
              onChange={(e) => set({ body: e.target.value })}
            />
          </FormField>
        )}
        {draft.type === 'Image' ? (
          <>
            {draft.fileId ? (
              <Alert
                tone="success"
                actions={
                  <Button size="sm" variant="ghost" onClick={() => set({ fileId: '' })}>
                    Remove
                  </Button>
                }
              >
                Uploaded image attached.
              </Alert>
            ) : (
              <>
                <ImageUpload label="Upload image" onUploaded={(file) => set({ fileId: file.id, url: '' })} />
                <FormField label="Or image URL" optional hint="https only" error={fieldError(errors, 'url')}>
                  <Input type="url" value={draft.url} onChange={(e) => set({ url: e.target.value })} />
                </FormField>
              </>
            )}
          </>
        ) : (
          draft.type !== 'Caption' && (
            <FormField label="URL" required hint="https only" error={fieldError(errors, 'url')}>
              <Input
                type="url"
                value={draft.url}
                maxLength={1000}
                onChange={(e) => set({ url: e.target.value })}
              />
            </FormField>
          )
        )}
        {draft.type !== 'Caption' && (
          <FormField label="Notes or text" optional error={fieldError(errors, 'body')}>
            <Textarea
              value={draft.body}
              rows={3}
              maxLength={10000}
              onChange={(e) => set({ body: e.target.value })}
            />
          </FormField>
        )}
        <div className="mg-grid mg-grid--2">
          <FormField
            label="Platform"
            optional
            hint="Must be one of the campaign's platforms"
            error={fieldError(errors, 'platform')}
          >
            <Select
              value={draft.platform}
              options={[{ value: '', label: 'Any platform' }, ...platformOptions]}
              onChange={(e) => set({ platform: e.target.value })}
            />
          </FormField>
          <FormField
            label="Post template"
            optional
            hint={templates.isError ? 'Templates could not be loaded.' : undefined}
            error={fieldError(errors, 'templateId')}
          >
            <Select
              value={draft.templateId}
              options={templateOptions}
              disabled={!templates.data}
              onChange={(e) => set({ templateId: e.target.value })}
            />
          </FormField>
        </div>
      </form>
    </Dialog>
  );
}
