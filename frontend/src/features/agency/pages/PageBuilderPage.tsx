import { useMutation, useQueries, useQuery, useQueryClient } from '@tanstack/react-query';
import { ArrowDown, ArrowUp, ExternalLink, Monitor, Plus, Rocket, Save, Smartphone, Trash2, Undo2 } from 'lucide-react';
import { useEffect, useMemo, useRef, useState, type KeyboardEvent } from 'react';
import { useParams } from 'react-router-dom';
import {
  Alert,
  Badge,
  Button,
  Card,
  CardBody,
  CardHeader,
  Checkbox,
  DataTable,
  DateTime,
  ErrorState,
  FormField,
  IconButton,
  Input,
  PageHeader,
  Select,
  Skeleton,
  Switch,
  Tabs,
  Textarea,
  useToast,
  type DataTableColumn,
} from '@/components/ui';
import { api } from '@/lib/api/client';
import { errorMessage, isApiError } from '@/lib/api/errors';
import type { PagedResult } from '@/lib/api/types';
import { fieldErrors, fmt } from '../seo/common';
import {
  pageKeys,
  type Block,
  type BlockType,
  type FormDetail,
  type FormListItem,
  type PageAnalytics,
  type PageDetail,
  type PublicForm,
  type Variant,
  type VariantKey,
  type VersionInfo,
} from './api';
import { blockLabels, blockSummary, blockTypes, createBlock, move } from './blockDefaults';
import { BlockInspector } from './BlockInspector';
import { BlockRenderer } from './BlockRenderer';
import './pages.css';

interface Draft {
  name: string;
  slug: string;
  metaTitle: string;
  metaDescription: string;
  ogImageUrl: string;
  noIndex: boolean;
  experimentEnabled: boolean;
  variants: Variant[];
}

const fromDetail = (p: PageDetail): Draft => ({
  name: p.name,
  slug: p.slug,
  metaTitle: p.metaTitle ?? '',
  metaDescription: p.metaDescription ?? '',
  ogImageUrl: p.ogImageUrl ?? '',
  noIndex: p.noIndex,
  experimentEnabled: p.experimentEnabled,
  variants: p.variants,
});

export function PageBuilderPage() {
  const { pageId = '' } = useParams();
  const page = useQuery({ queryKey: pageKeys.page(pageId), queryFn: () => api.get<PageDetail>(`/agency/pages/landing-pages/${pageId}`) });
  if (page.isError) return <ErrorState error={page.error} onRetry={() => void page.refetch()} />;
  if (!page.data)
    return (
      <>
        <PageHeader title="Landing page" breadcrumbs={[{ label: 'Landing pages', to: '/agency/pages' }, { label: 'Loading…' }]} />
        <Skeleton height="20rem" />
      </>
    );
  return <PageBuilder key={page.data.id} page={page.data} />;
}

