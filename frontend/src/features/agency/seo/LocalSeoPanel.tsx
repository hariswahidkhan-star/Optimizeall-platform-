import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Check, Pencil, Plus, Star, Trash2, X } from 'lucide-react';
import { useState, type FormEvent } from 'react';
import {
  Alert,
  Badge,
  Button,
  Card,
  CardBody,
  CardHeader,
  Checkbox,
  ConfirmDialog,
  DataTable,
  DateTime,
  Dialog,
  FormField,
  Input,
  ProgressBar,
  Select,
  Stat,
  Textarea,
  useToast,
  type DataTableColumn,
} from '@/components/ui';
import { SafeExternalLink } from '@/components/SafeExternalLink';
import { api } from '@/lib/api/client';
import { errorMessage } from '@/lib/api/errors';
import { seoKeys, type Citation, type CitationStatus, type LocalSeo, type Review, type Site } from './api';
import { fieldErrors, firstError, fmt } from './common';

const citationStatuses: CitationStatus[] = ['NotStarted', 'Submitted', 'Live', 'NeedsUpdate', 'Rejected'];
const citationLabel: Record<CitationStatus, string> = {
  NotStarted: 'Not started',
  Submitted: 'Submitted',
  Live: 'Live',
  NeedsUpdate: 'Needs update',
  Rejected: 'Rejected',
};

