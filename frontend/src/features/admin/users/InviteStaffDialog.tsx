import { useMutation, useQueryClient } from '@tanstack/react-query';
import { MailPlus } from 'lucide-react';
import { useEffect, useId, useState, type FormEvent } from 'react';
import { useNavigate } from 'react-router-dom';
import { Alert } from '@/components/ui/Alert';
import { Button } from '@/components/ui/Button';
import { Checkbox } from '@/components/ui/Checkbox';
import { Dialog } from '@/components/ui/Dialog';
import { FormField } from '@/components/ui/FormField';
import { Input } from '@/components/ui/Input';
import { Select } from '@/components/ui/Select';
import { useToast } from '@/components/ui/toastContext';
import { countryOptions } from '@/features/auth/localeOptions';
import { api } from '@/lib/api/client';
import { ROLES, type AdminUserDetail } from '../api/types';
import { roleLabel } from '../shared/badges';
import { mapFieldErrors } from '../shared/errors';

const STAFF_ROLES = ROLES.filter((r) => r !== 'Participant');
const FIELDS = ['email', 'displayName', 'countryCode', 'roles'] as const;
const CODE_TO_FIELD = { 'admin.email_exists': 'email', 'admin.staff_role_required': 'roles' };

const ROLE_HELP: Record<string, string> = {
  Reviewer: 'Reviews submissions, verifies social accounts, answers support tickets.',
  CampaignManager: 'Creates and publishes campaigns, rewards and marketing.',
  Finance: 'Ledger, payouts and financial audit.',
  Admin: 'Full access, including users, roles and platform settings.',
};

export function InviteStaffDialog({ open, onClose }: { open: boolean; onClose: () => void }) {
  const formId = useId();
  const toast = useToast();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [email, setEmail] = useState('');
  const [displayName, setDisplayName] = useState('');
  const [countryCode, setCountryCode] = useState('');
  const [roles, setRoles] = useState<string[]>([]);
  const [clientErrors, setClientErrors] = useState<Record<string, string>>({});

  const invite = useMutation({
    mutationFn: () =>
      api.post<AdminUserDetail>('/admin/users/staff', {
        email: email.trim(),
        displayName: displayName.trim(),
        countryCode,
        roles,
      }),
    onSuccess: (user) => {
      void queryClient.invalidateQueries({ queryKey: ['admin', 'users'] });
      toast.success(
        'Staff account created',
        `${user.profile.email} will get an email to set their password.`,
      );
      onClose();
      navigate(user.profile.id);
    },
  });

  useEffect(() => {
    if (open) {
      setEmail('');
      setDisplayName('');
      setCountryCode('');
      setRoles([]);
      setClientErrors({});
      invite.reset();
    }
    // Reset only when the dialog opens.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open]);

  const server = mapFieldErrors(invite.error, FIELDS, CODE_TO_FIELD);
  const errorFor = (field: string) => clientErrors[field] ?? server.fields[field];

  const submit = (event: FormEvent) => {
    event.preventDefault();
    const found: Record<string, string> = {};
    if (!/^\S+@\S+\.\S+$/.test(email.trim())) found.email = 'Enter a valid email address.';
    if (displayName.trim().length < 2) found.displayName = 'Enter a name of at least 2 characters.';
    if (!countryCode) found.countryCode = 'Choose a country.';
    if (roles.length === 0) found.roles = 'Choose at least one staff role.';
    setClientErrors(found);
    if (Object.keys(found).length === 0) invite.mutate();
  };

  return (
    <Dialog
      open={open}
      onClose={onClose}
      title="Invite a staff member"
      description="We create a verified account and email them a link to set their password. The link expires, so they should use it soon."
      icon={<MailPlus />}
      dismissible={!invite.isPending}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={invite.isPending}>
            Cancel
          </Button>
          <Button type="submit" form={formId} loading={invite.isPending}>
            Send invitation
          </Button>
        </>
      }
    >
      <form id={formId} className="stack" onSubmit={submit} noValidate>
        {server.form && (
          <Alert tone="danger" role="alert" title={server.form.title}>
            {server.form.details.length > 0 && (
              <ul>
                {server.form.details.map((d) => (
                  <li key={d}>{d}</li>
                ))}
              </ul>
            )}
          </Alert>
        )}
        <FormField label="Email" required error={errorFor('email')}>
          <Input type="email" autoComplete="off" value={email} onChange={(e) => setEmail(e.target.value)} />
        </FormField>
        <FormField label="Name" required error={errorFor('displayName')}>
          <Input value={displayName} maxLength={100} onChange={(e) => setDisplayName(e.target.value)} />
        </FormField>
        <FormField label="Country" required error={errorFor('countryCode')}>
          <Select
            value={countryCode}
            placeholder="Choose a country"
            options={countryOptions()}
            onChange={(e) => setCountryCode(e.target.value)}
          />
        </FormField>
        <fieldset
          className="admin-fieldset"
          aria-describedby={errorFor('roles') ? `${formId}-roles-error` : undefined}
        >
          <legend className="ui-field__label">Roles</legend>
          <div className="stack admin-tight-stack">
            {STAFF_ROLES.map((role) => (
              <Checkbox
                key={role}
                label={roleLabel(role)}
                description={ROLE_HELP[role]}
                checked={roles.includes(role)}
                onChange={(e) =>
                  setRoles((current) =>
                    e.target.checked ? [...current, role] : current.filter((r) => r !== role),
                  )
                }
              />
            ))}
          </div>
          {errorFor('roles') && (
            <p id={`${formId}-roles-error`} className="ui-field__error">
              {[errorFor('roles')].flat().join(' ')}
            </p>
          )}
        </fieldset>
      </form>
    </Dialog>
  );
}
