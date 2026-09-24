import { useQuery } from '@tanstack/react-query';
import { api } from '@/lib/api/client';
import type { AuthProviders, GoogleStartResponse } from '@/lib/api/types';
import { redirectToGoogle } from './redirect';

export const providersQueryKey = ['auth', 'providers'] as const;
export const signInMethodsQueryKey = ['auth', 'external-logins'] as const;

/** Which external sign-in providers the API has configured. Failures count as "none" (the button stays hidden). */
export function useAuthProviders() {
  return useQuery({
    queryKey: providersQueryKey,
    queryFn: () => api.get<AuthProviders>('/auth/providers'),
    staleTime: 5 * 60_000,
    retry: false,
  });
}

/** Where the new-account terms step finds the referral/invite codes of the page that started the flow. */
const SIGNUP_CODES_KEY = 'oa.google.signupCodes';

export interface SignupCodes {
  referralCode?: string;
  inviteCode?: string;
}

export function rememberSignupCodes(codes: SignupCodes): void {
  try {
    if (codes.referralCode || codes.inviteCode)
      sessionStorage.setItem(SIGNUP_CODES_KEY, JSON.stringify(codes));
    else sessionStorage.removeItem(SIGNUP_CODES_KEY);
  } catch {
    // Storage unavailable (private mode): codes are optional.
  }
}

export function recallSignupCodes(): SignupCodes {
  try {
    const raw = sessionStorage.getItem(SIGNUP_CODES_KEY);
    if (!raw) return {};
    const parsed = JSON.parse(raw) as SignupCodes;
    const clean = (v: unknown) => (typeof v === 'string' && /^[A-Za-z0-9_-]{1,32}$/.test(v) ? v : undefined);
    return { referralCode: clean(parsed.referralCode), inviteCode: clean(parsed.inviteCode) };
  } catch {
    return {};
  }
}

export function forgetSignupCodes(): void {
  try {
    sessionStorage.removeItem(SIGNUP_CODES_KEY);
  } catch {
    // ignore
  }
}

/**
 * Starts a Google flow (sign-in, or linking from the profile) and sends the browser to Google. The API sets an
 * HttpOnly cookie with the attempt's nonce and PKCE verifier; the state in the URL is signed.
 */
export async function startGoogleFlow(
  kind: 'signin' | 'link',
  returnTo: string | null | undefined,
): Promise<void> {
  const path = kind === 'link' ? '/auth/external-logins/google/start' : '/auth/google/start';
  const response = await api.post<GoogleStartResponse>(path, { returnTo: returnTo ?? undefined });
  redirectToGoogle(response.authorizationUrl);
}
