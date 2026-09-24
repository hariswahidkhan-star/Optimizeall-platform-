import { Eye, LogOut } from 'lucide-react';
import { useState } from 'react';
import { DateTime } from '@/components/ui/DateTime';
import { useToast } from '@/components/ui/toastContext';
import { errorMessage } from '@/lib/api/errors';
import { useOptionalAuth } from '@/lib/auth/useAuth';
import './ImpersonationBanner.css';

/** "CampaignManager" → "campaign manager". */
function roleName(role: string | undefined): string {
  if (!role) return 'user';
  return role.replace(/([a-z])([A-Z])/g, '$1 $2').toLowerCase();
}

/**
 * Persistent, high-contrast notice shown at the top of every layout while a staff member is viewing as another user
 * ("log in as"), with the only way back: Exit. Renders nothing otherwise.
 */
export function ImpersonationBanner() {
  const auth = useOptionalAuth();
  const toast = useToast();
  const [exiting, setExiting] = useState(false);
  const user = auth?.user;
  const impersonation = auth?.impersonation;
  if (!auth || !user || !impersonation) return null;

  const primaryRole = user.roles.find((r) => r !== 'Participant') ?? user.roles[0];

  const exit = async () => {
    setExiting(true);
    try {
      await auth.exitImpersonation();
    } catch (error) {
      toast.error('Couldn’t exit', errorMessage(error));
    } finally {
      setExiting(false);
    }
  };

  return (
    <section className="impersonation-banner" aria-label="Impersonation" data-testid="impersonation-banner">
      <Eye aria-hidden="true" className="impersonation-banner__icon" />
      <p className="impersonation-banner__text">
        You are viewing as <strong>{user.displayName}</strong> ({roleName(primaryRole)})
        {user.isTestAccount && (
          <>
            {' '}
            <span className="impersonation-banner__test">TEST</span>
          </>
        )}
        <span>
          {' '}
          — signed in as {impersonation.displayName}. Ends{' '}
          <DateTime value={impersonation.expiresAt} format="datetime" />. Sensitive actions (password, payout
          details, payments) are blocked.
        </span>
      </p>
      <button
        type="button"
        className="impersonation-banner__exit"
        onClick={() => void exit()}
        disabled={exiting}
        aria-busy={exiting || undefined}
      >
        <LogOut aria-hidden="true" />
        {exiting ? 'Exiting…' : 'Exit'}
      </button>
    </section>
  );
}
