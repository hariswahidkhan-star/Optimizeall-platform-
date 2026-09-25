import { useQuery } from '@tanstack/react-query';
import { BarChart3, Download } from 'lucide-react';
import { useState } from 'react';
import {
  Alert,
  Badge,
  Button,
  ButtonLink,
  Card,
  CardBody,
  CardHeader,
  DataTable,
  type DataTableColumn,
  ErrorState,
  FormField,
  Input,
  PageHeader,
  Select,
  Stat,
  StatGrid,
  useToast,
} from '@/components/ui';
import { api } from '@/lib/api/client';
import { errorMessage } from '@/lib/api/errors';
import { type Seo, W } from '../api';
import {
  AreaField,
  EMPTY_SEO,
  errorFor,
  ImageField,
  LinesField,
  ListEditor,
  MarkdownField,
  MultiCheck,
  SeoFields,
  SwitchField,
  TextField,
} from '../shared/fields';
import { ResourcePage } from '../shared/ResourcePage';

/** Website → Partners (`/agency/website/partners`, site.manage). See docs/WEBSITE.md "Partners and sponsored placements". */

const P = `${W}/partners`;

export interface PartnerOfferingItem {
  title: string;
  summary: string | null;
  facts: string[];
  anchor: string | null;
  link: string | null;
}

export interface AdminPartner {
  id: string;
  slug: string;
  name: string;
  logoUrl: string;
  websiteUrl: string | null;
  tagline: string;
  descriptionMarkdown: string | null;
  relationshipLabel: string;
  highlights: string[];
  offerings: PartnerOfferingItem[];
  keywords: string[];
  categories: string[];
  sameAs: string[];
  relatedPartnerIds: string[];
  slots: string[];
  brandColor: string | null;
  utmSource: string;
  utmMedium: string;
  utmCampaign: string | null;
  offerText: string | null;
  offerCode: string | null;
  offerExpiresAt: string | null;
  offerConfirmed: boolean;
  offerUpdatedAt: string | null;
  offerVisible: boolean;
  offerVisibleUntil: string | null;
  seo: Seo;
  isActive: boolean;
  sortOrder: number;
  publicPath: string;
  createdAt: string;
  updatedAt: string;
  concurrencyStamp: string;
}

export interface PartnerSlotOption {
  name: string;
  kind: 'List' | 'Unit' | 'Page';
  label: string;
  description: string;
}

type PartnerDraft = Omit<
  AdminPartner,
  'id' | 'publicPath' | 'createdAt' | 'updatedAt' | 'concurrencyStamp' | 'offerVisible' | 'offerVisibleUntil' | 'offerUpdatedAt'
>;

const DEFAULT_RELATIONSHIP = 'Optimize All is the official marketing partner of {Partner}';

const emptyDraft = (): PartnerDraft => ({
  slug: '',
  name: '',
  logoUrl: '',
  websiteUrl: null,
  tagline: '',
  descriptionMarkdown: null,
  relationshipLabel: DEFAULT_RELATIONSHIP,
  highlights: [],
  offerings: [],
  keywords: [],
  categories: [],
  sameAs: [],
  relatedPartnerIds: [],
  slots: ['home.partners', 'footer.partners'],
  brandColor: null,
  utmSource: 'optimizeall',
  utmMedium: 'partner',
  utmCampaign: null,
  offerText: null,
  offerCode: null,
  offerExpiresAt: null,
  offerConfirmed: false,
  seo: EMPTY_SEO,
  isActive: false,
  sortOrder: 0,
});

function hostOf(url: string | null): string | null {
  if (!url) return null;
  try {
    return new URL(url).hostname.replace(/^www\./, '');
  } catch {
    return null;
  }
}

const usePartnerSlots = () =>
  useQuery({ queryKey: ['agency', 'website', 'partner-slots'], queryFn: () => api.get<PartnerSlotOption[]>(`${P}/slots`), staleTime: 10 * 60_000 });

function OfferStatus({ p }: { p: Pick<AdminPartner, 'offerText' | 'offerVisible' | 'offerConfirmed'> }) {
  if (!p.offerText) return <span className="text-muted">None</span>;
  if (p.offerVisible) return <Badge tone="success">Shown</Badge>;
  return <Badge tone="warning">{p.offerConfirmed ? 'Expired' : 'Needs review'}</Badge>;
}

