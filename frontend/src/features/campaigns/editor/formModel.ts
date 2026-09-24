import type {
  AdminCampaign,
  BonusApprovalMode,
  CampaignFieldsInput,
  CampaignVisibility,
  Disclosure,
  DisclosureInput,
  ParticipantTier,
  RewardRuleInput,
  RewardRuleSet,
  RewardRuleSetInput,
  RewardRuleType,
  PersonalRatesMode,
  SocialPlatform,
} from '../api/types';
import { isoToZonedInput, zonedInputToIso } from '../shared/zonedTime';

/**
 * Editor form state. Every value is kept as the user typed it (strings for numbers and wall-clock dates in the
 * campaign's time zone) and converted to the API shape on save.
 */
export interface CampaignForm {
  title: string;
  slug: string;
  /** False until the user edits the slug; while false it follows the title. */
  slugTouched: boolean;
  summary: string;
  description: string;
  categoryId: string;
  topics: string[];
  visibility: CampaignVisibility;
  timeZone: string;
  startsAt: string;
  endsAt: string;
  submissionDeadline: string;
  platforms: SocialPlatform[];
  countries: string[];
  languages: string[];
  interests: string[];
  tiers: ParticipantTier[];
  minFollowers: string;
  minAccountAgeDays: string;
  requireVerifiedAccount: boolean;
  postingInstructions: string;
  requiredHashtags: string;
  requiredMentions: string;
  defaultDisclosureText: string;
  disclosures: DisclosureRow[];
  budgetAmount: string;
  maxSubmissionsPerParticipant: string;
  minPostLiveHours: string;
  requireScreenshot: boolean;
  landingHeadline: string;
  landingBody: string;
  heroImageUrl: string;
  trackingDestinationUrl: string;
  utmCampaign: string;
}

export interface DisclosureRow {
  key: string;
  platform: string;
  countryCode: string;
  text: string;
}

export interface RuleRow {
  key: string;
  type: RewardRuleType;
  amount: string;
  platform: string;
  countryCode: string;
  tier: string;
  validFrom: string;
  validTo: string;
  approvalMode: BonusApprovalMode;
  priority: string;
  label: string;
}

export interface RulesForm {
  currency: string;
  dailyCap: string;
  weeklyCap: string;
  campaignCap: string;
  /** Whether rate cards / groups / personal deals may replace the campaign rate. */
  personalRatesMode: PersonalRatesMode;
  /** Ceiling on personal rates as a multiple of the campaign rate ('' = none). */
  personalRateMaxMultiplier: string;
  rules: RuleRow[];
}

let seq = 0;
export const rowKey = () => `row-${Date.now().toString(36)}-${(seq += 1)}`;

/** Lower-case hyphenated slug, mirroring the backend's `CampaignText.Slugify`. */
export function slugify(title: string): string {
  return title
    .normalize('NFKD')
    .replace(/[̀-ͯ]/g, '')
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')
    .slice(0, 100)
    .replace(/-+$/g, '');
}

const numStr = (n: number | null | undefined) => (n === null || n === undefined ? '' : String(n));
const orNull = (s: string) => (s.trim() === '' ? null : s.trim());
const numOrNull = (s: string): number | null => {
  if (s.trim() === '') return null;
  const n = Number(s);
  return Number.isFinite(n) ? n : null;
};

function defaultStart(): Date {
  const d = new Date(Date.now() + 24 * 3600_000);
  d.setUTCMinutes(0, 0, 0);
  return d;
}

export function emptyForm(timeZone: string): CampaignForm {
  const start = defaultStart();
  const end = new Date(start.getTime() + 30 * 24 * 3600_000);
  const deadline = new Date(end.getTime() + 3 * 24 * 3600_000);
  return {
    title: '',
    slug: '',
    slugTouched: false,
    summary: '',
    description: '',
    categoryId: '',
    topics: [],
    visibility: 'Public',
    timeZone,
    startsAt: isoToZonedInput(start.toISOString(), timeZone),
    endsAt: isoToZonedInput(end.toISOString(), timeZone),
    submissionDeadline: isoToZonedInput(deadline.toISOString(), timeZone),
    platforms: [],
    countries: [],
    languages: [],
    interests: [],
    tiers: [],
    minFollowers: '0',
    minAccountAgeDays: '',
    requireVerifiedAccount: false,
    postingInstructions: '',
    requiredHashtags: '',
    requiredMentions: '',
    defaultDisclosureText: '#ad',
    disclosures: [],
    budgetAmount: '',
    maxSubmissionsPerParticipant: '1',
    minPostLiveHours: '0',
    requireScreenshot: true,
    landingHeadline: '',
    landingBody: '',
    heroImageUrl: '',
    trackingDestinationUrl: '',
    utmCampaign: '',
  };
}

