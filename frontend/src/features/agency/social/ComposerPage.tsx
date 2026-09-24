import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { CheckCircle2, Copy, CopyPlus, RotateCcw, Send, Trash2 } from 'lucide-react';
import { useEffect, useMemo, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import {
  Alert,
  Button,
  Card,
  CardBody,
  CardHeader,
  Checkbox,
  ConfirmDialog,
  DataTable,
  DateTime,
  Dialog,
  ErrorState,
  FormField,
  Input,
  PageHeader,
  Select,
  Skeleton,
  Switch,
  Tabs,
  Textarea,
  Timeline,
  useToast,
  type DataTableColumn,
  type Tone,
} from '@/components/ui';
import { SafeExternalLink } from '@/components/SafeExternalLink';
import { api } from '@/lib/api/client';
import { errorMessage, isApiError } from '@/lib/api/errors';
import { useAuth } from '@/lib/auth/useAuth';
import { Permissions } from '@/lib/auth/permissions';
import { useDebouncedValue } from '@/lib/hooks/useDebouncedValue';
import {
  NETWORK_LABELS,
  socialKeys,
  useHashtagSets,
  useMedia,
  usePresets,
  useProfiles,
  useSnippets,
  useSocialCampaigns,
  type Media,
  type Post,
  type PostInput,
  type Preset,
  type Profile,
  type SocialNetwork,
  type VariantInput,
  type VariantValidation,
} from './api';
import { countFor } from './counters';
import { MediaImage, PostPreview } from './PostPreview';
import { ClientPicker, NetworkChip, PostStatusBadge, fromLocalInput, toLocalInput, useClientParam } from './shared';
import './social.css';

interface VariantForm {
  text: string;
  title: string;
  mediaIds: string[];
  altTexts: string[];
  link: string;
  firstComment: string;
  hashtags: string;
  mentions: string;
}

const emptyVariant = (): VariantForm => ({
  text: '',
  title: '',
  mediaIds: [],
  altTexts: [],
  link: '',
  firstComment: '',
  hashtags: '',
  mentions: '',
});

const split = (value: string) =>
  value
    .split(/[\s,]+/)
    .map((v) => v.trim())
    .filter(Boolean);

function toInput(profileId: string, v: VariantForm): VariantInput {
  return {
    profileId,
    text: v.text,
    title: v.title || null,
    mediaIds: v.mediaIds,
    altTexts: v.mediaIds.map((_, i) => v.altTexts[i] ?? ''),
    link: v.link || null,
    firstComment: v.firstComment || null,
    hashtags: split(v.hashtags),
    mentions: split(v.mentions),
  };
}

type Action = 'submit' | 'approve' | 'requestChanges' | 'schedule' | 'queue' | 'unschedule' | 'retry' | 'markPublished' | 'delete';

const ACTION_LABELS: Record<Action, string> = {
  submit: 'Submit for review',
  approve: 'Approve',
  requestChanges: 'Request changes',
  schedule: 'Schedule',
  queue: 'Add to queue',
  unschedule: 'Unschedule',
  retry: 'Retry publishing',
  markPublished: 'Mark as published',
  delete: 'Delete',
};

export function ComposerPage() {
  const { id } = useParams();
  const isNew = !id;
  const postQuery = useQuery({
    queryKey: socialKeys.post(id ?? 'new'),
    queryFn: () => api.get<Post>(`/agency/social/posts/${id}`),
    enabled: !isNew,
  });
  if (!isNew && postQuery.isLoading) return <Skeleton height="30rem" />;
  if (!isNew && postQuery.isError) return <ErrorState error={postQuery.error} onRetry={() => void postQuery.refetch()} />;
  return <Composer key={postQuery.data?.concurrencyStamp ?? 'new'} post={postQuery.data ?? null} />;
}

function Composer({ post }: { post: Post | null }) {
  const [clientParam, setClientParam] = useClientParam();
  const clientId = post?.clientAccountId ?? clientParam;
  const { hasPermission } = useAuth();
  const canPublish = hasPermission(Permissions.SocialPublish);
  const toast = useToast();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const presets = usePresets();
  const profiles = useProfiles(clientId);
  const media = useMedia(clientId);
  const hashtagSets = useHashtagSets(clientId);
  const snippets = useSnippets(clientId);
  const campaigns = useSocialCampaigns(clientId);

  const [title, setTitle] = useState(post?.title ?? '');
  const [plannedAt, setPlannedAt] = useState(toLocalInput(post?.scheduledAt));
  const [campaignId, setCampaignId] = useState(post?.campaignId ?? '');
  const [autoUtm, setAutoUtm] = useState(post?.autoAppendUtm ?? false);
  const [evergreen, setEvergreen] = useState(post?.isEvergreen ?? false);
  const [intervalDays, setIntervalDays] = useState(String(post?.evergreenIntervalDays ?? 30));
  const [maxRepeats, setMaxRepeats] = useState(String(post?.evergreenMaxRepeats ?? 3));
  const [selected, setSelected] = useState<string[]>(post?.variants.map((v) => v.profileId) ?? []);
  const [variants, setVariants] = useState<Record<string, VariantForm>>(() =>
    Object.fromEntries(
      (post?.variants ?? []).map((v) => [
        v.profileId,
        {
          text: v.text,
          title: v.title ?? '',
          mediaIds: v.mediaIds,
          altTexts: v.altTexts,
          link: v.link ?? '',
          firstComment: v.firstComment ?? '',
          hashtags: v.hashtags.join(' '),
          mentions: v.mentions.join(' '),
        },
      ]),
    ),
  );
  const [activeTab, setActiveTab] = useState<string | undefined>(selected[0]);
  const [action, setAction] = useState<Action | null>(null);
  const [formError, setFormError] = useState<string | null>(null);

  const profileMap = useMemo(() => new Map((profiles.data ?? []).map((p) => [p.id, p])), [profiles.data]);
  const presetMap = useMemo(() => new Map((presets.data ?? []).map((p) => [p.network, p])), [presets.data]);
  const mediaMap = useMemo(() => new Map((media.data?.items ?? []).map((m) => [m.id, m])), [media.data]);
  const editable = !post || post.allowedActions.includes('edit');

  const body: PostInput | null = clientId
    ? {
        clientAccountId: clientId,
        title: title.trim() || 'Untitled post',
        scheduledAt: fromLocalInput(plannedAt),
        campaignId: campaignId || null,
        autoAppendUtm: autoUtm,
        isEvergreen: evergreen,
        evergreenIntervalDays: Number(intervalDays) || 30,
        evergreenMaxRepeats: Number(maxRepeats) || 3,
        variants: selected.map((pid) => toInput(pid, variants[pid] ?? emptyVariant())),
        concurrencyStamp: post?.concurrencyStamp,
      }
    : null;

  // Server-side validation (authoritative), debounced while typing.
  const debounced = useDebouncedValue(body ? JSON.stringify({ ...body, concurrencyStamp: undefined }) : null, 500);
  const validation = useQuery({
    queryKey: ['social', 'validate', debounced],
    queryFn: async () => {
      const parsed = JSON.parse(debounced!) as PostInput;
      const result = await api.post<{ isValid: boolean; variants: VariantValidation[] }>('/agency/social/validate', {
        clientAccountId: parsed.clientAccountId,
        campaignId: parsed.campaignId,
        autoAppendUtm: parsed.autoAppendUtm,
        variants: parsed.variants,
      });
      return { ...result, profileIds: parsed.variants.map((v) => v.profileId) };
    },
    // Gate on the debounced body, not `selected`: right after the first network is ticked `selected` is non-empty while
    // the debounced body still has no variants, which the API rejects with 400.
    enabled: !!debounced && selected.length > 0 && (JSON.parse(debounced) as PostInput).variants.length > 0,
    retry: false,
  });
  const validationByNetwork = new Map(
    (validation.data?.variants ?? []).map((v, i) => [validation.data!.profileIds[i], v] as const),
  );

  const update = (pid: string, patch: Partial<VariantForm>) =>
    setVariants((all) => ({ ...all, [pid]: { ...(all[pid] ?? emptyVariant()), ...patch } }));

  const toggleProfile = (pid: string, on: boolean) => {
    setSelected((s) => (on ? [...s, pid] : s.filter((x) => x !== pid)));
    if (on) {
      const first = selected[0] ? variants[selected[0]] : undefined;
      setVariants((all) => ({ ...all, [pid]: all[pid] ?? { ...emptyVariant(), text: first?.text ?? '' } }));
      setActiveTab((t) => t ?? pid);
    } else if (activeTab === pid) setActiveTab(selected.find((x) => x !== pid));
  };

  const save = useMutation({
    mutationFn: () =>
      post ? api.put<Post>(`/agency/social/posts/${post.id}`, body) : api.post<Post>('/agency/social/posts', body),
    onSuccess: (saved) => {
      toast.success(post ? 'Post saved' : 'Draft created');
      void queryClient.invalidateQueries({ queryKey: socialKeys.all });
      if (!post) navigate(`/agency/social/posts/${saved.id}`, { replace: true });
    },
    onError: (error) => setFormError(errorMessage(error)),
  });

  const runAction = async (kind: Action, payload?: Record<string, unknown>) => {
    if (!post) return;
    const path = {
      submit: 'submit',
      approve: 'approve',
      requestChanges: 'request-changes',
      schedule: 'schedule',
      queue: 'queue',
      unschedule: 'unschedule',
      retry: 'retry',
      markPublished: 'mark-published',
      delete: '',
    }[kind];
    if (kind === 'delete') {
      await api.delete(`/agency/social/posts/${post.id}`);
      toast.success('Post deleted');
      await queryClient.invalidateQueries({ queryKey: socialKeys.all });
      navigate('/agency/social');
      return;
    }
    await api.post<Post>(`/agency/social/posts/${post.id}/${path}`, { concurrencyStamp: post.concurrencyStamp, ...payload });
    toast.success(`${ACTION_LABELS[kind]}: done`);
    await queryClient.invalidateQueries({ queryKey: socialKeys.all });
  };

  const tabs = selected
    .map((pid) => profileMap.get(pid))
    .filter((p): p is Profile => !!p)
    .map((p) => {
      const v = variants[p.id] ?? emptyVariant();
      const preset = presetMap.get(p.network);
      const counter = preset ? countFor(preset, v.text, split(v.hashtags), v.link) : null;
      const server = validationByNetwork.get(p.id);
      const errors = server?.issues.filter((i) => i.severity === 'Error').length ?? 0;
      return {
        id: p.id,
        label: `${NETWORK_LABELS[p.network]} @${p.handle}`,
        badge: errors > 0 ? `${errors} error${errors > 1 ? 's' : ''}` : undefined,
        content: (
          <VariantEditor
            profile={p}
            form={v}
            preset={preset}
            counterText={counter ? `${counter.length.toLocaleString()} / ${counter.max.toLocaleString()}` : ''}
            over={counter?.over ?? false}
            validation={server}
            media={media.data?.items ?? []}
            hashtagSets={hashtagSets.data ?? []}
            snippets={snippets.data ?? []}
            disabled={!editable}
            onChange={(patch) => update(p.id, patch)}
            onCopyToAll={() =>
              setVariants((all) => Object.fromEntries(selected.map((pid) => [pid, { ...(all[pid] ?? emptyVariant()), text: v.text }])))
            }
          />
        ),
      };
    });

  const active = activeTab && profileMap.get(activeTab);
  const activeForm = activeTab ? variants[activeTab] : undefined;
  const allowed = post?.allowedActions ?? [];
  const duplicate = useMutation({
    mutationFn: () => api.post<{ id: string; title: string }>(`/agency/social/posts/${post!.id}/duplicate`),
    onSuccess: (copy) => {
      toast.success('Post duplicated', `“${copy.title}” is a new draft.`);
      void queryClient.invalidateQueries({ queryKey: socialKeys.all });
      navigate(`/agency/social/posts/${copy.id}`);
    },
    onError: (e) => toast.error('Could not duplicate the post', errorMessage(e)),
  });
  const primaryActions = (['submit', 'approve', 'schedule', 'queue', 'unschedule', 'retry', 'markPublished', 'requestChanges'] as Action[]).filter(
    (a) => allowed.includes(a),
  );

  return (
    <>
      <PageHeader
        title={post ? post.title : 'New post'}
        eyebrow={post ? post.clientName : 'Composer'}
        breadcrumbs={[{ label: 'Social', to: '/agency/social' }, { label: post ? 'Post' : 'Compose' }]}
        meta={post ? <PostStatusBadge status={post.status} /> : undefined}
        actions={
          <div className="cluster">
            {editable && (
              <Button onClick={() => { setFormError(null); save.mutate(); }} loading={save.isPending} disabled={!clientId || selected.length === 0}>
                {post ? 'Save changes' : 'Save draft'}
              </Button>
            )}
            {primaryActions.map((a) => (
              <Button key={a} variant={a === 'approve' || a === 'schedule' ? 'highlight' : 'secondary'} onClick={() => setAction(a)}>
                {ACTION_LABELS[a]}
              </Button>
            ))}
            {post && (
              <Button variant="ghost" leadingIcon={<CopyPlus />} loading={duplicate.isPending} onClick={() => duplicate.mutate()}>
                Duplicate
              </Button>
            )}
            {allowed.includes('delete') ? (
              <Button variant="ghost" onClick={() => setAction('delete')}>
                Delete
              </Button>
            ) : (
              post && (
                <Button variant="ghost" disabled title="Published posts are kept for reporting and cannot be deleted.">
                  Delete
                </Button>
              )
            )}
          </div>
        }
      />

      {formError && (
        <Alert tone="danger" title="Could not save" onDismiss={() => setFormError(null)}>
          {formError}
        </Alert>
      )}
      {post?.status === 'Failed' && post.failureReason && (
        <Alert tone="danger" title="Publishing failed">
          {post.failureReason}
        </Alert>
      )}
      {post && !editable && (
        <Alert tone="info" title="This post is locked">
          Published content cannot be edited.
        </Alert>
      )}
      {post && post.status !== 'Draft' && editable && (
        <Alert tone="warning" title="Editing resets approval">
          Saving content changes sends the post back to Draft so it is reviewed again.
        </Alert>
      )}

      <div className="sm-grid-2">
        <div className="stack">
          <Card as="section" aria-labelledby="sm-compose-setup">
            <CardHeader title="Post" titleId="sm-compose-setup" />
            <CardBody className="stack">
              {!post && <ClientPicker value={clientParam} onChange={setClientParam} />}
              <FormField label="Internal title" required>
                <Input value={title} onChange={(e) => setTitle(e.target.value)} maxLength={200} disabled={!editable} />
              </FormField>
              {clientId && (
                <fieldset className="stack" style={{ border: 0, padding: 0, margin: 0 }}>
                  <legend className="sm-h3">Networks</legend>
                  {profiles.isLoading && <Skeleton height="3rem" />}
                  {(profiles.data ?? []).length === 0 && !profiles.isLoading && (
                    <p className="sm-muted">This client has no brand profiles yet. Add them under Profiles & connections.</p>
                  )}
                  <div className="cluster">
                    {(profiles.data ?? []).map((p) => (
                      <Checkbox
                        key={p.id}
                        label={`${NETWORK_LABELS[p.network]} · @${p.handle}`}
                        checked={selected.includes(p.id)}
                        disabled={!editable}
                        onChange={(e) => toggleProfile(p.id, e.target.checked)}
                      />
                    ))}
                  </div>
                </fieldset>
              )}
              <div className="sm-grid-stats">
                <FormField label="Planned date and time" hint="Your local time">
                  <Input type="datetime-local" value={plannedAt} onChange={(e) => setPlannedAt(e.target.value)} disabled={!editable} />
                </FormField>
                <FormField label="Campaign" optional>
                  <Select
                    value={campaignId}
                    onChange={(e) => setCampaignId(e.target.value)}
                    placeholder="No campaign"
                    options={(campaigns.data ?? []).map((c) => ({ value: c.id, label: c.name }))}
                    disabled={!editable}
                  />
                </FormField>
              </div>
              <Switch
                checked={autoUtm}
                onCheckedChange={setAutoUtm}
                disabled={!editable}
                label="Append UTM parameters to links"
                description="Uses the campaign's UTM settings; utm_source is the network."
              />
              <Switch
                checked={evergreen}
                onCheckedChange={setEvergreen}
                disabled={!editable}
                label="Evergreen"
                description="Re-queue this post after it is published."
              />
              {evergreen && (
                <div className="sm-grid-stats">
                  <FormField label="Repeat every (days)">
                    <Input type="number" min={1} max={365} value={intervalDays} onChange={(e) => setIntervalDays(e.target.value)} disabled={!editable} />
                  </FormField>
                  <FormField label="Maximum repeats">
                    <Input type="number" min={1} max={52} value={maxRepeats} onChange={(e) => setMaxRepeats(e.target.value)} disabled={!editable} />
                  </FormField>
                </div>
              )}
            </CardBody>
          </Card>

          {tabs.length > 0 && (
            <Card as="section" aria-labelledby="sm-compose-variants">
              <CardHeader title="Content per network" titleId="sm-compose-variants" />
              <CardBody>
                <Tabs label="Network variants" tabs={tabs} value={activeTab} onValueChange={setActiveTab} />
              </CardBody>
            </Card>
          )}

          {post && <Discussion post={post} />}
        </div>

        <div className="stack">
          <Card as="section" aria-labelledby="sm-compose-preview">
            <CardHeader title="Preview" titleId="sm-compose-preview" description="Layout mock-up; the network renders the final post." />
            <CardBody className="stack">
              {active && activeForm ? (
                <PostPreview
                  network={active.network}
                  displayName={active.displayName}
                  handle={active.handle}
                  text={activeForm.text}
                  title={activeForm.title}
                  hashtags={split(activeForm.hashtags)}
                  link={activeForm.link}
                  media={activeForm.mediaIds.map((m) => mediaMap.get(m)).filter((m): m is Media => !!m)}
                  altTexts={activeForm.altTexts}
                  preset={presetMap.get(active.network)}
                />
              ) : (
                <p className="sm-muted">Choose at least one network to see a preview.</p>
              )}
              {validation.data && (
                <Alert tone={validation.data.isValid ? 'success' : 'danger'} title={validation.data.isValid ? 'Ready for review' : 'Fix before submitting'}>
                  {validation.data.isValid
                    ? 'Every network variant passes its limits.'
                    : 'Some variants break a network limit (see each tab).'}
                </Alert>
              )}
            </CardBody>
          </Card>
          {post && <PublishingState post={post} />}
        </div>
      </div>

      <ActionDialogs post={post} action={action} onClose={() => setAction(null)} run={runAction} canPublish={canPublish} />
    </>
  );
}

function VariantEditor({
  profile,
  form,
  preset,
  counterText,
  over,
  validation,
  media,
  hashtagSets,
  snippets,
  disabled,
  onChange,
  onCopyToAll,
}: {
  profile: Profile;
  form: VariantForm;
  preset: Preset | undefined;
  counterText: string;
  over: boolean;
  validation: VariantValidation | undefined;
  media: Media[];
  hashtagSets: { id: string; name: string; hashtags: string[] }[];
  snippets: { id: string; name: string; body: string }[];
  disabled: boolean;
  onChange: (patch: Partial<VariantForm>) => void;
  onCopyToAll: () => void;
}) {
  const textId = `sm-text-${profile.id}`;
  const counterId = `sm-counter-${profile.id}`;
  return (
    <div className="sm-variant">
      <FormField
        id={textId}
        label={`${NETWORK_LABELS[profile.network]} text`}
        labelAside={
          <span id={counterId} className={`sm-counter ${over ? 'sm-counter--over' : ''}`}>
            {counterText} characters{over ? ' (over the limit)' : ''}
          </span>
        }
      >
        <Textarea rows={6} value={form.text} onChange={(e) => onChange({ text: e.target.value })} disabled={disabled} aria-describedby={counterId} />
      </FormField>
      {/* The count is read with the field (aria-describedby); only crossing the limit is announced, not every keystroke. */}
      <span className="visually-hidden" role="status">
        {over ? `${NETWORK_LABELS[profile.network]} text is over the character limit.` : ''}
      </span>
      <div className="cluster">
        <Button size="sm" variant="secondary" leadingIcon={<Copy />} onClick={onCopyToAll} disabled={disabled}>
          Use this text for all networks
        </Button>
        {snippets.length > 0 && (
          <Select
            aria-label="Insert a caption snippet"
            size="sm"
            value=""
            placeholder="Insert snippet…"
            options={snippets.map((s) => ({ value: s.id, label: s.name }))}
            onChange={(e) => {
              const s = snippets.find((x) => x.id === e.target.value);
              if (s) onChange({ text: form.text ? `${form.text}\n\n${s.body}` : s.body });
            }}
            disabled={disabled}
          />
        )}
        {hashtagSets.length > 0 && (
          <Select
            aria-label="Insert a hashtag set"
            size="sm"
            value=""
            placeholder="Insert hashtags…"
            options={hashtagSets.map((s) => ({ value: s.id, label: s.name }))}
            onChange={(e) => {
              const s = hashtagSets.find((x) => x.id === e.target.value);
              if (s) onChange({ hashtags: [form.hashtags, ...s.hashtags].filter(Boolean).join(' ') });
            }}
            disabled={disabled}
          />
        )}
      </div>
      {preset?.maxTitleLength != null && (
        <FormField label="Title" required={preset.requiresTitle} hint={`Up to ${preset.maxTitleLength} characters`}>
          <Input value={form.title} onChange={(e) => onChange({ title: e.target.value })} disabled={disabled} />
        </FormField>
      )}
      <FormField label="Hashtags" hint="Separate with spaces; added after the text unless already in it">
        <Input value={form.hashtags} onChange={(e) => onChange({ hashtags: e.target.value })} disabled={disabled} />
      </FormField>
      <FormField label="Mentions / tagged accounts" optional>
        <Input value={form.mentions} onChange={(e) => onChange({ mentions: e.target.value })} disabled={disabled} />
      </FormField>
      <FormField
        label="Link"
        optional
        hint={
          preset?.linkHandling === 'NotClickable'
            ? 'Links are not clickable in captions on this network.'
            : preset?.linkHandling === 'InText'
              ? 'Added to the text; X counts every link as 23 characters.'
              : undefined
        }
      >
        <Input type="url" value={form.link} onChange={(e) => onChange({ link: e.target.value })} disabled={disabled} />
      </FormField>
      {preset?.supportsFirstComment && (
        <FormField label="First comment" optional>
          <Textarea rows={2} value={form.firstComment} onChange={(e) => onChange({ firstComment: e.target.value })} disabled={disabled} />
        </FormField>
      )}
      <fieldset className="stack" style={{ border: 0, padding: 0, margin: 0 }}>
        <legend className="sm-h3">Media</legend>
        {media.length === 0 && <p className="sm-muted">No media in this client's library yet.</p>}
        <div className="sm-media-grid">
          {media.map((m) => {
            const index = form.mediaIds.indexOf(m.id);
            return (
              <div key={m.id} className="sm-media-card">
                <MediaImage media={m} alt={m.altText || m.title} />
                <div className="sm-media-card__body">
                  <Checkbox
                    label={m.title}
                    checked={index >= 0}
                    disabled={disabled}
                    onChange={(e) =>
                      onChange(
                        e.target.checked
                          ? { mediaIds: [...form.mediaIds, m.id], altTexts: [...form.mediaIds.map((_, i) => form.altTexts[i] ?? ''), m.altText ?? ''] }
                          : {
                              mediaIds: form.mediaIds.filter((x) => x !== m.id),
                              altTexts: form.altTexts.filter((_, i) => i !== index),
                            },
                      )
                    }
                  />
                  {index >= 0 && m.kind === 'Image' && (preset?.maxAltTextLength ?? 0) > 0 && (
                    <FormField label="Alt text">
                      <Input
                        value={form.altTexts[index] ?? ''}
                        disabled={disabled}
                        onChange={(e) => {
                          const next = form.mediaIds.map((_, i) => form.altTexts[i] ?? '');
                          next[index] = e.target.value;
                          onChange({ altTexts: next });
                        }}
                      />
                    </FormField>
                  )}
                </div>
              </div>
            );
          })}
        </div>
      </fieldset>
      {validation && validation.issues.length > 0 && (
        <section aria-label={`${NETWORK_LABELS[profile.network]} checks`}>
          <ul className="sm-issues">
            {validation.issues.map((i, n) => (
              <li key={n} className={`sm-issue--${i.severity}`}>
                <strong>{i.severity === 'Error' ? 'Error' : 'Warning'}:</strong> {i.message}
              </li>
            ))}
          </ul>
        </section>
      )}
    </div>
  );
}

function Discussion({ post }: { post: Post }) {
  const [body, setBody] = useState('');
  const [internal, setInternal] = useState(true);
  const queryClient = useQueryClient();
  const toast = useToast();
  const { user } = useAuth();
  const refresh = () => void queryClient.invalidateQueries({ queryKey: socialKeys.post(post.id) });
  const add = useMutation({
    mutationFn: () => api.post<Post>(`/agency/social/posts/${post.id}/comments`, { body, internal }),
    onSuccess: () => {
      setBody('');
      refresh();
    },
    onError: (e) => toast.error('Comment not saved', errorMessage(e)),
  });
  const resolve = useMutation({
    mutationFn: ({ id, done }: { id: string; done: boolean }) => api.post(`/agency/social/posts/${post.id}/comments/${id}/${done ? 'resolve' : 'reopen'}`),
    onSuccess: refresh,
    onError: (e) => toast.error('Not changed', errorMessage(e)),
  });
  const remove = useMutation({
    mutationFn: (id: string) => api.delete(`/agency/social/posts/${post.id}/comments/${id}`),
    onSuccess: refresh,
    onError: (e) => toast.error('Comment not deleted', errorMessage(e)),
  });
  const tone = (kind: string): Tone =>
    kind === 'Approved' || kind === 'Published' ? 'success' : kind === 'ChangesRequested' || kind === 'Failed' ? 'danger' : 'neutral';
  const feedback = post.comments.filter((c) => c.kind === 'Comment' || c.kind === 'ChangesRequested');
  const open = feedback.filter((c) => !c.isResolved).length;
  return (
    <Card as="section" aria-labelledby="sm-discussion">
      <CardHeader
        title="Approvals & comments"
        titleId="sm-discussion"
        description={feedback.length > 0 ? `${open} open feedback item${open === 1 ? '' : 's'} of ${feedback.length}` : undefined}
      />
      <CardBody className="stack">
        {post.comments.length === 0 ? (
          <p className="sm-muted">No comments yet.</p>
        ) : (
          <Timeline
            label="Post history"
            items={post.comments.map((c) => {
              const resolvable = c.kind === 'Comment' || c.kind === 'ChangesRequested';
              const mine = !!user && c.authorUserId === user.id && c.kind === 'Comment' && !c.isClient;
              return {
                id: c.id,
                title: `${c.authorName}${c.isClient ? ' (client)' : ''}${c.isInternal ? ' · internal' : ''}${c.isResolved ? ' · done' : ''}`,
                description: (
                  <span className="stack" style={{ gap: 4 }}>
                    <span className={c.isResolved ? 'sm-pre sm-muted' : 'sm-pre'}>{c.body}</span>
                    {(resolvable || mine) && (
                      <span className="cluster">
                        {resolvable && (
                          <Button
                            size="sm"
                            variant="ghost"
                            leadingIcon={c.isResolved ? <RotateCcw /> : <CheckCircle2 />}
                            onClick={() => resolve.mutate({ id: c.id, done: !c.isResolved })}
                          >
                            {c.isResolved ? 'Reopen' : 'Mark done'}
                          </Button>
                        )}
                        {mine && (
                          <Button size="sm" variant="ghost" leadingIcon={<Trash2 />} onClick={() => remove.mutate(c.id)}>
                            Delete
                          </Button>
                        )}
                      </span>
                    )}
                  </span>
                ),
                timestamp: c.createdAt,
                tone: c.isResolved ? 'success' : tone(c.kind),
              };
            })}
          />
        )}
        <FormField label="Add a comment">
          <Textarea rows={3} value={body} onChange={(e) => setBody(e.target.value)} />
        </FormField>
        <Checkbox label="Internal note (hidden from the client)" checked={internal} onChange={(e) => setInternal(e.target.checked)} />
        <div>
          <Button size="sm" leadingIcon={<Send />} disabled={!body.trim()} loading={add.isPending} onClick={() => add.mutate()}>
            Comment
          </Button>
        </div>
      </CardBody>
    </Card>
  );
}

function PublishingState({ post }: { post: Post }) {
  const columns: DataTableColumn<Post['variants'][number]>[] = [
    { id: 'network', header: 'Network', primary: true, cell: (v) => <NetworkChip network={v.network as SocialNetwork} /> },
    { id: 'state', header: 'State', cell: (v) => v.publishStatus + (v.publishedManually ? ' (manual)' : '') },
    {
      id: 'detail',
      header: 'Detail',
      cell: (v) =>
        v.publishedUrl ? (
          <SafeExternalLink href={v.publishedUrl}>Live post</SafeExternalLink>
        ) : v.failureReason ? (
          <span className="sm-issue--Error">{v.failureReason}</span>
        ) : v.nextAttemptAt ? (
          <>
            Retry at <DateTime value={v.nextAttemptAt} format="datetime" />
          </>
        ) : (
          '—'
        ),
    },
  ];
  return (
    <Card as="section" aria-labelledby="sm-publishing-state">
      <CardHeader title="Publishing" titleId="sm-publishing-state" />
      <CardBody className="stack">
        {post.scheduledAt && (
          <p className="sm-muted">
            {post.status === 'Scheduled' ? 'Scheduled for ' : 'Planned for '}
            <DateTime value={post.scheduledAt} format="datetime" />
          </p>
        )}
        <DataTable caption="Publishing state per network" columns={columns} rows={post.variants} getRowId={(v) => v.id} />
      </CardBody>
    </Card>
  );
}

function ActionDialogs({
  post,
  action,
  onClose,
  run,
  canPublish,
}: {
  post: Post | null;
  action: Action | null;
  onClose: () => void;
  run: (kind: Action, payload?: Record<string, unknown>) => Promise<void>;
  canPublish: boolean;
}) {
  const [when, setWhen] = useState(toLocalInput(post?.scheduledAt));
  const [variantId, setVariantId] = useState('');
  const [url, setUrl] = useState('');
  const [confirmNotLive, setConfirmNotLive] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  useEffect(() => setError(null), [action]);
  if (!post || !action) return null;

  const exec = async (payload?: Record<string, unknown>) => {
    setBusy(true);
    setError(null);
    try {
      await run(action, payload);
      onClose();
    } catch (e) {
      setError(isApiError(e) ? e.title : errorMessage(e));
    } finally {
      setBusy(false);
    }
  };

  if (action === 'requestChanges' || action === 'submit' || action === 'approve')
    return (
      <ConfirmDialog
        open
        onClose={onClose}
        title={ACTION_LABELS[action]}
        description={
          action === 'requestChanges'
            ? 'The post goes back to Draft with your note.'
            : action === 'approve'
              ? post.requiresClientApproval
                ? 'The client will be asked to approve it in their portal.'
                : 'The post becomes ready to schedule.'
              : 'The team will review it before it can be scheduled.'
        }
        requireReason={action === 'requestChanges'}
        reasonLabel="What should change?"
        confirmLabel={ACTION_LABELS[action]}
        onConfirm={async ({ reason }) => run(action, { comment: reason || undefined })}
      />
    );

  if (action === 'schedule')
    return (
      <Dialog
        open
        onClose={onClose}
        title="Schedule post"
        footer={
          <>
            <Button variant="secondary" onClick={onClose}>
              Cancel
            </Button>
            <Button loading={busy} onClick={() => void exec({ scheduledAt: fromLocalInput(when) })}>
              Schedule
            </Button>
          </>
        }
      >
        {error && <Alert tone="danger">{error}</Alert>}
        <FormField label="Publish at" hint="Your local time">
          <Input type="datetime-local" value={when} onChange={(e) => setWhen(e.target.value)} />
        </FormField>
      </Dialog>
    );

  if (action === 'markPublished') {
    const candidates = post.variants.filter((v) => v.publishStatus !== 'Published' && v.publishStatus !== 'Publishing');
    return (
      <Dialog
        open
        onClose={onClose}
        title="Mark as published"
        description="Record a post you published on the network yourself. Only do this once it is live."
        footer={
          <>
            <Button variant="secondary" onClick={onClose}>
              Cancel
            </Button>
            <Button loading={busy} disabled={!canPublish} onClick={() => void exec({ variantId: variantId || candidates[0]?.id, url })}>
              Mark as published
            </Button>
          </>
        }
      >
        {error && <Alert tone="danger">{error}</Alert>}
        <FormField label="Network">
          <Select
            value={variantId || candidates[0]?.id || ''}
            onChange={(e) => setVariantId(e.target.value)}
            options={candidates.map((v) => ({ value: v.id, label: `${NETWORK_LABELS[v.network]} @${v.profileHandle}` }))}
          />
        </FormField>
        <FormField label="Live post URL" required hint="https link to the published post">
          <Input type="url" value={url} onChange={(e) => setUrl(e.target.value)} />
        </FormField>
      </Dialog>
    );
  }

  if (action === 'retry') {
    const unknown = post.variants.some((v) => v.failureKind === 'Unknown');
    return (
      <Dialog
        open
        onClose={onClose}
        title="Retry publishing"
        footer={
          <>
            <Button variant="secondary" onClick={onClose}>
              Cancel
            </Button>
            <Button loading={busy} disabled={unknown && !confirmNotLive} onClick={() => void exec({ confirmNotPublished: confirmNotLive })}>
              Retry
            </Button>
          </>
        }
      >
        {error && <Alert tone="danger">{error}</Alert>}
        <p>Failed networks are queued again. Networks without an adapter will fail again — use “Mark as published” instead.</p>
        {unknown && (
          <Checkbox
            label="I checked the network: the post is not live"
            description="A previous attempt was interrupted after it was sent."
            checked={confirmNotLive}
            onChange={(e) => setConfirmNotLive(e.target.checked)}
          />
        )}
      </Dialog>
    );
  }

  return (
    <ConfirmDialog
      open
      onClose={onClose}
      tone={action === 'delete' ? 'danger' : 'primary'}
      title={`${ACTION_LABELS[action]}?`}
      description={
        action === 'queue'
          ? 'The post is scheduled at the next free queue slot of its profiles.'
          : action === 'unschedule'
            ? 'The post goes back to Approved.'
            : 'This cannot be undone.'
      }
      confirmLabel={ACTION_LABELS[action]}
      onConfirm={() => run(action)}
    />
  );
}
