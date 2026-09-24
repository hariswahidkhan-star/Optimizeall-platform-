import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { FlaskConical } from 'lucide-react';
import { useEffect, useId, useState, type FormEvent } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { defaultLandingPath } from '@/app/portals';
import { Alert } from '@/components/ui/Alert';
import { Badge } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';
import { Checkbox } from '@/components/ui/Checkbox';
import { ConfirmDialog } from '@/components/ui/ConfirmDialog';
import { Dialog } from '@/components/ui/Dialog';
import { FormField } from '@/components/ui/FormField';
import { Input } from '@/components/ui/Input';
import { Select } from '@/components/ui/Select';
import { api } from '@/lib/api/client';
import type { PagedResult } from '@/lib/api/types';
import { useAuth } from '@/lib/auth/useAuth';
import { roleLabel } from '../shared/badges';
import { mapFieldErrors, toDisplayError } from '../shared/errors';

/** Every role a test account may have (mirrors the backend Role enum). */
export const TEST_USER_ROLES = [
  'Participant',
  'Reviewer',
  'CampaignManager',
  'Finance',
  'Admin',
  'AccountManager',
  'Strategist',
  'ContentCreator',
  'Designer',
  'SeoSpecialist',
  'AdsSpecialist',
  'SocialMediaManager',
  'SalesRep',
  'Client',
] as const;

const CLIENT_MEMBER_ROLES = ['Viewer', 'Approver', 'Billing', 'Owner'] as const;

/** "TEST" label for QA/demo accounts, used in lists, headers and the impersonation banner. */
export function TestBadge() {
  return (
    <Badge size="sm" tone="warning" title="Test account: never paid, left out of analytics">
      TEST
    </Badge>
  );
}

/** Who may be impersonated from the UI (the API enforces the same rules). */
export function canImpersonateTarget(target: { id: string; roles: string[]; status: string }, meId?: string) {
  return target.id !== meId && target.status === 'Active' && !target.roles.includes('Admin');
}

export interface ImpersonationTarget {
  id: string;
  displayName: string;
  email: string;
  roles: string[];
  isTestAccount?: boolean;
}

/**
 * "Log in as" confirmation: a written reason (audited) and the target's email typed out. On success the app switches
 * to the target's session and portal; the banner's Exit returns to the admin users page.
 */
export function ImpersonateDialog({
  target,
  open,
  onClose,
}: {
  target: ImpersonationTarget;
  open: boolean;
  onClose: () => void;
}) {
  const { startImpersonation } = useAuth();
  const navigate = useNavigate();
  return (
    <ConfirmDialog
      open={open}
      onClose={onClose}
      tone="danger"
      title={`Log in as ${target.displayName}?`}
      description="You will see the platform exactly as this user does for up to 60 minutes. Everything you do is recorded in the audit log under your name."
      confirmLabel="Log in as user"
      requireReason
      reasonMinLength={5}
      reasonLabel="Why do you need to view this account?"
      reasonHint="Required and audited, e.g. the support ticket you are working on."
      confirmText={target.email}
      onConfirm={async ({ reason }) => {
        let user;
        try {
          user = await startImpersonation(target.id, reason);
        } catch (error) {
          throw toDisplayError(error);
        }
        navigate(defaultLandingPath(user.permissions), { replace: true });
      }}
    >
      <Alert tone="warning" title="Some actions are blocked while you view as someone else">
        Changing the password, email or payout details, approving or recording payments, managing API keys and
        impersonating again are refused. Exit from the banner at the top of every page.
        {target.isTestAccount && (
          <>
            {' '}
            <TestBadge />
          </>
        )}
      </Alert>
    </ConfirmDialog>
  );
}

interface ClientSummary {
  id: string;
  name: string;
}

interface CreatedTestUser {
  id: string;
  email: string;
  displayName: string;
  roles: string[];
  password: string;
  clientAccountId: string | null;
}

const FIELDS = ['roles', 'displayName', 'clientAccountId'] as const;
const CODE_TO_FIELD = {
  'admin.client_role_required': 'clientAccountId',
  'admin.client_not_found': 'clientAccountId',
};