export function disclosureRows(disclosures: Disclosure[]): DisclosureRow[] {
  return disclosures.map((d) => ({
    key: d.id,
    platform: d.platform ?? '',
    countryCode: d.countryCode ?? '',
    text: d.text,
  }));
}

export function formFromCampaign(c: AdminCampaign): CampaignForm {
  const tz = c.timeZone || 'UTC';
  return {
    title: c.title,
    slug: c.slug,
    slugTouched: true,
    summary: c.summary,
    description: c.description ?? '',
    categoryId: c.category?.id ?? '',
    topics: [...c.topics],
    visibility: c.visibility,
    timeZone: tz,
    startsAt: isoToZonedInput(c.startsAt, tz),
    endsAt: isoToZonedInput(c.endsAt, tz),
    submissionDeadline: isoToZonedInput(c.submissionDeadline, tz),
    platforms: [...c.platforms],
    countries: [...c.eligibility.countries],
    languages: [...c.eligibility.languages],
    interests: [...c.eligibility.interests],
    tiers: [...c.eligibility.tiers],
    minFollowers: String(c.eligibility.minFollowers ?? 0),
    minAccountAgeDays: numStr(c.eligibility.minAccountAgeDays),
    requireVerifiedAccount: c.eligibility.requireVerifiedAccount,
    postingInstructions: c.postingInstructions ?? '',
    requiredHashtags: c.requiredHashtags ?? '',
    requiredMentions: c.requiredMentions ?? '',
    defaultDisclosureText: c.defaultDisclosureText ?? '',
    disclosures: disclosureRows(c.disclosures),
    budgetAmount: numStr(c.budgetAmount),
    maxSubmissionsPerParticipant: String(c.maxSubmissionsPerParticipant),
    minPostLiveHours: String(c.minPostLiveHours),
    requireScreenshot: c.requireScreenshot,
    landingHeadline: c.landingHeadline ?? '',
    landingBody: c.landingBody ?? '',
    heroImageUrl: c.heroImageUrl ?? '',
    trackingDestinationUrl: c.trackingDestinationUrl ?? '',
    utmCampaign: c.utmCampaign ?? '',
  };
}

/** Form → create/update body fields. `ruleCurrency` is the reward currency (the budget is always in it). */
export function fieldsFromForm(form: CampaignForm, ruleCurrency: string): CampaignFieldsInput {
  const tz = form.timeZone || 'UTC';
  const budget = numOrNull(form.budgetAmount);
  return {
    title: form.title.trim(),
    slug: orNull(form.slug),
    summary: form.summary.trim(),
    description: form.description,
    categoryId: form.categoryId || null,
    topics: form.topics,
    visibility: form.visibility,
    startsAt: zonedInputToIso(form.startsAt, tz) ?? '',
    endsAt: zonedInputToIso(form.endsAt, tz) ?? '',
    submissionDeadline: zonedInputToIso(form.submissionDeadline, tz),
    timeZone: tz,
    postingInstructions: form.postingInstructions,
    defaultDisclosureText: form.defaultDisclosureText.trim(),
    requiredHashtags: orNull(form.requiredHashtags),
    requiredMentions: orNull(form.requiredMentions),
    budgetAmount: budget,
    budgetCurrency: budget === null ? null : ruleCurrency,
    maxSubmissionsPerParticipant: numOrNull(form.maxSubmissionsPerParticipant) ?? 1,
    minPostLiveHours: numOrNull(form.minPostLiveHours) ?? 0,
    requireScreenshot: form.requireScreenshot,
    eligibility: {
      minAccountAgeDays: numOrNull(form.minAccountAgeDays),
      minFollowers: numOrNull(form.minFollowers) ?? 0,
      requireVerifiedAccount: form.requireVerifiedAccount,
      countries: form.countries,
      languages: form.languages,
      interests: form.interests,
      tiers: form.tiers,
    },
    platforms: form.platforms,
    landingHeadline: orNull(form.landingHeadline),
    landingBody: orNull(form.landingBody),
    heroImageUrl: orNull(form.heroImageUrl),
    trackingDestinationUrl: orNull(form.trackingDestinationUrl),
    utmCampaign: orNull(form.utmCampaign),
  };
}

