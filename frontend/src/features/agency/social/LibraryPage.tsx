import { useMutation, useQueryClient } from '@tanstack/react-query';
import { Archive, ArchiveRestore, Hash, Image as ImageIcon, Link2, Pencil, Plus, Trash2, Upload } from 'lucide-react';
import { useState } from 'react';
import {
  Alert,
  Badge,
  Button,
  Checkbox,
  ConfirmDialog,
  Dialog,
  EmptyState,
  FileDrop,
  FormField,
  IconButton,
  Input,
  PageHeader,
  Select,
  Tabs,
  Textarea,
  useToast,
} from '@/components/ui';
import { api } from '@/lib/api/client';
import { errorMessage } from '@/lib/api/errors';
import { formatBytes } from '@/lib/format/text';
import {
  socialKeys,
  useHashtagSets,
  useMedia,
  useSnippets,
  useSocialCampaigns,
  type HashtagSet,
  type Media,
  type SocialCampaign,
  type Snippet,
} from './api';
import { CampaignDialog, HashtagSetDialog, MediaEditDialog, SnippetDialog } from './LibraryDialogs';
import { MediaImage } from './PostPreview';
import { ClientPicker, useClientParam } from './shared';
import './social.css';

/** The client's media library, hashtag library, caption snippets and UTM campaigns. */
export function LibraryPage() {
  const [clientId, setClientId] = useClientParam();
  return (
    <>
      <PageHeader title="Library" description="Media, hashtag sets, caption snippets and campaign UTM settings, per client." />
      <div className="sm-toolbar">
        <ClientPicker value={clientId} onChange={setClientId} />
      </div>
      {!clientId ? (
        <EmptyState icon={<ImageIcon />} title="Choose a client" />
      ) : (
        <Tabs
          label="Library sections"
          tabs={[
            { id: 'media', label: 'Media', content: <MediaTab clientId={clientId} /> },
            { id: 'hashtags', label: 'Hashtag sets', content: <HashtagTab clientId={clientId} /> },
            { id: 'snippets', label: 'Caption snippets', content: <SnippetTab clientId={clientId} /> },
            { id: 'campaigns', label: 'Campaigns & UTM', content: <CampaignTab clientId={clientId} /> },
          ]}
        />
      )}
    </>
  );
}

function MediaTab({ clientId }: { clientId: string }) {
  const [search, setSearch] = useState('');
  const media = useMedia(clientId, search || undefined);
  const [adding, setAdding] = useState<'upload' | 'url' | null>(null);
  const [editing, setEditing] = useState<Media | null>(null);
  const [deleting, setDeleting] = useState<Media | null>(null);
  const queryClient = useQueryClient();
  const refresh = () => void queryClient.invalidateQueries({ queryKey: ['social', 'media', clientId] });
  return (
    <div className="stack">
      <div className="cluster">
        <Button leadingIcon={<Upload />} onClick={() => setAdding('upload')}>
          Upload image
        </Button>
        <Button variant="secondary" leadingIcon={<Link2 />} onClick={() => setAdding('url')}>
          Add image or video by URL
        </Button>
        <FormField label="Search media" hideLabel>
          <Input placeholder="Search media" value={search} onChange={(e) => setSearch(e.target.value)} />
        </FormField>
      </div>
      {(media.data?.items.length ?? 0) === 0 ? (
        <EmptyState compact icon={<ImageIcon />} headingLevel={3} title="No media yet" />
      ) : (
        <ul className="sm-media-grid" style={{ listStyle: 'none', padding: 0, margin: 0 }}>
          {media.data!.items.map((m) => (
            <li key={m.id} className="sm-media-card">
              <MediaImage media={m} alt={m.altText || m.title} />
              <div className="sm-media-card__body">
                <strong>{m.title}</strong>
                <span className="sm-muted">
                  {m.kind}
                  {m.width && m.height ? ` · ${m.width}×${m.height}` : ''}
                  {m.durationSeconds ? ` · ${m.durationSeconds}s` : ''}
                  {m.sizeBytes ? ` · ${formatBytes(m.sizeBytes)}` : ''}
                </span>
                <span className="cluster">
                  {m.isPublic ? <Badge tone="info">Public URL</Badge> : <Badge>Private</Badge>}
                  {!m.altText && m.kind === 'Image' && <Badge tone="warning">No alt text</Badge>}
                </span>
                <span className="cluster">
                  <IconButton size="sm" label={`Edit ${m.title}`} icon={<Pencil />} onClick={() => setEditing(m)} />
                  <IconButton size="sm" label={`Delete ${m.title}`} icon={<Trash2 />} onClick={() => setDeleting(m)} />
                </span>
              </div>
            </li>
          ))}
        </ul>
      )}
      {adding === 'upload' && <UploadDialog clientId={clientId} onClose={() => setAdding(null)} />}
      {adding === 'url' && <UrlDialog clientId={clientId} onClose={() => setAdding(null)} />}
      {editing && <MediaEditDialog media={editing} onClose={() => setEditing(null)} onSaved={refresh} />}
      <ConfirmDialog
        open={deleting !== null}
        onClose={() => setDeleting(null)}
        tone="danger"
        title="Delete this media item?"
        description="Posts that still use it will fail validation until you choose other media. Published posts are not affected."
        confirmLabel="Delete"
        onConfirm={async () => {
          if (!deleting) return;
          await api.delete(`/agency/social/media/${deleting.id}`);
          refresh();
        }}
      />
    </div>
  );
}

