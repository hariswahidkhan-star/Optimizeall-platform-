import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Pencil, Plus, Trash2 } from 'lucide-react';
import { useState } from 'react';
import { Alert, Badge, Button, Dialog, Tabs, useToast } from '@/components/ui';
import { api } from '@/lib/api/client';
import { errorMessage, isApiError } from '@/lib/api/errors';
import { formatMoney } from '@/lib/format/money';
import { type Package, type Paged, type Service, type ServiceCategory, type ServiceSummary, W } from '../api';
import {
  AreaField,
  EMPTY_SEO,
  type Errors,
  errorFor,
  ImageField,
  LinesField,
  ListEditor,
  MarkdownField,
  MultiCheck,
  SelectField,
  SeoFields,
  SwitchField,
  TextField,
  toErrors,
} from '../shared/fields';
import { ResourcePage } from '../shared/ResourcePage';
import { ICON_OPTIONS, useServiceOptions } from './ContentPages';

type ServiceDraft = Omit<Service, 'createdAt' | 'updatedAt' | 'concurrencyStamp' | 'id'> & { id: string | null };

const PERIODS = [
  { value: 'Monthly', label: 'Monthly' },
  { value: 'Quarterly', label: 'Quarterly' },
  { value: 'Yearly', label: 'Yearly' },
  { value: 'OneTime', label: 'One-time' },
];

interface PackageDraft {
  name: string;
  description: string;
  price: string;
  currency: string;
  billingPeriod: string;
  setupFee: string;
  features: string[];
  isMostPopular: boolean;
  isCustomQuote: boolean;
  isActive: boolean;
  sortOrder: number;
}