/** Logo upload/URL with a live preview (uploads, allow-listed hosts or a bundled /partners/… asset). */
function LogoField({ value, name, onChange, error }: { value: string; name: string; onChange: (v: string) => void; error?: string }) {
  return (
    <div className="cms-grid-2" style={{ alignItems: 'start' }}>
      <ImageField label="Logo" value={value} onChange={onChange} error={error} />
      <figure style={{ margin: 0 }} aria-label="Logo preview">
        <div
          style={{
            display: 'grid',
            placeItems: 'center',
            width: 128,
            height: 128,
            border: '1px solid var(--color-border)',
            borderRadius: 'var(--radius-lg)',
            background: '#fff',
            overflow: 'hidden',
          }}
        >
          {value ? (
            <img src={value} alt={`${name || 'Partner'} logo preview`} width={128} height={128} style={{ objectFit: 'contain' }} />
          ) : (
            <span className="text-small text-muted">No logo</span>
          )}
        </div>
        <figcaption className="text-small text-muted" style={{ marginTop: 'var(--space-2)' }}>
          Square logos work best (at least 256 × 256).
        </figcaption>
      </figure>
    </div>
  );
}

export function PartnersAdminPage() {
  const slots = usePartnerSlots();
  const [all, setAll] = useState<AdminPartner[]>([]);
  const switchable = (slots.data ?? []).filter((s) => s.kind !== 'Page');
  const slotLabel = (name: string) => slots.data?.find((s) => s.name === name)?.label ?? name;

  return (
    <ResourcePage<AdminPartner, AdminPartner, PartnerDraft>
      title="Partners"
      description="Partner profiles, where their sponsored placements appear, and the statement that Optimize All is their official marketing partner."
      singular="Partner"
      queryKey={['agency', 'website', 'partners']}
      list={async () => {
        const rows = await api.get<AdminPartner[]>(P);
        setAll(rows);
        return rows;
      }}
      getId={(r) => r.id}
      rowLabel={(r) => r.name}
      publicUrl={(r) => (r.isActive ? r.publicPath : null)}
      actions={
        <ButtonLink to="/agency/website/partners/report" variant="secondary" leadingIcon={<BarChart3 />}>
          Placement report
        </ButtonLink>
      }
      emptyText="No partners yet."
      columns={[
        {
          id: 'name',
          header: 'Partner',
          primary: true,
          cell: (r) => (
            <span style={{ display: 'inline-flex', alignItems: 'center', gap: 'var(--space-3)' }}>
              <img src={r.logoUrl} alt="" width={32} height={32} style={{ objectFit: 'contain', borderRadius: 6, background: '#fff' }} />
              <span>
                <strong>{r.name}</strong>
                <br />
                <span className="text-small text-muted">{r.tagline}</span>
              </span>
            </span>
          ),
        },
        {
          id: 'website',
          header: 'Website',
          cell: (r) => (r.websiteUrl ? hostOf(r.websiteUrl) : <Badge tone="warning">Not set — links hidden</Badge>),
        },
        {
          id: 'slots',
          header: 'Placements',
          hideOnMobile: true,
          cell: (r) => <span title={r.slots.map(slotLabel).join(', ')}>{r.slots.length}</span>,
        },
        { id: 'offer', header: 'Offer', hideOnMobile: true, cell: (r) => <OfferStatus p={r} /> },
        { id: 'status', header: 'Status', cell: (r) => <Badge tone={r.isActive ? 'success' : 'neutral'}>{r.isActive ? 'Active' : 'Inactive'}</Badge> },
      ]}
      load={(r) => api.get<AdminPartner>(`${P}/${r.id}`)}
      toDraft={(d) => {
        if (!d) return emptyDraft();
        // eslint-disable-next-line @typescript-eslint/no-unused-vars
        const { id, publicPath, createdAt, updatedAt, concurrencyStamp, offerVisible, offerVisibleUntil, offerUpdatedAt, ...draft } = d;
        return draft;
      }}
      save={(draft, existing) =>
        existing ? api.put(`${P}/${existing.id}`, { ...draft, concurrencyStamp: existing.concurrencyStamp }) : api.post(P, draft)
      }
      remove={(r) => api.delete(`${P}/${r.id}`)}
      reorder={(ids) => api.post(`${P}/reorder`, { ids })}
      Form={({ draft, setDraft, errors }) => {
        const set = <K extends keyof PartnerDraft>(k: K, v: PartnerDraft[K]) => setDraft({ ...draft, [k]: v });
        const others = all.filter((p) => p.slug !== draft.slug);
        const statement = (draft.relationshipLabel || DEFAULT_RELATIONSHIP).replace('{Partner}', draft.name || 'the partner');
        return (
          <>
            <fieldset className="cms-fieldset">
              <legend>Profile</legend>
              <div className="cms-grid-2">
                <TextField label="Name" required value={draft.name} onChange={(v) => set('name', v)} error={errors.name} maxLength={100} />
                <TextField label="Slug" required value={draft.slug} onChange={(v) => set('slug', v)} error={errors.slug} hint={`Profile page: /partners/${draft.slug || '…'}`} />
              </div>
              <TextField label="Tagline" required value={draft.tagline} onChange={(v) => set('tagline', v)} error={errors.tagline} maxLength={160} />
              <TextField
                label="Relationship statement"
                value={draft.relationshipLabel}
                onChange={(v) => set('relationshipLabel', v)}
                error={errors.relationshipLabel}
                maxLength={160}
                hint={`{Partner} is replaced by the name. Shown as: “${statement}.”`}
              />
              <LogoField value={draft.logoUrl} name={draft.name} onChange={(v) => set('logoUrl', v)} error={errors.logoUrl} />
              <div className="cms-grid-2">
                <TextField
                  label="Website"
                  value={draft.websiteUrl}
                  onChange={(v) => set('websiteUrl', v || null)}
                  error={errors.websiteUrl}
                  placeholder="https://"
                  hint="While empty, every link and call to action to the partner is hidden."
                />
                <FormField label="Brand colour" optional error={errors.brandColor} hint="Accent of the partner's cards, e.g. #1F3A93.">
                  <Input value={draft.brandColor ?? ''} maxLength={7} placeholder="#1F3A93" onChange={(e) => set('brandColor', e.target.value || null)} />
                </FormField>
              </div>
            </fieldset>

            <MarkdownField label="Description" value={draft.descriptionMarkdown} onChange={(v) => set('descriptionMarkdown', v || null)} rows={10} error={errors.descriptionMarkdown} />
            <Alert tone="info" title="Keep it factual">
              State only what the partner has confirmed or publishes on its own website. Links to the partner in this text are marked as
              sponsored automatically.
            </Alert>
            <LinesField label="Highlights" value={draft.highlights} onChange={(v) => set('highlights', v)} error={errorFor(errors, 'highlights')} hint="Short facts, one per line (shown as a checklist)." />

            <ListEditor<PartnerOfferingItem>
              legend="Offerings (products, certifications, exams)"
              items={draft.offerings}
              onChange={(v) => set('offerings', v)}
              empty={{ title: '', summary: null, facts: [], anchor: null, link: null }}
              error={errorFor(errors, 'offerings')}
              addLabel="Add offering"
              render={(o, update, i) => (
                <div className="cms-stack">
                  <div className="cms-grid-2">
                    <TextField label="Title" value={o.title} onChange={(v) => update({ ...o, title: v })} error={errors[`offerings[${i}].title`]} maxLength={120} />
                    <TextField label="Section id" value={o.anchor} onChange={(v) => update({ ...o, anchor: v || null })} error={errors[`offerings[${i}].anchor`]} hint="For links like /partners/slug#id" />
                  </div>
                  <AreaField label="Summary" value={o.summary} onChange={(v) => update({ ...o, summary: v || null })} maxLength={600} rows={2} />
                  <LinesField label="Facts" value={o.facts} onChange={(v) => update({ ...o, facts: v })} error={errorFor(errors, `offerings[${i}].facts`)} />
                  <TextField label="Links to" value={o.link} onChange={(v) => update({ ...o, link: v || null })} error={errors[`offerings[${i}].link`]} hint="Optional path on this site, e.g. /partners/pci-ai#pcl-ai" />
                </div>
              )}
            />

            <fieldset className="cms-fieldset">
              <legend>Placements</legend>
              <p className="text-small text-muted">
                Where the partner may appear. Ad units show at most one partner per slot, chosen by the keywords and categories below, and are
                always labelled “Sponsored”.
              </p>
              {slots.isError ? (
                <ErrorState error={slots.error} onRetry={() => void slots.refetch()} />
              ) : (
                <div className="cms-stack">
                  {switchable.map((s) => (
                    <SwitchField
                      key={s.name}
                      label={s.label}
                      description={s.description}
                      checked={draft.slots.includes(s.name)}
                      onChange={(on) => set('slots', on ? [...draft.slots, s.name] : draft.slots.filter((x) => x !== s.name))}
                    />
                  ))}
                </div>
              )}
              {errors.slots && (
                <p className="site-field-error" role="alert">
                  {errors.slots}
                </p>
              )}
            </fieldset>

            <fieldset className="cms-fieldset">
              <legend>Targeting</legend>
              <LinesField label="Keywords" value={draft.keywords} onChange={(v) => set('keywords', v)} error={errorFor(errors, 'keywords')} hint="Topics matched against page keywords (blog tags, service names, course topics). One per line." />
              <LinesField label="Categories" value={draft.categories} onChange={(v) => set('categories', v)} error={errorFor(errors, 'categories')} hint="Category slugs (blog categories, service slugs, course categories), e.g. project-management." />
            </fieldset>

            {others.length > 0 && (
              <MultiCheck
                legend="Related partners"
                options={others.map((p) => ({ value: p.id, label: p.name }))}
                value={draft.relatedPartnerIds}
                onChange={(v) => set('relatedPartnerIds', v)}
                error={errors.relatedPartnerIds}
              />
            )}

            <fieldset className="cms-fieldset">
              <legend>Links and tracking</legend>
              <div className="cms-grid-2">
                <TextField label="utm_source" value={draft.utmSource} onChange={(v) => set('utmSource', v)} error={errors.utmSource} />
                <TextField label="utm_medium" value={draft.utmMedium} onChange={(v) => set('utmMedium', v)} error={errors.utmMedium} />
              </div>
              <TextField label="utm_campaign" value={draft.utmCampaign} onChange={(v) => set('utmCampaign', v || null)} error={errors.utmCampaign} hint="Leave empty to use the placement name (e.g. blog.end)." />
              <LinesField label="Official profiles (sameAs)" value={draft.sameAs} onChange={(v) => set('sameAs', v)} error={errorFor(errors, 'sameAs')} hint="https:// addresses of the partner's own social or directory profiles." />
            </fieldset>

            <fieldset className="cms-fieldset">
              <legend>Current offer</legend>
              <TextField label="Offer" value={draft.offerText} onChange={(v) => set('offerText', v || null)} error={errors.offerText} maxLength={200} />
              <div className="cms-grid-2">
                <TextField label="Code" value={draft.offerCode} onChange={(v) => set('offerCode', v || null)} error={errors.offerCode} maxLength={40} />
                <TextField
                  label="Ends on"
                  type="date"
                  value={draft.offerExpiresAt?.slice(0, 10) ?? ''}
                  onChange={(v) => set('offerExpiresAt', v ? `${v}T23:59:59Z` : null)}
                  error={errors.offerExpiresAt}
                />
              </div>
              <SwitchField
                label="I checked this offer is still valid"
                checked={draft.offerConfirmed}
                onChange={(v) => set('offerConfirmed', v)}
                description="Offers are shown only when confirmed: until the end date, or for 30 days after the last edit when there is none."
              />
              {errors.offerConfirmed && (
                <p className="site-field-error" role="alert">
                  {errors.offerConfirmed}
                </p>
              )}
            </fieldset>

            <SeoFields value={draft.seo} onChange={(v) => set('seo', v)} errors={errors} />
            <TextField label="Sort order" type="number" value={String(draft.sortOrder)} onChange={(v) => set('sortOrder', Number.isFinite(Number(v)) ? Math.trunc(Number(v)) : 0)} />
            <SwitchField label="Active" checked={draft.isActive} onChange={(v) => set('isActive', v)} description="Shows the profile, the partners page entry and the enabled placements." />
          </>
        );
      }}
    />
  );
}

