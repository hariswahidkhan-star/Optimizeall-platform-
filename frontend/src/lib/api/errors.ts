import type { ProblemDetails } from './types';

export type FieldErrors = Record<string, string[]>;

interface ApiErrorInit {
  status: number;
  code: string;
  title: string;
  errors?: FieldErrors;
  traceId?: string;
}

/**
 * Error thrown by the API client for every non-2xx response and for network failures (status 0).
 * `code` is the backend's stable machine code (e.g. `auth.invalid_credentials`); `errors` holds field errors keyed
 * by camelCase field name.
 */
export class ApiError extends Error {
  readonly status: number;
  readonly code: string;
  readonly title: string;
  readonly errors?: FieldErrors;
  readonly traceId?: string;

  constructor(init: ApiErrorInit) {
    super(init.title);
    this.name = 'ApiError';
    this.status = init.status;
    this.code = init.code;
    this.title = init.title;
    this.errors = init.errors;
    this.traceId = init.traceId;
  }

  /** First error message for a field, if any. */
  fieldError(field: string): string | undefined {
    return this.errors?.[field]?.[0];
  }

  get isNetworkError(): boolean {
    return this.status === 0;
  }
}

export function isApiError(error: unknown): error is ApiError {
  return error instanceof ApiError;
}

export const NETWORK_ERROR_CODE = 'network_error';

export function networkError(): ApiError {
  return new ApiError({
    status: 0,
    code: NETWORK_ERROR_CODE,
    title: 'We couldn’t reach Optimize All. Check your connection and try again.',
  });
}

const DEFAULT_TITLES: Record<number, string> = {
  400: 'Some of the information needs attention.',
  401: 'Please sign in to continue.',
  403: 'You don’t have access to this.',
  404: 'We couldn’t find what you were looking for.',
  409: 'This was changed by someone else. Reload and try again.',
  413: 'That file is too large.',
  429: 'Too many requests. Please wait a moment and try again.',
};

function defaultTitle(status: number): string {
  return (
    DEFAULT_TITLES[status] ??
    (status >= 500 ? 'Something went wrong on our side. Please try again.' : 'Request failed.')
  );
}

/**
 * Normalizes ASP.NET field keys to the camelCase names the UI uses:
 * `Email` → `email`, `$.email` → `email`, `Items[0].Name` → `items[0].name`.
 */
export function normalizeFieldKey(key: string): string {
  const trimmed = key.replace(/^\$\.?/, '');
  return trimmed
    .split('.')
    .map((segment) => (segment ? segment.charAt(0).toLowerCase() + segment.slice(1) : segment))
    .join('.');
}

function normalizeFieldErrors(raw: unknown): FieldErrors | undefined {
  if (!raw || typeof raw !== 'object') return undefined;
  const result: FieldErrors = {};
  for (const [key, value] of Object.entries(raw as Record<string, unknown>)) {
    const messages = Array.isArray(value) ? value.map(String) : typeof value === 'string' ? [value] : [];
    if (messages.length === 0) continue;
    const field = normalizeFieldKey(key);
    result[field] = [...(result[field] ?? []), ...messages];
  }
  return Object.keys(result).length > 0 ? result : undefined;
}

/** Builds an ApiError from a problem document (or any JSON body) and the HTTP status. */
export function apiErrorFromProblem(status: number, body: unknown, fallbackTraceId?: string): ApiError {
  const problem = (body && typeof body === 'object' ? body : {}) as ProblemDetails;
  const errors = normalizeFieldErrors(problem.errors);
  const code =
    typeof problem.code === 'string' && problem.code
      ? problem.code
      : status === 400 && errors
        ? 'validation_failed'
        : `http_${status}`;
  const title =
    typeof problem.title === 'string' && problem.title && !isGenericValidationTitle(problem.title)
      ? problem.title
      : defaultTitle(status);
  return new ApiError({
    status,
    code,
    title,
    errors,
    traceId: typeof problem.traceId === 'string' ? problem.traceId : fallbackTraceId,
  });
}

/** ASP.NET's automatic model-validation title is not user friendly; replace it with ours. */
function isGenericValidationTitle(title: string): boolean {
  return title === 'One or more validation errors occurred.';
}

/** Reads a failed fetch Response into an ApiError. Never throws. */
export async function parseErrorResponse(response: Response): Promise<ApiError> {
  let body: unknown;
  try {
    const text = await response.text();
    body = text ? JSON.parse(text) : undefined;
  } catch {
    body = undefined;
  }
  return apiErrorFromProblem(response.status, body, response.headers.get('x-trace-id') ?? undefined);
}

/** A user-presentable message for any thrown value. */
export function errorMessage(error: unknown): string {
  if (isApiError(error)) return error.title;
  if (error instanceof Error && error.message) return error.message;
  return 'Something went wrong. Please try again.';
}