export function LocalSeoPanel({ site }: { site: Site }) {
  const queryClient = useQueryClient();
  const toast = useToast();
  const local = useQuery({ queryKey: seoKeys.local(site.id), queryFn: () => api.get<LocalSeo>(`/agency/seo/sites/${site.id}/local`) });
  const reviews = useQuery({ queryKey: ['agency', 'seo', 'site', site.id, 'reviews'], queryFn: () => api.get<Review[]>(`/agency/seo/sites/${site.id}/reviews`) });
  const [editing, setEditing] = useState<Citation | null>(null);
  const [addingReview, setAddingReview] = useState<Review | 'new' | null>(null);
  const [deletingReview, setDeletingReview] = useState<Review | null>(null);
  const refresh = () => void queryClient.invalidateQueries({ queryKey: ['agency', 'seo', 'site', site.id] });
  const data = local.data;
  const [profile, setProfile] = useState<{ businessName: string; address: string; phone: string; website: string; done: string[] } | null>(null);
  const current = profile ?? {
    businessName: data?.profile?.businessName ?? '',
    address: data?.profile?.address ?? '',
    phone: data?.profile?.phone ?? '',
    website: data?.profile?.website ?? site.baseUrl,
    done: data?.checklist.filter((c) => c.done).map((c) => c.key) ?? [],
  };
  const saveProfile = useMutation({
    mutationFn: () =>
      api.put<LocalSeo>(`/agency/seo/sites/${site.id}/local/profile`, {
        businessName: current.businessName,
        address: current.address || null,
        phone: current.phone || null,
        website: current.website || null,
        completedChecklist: current.done,
        concurrencyStamp: data?.profile?.concurrencyStamp,
      }),
    onSuccess: () => {
      toast.success('Local profile saved');
      setProfile(null);
      refresh();
    },
    onError: (err) => toast.error('Could not save', errorMessage(err)),
  });

  const columns: DataTableColumn<Citation>[] = [
    {
      id: 'directory',
      header: 'Directory',
      primary: true,
      cell: (c) => (
        <span className="stack seo-stack-xs">
          <span className="seo-strong">{c.name}</span>
          <span className="text-small text-muted">
            {c.category}
            {c.countries.length > 0 && ` · ${c.countries.join(', ')}`}
          </span>
        </span>
      ),
    },
    { id: 'status', header: 'Status', cell: (c) => <Badge tone={c.status === 'Live' ? 'success' : c.status === 'NeedsUpdate' ? 'warning' : c.status === 'Rejected' ? 'danger' : 'neutral'}>{citationLabel[c.status]}</Badge> },
    {
      id: 'nap',
      header: 'NAP',
      cell: (c) =>
        c.status === 'NotStarted' ? (
          '—'
        ) : (
          <span className="seo-nap">
            <Nap label="Name" value={c.nameMatches} />
            <Nap label="Address" value={c.addressMatches} />
            <Nap label="Phone" value={c.phoneMatches} />
          </span>
        ),
    },
    {
      id: 'listing',
      header: 'Listing',
      hideOnMobile: true,
      cell: (c) => (c.listingUrl ? <SafeExternalLink href={c.listingUrl}>View listing</SafeExternalLink> : '—'),
    },
  ];

  return (
    <div className="stack">
      <div className="grid-auto seo-stats">
        <Stat label="Profile checklist" value={data ? `${data.checklistDone}/${data.checklist.length}` : '—'} measurement="Count" loading={local.isLoading} />
        <Stat label="Live citations" value={data?.citationsLive ?? '—'} measurement="Count" loading={local.isLoading} />
        <Stat label="Inconsistent NAP" value={data?.citationsInconsistent ?? '—'} measurement="Count" loading={local.isLoading} />
        <Stat label="Average rating" value={data?.reviews.averageRating ?? '—'} measurement="Measured" loading={local.isLoading} hint={`${data?.reviews.count ?? 0} reviews · ${fmt.percent(data?.reviews.responseRate)} answered`} />
      </div>

      <div className="seo-grid-2">
        <Card>
          <CardHeader title="Business NAP" description="Canonical name, address and phone — every listing is compared with this." headingLevel={3} />
          <CardBody>
            <form className="stack" onSubmit={(e: FormEvent) => (e.preventDefault(), saveProfile.mutate())}>
              <FormField label="Business name" required>
                <Input value={current.businessName} onChange={(e) => setProfile({ ...current, businessName: e.target.value })} />
              </FormField>
              <FormField label="Address" optional>
                <Textarea rows={2} value={current.address} onChange={(e) => setProfile({ ...current, address: e.target.value })} />
              </FormField>
              <div className="seo-grid-2">
                <FormField label="Phone" optional>
                  <Input type="tel" value={current.phone} onChange={(e) => setProfile({ ...current, phone: e.target.value })} />
                </FormField>
                <FormField label="Website" optional>
                  <Input value={current.website} onChange={(e) => setProfile({ ...current, website: e.target.value })} />
                </FormField>
              </div>
              <Button type="submit" loading={saveProfile.isPending} disabled={!current.businessName.trim()}>
                Save profile & checklist
              </Button>
            </form>
          </CardBody>
        </Card>
        <Card>
          <CardHeader title="Google Business Profile checklist" headingLevel={3} />
          <CardBody>
            {data && <ProgressBar value={current.done.length} max={data.checklist.length} label="Checklist progress" />}
            <ul className="seo-checklist">
              {data?.checklist.map((item) => (
                <li key={item.key}>
                  <Checkbox
                    label={item.title}
                    description={item.guidance}
                    checked={current.done.includes(item.key)}
                    onChange={(e) =>
                      setProfile({ ...current, done: e.target.checked ? [...current.done, item.key] : current.done.filter((k) => k !== item.key) })
                    }
                  />
                </li>
              ))}
            </ul>
          </CardBody>
        </Card>
      </div>

      <DataTable
        caption="Citations"
        columns={columns}
        rows={data?.citations ?? []}
        getRowId={(c) => c.sourceId}
        rowLabel={(c) => c.name}
        loading={local.isLoading}
        rowActions={(c) => [{ id: 'edit', label: 'Update listing', onSelect: () => setEditing(c) }]}
      />

      <Card>
        <CardHeader
          title="Reviews"
          headingLevel={3}
          actions={
            <Button size="sm" variant="secondary" leadingIcon={<Plus />} onClick={() => setAddingReview('new')}>
              Log review
            </Button>
          }
        />
        <CardBody>
          {(reviews.data ?? []).length === 0 ? (
            <p className="text-muted">No reviews logged yet.</p>
          ) : (
            <ul className="seo-reviews">
              {reviews.data!.map((r) => (
                <li key={r.id}>
                  <div className="seo-review__head">
                    <span className="seo-stars" role="img" aria-label={`${r.rating} out of 5 stars`}>
                      {Array.from({ length: 5 }, (_, i) => (
                        <Star key={i} aria-hidden="true" className={i < r.rating ? 'is-on' : undefined} />
                      ))}
                    </span>
                    <span className="seo-strong">{r.authorName ?? 'Anonymous'}</span>
                    <span className="text-small text-muted">
                      {r.platform} · <DateTime value={r.reviewedAt} format="date" />
                    </span>
                    {r.responded ? <Badge tone="success">Responded</Badge> : <Badge tone="warning">Needs a reply</Badge>}
                    <span className="seo-toolbar">
                      <Button size="sm" variant="ghost" leadingIcon={<Pencil />} onClick={() => setAddingReview(r)} aria-label={`Edit review by ${r.authorName ?? 'Anonymous'}`}>
                        {r.responded ? 'Edit' : 'Reply / edit'}
                      </Button>
                      <Button size="sm" variant="ghost" leadingIcon={<Trash2 />} onClick={() => setDeletingReview(r)} aria-label={`Delete review by ${r.authorName ?? 'Anonymous'}`}>
                        Delete
                      </Button>
                    </span>
                  </div>
                  {r.text && <p>{r.text}</p>}
                  {r.responseText && <p className="text-small text-muted">Our reply: {r.responseText}</p>}
                </li>
              ))}
            </ul>
          )}
        </CardBody>
      </Card>
      {editing && <CitationDialog siteId={site.id} citation={editing} onClose={() => setEditing(null)} onSaved={refresh} />}
      {addingReview && (
        <ReviewDialog siteId={site.id} review={addingReview === 'new' ? null : addingReview} onClose={() => setAddingReview(null)} onSaved={refresh} />
      )}
      <ConfirmDialog
        open={deletingReview !== null}
        onClose={() => setDeletingReview(null)}
        tone="danger"
        title="Delete this review?"
        description="It is removed from the review log and the rating summary."
        confirmLabel="Delete review"
        onConfirm={async () => {
          if (!deletingReview) return;
          await api.delete(`/agency/seo/reviews/${deletingReview.id}`);
          toast.success('Review deleted');
          refresh();
        }}
      />
    </div>
  );
}

