import { mapFormErrors } from '../lib/formErrors';

/** Form fields of the submit/resubmit forms (names match the multipart fields the API validates). */
export const PROOF_FIELDS = ['socialAccountId', 'postUrl', 'format', 'postedAt', 'captionText', 'screenshot'] as const;
export type ProofField = (typeof PROOF_FIELDS)[number];

/** Domain codes without field details, routed to the field the participant has to change. */
export const PROOF_CODE_TO_FIELD: Record<string, ProofField> = {
  'submission.duplicate_url': 'postUrl',
  'submission.invalid_url': 'postUrl',
  'submission.url_platform_mismatch': 'postUrl',
  'submission.format_mismatch': 'format',
  'submission.posted_at_in_future': 'postedAt',
  'submission.posted_at_too_old': 'postedAt',
  'submission.social_account_invalid': 'socialAccountId',
  'submission.platform_mismatch': 'socialAccountId',
  'submission.platform_not_allowed': 'socialAccountId',
  'submission.social_account_inactive': 'socialAccountId',
  'submission.account_ineligible': 'socialAccountId',
  'submission.screenshot_required': 'screenshot',
  'file.required': 'screenshot',
  'file.empty': 'screenshot',
  'file.too_large': 'screenshot',
  'file.unsupported_type': 'screenshot',
  'file.too_small': 'screenshot',
  'file.too_large_dimensions': 'screenshot',
};

/**
 * Splits a submission error into field messages and a form-level message. Eligibility reasons arrive as
 * "code: message" strings; only the message is shown.
 */
export function mapProofErrors(error: unknown) {
  const mapped = mapFormErrors(error, [...PROOF_FIELDS, 'platform'], PROOF_CODE_TO_FIELD);
  if (!mapped) return null;
  // A platform validation error is about the selected account.
  if (mapped.fields.platform && !mapped.fields.socialAccountId)
    mapped.fields.socialAccountId = mapped.fields.platform;
  if (mapped.form) {
    mapped.form = {
      ...mapped.form,
      details: mapped.form.details.map((d) => d.replace(/^[a-z_]+\.[a-z_.]+:\s*/, '')),
    };
  }
  return mapped;
}
