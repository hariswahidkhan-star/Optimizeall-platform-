/** Client-side copy and ranges for platform settings. Ranges mirror `AdminSettingsService.SettingDefinitions`. */

export interface SettingMeta {
  label: string;
  group: 'Eligibility' | 'Fraud & review' | 'Rates' | 'Retention' | 'Growth';
  /** Inclusive range for integer settings (the server enforces the same). */
  range?: [number, number];
  unit?: string;
  /** What changing this does in practice. */
  impact: string;
}

export const SETTING_META: Record<string, SettingMeta> = {
  'eligibility.minAccountAgeDays': {
    label: 'Minimum social account age',
    group: 'Eligibility',
    range: [0, 3650],
    unit: 'days',
    impact:
      'Social profiles younger than this don’t qualify for campaigns. Raising it makes newer accounts ineligible straight away — participants whose only profiles are too young drop back to “awaiting eligibility” until the profiles age in. Lowering it lets brand-new (and more often fake) accounts take part.',
  },
  'eligibility.minFollowers': {
    label: 'Minimum followers',
    group: 'Eligibility',
    range: [0, 10_000_000],
    unit: 'followers',
    impact:
      'Profiles below this follower count stop qualifying immediately, platform-wide. Individual campaigns can require more, never less.',
  },
  'fraud.submissionVelocityPer24h': {
    label: 'Submission velocity limit',
    group: 'Fraud & review',
    range: [1, 1000],
    unit: 'submissions / 24 h',
    impact:
      'Submissions beyond this many in a rolling 24 hours raise a velocity fraud flag for review. Lower values catch bursts sooner but flag more genuine power users.',
  },
  'fraud.highRiskThreshold': {
    label: 'High-risk score threshold',
    group: 'Fraud & review',
    range: [1, 1000],
    unit: 'risk score',
    impact:
      'Submissions scoring at or above this need senior review. Lowering it sends more work to senior reviewers.',
  },
  'review.claimMinutes': {
    label: 'Reviewer claim duration',
    group: 'Fraud & review',
    range: [1, 240],
    unit: 'minutes',
    impact: 'How long a reviewer keeps a claimed submission before it returns to the shared queue.',
  },
  'review.appealWindowDays': {
    label: 'Appeal window',
    group: 'Fraud & review',
    range: [1, 365],
    unit: 'days',
    impact: 'How long after a decision participants can appeal. Applies to decisions made from now on.',
  },
  'rates.fourEyesIncreasePercent': {
    label: 'Four-eyes threshold for rate increases',
    group: 'Rates',
    range: [0, 1000],
    unit: '%',
    impact:
      'A new rate card version that raises any rate by more than this percentage waits for approval by a second person before it prices posts. 0 turns the check off.',
  },
  'retention.inactivityDays': {
    label: 'Inactivity threshold',
    group: 'Retention',
    range: [7, 365],
    unit: 'days',
    impact:
      'Participants without activity for this long count as inactive: they enter the “Inactive” content audience and become eligible for reactivation messages.',
  },
  'retention.enabled': {
    label: 'Retention automations',
    group: 'Retention',
    impact:
      'Turns onboarding reminders, reactivation messages and campaign alerts on or off for everyone. Messages already queued are still delivered.',
  },
  'referral.program': {
    label: 'Referral program',
    group: 'Growth',
    impact:
      'Controls referral rewards. Changes apply to referrals that qualify from now on; rewards already granted are not changed.',
  },
};

export const REFERRAL_ACTIONS = ['EmailVerified', 'FirstApprovedSubmission', 'FirstPaidPayout'] as const;
export const REFERRAL_CURRENCIES = [
  'AED',
  'AUD',
  'BHD',
  'BRL',
  'CAD',
  'EGP',
  'EUR',
  'GBP',
  'INR',
  'JPY',
  'KWD',
  'MXN',
  'NGN',
  'OMR',
  'PKR',
  'QAR',
  'SAR',
  'TRY',
  'USD',
  'ZAR',
] as const;

export const REFERRAL_FIELDS = [
  'enabled',
  'referrerRewardAmount',
  'currency',
  'qualifyingAction',
  'qualifyWithinDays',
  'requireManualApproval',
  'maxRewardedReferralsPerUser',
] as const;

/**
 * The server reports referral problems as one `value` message made of sentences that start with the property name
 * ("qualifyWithinDays must be from 1 to 365."). Split them back onto the form's fields.
 */
export function splitReferralErrors(messages: string[]): { fields: Record<string, string>; rest: string[] } {
  const fields: Record<string, string> = {};
  const rest: string[] = [];
  const sentences = messages.flatMap((m) => m.split(/(?<=\.)\s+(?=[a-zA-Z]+ must )/));
  for (const sentence of sentences) {
    const field = REFERRAL_FIELDS.find((f) => sentence.toLowerCase().startsWith(`${f.toLowerCase()} `));
    if (field) fields[field] = sentence;
    else rest.push(sentence);
  }
  return { fields, rest };
}