function Nap({ label, value }: { label: string; value: boolean | null }) {
  if (value === null) return <span className="seo-nap__item text-muted">{label}: —</span>;
  return (
    <span className={`seo-nap__item ${value ? 'seo-up' : 'seo-down'}`}>
      {value ? <Check aria-hidden="true" /> : <X aria-hidden="true" />}
      {label}
      <span className="visually-hidden">{value ? ' matches' : ' does not match'}</span>
    </span>
  );
}

function CitationDialog({ siteId, citation, onClose, onSaved }: { siteId: string; citation: Citation; onClose: () => void; onSaved: () => void }) {
  const [form, setForm] = useState({
    status: citation.status,
    listingUrl: citation.listingUrl ?? '',
    listedName: citation.listedName ?? '',
    listedAddress: citation.listedAddress ?? '',
    listedPhone: citation.listedPhone ?? '',
    notes: citation.notes ?? '',
  });
  const save = useMutation({
    mutationFn: () =>
      api.put(`/agency/seo/sites/${siteId}/local/citations/${citation.sourceId}`, {
        status: form.status,
        listingUrl: form.listingUrl || null,
        listedName: form.listedName || null,
        listedAddress: form.listedAddress || null,
        listedPhone: form.listedPhone || null,
        notes: form.notes || null,
      }),
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
      title={`Listing on ${citation.name}`}
      description="Enter what the directory shows; mismatches with your canonical NAP are flagged."
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" form="seo-citation" loading={save.isPending}>
            Save
          </Button>
        </>
      }
    >
      <form id="seo-citation" className="stack" onSubmit={(e: FormEvent) => (e.preventDefault(), save.mutate())}>
        {save.isError && <Alert tone="danger" title="Could not save">{errorMessage(save.error)}</Alert>}
        <FormField label="Status">
          <Select value={form.status} onChange={(e) => setForm({ ...form, status: e.target.value as CitationStatus })} options={citationStatuses.map((s) => ({ value: s, label: citationLabel[s] }))} />
        </FormField>
        <FormField label="Listing URL" optional error={firstError(errors, 'listingUrl')}>
          <Input value={form.listingUrl} inputMode="url" onChange={(e) => setForm({ ...form, listingUrl: e.target.value })} />
        </FormField>
        <FormField label="Name on the listing" optional>
          <Input value={form.listedName} onChange={(e) => setForm({ ...form, listedName: e.target.value })} />
        </FormField>
        <FormField label="Address on the listing" optional>
          <Input value={form.listedAddress} onChange={(e) => setForm({ ...form, listedAddress: e.target.value })} />
        </FormField>
        <FormField label="Phone on the listing" optional>
          <Input value={form.listedPhone} onChange={(e) => setForm({ ...form, listedPhone: e.target.value })} />
        </FormField>
        <FormField label="Notes" optional>
          <Textarea rows={2} value={form.notes} onChange={(e) => setForm({ ...form, notes: e.target.value })} />
        </FormField>
      </form>
    </Dialog>
  );
}

