import { isApiError, networkError, type ApiError } from '@/lib/api/errors';

export interface FieldErrorMap {
  /** Messages per requested field name (as the form spells it). */
  fields: Record<string, string[]>;
  /** Error shown above the form: the problem title plus messages for fields the form does not render. */
  form: { title: string; details: string[]; code: string } | null;
}

/**
 * Maps an API error onto a form's fields. ASP.NET answers with PascalCase keys (`CtaUrl`, `Value`), domain rules
 * with camelCase (`ctaUrl`), and nested keys like `$.value` — so matching is case-insensitive and ignores a
 * leading `$.`. `codeToField` routes errors without field details (e.g. `content.duplicate_key`) to a field.
 */
export function mapFieldErrors(
  error: unknown,
  fields: readonly string[],
  codeToField: Record<string, string> = {},
): FieldErrorMap {
  if (!error) return { fields: {}, form: null };
  const apiError: ApiError = isApiError(error) ? error : networkError();
  const byLower = new Map(fields.map((f) => [f.toLowerCase(), f]));
  const mapped: Record<string, string[]> = {};
  const stray: string[] = [];

  for (const [rawKey, messages] of Object.entries(apiError.errors ?? {})) {
    const key = rawKey.replace(/^\$\.?/, '').toLowerCase();
    const field = byLower.get(key);
    if (field) mapped[field] = [...(mapped[field] ?? []), ...messages];
    else stray.push(...messages);
  }

  const routed = codeToField[apiError.code];
  if (routed && fields.includes(routed) && !mapped[routed]) mapped[routed] = [apiError.title];

  const hasFields = Object.keys(mapped).length > 0;
  return {
    fields: mapped,
    form:
      !hasFields || stray.length > 0 ? { title: apiError.title, details: stray, code: apiError.code } : null,
  };
}

export function isForbidden(error: unknown): boolean {
  return isApiError(error) && error.status === 403;
}

export function isConflict(error: unknown, code?: string): boolean {
  return isApiError(error) && error.status === 409 && (!code || error.code === code);
}

/** Friendly copy for the admin error codes whose server titles benefit from context. */
const CODE_MESSAGES: Record<string, string> = {
  'admin.cannot_remove_own_admin':
    'You can’t remove your own Admin role. Ask another administrator to change your roles.',
  'admin.last_admin':
    'At least one active administrator must remain. Give another account the Admin role first.',
  'admin.cannot_suspend_self': 'You can’t suspend your own account.',
  'admin.staff_requires_admin': 'Only administrators can change the status of staff accounts.',
  'jobs.lease_held':
    'This job is already running on another instance (its lease is held). Wait for that run to finish, then try again.',
  'concurrency.conflict': 'Someone else changed this since you opened it. Reload to get the latest version.',
};

/** User-facing message for an error: a mapped message for known codes, else the server title. */
export function adminErrorMessage(error: unknown): string {
  if (isApiError(error)) {
    if (error.status === 403 && !CODE_MESSAGES[error.code])
      return 'You don’t have permission to do this. Ask an administrator for access.';
    const base = CODE_MESSAGES[error.code] ?? error.title;
    const details = Object.values(error.errors ?? {}).flat();
    return details.length > 0 && !details.includes(base) ? `${base} ${details.join(' ')}` : base;
  }
  if (error instanceof Error && error.message) return error.message;
  return 'Something went wrong. Please try again.';
}

/** An Error whose message is the mapped admin message (so ConfirmDialog shows it inline). */
export function toDisplayError(error: unknown): Error {
  const wrapped = new Error(adminErrorMessage(error));
  (wrapped as Error & { cause?: unknown }).cause = error;
  return wrapped;
}
