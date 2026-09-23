import { Plus, Trash2 } from 'lucide-react';
import { useMemo, useState } from 'react';
import {
  Alert,
  Button,
  Card,
  CardBody,
  CardHeader,
  Checkbox,
  DataTable,
  DateTime,
  FileDrop,
  FormField,
  IconButton,
  Input,
  RadioGroup,
  Select,
  Switch,
  Textarea,
  useToast,
  type DataTableColumn,
} from '@/components/ui';
import { api } from '@/lib/api/client';
import { errorMessage } from '@/lib/api/errors';
import { browserTimeZone } from '@/lib/format/dates';
import { useEligibilityDefaults } from '@/lib/api/meta';
import { useCategories } from '../api/queries';
import type { UploadedFile } from '../api/types';
import { CheckboxGroup } from '../shared/CheckboxGroup';
import { fieldError, type FieldErrorMap } from '../shared/formErrors';
import { platformOptions, tierOptions } from '../shared/labels';
import { TagInput } from '../shared/TagInput';
import { timeZoneOptions, zonedInputToIso } from '../shared/zonedTime';
import { rowKey, slugify, type CampaignForm, type DisclosureRow } from './formModel';

export interface SectionProps {
  form: CampaignForm;
  set: (patch: Partial<CampaignForm>) => void;
  errors: FieldErrorMap;
  disabled?: boolean;
}

// ---------------------------------------------------------------- basics

export function BasicsSection({ form, set, errors, disabled }: SectionProps) {
  const categories = useCategories();
  const categoryOptions = [
    { value: '', label: 'No category' },
    ...(categories.data ?? [])
      .filter((c) => c.isActive || c.id === form.categoryId)
      .map((c) => ({ value: c.id, label: c.isActive ? c.name : `${c.name} (inactive)` })),
  ];
  const autoSlug = slugify(form.title);

  return (
    <div className="stack">
      <FormField label="Title" required error={fieldError(errors, 'title')}>
        <Input
          value={form.title}
          maxLength={200}
          disabled={disabled}
          onChange={(e) =>
            set(
              form.slugTouched
                ? { title: e.target.value }
                : { title: e.target.value, slug: slugify(e.target.value) },
            )
          }
        />
      </FormField>
      <FormField
        label="Slug"
        hint={
          form.slugTouched
            ? 'Lower-case letters, digits and single hyphens. Used in the campaign URL.'
            : `Generated from the title${autoSlug ? `: ${autoSlug}` : ''}. The server makes it unique. Edit to choose your own.`
        }
        error={fieldError(errors, 'slug')}
        labelAside={
          form.slugTouched && !disabled ? (
            <Button
              variant="link"
              size="sm"
              onClick={() => set({ slugTouched: false, slug: slugify(form.title) })}
            >
              Reset to automatic
            </Button>
          ) : undefined
        }
      >
        <Input
          value={form.slug}
          maxLength={100}
          disabled={disabled}
          spellCheck={false}
          onChange={(e) => set({ slug: e.target.value.toLowerCase(), slugTouched: true })}
        />
      </FormField>
      <FormField
        label="Summary"
        required
        hint={`${form.summary.length}/500 · Shown on campaign cards.`}
        error={fieldError(errors, 'summary')}
      >
        <Textarea
          value={form.summary}
          rows={2}
          maxLength={500}
          disabled={disabled}
          onChange={(e) => set({ summary: e.target.value })}
        />
      </FormField>
      <div className="mg-grid mg-grid--2 mg-align-start">
        <FormField
          label="Description"
          optional
          hint="Plain text. Line breaks are kept."
          error={fieldError(errors, 'description')}
        >
          <Textarea
            value={form.description}
            rows={8}
            maxLength={20000}
            disabled={disabled}
            onChange={(e) => set({ description: e.target.value })}
          />
        </FormField>
        <div className="ui-field">
          <span className="ui-field__label" id="description-preview-label">
            Preview
          </span>
          <div className="mg-prose mg-preview-box" aria-labelledby="description-preview-label" role="region">
            {form.description.trim() ? (
              form.description
            ) : (
              <span className="text-muted">Nothing to preview yet.</span>
            )}
          </div>
        </div>
      </div>
      <div className="mg-grid mg-grid--2">
        <FormField label="Category" optional error={fieldError(errors, 'categoryId')}>
          <Select
            value={form.categoryId}
            options={categoryOptions}
            disabled={disabled || categories.isLoading}
            onChange={(e) => set({ categoryId: e.target.value })}
          />
        </FormField>
        <FormField
          label="Topics"
          optional
          hint="Up to 20. Press Enter or comma to add."
          error={fieldError(errors, 'topics')}
        >
          <TagInput
            value={form.topics}
            max={20}
            itemLabel="topic"
            disabled={disabled}
            normalize={(s) => s.trim().toLowerCase()}
            onChange={(topics) => set({ topics })}
          />
        </FormField>
      </div>
      <RadioGroup
        legend="Visibility"
        value={form.visibility}
        onChange={(v) => set({ visibility: v as CampaignForm['visibility'] })}
        variant="cards"
        orientation="horizontal"
        error={fieldError(errors, 'visibility')?.join(' ')}
        options={[
          {
            value: 'Public',
            label: 'Public',
            description: 'Listed in the campaign catalogue and recommendations for eligible participants.',
          },
          {
            value: 'InviteOnly',
            label: 'Invite only (unlisted)',
            description:
              'Never listed or recommended. Participants reach it only through its link or an invitation; eligibility rules still apply.',
          },
        ]}
      />
    </div>
  );
}