function PageBuilder({ page: initial }: { page: PageDetail }) {
  const queryClient = useQueryClient();
  const toast = useToast();
  const [page, setPage] = useState(initial);
  const [draft, setDraft] = useState<Draft>(() => fromDetail(initial));
  const [dirty, setDirty] = useState(false);
  const [variantKey, setVariantKey] = useState<VariantKey>('A');
  const [selectedId, setSelectedId] = useState<string | null>(initial.variants[0]?.blocks[0]?.id ?? null);
  const [device, setDevice] = useState<'desktop' | 'mobile'>('desktop');
  const [tab, setTab] = useState('build');
  const [serverErrors, setServerErrors] = useState<Record<string, string[]>>({});

  const variantIndex = Math.max(0, draft.variants.findIndex((v) => v.key === variantKey));
  const variant = draft.variants[variantIndex];
  const blocks = variant.blocks;

  const forms = useQuery({
    queryKey: pageKeys.forms({ clientId: page.clientAccountId, status: 'Active' }),
    queryFn: () => api.get<PagedResult<FormListItem>>('/agency/pages/forms', { query: { clientId: page.clientAccountId, status: 'Active', pageSize: 200 } }),
  });
  const formIds = useMemo(
    () => [...new Set(draft.variants.flatMap((v) => v.blocks).filter((b) => b.type === 'form').map((b) => (b.type === 'form' ? b.props.formId : '')).filter(Boolean))],
    [draft.variants],
  );
  const formDetails = useQueries({
    queries: formIds.map((id) => ({ queryKey: pageKeys.form(id), queryFn: () => api.get<FormDetail>(`/agency/pages/forms/${id}`), staleTime: 60_000 })),
  });
  const previewForms: Record<string, PublicForm> = {};
  for (const q of formDetails) {
    const f = q.data;
    if (f)
      previewForms[f.id] = {
        id: f.id, name: f.name, schema: f.schema, submitLabel: f.submitLabel, successMessage: f.successMessage, redirectUrl: null,
        consentText: f.consentText, consentVersion: f.consentVersion, captcha: null, token: '',
      };
  }

  const updateDraft = (patch: Partial<Draft>) => {
    setDraft((d) => ({ ...d, ...patch }));
    setDirty(true);
  };
  const setBlocks = (next: Block[]) => updateDraft({ variants: draft.variants.map((v, i) => (i === variantIndex ? { ...v, blocks: next } : v)) });

  const save = useMutation({
    mutationFn: () =>
      api.put<PageDetail>(`/agency/pages/landing-pages/${page.id}`, {
        name: draft.name,
        slug: draft.slug,
        metaTitle: draft.metaTitle || null,
        metaDescription: draft.metaDescription || null,
        ogImageUrl: draft.ogImageUrl || null,
        noIndex: draft.noIndex,
        experimentEnabled: draft.experimentEnabled,
        variants: draft.variants,
        concurrencyStamp: page.concurrencyStamp,
      }),
    onSuccess: (saved) => {
      setPage(saved);
      setDraft(fromDetail(saved));
      setDirty(false);
      setServerErrors({});
      queryClient.setQueryData(pageKeys.page(saved.id), saved);
    },
    onError: (err) => setServerErrors(fieldErrors(err)),
  });
  const publish = useMutation({
    mutationFn: async () => {
      if (dirty) await save.mutateAsync();
      return api.post<PageDetail>(`/agency/pages/landing-pages/${page.id}/publish`);
    },
    onSuccess: (saved) => {
      setPage(saved);
      setDirty(false);
      toast.success(`Version ${saved.publishedVersion} is live`, saved.publicPath);
      void queryClient.invalidateQueries({ queryKey: pageKeys.versions(saved.id) });
    },
    onError: (err) => {
      if (!isApiError(err) || !err.errors) toast.error('Publish failed', errorMessage(err));
    },
  });
  const unpublish = useMutation({
    mutationFn: () => api.post<PageDetail>(`/agency/pages/landing-pages/${page.id}/unpublish`),
    onSuccess: (saved) => {
      setPage(saved);
      toast.info('Page taken offline');
    },
  });

  const errorCount = Object.keys(serverErrors).length;
  const blockHasError = (index: number) => Object.keys(serverErrors).some((k) => k.startsWith(`variants[${variantIndex}].blocks[${index}]`));

  return (
    <>
      <PageHeader
        title={draft.name}
        description={
          <span>
            {page.clientName} · <code>{page.publicPath}</code>
          </span>
        }
        breadcrumbs={[{ label: 'Landing pages', to: '/agency/pages' }, { label: draft.name }]}
        meta={
          <>
            <Badge tone={page.status === 'Published' ? 'success' : page.status === 'Archived' ? 'neutral' : 'warning'}>{page.status}</Badge>
            {page.publishedVersion && <Badge tone="neutral">Live: v{page.publishedVersion}</Badge>}
            {(dirty || page.hasUnpublishedChanges) && page.status === 'Published' && <Badge tone="info">Unpublished changes</Badge>}
            {draft.experimentEnabled && <Badge tone="brand">A/B test</Badge>}
          </>
        }
        actions={
          <>
            {page.status === 'Published' && (
              <a className="ui-link pb-live-link" href={page.publicPath} target="_blank" rel="noopener noreferrer">
                <ExternalLink aria-hidden="true" /> View live
              </a>
            )}
            <Button variant="secondary" leadingIcon={<Save />} onClick={() => save.mutate()} loading={save.isPending} disabled={!dirty}>
              Save draft
            </Button>
            <Button leadingIcon={<Rocket />} onClick={() => publish.mutate()} loading={publish.isPending}>
              Publish
            </Button>
            {page.status === 'Published' && (
              <Button variant="ghost" onClick={() => unpublish.mutate()} loading={unpublish.isPending}>
                Unpublish
              </Button>
            )}
          </>
        }
      />
      {errorCount > 0 && (
        <Alert tone="danger" title={`The page has ${errorCount} problem${errorCount === 1 ? '' : 's'}`} onDismiss={() => setServerErrors({})}>
          <ul className="pb-error-list">
            {Object.entries(serverErrors).slice(0, 8).map(([key, messages]) => (
              <li key={key}>
                <code>{key}</code>: {messages[0]}
              </li>
            ))}
          </ul>
        </Alert>
      )}
      <Tabs
        label="Page builder"
        value={tab}
        onValueChange={setTab}
        tabs={[
          {
            id: 'build',
            label: 'Build',
            content: (
              <div className="pb-layout">
                <div className="pb-sidebar stack">
                  {draft.variants.length > 1 && (
                    <FormField label="Editing variant">
                      <Select value={variantKey} onChange={(e) => setVariantKey(e.target.value as VariantKey)} options={draft.variants.map((v) => ({ value: v.key, label: `${v.key} — ${v.name}` }))} />
                    </FormField>
                  )}
                  <BlockList
                    blocks={blocks}
                    selectedId={selectedId}
                    onSelect={setSelectedId}
                    onChange={setBlocks}
                    hasError={blockHasError}
                    defaultFormId={forms.data?.items[0]?.id}
                  />
                </div>
                <div className="pb-preview-column">
                  <div className="pb-preview-toolbar" role="group" aria-label="Preview size">
                    <Button size="sm" variant={device === 'desktop' ? 'primary' : 'ghost'} leadingIcon={<Monitor />} aria-pressed={device === 'desktop'} onClick={() => setDevice('desktop')}>
                      Desktop
                    </Button>
                    <Button size="sm" variant={device === 'mobile' ? 'primary' : 'ghost'} leadingIcon={<Smartphone />} aria-pressed={device === 'mobile'} onClick={() => setDevice('mobile')}>
                      Mobile
                    </Button>
                  </div>
                  <div className={`pb-preview pb-preview--${device}`} aria-label={`Live preview (${device})`} role="region">
                    <BlockRenderer blocks={blocks} forms={previewForms} preview selectedId={selectedId} />
                  </div>
                </div>
                <div className="pb-inspector-column">
                  {selectedId && blocks.find((b) => b.id === selectedId) ? (
                    <BlockInspector
                      block={blocks.find((b) => b.id === selectedId)!}
                      forms={forms.data?.items ?? []}
                      errors={serverErrors}
                      prefix={`variants[${variantIndex}].blocks[${blocks.findIndex((b) => b.id === selectedId)}].props`}
                      onChange={(next) => setBlocks(blocks.map((b) => (b.id === next.id ? next : b)))}
                    />
                  ) : (
                    <p className="text-muted">Select a block to edit it.</p>
                  )}
                </div>
              </div>
            ),
          },
          { id: 'settings', label: 'SEO & settings', content: <SettingsPanel draft={draft} onChange={updateDraft} errors={serverErrors} /> },
          {
            id: 'ab',
            label: 'A/B test & results',
            content: (
              <ExperimentPanel
                page={page}
                draft={draft}
                onChange={updateDraft}
                onAddVariant={() => {
                  const used = draft.variants.map((v) => v.key);
                  const key = (['B', 'C', 'D'] as VariantKey[]).find((k) => !used.includes(k));
                  if (!key) return;
                  const copy: Variant = { key, name: `Variant ${key}`, weight: 50, blocks: structuredClone(draft.variants[0].blocks) };
                  updateDraft({ variants: [...draft.variants, copy] });
                  setVariantKey(key);
                  setTab('build');
                }}
                onRemoveVariant={(key) => {
                  updateDraft({ variants: draft.variants.filter((v) => v.key !== key), experimentEnabled: draft.variants.length - 1 > 1 && draft.experimentEnabled });
                  setVariantKey('A');
                }}
                onReset={(saved) => setPage(saved)}
              />
            ),
          },
          { id: 'versions', label: 'Versions', content: <VersionsPanel page={page} onRestored={(saved) => (setPage(saved), setDraft(fromDetail(saved)), setDirty(false))} /> },
        ]}
      />
    </>
  );
}

