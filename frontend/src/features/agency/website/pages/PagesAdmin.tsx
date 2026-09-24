import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ArrowDown, ArrowUp, Eye, ExternalLink, History, Plus, RotateCcw, Trash2 } from 'lucide-react';
import { useState, type FormEvent } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { Alert, Badge, Button, ButtonLink, ConfirmDialog, DataTable, DateTime, ErrorState, IconButton, PageHeader, ScrollArea, Select, Skeleton, useToast } from '@/components/ui';
import { isoToLocalInput, localInputToIso } from '@/features/admin/shared/common';
import type { PageBlock } from '@/features/public/site/api';
import { Blocks } from '@/features/public/site/Blocks';
import { api } from '@/lib/api/client';
import { errorMessage, isApiError } from '@/lib/api/errors';
import { formatDate } from '@/lib/format/dates';
import { type SitePage, type SitePageRevision, type SitePageRevisionSummary, type SitePageSummary, W } from '../api';
import { AreaField, EMPTY_SEO, type Errors, ImageField, ListEditor, MarkdownField, SelectField, SeoFields, SwitchField, TextField, toErrors } from '../shared/fields';
import { ICON_OPTIONS } from './ContentPages';
import '@/features/public/site/site.css';
import '../website.css';

export const BLOCK_TYPES: { value: string; label: string; empty: Record<string, unknown> }[] = [
  { value: 'hero', label: 'Hero', empty: { eyebrow: '', title: 'New headline', subtitle: '', primaryCta: null, secondaryCta: null, imageUrl: null } },
  { value: 'richText', label: 'Rich text', empty: { markdown: 'Write something useful.' } },
  { value: 'featuresGrid', label: 'Features grid', empty: { title: 'Why us', intro: '', items: [{ title: 'Feature', text: 'Explain the benefit.', icon: null }] } },
  { value: 'stats', label: 'Stats', empty: { title: 'Results', items: [{ label: 'Metric', value: '+10%', measurement: 'Measured', context: null }] } },
  { value: 'cta', label: 'Call to action', empty: { title: 'Ready to grow?', text: '', primary: { label: 'Get a free audit', url: '/free-audit' }, secondary: null } },
  { value: 'faq', label: 'FAQ', empty: { title: 'Frequently asked questions', items: [{ question: 'Question?', answer: 'Answer.' }] } },
  { value: 'testimonials', label: 'Testimonials', empty: { title: 'What clients say', testimonialIds: [] } },
  { value: 'logoCloud', label: 'Logo cloud', empty: { title: 'Trusted by', logos: [] } },
  { value: 'servicesGrid', label: 'Services grid', empty: { title: 'Our services', intro: '', categorySlug: null } },
  { value: 'caseStudyHighlight', label: 'Case study highlight', empty: { title: 'Featured case study', caseStudySlug: '' } },
];

const labelOf = (type: string) => BLOCK_TYPES.find((b) => b.value === type)?.label ?? type;
const s = (v: unknown) => (typeof v === 'string' ? v : '');
type Link = { label: string; url: string } | null;
const lnk = (v: unknown): Link => (v && typeof v === 'object' ? (v as Link) : null);
const arr = <T,>(v: unknown): T[] => (Array.isArray(v) ? (v as T[]) : []);

function LinkFields({ label, value, onChange, error }: { label: string; value: Link; onChange: (v: Link) => void; error?: string }) {
  return (
    <div className="cms-grid-2">
      <TextField label={`${label} label`} value={value?.label ?? ''} onChange={(v) => onChange(v || value?.url ? { label: v, url: value?.url ?? '' } : null)} error={error} />
      <TextField label={`${label} link`} value={value?.url ?? ''} onChange={(v) => onChange(v || value?.label ? { label: value?.label ?? '', url: v } : null)} hint="/path or https://…" />
    </div>
  );
}

