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
  /** QA/demo account (created as a test user). */
  isTestAccount?: boolean;
  /** Present while a staff member is viewing as this user ("log in as"). */
  impersonatedBy?: Impersonator | null;
  /** Names of the custom roles assigned to the user (badges next to the built-in roles). */
  customRoles?: string[] | null;
}

export interface Impersonator {
  id: string;
  displayName: string;
  email: string;
  startedAt: IsoDateTime;
  /** Hard end of the impersonation session. */
  expiresAt: IsoDateTime;
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

// ---------- Sign in with Google (Modules/Auth/Google/GoogleAuthDtos.cs) ----------

export interface AuthProviders {
  google: { enabled: boolean };
}

export interface GoogleStartResponse {
  authorizationUrl: string;
}

export type GoogleCallbackStatus = 'signedIn' | 'needsTerms' | 'linked';

export interface GoogleCallbackResponse {
  status: GoogleCallbackStatus;
  /** Set when signed in (the refresh cookie was set too). */
  auth: AuthResponse | null;
  /** Short-lived signed ticket for POST /auth/google/complete (terms step). */
  ticket: string | null;
  email: string | null;
  displayName: string | null;
  /** App-relative path to continue to (validated server side; validate again before navigating). */
  returnTo: string | null;
}

export interface GoogleCompleteRequest {
  ticket: string;
  acceptTerms: boolean;
  marketingEmailOptIn: boolean;
  displayName?: string;
  countryCode: string;
  languageCode: string;
  timeZone: string;
  referralCode?: string;
  inviteCode?: string;
  deviceId?: string;
}

export interface ExternalLogin {
  provider: 'google' | (string & {});
  email: string;
  createdAt: IsoDateTime;
  lastUsedAt: IsoDateTime | null;
}

export interface SignInMethods {
  hasPassword: boolean;
  googleEnabled: boolean;
  externalLogins: ExternalLogin[];
}
