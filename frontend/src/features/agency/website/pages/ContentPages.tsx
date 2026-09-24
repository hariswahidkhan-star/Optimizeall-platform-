import { useQuery } from '@tanstack/react-query';
import { Badge } from '@/components/ui';
import { ICON_NAMES } from '@/features/public/site/icons';
import { api } from '@/lib/api/client';
import { type CaseStudy, type Industry, type Paged, type ResultMetric, type ServiceSummary, type TeamMember, type Testimonial, W } from '../api';
import {
  AreaField,
  EMPTY_SEO,
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
} from '../shared/fields';
import { ResourcePage } from '../shared/ResourcePage';

export const ICON_OPTIONS = ICON_NAMES.map((n) => ({ value: n, label: n }));

const published = (on: boolean) => <Badge tone={on ? 'success' : 'neutral'}>{on ? 'Published' : 'Draft'}</Badge>;

/** Service options for pickers. */
export function useServiceOptions() {
  return useQuery({
    queryKey: ['agency', 'website', 'service-options'],
    queryFn: async () => (await api.get<Paged<ServiceSummary>>(`${W}/services`, { query: { pageSize: 200 } })).items,
    staleTime: 60_000,
  });
}

const intOr = (v: string, fallback = 0) => (Number.isFinite(Number(v)) && v.trim() !== '' ? Math.trunc(Number(v)) : fallback);

// ---------------------------------------------------------------- Industries

type IndustryDraft = Omit<Industry, 'id' | 'updatedAt' | 'concurrencyStamp'>;

export function IndustriesAdminPage() {
  const services = useServiceOptions();
  return (
    <ResourcePage<Industry, Industry, IndustryDraft>
      title="Industries"
      description="Industries the agency serves, with recommended services. Case studies link to an industry."
      singular="Industry"
      queryKey={['agency', 'website', 'industries']}
      list={async () => (await api.get<Paged<Industry>>(`${W}/industries`, { query: { pageSize: 200 } })).items}
      getId={(r) => r.id}
      rowLabel={(r) => r.name}
      publicUrl={(r) => (r.isPublished ? `/industries/${r.slug}` : null)}
      columns={[
        { id: 'name', header: 'Industry', primary: true, cell: (r) => r.name },
        { id: 'slug', header: 'Slug', cell: (r) => <code>{r.slug}</code>, hideOnMobile: true },
        { id: 'status', header: 'Status', cell: (r) => published(r.isPublished) },
      ]}
      toDraft={(d) =>
        d
          ? { ...d }
          : { slug: '', name: '', summary: '', bodyMarkdown: '', challenges: [], serviceIds: [], icon: null, heroImageUrl: null, seo: EMPTY_SEO, isPublished: false, sortOrder: 0 }
      }
      save={(draft, existing) =>
        existing ? api.put(`${W}/industries/${existing.id}`, { ...draft, concurrencyStamp: existing.concurrencyStamp }) : api.post(`${W}/industries`, draft)
      }
      remove={(r) => api.delete(`${W}/industries/${r.id}`)}
      reorder={(ids) => api.post(`${W}/industries/reorder`, { ids })}
      Form={({ draft, setDraft, errors }) => {
        const set = <K extends keyof IndustryDraft>(k: K, v: IndustryDraft[K]) => setDraft({ ...draft, [k]: v });
        return (
          <>
            <div className="cms-grid-2">
              <TextField label="Name" required value={draft.name} onChange={(v) => set('name', v)} error={errors.name} />
              <TextField label="Slug" required value={draft.slug} onChange={(v) => set('slug', v)} error={errors.slug} hint="URL: /industries/slug" />
            </div>
            <AreaField label="Summary" required value={draft.summary} onChange={(v) => set('summary', v)} maxLength={500} error={errors.summary} />
            <MarkdownField label="Body" value={draft.bodyMarkdown} onChange={(v) => set('bodyMarkdown', v)} error={errors.bodyMarkdown} />
            <LinesField label="Challenges" value={draft.challenges} onChange={(v) => set('challenges', v)} error={errorFor(errors, 'challenges')} />
            <MultiCheck
              legend="Recommended services"
              options={(services.data ?? []).map((s) => ({ value: s.id, label: s.name }))}
              value={draft.serviceIds}
              onChange={(v) => set('serviceIds', v)}
              error={errors.serviceIds}
            />
            <SelectField label="Icon" value={draft.icon} onChange={(v) => set('icon', v || null)} options={ICON_OPTIONS} placeholder="Default" />
            <ImageField label="Hero image" value={draft.heroImageUrl} onChange={(v) => set('heroImageUrl', v)} error={errors.heroImageUrl} />
            <SeoFields value={draft.seo} onChange={(v) => set('seo', v)} errors={errors} />
            <TextField label="Sort order" type="number" value={String(draft.sortOrder)} onChange={(v) => set('sortOrder', intOr(v))} />
            <SwitchField label="Published" checked={draft.isPublished} onChange={(v) => set('isPublished', v)} />
          </>
        );
      }}
    />
  );
}

