import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useState } from 'react';
import { Alert, Button, ErrorState, PageHeader, Skeleton, Tabs, useToast } from '@/components/ui';
import type { MenuItem, SiteLink } from '@/features/public/site/api';
import { api } from '@/lib/api/client';
import { errorMessage, isApiError } from '@/lib/api/errors';
import { type SiteSettings, type SiteSettingsEnvelope, W } from '../api';
import { AreaField, type Errors, errorFor, ImageField, LinesField, ListEditor, SelectField, SwitchField, TextField, toErrors } from '../shared/fields';
import '../website.css';

const SOCIAL_PLATFORMS = ['LinkedIn', 'Instagram', 'Facebook', 'X', 'TikTok', 'YouTube', 'Pinterest', 'Threads', 'WhatsApp', 'GitHub', 'Behance', 'Dribbble'];
const EMPTY_LINK: SiteLink = { label: '', url: '' };
const EMPTY_MENU: MenuItem = { label: '', url: null, description: null, children: [] };

function LinkFields({ value, onChange, errors, field }: { value: SiteLink; onChange: (v: SiteLink) => void; errors: Errors; field: string }) {
  return (
    <div className="cms-grid-2">
      <TextField label="Label" value={value.label} onChange={(label) => onChange({ ...value, label })} error={errors[`${field}.label`]} required maxLength={60} />
      <TextField label="Link" value={value.url} onChange={(url) => onChange({ ...value, url })} error={errors[`${field}.url`]} required hint="/relative/path or https://…" />
    </div>
  );
}