// ---------------------------------------------------------------- report

interface Metric {
  key: string;
  label: string;
  impressions: number;
  clicks: number;
  clickThroughRate: number | null;
}

interface PartnerReport {
  from: string;
  to: string;
  impressions: number;
  clicks: number;
  clickThroughRate: number | null;
  byPartner: Metric[];
  bySlot: Metric[];
  byPage: Metric[];
  daily: { day: string; impressions: number; clicks: number }[];
}

const pct = (v: number | null) => (v === null ? '—' : `${(v * 100).toFixed(1)}%`);
const isoDay = (d: Date) => d.toISOString().slice(0, 10);

function MetricTable({ title, rows, keyHeader }: { title: string; rows: Metric[]; keyHeader: string }) {
  const columns: DataTableColumn<Metric>[] = [
    { id: 'label', header: keyHeader, primary: true, cell: (r) => r.label, sortable: true, sortValue: (r) => r.label },
    { id: 'impressions', header: 'Impressions', align: 'right', cell: (r) => r.impressions.toLocaleString(), sortable: true, sortValue: (r) => r.impressions },
    { id: 'clicks', header: 'Clicks', align: 'right', cell: (r) => r.clicks.toLocaleString(), sortable: true, sortValue: (r) => r.clicks },
    { id: 'ctr', header: 'CTR', align: 'right', cell: (r) => pct(r.clickThroughRate), sortable: true, sortValue: (r) => r.clickThroughRate ?? -1 },
  ];
  return (
    <Card>
      <CardHeader title={title} />
      <CardBody>
        <DataTable caption={title} columns={columns} rows={rows} getRowId={(r) => r.key} emptyState={<p className="text-muted">No data for these dates.</p>} />
      </CardBody>
    </Card>
  );
}

