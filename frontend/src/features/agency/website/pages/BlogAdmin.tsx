import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Plus } from 'lucide-react';
import { useState, type FormEvent } from 'react';
import { useNavigate, useParams, useSearchParams } from 'react-router-dom';
import { Alert, Badge, Button, ButtonLink, ConfirmDialog, DataTable, Dialog, ErrorState, FilterBar, PageHeader, Skeleton, Tabs, useToast } from '@/components/ui';
import type { Tone } from '@/components/ui';
import { api } from '@/lib/api/client';
import { errorMessage, isApiError } from '@/lib/api/errors';
import { formatDateTime } from '@/lib/format/dates';
import { type BlogCategory, type BlogPost, type BlogPostSummary, type BlogStatus, type Paged, W } from '../api';
import { AreaField, EMPTY_SEO, type Errors, ImageField, MarkdownField, MultiCheck, SelectField, SeoFields, TextField, toErrors } from '../shared/fields';
import { ResourcePage } from '../shared/ResourcePage';
import '../website.css';

export const STATUS_TONE: Record<BlogStatus, Tone> = {
  Draft: 'neutral',
  InReview: 'warning',
  Scheduled: 'info',
  Published: 'success',
  Archived: 'neutral',
};

const STATUS_LABEL: Record<BlogStatus, string> = {
  Draft: 'Draft',
  InReview: 'In review',
  Scheduled: 'Scheduled',
  Published: 'Published',
  Archived: 'Archived',
};

export function BlogStatusBadge({ status }: { status: BlogStatus }) {
  return <Badge tone={STATUS_TONE[status]}>{STATUS_LABEL[status]}</Badge>;
}

function PostsTab() {
  const [params, setParams] = useSearchParams();
  const status = params.get('status') ?? undefined;
  const [search, setSearch] = useState('');
  const query = useQuery({
    queryKey: ['agency', 'website', 'posts', status, search],
    queryFn: () => api.get<Paged<BlogPostSummary>>(`${W}/blog/posts`, { query: { status, search, pageSize: 100 } }),
  });
  return (
    <div className="cms-page">
      <PageHeader
        title="Blog"
        description="Writers draft and submit posts for review; editors with publishing rights publish now or schedule."
        actions={
          <ButtonLink to="new" leadingIcon={<Plus />}>
            New post
          </ButtonLink>
        }
      />
      <FilterBar
        search={search}
        onSearchChange={setSearch}
        searchLabel="Search posts"
        filters={[{ id: 'status', label: 'Status', options: (Object.keys(STATUS_LABEL) as BlogStatus[]).map((s) => ({ value: s, label: STATUS_LABEL[s] })) }]}
        values={{ status }}
        onFilterChange={(_, v) => setParams(v ? { status: v } : {})}
        onReset={() => {
          setSearch('');
          setParams({});
        }}
      />
      {query.isError ? (
        <ErrorState error={query.error} onRetry={() => void query.refetch()} />
      ) : (
        <DataTable
          caption="Blog posts"
          rows={query.data?.items ?? []}
          loading={query.isLoading}
          getRowId={(r) => r.id}
          rowLabel={(r) => r.title}
          columns={[
            { id: 'title', header: 'Title', primary: true, cell: (r) => <ButtonLink to={r.id} variant="link">{r.title}</ButtonLink> },
            { id: 'status', header: 'Status', cell: (r) => <BlogStatusBadge status={r.status} /> },
            { id: 'author', header: 'Author', cell: (r) => r.authorName ?? '—', hideOnMobile: true },
            {
              id: 'when',
              header: 'Publish date',
              hideOnMobile: true,
              cell: (r) => (r.status === 'Scheduled' && r.publishAt ? `Scheduled ${formatDateTime(r.publishAt)}` : r.publishedAt ? formatDateTime(r.publishedAt) : '—'),
            },
          ]}
          emptyState={<p className="text-muted">No posts yet.</p>}
        />
      )}
    </div>
  );
}

type CategoryDraft = Omit<BlogCategory, 'id' | 'updatedAt' | 'concurrencyStamp' | 'postCount'>;