function PackagesEditor({ serviceId }: { serviceId: string }) {
  const client = useQueryClient();
  const toast = useToast();
  const detail = useQuery({ queryKey: ['agency', 'website', 'service', serviceId], queryFn: () => api.get<Service>(`${W}/services/${serviceId}`) });
  const [editing, setEditing] = useState<{ pkg: Package | null; draft: PackageDraft } | null>(null);
  const [errors, setErrors] = useState<Errors>({});
  const save = useMutation({
    mutationFn: () => {
      const d = editing!.draft;
      const body = {
        ...d,
        price: d.isCustomQuote || d.price.trim() === '' ? null : Number(d.price),
        setupFee: d.setupFee.trim() === '' ? null : Number(d.setupFee),
        currency: d.currency.toUpperCase(),
        concurrencyStamp: editing!.pkg?.concurrencyStamp,
      };
      return editing!.pkg
        ? api.put(`${W}/services/${serviceId}/packages/${editing!.pkg.id}`, body)
        : api.post(`${W}/services/${serviceId}/packages`, body);
    },
    onSuccess: async () => {
      toast.success('Package saved');
      setEditing(null);
      await client.invalidateQueries({ queryKey: ['agency', 'website', 'service', serviceId] });
    },
    onError: (e) => isApiError(e) && setErrors(toErrors(e.errors)),
  });
  const remove = useMutation({
    mutationFn: (pkg: Package) => api.delete(`${W}/services/${serviceId}/packages/${pkg.id}`),
    onSuccess: () => client.invalidateQueries({ queryKey: ['agency', 'website', 'service', serviceId] }),
    onError: (e) => toast.error("Couldn't delete the package", errorMessage(e)),
  });

  const open = (pkg: Package | null) => {
    setErrors({});
    setEditing({
      pkg,
      draft: pkg
        ? {
            name: pkg.name, description: pkg.description ?? '', price: pkg.price?.toString() ?? '', currency: pkg.currency, billingPeriod: pkg.billingPeriod,
            setupFee: pkg.setupFee?.toString() ?? '', features: pkg.features, isMostPopular: pkg.isMostPopular, isCustomQuote: pkg.isCustomQuote,
            isActive: pkg.isActive, sortOrder: pkg.sortOrder,
          }
        : { name: '', description: '', price: '', currency: 'USD', billingPeriod: 'Monthly', setupFee: '', features: [], isMostPopular: false, isCustomQuote: false, isActive: true, sortOrder: 0 },
    });
  };
  const d = editing?.draft;
  const set = <K extends keyof PackageDraft>(k: K, v: PackageDraft[K]) => setEditing((e) => (e ? { ...e, draft: { ...e.draft, [k]: v } } : e));

  return (
    <fieldset className="cms-fieldset">
      <legend>Packages &amp; pricing</legend>
      <p className="text-small text-muted">Packages are referenced by proposals and invoices. Deactivate a package rather than deleting it once it has been sold.</p>
      <ul className="cms-list">
        {(detail.data?.packages ?? []).map((p) => (
          <li key={p.id} className="cms-list__item">
            <div className="cms-list__body">
              <strong>
                {p.name} {p.isMostPopular && <Badge tone="brand">Most popular</Badge>} {!p.isActive && <Badge>Inactive</Badge>}
              </strong>
              <span className="text-small">
                {p.isCustomQuote || p.price === null ? 'Custom quote' : `${formatMoney(p.price, p.currency)} ${p.billingPeriod === 'OneTime' ? 'one-time' : `/ ${p.billingPeriod.toLowerCase().replace('ly', '')}`}`}
              </span>
            </div>
            <div className="cms-row-actions">
              <Button size="sm" variant="secondary" leadingIcon={<Pencil />} onClick={() => open(p)}>
                Edit<span className="visually-hidden"> {p.name}</span>
              </Button>
              <Button size="sm" variant="ghost" leadingIcon={<Trash2 />} onClick={() => remove.mutate(p)}>
                <span className="visually-hidden">Delete {p.name}</span>
              </Button>
            </div>
          </li>
        ))}
      </ul>
      <div>
        <Button size="sm" variant="secondary" leadingIcon={<Plus />} onClick={() => open(null)}>
          Add package
        </Button>
      </div>
      <Dialog
        open={!!editing}
        onClose={() => setEditing(null)}
        title={editing?.pkg ? 'Edit package' : 'New package'}
        size="lg"
        footer={
          <>
            <Button variant="ghost" onClick={() => setEditing(null)}>
              Cancel
            </Button>
            <Button onClick={() => save.mutate()} loading={save.isPending}>
              Save package
            </Button>
          </>
        }
      >
        {d && (
          <div className="cms-form">
            <div className="cms-grid-2">
              <TextField label="Name" required value={d.name} onChange={(v) => set('name', v)} error={errors.name} />
              <SelectField label="Billing" required value={d.billingPeriod} onChange={(v) => set('billingPeriod', v)} options={PERIODS} error={errors.billingPeriod} />
              <TextField label="Price" type="number" value={d.price} onChange={(v) => set('price', v)} error={errors.price} hint="Leave empty for a custom quote." />
              <TextField label="Currency" required value={d.currency} onChange={(v) => set('currency', v)} maxLength={3} error={errors.currency} />
              <TextField label="Setup fee" type="number" value={d.setupFee} onChange={(v) => set('setupFee', v)} error={errors.setupFee} />
              <TextField label="Sort order" type="number" value={String(d.sortOrder)} onChange={(v) => set('sortOrder', Number(v) || 0)} />
            </div>
            <AreaField label="Description" value={d.description} onChange={(v) => set('description', v)} maxLength={500} rows={2} />
            <LinesField label="Included features" value={d.features} onChange={(v) => set('features', v)} error={errorFor(errors, 'features')} />
            <SwitchField label="Custom quote (no fixed price)" checked={d.isCustomQuote} onChange={(v) => set('isCustomQuote', v)} />
            <SwitchField label="Most popular" checked={d.isMostPopular} onChange={(v) => set('isMostPopular', v)} description="Only one package per service is highlighted." />
            <SwitchField label="Active" checked={d.isActive} onChange={(v) => set('isActive', v)} />
            {save.isError && !Object.keys(errors).length && <Alert tone="danger" title="Couldn't save">{errorMessage(save.error)}</Alert>}
          </div>
        )}
      </Dialog>
    </fieldset>
  );
}