function UploadDialog({ clientId, onClose }: { clientId: string; onClose: () => void }) {
  const [file, setFile] = useState<File | null>(null);
  const [title, setTitle] = useState('');
  const [alt, setAlt] = useState('');
  const [isPublic, setIsPublic] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const queryClient = useQueryClient();
  const upload = useMutation({
    mutationFn: () => {
      const form = new FormData();
      form.append('file', file!);
      form.append('title', title);
      form.append('altText', alt);
      form.append('isPublic', String(isPublic));
      return api.upload<Media>(`/agency/social/clients/${clientId}/media/upload`, form);
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['social', 'media', clientId] });
      onClose();
    },
    onError: (e) => setError(errorMessage(e)),
  });
  return (
    <Dialog
      open
      onClose={onClose}
      title="Upload image"
      description="PNG, JPEG or WebP up to 10 MB; metadata (GPS, EXIF) is removed."
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button loading={upload.isPending} disabled={!file} onClick={() => upload.mutate()}>
            Upload
          </Button>
        </>
      }
    >
      <div className="stack">
        {error && <Alert tone="danger">{error}</Alert>}
        <FileDrop label="Image" value={file} onChange={setFile} />
        <FormField label="Title" optional>
          <Input value={title} onChange={(e) => setTitle(e.target.value)} />
        </FormField>
        <FormField label="Alt text" hint="Describe the image for screen-reader users.">
          <Textarea rows={2} value={alt} onChange={(e) => setAlt(e.target.value)} />
        </FormField>
        <Checkbox
          label="Public URL"
          description="Required for Instagram/Facebook API publishing, which fetch media by URL."
          checked={isPublic}
          onChange={(e) => setIsPublic(e.target.checked)}
        />
      </div>
    </Dialog>
  );
}

function UrlDialog({ clientId, onClose }: { clientId: string; onClose: () => void }) {
  const [kind, setKind] = useState<'Image' | 'Video'>('Image');
  const [url, setUrl] = useState('');
  const [title, setTitle] = useState('');
  const [width, setWidth] = useState('');
  const [height, setHeight] = useState('');
  const [duration, setDuration] = useState('');
  const [alt, setAlt] = useState('');
  const [error, setError] = useState<string | null>(null);
  const queryClient = useQueryClient();
  const add = useMutation({
    mutationFn: () =>
      api.post<Media>(`/agency/social/clients/${clientId}/media/url`, {
        kind,
        url,
        title,
        width: width ? Number(width) : null,
        height: height ? Number(height) : null,
        durationSeconds: duration ? Number(duration) : null,
        altText: alt || null,
      }),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['social', 'media', clientId] });
      onClose();
    },
    onError: (e) => setError(errorMessage(e)),
  });
  return (
    <Dialog
      open
      onClose={onClose}
      title="Add media by URL"
      description="An https URL on your CDN or asset manager. Dimensions and duration let us check each network's limits."
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button loading={add.isPending} disabled={!url || !title} onClick={() => add.mutate()}>
            Add
          </Button>
        </>
      }
    >
      <div className="stack">
        {error && <Alert tone="danger">{error}</Alert>}
        <FormField label="Type">
          <Select value={kind} onChange={(e) => setKind(e.target.value as 'Image' | 'Video')} options={[{ value: 'Image', label: 'Image' }, { value: 'Video', label: 'Video' }]} />
        </FormField>
        <FormField label="URL" required>
          <Input type="url" value={url} onChange={(e) => setUrl(e.target.value)} />
        </FormField>
        <FormField label="Title" required>
          <Input value={title} onChange={(e) => setTitle(e.target.value)} />
        </FormField>
        <div className="sm-grid-stats">
          <FormField label="Width (px)" optional>
            <Input type="number" value={width} onChange={(e) => setWidth(e.target.value)} />
          </FormField>
          <FormField label="Height (px)" optional>
            <Input type="number" value={height} onChange={(e) => setHeight(e.target.value)} />
          </FormField>
          {kind === 'Video' && (
            <FormField label="Duration (s)" optional>
              <Input type="number" value={duration} onChange={(e) => setDuration(e.target.value)} />
            </FormField>
          )}
        </div>
        {kind === 'Image' && (
          <FormField label="Alt text" optional>
            <Input value={alt} onChange={(e) => setAlt(e.target.value)} />
          </FormField>
        )}
      </div>
    </Dialog>
  );
}

