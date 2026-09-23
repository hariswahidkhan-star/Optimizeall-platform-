import { ShieldAlert } from 'lucide-react';
import { ButtonLink } from '@/components/ui/ButtonLink';
import { useAuth } from '@/lib/auth/useAuth';
import { StatusPage } from './StatusPage';

/** 403 — signed in, but without the permission this page needs. */
export function ForbiddenPage() {
  const { user } = useAuth();
  return (
    <StatusPage
      code="403"
      icon={<ShieldAlert />}
      title="You don’t have access to this page"
      description={
        <>
          {user ? `You’re signed in as ${user.email}, but ` : ''}this area needs a permission your account doesn’t have.
          If you think that’s a mistake, ask an administrator to review your role.
        </>
      }
      actions={
        <>
          <ButtonLink to="/">Go to the home page</ButtonLink>
          <ButtonLink to="/login" variant="secondary">
            Switch account
          </ButtonLink>
        </>
      }
    />
  );
}
