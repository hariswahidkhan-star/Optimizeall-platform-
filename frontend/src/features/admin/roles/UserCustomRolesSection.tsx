import { KeyRound } from 'lucide-react';
import { useState } from 'react';
import { Link } from 'react-router-dom';
import { Alert } from '@/components/ui/Alert';
import { Badge } from '@/components/ui/Badge';
import { Card, CardBody, CardHeader } from '@/components/ui/Card';
import { Checkbox } from '@/components/ui/Checkbox';
import { SkeletonText } from '@/components/ui/Skeleton';
import { useToast } from '@/components/ui/toastContext';
import { Permissions } from '@/lib/auth/permissions';
import type { AssignedCustomRole } from '../api/types';
import { QueryError, useCan } from '../shared/common';
import { adminErrorMessage } from '../shared/errors';
import { useRolesOverview, useToggleAssignment } from './api';
import './roles.css';

/**
 * Custom roles on the admin user page. With `roles.manage` each role is a checkbox that assigns/unassigns it right away
 * (the API enforces the guardrails; roles holding permissions you can't grant are disabled). Otherwise read-only.
 */
export function UserCustomRolesSection({
  userId,
  displayName,
  assigned,
}: {
  userId: string;
  displayName: string;
  assigned: AssignedCustomRole[];
}) {
  const canManage = useCan(Permissions.RolesManage);
  const overview = useRolesOverview(canManage);
  const toggle = useToggleAssignment(userId);
  const toast = useToast();
  const [error, setError] = useState<string | null>(null);
  const [pendingId, setPendingId] = useState<string | null>(null);
  const assignedIds = new Set(assigned.map((r) => r.id));

  let body;
  if (!canManage) {
    body =
      assigned.length === 0 ? (
        <p className="text-muted">No custom roles.</p>
      ) : (
        <ul className="cluster admin-tight" aria-label="Custom roles">
          {assigned.map((r) => (
            <li key={r.id}>
              <Badge tone="brand" icon={<KeyRound />}>
                {r.name}
              </Badge>
            </li>
          ))}
        </ul>
      );
  } else if (overview.isPending) {
    body = <SkeletonText lines={3} />;
  } else if (overview.isError) {
    body = <QueryError error={overview.error} onRetry={() => void overview.refetch()} headingLevel={3} compact />;
  } else if (overview.data.custom.length === 0) {
    body = (
      <p className="text-muted">
        No custom roles exist yet. <Link className="ui-link" to="/admin/roles">Create one</Link>.
      </p>
    );
  } else {
    body = (
      <fieldset className="admin-fieldset">
        <legend className="ui-field__label">Custom roles for {displayName}</legend>
        <ul className="roles-user-list">
          {overview.data.custom.map((role) => {
            const checked = assignedIds.has(role.id);
            return (
              <li key={role.id}>
                <Checkbox
                  label={role.name}
                  description={
                    role.canManage
                      ? (role.description ?? `${role.permissions.length} permissions`)
                      : 'Includes permissions you can’t grant, so you can’t change this assignment.'
                  }
                  checked={checked}
                  disabled={!role.canManage || pendingId !== null}
                  aria-busy={pendingId === role.id}
                  onChange={(e) => {
                    const assign = e.target.checked;
                    setError(null);
                    setPendingId(role.id);
                    toggle.mutate(
                      { roleId: role.id, assign },
                      {
                        onSuccess: () =>
                          toast.success(
                            assign ? 'Role assigned' : 'Role removed',
                            `${displayName} ${assign ? 'now has' : 'no longer has'} ${role.name}. It applies on their next request.`,
                          ),
                        onError: (err) => setError(adminErrorMessage(err)),
                        onSettled: () => setPendingId(null),
                      },
                    );
                  }}
                />
              </li>
            );
          })}
        </ul>
      </fieldset>
    );
  }

  return (
    <Card as="section" aria-labelledby="user-custom-roles">
      <CardHeader
        title="Custom roles"
        titleId="user-custom-roles"
        description="Extra permissions on top of the built-in roles. Changes apply immediately, without signing them out."
      />
      <CardBody>
        <div className="stack">
          {error && (
            <Alert tone="danger" role="alert">
              {error}
            </Alert>
          )}
          {body}
        </div>
      </CardBody>
    </Card>
  );
}