// ---------------------------------------------------------------- schedule

function ZoneEcho({ local, timeZone }: { local: string; timeZone: string }) {
  const iso = local ? zonedInputToIso(local, timeZone) : null;
  const mine = browserTimeZone();
  if (!iso) return null;
  return (
    <span className="mg-zone-echo">
      <DateTime value={iso} timeZone={timeZone} withZone /> campaign time
      {mine !== timeZone && (
        <>
          {' · '}
          <DateTime value={iso} timeZone={mine} withZone /> your time
        </>
      )}
    </span>
  );
}

export function ScheduleSection({ form, set, errors, disabled }: SectionProps) {
  const zones = useMemo(() => timeZoneOptions().map((z) => ({ value: z, label: z.replace(/_/g, ' ') })), []);
  const zoneOptions = zones.some((z) => z.value === form.timeZone)
    ? zones
    : [{ value: form.timeZone, label: form.timeZone }, ...zones];

  return (
    <div className="stack">
      <FormField
        label="Campaign time zone"
        required
        hint="Dates below are entered in this zone. Daily and weekly caps also reset by it."
        error={fieldError(errors, 'timeZone')}
      >
        <Select
          value={form.timeZone}
          options={zoneOptions}
          disabled={disabled}
          onChange={(e) => set({ timeZone: e.target.value })}
        />
      </FormField>
      <div className="mg-grid mg-grid--3 mg-align-start">
        <FormField
          label="Starts"
          required
          hint={<ZoneEcho local={form.startsAt} timeZone={form.timeZone} />}
          error={fieldError(errors, 'startsAt')}
        >
          <Input
            type="datetime-local"
            value={form.startsAt}
            disabled={disabled}
            onChange={(e) => set({ startsAt: e.target.value })}
          />
        </FormField>
        <FormField
          label="Ends"
          required
          hint={<ZoneEcho local={form.endsAt} timeZone={form.timeZone} />}
          error={fieldError(errors, 'endsAt')}
        >
          <Input
            type="datetime-local"
            value={form.endsAt}
            disabled={disabled}
            onChange={(e) => set({ endsAt: e.target.value })}
          />
        </FormField>
        <FormField
          label="Submission deadline"
          optional
          hint={
            form.submissionDeadline ? (
              <ZoneEcho local={form.submissionDeadline} timeZone={form.timeZone} />
            ) : (
              'Blank = 3 days after the end.'
            )
          }
          error={fieldError(errors, 'submissionDeadline')}
        >
          <Input
            type="datetime-local"
            value={form.submissionDeadline}
            disabled={disabled}
            onChange={(e) => set({ submissionDeadline: e.target.value })}
          />
        </FormField>
      </div>
      {form.startsAt && form.endsAt && form.endsAt <= form.startsAt && (
        <Alert tone="warning">The campaign must end after it starts.</Alert>
      )}
      {form.submissionDeadline && form.endsAt && form.submissionDeadline < form.endsAt && (
        <Alert tone="warning">The submission deadline cannot be before the campaign ends.</Alert>
      )}
    </div>
  );
}

// ---------------------------------------------------------------- targeting

