import type { SelectOption } from '@/components/ui';
import { humanize } from '@/lib/format/text';
import { PARTICIPANT_TIERS, SOCIAL_PLATFORMS } from '../api/types';

export const platformOptions: SelectOption[] = SOCIAL_PLATFORMS.map((p) => ({
  value: p,
  label: p === 'X' ? 'X (Twitter)' : p,
}));

export const tierOptions: SelectOption[] = PARTICIPANT_TIERS.map((t) => ({ value: t, label: t }));

export function enumOptions(values: readonly string[]): SelectOption[] {
  return values.map((v) => ({ value: v, label: humanize(v) }));
}

const RULE_TYPE_LABELS: Record<string, string> = {
  BaseRate: 'Base rate',
  RateOverride: 'Rate override',
  TimeLimitedBonus: 'Time-limited bonus',
  FirstPostBonus: 'First-post bonus',
  QualityBonus: 'Quality bonus',
  PostReward: 'Post reward',
};

export function ruleTypeLabel(type: string): string {
  return RULE_TYPE_LABELS[type] ?? humanize(type);
}

const CAP_LABELS: Record<string, string> = {
  daily_cap: 'Daily cap per participant',
  weekly_cap: 'Weekly cap per participant',
  campaign_cap: 'Campaign cap per participant',
  campaign_budget: 'Campaign budget',
};

export function capLabel(cap: string): string {
  return CAP_LABELS[cap] ?? humanize(cap);
}

const FRAUD_SIGNAL_LABELS: Record<string, string> = {
  shared_device: 'Shared device',
  shared_ip: 'Shared IP address',
  same_ip: 'Same IP address',
  disposable_email: 'Disposable email',
  self_referral: 'Self-referral',
};

export function fraudSignalLabel(signal: string): string {
  return FRAUD_SIGNAL_LABELS[signal] ?? humanize(signal);
}

const RETENTION_LABELS: Record<string, string> = {
  'onboarding.verify_email': 'Onboarding: verify email',
  'onboarding.add_social': 'Onboarding: add a social account',
  'onboarding.first_submission': 'Onboarding: first submission',
  'campaign.alert': 'New campaign alert',
  reactivation: 'Reactivation',
};

export function retentionKindLabel(kind: string): string {
  return RETENTION_LABELS[kind] ?? humanize(kind.replace('.', ' '));
}

/** "a, b ,c" → ['a','b','c'] (trimmed, empty removed, de-duplicated case-insensitively). */
export function splitList(value: string): string[] {
  const seen = new Set<string>();
  return value
    .split(/[,\n]/)
    .map((s) => s.trim())
    .filter((s) => {
      const key = s.toLowerCase();
      if (!s || seen.has(key)) return false;
      seen.add(key);
      return true;
    });
}

/** Parses a numeric input value; '' → null. */
export function toNumberOrNull(value: string): number | null {
  if (value.trim() === '') return null;
  const n = Number(value);
  return Number.isFinite(n) ? n : null;
}
