import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { RotateCcw, Save } from 'lucide-react';
import { useEffect, useState, type FormEvent } from 'react';
import { Alert } from '@/components/ui/Alert';
import { Badge } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';
import { Card, CardBody, CardFooter, CardHeader } from '@/components/ui/Card';
import { ConfirmDialog } from '@/components/ui/ConfirmDialog';
import { DateTime } from '@/components/ui/DateTime';
import { FormField } from '@/components/ui/FormField';
import { Input } from '@/components/ui/Input';
import { PageHeader } from '@/components/ui/PageHeader';
import { Select } from '@/components/ui/Select';
import { SkeletonText } from '@/components/ui/Skeleton';
import { Switch } from '@/components/ui/Switch';
import { useToast } from '@/components/ui/toastContext';
import { api } from '@/lib/api/client';
import { isApiError } from '@/lib/api/errors';
import { formatNumber } from '@/lib/format/money';
import { humanize } from '@/lib/format/text';
import type { ReferralProgram, Setting, SettingValue } from '../api/types';
import { enumOptions, QueryError } from '../shared/common';
import { mapFieldErrors, toDisplayError } from '../shared/errors';
import {
  REFERRAL_ACTIONS,
  REFERRAL_CURRENCIES,
  SETTING_META,
  splitReferralErrors,
  type SettingMeta,
} from './settingsMeta';

export const settingsQueryKey = ['admin', 'settings'] as const;

type ReferralDraft = Record<keyof ReferralProgram, string | boolean>;

function formatValue(setting: Setting, value: SettingValue): string {
  if (value === null || value === undefined) return '—';
  if (typeof value === 'boolean') return value ? 'On' : 'Off';
  if (typeof value === 'number') {
    const unit = SETTING_META[setting.key]?.unit;
    return `${formatNumber(value)}${unit ? ` ${unit}` : ''}`;
  }
  if (typeof value === 'object') {
    const r = value as Partial<ReferralProgram>;
    if ('referrerRewardAmount' in r) {
      return `${r.enabled ? 'Enabled' : 'Disabled'} · ${r.referrerRewardAmount} ${r.currency} on ${humanize(
        String(r.qualifyingAction ?? ''),
      )} within ${r.qualifyWithinDays} days`;
    }
    return JSON.stringify(value);
  }
  return String(value);
}

function toReferralDraft(value: SettingValue): ReferralDraft {
  const v = (value ?? {}) as Partial<ReferralProgram>;
  return {
    enabled: v.enabled ?? true,
    referrerRewardAmount: String(v.referrerRewardAmount ?? ''),
    currency: v.currency ?? 'USD',
    qualifyingAction: v.qualifyingAction ?? 'FirstApprovedSubmission',
    qualifyWithinDays: String(v.qualifyWithinDays ?? ''),
    requireManualApproval: v.requireManualApproval ?? true,
    maxRewardedReferralsPerUser: String(v.maxRewardedReferralsPerUser ?? ''),
  };
}

/** Validates a draft and returns the JSON value to send, or field errors. */
function parseDraft(
  setting: Setting,
  meta: SettingMeta | undefined,
  draft: string | boolean | ReferralDraft,
): { value?: SettingValue; errors: Record<string, string> } {
  if (setting.valueType === 'boolean') return { value: Boolean(draft), errors: {} };
  if (setting.valueType === 'string') {
    const text = String(draft).trim();
    if (meta?.maxLength && text.length > meta.maxLength) return { errors: { value: `Use at most ${meta.maxLength} characters.` } };
    return { value: text, errors: {} };
  }
  if (setting.valueType === 'integer') {
    const text = String(draft).trim();
    const n = Number(text);
    const [min, max] = meta?.range ?? [Number.MIN_SAFE_INTEGER, Number.MAX_SAFE_INTEGER];
    if (text === '' || !Number.isInteger(n) || n < min || n > max)
      return { errors: { value: `Use a whole number from ${formatNumber(min)} to ${formatNumber(max)}.` } };
    return { value: n, errors: {} };
  }
  const d = draft as ReferralDraft;
  const errors: Record<string, string> = {};
  const num = (key: keyof ReferralProgram, min: number, max: number, integer: boolean) => {
    const text = String(d[key]).trim();
    const n = Number(text);
    if (text === '' || Number.isNaN(n) || n < min || n > max || (integer && !Number.isInteger(n))) {
      errors[key] =
        `Use ${integer ? 'a whole number' : 'an amount'} from ${formatNumber(min)} to ${formatNumber(max)}.`;
    }
    return n;
  };
  const value: ReferralProgram = {
    enabled: Boolean(d.enabled),
    referrerRewardAmount: num('referrerRewardAmount', 0, 100_000, false),
    currency: String(d.currency),
    qualifyingAction: String(d.qualifyingAction),
    qualifyWithinDays: num('qualifyWithinDays', 1, 365, true),
    requireManualApproval: Boolean(d.requireManualApproval),
    maxRewardedReferralsPerUser: num('maxRewardedReferralsPerUser', 0, 100_000, true),
  };
  return Object.keys(errors).length ? { errors } : { value: value as unknown as SettingValue, errors: {} };
}

