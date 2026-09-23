import { MailCheck } from 'lucide-react';
import { useEffect } from 'react';
import { Link, useLocation } from 'react-router-dom';
import { ResendVerificationForm } from './ResendVerificationForm';
import '@/app/layouts/AuthLayout.css';

/** After registration: tells the user to open the verification link, with a resend form. */
export function CheckEmailPage() {
  const location = useLocation();
  const email = (location.state as { email?: string } | null)?.email ?? '';

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
      </div>
      <div className="stack">
        <h2 className="text-small" style={{ letterSpacing: 0 }}>
          Didn’t get it? Check your spam folder, or request a new link.
        </h2>
        <ResendVerificationForm initialEmail={email} idPrefix="check-email" />
      </div>
      <p className="auth-page__switch">
        Already verified?{' '}
        <Link className="ui-link" to="/login">
          Sign in
        </Link>
      </p>
    </div>
  );
}