/** Site settings: header menu, footer, contact & social, announcement bar, SEO & organization schema, analytics, home stats and trust logos. */
export function SiteSettingsPage() {
  const qc = useQueryClient();
  const toast = useToast();
  const query = useQuery({ queryKey: ['website', 'settings'], queryFn: () => api.get<SiteSettingsEnvelope>(`${W}/settings`) });
  const [draft, setDraft] = useState<SiteSettings | null>(null);
  const [errors, setErrors] = useState<Errors>({});

  useEffect(() => {
    if (query.data && !draft) setDraft(query.data.settings);
  }, [query.data, draft]);

  const save = useMutation({
    mutationFn: (settings: SiteSettings) => api.put<SiteSettingsEnvelope>(`${W}/settings`, { settings, concurrencyStamp: query.data!.concurrencyStamp }),
    onSuccess: (saved) => {
      qc.setQueryData(['website', 'settings'], saved);
      qc.invalidateQueries({ queryKey: ['public', 'site'] });
      setDraft(saved.settings);
      setErrors({});
      toast.success('Site settings saved');
    },
    onError: (e) => {
      if (isApiError(e)) setErrors(toErrors(e.errors));
      toast.error(errorMessage(e));
    },
  });

  if (query.isError) return <ErrorState error={query.error} onRetry={() => void query.refetch()} />;
  if (!draft) return <Skeleton height={320} />;

  const set = <K extends keyof SiteSettings>(key: K, value: SiteSettings[K]) => setDraft({ ...draft, [key]: value });
  const e = errors;
  const errorCount = Object.keys(errors).length;

  const general = (
    <div className="cms-form">
      <TextField label="Site name" value={draft.siteName} onChange={(v) => set('siteName', v)} error={e.siteName} required maxLength={80} />
      <TextField label="Tagline" value={draft.tagline} onChange={(v) => set('tagline', v)} error={e.tagline} required maxLength={120} />
      <fieldset className="cms-fieldset">
        <legend>Announcement bar</legend>
        <SwitchField label="Show the announcement bar" checked={draft.announcement.enabled} onChange={(enabled) => set('announcement', { ...draft.announcement, enabled })} />
        <TextField label="Text" value={draft.announcement.text} onChange={(text) => set('announcement', { ...draft.announcement, text })} error={e['announcement.text']} maxLength={200} />
        <div className="cms-grid-2">
          <TextField label="Link label" value={draft.announcement.linkLabel} onChange={(linkLabel) => set('announcement', { ...draft.announcement, linkLabel })} error={e['announcement.linkLabel']} maxLength={40} />
          <TextField label="Link" value={draft.announcement.linkUrl} onChange={(linkUrl) => set('announcement', { ...draft.announcement, linkUrl })} error={e['announcement.linkUrl']} />
        </div>
      </fieldset>
    </div>
  );

  const navigation = (
    <div className="cms-form">
      <ListEditor
        legend="Header menu"
        addLabel="Add menu item"
        items={draft.header.menu}
        empty={EMPTY_MENU}
        error={e['header.menu']}
        onChange={(menu) => set('header', { ...draft.header, menu })}
        render={(item, update, i) => (
          <>
            <div className="cms-grid-2">
              <TextField label="Label" value={item.label} onChange={(label) => update({ ...item, label })} error={e[`header.menu[${i}].label`]} required maxLength={40} />
              <TextField label="Link" value={item.url} onChange={(url) => update({ ...item, url: url || null })} error={e[`header.menu[${i}].url`]} hint="Optional when the item has sub-items." />
            </div>
            <ListEditor
              legend={`Sub-items of ${item.label || `item ${i + 1}`}`}
              addLabel="Add sub-item"
              items={item.children ?? []}
              empty={EMPTY_MENU}
              error={errorFor(e, `header.menu[${i}].children`)}
              onChange={(children) => update({ ...item, children })}
              render={(child, updateChild, j) => (
                <>
                  <div className="cms-grid-2">
                    <TextField label="Label" value={child.label} onChange={(label) => updateChild({ ...child, label })} error={e[`header.menu[${i}].children[${j}].label`]} required maxLength={60} />
                    <TextField label="Link" value={child.url} onChange={(url) => updateChild({ ...child, url })} error={e[`header.menu[${i}].children[${j}].url`]} required />
                  </div>
                  <TextField label="Description" value={child.description} onChange={(description) => updateChild({ ...child, description: description || null })} maxLength={160} />
                </>
              )}
            />
          </>
        )}
      />
      <p className="text-small text-muted">The Services mega-menu is built automatically from published service categories.</p>
      <fieldset className="cms-fieldset">
        <legend>Header button</legend>
        <LinkFields value={draft.header.cta ?? EMPTY_LINK} onChange={(cta) => set('header', { ...draft.header, cta })} errors={e} field="header.cta" />
      </fieldset>
    </div>
  );

  const footer = (
    <div className="cms-form">
      <AreaField label="Footer blurb" value={draft.footer.blurb} onChange={(blurb) => set('footer', { ...draft.footer, blurb })} error={e['footer.blurb']} maxLength={400} />
      <ListEditor
        legend="Footer columns"
        addLabel="Add column"
        items={draft.footer.columns}
        empty={{ title: '', links: [] }}
        error={e['footer.columns']}
        onChange={(columns) => set('footer', { ...draft.footer, columns })}
        render={(col, update, i) => (
          <>
            <TextField label="Column title" value={col.title} onChange={(title) => update({ ...col, title })} error={e[`footer.columns[${i}].title`]} required maxLength={40} />
            <ListEditor
              legend={`Links in ${col.title || `column ${i + 1}`}`}
              addLabel="Add link"
              items={col.links}
              empty={EMPTY_LINK}
              error={e[`footer.columns[${i}].links`]}
              onChange={(links) => update({ ...col, links })}
              render={(link, updateLink, j) => <LinkFields value={link} onChange={updateLink} errors={e} field={`footer.columns[${i}].links[${j}]`} />}
            />
          </>
        )}
      />
      <ListEditor
        legend="Legal links"
        addLabel="Add legal link"
        items={draft.footer.legalLinks}
        empty={EMPTY_LINK}
        onChange={(legalLinks) => set('footer', { ...draft.footer, legalLinks })}
        render={(link, update, j) => <LinkFields value={link} onChange={update} errors={e} field={`footer.legalLinks[${j}]`} />}
      />
    </div>
  );

  const contact = (
    <div className="cms-form">
      <fieldset className="cms-fieldset">
        <legend>Contact details</legend>
        <div className="cms-grid-2">
          <TextField label="Email" type="email" value={draft.contact.email} onChange={(email) => set('contact', { ...draft.contact, email })} error={e['contact.email']} />
          <TextField label="Phone" type="tel" value={draft.contact.phone} onChange={(phone) => set('contact', { ...draft.contact, phone })} error={e['contact.phone']} />
        </div>
        <TextField label="WhatsApp number" value={draft.contact.whatsApp} onChange={(whatsApp) => set('contact', { ...draft.contact, whatsApp })} error={e['contact.whatsApp']} hint="International format, e.g. +14155550100. Shown as a WhatsApp link in the footer and contact page." />
        <AreaField label="Address" value={draft.contact.address} onChange={(address) => set('contact', { ...draft.contact, address })} error={e['contact.address']} rows={2} />
        <TextField label="Hours" value={draft.contact.hours} onChange={(hours) => set('contact', { ...draft.contact, hours })} error={e['contact.hours']} placeholder="Mon–Fri, 9:00–18:00" />
      </fieldset>
      <ListEditor
        legend="Social profiles"
        addLabel="Add profile"
        items={draft.social}
        empty={{ platform: 'LinkedIn', url: '' }}
        onChange={(social) => set('social', social)}
        render={(p, update, i) => (
          <div className="cms-grid-2">
            <SelectField label="Platform" value={p.platform} onChange={(platform) => update({ ...p, platform })} options={SOCIAL_PLATFORMS.map((x) => ({ value: x, label: x }))} error={e[`social[${i}].platform`]} required />
            <TextField label="Profile URL" value={p.url} onChange={(url) => update({ ...p, url })} error={e[`social[${i}].url`]} required placeholder="https://" />
          </div>
        )}
      />
    </div>
  );

  const seo = (
    <div className="cms-form">
      <fieldset className="cms-fieldset">
        <legend>Default SEO</legend>
        <TextField label="Site URL" value={draft.seo.siteUrl} onChange={(siteUrl) => set('seo', { ...draft.seo, siteUrl })} error={e['seo.siteUrl']} hint="The public https:// origin used for canonical links and the sitemap." />
        <TextField label="Title template" value={draft.seo.titleTemplate} onChange={(titleTemplate) => set('seo', { ...draft.seo, titleTemplate })} error={e['seo.titleTemplate']} required hint="%s is replaced by the page title, e.g. “%s | Optimize All”." />
        <TextField label="Default title" value={draft.seo.defaultTitle} onChange={(defaultTitle) => set('seo', { ...draft.seo, defaultTitle })} error={e['seo.defaultTitle']} required maxLength={70} />
        <AreaField label="Default description" value={draft.seo.defaultDescription} onChange={(defaultDescription) => set('seo', { ...draft.seo, defaultDescription })} error={e['seo.defaultDescription']} maxLength={200} rows={2} />
        <ImageField label="Default social image" value={draft.seo.defaultOgImageUrl} onChange={(defaultOgImageUrl) => set('seo', { ...draft.seo, defaultOgImageUrl })} error={e['seo.defaultOgImageUrl']} />
        <TextField label="X / Twitter handle" value={draft.seo.twitterHandle} onChange={(twitterHandle) => set('seo', { ...draft.seo, twitterHandle })} error={e['seo.twitterHandle']} placeholder="@optimizeall" />
      </fieldset>
      <fieldset className="cms-fieldset">
        <legend>Organization (structured data)</legend>
        <TextField label="Legal name" value={draft.organization.legalName} onChange={(legalName) => set('organization', { ...draft.organization, legalName })} error={e['organization.legalName']} />
        <ImageField label="Logo" value={draft.organization.logoUrl} onChange={(logoUrl) => set('organization', { ...draft.organization, logoUrl })} error={e['organization.logoUrl']} />
        <TextField
          label="Founding year"
          type="number"
          value={draft.organization.foundingYear?.toString() ?? ''}
          onChange={(v) => set('organization', { ...draft.organization, foundingYear: v ? Number(v) : null })}
          error={e['organization.foundingYear']}
        />
        <TextField label="Street address" value={draft.organization.streetAddress} onChange={(streetAddress) => set('organization', { ...draft.organization, streetAddress })} error={e['organization.streetAddress']} />
        <div className="cms-grid-2">
          <TextField label="City" value={draft.organization.locality} onChange={(locality) => set('organization', { ...draft.organization, locality })} error={e['organization.locality']} />
          <TextField label="Region" value={draft.organization.region} onChange={(region) => set('organization', { ...draft.organization, region })} error={e['organization.region']} />
        </div>
        <div className="cms-grid-2">
          <TextField label="Postal code" value={draft.organization.postalCode} onChange={(postalCode) => set('organization', { ...draft.organization, postalCode })} error={e['organization.postalCode']} />
          <TextField label="Country code" value={draft.organization.countryCode} onChange={(countryCode) => set('organization', { ...draft.organization, countryCode })} error={e['organization.countryCode']} maxLength={2} placeholder="US" />
        </div>
        <LinesField label="Areas served" value={draft.organization.areaServed} onChange={(areaServed) => set('organization', { ...draft.organization, areaServed })} error={errorFor(e, 'organization.areaServed')} />
      </fieldset>
    </div>
  );

  const analytics = (
    <div className="cms-form">
      <Alert tone="info" title="Loaded only after consent">
        These tags are injected only after a visitor accepts analytics or marketing cookies in the consent banner. Adding an ID here also turns the banner on. Your server&apos;s Content-Security-Policy must allow the vendor hosts (see docs/WEBSITE.md).
      </Alert>
      <TextField label="GA4 measurement ID" value={draft.analytics.ga4MeasurementId} onChange={(ga4MeasurementId) => set('analytics', { ...draft.analytics, ga4MeasurementId })} error={e['analytics.ga4MeasurementId']} placeholder="G-ABC123XYZ9" />
      <TextField label="Google Tag Manager container ID" value={draft.analytics.gtmContainerId} onChange={(gtmContainerId) => set('analytics', { ...draft.analytics, gtmContainerId })} error={e['analytics.gtmContainerId']} placeholder="GTM-ABC1234" />
      <TextField label="Meta Pixel ID" value={draft.analytics.metaPixelId} onChange={(metaPixelId) => set('analytics', { ...draft.analytics, metaPixelId })} error={e['analytics.metaPixelId']} />
    </div>
  );

  const home = (
    <div className="cms-form">
      <ListEditor
        legend="Home stats"
        addLabel="Add stat"
        items={draft.homeStats}
        empty={{ label: '', value: '', measurement: 'Measured', context: null }}
        error={e.homeStats}
        onChange={(homeStats) => set('homeStats', homeStats)}
        render={(s, update, i) => (
          <>
            <div className="cms-grid-2">
              <TextField label="Value" value={s.value} onChange={(value) => update({ ...s, value })} error={e[`homeStats[${i}].value`]} required maxLength={20} placeholder="120+" />
              <TextField label="Label" value={s.label} onChange={(label) => update({ ...s, label })} error={e[`homeStats[${i}].label`]} required maxLength={80} />
            </div>
            <div className="cms-grid-2">
              <SelectField
                label="Measurement"
                value={s.measurement}
                onChange={(m) => update({ ...s, measurement: m as typeof s.measurement })}
                options={[
                  { value: 'Measured', label: 'Measured' },
                  { value: 'Estimated', label: 'Estimated' },
                ]}
                error={e[`homeStats[${i}].measurement`]}
                required
                hint="Estimated figures are labelled as such on the site."
              />
              <TextField label="Context" value={s.context} onChange={(context) => update({ ...s, context: context || null })} error={e[`homeStats[${i}].context`]} maxLength={160} />
            </div>
          </>
        )}
      />
      <ListEditor
        legend="Trust logos"
        addLabel="Add logo"
        items={draft.trustLogos}
        empty={{ name: '', imageUrl: '', url: null }}
        error={e.trustLogos}
        onChange={(trustLogos) => set('trustLogos', trustLogos)}
        render={(l, update, i) => (
          <>
            <TextField label="Name" value={l.name} onChange={(name) => update({ ...l, name })} error={e[`trustLogos[${i}].name`]} required maxLength={80} />
            <ImageField label="Logo image" value={l.imageUrl} onChange={(imageUrl) => update({ ...l, imageUrl })} error={e[`trustLogos[${i}].imageUrl`]} />
            <TextField label="Link" value={l.url} onChange={(url) => update({ ...l, url: url || null })} error={e[`trustLogos[${i}].url`]} />
          </>
        )}
      />
      <p className="text-small text-muted">Only show logos of clients who agreed to be named.</p>
    </div>
  );

  const tabError = (prefixes: string[]) => (Object.keys(errors).some((k) => prefixes.some((p) => k.startsWith(p))) ? ' •' : '');

  return (
    <div className="cms-page">
      <PageHeader
        title="Site settings"
        description="Navigation, footer, contact details, SEO defaults and analytics for the public website."
        actions={
          <Button onClick={() => save.mutate(draft)} loading={save.isPending}>
            Save settings
          </Button>
        }
      />
      {errorCount > 0 && (
        <Alert tone="danger" title="Some fields need attention">
          Fix the highlighted fields (tabs marked •) and save again.
        </Alert>
      )}
      <Tabs
        label="Site settings sections"
        tabs={[
          { id: 'general', label: `General${tabError(['siteName', 'tagline', 'announcement'])}`, content: general },
          { id: 'navigation', label: `Navigation${tabError(['header'])}`, content: navigation },
          { id: 'footer', label: `Footer${tabError(['footer'])}`, content: footer },
          { id: 'contact', label: `Contact & social${tabError(['contact', 'social'])}`, content: contact },
          { id: 'seo', label: `SEO & organization${tabError(['seo', 'organization'])}`, content: seo },
          { id: 'analytics', label: `Analytics${tabError(['analytics'])}`, content: analytics },
          { id: 'home', label: `Home stats & logos${tabError(['homeStats', 'trustLogos'])}`, content: home },
        ]}
      />
    </div>
  );
}