// ---------------------------------------------------------------- Case studies

type CaseDraft = Omit<CaseStudy, 'id' | 'updatedAt' | 'concurrencyStamp' | 'publishedAt'>;

export function CaseStudiesAdminPage() {
  const services = useServiceOptions();
  const industries = useQuery({
    queryKey: ['agency', 'website', 'industries'],
    queryFn: async () => (await api.get<Paged<Industry>>(`${W}/industries`, { query: { pageSize: 200 } })).items,
  });
  return (
    <ResourcePage<CaseStudy, CaseStudy, CaseDraft>
      title="Case studies"
      description="Client results. Every metric must say whether it was measured or estimated — estimates are never shown as measured."
      singular="Case study"
      queryKey={['agency', 'website', 'case-studies']}
      list={async () => (await api.get<Paged<CaseStudy>>(`${W}/case-studies`, { query: { pageSize: 200 } })).items}
      getId={(r) => r.id}
      rowLabel={(r) => r.title}
      publicUrl={(r) => (r.isPublished ? `/case-studies/${r.slug}` : null)}
      columns={[
        { id: 'title', header: 'Case study', primary: true, cell: (r) => r.title },
        { id: 'client', header: 'Client', cell: (r) => r.clientName },
        { id: 'status', header: 'Status', cell: (r) => published(r.isPublished) },
        { id: 'featured', header: 'Featured', cell: (r) => (r.isFeatured ? <Badge tone="brand">Featured</Badge> : null), hideOnMobile: true },
      ]}
      toDraft={(d) =>
        d
          ? { ...d }
          : {
              slug: '', title: '', clientName: '', clientAnonymized: false, summary: '', industryId: null, serviceIds: [], challengeMarkdown: '',
              strategyMarkdown: '', executionMarkdown: '', metrics: [], testimonialQuote: null, testimonialAuthor: null, testimonialRole: null,
              coverImageUrl: null, galleryImageUrls: [], seo: EMPTY_SEO, isPublished: false, isFeatured: false, sortOrder: 0,
            }
      }
      save={(draft, existing) =>
        existing ? api.put(`${W}/case-studies/${existing.id}`, { ...draft, concurrencyStamp: existing.concurrencyStamp }) : api.post(`${W}/case-studies`, draft)
      }
      remove={(r) => api.delete(`${W}/case-studies/${r.id}`)}
      reorder={(ids) => api.post(`${W}/case-studies/reorder`, { ids })}
      Form={({ draft, setDraft, errors }) => {
        const set = <K extends keyof CaseDraft>(k: K, v: CaseDraft[K]) => setDraft({ ...draft, [k]: v });
        return (
          <>
            <div className="cms-grid-2">
              <TextField label="Title" required value={draft.title} onChange={(v) => set('title', v)} error={errors.title} />
              <TextField label="Slug" required value={draft.slug} onChange={(v) => set('slug', v)} error={errors.slug} />
              <TextField label="Client" required value={draft.clientName} onChange={(v) => set('clientName', v)} error={errors.clientName} hint="Or a description such as 'A DTC skincare brand'." />
              <SelectField
                label="Industry"
                value={draft.industryId}
                onChange={(v) => set('industryId', v || null)}
                placeholder="None"
                options={(industries.data ?? []).map((i) => ({ value: i.id, label: i.name }))}
                error={errors.industryId}
              />
            </div>
            <SwitchField label="Client asked not to be named" checked={draft.clientAnonymized} onChange={(v) => set('clientAnonymized', v)} />
            <AreaField label="Summary" required value={draft.summary} onChange={(v) => set('summary', v)} maxLength={500} error={errors.summary} />
            <MultiCheck legend="Services" options={(services.data ?? []).map((s) => ({ value: s.id, label: s.name }))} value={draft.serviceIds} onChange={(v) => set('serviceIds', v)} error={errors.serviceIds} />
            <MarkdownField label="Challenge" value={draft.challengeMarkdown} onChange={(v) => set('challengeMarkdown', v)} rows={6} error={errors.challengeMarkdown} />
            <MarkdownField label="Strategy" value={draft.strategyMarkdown} onChange={(v) => set('strategyMarkdown', v)} rows={6} error={errors.strategyMarkdown} />
            <MarkdownField label="Execution" value={draft.executionMarkdown} onChange={(v) => set('executionMarkdown', v)} rows={6} error={errors.executionMarkdown} />
            <ListEditor<ResultMetric>
              legend="Results"
              items={draft.metrics}
              onChange={(v) => set('metrics', v)}
              empty={{ label: '', value: '', measurement: 'Measured', context: null }}
              error={errorFor(errors, 'metrics')}
              addLabel="Add result"
              render={(m, update, i) => (
                <div className="cms-grid-2">
                  <TextField label="Label" value={m.label} onChange={(v) => update({ ...m, label: v })} error={errors[`metrics[${i}]`]} />
                  <TextField label="Value" value={m.value} onChange={(v) => update({ ...m, value: v })} hint="e.g. +212% or $1.4M" />
                  <SelectField
                    label="Measurement"
                    required
                    value={m.measurement}
                    onChange={(v) => update({ ...m, measurement: v as ResultMetric['measurement'] })}
                    options={[
                      { value: 'Measured', label: 'Measured (from tracked data)' },
                      { value: 'Estimated', label: 'Estimated (projection or model)' },
                    ]}
                    error={errors[`metrics[${i}].measurement`]}
                  />
                  <TextField label="Context" value={m.context} onChange={(v) => update({ ...m, context: v || null })} hint="Source or period" />
                </div>
              )}
            />
            <fieldset className="cms-fieldset">
              <legend>Testimonial</legend>
              <AreaField label="Quote" value={draft.testimonialQuote} onChange={(v) => set('testimonialQuote', v || null)} maxLength={1000} />
              <div className="cms-grid-2">
                <TextField label="Author" value={draft.testimonialAuthor} onChange={(v) => set('testimonialAuthor', v || null)} error={errors.testimonialAuthor} />
                <TextField label="Role" value={draft.testimonialRole} onChange={(v) => set('testimonialRole', v || null)} />
              </div>
            </fieldset>
            <ImageField label="Cover image" value={draft.coverImageUrl} onChange={(v) => set('coverImageUrl', v)} error={errors.coverImageUrl} />
            <LinesField label="Gallery image URLs" value={draft.galleryImageUrls} onChange={(v) => set('galleryImageUrls', v)} error={errors.galleryImageUrls} hint="One uploaded image URL per line (upload with the cover image field, then paste here)." />
            <SeoFields value={draft.seo} onChange={(v) => set('seo', v)} errors={errors} />
            <TextField label="Sort order" type="number" value={String(draft.sortOrder)} onChange={(v) => set('sortOrder', intOr(v))} />
            <SwitchField label="Featured on the homepage" checked={draft.isFeatured} onChange={(v) => set('isFeatured', v)} />
            <SwitchField label="Published" checked={draft.isPublished} onChange={(v) => set('isPublished', v)} description="Only publish with the client's written approval." />
          </>
        );
      }}
    />
  );
}