/** Per-type block form. Errors use the API's keys, e.g. `blocks[2].data.title`. */
function BlockForm({ block, update, errors, index }: { block: PageBlock; update: (data: Record<string, unknown>) => void; errors: Errors; index: number }) {
  const d = block.data ?? {};
  const set = (key: string, value: unknown) => update({ ...d, [key]: value });
  const e = (field: string) => errors[`blocks[${index}].data.${field}`] ?? errors[`blocks[${index}].data`];
  switch (block.type) {
    case 'hero':
      return (
        <>
          <TextField label="Eyebrow" value={s(d.eyebrow)} onChange={(v) => set('eyebrow', v)} error={e('eyebrow')} />
          <TextField label="Headline" required value={s(d.title)} onChange={(v) => set('title', v)} error={e('title')} />
          <AreaField label="Subtitle" value={s(d.subtitle)} onChange={(v) => set('subtitle', v)} error={e('subtitle')} />
          <LinkFields label="Primary button" value={lnk(d.primaryCta)} onChange={(v) => set('primaryCta', v)} error={e('primaryCta.url') ?? e('primaryCta')} />
          <LinkFields label="Secondary button" value={lnk(d.secondaryCta)} onChange={(v) => set('secondaryCta', v)} error={e('secondaryCta.url') ?? e('secondaryCta')} />
          <ImageField label="Image" value={s(d.imageUrl) || null} onChange={(v) => set('imageUrl', v || null)} error={e('imageUrl')} />
        </>
      );
    case 'richText':
      return <MarkdownField label="Content" required value={s(d.markdown)} onChange={(v) => set('markdown', v)} error={e('markdown')} />;
    case 'featuresGrid':
      return (
        <>
          <TextField label="Title" value={s(d.title)} onChange={(v) => set('title', v)} error={e('title')} />
          <AreaField label="Intro" value={s(d.intro)} onChange={(v) => set('intro', v)} rows={2} />
          <ListEditor<{ title: string; text: string; icon: string | null }>
            legend="Features"
            items={arr(d.items)}
            onChange={(v) => set('items', v)}
            empty={{ title: '', text: '', icon: null }}
            error={e('items')}
            render={(it, up) => (
              <>
                <TextField label="Feature title" value={it.title} onChange={(v) => up({ ...it, title: v })} />
                <AreaField label="Text" value={it.text} onChange={(v) => up({ ...it, text: v })} rows={2} />
                <SelectField label="Icon" value={it.icon} onChange={(v) => up({ ...it, icon: v || null })} options={ICON_OPTIONS} placeholder="Default" />
              </>
            )}
          />
        </>
      );
    case 'stats':
      return (
        <>
          <TextField label="Title" value={s(d.title)} onChange={(v) => set('title', v)} />
          <ListEditor<{ label: string; value: string; measurement: string; context: string | null }>
            legend="Stats"
            items={arr(d.items)}
            onChange={(v) => set('items', v)}
            empty={{ label: '', value: '', measurement: 'Measured', context: null }}
            error={e('items')}
            render={(it, up) => (
              <div className="cms-grid-2">
                <TextField label="Label" value={it.label} onChange={(v) => up({ ...it, label: v })} />
                <TextField label="Value" value={it.value} onChange={(v) => up({ ...it, value: v })} />
                <SelectField label="Measurement" required value={it.measurement} onChange={(v) => up({ ...it, measurement: v })} options={[{ value: 'Measured', label: 'Measured' }, { value: 'Estimated', label: 'Estimated' }]} />
                <TextField label="Context" value={it.context} onChange={(v) => up({ ...it, context: v || null })} />
              </div>
            )}
          />
        </>
      );
    case 'cta':
      return (
        <>
          <TextField label="Title" required value={s(d.title)} onChange={(v) => set('title', v)} error={e('title')} />
          <AreaField label="Text" value={s(d.text)} onChange={(v) => set('text', v)} rows={2} />
          <LinkFields label="Primary button" value={lnk(d.primary)} onChange={(v) => set('primary', v)} error={e('primary.url') ?? e('primary')} />
          <LinkFields label="Secondary button" value={lnk(d.secondary)} onChange={(v) => set('secondary', v)} error={e('secondary.url')} />
        </>
      );
    case 'faq':
      return (
        <>
          <TextField label="Title" value={s(d.title)} onChange={(v) => set('title', v)} />
          <ListEditor<{ question: string; answer: string }>
            legend="Questions"
            items={arr(d.items)}
            onChange={(v) => set('items', v)}
            empty={{ question: '', answer: '' }}
            error={e('items')}
            render={(it, up) => (
              <>
                <TextField label="Question" value={it.question} onChange={(v) => up({ ...it, question: v })} />
                <AreaField label="Answer (Markdown)" value={it.answer} onChange={(v) => up({ ...it, answer: v })} rows={3} />
              </>
            )}
          />
        </>
      );
    case 'testimonials':
      return <TextField label="Title" value={s(d.title)} onChange={(v) => set('title', v)} hint="Shows the featured testimonials." />;
    case 'logoCloud':
      return (
        <>
          <TextField label="Title" value={s(d.title)} onChange={(v) => set('title', v)} />
          <ListEditor<{ name: string; imageUrl: string; url: string | null }>
            legend="Logos"
            items={arr(d.logos)}
            onChange={(v) => set('logos', v)}
            empty={{ name: '', imageUrl: '', url: null }}
            error={e('logos')}
            render={(it, up) => (
              <>
                <TextField label="Company" value={it.name} onChange={(v) => up({ ...it, name: v })} />
                <ImageField label="Logo" value={it.imageUrl} onChange={(v) => up({ ...it, imageUrl: v })} />
              </>
            )}
          />
          <p className="text-small text-muted">Leave empty to show the site's trust logos.</p>
        </>
      );
    case 'servicesGrid':
      return (
        <>
          <TextField label="Title" value={s(d.title)} onChange={(v) => set('title', v)} />
          <AreaField label="Intro" value={s(d.intro)} onChange={(v) => set('intro', v)} rows={2} />
          <TextField label="Service line slug" value={s(d.categorySlug)} onChange={(v) => set('categorySlug', v || null)} error={e('categorySlug')} hint="Empty shows every service." />
        </>
      );
    case 'caseStudyHighlight':
      return (
        <>
          <TextField label="Title" value={s(d.title)} onChange={(v) => set('title', v)} />
          <TextField label="Case study slug" required value={s(d.caseStudySlug)} onChange={(v) => set('caseStudySlug', v)} error={e('caseStudySlug')} />
        </>
      );
    default:
      return <p className="text-muted">Unknown block type.</p>;
  }
}

