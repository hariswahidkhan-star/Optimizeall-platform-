import { ArrowRight, ImageOff } from 'lucide-react';
import { Badge } from '@/components/ui/Badge';
import { buttonClasses } from '@/components/ui/buttonStyles';
import { DateTime } from '@/components/ui/DateTime';
import { Switch } from '@/components/ui/Switch';
import { countryName, countryOptions, languageOptions } from '@/features/auth/localeOptions';
import { humanize } from '@/lib/format/text';
import { AUDIENCES, type Banner } from '../api/types';
import { enumOptions, isoToLocalInput, localInputToIso, orNull } from '../shared/common';
import type { ContentConfig, FormProps } from './ContentEditor';
import { ContentTab } from './ContentTab';
import {
  isSafeLink,
  LINK_HINT,
  parseSortOrder,
  requireLength,
  SelectField,
  TextAreaField,
  TextField,
} from './fields';

export interface BannerDraft {
  title: string;
  body: string;
  imageUrl: string;
  ctaLabel: string;
  ctaUrl: string;
  audience: string;
  countryCode: string;
  languageCode: string;
  startsAt: string;
  endsAt: string;
  sortOrder: string;
  isActive: boolean;
}

const FIELDS = [
  'title',
  'body',
  'imageUrl',
  'ctaLabel',
  'ctaUrl',
  'audience',
  'countryCode',
  'languageCode',
  'startsAt',
  'endsAt',
  'sortOrder',
  'isActive',
] as const;

export const AUDIENCE_HELP: Record<string, string> = {
  Everyone: 'Every signed-in participant.',
  Onboarding: 'Email not verified yet, or no qualifying social account.',
  Eligible: 'Has at least one qualifying social account.',
  ActiveEarners: 'Has at least one approved submission.',
  Inactive: 'No activity within the inactivity threshold (Settings → Retention).',
};

export function audienceOptions() {
  return enumOptions(AUDIENCES);
}

function flatCountries() {
  const all = countryOptions().find((g) => 'options' in g && g.label === 'All countries');
  return all && 'options' in all ? all.options : [];
}

function BannerForm({ draft, setDraft, errors }: FormProps<BannerDraft>) {
  const set = <K extends keyof BannerDraft>(key: K, value: BannerDraft[K]) =>
    setDraft({ ...draft, [key]: value });
  return (
    <>
      <TextField
        label="Title"
        required
        maxLength={150}
        value={draft.title}
        onChange={(v) => set('title', v)}
        error={errors.title}
      />
      <TextAreaField
        label="Body"
        optional
        maxLength={1000}
        rows={3}
        value={draft.body}
        onChange={(v) => set('body', v)}
        error={errors.body}
      />
      <TextField
        label="Image URL"
        optional
        type="url"
        maxLength={500}
        value={draft.imageUrl}
        onChange={(v) => set('imageUrl', v)}
        error={errors.imageUrl}
        hint={LINK_HINT}
      />
      <div className="admin-form-grid">
        <TextField
          label="Button label"
          optional
          maxLength={60}
          value={draft.ctaLabel}
          onChange={(v) => set('ctaLabel', v)}
          error={errors.ctaLabel}
          hint="Label and link go together."
        />
        <TextField
          label="Button link"
          optional
          maxLength={500}
          value={draft.ctaUrl}
          onChange={(v) => set('ctaUrl', v)}
          error={errors.ctaUrl}
          hint={LINK_HINT}
        />
      </div>
      <SelectField
        label="Audience"
        required
        value={draft.audience}
        options={audienceOptions()}
        onChange={(v) => set('audience', v)}
        error={errors.audience}
        hint={AUDIENCE_HELP[draft.audience]}
      />
      <div className="admin-form-grid">
        <SelectField
          label="Country"
          optional
          placeholder="All countries"
          value={draft.countryCode}
          options={flatCountries()}
          onChange={(v) => set('countryCode', v)}
          error={errors.countryCode}
        />
        <SelectField
          label="Language"
          optional
          placeholder="All languages"
          value={draft.languageCode}
          options={languageOptions()}
          onChange={(v) => set('languageCode', v)}
          error={errors.languageCode}
        />
      </div>
      <div className="admin-form-grid">
        <TextField
          label="Show from"
          optional
          type="datetime-local"
          value={draft.startsAt}
          onChange={(v) => set('startsAt', v)}
          error={errors.startsAt}
          hint="Your local time."
        />
        <TextField
          label="Show until"
          optional
          type="datetime-local"
          value={draft.endsAt}
          onChange={(v) => set('endsAt', v)}
          error={errors.endsAt}
          hint="Must be after the start."
        />
      </div>
      <TextField
        label="Sort order"
        type="number"
        value={draft.sortOrder}
        onChange={(v) => set('sortOrder', v)}
        error={errors.sortOrder}
        hint="Lower numbers show first. Use “Reorder” to arrange banners visually."
      />
      <Switch
        checked={draft.isActive}
        onCheckedChange={(v) => set('isActive', v)}
        label="Active"
        description="Inactive banners are never shown, even inside the date window."
      />
    </>
  );
}