export function PartnerReportPage() {
  const toast = useToast();
  const today = new Date();
  const [from, setFrom] = useState(isoDay(new Date(today.getTime() - 29 * 86_400_000)));
  const [to, setTo] = useState(isoDay(today));
  const [partnerId, setPartnerId] = useState('');
  const [slot, setSlot] = useState('');
  const partners = useQuery({ queryKey: ['agency', 'website', 'partners'], queryFn: () => api.get<AdminPartner[]>(P) });
  const slots = usePartnerSlots();
  const query = { from, to, partnerId: partnerId || undefined, slot: slot || undefined };
  const report = useQuery({ queryKey: ['agency', 'website', 'partner-report', query], queryFn: () => api.get<PartnerReport>(`${P}/report`, { query }) });

  const exportCsv = async () => {
    try {
      await api.download(`${P}/report.csv`, 'partner-placements.csv', { query });
    } catch (e) {
      toast.error('Export failed', errorMessage(e));
    }
  };

  return (
    <div className="cms-page">
      <PageHeader
        title="Placement report"
        description="Impressions and clicks of partner placements (UTC days; bots are never counted)."
        breadcrumbs={[{ label: 'Partners', to: '/agency/website/partners' }, { label: 'Placement report' }]}
        actions={
          <Button variant="secondary" leadingIcon={<Download />} onClick={() => void exportCsv()}>
            Export CSV
          </Button>
        }
      />
      <div className="cms-grid-2" style={{ gridTemplateColumns: 'repeat(auto-fit, minmax(12rem, 1fr))', marginBottom: 'var(--space-6)' }}>
        <FormField label="From">
          <Input type="date" value={from} onChange={(e) => setFrom(e.target.value)} />
        </FormField>
        <FormField label="To">
          <Input type="date" value={to} onChange={(e) => setTo(e.target.value)} />
        </FormField>
        <FormField label="Partner">
          <Select
            value={partnerId}
            onChange={(e) => setPartnerId(e.target.value)}
            placeholder="All partners"
            options={(partners.data ?? []).map((p) => ({ value: p.id, label: p.name }))}
          />
        </FormField>
        <FormField label="Placement">
          <Select value={slot} onChange={(e) => setSlot(e.target.value)} placeholder="All placements" options={(slots.data ?? []).map((s) => ({ value: s.name, label: s.label }))} />
        </FormField>
      </div>
      {report.isError ? (
        <ErrorState error={report.error} onRetry={() => void report.refetch()} />
      ) : (
        <>
          <StatGrid strip style={{ marginBottom: 'var(--space-6)' }}>
            <Stat label="Impressions" value={report.data?.impressions.toLocaleString() ?? '—'} loading={report.isLoading} />
            <Stat label="Clicks" value={report.data?.clicks.toLocaleString() ?? '—'} loading={report.isLoading} />
            <Stat label="Click-through rate" value={report.data ? pct(report.data.clickThroughRate) : '—'} loading={report.isLoading} />
          </StatGrid>
          <div className="cms-stack">
            <MetricTable title="By partner" keyHeader="Partner" rows={report.data?.byPartner ?? []} />
            <MetricTable title="By placement" keyHeader="Placement" rows={report.data?.bySlot ?? []} />
            <MetricTable title="Top pages" keyHeader="Page" rows={report.data?.byPage ?? []} />
          </div>
        </>
      )}
    </div>
  );
}