export function TargetingSection({ form, set, errors, disabled }: SectionProps) {
  const defaults = useEligibilityDefaults().data;
  return (
    <div className="stack">
      <CheckboxGroup
        legend="Platforms"
        required
        hint="Participants can submit posts from these platforms only."
        options={platformOptions}
        value={form.platforms}
        disabled={disabled}
        error={fieldError(errors, 'platforms')}
        onChange={(platforms) => set({ platforms: platforms as CampaignForm['platforms'] })}
      />
      <div className="mg-grid mg-grid--2">
        <FormField
          label="Countries"
          optional
          hint="Two-letter ISO codes (e.g. PK, AE). Empty = every country."
          error={fieldError(errors, 'eligibility.countries')}
        >
          <TagInput
            value={form.countries}
            itemLabel="country"
            disabled={disabled}
            normalize={(s) => (/^[A-Za-z]{2}$/.test(s.trim()) ? s.trim().toUpperCase() : '')}
            onChange={(countries) => set({ countries })}
          />
        </FormField>
        <FormField
          label="Languages"
          optional
          hint="Language codes (e.g. en, ur). Empty = any language."
          error={fieldError(errors, 'eligibility.languages')}
        >
          <TagInput
            value={form.languages}
            itemLabel="language"
            disabled={disabled}
            normalize={(s) => s.trim().toLowerCase()}
            onChange={(languages) => set({ languages })}
          />
        </FormField>
        <FormField
          label="Interests"
          optional
          hint="Participants need at least one matching interest. Empty = no interest filter."
          error={fieldError(errors, 'eligibility.interests')}
        >
          <TagInput
            value={form.interests}
            itemLabel="interest"
            disabled={disabled}
            normalize={(s) => s.trim().toLowerCase()}
            onChange={(interests) => set({ interests })}
          />
        </FormField>
        <CheckboxGroup
          legend="Tiers"
          hint="Empty = every tier."
          options={tierOptions}
          value={form.tiers}
          disabled={disabled}
          error={fieldError(errors, 'eligibility.tiers')}
          onChange={(tiers) => set({ tiers: tiers as CampaignForm['tiers'] })}
        />
      </div>
      <div className="mg-grid mg-grid--2">
        <FormField label="Minimum followers" error={fieldError(errors, 'eligibility.minFollowers')}>
          <Input
            type="number"
            min={0}
            step={1}
            inputMode="numeric"
            value={form.minFollowers}
            disabled={disabled}
            onChange={(e) => set({ minFollowers: e.target.value })}
          />
        </FormField>
        <FormField
          label="Minimum account age (days)"
          optional
          hint={
            defaults
              ? `Blank = the platform default, currently ${defaults.minAccountAgeDays} days.`
              : 'Blank = the platform default.'
          }
          error={fieldError(errors, 'eligibility.minAccountAgeDays')}
        >
          <Input
            type="number"
            min={0}
            max={3650}
            step={1}
            inputMode="numeric"
            placeholder={defaults ? `Default: ${defaults.minAccountAgeDays}` : undefined}
            value={form.minAccountAgeDays}
            disabled={disabled}
            onChange={(e) => set({ minAccountAgeDays: e.target.value })}
          />
        </FormField>
      </div>
      <Switch
        checked={form.requireVerifiedAccount}
        onCheckedChange={(requireVerifiedAccount) => set({ requireVerifiedAccount })}
        label="Require a verified social account"
        description="Only accounts a reviewer has verified can submit."
        disabled={disabled}
      />
    </div>
  );
}

// ---------------------------------------------------------------- content

