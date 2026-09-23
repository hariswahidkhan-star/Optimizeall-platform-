import { useMutation } from '@tanstack/react-query';
import { KeyRound, Mail } from 'lucide-react';
import { useEffect, useState, type FormEvent } from 'react';
import { Link } from 'react-router-dom';
import { Alert } from '@/components/ui/Alert';
import { Button } from '@/components/ui/Button';
import { FormField } from '@/components/ui/FormField';
import { Input } from '@/components/ui/Input';
import { api } from '@/lib/api/client';
import type { MessageResponse } from '@/lib/api/types';
import { mapServerErrors } from './formErrors';
import '@/app/layouts/AuthLayout.css';

const EMAIL_RE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

export function ForgotPasswordPage() {
  const [email, setEmail] = useState('');
  const [error, setError] = useState<string | null>(null);
  const request = useMutation({
    mutationFn: (address: string) => api.post<MessageResponse>('/auth/forgot-password', { email: address }),
  });
  const server = request.isError ? mapServerErrors(request.error, ['email']) : null;

  useEffect(() => {
    document.title = 'Reset your password · Optimize All';
  }, []);

  const onSubmit = (event: FormEvent) => {
    event.preventDefault();
    if (!EMAIL_RE.test(email.trim())) {
      setError('Enter the email address you registered with.');
      document.getElementById('forgot-email')?.focus();
      return;
    }
    setError(null);
    request.mutate(email.trim());
  };

  if (request.isSuccess) {
    return (
      <div className="auth-page">
        <span className="auth-page__icon" aria-hidden="true">
          <Mail />
        </span>
        <div className="auth-page__header" role="status">
          <h1 className="auth-page__title">Check your email</h1>
          <p className="auth-page__subtitle">
            {request.data?.message ?? 'If an account exists for that email, we’ve sent a reset link.'} The
            link works once and expires soon.
          </p>
        </div>
        <p className="auth-page__switch">
          Remembered it?{' '}
          <Link className="ui-link" to="/login">
            Back to sign in
          </Link>
        </p>
      </div>
    );
  }

  return (
    <div className="auth-page">
      <span className="auth-page__icon" aria-hidden="true">
        <KeyRound />
      </span>
      <div className="auth-page__header">
        <h1 className="auth-page__title">Forgot your password?</h1>
        <p className="auth-page__subtitle">Enter your email and we’ll send you a link to choose a new one.</p>
      </div>
      {server?.form && (
        <Alert tone="danger" role="alert">
          {server.form.title}
        </Alert>
      )}
      <form className="auth-form" onSubmit={onSubmit} noValidate aria-label="Request password reset">
        <FormField id="forgot-email" label="Email" required error={error ?? server?.fields.email}>
          <Input
            type="email"
            autoComplete="email"
            inputMode="email"
            leading={<Mail />}
            value={email}
            onChange={(e) => setEmail(e.target.value)}
          />
        </FormField>
        <Button type="submit" size="lg" fullWidth loading={request.isPending}>
          Send reset link
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
