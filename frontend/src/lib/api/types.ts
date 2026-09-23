/** Shared API contract types. Mirrors backend DTOs (camelCase JSON, string enums, UTC ISO-8601 timestamps). */

/** `Common/Http/Paging.cs` — PagedResult<T>. */
export interface PagedResult<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

/** Standard list query parameters: ?page=1&pageSize=25&search=...&sort=field&desc=true */
export interface PageQuery {
  page?: number;
  pageSize?: number;
  search?: string;
  sort?: string;
  desc?: boolean;
}

/** RFC 7807 problem document as produced by `Common/Errors/ExceptionHandling.cs` (plus ASP.NET validation problems). */
export interface ProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  instance?: string;
  code?: string;
  traceId?: string;
  errors?: Record<string, string[]>;
}

export interface MessageResponse {
  message: string;
}

/** ISO-8601 UTC timestamp string, e.g. 2026-09-23T10:00:00Z. */
export type IsoDateTime = string;

/** Amount + ISO 4217 currency, as every money value travels in the API. */
export interface MoneyAmount {
  amount: number;
  currency: string;
}

// ---------- Auth (Modules/Auth/AuthDtos.cs) ----------

export type UserStatus = 'Active' | 'Suspended' | 'Deactivated' | (string & {});

export interface SessionUser {
  id: string;
  email: string;
  displayName: string;
  emailVerified: boolean;
  countryCode: string;
  languageCode: string;
  timeZone: string;
  status: UserStatus;
  roles: string[];
  permissions: string[];
}

export interface AuthResponse {
  accessToken: string;
  expiresAt: IsoDateTime;
  user: SessionUser;
}

export interface RegisterRequest {
  email: string;
  password: string;
  displayName: string;
  countryCode: string;
  languageCode: string;
  timeZone: string;
  referralCode?: string;
  inviteCode?: string;
  deviceId?: string;
  acceptTerms: boolean;
  marketingEmailOptIn: boolean;
}

export interface LoginRequest {
  email: string;
  password: string;
}

export interface ResetPasswordRequest {
  token: string;
  newPassword: string;
}

export interface ChangePasswordRequest {
  currentPassword: string;
  newPassword: string;
}