function CategoriesTab() {
  return (
    <ResourcePage<BlogCategory, BlogCategory, CategoryDraft>
      title="Blog categories"
      description="Topics used to group and filter posts. Managing categories needs publishing rights."
      singular="Category"
      queryKey={['agency', 'website', 'blog-categories']}
      list={() => api.get<BlogCategory[]>(`${W}/blog/categories`)}
      getId={(r) => r.id}
      rowLabel={(r) => r.name}
      columns={[
        { id: 'name', header: 'Name', primary: true, cell: (r) => r.name },
        { id: 'slug', header: 'Slug', cell: (r) => <code>{r.slug}</code>, hideOnMobile: true },
        { id: 'posts', header: 'Posts', align: 'right', cell: (r) => r.postCount },
      ]}
      toDraft={(d) => (d ? { ...d } : { slug: '', name: '', description: null, sortOrder: 0 })}
      save={(draft, existing) =>
        existing ? api.put(`${W}/blog/categories/${existing.id}`, { ...draft, concurrencyStamp: existing.concurrencyStamp }) : api.post(`${W}/blog/categories`, draft)
      }
      remove={(r) => api.delete(`${W}/blog/categories/${r.id}`)}
      reorder={(ids) => api.post(`${W}/blog/categories/reorder`, { ids })}
      Form={({ draft, setDraft, errors }) => (
        <>
          <TextField label="Name" required value={draft.name} onChange={(v) => setDraft({ ...draft, name: v })} error={errors.name} />
          <TextField label="Slug" required value={draft.slug} onChange={(v) => setDraft({ ...draft, slug: v })} error={errors.slug} />
          <AreaField label="Description" value={draft.description} onChange={(v) => setDraft({ ...draft, description: v || null })} maxLength={500} />
          <TextField label="Sort order" type="number" value={String(draft.sortOrder)} onChange={(v) => setDraft({ ...draft, sortOrder: Number(v) || 0 })} />
        </>
      )}
    />
  );
}

/** Blog posts and categories. */
export function BlogAdminPage() {
  return (
    <Tabs
      label="Blog"
      tabs={[
        { id: 'posts', label: 'Posts', content: <PostsTab /> },
        { id: 'categories', label: 'Categories', content: <CategoriesTab /> },
      ]}
    />
  );
}

type PostDraft = {
  slug: string;
  title: string;
  excerpt: string;
  bodyMarkdown: string;
  coverImageUrl: string | null;
  coverImageAlt: string | null;
  authorId: string | null;
  categoryIds: string[];
  tags: string[];
  seo: BlogPost['seo'];
};

const toDraft = (p: BlogPost | undefined): PostDraft =>
  p
    ? { slug: p.slug, title: p.title, excerpt: p.excerpt, bodyMarkdown: p.bodyMarkdown, coverImageUrl: p.coverImageUrl, coverImageAlt: p.coverImageAlt, authorId: p.authorId, categoryIds: p.categoryIds, tags: p.tags, seo: p.seo }
    : { slug: '', title: '', excerpt: '', bodyMarkdown: '', coverImageUrl: null, coverImageAlt: null, authorId: null, categoryIds: [], tags: [], seo: EMPTY_SEO };

function toLocalInput(iso: string | null): string {
  if (!iso) return '';
  const d = new Date(iso);
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
}