function ServicesTab() {
  const categories = useQuery({ queryKey: ['agency', 'website', 'categories'], queryFn: () => api.get<ServiceCategory[]>(`${W}/service-categories`) });
  const services = useServiceOptions();
  return (
    <ResourcePage<ServiceSummary, Service, ServiceDraft>
      title="Services"
      description="Every service on the website, with packages. Unpublished services and services in unpublished categories are hidden."
      singular="Service"
      queryKey={['agency', 'website', 'service-options']}
      list={async () => (await api.get<Paged<ServiceSummary>>(`${W}/services`, { query: { pageSize: 200 } })).items}
      getId={(r) => r.id}
      rowLabel={(r) => r.name}
      load={(r) => api.get<Service>(`${W}/services/${r.id}`)}
      publicUrl={(r) => (r.isPublished ? `/services/${r.slug}` : null)}
      columns={[
        { id: 'name', header: 'Service', primary: true, cell: (r) => r.name },
        { id: 'category', header: 'Line', cell: (r) => r.categoryName, hideOnMobile: true },
        { id: 'packages', header: 'Packages', align: 'right', cell: (r) => r.packageCount, hideOnMobile: true },
        { id: 'status', header: 'Status', cell: (r) => <Badge tone={r.isPublished ? 'success' : 'neutral'}>{r.isPublished ? 'Published' : 'Draft'}</Badge> },
      ]}
      toDraft={(d) =>
        d
          ? { ...d, id: d.id }
          : {
              id: null, categoryId: categories.data?.[0]?.id ?? '', slug: '', name: '', tagline: '', heroTitle: null, heroBody: null, overviewMarkdown: '',
              problemsSolved: [], deliverables: [], processSteps: [], tools: [], kpis: [], faqs: [], relatedServiceIds: [], icon: null, heroImageUrl: null,
              ctaLabel: null, ctaUrl: null, seo: EMPTY_SEO, isPublished: false, isFeatured: false, sortOrder: 0, packages: [],
            }
      }
      save={(draft, existing) => {
        const { packages: _p, id: _id, ...body } = draft;
        return existing ? api.put(`${W}/services/${existing.id}`, { ...body, concurrencyStamp: existing.concurrencyStamp }) : api.post(`${W}/services`, body);
      }}
      remove={(r) => api.delete(`${W}/services/${r.id}`)}
      Form={({ draft, setDraft, errors }) => {
        const set = <K extends keyof ServiceDraft>(k: K, v: ServiceDraft[K]) => setDraft({ ...draft, [k]: v });
        return (
          <>
            <div className="cms-grid-2">
              <TextField label="Name" required value={draft.name} onChange={(v) => set('name', v)} error={errors.name} />
              <TextField label="Slug" required value={draft.slug} onChange={(v) => set('slug', v)} error={errors.slug} hint="URL: /services/slug" />
              <SelectField
                label="Service line"
                required
                value={draft.categoryId}
                onChange={(v) => set('categoryId', v)}
                options={(categories.data ?? []).map((c) => ({ value: c.id, label: c.name }))}
                error={errors.categoryId}
              />
              <SelectField label="Icon" value={draft.icon} onChange={(v) => set('icon', v || null)} options={ICON_OPTIONS} placeholder="Default" />
            </div>
            <TextField label="Tagline" required value={draft.tagline} onChange={(v) => set('tagline', v)} maxLength={200} error={errors.tagline} />
            <AreaField label="Hero text" value={draft.heroBody} onChange={(v) => set('heroBody', v || null)} maxLength={600} error={errors.heroBody} />
            <MarkdownField label="Overview" value={draft.overviewMarkdown} onChange={(v) => set('overviewMarkdown', v)} error={errors.overviewMarkdown} />
            <LinesField label="Problems solved" value={draft.problemsSolved} onChange={(v) => set('problemsSolved', v)} error={errorFor(errors, 'problemsSolved')} />
            <LinesField label="Deliverables" value={draft.deliverables} onChange={(v) => set('deliverables', v)} error={errorFor(errors, 'deliverables')} />
            <ListEditor
              legend="Process steps"
              items={draft.processSteps}
              onChange={(v) => set('processSteps', v)}
              empty={{ title: '', description: '' }}
              error={errorFor(errors, 'processSteps')}
              addLabel="Add step"
              render={(s, update) => (
                <>
                  <TextField label="Step title" value={s.title} onChange={(v) => update({ ...s, title: v })} />
                  <AreaField label="Description" value={s.description} onChange={(v) => update({ ...s, description: v })} rows={2} />
                </>
              )}
            />
            <LinesField label="Tools & platforms" value={draft.tools} onChange={(v) => set('tools', v)} />
            <LinesField label="KPIs it moves" value={draft.kpis} onChange={(v) => set('kpis', v)} />
            <ListEditor
              legend="FAQs"
              items={draft.faqs}
              onChange={(v) => set('faqs', v)}
              empty={{ question: '', answer: '' }}
              error={errorFor(errors, 'faqs')}
              addLabel="Add question"
              render={(f, update) => (
                <>
                  <TextField label="Question" value={f.question} onChange={(v) => update({ ...f, question: v })} />
                  <AreaField label="Answer" value={f.answer} onChange={(v) => update({ ...f, answer: v })} rows={3} />
                </>
              )}
            />
            <MultiCheck
              legend="Related services"
              options={(services.data ?? []).filter((s) => s.id !== draft.id).map((s) => ({ value: s.id, label: s.name }))}
              value={draft.relatedServiceIds}
              onChange={(v) => set('relatedServiceIds', v)}
              error={errors.relatedServiceIds}
            />
            <ImageField label="Hero image" value={draft.heroImageUrl} onChange={(v) => set('heroImageUrl', v)} error={errors.heroImageUrl} />
            <div className="cms-grid-2">
              <TextField label="Extra button label" value={draft.ctaLabel} onChange={(v) => set('ctaLabel', v || null)} error={errors.ctaLabel} hint="E.g. 'Join as a creator'." />
              <TextField label="Extra button link" value={draft.ctaUrl} onChange={(v) => set('ctaUrl', v || null)} error={errors.ctaUrl} />
            </div>
            <SeoFields value={draft.seo} onChange={(v) => set('seo', v)} errors={errors} />
            {draft.id ? <PackagesEditor serviceId={draft.id} /> : <p className="text-small text-muted">Save the service to add packages.</p>}
            <TextField label="Sort order" type="number" value={String(draft.sortOrder)} onChange={(v) => set('sortOrder', Number(v) || 0)} />
            <SwitchField label="Featured" checked={draft.isFeatured} onChange={(v) => set('isFeatured', v)} />
            <SwitchField label="Published" checked={draft.isPublished} onChange={(v) => set('isPublished', v)} />
          </>
        );
      }}
    />
  );
}

