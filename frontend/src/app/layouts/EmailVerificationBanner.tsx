import { useMutation } from '@tanstack/react-query';
import { MailWarning } from 'lucide-react';
import { api } from '@/lib/api/client';
import { errorMessage } from '@/lib/api/errors';
import type { MessageResponse } from '@/lib/api/types';
import { Alert } from '@/components/ui/Alert';
import { Button } from '@/components/ui/Button';
import { useToast } from '@/components/ui/toastContext';

/** Account-level reminder shown in every portal until the email address is verified. */
export function EmailVerificationBanner({ email }: { email: string }) {
  const toast = useToast();
  const resend = useMutation({
    mutationFn: () => api.post<MessageResponse>('/auth/resend-verification', { email }),
    onSuccess: (response) => toast.success('Verification email sent', response?.message ?? `Check ${email}.`),
    onError: (error) => toast.error('Couldn’t send the email', errorMessage(error)),
  });

  return (
    <Alert
      tone="warning"
      icon={<MailWarning />}
      title="Verify your email address"
      className="portal-banner"
      actions={
        <Button size="sm" variant="secondary" loading={resend.isPending} onClick={() => resend.mutate()}>
          {resend.isSuccess ? 'Resend again' : 'Resend email'}
        </Button>
      }
    >
      We sent a link to <strong>{email}</strong>. Verify it to submit posts and receive payouts.
    </Alert>
  );
}
