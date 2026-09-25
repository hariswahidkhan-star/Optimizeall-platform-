import { useMutation } from '@tanstack/react-query';
import { CircleCheck, MailX } from 'lucide-react';
import { useEffect, useRef } from 'react';
import { useSearchParams } from 'react-router-dom';
import { defaultLandingPath } from '@/app/portals';
import { Button } from '@/components/ui/Button';
import { ButtonLink } from '@/components/ui/ButtonLink';
import { Spinner } from '@/components/ui/Spinner';
import { pendingEnrolPath } from '@/features/learning/enrolIntent';
import { api } from '@/lib/api/client';
import { errorMessage, isApiError } from '@/lib/api/errors';
import type { MessageResponse } from '@/lib/api/types';
import { useAuth } from '@/lib/auth/useAuth';
import { ResendVerificationForm } from './ResendVerificationForm';
import '@/app/layouts/AuthLayout.css';

/** Opens from the email link (?token=...). Tokens are single-use, so the request is sent exactly once. */
export function VerifyEmailPage() {
  const [params] = useSearchParams();
  const token = params.get('token')?.trim() ?? '';
  const { status, user, refreshUser } = useAuth();
  const sentFor = useRef<string | null>(null);

  const verify = useMutation({
    mutationFn: (value: string) => api.post<MessageResponse>('/auth/verify-email', { token: value }),
    onSuccess: () => {
      if (status === 'authenticated') void refreshUser().catch(() => undefined);
    },
  });
  const { mutate } = verify;

  useEffect(() => {
    document.title = 'Verify your email · Optimize All';
  }, []);

  useEffect(() => {
    if (!token || sentFor.current === token) return;
    sentFor.current = token;
    mutate(token);
  }, [token, mutate]);

  if (!token) {
    return (
      <div className="auth-page">
        <span className="auth-page__icon auth-page__icon--danger" aria-hidden="true">
          <MailX />
        </span>
        <div className="auth-page__header">
          <h1 className="auth-page__title">This link is incomplete</h1>
          <p className="auth-page__subtitle">
            Open the link from your verification email again, or request a new one below.
          </p>
        </div>
        <ResendVerificationForm initialEmail={user?.email} idPrefix="verify-missing" />
      </div>
    );
  }

  if (verify.isPending || verify.isIdle) {
    return (
      <div className="auth-page" aria-busy="true">
        <div className="auth-page__header">
          <h1 className="auth-page__title">Verifying your email…</h1>
          <p className="auth-page__subtitle">This only takes a moment.</p>
        </div>
        <Spinner size="lg" label="Verifying your email address" />
      </div>
    );
  }

  if (verify.isSuccess) {
    // A learner who registered from a course's "Enrol" button goes back to that course (enrolled automatically).
    const course = pendingEnrolPath();
    const continueTo =
      status === 'authenticated' && user
        ? defaultLandingPath(user.permissions, course)
        : `/login?verified=1${course ? `&next=${encodeURIComponent(course)}` : ''}`;
    return (
      <div className="auth-page">
        <span className="auth-page__icon auth-page__icon--success" aria-hidden="true">
          <CircleCheck />
        </span>
        <div className="auth-page__header" role="status">
          <h1 className="auth-page__title">Your email is verified</h1>
          <p className="auth-page__subtitle">
            {verify.data?.message ?? 'Your email address is verified.'} You can now join campaigns and submit
            posts.
          </p>
        </div>
        <ButtonLink to={continueTo} size="lg" fullWidth>
          {course
            ? status === 'authenticated'
              ? 'Continue to your course'
              : 'Sign in and start learning'
            : status === 'authenticated'
              ? 'Continue to your dashboard'
              : 'Sign in'}
        </ButtonLink>
      </div>
    );
  }

  const expired = isApiError(verify.error) && verify.error.code === 'auth.invalid_token';
  return (
    <div className="auth-page">
      <span className="auth-page__icon auth-page__icon--danger" aria-hidden="true">
        <MailX />
      </span>
      <div className="auth-page__header" role="alert">
        <h1 className="auth-page__title">
          {expired ? 'This link has expired' : 'We couldn’t verify your email'}
        </h1>
        <p className="auth-page__subtitle">
          {expired
            ? 'Verification links can only be used once and expire after a while. Request a fresh one below.'
            : errorMessage(verify.error)}
        </p>
      </div>
      {!expired && (
        <Button variant="secondary" onClick={() => mutate(token)}>
          Try again
        </Button>
      )}
      <ResendVerificationForm initialEmail={user?.email} idPrefix="verify-expired" />
    </div>
  );
}