/** Ordered block list: select, add, remove and reorder (buttons or Alt+↑/↓ on the focused block). */
export function BlockList({
  blocks,
  selectedId,
  onSelect,
  onChange,
  hasError,
  defaultFormId,
}: {
  blocks: Block[];
  selectedId: string | null;
  onSelect: (id: string) => void;
  onChange: (blocks: Block[]) => void;
  hasError?: (index: number) => boolean;
  defaultFormId?: string;
}) {
  const [newType, setNewType] = useState<BlockType>('text');
  const [announcement, setAnnouncement] = useState('');
  const refs = useRef(new Map<string, HTMLButtonElement>());
  const [focusId, setFocusId] = useState<string | null>(null);

  useEffect(() => {
    if (focusId) {
      refs.current.get(focusId)?.focus();
      setFocusId(null);
    }
  }, [focusId, blocks]);

  const moveBlock = (index: number, to: number) => {
    if (to < 0 || to >= blocks.length) return;
    const block = blocks[index];
    onChange(move(blocks, index, to));
    setAnnouncement(`${blockLabels[block.type]} moved to position ${to + 1} of ${blocks.length}.`);
    setFocusId(block.id);
  };

  const onKeyDown = (event: KeyboardEvent<HTMLButtonElement>, index: number) => {
    if (!event.altKey) return;
    if (event.key === 'ArrowUp') {
      event.preventDefault();
      moveBlock(index, index - 1);
    } else if (event.key === 'ArrowDown') {
      event.preventDefault();
      moveBlock(index, index + 1);
    }
  };

  const add = () => {
    const block = createBlock(newType, blocks, defaultFormId);
    const at = selectedId ? blocks.findIndex((b) => b.id === selectedId) + 1 : blocks.length;
    const next = [...blocks.slice(0, at), block, ...blocks.slice(at)];
    onChange(next);
    onSelect(block.id);
    setAnnouncement(`${blockLabels[newType]} block added at position ${at + 1} of ${next.length}.`);
    setFocusId(block.id);
  };

  return (
    <div className="stack pb-blocks">
      <h2 className="pb-panel-title" id="pb-blocks-title">
        Blocks
      </h2>
      <p className="text-small text-muted" id="pb-reorder-hint">
        Tip: focus a block and press Alt+↑ or Alt+↓ to move it.
      </p>
      <ol className="pb-block-list" aria-labelledby="pb-blocks-title">
        {blocks.map((block, index) => (
          <li key={block.id} className={selectedId === block.id ? 'is-selected' : undefined}>
            <button
              type="button"
              ref={(el) => {
                if (el) refs.current.set(block.id, el);
                else refs.current.delete(block.id);
              }}
              className="pb-block-list__select"
              aria-current={selectedId === block.id ? 'true' : undefined}
              aria-describedby="pb-reorder-hint"
              onClick={() => onSelect(block.id)}
              onKeyDown={(e) => onKeyDown(e, index)}
            >
              <span className="pb-block-list__type">
                {index + 1}. {blockLabels[block.type]}
              </span>
              <span className="pb-block-list__summary">{blockSummary(block)}</span>
              {hasError?.(index) && <Badge tone="danger" size="sm">Fix</Badge>}
            </button>
            <span className="pb-block-list__actions">
              <IconButton size="sm" variant="ghost" icon={<ArrowUp />} label={`Move ${blockLabels[block.type]} up`} disabled={index === 0} onClick={() => moveBlock(index, index - 1)} />
              <IconButton size="sm" variant="ghost" icon={<ArrowDown />} label={`Move ${blockLabels[block.type]} down`} disabled={index === blocks.length - 1} onClick={() => moveBlock(index, index + 1)} />
              <IconButton
                size="sm"
                variant="ghost"
                icon={<Trash2 />}
                label={`Remove ${blockLabels[block.type]}`}
                onClick={() => {
                  onChange(blocks.filter((b) => b.id !== block.id));
                  setAnnouncement(`${blockLabels[block.type]} removed.`);
                }}
              />
            </span>
          </li>
        ))}
      </ol>
      <div className="pb-add">
        <FormField label="New block type">
          <Select value={newType} onChange={(e) => setNewType(e.target.value as BlockType)} options={blockTypes.map((t) => ({ value: t, label: blockLabels[t] }))} />
        </FormField>
        <Button leadingIcon={<Plus />} onClick={add}>
          Add block
        </Button>
      </div>
      <div aria-live="polite" role="status" className="visually-hidden">
        {announcement}
      </div>
    </div>
  );
}

