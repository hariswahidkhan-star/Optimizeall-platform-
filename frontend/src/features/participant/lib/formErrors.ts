import { mapServerErrors, type MappedErrors } from '@/features/auth/formErrors';

export type { MappedErrors };

/**
 * Maps an API error onto a form's fields. `codeToField` routes domain codes without field details
 * (e.g. `submission.duplicate_url`) to the field the user must fix.
 */
export function mapFormErrors(
  error: unknown,
  fields: readonly string[],
  codeToField: Record<string, string> = {},
): MappedErrors | null {
  if (!error) return null;
  return mapServerErrors(error, fields, codeToField);
}

/** Moves focus to the first invalid control (ids are `${prefix}-${field}`). */
export function focusFirstError(
  prefix: string,
  fields: readonly string[],
  errors: Record<string, unknown>,
): void {
  const first = fields.find((f) => errors[f]);
  if (!first) return;
  // Wait for the error markup to render before focusing so aria-describedby is announced.
  requestAnimationFrame(() => document.getElementById(`${prefix}-${first}`)?.focus());
}

export function firstMessage(value: string[] | string | undefined | null): string | undefined {
  if (!value) return undefined;
  return Array.isArray(value) ? value[0] : value;
}
