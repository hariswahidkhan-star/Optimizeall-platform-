import { useState, type ButtonHTMLAttributes } from 'react';
import { Alert } from '@/components/ui/Alert';
import { isApiError } from '@/lib/api/errors';
import { startGoogleFlow, useAuthProviders } from './googleApi';
import './GoogleButton.css';

/** Google's four-colour "G" mark (inline, decorative: the button text names the action). */
export function GoogleLogo() {
  return (
    <svg className="google-button__logo" viewBox="0 0 48 48" aria-hidden="true" focusable="false">
      <path
        fill="#EA4335"
        d="M24 9.5c3.54 0 6.71 1.22 9.21 3.6l6.85-6.85C35.9 2.38 30.47 0 24 0 14.62 0 6.51 5.38 2.56 13.22l7.98 6.19C12.43 13.72 17.74 9.5 24 9.5z"
      />
      <path
        fill="#4285F4"
        d="M46.98 24.55c0-1.57-.15-3.09-.38-4.55H24v9.02h12.94c-.58 2.96-2.26 5.48-4.78 7.18l7.73 6c4.51-4.18 7.09-10.36 7.09-17.65z"
      />
      <path
        fill="#FBBC05"
        d="M10.53 28.59c-.48-1.45-.76-2.99-.76-4.59s.27-3.14.76-4.59l-7.98-6.19C.92 16.46 0 20.12 0 24c0 3.88.92 7.54 2.56 10.78l7.97-6.19z"
      />
      <path
        fill="#34A853"
        d="M24 48c6.48 0 11.93-2.13 15.89-5.81l-7.73-6c-2.15 1.45-4.92 2.3-8.16 2.3-6.26 0-11.57-4.22-13.47-9.91l-7.98 6.19C6.51 42.62 14.62 48 24 48z"
      />
    </svg>
  );
}

type GoogleButtonProps = Omit<ButtonHTMLAttributes<HTMLButtonElement>, 'children'> & {
  loading?: boolean;
  label?: string;
};

/** The branded button (white, G logo, "Continue with Google"). */
export function GoogleButton({
  loading,
  label = 'Continue with Google',
  disabled,
  ...rest
}: GoogleButtonProps) {
  return (
    <button
      type="button"
      className="google-button"
      disabled={disabled || loading}
      aria-busy={loading || undefined}
      {...rest}
    >
      <GoogleLogo />
      <span>{loading ? 'Redirecting to Google…' : label}</span>
    </button>
  );
}

interface ContinueWithGoogleProps {
  /** App-relative path to land on after signing in (validated by the API and again on return). */
  returnTo?: string | null;
  /** Called right before leaving for Google (e.g. to remember referral codes). */
  onBeforeRedirect?: () => void;
}

/**
 * "Continue with Google" plus an "or" divider, shown only when the API reports Google sign-in as configured — an
 * unconfigured deployment (or a failed provider lookup) shows no button at all rather than a broken one.
 */
export function ContinueWithGoogle({ returnTo, onBeforeRedirect }: ContinueWithGoogleProps) {
  const providers = useAuthProviders();
  const [starting, setStarting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  if (!providers.data?.google.enabled) return null;

  const onClick = async () => {
    setError(null);
    setStarting(true);
    try {
      onBeforeRedirect?.();
      await startGoogleFlow('signin', returnTo);
    } catch (e) {
      setStarting(false);
      setError(isApiError(e) ? e.title : 'Google sign-in couldn’t start. Please try again.');
    }
  };

  return (
    <>
      {error && (
        <Alert tone="danger" role="alert">
          {error}
        </Alert>
      )}
      <GoogleButton loading={starting} onClick={onClick} />
      <p className="auth-divider" role="separator" aria-label="or">
        or
      </p>
    </>
  );
}