function HashtagTab({ clientId }: { clientId: string }) {
  const sets = useHashtagSets(clientId);
  const [name, setName] = useState('');
  const [tags, setTags] = useState('');
  const queryClient = useQueryClient();
  const toast = useToast();
  const create = useMutation({
    mutationFn: () =>
      api.post<HashtagSet>(`/agency/social/clients/${clientId}/hashtag-sets`, { name, hashtags: tags.split(/[\s,]+/).filter(Boolean) }),
    onSuccess: () => {
      setName('');
      setTags('');
      void queryClient.invalidateQueries({ queryKey: socialKeys.hashtags(clientId) });
    },
    onError: (e) => toast.error('Not saved', errorMessage(e)),
  });
  const [editing, setEditing] = useState<HashtagSet | null>(null);
  const [deleting, setDeleting] = useState<HashtagSet | null>(null);
  const refresh = () => void queryClient.invalidateQueries({ queryKey: socialKeys.hashtags(clientId) });
  return (
    <div className="stack">
      <ul className="stack" style={{ listStyle: 'none', padding: 0 }}>
        {(sets.data ?? []).map((s) => (
          <li key={s.id} className="cluster">
            <strong>{s.name}:</strong> <span>{s.hashtags.join(' ')}</span>
            <IconButton size="sm" label={`Edit ${s.name}`} icon={<Pencil />} onClick={() => setEditing(s)} />
            <IconButton size="sm" label={`Delete ${s.name}`} icon={<Trash2 />} onClick={() => setDeleting(s)} />
          </li>
        ))}
      </ul>
      {(sets.data?.length ?? 0) === 0 && <EmptyState compact icon={<Hash />} headingLevel={3} title="No hashtag sets" />}
      <div className="sm-grid-stats">
        <FormField label="Set name">
          <Input value={name} onChange={(e) => setName(e.target.value)} />
        </FormField>
        <FormField label="Hashtags" hint="Separate with spaces">
          <Input value={tags} onChange={(e) => setTags(e.target.value)} />
        </FormField>
      </div>
      <div>
        <Button leadingIcon={<Plus />} disabled={!name || !tags} loading={create.isPending} onClick={() => create.mutate()}>
          Add hashtag set
        </Button>
      </div>
      {editing && <HashtagSetDialog set={editing} onClose={() => setEditing(null)} onSaved={refresh} />}
      <ConfirmDialog
        open={deleting !== null}
        onClose={() => setDeleting(null)}
        tone="danger"
        title="Delete this hashtag set?"
        description={deleting ? `“${deleting.name}” is removed from the library. Posts keep the hashtags they already use.` : undefined}
        confirmLabel="Delete"
        onConfirm={async () => {
          if (!deleting) return;
          await api.delete(`/agency/social/hashtag-sets/${deleting.id}`);
          refresh();
        }}
      />
    </div>
  );
}