/** Creates a verified test account of any role; the generated password is shown once. */
export function CreateTestUserDialog({ open, onClose }: { open: boolean; onClose: () => void }) {
  const formId = useId();
  const queryClient = useQueryClient();
  const [roles, setRoles] = useState<string[]>(['Participant']);
  const [displayName, setDisplayName] = useState('');
  const [clientAccountId, setClientAccountId] = useState('');
  const [clientMemberRole, setClientMemberRole] = useState('Viewer');
  const [roleError, setRoleError] = useState<string | null>(null);
  const [created, setCreated] = useState<CreatedTestUser | null>(null);
  const isClient = roles.includes('Client');

  const clients = useQuery({
    queryKey: ['admin', 'test-user-clients'],
    queryFn: ({ signal }) =>
      api.get<PagedResult<ClientSummary>>('/agency/clients', { query: { pageSize: 200 }, signal }),
    enabled: open && isClient,
  });

  const create = useMutation({
    mutationFn: () =>
      api.post<CreatedTestUser>('/admin/test-users', {
        roles,
        displayName: displayName.trim() || undefined,
        clientAccountId: isClient && clientAccountId ? clientAccountId : undefined,
        clientMemberRole: isClient && clientAccountId ? clientMemberRole : undefined,
      }),
    onSuccess: (user) => {
      setCreated(user);
      void queryClient.invalidateQueries({ queryKey: ['admin', 'users'] });
    },
  });

  useEffect(() => {
    if (open) {
      setRoles(['Participant']);
      setDisplayName('');
      setClientAccountId('');
      setClientMemberRole('Viewer');
      setRoleError(null);
      setCreated(null);
      create.reset();
    }
    // Reset only when the dialog opens.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open]);

  const server = mapFieldErrors(create.error, FIELDS, CODE_TO_FIELD);

  const submit = (event: FormEvent) => {
    event.preventDefault();
    if (roles.length === 0) {
      setRoleError('Choose at least one role.');
      return;
    }
    setRoleError(null);
    create.mutate();
  };

  if (created) {
    return (
      <Dialog
        open={open}
        onClose={onClose}
        title="Test user created"
        icon={<FlaskConical />}
        tone="success"
        footer={
          <>
            <Link className="ui-link" to={`/admin/users/${created.id}`} onClick={onClose}>
              Open user
            </Link>
            <Button onClick={onClose}>Done</Button>
          </>
        }
      >
        <div className="stack">
          <Alert tone="warning" title="Copy the password now">
            It is shown only once and cannot be retrieved later.
          </Alert>
          <dl className="admin-dl">
            <dt>Name</dt>
            <dd>
              {created.displayName} <TestBadge />
            </dd>
            <dt>Email</dt>
            <dd>
              <code>{created.email}</code>
            </dd>
            <dt>Password</dt>
            <dd>
              <code data-testid="test-user-password">{created.password}</code>
            </dd>
            <dt>Roles</dt>
            <dd>{created.roles.map(roleLabel).join(', ')}</dd>
          </dl>
        </div>
      </Dialog>
    );
  }

  return (
    <Dialog
      open={open}
      onClose={onClose}
      title="Create a test user"
      description="A verified account for QA and demos. Test users are never paid, are left out of analytics and marketing KPIs, and are labelled TEST everywhere."
      icon={<FlaskConical />}
      dismissible={!create.isPending}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={create.isPending}>
            Cancel
          </Button>
          <Button type="submit" form={formId} loading={create.isPending}>
            Create test user
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
        <FormField
          label="Name"
          hint="Optional. Also used in the generated email address."
          error={server.fields.displayName}
        >
          <Input value={displayName} maxLength={80} onChange={(e) => setDisplayName(e.target.value)} />
        </FormField>
        <fieldset
          className="admin-fieldset"
          aria-describedby={roleError ? `${formId}-roles-error` : undefined}
        >
          <legend className="ui-field__label">Roles</legend>
          <div className="stack admin-tight-stack">
            {TEST_USER_ROLES.map((role) => (
              <Checkbox
                key={role}
                label={roleLabel(role)}
                checked={roles.includes(role)}
                invalid={!!roleError}
                onChange={(e) =>
                  setRoles((current) =>
                    e.target.checked ? [...current, role] : current.filter((r) => r !== role),
                  )
                }
              />
            ))}
          </div>
          {(roleError || server.fields.roles) && (
            <p id={`${formId}-roles-error`} className="ui-field__error">
              {roleError ?? server.fields.roles?.join(' ')}
            </p>
          )}
        </fieldset>
        {isClient && (
          <>
            <FormField
              label="Client organization"
              hint="Optional: the organization this client user belongs to."
              error={server.fields.clientAccountId}
            >
              <Select
                value={clientAccountId}
                placeholder={clients.isPending ? 'Loading…' : 'No organization'}
                options={(clients.data?.items ?? []).map((c) => ({ value: c.id, label: c.name }))}
                onChange={(e) => setClientAccountId(e.target.value)}
              />
            </FormField>
            {clientAccountId && (
              <FormField label="Client role">
                <Select
                  value={clientMemberRole}
                  options={CLIENT_MEMBER_ROLES.map((r) => ({ value: r, label: r }))}
                  onChange={(e) => setClientMemberRole(e.target.value)}
                />
              </FormField>
            )}
          </>
        )}
      </form>
    </Dialog>
  );
}