/** How the banner appears on the participant homepage (brand hero card: navy surface, amber call to action). */
export function BannerPreview({
  draft,
}: {
  draft: Pick<BannerDraft, 'title' | 'body' | 'imageUrl' | 'ctaLabel' | 'ctaUrl'>;
}) {
  const imageOk = draft.imageUrl && isSafeLink(draft.imageUrl);
  return (
    <div className="admin-banner-preview" data-testid="banner-preview">
      {imageOk ? (
        <img className="admin-banner-preview__image" src={draft.imageUrl} alt="" />
      ) : draft.imageUrl ? (
        <span className="admin-banner-preview__noimage">
          <ImageOff aria-hidden="true" /> Image URL not valid
        </span>
      ) : null}
      <div className="admin-banner-preview__text">
        <p className="admin-banner-preview__title">{draft.title || 'Banner title'}</p>
        {draft.body && <p className="admin-banner-preview__body">{draft.body}</p>}
        {draft.ctaLabel && (
          <span
            className={buttonClasses('highlight', 'sm', { className: 'admin-banner-preview__cta' })}
            aria-hidden="true"
          >
            {draft.ctaLabel}
            <ArrowRight />
          </span>
        )}
        {draft.ctaLabel && draft.ctaUrl && (
          <span className="visually-hidden">
            Button “{draft.ctaLabel}” opens {draft.ctaUrl}
          </span>
        )}
      </div>
    </div>
  );
}

function BannerPreviewWithTargeting({ draft }: { draft: BannerDraft }) {
  return (
    <div className="stack admin-tight-stack">
      <BannerPreview draft={draft} />
      <p className="text-small text-muted">
        Shown to <strong>{humanize(draft.audience)}</strong>
        {draft.countryCode ? ` in ${countryName(draft.countryCode)}` : ''}
        {draft.languageCode ? ` (${draft.languageCode})` : ''}
        {draft.isActive ? '' : ' — currently inactive'}.
      </p>
    </div>
  );
}

