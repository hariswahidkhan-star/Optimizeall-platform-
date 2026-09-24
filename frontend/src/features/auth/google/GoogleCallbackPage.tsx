import { useEffect, useRef, useState } from 'react';
import { Link, useNavigate, useSearchParams } from 'react-router-dom';
import { defaultLandingPath } from '@/app/portals';
import { safeNextPath } from '@/app/redirects';
import { Alert } from '@/components/ui/Alert';
import { Spinner } from '@/components/ui/Spinner';
import { useToast } from '@/components/ui/toastContext';
import { api } from '@/lib/api/client';
import { isApiError } from '@/lib/api/errors';
import type { GoogleCallbackResponse } from '@/lib/api/types';
import { useAuth } from '@/lib/auth/useAuth';
import { GoogleTermsStep } from './GoogleTermsStep';
import '@/app/layouts/AuthLayout.css';

type Phase =
  | { kind: 'working' }
  | { kind: 'terms'; response: GoogleCallbackResponse }
  | { kind: 'error'; title: string; message: string; code?: string };

function googleErrorMessage(error: string): string {
  return error === 'access_denied'
    ? 'You cancelled signing in with Google. You can try again or use your email and password.'
    : 'Google didn’t complete the sign-in. Please try again.';
}

/**
 * Google redirects here (`/auth/google/callback?code=…&state=…`). The page posts the code and state once, then routes
 * like a normal sign-in (honouring the `next` path given when the flow started), shows the terms step for a new
 * account, or returns to the profile after linking.
 */
export function GoogleCallbackPage() {
  const { status, user, startSession } = useAuth();
  const navigate = useNavigate();
  const toast = useToast();
  const [params] = useSearchParams();
  const [phase, setPhase] = useState<Phase>({ kind: 'working' });
  const posted = useRef(false);
  const alertRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    document.title = 'Signing in with Google · Optimize All';
  }, []);

  useEffect(() => {
    // Wait for the session bootstrap so a linking flow carries the signed-in user's token; post exactly once (an
    // authorization code can only be exchanged once).
    if (status === 'loading' || posted.current) return;
    posted.current = true;

    const code = params.get('code');
    const state = params.get('state');
    const googleError = params.get('error');
    if (googleError || !code || !state) {
      setPhase({
        kind: 'error',
        title: 'Google sign-in didn’t complete',
        message: googleErrorMessage(googleError ?? 'missing_code'),
      });
      return;
    }

    api
      .post<GoogleCallbackResponse>('/auth/google/callback', { code, state })
      .then((response) => {
        const next = safeNextPath(response.returnTo);
        if (response.status === 'signedIn' && response.auth) {
          const user = startSession(response.auth);
          navigate(defaultLandingPath(user.permissions, next), { replace: true });
        } else if (response.status === 'linked') {
          toast.success('Google connected', 'You can now sign in with Google.');
          navigate(next ?? '/', { replace: true });
        } else if (response.status === 'needsTerms' && response.ticket) {
          setPhase({ kind: 'terms', response });
        } else {
          setPhase({
            kind: 'error',
            title: 'Google sign-in didn’t complete',
            message: googleErrorMessage(''),
          });
        }
      })
      .catch((error: unknown) => {
        setPhase({
          kind: 'error',
          title:
            isApiError(error) && error.code === 'account.suspended'
              ? 'This account can’t sign in'
              : 'Google sign-in didn’t complete',
          message: isApiError(error) ? error.title : googleErrorMessage(''),
          code: isApiError(error) ? error.code : undefined,
        });
      });
  }, [status, params, navigate, startSession, toast]);

  useEffect(() => {
    if (phase.kind === 'error') alertRef.current?.focus();
  }, [phase.kind]);

  if (phase.kind === 'terms') return <GoogleTermsStep response={phase.response} />;

  return (
    <div className="auth-page">
      <div className="auth-page__header">
        <h1 className="auth-page__title">Sign in with Google</h1>
      </div>
      {phase.kind === 'working' ? (
        <div role="status" aria-live="polite" className="cluster">
          <Spinner />
          <span>Finishing sign-in with Google…</span>
        </div>
      ) : (
        <div ref={alertRef} tabIndex={-1}>
          <Alert
            tone="danger"
            role="alert"
            title={phase.title}
            actions={
              // A signed-in user was linking Google from the profile: /login would just bounce them.
              user ? (
                <Link className="ui-link" to={defaultLandingPath(user.permissions, null)}>
                  Back to your account
                </Link>
              ) : (
                <Link className="ui-link" to="/login">
                  Back to sign in
                </Link>
              )
            }
          >
            {phase.message}
          </Alert>
        </div>
      )}
    </div>
  );
}
