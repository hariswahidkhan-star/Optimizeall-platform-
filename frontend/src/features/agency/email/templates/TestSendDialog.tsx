import { useMutation } from '@tanstack/react-query';
import { useState, type FormEvent } from 'react';
import { Alert } from '@/components/ui/Alert';
import { Button } from '@/components/ui/Button';
import { Dialog } from '@/components/ui/Dialog';
import { FormField } from '@/components/ui/FormField';
import { Input } from '@/components/ui/Input';
import { useToast } from '@/components/ui/toastContext';
import { api } from '@/lib/api/client';
import { errorMessage } from '@/lib/api/errors';
import { useAuth } from '@/lib/auth/useAuth';
import type { TestSendResult } from '../api/types';

/** Sends a test (sample data, [Test] subject) to a verified staff address. */
export function TestSendDialog({ open, onClose, path, clientId }: { open: boolean; onClose: () => void; path: string; clientId: string | null }) {
  const { user } = useAuth();
  const toast = useToast();
  const [to, setTo] = useState(user?.email ?? '');
  const send = useMutation({
    mutationFn: () => api.post<TestSendResult>(path, { to, clientAccountId: clientId }),
    onSuccess: (result) => {
      if (result.sent) {
        toast.success('Test email sent', `Sent to ${to}.`);
        onClose();
      }
    },
  });
  const submit = (event: FormEvent) => {
    event.preventDefault();
    send.mutate();
  };
  return (
    <Dialog
      open={open}
      onClose={onClose}
      title="Send a test email"
      description="Tests use sample merge data and can only go to verified staff addresses."
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" form="email-test-form" loading={send.isPending}>
            Send test
          </Button>
        </>
      }
    >
      <form id="email-test-form" onSubmit={submit} className="stack">
        <FormField label="Send to" required>
          <Input type="email" value={to} onChange={(e) => setTo(e.target.value)} required />
        </FormField>
        {send.isError && <Alert tone="danger">{errorMessage(send.error)}</Alert>}
        {send.data && !send.data.sent && <Alert tone="danger" title="Not sent">{send.data.error}</Alert>}
      </form>
    </Dialog>
  );
}