function SettingsPanel({ draft, onChange, errors }: { draft: Draft; onChange: (patch: Partial<Draft>) => void; errors: Record<string, string[]> }) {
  const e = (k: string) => errors[k.toLowerCase()]?.[0];
  return (
    <Card>
      <CardBody>
        <div className="stack pb-settings">
          <div className="pb-grid-2">
            <FormField label="Page name" required error={e('name')}>
              <Input value={draft.name} onChange={(ev) => onChange({ name: ev.target.value })} />
            </FormField>
            <FormField label="URL slug" required hint="Lower-case letters, digits and dashes." error={e('slug')}>
              <Input value={draft.slug} onChange={(ev) => onChange({ slug: ev.target.value.toLowerCase() })} />
            </FormField>
          </div>
          <FormField label="SEO title" hint={`${draft.metaTitle.length}/60 characters recommended`} error={e('metaTitle')}>
            <Input value={draft.metaTitle} onChange={(ev) => onChange({ metaTitle: ev.target.value })} />
          </FormField>
          <FormField label="Meta description" hint={`${draft.metaDescription.length}/160 characters recommended`} error={e('metaDescription')}>
            <Textarea rows={3} value={draft.metaDescription} onChange={(ev) => onChange({ metaDescription: ev.target.value })} />
          </FormField>
          <FormField label="Open Graph image URL" optional hint="1200×630; an uploaded image or an allowed https host." error={e('ogImageUrl')}>
            <Input value={draft.ogImageUrl} onChange={(ev) => onChange({ ogImageUrl: ev.target.value })} />
          </FormField>
          <Checkbox label="Hide from search engines (noindex)" description="Use for thank-you pages, paid-only campaigns and tests." checked={draft.noIndex} onChange={(ev) => onChange({ noIndex: ev.target.checked })} />
          <Alert tone="info" title="Tracking and analytics">
            Pages never contain third-party scripts. Views and conversions are measured by the platform; consent-gated analytics tags are configured once in the website settings.
          </Alert>
        </div>
      </CardBody>
    </Card>
  );
}

