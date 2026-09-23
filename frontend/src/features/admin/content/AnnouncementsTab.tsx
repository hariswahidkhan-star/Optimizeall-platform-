import { Alert } from '@/components/ui/Alert';
import { Badge } from '@/components/ui/Badge';
import { DateTime } from '@/components/ui/DateTime';
import { Switch } from '@/components/ui/Switch';
import type { Tone } from '@/components/ui/tones';
import { humanize } from '@/lib/format/text';
import { SEVERITIES, type Announcement } from '../api/types';
import { AdminBadge } from '../shared/badges';
import { enumOptions, isoToLocalInput, localInputToIso } from '../shared/common';
import { AUDIENCE_HELP, audienceOptions } from './BannersTab';
import type { ContentConfig, FormProps } from './ContentEditor';
import { ContentTab } from './ContentTab';
import { requireLength, SelectField, TextAreaField, TextField } from './fields';

export interface AnnouncementDraft {
  title: string;
  body: string;
  severity: string;
  audience: string;
  publishAt: string;
  expiresAt: string;
  isActive: boolean;
}

const FIELDS = ['title', 'body', 'severity', 'audience', 'publishAt', 'expiresAt', 'isActive'] as const;

export const SEVERITY_TONE: Record<string, Tone> = {
  Info: 'info',
  Success: 'success',
  Warning: 'warning',
  Critical: 'danger',
};

function AnnouncementForm({ draft, setDraft, errors }: FormProps<AnnouncementDraft>) {
  const set = <K extends keyof AnnouncementDraft>(key: K, value: AnnouncementDraft[K]) =>
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
        label="Message"
        required
        maxLength={5000}
        rows={5}
        value={draft.body}
        onChange={(v) => set('body', v)}
        error={errors.body}
      />
      <div className="admin-form-grid">
        <SelectField
          label="Severity"
          required
          value={draft.severity}
          options={enumOptions(SEVERITIES)}
          onChange={(v) => set('severity', v)}
          error={errors.severity}
          hint="Critical is for outages and urgent account issues."
        />
        <SelectField
          label="Audience"
          required
          value={draft.audience}
          options={audienceOptions()}
          onChange={(v) => set('audience', v)}
          error={errors.audience}
          hint={AUDIENCE_HELP[draft.audience]}
        />
      </div>
      <div className="admin-form-grid">
        <TextField
          label="Publish at"
          required
          type="datetime-local"
          value={draft.publishAt}
          onChange={(v) => set('publishAt', v)}
          error={errors.publishAt}
          hint="Your local time."
        />
        <TextField
          label="Expires at"
          optional
          type="datetime-local"
          value={draft.expiresAt}
          onChange={(v) => set('expiresAt', v)}
          error={errors.expiresAt}
          hint="Leave empty to keep it until deactivated."
        />
      </div>
      <Switch checked={draft.isActive} onCheckedChange={(v) => set('isActive', v)} label="Active" />
    </>
  );
}

/** Announcements render as callouts on the participant homepage, toned by severity. */
function AnnouncementPreview({ draft }: { draft: AnnouncementDraft }) {
  const publishAt = localInputToIso(draft.publishAt);
  return (
    <div className="stack admin-tight-stack">
      <Alert tone={SEVERITY_TONE[draft.severity] ?? 'info'} title={draft.title || 'Announcement title'}>
        <p className="admin-prewrap">{draft.body || 'Your message appears here.'}</p>
      </Alert>
      <p className="text-small text-muted">
        {humanize(draft.audience)} · {publishAt ? <DateTime value={publishAt} /> : 'publish time not set'}
        {draft.isActive ? '' : ' · inactive'}
      </p>
    </div>
  );
}

export const announcementConfig: ContentConfig<Announcement, AnnouncementDraft> = {
  kind: 'announcements',
  singular: 'announcement',
  fields: FIELDS,
  label: (a) => a.title,
  empty: () => ({
    title: '',
    body: '',
    severity: 'Info',
    audience: 'Everyone',
    publishAt: isoToLocalInput(new Date().toISOString()),
    expiresAt: '',
    isActive: true,
  }),
  fromItem: (a) => ({
    title: a.title,
    body: a.body,
    severity: a.severity,
    audience: a.audience,
    publishAt: isoToLocalInput(a.publishAt),
    expiresAt: isoToLocalInput(a.expiresAt),
    isActive: a.isActive,
  }),
  toRequest: (d) => {
    const errors: Record<string, string> = {};
    requireLength(errors, 'title', d.title, 1, 150, 'a title');
    requireLength(errors, 'body', d.body, 1, 5000, 'a message');
    const publishAt = localInputToIso(d.publishAt);
    const expiresAt = localInputToIso(d.expiresAt);
    if (!publishAt) errors.publishAt = 'Choose when to publish.';
    if (publishAt && expiresAt && expiresAt <= publishAt)
      errors.expiresAt = 'The expiry must be after publishing.';
    return {
      errors,
      body: {
        title: d.title.trim(),
        body: d.body.trim(),
        severity: d.severity,
        audience: d.audience,
        publishAt,
        expiresAt,
        isActive: d.isActive,
      },
    };
  },
  Form: AnnouncementForm,
  Preview: AnnouncementPreview,
};

export function AnnouncementsTab() {
  return (
    <ContentTab
      config={announcementConfig}
      title="Announcements"
      description="Short notices on the participant homepage, newest first. Shown between their publish and expiry times to the chosen audience."
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
        { id: 'title', header: 'Announcement', primary: true, cell: (a) => <strong>{a.title}</strong> },
        {
          id: 'severity',
          header: 'Severity',
          cell: (a) => <AdminBadge kind="severity" value={a.severity} />,
        },
        { id: 'audience', header: 'Audience', cell: (a) => humanize(a.audience) },
        { id: 'publishAt', header: 'Publish', cell: (a) => <DateTime value={a.publishAt} /> },
        {
          id: 'expiresAt',
          header: 'Expires',
          hideOnMobile: true,
          cell: (a) => (a.expiresAt ? <DateTime value={a.expiresAt} /> : 'Never'),
        },
        {
          id: 'active',
          header: 'Status',
          cell: (a) =>
            a.isActive ? <Badge tone="success">Active</Badge> : <Badge tone="neutral">Inactive</Badge>,
        },
      ]}
    />
  );
}
