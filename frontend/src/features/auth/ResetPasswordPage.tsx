import { useMutation } from '@tanstack/react-query';
import { KeyRound, MailX } from 'lucide-react';
import { useEffect, useState, type FormEvent } from 'react';
import { Link, useNavigate, useSearchParams } from 'react-router-dom';
import { Alert } from '@/components/ui/Alert';
import { Button } from '@/components/ui/Button';
import { ButtonLink } from '@/components/ui/ButtonLink';
import { FormField } from '@/components/ui/FormField';
import { PasswordInput } from '@/components/ui/PasswordInput';
import { api } from '@/lib/api/client';
import type { MessageResponse } from '@/lib/api/types';
import { mapServerErrors } from './formErrors';
import { passwordProblem } from './passwordPolicy';
import { PasswordStrength } from './PasswordStrength';
import '@/app/layouts/AuthLayout.css';

/** Opened from the reset email (?token=...). */
export function ResetPasswordPage() {
  const [params] = useSearchParams();
  const token = params.get('token')?.trim() ?? '';
  const navigate = useNavigate();
  const [password, setPassword] = useState('');
  const [confirm, setConfirm] = useState('');
  const [errors, setErrors] = useState<{ password?: string; confirm?: string }>({});

  const reset = useMutation({
    mutationFn: (newPassword: string) =>
      api.post<MessageResponse>('/auth/reset-password', { token, newPassword }),
    onSuccess: () => navigate('/login?reset=1', { replace: true }),
  });
  const server = reset.isError
    ? mapServerErrors(reset.error, ['newPassword', 'password'], { 'auth.weak_password': 'password' })
    : null;
  const invalidToken = server?.form?.code === 'auth.invalid_token';

  useEffect(() => {
    document.title = 'Choose a new password · Optimize All';
  }, []);

  const onSubmit = (event: FormEvent) => {
    event.preventDefault();
    const next: typeof errors = {};
    const problem = passwordProblem(password);
    if (problem) next.password = problem;
    if (!next.password && confirm !== password) next.confirm = 'The passwords don’t match.';
    setErrors(next);
    if (next.password) return document.getElementById('reset-password')?.focus();
    if (next.confirm) return document.getElementById('reset-confirm')?.focus();
    reset.mutate(password);
  };

  if (!token || invalidToken) {
    return (
      <div className="auth-page">
        <span className="auth-page__icon auth-page__icon--danger" aria-hidden="true">
          <MailX />
        </span>
        <div className="auth-page__header" role={invalidToken ? 'alert' : undefined}>
          <h1 className="auth-page__title">
            {token ? 'This reset link has expired' : 'This link is incomplete'}
          </h1>
          <p className="auth-page__subtitle">
            Reset links work once and expire after a while. Request a new link to choose a password.
          </p>
        </div>
        <ButtonLink to="/forgot-password" size="lg" fullWidth>
          Request a new link
        </ButtonLink>
      </div>
    );
  }

  const passwordError = errors.password ?? server?.fields.password ?? server?.fields.newPassword;

  return (
    <div className="auth-page">
      <span className="auth-page__icon" aria-hidden="true">
        <KeyRound />
      </span>
      <div className="auth-page__header">
        <h1 className="auth-page__title">Choose a new password</h1>
        <p className="auth-page__subtitle">You’ll be signed out on other devices after the change.</p>
      </div>
      {server?.form && (
        <Alert tone="danger" role="alert">
          {server.form.title}
        </Alert>
      )}
      <form className="auth-form" onSubmit={onSubmit} noValidate aria-label="Choose a new password">
        <FormField
          id="reset-password"
          label="New password"
          required
          error={passwordError}
          hint={<PasswordStrength password={password} />}
        >
          <PasswordInput
            autoComplete="new-password"
            maxLength={128}
            value={password}
            onChange={(e) => setPassword(e.target.value)}
          />
        </FormField>
        <FormField id="reset-confirm" label="Confirm new password" required error={errors.confirm}>
          <PasswordInput
            autoComplete="new-password"
            maxLength={128}
            value={confirm}
            onChange={(e) => setConfirm(e.target.value)}
          />
        </FormField>
        <Button type="submit" size="lg" fullWidth loading={reset.isPending}>
          Change password
        </Button>
      </form>
      <p className="auth-page__switch">
        <Link className="ui-link" to="/login">
          Back to sign in
        </Link>
      </p>
    </div>
  );
}