/** Published pages with a go-live time in the future are scheduled. */
function pageStatus(p: { isPublished: boolean; publishAt: string | null }): { label: string; tone: 'success' | 'info' | 'neutral' } {
  if (!p.isPublished) return { label: 'Draft', tone: 'neutral' };
  if (p.publishAt && new Date(p.publishAt).getTime() > Date.now()) return { label: 'Scheduled', tone: 'info' };
  return { label: 'Published', tone: 'success' };
}

const ACTION_LABEL: Record<string, string> = {
  initial: 'Original content',
  created: 'Created',
  updated: 'Saved',
  restored: 'Restored',
};

/**
 * Version history of a page: every save is kept. Preview shows an older version in the live preview; restoring copies
 * its content into a new version (publishing state and schedule stay as they are).
 */
function PageHistory({
  pageId,
  stamp,
  onPreview,
  previewing,
}: {
  pageId: string;
  stamp: string;
  onPreview: (revision: SitePageRevision | null) => void;
  previewing: number | null;
}) {
  const toast = useToast();
  const client = useQueryClient();
  const [restoring, setRestoring] = useState<SitePageRevisionSummary | null>(null);
  const history = useQuery({
    queryKey: ['agency', 'website', 'page', pageId, 'revisions'],
    queryFn: () => api.get<SitePageRevisionSummary[]>(`${W}/pages/${pageId}/revisions`),
  });
  const load = useMutation({
    mutationFn: (version: number) => api.get<SitePageRevision>(`${W}/pages/${pageId}/revisions/${version}`),
    onSuccess: (r) => onPreview(r),
  });

  return (
    <section aria-labelledby="history-title" className="cms-form">
      <h2 id="history-title">
        <History aria-hidden="true" width={18} height={18} /> Version history
      </h2>
      {history.isError ? (
        <ErrorState error={history.error} onRetry={() => void history.refetch()} />
      ) : history.isLoading ? (
        <Skeleton height={80} />
      ) : (history.data ?? []).length === 0 ? (
        <p className="text-muted">No saved versions yet. Every save from now on is kept here.</p>
      ) : (
        <ol className="cms-history">
          {(history.data ?? []).map((r) => (
            <li key={r.version} className="cms-history__item">
              <div>
                <strong>Version {r.version}</strong> · {ACTION_LABEL[r.action] ?? r.action}
                {r.isCurrent && (
                  <>
                    {' '}
                    <Badge tone="success">Current</Badge>
                  </>
                )}
                {previewing === r.version && (
                  <>
                    {' '}
                    <Badge tone="info">In preview</Badge>
                  </>
                )}
                <div className="text-small text-muted">
                  <DateTime value={r.createdAt} format="datetime" />
                  {r.authorName && <> · {r.authorName}</>}
                  {r.note && <> · {r.note}</>}
                </div>
              </div>
              <div className="cms-toolbar">
                <Button size="sm" variant="ghost" leadingIcon={<Eye />} loading={load.isPending && load.variables === r.version} onClick={() => (previewing === r.version ? onPreview(null) : load.mutate(r.version))}>
                  {previewing === r.version ? 'Close preview' : 'Preview'}
                  <span className="visually-hidden"> version {r.version}</span>
                </Button>
                {!r.isCurrent && (
                  <Button size="sm" variant="secondary" leadingIcon={<RotateCcw />} onClick={() => setRestoring(r)}>
                    Restore<span className="visually-hidden"> version {r.version}</span>
                  </Button>
                )}
              </div>
            </li>
          ))}
        </ol>
      )}
      <ConfirmDialog
        open={!!restoring}
        onClose={() => setRestoring(null)}
        title={`Restore version ${restoring?.version ?? ''}?`}
        description="Its title, address, blocks and SEO settings become a new version. Whether the page is published, and its schedule, stay as they are now."
        confirmLabel="Restore version"
        onConfirm={async () => {
          if (!restoring) return;
          try {
            const saved = await api.post<SitePage>(`${W}/pages/${pageId}/revisions/${restoring.version}/restore`, { concurrencyStamp: stamp });
            client.setQueryData(['agency', 'website', 'page', pageId], saved);
          } catch (error) {
            throw new Error(
              isApiError(error) && error.status === 409 && error.code === 'concurrency.conflict'
                ? 'Someone else saved this page since you opened it. Reload the page, then try again.'
                : errorMessage(error),
            );
          }
          onPreview(null);
          toast.success(`Version ${restoring.version} restored`, 'It is now the current version.');
          await client.invalidateQueries({ queryKey: ['agency', 'website', 'page', pageId, 'revisions'] });
          await client.invalidateQueries({ queryKey: ['agency', 'website', 'pages'] });
        }}
      />
    </section>
  );
}

