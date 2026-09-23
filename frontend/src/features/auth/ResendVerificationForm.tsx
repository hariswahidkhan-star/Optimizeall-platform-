import { useMutation } from '@tanstack/react-query';
import { Send } from 'lucide-react';
import { useState, type FormEvent } from 'react';
import { Alert } from '@/components/ui/Alert';
import { Button } from '@/components/ui/Button';
import { FormField } from '@/components/ui/FormField';
import { Input } from '@/components/ui/Input';
import { api } from '@/lib/api/client';
import { errorMessage } from '@/lib/api/errors';
import type { MessageResponse } from '@/lib/api/types';

const EMAIL_RE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

/** Asks for a new verification email. The API always answers 202 (no account enumeration). */
export function ResendVerificationForm({
  initialEmail = '',
  idPrefix,
}: {
  initialEmail?: string;
  idPrefix: string;
}) {
  const [email, setEmail] = useState(initialEmail);
  const [error, setError] = useState<string | null>(null);
  const resend = useMutation({
    mutationFn: (address: string) =>
      api.post<MessageResponse>('/auth/resend-verification', { email: address }),
  });

  const onSubmit = (event: FormEvent) => {
    event.preventDefault();
    if (!EMAIL_RE.test(email.trim())) {
      setError('Enter the email address you registered with.');
      return;
    }
    setError(null);
    resend.mutate(email.trim());
  };

  return (
    <form className="auth-form" onSubmit={onSubmit} noValidate aria-label="Resend verification email">
      {resend.isSuccess && (
        <Alert tone="success" role="status" title="Check your inbox">
          {resend.data?.message ?? 'If the account exists and is unverified, a new link is on its way.'}
        </Alert>
      )}
      {resend.isError && (
        <Alert tone="danger" role="alert">
          {errorMessage(resend.error)}
        </Alert>
      )}
      <FormField id={`${idPrefix}-email`} label="Email address" error={error}>
        <Input type="email" autoComplete="email" value={email} onChange={(e) => setEmail(e.target.value)} />
      </FormField>
      <Button type="submit" variant="secondary" leadingIcon={<Send />} loading={resend.isPending}>
        Send a new link
      </Button>
    </form>
  );
}
