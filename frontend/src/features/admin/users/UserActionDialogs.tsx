import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useEffect, useState } from 'react';
import { Alert } from '@/components/ui/Alert';
import { Checkbox } from '@/components/ui/Checkbox';
import { ConfirmDialog } from '@/components/ui/ConfirmDialog';
import { FormField } from '@/components/ui/FormField';
import { Select } from '@/components/ui/Select';
import { useToast } from '@/components/ui/toastContext';
import { api } from '@/lib/api/client';
import { useAuth } from '@/lib/auth/useAuth';
import { ROLES, TIERS, type AdminUserDetail } from '../api/types';
import { roleLabel } from '../shared/badges';
import { enumOptions } from '../shared/common';
import { toDisplayError } from '../shared/errors';

export type UserAction = 'suspend' | 'reactivate' | 'roles' | 'tier';

interface DialogProps {
  user: AdminUserDetail;
  open: boolean;
  onClose: () => void;
}

/** Shared mutation: POST/PUT an admin user action and put the returned detail into the cache. */
function useUserMutation(user: AdminUserDetail) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ method, path, body }: { method: 'post' | 'put'; path: string; body: unknown }) =>
      api[method]<AdminUserDetail>(`/admin/users/${user.profile.id}/${path}`, body),
    onSuccess: (detail) => {
      queryClient.setQueryData(['admin', 'user', user.profile.id], detail);
      void queryClient.invalidateQueries({ queryKey: ['admin', 'users'] });
    },
  });
}

export function SuspendDialog({ user, open, onClose }: DialogProps) {
  const toast = useToast();
  const mutation = useUserMutation(user);
  return (
    <ConfirmDialog
      open={open}
      onClose={onClose}
      tone="danger"
      title={`Suspend ${user.profile.displayName}?`}
      description="The account is blocked until an administrator reactivates it."
      confirmLabel="Suspend account"
      requireReason
      reasonHint="Recorded in the audit log and included in the email the user receives."
      confirmText={user.profile.email}
      onConfirm={async ({ reason }) => {
        try {
          await mutation.mutateAsync({ method: 'post', path: 'suspend', body: { reason, confirm: true } });
        } catch (error) {
          throw toDisplayError(error);
        }
        toast.success('Account suspended', `${user.profile.displayName} has been signed out everywhere.`);
      }}
    >
      <Alert tone="warning" title="Sessions are revoked immediately">
        Every session this person has is ended right away — open pages stop working on their next request, and
        they can’t sign in again until reactivated. They get an email saying their account was suspended.
      </Alert>
    </ConfirmDialog>
  );
}

export function ReactivateDialog({ user, open, onClose }: DialogProps) {
  const toast = useToast();
  const mutation = useUserMutation(user);
  return (
    <ConfirmDialog
      open={open}
      onClose={onClose}
      title={`Reactivate ${user.profile.displayName}?`}
      description="They can sign in again straight away and will get an email that their account is active."
      confirmLabel="Reactivate account"
      requireReason
      onConfirm={async ({ reason }) => {
        try {
          await mutation.mutateAsync({ method: 'post', path: 'reactivate', body: { reason } });
        } catch (error) {
          throw toDisplayError(error);
        }
        toast.success('Account reactivated', `${user.profile.displayName} can sign in again.`);
      }}
    />
  );
}

export function RolesDialog({ user, open, onClose }: DialogProps) {
  const toast = useToast();
  const { user: me } = useAuth();
  const mutation = useUserMutation(user);
  const [roles, setRoles] = useState<string[]>(user.roles);
  const [roleError, setRoleError] = useState<string | null>(null);

  useEffect(() => {
    if (open) {
      setRoles(user.roles);
      setRoleError(null);
    }
  }, [open, user.roles]);

  const isSelf = me?.id === user.profile.id;
  const removingOwnAdmin = isSelf && user.roles.includes('Admin') && !roles.includes('Admin');
  const unchanged = [...roles].sort().join() === [...user.roles].sort().join();

  return (
    <ConfirmDialog
      open={open}
      onClose={onClose}
      title={`Change roles for ${user.profile.displayName}`}
      description="Roles are replaced with the selection below. Their sessions are revoked so the new permissions apply when they sign in again."
      confirmLabel="Save roles"
      requireReason
      onConfirm={async ({ reason }) => {
        if (roles.length === 0) {
          setRoleError('Choose at least one role.');
          throw new Error('Choose at least one role.');
        }
        if (unchanged) throw new Error('Select a different set of roles to save a change.');
        try {
          await mutation.mutateAsync({
            method: 'put',
            path: 'roles',
            body: { roles, reason, confirm: true },
          });
        } catch (error) {
          throw toDisplayError(error);
        }
        toast.success(
          'Roles updated',
          `${user.profile.displayName} now has: ${roles.map(roleLabel).join(', ')}.`,
        );
      }}
    >
      <fieldset className="admin-fieldset">
        <legend className="ui-field__label">Roles</legend>
        <div className="stack admin-tight-stack">
          {ROLES.map((role) => (
            <Checkbox
              key={role}
              label={roleLabel(role)}
              checked={roles.includes(role)}
              invalid={!!roleError}
              onChange={(e) => {
                setRoleError(null);
                setRoles((current) =>
                  e.target.checked ? [...current, role] : current.filter((r) => r !== role),
                );
              }}
            />
          ))}
        </div>
        {roleError && <p className="ui-field__error">{roleError}</p>}
      </fieldset>
      {removingOwnAdmin && (
        <Alert tone="warning">
          You are removing your own Admin role. The server will refuse this change.
        </Alert>
      )}
    </ConfirmDialog>
  );
}

export function TierDialog({ user, open, onClose }: DialogProps) {
  const toast = useToast();
  const mutation = useUserMutation(user);
  const [tier, setTier] = useState(user.profile.tier);

  useEffect(() => {
    if (open) setTier(user.profile.tier);
  }, [open, user.profile.tier]);

  return (
    <ConfirmDialog
      open={open}
      onClose={onClose}
      title={`Change tier for ${user.profile.displayName}`}
      description="Tiers can change which campaigns and reward rates apply to this participant."
      confirmLabel="Save tier"
      requireReason
      onConfirm={async ({ reason }) => {
        if (tier === user.profile.tier) throw new Error('Choose a different tier to save a change.');
        try {
          await mutation.mutateAsync({ method: 'put', path: 'tier', body: { tier, reason } });
        } catch (error) {
          throw toDisplayError(error);
        }
        toast.success('Tier updated', `${user.profile.displayName} is now ${tier}.`);
      }}
    >
      <FormField label="Tier" required>
        <Select value={tier} options={enumOptions(TIERS)} onChange={(e) => setTier(e.target.value)} />
      </FormField>
    </ConfirmDialog>
  );
}