/** List of CMS pages. */
export function PagesAdminPage() {
  const query = useQuery({ queryKey: ['agency', 'website', 'pages'], queryFn: () => api.get<SitePageSummary[]>(`${W}/pages`) });
  return (
    <div className="cms-page">
      <PageHeader
        title="Pages"
        description="Block-based pages such as About, How we work and the legal pages. Legal templates must be reviewed with counsel before launch."
        actions={
          <ButtonLink to="new" leadingIcon={<Plus />}>
            New page
          </ButtonLink>
        }
      />
      {query.isError ? (
        <ErrorState error={query.error} onRetry={() => void query.refetch()} />
      ) : (
        <DataTable
          caption="Pages"
          rows={query.data ?? []}
          loading={query.isLoading}
          getRowId={(r) => r.id}
          rowLabel={(r) => r.title}
          columns={[
            { id: 'title', header: 'Page', primary: true, cell: (r) => <ButtonLink to={r.id} variant="link">{r.title}</ButtonLink> },
            { id: 'slug', header: 'Address', cell: (r) => <code>/{r.slug}</code>, hideOnMobile: true },
            { id: 'kind', header: 'Kind', cell: (r) => r.kind },
            {
              id: 'status',
              header: 'Status',
              cell: (r) => {
                const st = pageStatus(r);
                return <Badge tone={st.tone}>{st.label}</Badge>;
              },
            },
            { id: 'version', header: 'Version', cell: (r) => (r.version > 0 ? `v${r.version}` : '—'), hideOnMobile: true },
            { id: 'updated', header: 'Updated', cell: (r) => formatDate(r.updatedAt), hideOnMobile: true },
          ]}
        />
      )}
    </div>
  );
}

type PageDraft = Omit<SitePage, 'id' | 'updatedAt' | 'concurrencyStamp' | 'version'>;