/** Post editor with Markdown preview and the review → publish workflow (buttons follow the API's `can` flags). */
export function PostEditorPage() {
  const { postId = 'new' } = useParams();
  const isNew = postId === 'new';
  const navigate = useNavigate();
  const toast = useToast();
  const client = useQueryClient();
  const post = useQuery({ queryKey: ['agency', 'website', 'post', postId], queryFn: () => api.get<BlogPost>(`${W}/blog/posts/${postId}`), enabled: !isNew });
  const categories = useQuery({ queryKey: ['agency', 'website', 'blog-categories'], queryFn: () => api.get<BlogCategory[]>(`${W}/blog/categories`) });
  const authors = useQuery({ queryKey: ['agency', 'website', 'authors'], queryFn: () => api.get<{ id: string; name: string; role: string }[]>(`${W}/blog/authors`) });
  const [draft, setDraft] = useState<PostDraft | null>(null);
  const [errors, setErrors] = useState<Errors>({});
  const [scheduleOpen, setScheduleOpen] = useState(false);
  const [publishAt, setPublishAt] = useState('');
  const [returnOpen, setReturnOpen] = useState(false);
  const current = draft ?? toDraft(post.data);
  const set = <K extends keyof PostDraft>(k: K, v: PostDraft[K]) => setDraft({ ...current, [k]: v });

  const afterChange = async (saved: BlogPost, message: string) => {
    toast.success(message);
    setDraft(null);
    setErrors({});
    client.setQueryData(['agency', 'website', 'post', saved.id], saved);
    await client.invalidateQueries({ queryKey: ['agency', 'website', 'posts'] });
  };

  const save = useMutation({
    mutationFn: () =>
      isNew
        ? api.post<BlogPost>(`${W}/blog/posts`, current)
        : api.put<BlogPost>(`${W}/blog/posts/${postId}`, { ...current, concurrencyStamp: post.data!.concurrencyStamp }),
    onSuccess: async (saved) => {
      await afterChange(saved, 'Post saved');
      if (isNew) navigate(`../blog/${saved.id}`, { replace: true, relative: 'path' });
    },
    onError: (e) => isApiError(e) && setErrors(toErrors(e.errors)),
  });

  const action = useMutation({
    mutationFn: ({ name, body }: { name: string; body?: Record<string, unknown> }) =>
      api.post<BlogPost>(`${W}/blog/posts/${postId}/${name}`, { concurrencyStamp: post.data!.concurrencyStamp, ...body }),
    onSuccess: (saved, { name }) =>
      afterChange(saved, { submit: 'Submitted for review', publish: 'Published', schedule: 'Scheduled', unpublish: 'Unpublished', 'return-to-draft': 'Returned to draft' }[name] ?? 'Updated'),
    onError: (e) => {
      if (isApiError(e)) setErrors(toErrors(e.errors));
      toast.error("Couldn't update the post", errorMessage(e));
    },
  });

  if (!isNew && post.isLoading) return <Skeleton height={320} />;
  if (!isNew && post.isError) return <ErrorState error={post.error} />;
  const can = post.data?.can;
  const dirty = !!draft;

  const submit = (e: FormEvent) => {
    e.preventDefault();
    save.mutate();
  };

  return (
    <div className="cms-page">
      <PageHeader
        title={isNew ? 'New post' : current.title || 'Untitled post'}
        breadcrumbs={[{ label: 'Blog', to: '..' }, { label: isNew ? 'New post' : 'Edit' }]}
        meta={post.data && <BlogStatusBadge status={post.data.status} />}
        actions={
          post.data && (
            <div className="cms-toolbar">
              {can?.submit && (
                <Button variant="secondary" disabled={dirty} onClick={() => action.mutate({ name: 'submit' })}>
                  Submit for review
                </Button>
              )}
              {can?.returnToDraft && post.data.status !== 'Draft' && (
                <Button variant="ghost" disabled={dirty} onClick={() => setReturnOpen(true)}>
                  Return to draft
                </Button>
              )}
              {can?.schedule && (
                <Button variant="secondary" disabled={dirty} onClick={() => setScheduleOpen(true)}>
                  Schedule
                </Button>
              )}
              {can?.unpublish && (
                <Button variant="ghost" disabled={dirty} onClick={() => action.mutate({ name: 'unpublish' })}>
                  Unpublish
                </Button>
              )}
              {can?.publish && (
                <Button variant="highlight" disabled={dirty} loading={action.isPending} onClick={() => action.mutate({ name: 'publish' })}>
                  Publish now
                </Button>
              )}
            </div>
          )
        }
      />
      {dirty && post.data && <Alert tone="info">Save your changes before changing the post's status.</Alert>}
      {post.data?.status === 'Scheduled' && post.data.publishAt && (
        <Alert tone="info" title="Scheduled">
          This post goes live on {formatDateTime(post.data.publishAt)}.
        </Alert>
      )}
      {post.data && !post.data.can.edit && (
        <Alert tone="warning" title="Read only">
          This post is live or scheduled. Only editors with publishing rights can change it.
        </Alert>
      )}
      {errors.bodyMarkdown && action.isError && <Alert tone="danger">{errors.bodyMarkdown}</Alert>}
      <form className="cms-form" onSubmit={submit} noValidate aria-label="Post editor">
        <fieldset disabled={post.data ? !post.data.can.edit : false} className="cms-form" style={{ border: 0, padding: 0, margin: 0 }}>
          <div className="cms-grid-2">
            <TextField label="Title" required value={current.title} onChange={(v) => set('title', v)} maxLength={180} error={errors.title} />
            <TextField label="Slug" required value={current.slug} onChange={(v) => set('slug', v)} maxLength={120} error={errors.slug} hint="URL: /blog/slug" />
          </div>
          <AreaField label="Excerpt" required value={current.excerpt} onChange={(v) => set('excerpt', v)} maxLength={500} rows={2} error={errors.excerpt} />
          <MarkdownField label="Body" required value={current.bodyMarkdown} onChange={(v) => set('bodyMarkdown', v)} rows={18} error={errors.bodyMarkdown} />
          <div className="cms-grid-2">
            <SelectField
              label="Author"
              value={current.authorId}
              onChange={(v) => set('authorId', v || null)}
              placeholder="No byline"
              options={(authors.data ?? []).map((a) => ({ value: a.id, label: `${a.name} — ${a.role}` }))}
              error={errors.authorId}
            />
            <TextField label="Tags" value={current.tags.join(', ')} onChange={(v) => set('tags', v.split(',').map((t) => t.trim()).filter(Boolean))} hint="Comma separated." error={errors.tags} />
          </div>
          <MultiCheck
            legend="Categories"
            options={(categories.data ?? []).map((c) => ({ value: c.id, label: c.name }))}
            value={current.categoryIds}
            onChange={(v) => set('categoryIds', v)}
            error={errors.categoryIds}
          />
          <ImageField label="Cover image" value={current.coverImageUrl} onChange={(v) => set('coverImageUrl', v || null)} error={errors.coverImageUrl} />
          <TextField label="Cover image description (alt text)" value={current.coverImageAlt} onChange={(v) => set('coverImageAlt', v || null)} error={errors.coverImageAlt} />
          <SeoFields value={current.seo} onChange={(v) => set('seo', v)} errors={errors} />
          {save.isError && !Object.keys(errors).length && (
            <Alert tone="danger" title="Couldn't save">
              {errorMessage(save.error)}
            </Alert>
          )}
          <div className="cms-form__actions">
            <Button type="submit" loading={save.isPending}>
              {isNew ? 'Create draft' : 'Save'}
            </Button>
            {post.data?.can.delete && <DeletePost id={post.data.id} />}
          </div>
        </fieldset>
      </form>

      <Dialog
        open={scheduleOpen}
        onClose={() => setScheduleOpen(false)}
        title="Schedule post"
        description="The post goes live automatically at this time (your local time)."
        footer={
          <>
            <Button variant="ghost" onClick={() => setScheduleOpen(false)}>
              Cancel
            </Button>
            <Button
              disabled={!publishAt}
              onClick={() => {
                action.mutate({ name: 'schedule', body: { publishAt: new Date(publishAt).toISOString() } });
                setScheduleOpen(false);
              }}
            >
              Schedule
            </Button>
          </>
        }
      >
        <TextField label="Publish at" type="datetime-local" value={publishAt || toLocalInput(post.data?.publishAt ?? null)} onChange={setPublishAt} error={errors.publishAt} />
      </Dialog>
      <ConfirmDialog
        open={returnOpen}
        onClose={() => setReturnOpen(false)}
        title="Return this post to draft?"
        description="The writer can edit it again and resubmit. Add a note so they know what to change."
        requireReason
        reasonLabel="Note for the writer"
        confirmLabel="Return to draft"
        onConfirm={async ({ reason }) => {
          await action.mutateAsync({ name: 'return-to-draft', body: { note: reason } });
          setReturnOpen(false);
        }}
      />
    </div>
  );
}

function DeletePost({ id }: { id: string }) {
  const [open, setOpen] = useState(false);
  const navigate = useNavigate();
  const client = useQueryClient();
  return (
    <>
      <Button type="button" variant="danger" onClick={() => setOpen(true)}>
        Delete
      </Button>
      <ConfirmDialog
        open={open}
        onClose={() => setOpen(false)}
        title="Delete this post?"
        description="This can't be undone."
        tone="danger"
        confirmLabel="Delete post"
        onConfirm={async () => {
          await api.delete(`${W}/blog/posts/${id}`);
          await client.invalidateQueries({ queryKey: ['agency', 'website', 'posts'] });
          navigate('..', { relative: 'path' });
        }}
      />
    </>
  );
}