export function ContentSection({ form, set, errors, disabled }: SectionProps) {
  const updateRow = (key: string, patch: Partial<DisclosureRow>) =>
    set({ disclosures: form.disclosures.map((r) => (r.key === key ? { ...r, ...patch } : r)) });

  const columns: DataTableColumn<DisclosureRow>[] = [
    {
      id: 'platform',
      header: 'Platform',
      cell: (row) => (
        <Select
          aria-label="Platform"
          size="sm"
          value={row.platform}
          disabled={disabled}
          options={[{ value: '', label: 'Any platform' }, ...platformOptions]}
          onChange={(e) => updateRow(row.key, { platform: e.target.value })}
        />
      ),
    },
    {
      id: 'country',
      header: 'Country',
      cell: (row) => (
        <Input
          aria-label="Country code"
          size="sm"
          value={row.countryCode}
          maxLength={2}
          placeholder="Any"
          disabled={disabled}
          onChange={(e) => updateRow(row.key, { countryCode: e.target.value.toUpperCase() })}
        />
      ),
    },
    {
      id: 'text',
      header: 'Disclosure text',
      primary: true,
      cell: (row) => (
        <Input
          aria-label="Disclosure text"
          size="sm"
          value={row.text}
          maxLength={500}
          disabled={disabled}
          onChange={(e) => updateRow(row.key, { text: e.target.value })}
        />
      ),
    },
    {
      id: 'remove',
      header: <span className="visually-hidden">Remove</span>,
      align: 'right',
      cell: (row) => (
        <IconButton
          size="sm"
          variant="ghost"
          label={`Remove override ${[row.platform || 'any platform', row.countryCode || 'any country'].join(', ')}`}
          icon={<Trash2 />}
          disabled={disabled}
          onClick={() => set({ disclosures: form.disclosures.filter((r) => r.key !== row.key) })}
        />
      ),
    },
  ];

  return (
    <div className="stack">
      <FormField
        label="Posting instructions"
        hint="What participants should post and how. Required to publish unless you add content assets."
        error={fieldError(errors, 'postingInstructions')}
      >
        <Textarea
          value={form.postingInstructions}
          rows={6}
          maxLength={20000}
          disabled={disabled}
          onChange={(e) => set({ postingInstructions: e.target.value })}
        />
      </FormField>
      <div className="mg-grid mg-grid--2">
        <FormField
          label="Required hashtags"
          optional
          hint="e.g. #brand #spring"
          error={fieldError(errors, 'requiredHashtags')}
        >
          <Input
            value={form.requiredHashtags}
            maxLength={300}
            disabled={disabled}
            onChange={(e) => set({ requiredHashtags: e.target.value })}
          />
        </FormField>
        <FormField
          label="Required mentions"
          optional
          hint="e.g. @brand"
          error={fieldError(errors, 'requiredMentions')}
        >
          <Input
            value={form.requiredMentions}
            maxLength={300}
            disabled={disabled}
            onChange={(e) => set({ requiredMentions: e.target.value })}
          />
        </FormField>
      </div>
      <FormField
        label="Default disclosure"
        required
        hint="The paid-content label participants must include (e.g. #ad)."
        error={fieldError(errors, 'defaultDisclosureText')}
      >
        <Input
          value={form.defaultDisclosureText}
          maxLength={300}
          disabled={disabled}
          onChange={(e) => set({ defaultDisclosureText: e.target.value })}
        />
      </FormField>
      <Card flat as="section" aria-labelledby="disclosure-overrides-title">
        <CardHeader
          titleId="disclosure-overrides-title"
          headingLevel={3}
          title="Disclosure overrides"
          description="Per-platform and per-country wording. The most specific match wins: platform + country, then platform, then country, then the default above."
          actions={
            <Button
              size="sm"
              variant="secondary"
              leadingIcon={<Plus />}
              disabled={disabled}
              onClick={() =>
                set({
                  disclosures: [
                    ...form.disclosures,
                    { key: rowKey(), platform: '', countryCode: '', text: '' },
                  ],
                })
              }
            >
              Add override
            </Button>
          }
        />
        <CardBody className="stack">
          {fieldError(errors, 'disclosures') && (
            <Alert tone="danger" role="alert">
              {fieldError(errors, 'disclosures')!.join(' ')}
            </Alert>
          )}
          <DataTable
            caption="Disclosure overrides"
            columns={columns}
            rows={form.disclosures}
            getRowId={(r) => r.key}
            emptyState={<p className="text-muted">No overrides. Every post uses the default disclosure.</p>}
          />
        </CardBody>
      </Card>
    </div>
  );
}

// ---------------------------------------------------------------- verification

export function VerificationSection({ form, set, errors, disabled }: SectionProps) {
  return (
    <div className="stack">
      <div className="mg-grid mg-grid--2">
        <FormField
          label="Minimum hours the post stays live"
          hint="0 = no live check. Otherwise earnings stay pending until the post is confirmed live after this many hours (max 720)."
          error={fieldError(errors, 'minPostLiveHours')}
        >
          <Input
            type="number"
            min={0}
            max={720}
            step={1}
            inputMode="numeric"
            value={form.minPostLiveHours}
            disabled={disabled}
            onChange={(e) => set({ minPostLiveHours: e.target.value })}
          />
        </FormField>
        <FormField
          label="Maximum submissions per participant"
          hint="1–100. Rejected submissions don't count."
          error={fieldError(errors, 'maxSubmissionsPerParticipant')}
        >
          <Input
            type="number"
            min={1}
            max={100}
            step={1}
            inputMode="numeric"
            value={form.maxSubmissionsPerParticipant}
            disabled={disabled}
            onChange={(e) => set({ maxSubmissionsPerParticipant: e.target.value })}
          />
        </FormField>
      </div>
      <Checkbox
        label="Require a screenshot with every submission"
        description="Reviewers see the screenshot next to the post link."
        checked={form.requireScreenshot}
        disabled={disabled}
        onChange={(e) => set({ requireScreenshot: e.target.checked })}
      />
    </div>
  );
}

