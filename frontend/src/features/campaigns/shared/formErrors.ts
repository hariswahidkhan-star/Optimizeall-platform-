import { isApiError } from '@/lib/api/errors';

/** Field errors keyed by lower-cased field path (so `Title`, `title` and `TITLE` all match). */
export type FieldErrorMap = Record<string, string[]>;

/**
 * Collects the field errors of a server response into a case-insensitive map. DataAnnotations errors arrive with
 * PascalCase keys (`Title`, `Eligibility.MinFollowers`, `Variants[0].Title`); the client camel-cases the first
 * letter of each segment, but nested/indexed keys may still differ in case, so everything is lower-cased here.
 * `codeFields` maps business error codes (which carry no field key) onto a field.
 */
export function fieldErrorsFrom(error: unknown, codeFields: Record<string, string> = {}): FieldErrorMap {
  if (!isApiError(error)) return {};
  const result: FieldErrorMap = {};
  for (const [key, messages] of Object.entries(error.errors ?? {})) {
    const field = key.replace(/^\$\.?/, '').toLowerCase();
    result[field] = [...(result[field] ?? []), ...messages];
  }
  const mapped = codeFields[error.code];
  if (mapped) {
    const field = mapped.toLowerCase();
    result[field] = [...(result[field] ?? []), error.title];
  }
  return result;
}

/** Errors for a field path (case-insensitive), or undefined. */
export function fieldError(errors: FieldErrorMap, field: string): string[] | undefined {
  const list = errors[field.toLowerCase()];
  return list && list.length > 0 ? list : undefined;
}

/** True when any error key starts with one of the prefixes (e.g. to badge a tab). */
export function countErrors(errors: FieldErrorMap, prefixes: string[]): number {
  const lower = prefixes.map((p) => p.toLowerCase());
  return Object.keys(errors).filter((key) =>
    lower.some((p) => key === p || key.startsWith(`${p}.`) || key.startsWith(`${p}[`)),
  ).length;
}

/** Messages of errors that are not claimed by any of the given field keys (shown in a summary). */
export function unclaimedErrors(errors: FieldErrorMap, claimed: string[]): string[] {
  const set = new Set(claimed.map((c) => c.toLowerCase()));
  return Object.entries(errors)
    .filter(([key]) => !set.has(key))
    .flatMap(([, messages]) => messages);
}
