import { useMutation } from '@tanstack/react-query';
import { useState, type FormEvent } from 'react';
import { Alert } from '@/components/ui/Alert';
import { Button } from '@/components/ui/Button';
import { Card, CardBody, CardHeader } from '@/components/ui/Card';
import { FormField } from '@/components/ui/FormField';
import { PasswordInput } from '@/components/ui/PasswordInput';
import { useToast } from '@/components/ui/toastContext';
import { mapServerErrors } from '@/features/auth/formErrors';
import { passwordProblem } from '@/features/auth/passwordPolicy';
import { PasswordStrength } from '@/features/auth/PasswordStrength';
import { api } from '@/lib/api/client';
import type { MessageResponse } from '@/lib/api/types';
import { useAuth } from '@/lib/auth/useAuth';

type FieldKey = 'currentPassword' | 'newPassword' | 'confirm';
const SERVER_FIELDS = ['currentPassword', 'newPassword', 'password'];
const CODE_TO_FIELD = { 'auth.invalid_password': 'currentPassword', 'auth.weak_password': 'newPassword' };

/** Change password. The API revokes every session, so the user is signed out and asked to sign in again. */
export function SecurityPage() {
  const { user, logout } = useAuth();
  const toast = useToast();
  const [current, setCurrent] = useState('');
  const [next, setNext] = useState('');
  const [confirm, setConfirm] = useState('');
  const [clientErrors, setClientErrors] = useState<Partial<Record<FieldKey, string>>>({});

  const change = useMutation({
    mutationFn: () =>
      api.post<MessageResponse>('/auth/change-password', { currentPassword: current, newPassword: next }),
    onSuccess: async (response) => {
      toast.success('Password changed', response?.message ?? 'Please sign in again with your new password.');
      await logout();
    },
  });

  const server = change.isError ? mapServerErrors(change.error, SERVER_FIELDS, CODE_TO_FIELD) : null;
  const errorFor = (key: FieldKey) =>
    clientErrors[key] ??
    (key === 'newPassword'
      ? (server?.fields.newPassword ?? server?.fields.password)
      : key === 'currentPassword'
        ? server?.fields.currentPassword
        : undefined);

  const onSubmit = (event: FormEvent) => {
    event.preventDefault();
    const found: Partial<Record<FieldKey, string>> = {};
    if (!current) found.currentPassword = 'Enter your current password.';
    const problem = passwordProblem(next, user?.email);
    if (problem) found.newPassword = problem;
    else if (next === current) found.newPassword = 'Choose a password different from your current one.';
    if (!found.newPassword && confirm !== next) found.confirm = 'The passwords don’t match.';
    setClientErrors(found);
    const first = (['currentPassword', 'newPassword', 'confirm'] as const).find((k) => found[k]);
    if (first) {
      document.getElementById(`security-${first}`)?.focus();
      return;
    }
    change.mutate();
  };

  return (
    <Card as="section" aria-labelledby="security-title">
      <CardHeader
        titleId="security-title"
        title="Change password"
        description="After changing your password you’ll be signed out everywhere and asked to sign in again."
      />
      <CardBody>
        <form className="stack" onSubmit={onSubmit} noValidate aria-labelledby="security-title">
          {server?.form && (
            <Alert tone="danger" role="alert">
              {server.form.title}
            </Alert>
          )}
          <FormField
            id="security-currentPassword"
            label="Current password"
            required
            error={errorFor('currentPassword')}
          >
            <PasswordInput
              autoComplete="current-password"
              value={current}
              onChange={(e) => setCurrent(e.target.value)}
            />
          </FormField>
          <FormField
            id="security-newPassword"
            label="New password"
            required
            error={errorFor('newPassword')}
            hint={<PasswordStrength password={next} email={user?.email} />}
          >
            <PasswordInput
              autoComplete="new-password"
              maxLength={128}
              value={next}
              onChange={(e) => setNext(e.target.value)}
            />
          </FormField>
          <FormField id="security-confirm" label="Confirm new password" required error={errorFor('confirm')}>
            <PasswordInput
              autoComplete="new-password"
              maxLength={128}
              value={confirm}
              onChange={(e) => setConfirm(e.target.value)}
            />
          </FormField>
          <div>
            <Button type="submit" loading={change.isPending}>
              Change password
            </Button>
          </div>
        </form>
      </CardBody>
    </Card>
  );
}