function ExperimentPanel({
  page,
  draft,
  onChange,
  onAddVariant,
  onRemoveVariant,
  onReset,
}: {
  page: PageDetail;
  draft: Draft;
  onChange: (patch: Partial<Draft>) => void;
  onAddVariant: () => void;
  onRemoveVariant: (key: VariantKey) => void;
  onReset: (saved: PageDetail) => void;
}) {
  const analytics = useQuery({ queryKey: pageKeys.analytics(page.id), queryFn: () => api.get<PageAnalytics>(`/agency/pages/landing-pages/${page.id}/analytics`) });
  const reset = useMutation({
    mutationFn: () => api.post<PageDetail>(`/agency/pages/landing-pages/${page.id}/experiment/reset`),
    onSuccess: onReset,
  });
  const a = analytics.data;
  const columns: DataTableColumn<PageAnalytics['variants'][number]>[] = [
    { id: 'variant', header: 'Variant', primary: true, cell: (v) => `${v.key} — ${v.name}` },
    { id: 'visitors', header: 'Visitors', align: 'right', cell: (v) => v.uniqueVisitors },
    { id: 'subs', header: 'Submissions', align: 'right', cell: (v) => v.submissions },
    { id: 'rate', header: 'Conversion', align: 'right', cell: (v) => fmt.percent(v.conversionRate) },
    { id: 'lift', header: 'Lift vs A', align: 'right', cell: (v) => (v.relativeLift === null ? '—' : fmt.percent(v.relativeLift)) },
    { id: 'p', header: 'p-value', align: 'right', hideOnMobile: true, cell: (v) => (v.pValue === null ? '—' : v.pValue.toFixed(3)) },
    { id: 'note', header: 'Result', cell: (v) => (v.significant ? <Badge tone="success">{v.note}</Badge> : <span className="text-small">{v.note}</span>) },
  ];
  return (
    <div className="stack">
      <Card>
        <CardHeader title="Variants" description="Visitors are split by weight and always see the same variant (sticky by visitor id)." headingLevel={2} />
        <CardBody>
          <div className="stack">
            <Switch
              checked={draft.experimentEnabled}
              onCheckedChange={(checked) => onChange({ experimentEnabled: checked })}
              label="Run an A/B test"
              description={draft.variants.length < 2 ? 'Add a variant first.' : 'Takes effect when you publish.'}
              disabled={draft.variants.length < 2}
            />
            <ul className="pb-variants">
              {draft.variants.map((v, i) => (
                <li key={v.key}>
                  <span className="pb-variant-key">{v.key}</span>
                  <FormField label={`Variant ${v.key} name`} hideLabel>
                    <Input value={v.name} onChange={(e) => onChange({ variants: draft.variants.map((x, j) => (j === i ? { ...x, name: e.target.value } : x)) })} />
                  </FormField>
                  <FormField label={`Variant ${v.key} weight`} hideLabel>
                    <Input type="number" min={1} max={100} value={v.weight} onChange={(e) => onChange({ variants: draft.variants.map((x, j) => (j === i ? { ...x, weight: Number(e.target.value) || 1 } : x)) })} />
                  </FormField>
                  {v.key !== 'A' && <IconButton variant="ghost" icon={<Trash2 />} label={`Remove variant ${v.key}`} onClick={() => onRemoveVariant(v.key)} />}
                </li>
              ))}
            </ul>
            <div className="pb-toolbar">
              <Button variant="secondary" leadingIcon={<Plus />} onClick={onAddVariant} disabled={draft.variants.length >= 4}>
                Add variant (copy of A)
              </Button>
              <Button variant="ghost" leadingIcon={<Undo2 />} onClick={() => reset.mutate()} loading={reset.isPending}>
                Start a fresh experiment
              </Button>
            </div>
          </div>
        </CardBody>
      </Card>
      <Card>
        <CardHeader title="Results (last 30 days)" headingLevel={2} description={a?.method} />
        <CardBody>
          <div className="grid-auto pb-stats">
            <div>
              <p className="text-small text-muted">Visitors</p>
              <p className="pb-metric tabular">{a?.uniqueVisitors ?? '—'}</p>
            </div>
            <div>
              <p className="text-small text-muted">Submissions</p>
              <p className="pb-metric tabular">{a?.submissions ?? '—'}</p>
            </div>
            <div>
              <p className="text-small text-muted">Conversion rate</p>
              <p className="pb-metric tabular">{fmt.percent(a?.conversionRate)}</p>
            </div>
          </div>
          <DataTable caption="Conversion by variant" columns={columns} rows={a?.variants ?? []} getRowId={(v) => v.key} loading={analytics.isLoading} />
        </CardBody>
      </Card>
    </div>
  );
}