// ---------------------------------------------------------------- Testimonials

type TestimonialDraft = Omit<Testimonial, 'id' | 'updatedAt' | 'concurrencyStamp'>;

export function TestimonialsAdminPage() {
  const services = useServiceOptions();
  return (
    <ResourcePage<Testimonial, Testimonial, TestimonialDraft>
      title="Testimonials"
      description="Client quotes shown on the homepage, service pages and CMS pages."
      singular="Testimonial"
      queryKey={['agency', 'website', 'testimonials']}
      list={async () => (await api.get<Paged<Testimonial>>(`${W}/testimonials`, { query: { pageSize: 200 } })).items}
      getId={(r) => r.id}
      rowLabel={(r) => `${r.authorName}${r.company ? `, ${r.company}` : ''}`}
      columns={[
        { id: 'author', header: 'Author', primary: true, cell: (r) => `${r.authorName}${r.company ? ` — ${r.company}` : ''}` },
        { id: 'quote', header: 'Quote', cell: (r) => (r.quote.length > 80 ? `${r.quote.slice(0, 80)}…` : r.quote), hideOnMobile: true },
        { id: 'status', header: 'Status', cell: (r) => published(r.isPublished) },
      ]}
      toDraft={(d) =>
        d ? { ...d } : { quote: '', authorName: '', authorRole: null, company: null, rating: 5, avatarUrl: null, serviceId: null, isPublished: false, isFeatured: false, sortOrder: 0 }
      }
      save={(draft, existing) =>
        existing ? api.put(`${W}/testimonials/${existing.id}`, { ...draft, concurrencyStamp: existing.concurrencyStamp }) : api.post(`${W}/testimonials`, draft)
      }
      remove={(r) => api.delete(`${W}/testimonials/${r.id}`)}
      reorder={(ids) => api.post(`${W}/testimonials/reorder`, { ids })}
      Form={({ draft, setDraft, errors }) => {
        const set = <K extends keyof TestimonialDraft>(k: K, v: TestimonialDraft[K]) => setDraft({ ...draft, [k]: v });
        return (
          <>
            <AreaField label="Quote" required value={draft.quote} onChange={(v) => set('quote', v)} maxLength={1000} rows={4} error={errors.quote} />
            <div className="cms-grid-2">
              <TextField label="Author" required value={draft.authorName} onChange={(v) => set('authorName', v)} error={errors.authorName} />
              <TextField label="Role" value={draft.authorRole} onChange={(v) => set('authorRole', v || null)} />
              <TextField label="Company" value={draft.company} onChange={(v) => set('company', v || null)} />
              <SelectField
                label="Rating"
                value={draft.rating ? String(draft.rating) : ''}
                onChange={(v) => set('rating', v ? Number(v) : null)}
                placeholder="No rating"
                options={[5, 4, 3, 2, 1].map((n) => ({ value: String(n), label: `${n} stars` }))}
              />
            </div>
            <SelectField
              label="Service"
              value={draft.serviceId}
              onChange={(v) => set('serviceId', v || null)}
              placeholder="General"
              options={(services.data ?? []).map((s) => ({ value: s.id, label: s.name }))}
              error={errors.serviceId}
            />
            <ImageField label="Avatar" value={draft.avatarUrl} onChange={(v) => set('avatarUrl', v)} error={errors.avatarUrl} />
            <TextField label="Sort order" type="number" value={String(draft.sortOrder)} onChange={(v) => set('sortOrder', intOr(v))} />
            <SwitchField label="Featured" checked={draft.isFeatured} onChange={(v) => set('isFeatured', v)} />
            <SwitchField label="Published" checked={draft.isPublished} onChange={(v) => set('isPublished', v)} description="Only publish quotes the client approved." />
          </>
        );
      }}
    />
  );
}