export function disclosuresFromForm(rows: DisclosureRow[]): DisclosureInput[] {
  return rows
    .filter((r) => r.text.trim() !== '')
    .map((r) => ({
      platform: (r.platform || null) as SocialPlatform | null,
      countryCode: r.countryCode.trim() ? r.countryCode.trim().toUpperCase() : null,
      text: r.text.trim(),
    }));
}

// ---------------------------------------------------------------- rules

export function newRule(type: RewardRuleType): RuleRow {
  return {
    key: rowKey(),
    type,
    amount: '',
    platform: '',
    countryCode: '',
    tier: '',
    validFrom: '',
    validTo: '',
    approvalMode: type === 'QualityBonus' ? 'ManualApproval' : 'Automatic',
    priority: '0',
    label: '',
  };
}

export function emptyRules(currency = 'USD'): RulesForm {
  return {
    currency,
    dailyCap: '',
    weeklyCap: '',
    campaignCap: '',
    personalRatesMode: 'Allowed',
    personalRateMaxMultiplier: '',
    rules: [newRule('BaseRate')],
  };
}

/** Rule windows are UTC; the editor shows them in the campaign's zone. */
export function rulesFromSet(set: RewardRuleSet | null | undefined, timeZone: string): RulesForm {
  if (!set) return emptyRules();
  return {
    currency: set.currency,
    dailyCap: numStr(set.dailyCapPerParticipant),
    weeklyCap: numStr(set.weeklyCapPerParticipant),
    campaignCap: numStr(set.campaignCapPerParticipant),
    personalRatesMode: set.personalRatesMode ?? 'Allowed',
    personalRateMaxMultiplier: numStr(set.personalRateMaxMultiplier),
    rules: set.rules.map((r) => ({
      key: r.id,
      type: r.type,
      amount: String(r.amount),
      platform: r.platform ?? '',
      countryCode: r.countryCode ?? '',
      tier: r.tier ?? '',
      validFrom: isoToZonedInput(r.validFrom, timeZone),
      validTo: isoToZonedInput(r.validTo, timeZone),
      approvalMode: r.approvalMode,
      priority: String(r.priority ?? 0),
      label: r.label ?? '',
    })),
  };
}

export function rulesToInput(form: RulesForm, timeZone: string): RewardRuleSetInput {
  return {
    currency: form.currency,
    dailyCapPerParticipant: numOrNull(form.dailyCap),
    weeklyCapPerParticipant: numOrNull(form.weeklyCap),
    campaignCapPerParticipant: numOrNull(form.campaignCap),
    personalRatesMode: form.personalRatesMode,
    personalRateMaxMultiplier:
      form.personalRatesMode === 'CampaignRatesOnly' ? null : numOrNull(form.personalRateMaxMultiplier),
    rules: form.rules.map((r): RewardRuleInput => {
      const conditional = r.type === 'RateOverride' || r.type === 'TimeLimitedBonus';
      return {
        type: r.type,
        amount: numOrNull(r.amount) ?? 0,
        platform: conditional ? ((r.platform || null) as SocialPlatform | null) : null,
        countryCode: conditional && r.countryCode.trim() ? r.countryCode.trim().toUpperCase() : null,
        tier: conditional ? ((r.tier || null) as ParticipantTier | null) : null,
        validFrom: conditional ? zonedInputToIso(r.validFrom, timeZone) : null,
        validTo: conditional ? zonedInputToIso(r.validTo, timeZone) : null,
        approvalMode: r.approvalMode,
        priority: r.type === 'RateOverride' ? (numOrNull(r.priority) ?? 0) : 0,
        label: orNull(r.label),
      };
    }),
  };
}