function ReviewDialog({ siteId, review, onClose, onSaved }: { siteId: string; review: Review | null; onClose: () => void; onSaved: () => void }) {
  const [form, setForm] = useState({
    platform: review?.platform ?? 'Google',
    rating: String(review?.rating ?? 5),
    authorName: review?.authorName ?? '',
    text: review?.text ?? '',
    reviewedAt: (review?.reviewedAt ?? new Date().toISOString()).slice(0, 10),
    responseText: review?.responseText ?? '',
  });
  const save = useMutation({
    mutationFn: () =>
      (review ? api.put : api.post)(review ? `/agency/seo/reviews/${review.id}` : `/agency/seo/sites/${siteId}/reviews`, {
        platform: form.platform,
        rating: Number(form.rating),
        authorName: form.authorName || null,
        text: form.text || null,
        reviewedAt: `${form.reviewedAt}T12:00:00Z`,
        responded: !!form.responseText,
        responseText: form.responseText || null,
      }),
    onSuccess: () => {
      onSaved();
      onClose();
    },
  });
  return (
    <Dialog
      open
      onClose={onClose}
      title={review ? 'Edit review' : 'Log a review'}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" form="seo-review" loading={save.isPending}>
            Save
          </Button>
        </>
      }
    >
      <form id="seo-review" className="stack" onSubmit={(e: FormEvent) => (e.preventDefault(), save.mutate())}>
        {save.isError && <Alert tone="danger" title="Could not save">{errorMessage(save.error)}</Alert>}
        <div className="seo-grid-2">
          <FormField label="Platform" required>
            <Input value={form.platform} onChange={(e) => setForm({ ...form, platform: e.target.value })} />
          </FormField>
          <FormField label="Rating" required>
            <Select value={form.rating} onChange={(e) => setForm({ ...form, rating: e.target.value })} options={['5', '4', '3', '2', '1'].map((r) => ({ value: r, label: `${r} stars` }))} />
          </FormField>
        </div>
        <div className="seo-grid-2">
          <FormField label="Reviewer" optional>
            <Input value={form.authorName} onChange={(e) => setForm({ ...form, authorName: e.target.value })} />
          </FormField>
          <FormField label="Date" required>
            <Input type="date" value={form.reviewedAt} onChange={(e) => setForm({ ...form, reviewedAt: e.target.value })} />
          </FormField>
        </div>
        <FormField label="Review text" optional>
          <Textarea rows={3} value={form.text} onChange={(e) => setForm({ ...form, text: e.target.value })} />
        </FormField>
        <FormField label="Our response" optional hint="Leave empty if the review still needs a reply.">
          <Textarea rows={2} value={form.responseText} onChange={(e) => setForm({ ...form, responseText: e.target.value })} />
        </FormField>
      </form>
    </Dialog>
  );
}