function VersionsPanel({ page, onRestored }: { page: PageDetail; onRestored: (saved: PageDetail) => void }) {
  const versions = useQuery({ queryKey: pageKeys.versions(page.id), queryFn: () => api.get<VersionInfo[]>(`/agency/pages/landing-pages/${page.id}/versions`) });
  const restore = useMutation({
    mutationFn: (v: VersionInfo) => api.post<PageDetail>(`/agency/pages/landing-pages/${page.id}/versions/${v.id}/restore`),
    onSuccess: onRestored,
  });
  const columns: DataTableColumn<VersionInfo>[] = [
    { id: 'version', header: 'Version', primary: true, cell: (v) => <>v{v.version} {v.isCurrent && <Badge tone="success">Live</Badge>}</> },
    { id: 'published', header: 'Published', cell: (v) => <DateTime value={v.publishedAt} format="datetime" /> },
    { id: 'hash', header: 'Fingerprint', hideOnMobile: true, cell: (v) => <code>{v.contentHash.slice(0, 12)}</code> },
  ];
  return (
    <DataTable
      caption="Published versions (immutable snapshots)"
      showCaption
      columns={columns}
      rows={versions.data ?? []}
      getRowId={(v) => v.id}
      rowLabel={(v) => `version ${v.version}`}
      loading={versions.isLoading}
      rowActions={(v) => [{ id: 'restore', label: 'Copy into draft', onSelect: () => restore.mutate(v) }]}
    />
  );
}