export interface RuleHint {
  /** Row key, or null for set-level hints. */
  rowKey: string | null;
  message: string;
}

/**
 * Client-side hints mirroring `RewardEngine.Validate`. Advisory only — the server is the authority and its
 * `reward.invalid_rules` errors are always shown as well.
 */
export function ruleHints(form: RulesForm): RuleHint[] {
  const hints: RuleHint[] = [];
  const baseRates = form.rules.filter((r) => r.type === 'BaseRate');
  if (baseRates.length === 0) hints.push({ rowKey: null, message: 'Add exactly one base rate.' });
  if (baseRates.length > 1)
    hints.push({ rowKey: null, message: `There must be exactly one base rate (found ${baseRates.length}).` });
  for (const single of ['FirstPostBonus', 'QualityBonus'] as const) {
    const count = form.rules.filter((r) => r.type === single).length;
    if (count > 1)
      hints.push({
        rowKey: null,
        message: `Only one ${single === 'FirstPostBonus' ? 'first-post' : 'quality'} bonus is allowed.`,
      });
  }
  for (const cap of [form.dailyCap, form.weeklyCap, form.campaignCap]) {
    if (cap.trim() !== '' && !(Number(cap) > 0)) {
      hints.push({ rowKey: null, message: 'Caps must be greater than zero (leave blank for no cap).' });
      break;
    }
  }
  for (const r of form.rules) {
    const amount = Number(r.amount);
    if (r.amount.trim() === '' || !Number.isFinite(amount) || amount < 0)
      hints.push({ rowKey: r.key, message: 'Enter an amount of 0 or more.' });
    if (r.countryCode.trim() && !/^[A-Za-z]{2}$/.test(r.countryCode.trim()))
      hints.push({ rowKey: r.key, message: 'Country must be a two-letter code (e.g. PK).' });
    if (r.label.length > 150) hints.push({ rowKey: r.key, message: 'Labels are limited to 150 characters.' });
    const hasWindow = r.validFrom !== '' || r.validTo !== '';
    if (r.validFrom && r.validTo && r.validTo <= r.validFrom)
      hints.push({ rowKey: r.key, message: 'The window must end after it starts.' });
    if (r.type === 'RateOverride') {
      if (!r.platform && !r.countryCode.trim() && !r.tier && !hasWindow)
        hints.push({ rowKey: r.key, message: 'An override needs at least one condition or a time window.' });
    }
    if (r.type === 'TimeLimitedBonus' && (!r.validFrom || !r.validTo))
      hints.push({ rowKey: r.key, message: 'A time-limited bonus needs both a start and an end.' });
  }
  return hints;
}

export function formsEqual<T>(a: T, b: T): boolean {
  return JSON.stringify(a) === JSON.stringify(b);
}

/** Publish readiness, mirroring `CampaignAdminService.PublishAsync`. */
export interface ReadinessItem {
  id: string;
  label: string;
  ok: boolean;
}

export function readiness(campaign: AdminCampaign, now: Date = new Date()): ReadinessItem[] {
  const hasBaseRate = !!campaign.currentRuleSet?.rules.some((r) => r.type === 'BaseRate');
  const starts = new Date(campaign.startsAt).getTime();
  const ends = new Date(campaign.endsAt).getTime();
  const deadline = new Date(campaign.submissionDeadline).getTime();
  return [
    { id: 'baseRate', label: 'A base reward rate is saved', ok: hasBaseRate },
    { id: 'platform', label: 'At least one platform is chosen', ok: campaign.platforms.length > 0 },
    {
      id: 'content',
      label: 'At least one content asset or posting instructions',
      ok: campaign.assets.length > 0 || campaign.postingInstructions.trim() !== '',
    },
    { id: 'dates', label: 'The campaign ends after it starts', ok: ends > starts },
    { id: 'deadline', label: 'The submission deadline is not before the end', ok: deadline >= ends },
    { id: 'deadlineFuture', label: 'The submission deadline is in the future', ok: deadline > now.getTime() },
    {
      id: 'disclosure',
      label: 'A paid-content disclosure is set',
      ok: campaign.defaultDisclosureText.trim() !== '',
    },
  ];
}