type CategoryDraft = Omit<ServiceCategory, 'id' | 'updatedAt' | 'concurrencyStamp' | 'serviceCount'>;

function CategoriesTab() {
  return (
    <ResourcePage<ServiceCategory, ServiceCategory, CategoryDraft>
      title="Service lines"
      description="Categories that group services in the mega-menu and on the services page."
      singular="Service line"
      queryKey={['agency', 'website', 'categories']}
      list={() => api.get<ServiceCategory[]>(`${W}/service-categories`)}
      getId={(r) => r.id}
      rowLabel={(r) => r.name}
      columns={[
        { id: 'name', header: 'Name', primary: true, cell: (r) => r.name },
        { id: 'count', header: 'Services', align: 'right', cell: (r) => r.serviceCount },
        { id: 'status', header: 'Status', cell: (r) => <Badge tone={r.isPublished ? 'success' : 'neutral'}>{r.isPublished ? 'Published' : 'Hidden'}</Badge> },
      ]}
      toDraft={(d) => (d ? { ...d } : { slug: '', name: '', description: null, icon: null, sortOrder: 0, isPublished: true })}
      save={(draft, existing) =>
        existing
          ? api.put(`${W}/service-categories/${existing.id}`, { ...draft, concurrencyStamp: existing.concurrencyStamp })
          : api.post(`${W}/service-categories`, draft)
      }
      remove={(r) => api.delete(`${W}/service-categories/${r.id}`)}
      Form={({ draft, setDraft, errors }) => (
        <>
          <TextField label="Name" required value={draft.name} onChange={(v) => setDraft({ ...draft, name: v })} error={errors.name} />
          <TextField label="Slug" required value={draft.slug} onChange={(v) => setDraft({ ...draft, slug: v })} error={errors.slug} />
          <AreaField label="Description" value={draft.description} onChange={(v) => setDraft({ ...draft, description: v || null })} maxLength={500} />
          <SelectField label="Icon" value={draft.icon} onChange={(v) => setDraft({ ...draft, icon: v || null })} options={ICON_OPTIONS} placeholder="Default" />
          <TextField label="Sort order" type="number" value={String(draft.sortOrder)} onChange={(v) => setDraft({ ...draft, sortOrder: Number(v) || 0 })} />
          <SwitchField label="Published" checked={draft.isPublished} onChange={(v) => setDraft({ ...draft, isPublished: v })} />
        </>
      )}
    />
  );
}

/** Services & packages. */
export function ServicesAdminPage() {
  return (
    <Tabs
      label="Services and service lines"
      tabs={[
        { id: 'services', label: 'Services', content: <ServicesTab /> },
        { id: 'lines', label: 'Service lines', content: <CategoriesTab /> },
      ]}
    />
  );
}