function initialDraft(setting: Setting): string | boolean | ReferralDraft {
  if (setting.valueType === 'boolean') return Boolean(setting.value);
  if (setting.valueType === 'object') return toReferralDraft(setting.value);
  return setting.value === null || setting.value === undefined ? '' : String(setting.value);
}

function SettingCard({ setting }: { setting: Setting }) {
  const meta = SETTING_META[setting.key];
  const toast = useToast();
  const queryClient = useQueryClient();
  const titleId = `setting-${setting.key.replace(/\W/g, '-')}`;
  const [draft, setDraft] = useState(() => initialDraft(setting));
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [formError, setFormError] = useState<string | null>(null);
  const [pending, setPending] = useState<SettingValue | undefined>(undefined);
  const [resetting, setResetting] = useState(false);

  const serialized = JSON.stringify(setting.value);
  useEffect(() => {
    setDraft(initialDraft(setting));
    // Reset the draft whenever the server value changes (after save or refetch).
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [serialized]);

  const save = useMutation({
    mutationFn: ({ value, reason }: { value: SettingValue; reason: string }) =>
      api.put<Setting>(`/admin/settings/${encodeURIComponent(setting.key)}`, {
        value,
        reason,
        confirm: true,
      }),
    onSuccess: (updated) => {
      queryClient.setQueryData<Setting[]>(settingsQueryKey, (list) =>
        list?.map((s) => (s.key === updated.key ? updated : s)),
      );
      toast.success(
        'Setting saved',
        `${meta?.label ?? setting.key} is now ${formatValue(updated, updated.value)}.`,
      );
    },
  });

  const parsed = parseDraft(setting, meta, draft);
  const dirty = parsed.value !== undefined ? JSON.stringify(parsed.value) !== serialized : true;

  const submit = (event: FormEvent) => {
    event.preventDefault();
    setFormError(null);
    setErrors(parsed.errors);
    if (parsed.value === undefined) return;
    if (!dirty) {
      setFormError('Nothing to save — the value is unchanged.');
      return;
    }
    setPending(parsed.value);
  };

  const onConfirm = async ({ reason }: { reason: string }) => {
    if (pending === undefined) return;
    try {
      await save.mutateAsync({ value: pending, reason });
      setErrors({});
    } catch (error) {
      // Field validation: close the dialog and show the messages on the control they belong to.
      if (isApiError(error) && error.status === 400 && error.errors) {
        const mapped = mapFieldErrors(error, ['value']);
        const messages = mapped.fields.value ?? [];
        if (setting.valueType === 'object') {
          const split = splitReferralErrors(messages);
          setErrors(split.fields);
          setFormError(split.rest.length ? split.rest.join(' ') : null);
        } else {
          setErrors(messages.length ? { value: messages.join(' ') } : {});
        }
        if (mapped.form) setFormError(mapped.form.details.join(' ') || mapped.form.title);
        toast.error('Setting not saved', 'Check the highlighted value.');
        return;
      }
      throw toDisplayError(error);
    }
  };

  const range = meta?.range;
  let control;
  if (setting.valueType === 'boolean') {
    control = (
      <Switch
        checked={Boolean(draft)}
        onCheckedChange={(checked) => setDraft(checked)}
        label={meta?.label ?? setting.key}
        description={draft ? 'On' : 'Off'}
      />
    );
  } else if (setting.valueType === 'string') {
    control = (
      <FormField label="New value" error={errors.value}>
        <Input value={String(draft)} maxLength={meta?.maxLength} onChange={(e) => setDraft(e.target.value)} />
      </FormField>
    );
  } else if (setting.valueType === 'integer') {
    control = (
      <FormField
        label="New value"
        error={errors.value}
        hint={
          range
            ? `Allowed: ${formatNumber(range[0])}–${formatNumber(range[1])}${meta?.unit ? ` ${meta.unit}` : ''}.`
            : undefined
        }
      >
        <Input
          type="number"
          inputMode="numeric"
          min={range?.[0]}
          max={range?.[1]}
          step={1}
          value={String(draft)}
          onChange={(e) => setDraft(e.target.value)}
          className="admin-number-input"
        />
      </FormField>
    );
  } else {
    const d = draft as ReferralDraft;
    const set = (key: keyof ReferralProgram, value: string | boolean) => setDraft({ ...d, [key]: value });
    control = (
      <div className="stack">
        <Switch
          checked={Boolean(d.enabled)}
          onCheckedChange={(v) => set('enabled', v)}
          label="Referral program enabled"
          description="When off, new referrals are still recorded but earn no reward."
        />
        <div className="admin-form-grid">
          <FormField
            label="Referrer reward"
            error={errors.referrerRewardAmount}
            hint="0–100,000, rounded to the currency."
          >
            <Input
              type="number"
              inputMode="decimal"
              min={0}
              max={100000}
              step="any"
              value={String(d.referrerRewardAmount)}
              onChange={(e) => set('referrerRewardAmount', e.target.value)}
            />
          </FormField>
          <FormField label="Currency" error={errors.currency}>
            <Select
              value={String(d.currency)}
              options={REFERRAL_CURRENCIES.map((c) => ({ value: c, label: c }))}
              onChange={(e) => set('currency', e.target.value)}
            />
          </FormField>
          <FormField
            label="Qualifying action"
            error={errors.qualifyingAction}
            hint="What the referred person must do."
          >
            <Select
              value={String(d.qualifyingAction)}
              options={enumOptions(REFERRAL_ACTIONS)}
              onChange={(e) => set('qualifyingAction', e.target.value)}
            />
          </FormField>
          <FormField
            label="Qualify within"
            error={errors.qualifyWithinDays}
            hint="Days after sign-up, 1–365."
          >
            <Input
              type="number"
              inputMode="numeric"
              min={1}
              max={365}
              value={String(d.qualifyWithinDays)}
              onChange={(e) => set('qualifyWithinDays', e.target.value)}
            />
          </FormField>
          <FormField
            label="Max rewarded referrals per user"
            error={errors.maxRewardedReferralsPerUser}
            hint="0–100,000. 0 means no rewards."
          >
            <Input
              type="number"
              inputMode="numeric"
              min={0}
              max={100000}
              value={String(d.maxRewardedReferralsPerUser)}
              onChange={(e) => set('maxRewardedReferralsPerUser', e.target.value)}
            />
          </FormField>
        </div>
        <Switch
          checked={Boolean(d.requireManualApproval)}
          onCheckedChange={(v) => set('requireManualApproval', v)}
          label="Require manual approval"
          description="Marketing staff approve each referral reward before it is credited."
        />
      </div>
    );
  }

  return (
    <Card as="section" aria-labelledby={titleId}>
      <CardHeader
        titleId={titleId}
        headingLevel={3}
        title={meta?.label ?? setting.key}
        description={setting.description}
        actions={
          setting.isDefault ? <Badge tone="neutral">Default</Badge> : <Badge tone="brand">Customised</Badge>
        }
      />
      <form onSubmit={submit} noValidate aria-labelledby={titleId}>
        <CardBody className="stack">
          <p className="text-small admin-setting-key">
            <code>{setting.key}</code>
          </p>
          {meta?.impact && <p className="text-small text-muted">{meta.impact}</p>}
          <dl className="admin-facts">
            <div>
              <dt>Current</dt>
              <dd>{formatValue(setting, setting.value)}</dd>
            </div>
            <div>
              <dt>Default</dt>
              <dd>{formatValue(setting, setting.defaultValue)}</dd>
            </div>
            <div>
              <dt>Last updated</dt>
              <dd>
                {setting.updatedAt ? (
                  <>
                    <DateTime value={setting.updatedAt} /> by {setting.updatedBy?.displayName ?? 'unknown'}
                  </>
                ) : (
                  'Never changed'
                )}
              </dd>
            </div>
          </dl>
          {control}
          {formError && (
            <Alert tone="danger" role="alert">
              {formError}
            </Alert>
          )}
        </CardBody>
        <CardFooter className="cluster">
          <Button type="submit" leadingIcon={<Save />} loading={save.isPending}>
            Save…
          </Button>
          <Button
            variant="ghost"
            leadingIcon={<RotateCcw />}
            disabled={!dirty || save.isPending}
            onClick={() => {
              setDraft(initialDraft(setting));
              setErrors({});
              setFormError(null);
            }}
          >
            Discard changes
          </Button>
          {!setting.isDefault && (
            <Button variant="ghost" disabled={save.isPending} onClick={() => setResetting(true)}>
              Restore default…
            </Button>
          )}
        </CardFooter>
      </form>
      <ConfirmDialog
        open={resetting}
        onClose={() => setResetting(false)}
        tone="danger"
        title={`Restore the default for ${meta?.label ?? setting.key}?`}
        description="The saved value is removed and the built-in default applies immediately for everyone."
        confirmLabel="Restore default"
        requireReason
        onConfirm={async ({ reason }) => {
          try {
            const updated = await api.post<Setting>(`/admin/settings/${encodeURIComponent(setting.key)}/reset`, { reason, confirm: true });
            queryClient.setQueryData<Setting[]>(settingsQueryKey, (list) => list?.map((s) => (s.key === updated.key ? updated : s)));
            toast.success('Default restored', `${meta?.label ?? setting.key} is now ${formatValue(updated, updated.value)}.`);
          } catch (error) {
            throw toDisplayError(error);
          }
        }}
      >
        <KeyChange from={formatValue(setting, setting.value)} to={formatValue(setting, setting.defaultValue)} />
      </ConfirmDialog>
      <ConfirmDialog
        open={pending !== undefined}
        onClose={() => setPending(undefined)}
        tone="danger"
        title={`Change ${meta?.label ?? setting.key}?`}
        description="Platform settings take effect immediately for everyone."
        confirmLabel="Save setting"
        requireReason
        onConfirm={onConfirm}
      >
        <KeyChange
          from={formatValue(setting, setting.value)}
          to={pending === undefined ? '' : formatValue(setting, pending)}
        />
      </ConfirmDialog>
    </Card>
  );
}

function KeyChange({ from, to }: { from: string; to: string }) {
  return (
    <dl className="admin-facts">
      <div>
        <dt>From</dt>
        <dd>{from}</dd>
      </div>
      <div>
        <dt>To</dt>
        <dd>
          <strong>{to}</strong>
        </dd>
      </div>
    </dl>
  );
}

/** A fragment-safe id for a group heading (the section nav links to it). */
const groupId = (group: string) => `group-${group.toLowerCase().replace(/[^a-z0-9]+/g, '-')}`;

const GROUP_ORDER: SettingMeta['group'][] = ['Eligibility', 'Fraud & review', 'Rates', 'Retention', 'Growth', 'Learning'];

export function SettingsPage() {
  const settings = useQuery({
    queryKey: settingsQueryKey,
    queryFn: ({ signal }) => api.get<Setting[]>('/admin/settings', { signal }),
  });

  const groups = GROUP_ORDER.map((group) => ({
    group,
    items: (settings.data ?? []).filter((s) => (SETTING_META[s.key]?.group ?? 'Growth') === group),
  })).filter((g) => g.items.length > 0);

  return (
    <>
      <PageHeader
        title="Settings"
        description="Platform-wide rules. Every change needs a reason, takes effect immediately and is recorded in the audit log."
      />
      <Alert tone="warning" title="These settings are sensitive">
        They change who can take part, how fraud is detected and what people are paid. Double-check the value
        before saving; you can always see previous values in the audit log.
      </Alert>
      {settings.isPending ? (
        <Card>
          <CardBody>
            <SkeletonText lines={8} />
          </CardBody>
        </Card>
      ) : settings.isError ? (
        <QueryError error={settings.error} onRetry={() => void settings.refetch()} />
      ) : (
        <div className="admin-settings-layout">
          <nav className="admin-settings-nav" aria-label="Setting groups">
            <ul>
              {groups.map(({ group, items }) => (
                <li key={group}>
                  <a href={`#${groupId(group)}`}>
                    <span>{group}</span>
                    <span className="admin-settings-nav__count" aria-hidden="true">
                      {items.length}
                    </span>
                  </a>
                </li>
              ))}
            </ul>
          </nav>
          <div className="admin-settings-sections">
            {groups.map(({ group, items }) => (
              <section key={group} className="stack admin-settings-group" aria-labelledby={groupId(group)}>
                <h2 id={groupId(group)} className="admin-section-title">
                  {group}
                </h2>
                <div className="admin-settings-grid">
                  {items.map((s) => (
                    <SettingCard key={s.key} setting={s} />
                  ))}
                </div>
              </section>
            ))}
          </div>
        </div>
      )}
    </>
  );
}