export const bannerConfig: ContentConfig<Banner, BannerDraft> = {
  kind: 'banners',
  singular: 'banner',
  fields: FIELDS,
  label: (b) => b.title,
  empty: () => ({
    title: '',
    body: '',
    imageUrl: '',
    ctaLabel: '',
    ctaUrl: '',
    audience: 'Everyone',
    countryCode: '',
    languageCode: '',
    startsAt: '',
    endsAt: '',
    sortOrder: '0',
    isActive: true,
  }),
  fromItem: (b) => ({
    title: b.title,
    body: b.body ?? '',
    imageUrl: b.imageUrl ?? '',
    ctaLabel: b.ctaLabel ?? '',
    ctaUrl: b.ctaUrl ?? '',
    audience: b.audience,
    countryCode: b.countryCode ?? '',
    languageCode: b.languageCode ?? '',
    startsAt: isoToLocalInput(b.startsAt),
    endsAt: isoToLocalInput(b.endsAt),
    sortOrder: String(b.sortOrder),
    isActive: b.isActive,
  }),
  toRequest: (d) => {
    const errors: Record<string, string> = {};
    requireLength(errors, 'title', d.title, 1, 150, 'a title');
    if (d.body.length > 1000) errors.body = 'The body can be at most 1000 characters.';
    if (d.imageUrl.trim() && !isSafeLink(d.imageUrl.trim())) errors.imageUrl = LINK_HINT;
    if (d.ctaUrl.trim() && !isSafeLink(d.ctaUrl.trim())) errors.ctaUrl = LINK_HINT;
    if (d.ctaLabel.trim() && !d.ctaUrl.trim()) errors.ctaUrl = 'Add the link the button opens.';
    if (d.ctaUrl.trim() && !d.ctaLabel.trim()) errors.ctaLabel = 'Add a button label for the link.';
    const startsAt = localInputToIso(d.startsAt);
    const endsAt = localInputToIso(d.endsAt);
    if (startsAt && endsAt && endsAt <= startsAt) errors.endsAt = 'The end must be after the start.';
    const sortOrder = parseSortOrder(errors, d.sortOrder);
    return {
      errors,
      body: {
        title: d.title.trim(),
        body: orNull(d.body),
        imageUrl: orNull(d.imageUrl),
        ctaLabel: orNull(d.ctaLabel),
        ctaUrl: orNull(d.ctaUrl),
        audience: d.audience,
        countryCode: orNull(d.countryCode),
        languageCode: orNull(d.languageCode),
        startsAt,
        endsAt,
        sortOrder,
        isActive: d.isActive,
      },
    };
  },
  Form: BannerForm,
  Preview: BannerPreviewWithTargeting,
};

export function BannersTab() {
  return (
    <ContentTab
      config={bannerConfig}
      title="Banners"
      description="Hero cards at the top of the participant homepage. Only active banners inside their date window and matching the person’s audience, country and language are shown."
      reorderable
      filters={[
        {
          id: 'isActive',
          label: 'Active',
          options: [
            { value: 'true', label: 'Active' },
            { value: 'false', label: 'Inactive' },
          ],
        },
        { id: 'audience', label: 'Audience', options: audienceOptions() },
      ]}
      columns={[
        {
          id: 'title',
          header: 'Banner',
          primary: true,
          cell: (b) => (
            <div className="admin-cell-stack">
              <strong>{b.title}</strong>
              {b.body && <span className="text-small text-muted admin-clamp">{b.body}</span>}
            </div>
          ),
        },
        { id: 'audience', header: 'Audience', cell: (b) => humanize(b.audience) },
        {
          id: 'target',
          header: 'Country / language',
          hideOnMobile: true,
          cell: (b) => [b.countryCode ?? 'All', b.languageCode ?? 'all'].join(' · '),
        },
        {
          id: 'window',
          header: 'Window',
          cell: (b) =>
            b.startsAt || b.endsAt ? (
              <span className="text-small">
                {b.startsAt ? <DateTime value={b.startsAt} format="date" /> : 'Now'} –{' '}
                {b.endsAt ? <DateTime value={b.endsAt} format="date" /> : 'no end'}
              </span>
            ) : (
              'Always'
            ),
        },
        { id: 'order', header: 'Order', align: 'right', cell: (b) => b.sortOrder },
        {
          id: 'active',
          header: 'Status',
          cell: (b) =>
            b.isActive ? <Badge tone="success">Active</Badge> : <Badge tone="neutral">Inactive</Badge>,
        },
      ]}
      renderReorderItem={(b) => (
        <div className="admin-cell-stack">
          <strong>{b.title}</strong>
          <span className="text-small text-muted">
            {humanize(b.audience)} · {b.isActive ? 'Active' : 'Inactive'}
          </span>
        </div>
      )}
    />
  );
}
