import { useMutation } from '@tanstack/react-query';
import { useState, type FormEvent } from 'react';
import { Alert, Button, Dialog, FormField, Input, Textarea } from '@/components/ui';
import { api } from '@/lib/api/client';
import { errorMessage, isApiError } from '@/lib/api/errors';
import type { HashtagSet, Media, SocialCampaign, Snippet } from './api';

function fieldError(error: unknown, field: string): string | undefined {
  if (!isApiError(error)) return undefined;
  const entry = Object.entries(error.errors ?? {}).find(([k]) => k.toLowerCase() === field.toLowerCase());
  return entry?.[1][0];
}

function DialogShell({
  title,
  description,
  formId,
  saving,
  error,
  onClose,
  onSubmit,
  children,
}: {
  title: string;
  description?: string;
  formId: string;
  saving: boolean;
  error: unknown;
  onClose: () => void;
  onSubmit: () => void;
  children: React.ReactNode;
}) {
  return (
    <Dialog
      open
      onClose={onClose}
      title={title}
      description={description}
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
      <form
        id={formId}
        className="stack"
        onSubmit={(e: FormEvent) => {
          e.preventDefault();
          onSubmit();
        }}
      >
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

/** Edits a media item's title, alt text and tags (the file itself is immutable; upload a new one to replace it). */
export function MediaEditDialog({
  media,
  onClose,
  onSaved,
}: {
  media: Media;
  onClose: () => void;
  onSaved: () => void;
}) {
  const [form, setForm] = useState({
    title: media.title,
    altText: media.altText ?? '',
    tags: media.tags.join(', '),
  });
  const save = useMutation({
    mutationFn: () =>
      api.put<Media>(`/agency/social/media/${media.id}`, {
        title: form.title,
        altText: form.altText || null,
        tags: form.tags
          .split(',')
          .map((t) => t.trim())
          .filter(Boolean),
        durationSeconds: media.durationSeconds,
      }),
    onSuccess: () => {
      onSaved();
      onClose();
    },
  });
  return (
    <DialogShell
      title="Edit media"
      description="The file itself cannot change; upload a new one to replace it."
      formId="sm-media-edit"
      saving={save.isPending}
      error={save.error}
      onClose={onClose}
      onSubmit={() => save.mutate()}
    >
      <FormField label="Title" required error={fieldError(save.error, 'title')}>
        <Input
          value={form.title}
          maxLength={200}
          onChange={(e) => setForm({ ...form, title: e.target.value })}
        />
      </FormField>
      {media.kind === 'Image' && (
        <FormField
          label="Alt text"
          hint="Describe the image for screen-reader users."
          error={fieldError(save.error, 'altText')}
        >
          <Textarea
            rows={2}
            maxLength={1000}
            value={form.altText}
            onChange={(e) => setForm({ ...form, altText: e.target.value })}
          />
        </FormField>
      )}
      <FormField label="Tags" optional hint="Comma-separated">
        <Input value={form.tags} onChange={(e) => setForm({ ...form, tags: e.target.value })} />
      </FormField>
    </DialogShell>
  );
}

export function HashtagSetDialog({
  set,
  onClose,
  onSaved,
}: {
  set: HashtagSet;
  onClose: () => void;
  onSaved: () => void;
}) {
  const [form, setForm] = useState({ name: set.name, hashtags: set.hashtags.join(' ') });
  const save = useMutation({
    mutationFn: () =>
      api.put<HashtagSet>(`/agency/social/hashtag-sets/${set.id}`, {
        name: form.name,
        hashtags: form.hashtags.split(/[\s,]+/).filter(Boolean),
        concurrencyStamp: set.concurrencyStamp,
      }),
    onSuccess: () => {
      onSaved();
      onClose();
    },
  });
  return (
    <DialogShell
      title="Edit hashtag set"
      formId="sm-hashtags-edit"
      saving={save.isPending}
      error={save.error}
      onClose={onClose}
      onSubmit={() => save.mutate()}
    >
      <FormField label="Set name" required error={fieldError(save.error, 'name')}>
        <Input
          value={form.name}
          maxLength={150}
          onChange={(e) => setForm({ ...form, name: e.target.value })}
        />
      </FormField>
      <FormField
        label="Hashtags"
        required
        hint="Separate with spaces"
        error={fieldError(save.error, 'hashtags')}
      >
        <Textarea
          rows={3}
          value={form.hashtags}
          onChange={(e) => setForm({ ...form, hashtags: e.target.value })}
        />
      </FormField>
    </DialogShell>
  );
}

export function SnippetDialog({
  snippet,
  onClose,
  onSaved,
}: {
  snippet: Snippet;
  onClose: () => void;
  onSaved: () => void;
}) {
  const [form, setForm] = useState({ name: snippet.name, body: snippet.body });
  const save = useMutation({
    mutationFn: () =>
      api.put<Snippet>(`/agency/social/snippets/${snippet.id}`, {
        ...form,
        concurrencyStamp: snippet.concurrencyStamp,
      }),
    onSuccess: () => {
      onSaved();
      onClose();
    },
  });
  return (
    <DialogShell
      title="Edit caption snippet"
      formId="sm-snippet-edit"
      saving={save.isPending}
      error={save.error}
      onClose={onClose}
      onSubmit={() => save.mutate()}
    >
      <FormField label="Snippet name" required error={fieldError(save.error, 'name')}>
        <Input
          value={form.name}
          maxLength={150}
          onChange={(e) => setForm({ ...form, name: e.target.value })}
        />
      </FormField>
      <FormField label="Text" required error={fieldError(save.error, 'body')}>
        <Textarea
          rows={5}
          maxLength={4000}
          value={form.body}
          onChange={(e) => setForm({ ...form, body: e.target.value })}
        />
      </FormField>
    </DialogShell>
  );
}

/** Creates or edits a UTM campaign. */
export function CampaignDialog({
  clientId,
  campaign,
  onClose,
  onSaved,
}: {
  clientId: string;
  campaign: SocialCampaign | null;
  onClose: () => void;
  onSaved: () => void;
}) {
  const [form, setForm] = useState({
    name: campaign?.name ?? '',
    utmCampaign: campaign?.utmCampaign ?? '',
    utmSource: campaign?.utmSource ?? '',
    utmMedium: campaign?.utmMedium ?? '',
    utmContent: campaign?.utmContent ?? '',
    utmTerm: campaign?.utmTerm ?? '',
  });
  const save = useMutation({
    mutationFn: () => {
      const body = {
        name: form.name,
        utmCampaign: form.utmCampaign,
        utmSource: form.utmSource || null,
        utmMedium: form.utmMedium || null,
        utmContent: form.utmContent || null,
        utmTerm: form.utmTerm || null,
        concurrencyStamp: campaign?.concurrencyStamp,
      };
      return campaign
        ? api.put<SocialCampaign>(`/agency/social/campaigns/${campaign.id}`, body)
        : api.post<SocialCampaign>(`/agency/social/clients/${clientId}/campaigns`, body);
    },
    onSuccess: () => {
      onSaved();
      onClose();
    },
  });
  return (
    <DialogShell
      title={campaign ? 'Edit campaign' : 'New campaign'}
      description="Posts with “Append UTM parameters” on get these values; utm_source defaults to the network."
      formId="sm-campaign"
      saving={save.isPending}
      error={save.error}
      onClose={onClose}
      onSubmit={() => save.mutate()}
    >
      <FormField label="Campaign name" required error={fieldError(save.error, 'name')}>
        <Input
          value={form.name}
          maxLength={200}
          onChange={(e) => setForm({ ...form, name: e.target.value })}
        />
      </FormField>
      <FormField label="utm_campaign" required error={fieldError(save.error, 'utmCampaign')}>
        <Input
          value={form.utmCampaign}
          maxLength={150}
          onChange={(e) => setForm({ ...form, utmCampaign: e.target.value })}
        />
      </FormField>
      <div className="sm-grid-stats">
        <FormField label="utm_source" optional hint="Empty = the network">
          <Input
            value={form.utmSource}
            maxLength={100}
            onChange={(e) => setForm({ ...form, utmSource: e.target.value })}
          />
        </FormField>
        <FormField label="utm_medium" optional hint="Empty = the client default">
          <Input
            value={form.utmMedium}
            maxLength={100}
            onChange={(e) => setForm({ ...form, utmMedium: e.target.value })}
          />
        </FormField>
        <FormField label="utm_content" optional>
          <Input
            value={form.utmContent}
            maxLength={150}
            onChange={(e) => setForm({ ...form, utmContent: e.target.value })}
          />
        </FormField>
        <FormField label="utm_term" optional>
          <Input
            value={form.utmTerm}
            maxLength={150}
            onChange={(e) => setForm({ ...form, utmTerm: e.target.value })}
          />
        </FormField>
      </div>
    </DialogShell>
  );
}