// ---------------------------------------------------------------- Team

type TeamDraft = Omit<TeamMember, 'id' | 'updatedAt' | 'concurrencyStamp'>;

export function TeamAdminPage() {
  return (
    <ResourcePage<TeamMember, TeamMember, TeamDraft>
      title="Team"
      description="People shown on the team page and as blog authors."
      singular="Team member"
      queryKey={['agency', 'website', 'team']}
      list={() => api.get<TeamMember[]>(`${W}/team`)}
      getId={(r) => r.id}
      rowLabel={(r) => r.name}
      columns={[
        { id: 'name', header: 'Name', primary: true, cell: (r) => r.name },
        { id: 'role', header: 'Role', cell: (r) => r.role },
        { id: 'status', header: 'Visible', cell: (r) => published(r.isPublished) },
      ]}
      toDraft={(d) => (d ? { ...d } : { slug: '', name: '', role: '', bio: null, photoUrl: null, expertise: [], socialLinks: [], isPublished: true, sortOrder: 0 })}
      save={(draft, existing) =>
        existing ? api.put(`${W}/team/${existing.id}`, { ...draft, concurrencyStamp: existing.concurrencyStamp }) : api.post(`${W}/team`, draft)
      }
      remove={(r) => api.delete(`${W}/team/${r.id}`)}
      reorder={(ids) => api.post(`${W}/team/reorder`, { ids })}
      Form={({ draft, setDraft, errors }) => {
        const set = <K extends keyof TeamDraft>(k: K, v: TeamDraft[K]) => setDraft({ ...draft, [k]: v });
        return (
          <>
            <div className="cms-grid-2">
              <TextField label="Name" required value={draft.name} onChange={(v) => set('name', v)} error={errors.name} />
              <TextField label="Slug" required value={draft.slug} onChange={(v) => set('slug', v)} error={errors.slug} />
              <TextField label="Role" required value={draft.role} onChange={(v) => set('role', v)} error={errors.role} />
            </div>
            <AreaField label="Bio" value={draft.bio} onChange={(v) => set('bio', v || null)} maxLength={2000} rows={4} />
            <ImageField label="Photo" value={draft.photoUrl} onChange={(v) => set('photoUrl', v)} error={errors.photoUrl} />
            <LinesField label="Expertise" value={draft.expertise} onChange={(v) => set('expertise', v)} error={errorFor(errors, 'expertise')} />
            <ListEditor
              legend="Social profiles"
              items={draft.socialLinks}
              onChange={(v) => set('socialLinks', v)}
              empty={{ label: 'LinkedIn', url: 'https://' }}
              error={errorFor(errors, 'socialLinks')}
              addLabel="Add profile"
              render={(l, update) => (
                <div className="cms-grid-2">
                  <TextField label="Label" value={l.label} onChange={(v) => update({ ...l, label: v })} />
                  <TextField label="URL" value={l.url} onChange={(v) => update({ ...l, url: v })} />
                </div>
              )}
            />
            <TextField label="Sort order" type="number" value={String(draft.sortOrder)} onChange={(v) => set('sortOrder', intOr(v))} />
            <SwitchField label="Show on the team page" checked={draft.isPublished} onChange={(v) => set('isPublished', v)} />
          </>
        );
      }}
    />
  );
}