function SnippetTab({ clientId }: { clientId: string }) {
  const snippets = useSnippets(clientId);
  const [name, setName] = useState('');
  const [body, setBody] = useState('');
  const queryClient = useQueryClient();
  const create = useMutation({
    mutationFn: () => api.post<Snippet>(`/agency/social/clients/${clientId}/snippets`, { name, body }),
    onSuccess: () => {
      setName('');
      setBody('');
      void queryClient.invalidateQueries({ queryKey: socialKeys.snippets(clientId) });
    },
  });
  const [editing, setEditing] = useState<Snippet | null>(null);
  const [deleting, setDeleting] = useState<Snippet | null>(null);
  const refresh = () => void queryClient.invalidateQueries({ queryKey: socialKeys.snippets(clientId) });
  return (
    <div className="stack">
      <ul className="stack" style={{ listStyle: 'none', padding: 0 }}>
        {(snippets.data ?? []).map((s) => (
          <li key={s.id} className="stack" style={{ gap: 2 }}>
            <span className="cluster">
              <strong>{s.name}</strong>
              <IconButton size="sm" label={`Edit ${s.name}`} icon={<Pencil />} onClick={() => setEditing(s)} />
              <IconButton size="sm" label={`Delete ${s.name}`} icon={<Trash2 />} onClick={() => setDeleting(s)} />
            </span>
            <p className="sm-pre sm-muted">{s.body}</p>
          </li>
        ))}
      </ul>
      <FormField label="Snippet name">
        <Input value={name} onChange={(e) => setName(e.target.value)} />
      </FormField>
      <FormField label="Text">
        <Textarea rows={3} value={body} onChange={(e) => setBody(e.target.value)} />
      </FormField>
      <div>
        <Button leadingIcon={<Plus />} disabled={!name || !body} loading={create.isPending} onClick={() => create.mutate()}>
          Add snippet
        </Button>
      </div>
      {editing && <SnippetDialog snippet={editing} onClose={() => setEditing(null)} onSaved={refresh} />}
      <ConfirmDialog
        open={deleting !== null}
        onClose={() => setDeleting(null)}
        tone="danger"
        title="Delete this snippet?"
        description={deleting ? `“${deleting.name}” is removed from the library.` : undefined}
        confirmLabel="Delete"
        onConfirm={async () => {
          if (!deleting) return;
          await api.delete(`/agency/social/snippets/${deleting.id}`);
          refresh();
        }}
      />
    </div>
  );
}

function CampaignTab({ clientId }: { clientId: string }) {
  const [showArchived, setShowArchived] = useState(false);
  const campaigns = useSocialCampaigns(clientId, showArchived);
  const [editing, setEditing] = useState<SocialCampaign | 'new' | null>(null);
  const [deleting, setDeleting] = useState<SocialCampaign | null>(null);
  const queryClient = useQueryClient();
  const toast = useToast();
  const refresh = () => void queryClient.invalidateQueries({ queryKey: socialKeys.campaigns(clientId) });
  const archive = useMutation({
    mutationFn: (c: SocialCampaign) =>
      api.post<SocialCampaign>(`/agency/social/campaigns/${c.id}/${c.isArchived ? 'restore' : 'archive'}`, { concurrencyStamp: c.concurrencyStamp }),
    onSuccess: (c) => {
      toast.success(c.isArchived ? 'Campaign archived' : 'Campaign restored', c.name);
      refresh();
    },
    onError: (e) => toast.error('Not changed', errorMessage(e)),
  });
  const rows = campaigns.data ?? [];
  return (
    <div className="stack">
      <p className="sm-muted">When a post has “Append UTM parameters” on, links get utm_source = network, utm_medium and utm_campaign from its campaign.</p>
      <div className="cluster">
        <Button leadingIcon={<Plus />} onClick={() => setEditing('new')}>
          New campaign
        </Button>
        <Checkbox label="Show archived campaigns" checked={showArchived} onChange={(e) => setShowArchived(e.target.checked)} />
      </div>
      {rows.length === 0 ? (
        <EmptyState compact headingLevel={3} title="No campaigns" description="Group posts by campaign to tag their links with UTM parameters." />
      ) : (
        <ul className="stack" style={{ listStyle: 'none', padding: 0 }}>
          {rows.map((c) => (
            <li key={c.id} className="cluster">
              <span>
                <strong>{c.name}</strong> — utm_campaign={c.utmCampaign}
                {c.utmMedium ? `, utm_medium=${c.utmMedium}` : ''}
                {c.utmSource ? `, utm_source=${c.utmSource}` : ''}
              </span>
              {c.isArchived && <Badge>Archived</Badge>}
              <IconButton size="sm" label={`Edit ${c.name}`} icon={<Pencil />} onClick={() => setEditing(c)} />
              <IconButton
                size="sm"
                label={c.isArchived ? `Restore ${c.name}` : `Archive ${c.name}`}
                icon={c.isArchived ? <ArchiveRestore /> : <Archive />}
                onClick={() => archive.mutate(c)}
              />
              <IconButton size="sm" label={`Delete ${c.name}`} icon={<Trash2 />} onClick={() => setDeleting(c)} />
            </li>
          ))}
        </ul>
      )}
      {editing && <CampaignDialog clientId={clientId} campaign={editing === 'new' ? null : editing} onClose={() => setEditing(null)} onSaved={refresh} />}
      <ConfirmDialog
        open={deleting !== null}
        onClose={() => setDeleting(null)}
        tone="danger"
        title="Delete this campaign?"
        description="Only campaigns no post uses can be deleted; archive a used campaign instead so its posts keep their UTM values."
        confirmLabel="Delete"
        onConfirm={async () => {
          if (!deleting) return;
          await api.delete(`/agency/social/campaigns/${deleting.id}`);
          refresh();
        }}
      />
    </div>
  );
}