/** Block editor with add / reorder / remove and a live preview rendered by the public site's block renderer. */
export function PageEditorPage() {
  const { pageId = 'new' } = useParams();
  const isNew = pageId === 'new';
  const navigate = useNavigate();
  const toast = useToast();
  const client = useQueryClient();
  const detail = useQuery({ queryKey: ['agency', 'website', 'page', pageId], queryFn: () => api.get<SitePage>(`${W}/pages/${pageId}`), enabled: !isNew });
  const [draft, setDraft] = useState<PageDraft | null>(null);
  const [errors, setErrors] = useState<Errors>({});
  const [newType, setNewType] = useState('richText');
  const [note, setNote] = useState('');
  const [preview, setPreview] = useState<SitePageRevision | null>(null);
  const current: PageDraft | null =
    draft ??
    (isNew
      ? { slug: '', title: '', summary: null, kind: 'Standard', blocks: [], seo: EMPTY_SEO, isPublished: false, sortOrder: 0, publishAt: null }
      : detail.data
        ? { ...detail.data }
        : null);

  const save = useMutation({
    mutationFn: () => {
      const body = {
        ...current!,
        publishAt: current!.isPublished ? current!.publishAt : null,
        revisionNote: note.trim() || null,
        blocks: current!.blocks.map((b) => ({ id: b.id, type: b.type, data: b.data })),
      };
      return isNew
        ? api.post<SitePage>(`${W}/pages`, body)
        : api.put<SitePage>(`${W}/pages/${pageId}`, { ...body, concurrencyStamp: detail.data!.concurrencyStamp });
    },
    onSuccess: async (saved) => {
      toast.success('Page saved', `Saved as version ${saved.version}.`);
      setDraft(null);
      setErrors({});
      setNote('');
      await client.invalidateQueries({ queryKey: ['agency', 'website', 'pages'] });
      await client.invalidateQueries({ queryKey: ['agency', 'website', 'page', saved.id, 'revisions'] });
      client.setQueryData(['agency', 'website', 'page', saved.id], saved);
      if (isNew) navigate(`../pages/${saved.id}`, { replace: true, relative: 'path' });
    },
    onError: (e) => isApiError(e) && setErrors(toErrors(e.errors)),
  });

  if (!isNew && detail.isLoading) return <Skeleton height={320} />;
  if (!isNew && detail.isError) return <ErrorState error={detail.error} />;
  if (!current) return null;
  const set = <K extends keyof PageDraft>(k: K, v: PageDraft[K]) => setDraft({ ...current, [k]: v });
  const setBlocks = (blocks: PageBlock[]) => set('blocks', blocks);
  const move = (from: number, to: number) => {
    const next = [...current.blocks];
    const [x] = next.splice(from, 1);
    next.splice(to, 0, x);
    setBlocks(next);
  };

  const submit = (e: FormEvent) => {
    e.preventDefault();
    save.mutate();
  };

  return (
    <div className="cms-page">
      <PageHeader
        title={isNew ? 'New page' : `Edit “${current.title}”`}
        breadcrumbs={[{ label: 'Pages', to: '..' }, { label: isNew ? 'New page' : current.title }]}
        actions={
          !isNew && current.isPublished ? (
            <a className="ui-button ui-button--secondary ui-button--md" href={`/${current.slug}`} target="_blank" rel="noopener noreferrer">
              <ExternalLink aria-hidden="true" width={16} height={16} /> View page<span className="visually-hidden"> (opens in a new tab)</span>
            </a>
          ) : undefined
        }
      />
      <div className="cms-editor cms-editor--split">
        <form className="cms-form" onSubmit={submit} noValidate aria-label="Page editor">
          <div className="cms-grid-2">
            <TextField label="Title" required value={current.title} onChange={(v) => set('title', v)} error={errors.title} />
            <TextField label="Slug" required value={current.slug} onChange={(v) => set('slug', v)} error={errors.slug} hint="Address: /slug" />
            <SelectField label="Kind" required value={current.kind} onChange={(v) => set('kind', v as PageDraft['kind'])} options={[{ value: 'Standard', label: 'Standard' }, { value: 'Legal', label: 'Legal' }]} error={errors.kind} />
            <TextField label="Sort order" type="number" value={String(current.sortOrder)} onChange={(v) => set('sortOrder', Number(v) || 0)} />
          </div>
          <AreaField label="Summary" value={current.summary} onChange={(v) => set('summary', v || null)} maxLength={500} rows={2} />

          <section aria-labelledby="blocks-title" className="cms-form">
            <h2 id="blocks-title">Blocks</h2>
            {errors.blocks && <Alert tone="danger">{errors.blocks}</Alert>}
            {current.blocks.length === 0 && <p className="text-muted">No blocks yet. Add the first one below.</p>}
            {current.blocks.map((block, i) => (
              <div key={block.id} className="cms-block" role="group" aria-label={`Block ${i + 1}: ${labelOf(block.type)}`}>
                <div className="cms-block__head">
                  <strong>
                    {i + 1}. {labelOf(block.type)}
                  </strong>
                  {errors[`blocks[${i}].type`] && <Badge tone="danger">{errors[`blocks[${i}].type`]}</Badge>}
                  <div className="cms-toolbar">
                    <IconButton size="sm" variant="ghost" label={`Move block ${i + 1} up`} icon={<ArrowUp />} disabled={i === 0} onClick={() => move(i, i - 1)} />
                    <IconButton size="sm" variant="ghost" label={`Move block ${i + 1} down`} icon={<ArrowDown />} disabled={i === current.blocks.length - 1} onClick={() => move(i, i + 1)} />
                    <IconButton size="sm" variant="ghost" label={`Remove block ${i + 1}`} icon={<Trash2 />} onClick={() => setBlocks(current.blocks.filter((_, j) => j !== i))} />
                  </div>
                </div>
                <BlockForm block={block} index={i} errors={errors} update={(data) => setBlocks(current.blocks.map((b, j) => (j === i ? { ...b, data } : b)))} />
              </div>
            ))}
            <div className="cms-toolbar">
              <label htmlFor="new-block-type" className="visually-hidden">
                Block type
              </label>
              <Select id="new-block-type" value={newType} onChange={(e) => setNewType(e.target.value)} options={BLOCK_TYPES.map((b) => ({ value: b.value, label: b.label }))} />
              <Button
                variant="secondary"
                leadingIcon={<Plus />}
                onClick={() => {
                  const type = BLOCK_TYPES.find((b) => b.value === newType)!;
                  setBlocks([...current.blocks, { id: Math.random().toString(36).slice(2, 14), type: type.value, data: structuredClone(type.empty) }]);
                }}
              >
                Add block
              </Button>
            </div>
          </section>

          <SeoFields value={current.seo} onChange={(v) => set('seo', v)} errors={errors} />
          <SwitchField label="Published" checked={current.isPublished} onChange={(v) => set('isPublished', v)} />
          {current.isPublished && (
            <TextField
              label="Go live at"
              type="datetime-local"
              value={isoToLocalInput(current.publishAt)}
              onChange={(v) => set('publishAt', localInputToIso(v))}
              error={errors.publishAt}
              hint="Optional. Leave empty to publish as soon as you save; a future time keeps the page hidden until then (your local time)."
            />
          )}
          {!isNew && (
            <TextField
              label="Change note"
              value={note}
              onChange={setNote}
              maxLength={300}
              error={errors.revisionNote}
              hint="Optional. Shown in the version history, e.g. “Updated data retention period”."
            />
          )}
          {save.isError && !Object.keys(errors).length && (
            <Alert tone="danger" title="Couldn't save the page">
              {isApiError(save.error) && save.error.code === 'concurrency.conflict'
                ? 'Someone else saved this page since you opened it. Copy your changes, reload the page and apply them again.'
                : errorMessage(save.error)}
            </Alert>
          )}
          {save.isError && Object.keys(errors).length > 0 && (
            <Alert tone="danger" title="Some fields need attention">
              Check the highlighted fields and blocks.
            </Alert>
          )}
          <div className="cms-form__actions">
            <Button type="submit" loading={save.isPending}>
              Save page
            </Button>
            {draft && (
              <Button type="button" variant="ghost" onClick={() => setDraft(null)}>
                Discard changes
              </Button>
            )}
          </div>
          {!isNew && detail.data && (
            <PageHistory pageId={pageId} stamp={detail.data.concurrencyStamp} onPreview={setPreview} previewing={preview?.version ?? null} />
          )}
        </form>
        <aside className="cms-editor__preview" aria-label={preview ? `Preview of version ${preview.version}` : 'Live preview'}>
          {/* A long preview scrolls on its own; keyboard users can focus and scroll it (it holds no links of its own). */}
          <ScrollArea className="cms-editor__preview-scroll" label="Preview content">
            <p className="cms-editor__preview-label">{preview ? `Version ${preview.version} (read only)` : 'Live preview'}</p>
            <div className="site-layout">
              {(preview ?? current).blocks[0]?.type !== 'hero' && (
                <div className="site-hero">
                  <div className="container">
                    <p className="site-hero__title">
                      {(preview ?? current).title || 'Page title'}
                    </p>
                  </div>
                </div>
              )}
              <Blocks blocks={(preview ?? current).blocks} context={{ pageTitle: (preview ?? current).title }} />
            </div>
          </ScrollArea>
        </aside>
      </div>
    </div>
  );
}

