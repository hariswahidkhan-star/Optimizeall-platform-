import { MailCheck } from 'lucide-react';
import { useEffect } from 'react';
import { Link, useLocation } from 'react-router-dom';
import { safeNextPath } from '@/app/redirects';
import { ResendVerificationForm } from './ResendVerificationForm';
import '@/app/layouts/AuthLayout.css';

/** After registration: tells the user to open the verification link, with a resend form. */
export function CheckEmailPage() {
  const location = useLocation();
  const state = location.state as { email?: string; next?: string | null } | null;
  const email = state?.email ?? '';
  const next = safeNextPath(state?.next);
  const toCourse = next !== null && next.startsWith('/learn/');

  useEffect(() => {
    document.title = 'Check your email · Optimize All';
  }, []);

  return (
    <div className="auth-page">
      <span className="auth-page__icon" aria-hidden="true">
        <MailCheck />
      </span>
      <div className="auth-page__header">
        <h1 className="auth-page__title">Check your email</h1>
        <p className="auth-page__subtitle">
          {email ? (
            <>
              We sent a verification link to <strong>{email}</strong>.
            </>
          ) : (
            'We sent you a verification link.'
          )}{' '}
          Open it to activate your account. The link expires after a while, so use it soon.
        </p>
        {toCourse && (
          <p className="auth-page__subtitle" role="status">
            After you verify and sign in, we’ll take you straight back to your course, enrol you and open the first
            lesson.
          </p>
        )}
      </div>
      <div className="stack">
        <h2 className="text-small" style={{ letterSpacing: 0 }}>
          Didn’t get it? Check your spam folder, or request a new link.
        </h2>
        <ResendVerificationForm initialEmail={email} idPrefix="check-email" />
      </div>
      <p className="auth-page__switch">
        Already verified?{' '}
        <Link className="ui-link" to={next ? `/login?next=${encodeURIComponent(next)}` : '/login'}>
          Sign in
        </Link>
      </p>
    </div>
  );
}
