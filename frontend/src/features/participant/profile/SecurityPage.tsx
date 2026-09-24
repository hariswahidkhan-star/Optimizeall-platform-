import { useMutation, useQuery } from '@tanstack/react-query';
import { useState, type FormEvent } from 'react';
import { Alert } from '@/components/ui/Alert';
import { Button } from '@/components/ui/Button';
import { Card, CardBody, CardHeader } from '@/components/ui/Card';
import { FormField } from '@/components/ui/FormField';
import { PasswordInput } from '@/components/ui/PasswordInput';
import { useToast } from '@/components/ui/toastContext';
import { mapServerErrors } from '@/features/auth/formErrors';
import { GoogleConnectionCard } from '@/features/auth/google/GoogleConnectionCard';
import { signInMethodsQueryKey } from '@/features/auth/google/googleApi';
import { passwordProblem } from '@/features/auth/passwordPolicy';
import { PasswordStrength } from '@/features/auth/PasswordStrength';
import { api } from '@/lib/api/client';
import type { MessageResponse, SignInMethods } from '@/lib/api/types';
import { useAuth } from '@/lib/auth/useAuth';

type FieldKey = 'currentPassword' | 'newPassword' | 'confirm';
const SERVER_FIELDS = ['currentPassword', 'newPassword', 'password'];
const CODE_TO_FIELD = { 'auth.invalid_password': 'currentPassword', 'auth.weak_password': 'newPassword' };

/**
 * Accounts created with Google have no password: changing one (which needs the current password) is impossible, so
 * they get a link, sent to their verified address, to set a first password through the reset flow instead.
 */
function SetPasswordCard({ email }: { email: string | undefined }) {
  const send = useMutation({
    mutationFn: () => api.post<MessageResponse>('/auth/forgot-password', { email }),
  });
  return (
    <Card as="section" aria-labelledby="set-password-title">
      <CardHeader
        titleId="set-password-title"
        title="Set a password"
        description="You sign in with Google and don’t have a password yet. Add one to also sign in with your email address."
      />
      <CardBody className="stack">
        {send.isSuccess ? (
          <Alert tone="success" title="Check your email">
            We’ve sent a link to <strong>{email}</strong> to set your password. It expires in one hour.
          </Alert>
        ) : (
          <>
            {send.isError && (
              <Alert tone="danger" role="alert">
                Couldn’t send the link. Please try again.
              </Alert>
            )}
            <div>
              <Button onClick={() => send.mutate()} loading={send.isPending}>
                Email me a link to set a password
              </Button>
            </div>
          </>
        )}
      </CardBody>
    </Card>
  );
}

/**
 * Change password (the API revokes every session, so the user is signed out and asked to sign in again), or set a
 * first one for accounts created with Google, and the Google sign-in connection.
 */
export function SecurityPage() {
  const { user, logout } = useAuth();
  const toast = useToast();
  const methods = useQuery({
    queryKey: signInMethodsQueryKey,
    queryFn: () => api.get<SignInMethods>('/auth/external-logins'),
  });
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

  if (methods.data?.hasPassword === false)
    return (
      <div className="stack">
        <SetPasswordCard email={user?.email} />
        <GoogleConnectionCard />
      </div>
    );

  return (
    <div className="stack">
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
            <FormField
              id="security-confirm"
              label="Confirm new password"
              required
              error={errorFor('confirm')}
            >
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
      <GoogleConnectionCard />
    </div>
  );
}