// ---------------------------------------------------------------- landing & tracking

/** Uploads an image to `/admin/files` and returns its URL. */
export function ImageUpload({
  label,
  onUploaded,
  disabled,
}: {
  label: string;
  onUploaded: (file: UploadedFile) => void;
  disabled?: boolean;
}) {
  const toast = useToast();
  const [file, setFile] = useState<File | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const upload = async () => {
    if (!file) return;
    setBusy(true);
    setError(null);
    try {
      const form = new FormData();
      form.append('file', file);
      form.append('purpose', 'CampaignAsset');
      const uploaded = await api.upload<UploadedFile>('/admin/files', form);
      onUploaded(uploaded);
      setFile(null);
      toast.success('Image uploaded');
    } catch (err) {
      setError(errorMessage(err));
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="stack mg-stack-sm">
      <FileDrop
        label={label}
        value={file}
        onChange={setFile}
        maxSizeBytes={10 * 1024 * 1024}
        hint="PNG, JPEG or WebP, up to 10 MB, at least 200×200 px."
        error={error}
        disabled={disabled || busy}
      />
      {file && (
        <div>
          <Button size="sm" onClick={upload} loading={busy} disabled={disabled}>
            Upload image
          </Button>
        </div>
      )}
    </div>
  );
}

export function LandingSection({ form, set, errors, disabled }: SectionProps) {
  return (
    <div className="stack">
      <Card flat as="section" aria-labelledby="landing-title">
        <CardHeader
          titleId="landing-title"
          headingLevel={3}
          title="Landing page"
          description="Shown on the public campaign page and invitation links. Headline and body fall back to the title and summary."
        />
        <CardBody className="stack">
          <FormField label="Headline" optional error={fieldError(errors, 'landingHeadline')}>
            <Input
              value={form.landingHeadline}
              maxLength={200}
              disabled={disabled}
              onChange={(e) => set({ landingHeadline: e.target.value })}
            />
          </FormField>
          <FormField label="Body" optional error={fieldError(errors, 'landingBody')}>
            <Textarea
              value={form.landingBody}
              rows={5}
              maxLength={20000}
              disabled={disabled}
              onChange={(e) => set({ landingBody: e.target.value })}
            />
          </FormField>
          <FormField
            label="Hero image URL"
            optional
            hint="An https URL or an uploaded file (/api/v1/files/…)."
            error={fieldError(errors, 'heroImageUrl')}
          >
            <Input
              value={form.heroImageUrl}
              maxLength={500}
              disabled={disabled}
              onChange={(e) => set({ heroImageUrl: e.target.value })}
            />
          </FormField>
          {form.heroImageUrl && (
            <img className="mg-hero-preview" src={form.heroImageUrl} alt="Current hero" />
          )}
          <ImageUpload
            label="Or upload a hero image"
            disabled={disabled}
            onUploaded={(file) => set({ heroImageUrl: file.url })}
          />
        </CardBody>
      </Card>
      <Card flat as="section" aria-labelledby="tracking-title">
        <CardHeader
          titleId="tracking-title"
          headingLevel={3}
          title="Tracking"
          description="Participants get their own short link that redirects here. utm_source, utm_medium, utm_campaign and utm_content (the participant's referral code) are appended per participant, so clicks and verified conversions are attributed to them."
        />
        <CardBody className="stack">
          <FormField
            label="Destination URL"
            optional
            hint="Absolute https URL. Leave blank to disable tracking links for this campaign."
            error={fieldError(errors, 'trackingDestinationUrl')}
          >
            <Input
              type="url"
              value={form.trackingDestinationUrl}
              maxLength={1000}
              placeholder="https://"
              disabled={disabled}
              onChange={(e) => set({ trackingDestinationUrl: e.target.value })}
            />
          </FormField>
          <FormField
            label="UTM campaign"
            optional
            hint="Value of utm_campaign. Blank = the campaign slug."
            error={fieldError(errors, 'utmCampaign')}
          >
            <Input
              value={form.utmCampaign}
              maxLength={100}
              disabled={disabled}
              onChange={(e) => set({ utmCampaign: e.target.value })}
            />
          </FormField>
        </CardBody>
      </Card>
    </div>
  );
}
