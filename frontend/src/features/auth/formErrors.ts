import { isApiError, networkError, type ApiError, type FieldErrors } from '@/lib/api/errors';

export interface MappedErrors {
  /** Field errors for fields rendered on the form. */
  fields: FieldErrors;
  /** Error to show above the form (title plus any field errors for fields not on the form), or null. */
  form: { title: string; details: string[]; code: string; traceId?: string } | null;
}

/**
 * Splits an API error into per-field messages for the given fields and a form-level message for everything else.
 * `codeToField` routes errors without field details (e.g. `auth.terms_required`) to a specific field.
 */
export function mapServerErrors(
  error: unknown,
  knownFields: readonly string[],
  codeToField: Record<string, string> = {},
): MappedErrors {
  const apiError: ApiError = isApiError(error) ? error : networkError();
  const fields: FieldErrors = {};
  const stray: string[] = [];

  for (const [key, messages] of Object.entries(apiError.errors ?? {})) {
    if (knownFields.includes(key)) fields[key] = messages;
    else stray.push(...messages);
  }

  const routed = codeToField[apiError.code];
  if (routed && knownFields.includes(routed) && !fields[routed]) {
    fields[routed] = [apiError.title];
    return {
      fields,
      form: stray.length ? { title: apiError.title, details: stray, code: apiError.code } : null,
    };
  }

  const hasFieldErrors = Object.keys(fields).length > 0;
  const form =
    !hasFieldErrors || stray.length > 0
      ? {
          title: apiError.title,
          details: stray,
          code: apiError.code,
          traceId: apiError.status >= 500 ? apiError.traceId : undefined,
        }
      : null;
  return { fields, form };
}
