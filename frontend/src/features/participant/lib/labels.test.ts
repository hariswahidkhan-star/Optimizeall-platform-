import { describe, expect, it } from 'vitest';
import { qualifyingActionLabel } from './labels';

describe('qualifyingActionLabel', () => {
  // The referral terms read "…but only after they <label> within 60 days", so the label follows "they".
  it.each([
    ['EmailVerified', 'verify their email address'],
    ['FirstApprovedSubmission', 'get their first post approved'],
    ['FirstPaidPayout', 'receive their first payout'],
  ])('%s reads correctly after "they"', (action, label) => {
    expect(qualifyingActionLabel(action)).toBe(label);
    expect(`only after they ${qualifyingActionLabel(action)}`).not.toMatch(/they \w+s /);
  });
});
