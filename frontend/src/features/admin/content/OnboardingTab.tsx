import { ArrowRight } from 'lucide-react';
import { Badge } from '@/components/ui/Badge';
import { buttonClasses } from '@/components/ui/buttonStyles';
import { Stepper } from '@/components/ui/Stepper';
import { Switch } from '@/components/ui/Switch';
import { humanize } from '@/lib/format/text';
import { COMPLETION_RULES, type OnboardingStep } from '../api/types';
import { enumOptions, orNull } from '../shared/common';
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

export interface OnboardingDraft {
  key: string;
  title: string;
  description: string;
  actionLabel: string;
  actionUrl: string;
  completionRule: string;
  sortOrder: string;
  isActive: boolean;
}

const FIELDS = [
  'key',
  'title',
  'description',
  'actionLabel',
  'actionUrl',
  'completionRule',
  'sortOrder',
  'isActive',
] as const;

const RULE_HELP: Record<string, string> = {
  Manual: 'Completed when the participant dismisses it (informational step).',
  EmailVerified: 'Completed once the email address is verified.',
  ProfileCompleted: 'Country, time zone and at least one interest are set.',
  SocialAccountAdded: 'At least one active social account is added.',
  EligibleSocialAccount: 'At least one social account meets the eligibility rules.',
  PayoutProfileAdded: 'Payout details are saved.',
  FirstSubmission: 'The participant submitted their first post.',
  FirstApprovedSubmission: 'A submission has been approved.',
};

const KEY_PATTERN = /^[a-z0-9]+(-[a-z0-9]+)*$/;

function OnboardingForm({ draft, setDraft, errors }: FormProps<OnboardingDraft>) {
  const set = <K extends keyof OnboardingDraft>(key: K, value: OnboardingDraft[K]) =>
    setDraft({ ...draft, [key]: value });
  return (
    <>
      <TextField
        label="Key"
        required
        maxLength={60}
        value={draft.key}
        onChange={(v) => set('key', v)}
        error={errors.key}
        hint="Unique, lower-case letters, digits and single dashes (e.g. add-social-account)."
      />
      <TextField
        label="Title"
        required
        maxLength={150}
        value={draft.title}
        onChange={(v) => set('title', v)}
        error={errors.title}
      />
      <TextAreaField
        label="Description"
        required
        maxLength={1000}
        rows={3}
        value={draft.description}
        onChange={(v) => set('description', v)}
        error={errors.description}
      />
      <SelectField
        label="Completion rule"
        required
        value={draft.completionRule}
        options={enumOptions(COMPLETION_RULES)}
        onChange={(v) => set('completionRule', v)}
        error={errors.completionRule}
        hint={RULE_HELP[draft.completionRule]}
      />
      <div className="admin-form-grid">
        <TextField
          label="Button label"
          optional
          maxLength={60}
          value={draft.actionLabel}
          onChange={(v) => set('actionLabel', v)}
          error={errors.actionLabel}
          hint="Required when a link is set."
        />
        <TextField
          label="Action URL"
          optional
          maxLength={300}
          value={draft.actionUrl}
          onChange={(v) => set('actionUrl', v)}
          error={errors.actionUrl}
          hint={LINK_HINT}
        />
      </div>
      <TextField
        label="Sort order"
        type="number"
        value={draft.sortOrder}
        onChange={(v) => set('sortOrder', v)}
        error={errors.sortOrder}
        hint="Lower numbers come first. Use “Reorder” to arrange steps visually."
      />
      <Switch checked={draft.isActive} onCheckedChange={(v) => set('isActive', v)} label="Active" />
    </>
  );
}

/** Rendered with the same Stepper the participant homepage uses for onboarding. */
function OnboardingPreview({ draft }: { draft: OnboardingDraft }) {
  return (
    <div className="stack admin-tight-stack">
      <Stepper
        label="Onboarding step preview"
        steps={[
          {
            id: 'preview',
            title: draft.title || 'Step title',
            description: draft.description || 'Step description.',
            status: 'current',
            action: draft.actionLabel ? (
              <span className={buttonClasses('primary', 'sm')} aria-hidden="true">
                {draft.actionLabel}
                <ArrowRight />
              </span>
            ) : undefined,
          },
        ]}
      />
      <p className="text-small text-muted">
        Completes when: {RULE_HELP[draft.completionRule] ?? humanize(draft.completionRule)}
      </p>
    </div>
  );
}

export const onboardingConfig: ContentConfig<OnboardingStep, OnboardingDraft> = {
  kind: 'onboarding-steps',
  singular: 'step',
  fields: FIELDS,
  codeToField: { 'content.duplicate_key': 'key' },
  label: (s) => s.title,
  empty: () => ({
    key: '',
    title: '',
    description: '',
    actionLabel: '',
    actionUrl: '',
    completionRule: 'Manual',
    sortOrder: '0',
    isActive: true,
  }),
  fromItem: (s) => ({
    key: s.key,
    title: s.title,
    description: s.description,
    actionLabel: s.actionLabel ?? '',
    actionUrl: s.actionUrl ?? '',
    completionRule: s.completionRule,
    sortOrder: String(s.sortOrder),
    isActive: s.isActive,
  }),
  toRequest: (d) => {
    const errors: Record<string, string> = {};
    const key = d.key.trim();
    if (key.length < 2 || key.length > 60 || !KEY_PATTERN.test(key))
      errors.key = 'Use 2–60 lower-case letters, digits and single dashes, e.g. add-social-account.';
    requireLength(errors, 'title', d.title, 1, 150, 'a title');
    requireLength(errors, 'description', d.description, 1, 1000, 'a description');
    if (d.actionUrl.trim() && !isSafeLink(d.actionUrl.trim())) errors.actionUrl = LINK_HINT;
    if (d.actionUrl.trim() && !d.actionLabel.trim()) errors.actionLabel = 'Add a button label for the link.';
    const sortOrder = parseSortOrder(errors, d.sortOrder);
    return {
      errors,
      body: {
        key,
        title: d.title.trim(),
        description: d.description.trim(),
        actionLabel: orNull(d.actionLabel),
        actionUrl: orNull(d.actionUrl),
        completionRule: d.completionRule,
        sortOrder,
        isActive: d.isActive,
      },
    };
  },
  Form: OnboardingForm,
  Preview: OnboardingPreview,
};

export function OnboardingTab() {
  return (
    <ContentTab
      config={onboardingConfig}
      title="Onboarding steps"
      description="The checklist new participants see on their homepage. Steps complete automatically by their rule."
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
      ]}
      columns={[
        {
          id: 'title',
          header: 'Step',
          primary: true,
          cell: (s) => (
            <div className="admin-cell-stack">
              <strong>{s.title}</strong>
              <code className="text-small">{s.key}</code>
            </div>
          ),
        },
        { id: 'rule', header: 'Completion rule', cell: (s) => humanize(s.completionRule) },
        { id: 'action', header: 'Action', hideOnMobile: true, cell: (s) => s.actionUrl ?? '—' },
        { id: 'order', header: 'Order', align: 'right', cell: (s) => s.sortOrder },
        {
          id: 'active',
          header: 'Status',
          cell: (s) =>
            s.isActive ? <Badge tone="success">Active</Badge> : <Badge tone="neutral">Inactive</Badge>,
        },
      ]}
      renderReorderItem={(s) => (
        <div className="admin-cell-stack">
          <strong>{s.title}</strong>
          <span className="text-small text-muted">{humanize(s.completionRule)}</span>
        </div>
      )}
    />
  );
}
