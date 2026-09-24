import { LogIn, Mail } from 'lucide-react';
import { useEffect, useRef, useState, type FormEvent } from 'react';
import { Link, useNavigate, useSearchParams } from 'react-router-dom';
import { defaultLandingPath } from '@/app/portals';
import { safeNextPath } from '@/app/redirects';
import { Alert } from '@/components/ui/Alert';
import { Button } from '@/components/ui/Button';
import { FormField } from '@/components/ui/FormField';
import { Input } from '@/components/ui/Input';
import { PasswordInput } from '@/components/ui/PasswordInput';
import { isApiError } from '@/lib/api/errors';
import { useAuth } from '@/lib/auth/useAuth';
import { mapServerErrors, type MappedErrors } from './formErrors';
import { ContinueWithGoogle } from './google/GoogleButton';
import { TestAccountsPanel } from './TestAccountsPanel';
import '@/app/layouts/AuthLayout.css';

const EMAIL_RE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

type Notice = { tone: 'info' | 'success' | 'warning'; title: string; text?: string };

function noticeFor(params: URLSearchParams): Notice | null {
  if (params.get('expired') === '1')
    return {
      tone: 'warning',
      title: 'Your session has expired',
      text: 'Please sign in again to continue where you left off.',
    };
  if (params.get('reset') === '1')
    return { tone: 'success', title: 'Password changed', text: 'Sign in with your new password.' };
  if (params.get('verified') === '1')
    return { tone: 'success', title: 'Email verified', text: 'Sign in to start sharing campaigns.' };
  if (params.get('signedOut') === '1') return { tone: 'info', title: 'You’ve been signed out.' };
  return null;
}

export function LoginPage() {
  const { login } = useAuth();
  const navigate = useNavigate();
  const [params] = useSearchParams();
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [clientErrors, setClientErrors] = useState<Record<string, string>>({});
  const [server, setServer] = useState<MappedErrors | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const alertRef = useRef<HTMLDivElement>(null);
  const notice = noticeFor(params);

  useEffect(() => {
    document.title = 'Sign in · Optimize All';
  }, []);

  useEffect(() => {
    if (server?.form) alertRef.current?.focus();
  }, [server]);

  const onSubmit = async (event: FormEvent) => {
    event.preventDefault();
    const errors: Record<string, string> = {};
    if (!EMAIL_RE.test(email.trim())) errors.email = 'Enter the email address you registered with.';
    if (!password) errors.password = 'Enter your password.';
    setClientErrors(errors);
    setServer(null);
    if (Object.keys(errors).length > 0) {
      document.getElementById(errors.email ? 'login-email' : 'login-password')?.focus();
      return;
    }
    setSubmitting(true);
    try {
      const user = await login(email.trim(), password);
      navigate(defaultLandingPath(user.permissions, safeNextPath(params.get('next'))), { replace: true });
    } catch (error) {
      setServer(mapServerErrors(error, ['email', 'password']));
      if (isApiError(error) && error.code === 'auth.invalid_credentials') setPassword('');
    } finally {
      setSubmitting(false);
    }
  };

  const formError = server?.form;
  const code = formError?.code;

  return (
    <div className="auth-page">
      <div className="auth-page__header">
        <h1 className="auth-page__title">Welcome back</h1>
        <p className="auth-page__subtitle">Sign in to your Optimize All account.</p>
      </div>

      {notice && !formError && (
        <Alert tone={notice.tone} title={notice.title} role="status">
          {notice.text}
        </Alert>
      )}

      {formError && (
        <div ref={alertRef} tabIndex={-1}>
          {code === 'auth.locked_out' ? (
            <Alert
              tone="warning"
              role="alert"
              title="Too many sign-in attempts"
              actions={
                <Link className="ui-link" to="/forgot-password">
                  Reset your password
                </Link>
              }
            >
              {formError.title}
            </Alert>
          ) : code === 'account.suspended' ? (
            <Alert tone="danger" role="alert" title="This account can’t sign in">
              {formError.title} Write to support from the email address on your account and we’ll look into
              it.
            </Alert>
          ) : (
            <Alert
              tone="danger"
              role="alert"
              title={code === 'auth.invalid_credentials' ? 'Sign-in failed' : undefined}
            >
              {formError.title}
              {formError.details.length > 0 && (
                <ul>
                  {formError.details.map((d) => (
                    <li key={d}>{d}</li>
                  ))}
                </ul>
              )}
            </Alert>
          )}
        </div>
      )}

      <ContinueWithGoogle returnTo={safeNextPath(params.get('next'))} />

      <form className="auth-form" onSubmit={onSubmit} noValidate aria-label="Sign in">
        <FormField id="login-email" label="Email" error={clientErrors.email ?? server?.fields.email} required>
          <Input
            type="email"
            autoComplete="email"
            inputMode="email"
            leading={<Mail />}
            value={email}
            onChange={(e) => setEmail(e.target.value)}
          />
        </FormField>
        <FormField
          id="login-password"
          label="Password"
          error={clientErrors.password ?? server?.fields.password}
          required
          labelAside={
            <Link className="ui-link text-small" to="/forgot-password">
              Forgot password?
            </Link>
          }
        >
          <PasswordInput
            autoComplete="current-password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
          />
        </FormField>
        <Button type="submit" size="lg" fullWidth loading={submitting} leadingIcon={<LogIn />}>
          Sign in
        </Button>
      </form>

      <p className="auth-page__switch">
        New to Optimize All?{' '}
        <Link
          className="ui-link"
          to={`/register${params.get('ref') ? `?ref=${encodeURIComponent(params.get('ref')!)}` : ''}`}
        >
          Create an account
        </Link>
      </p>

      <TestAccountsPanel />
    </div>
  );
}
