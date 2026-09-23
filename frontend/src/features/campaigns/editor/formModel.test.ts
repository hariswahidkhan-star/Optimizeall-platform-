import { describe, expect, it } from 'vitest';
import { fieldError, fieldErrorsFrom } from '../shared/formErrors';
import { isoToZonedInput, zonedInputToIso } from '../shared/zonedTime';
import { ApiError } from '@/lib/api/errors';
import { makeCampaign } from '../test/fixtures';
import { emptyRules, newRule, readiness, ruleHints, rulesToInput, slugify } from './formModel';

describe('ruleHints', () => {
  it('asks for exactly one base rate', () => {
    const none = { ...emptyRules(), rules: [] };
    expect(ruleHints(none).map((h) => h.message)).toContain('Add exactly one base rate.');

    const two = {
      ...emptyRules(),
      rules: [
        { ...newRule('BaseRate'), amount: '5' },
        { ...newRule('BaseRate'), amount: '6' },
      ],
    };
    expect(ruleHints(two).map((h) => h.message)).toContain('There must be exactly one base rate (found 2).');

    const one = { ...emptyRules(), rules: [{ ...newRule('BaseRate'), amount: '5' }] };
    expect(ruleHints(one)).toEqual([]);
  });

  it('flags overrides without conditions and bonuses without a window', () => {
    const form = {
      ...emptyRules(),
      rules: [
        { ...newRule('BaseRate'), amount: '5' },
        { ...newRule('RateOverride'), amount: '7' },
        { ...newRule('TimeLimitedBonus'), amount: '1', validFrom: '2030-01-01T00:00' },
      ],
    };
    const messages = ruleHints(form).map((h) => h.message);
    expect(messages).toContain('An override needs at least one condition or a time window.');
    expect(messages).toContain('A time-limited bonus needs both a start and an end.');
  });
});

describe('rulesToInput', () => {
  it('converts windows from the campaign zone to UTC and drops conditions on unconditional rules', () => {
    const form = {
      ...emptyRules('USD'),
      dailyCap: '20',
      rules: [
        { ...newRule('BaseRate'), amount: '5', countryCode: 'pk' },
        { ...newRule('RateOverride'), amount: '7', countryCode: 'pk', validFrom: '2030-01-01T05:00' },
      ],
    };
    const input = rulesToInput(form, 'Asia/Karachi');
    expect(input.dailyCapPerParticipant).toBe(20);
    expect(input.rules[0]).toMatchObject({ type: 'BaseRate', amount: 5, countryCode: null });
    expect(input.rules[1]).toMatchObject({ countryCode: 'PK', validFrom: '2030-01-01T00:00:00.000Z' });
  });
});

describe('zoned time', () => {
  it('round-trips wall clock values across zones and DST', () => {
    expect(zonedInputToIso('2030-07-01T12:00', 'America/New_York')).toBe('2030-07-01T16:00:00.000Z');
    expect(zonedInputToIso('2030-01-01T12:00', 'America/New_York')).toBe('2030-01-01T17:00:00.000Z');
    expect(isoToZonedInput('2030-01-01T17:00:00Z', 'America/New_York')).toBe('2030-01-01T12:00');
  });
});

describe('slugify', () => {
  it('builds lower-case hyphenated slugs', () => {
    expect(slugify('  Spring Drop — 2026! ')).toBe('spring-drop-2026');
    expect(slugify('Café au lait')).toBe('cafe-au-lait');
  });
});

describe('readiness', () => {
  it('mirrors the server publish validation', () => {
    const ok = readiness(makeCampaign(), new Date('2026-09-23T00:00:00Z'));
    expect(ok.every((i) => i.ok)).toBe(true);
    const bad = readiness(
      makeCampaign({ platforms: [], postingInstructions: '', currentRuleSet: null }),
      new Date('2026-09-23T00:00:00Z'),
    );
    expect(bad.filter((i) => !i.ok).map((i) => i.id)).toEqual(['baseRate', 'platform', 'content']);
  });
});

describe('fieldErrorsFrom', () => {
  it('maps PascalCase keys and business codes case-insensitively', () => {
    const error = new ApiError({
      status: 400,
      code: 'validation_failed',
      title: 'Invalid',
      errors: { Title: ['Title is required.'], 'eligibility.MinFollowers': ['Too low.'] },
    });
    const map = fieldErrorsFrom(error);
    expect(fieldError(map, 'title')).toEqual(['Title is required.']);
    expect(fieldError(map, 'eligibility.minFollowers')).toEqual(['Too low.']);
    const coded = fieldErrorsFrom(
      new ApiError({ status: 409, code: 'campaign.slug_taken', title: 'Slug in use' }),
      {
        'campaign.slug_taken': 'slug',
      },
    );
    expect(fieldError(coded, 'Slug')).toEqual(['Slug in use']);
  });
});
