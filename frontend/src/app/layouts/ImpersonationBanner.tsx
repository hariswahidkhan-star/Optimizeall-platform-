import { Eye, LogOut } from 'lucide-react';
import { type RefObject, useEffect, useId, useRef, useState } from 'react';
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

/** CSS variable with the banner's current height, so sticky headers below it stay visible (see PortalLayout.css). */
export const BANNER_OFFSET_VAR = '--impersonation-banner-height';

/**
 * Publishes the banner's height as {@link BANNER_OFFSET_VAR} on the document root while it is shown (it wraps on narrow
 * screens), and removes it when the banner goes away.
 */
function useBannerOffset(ref: RefObject<HTMLElement | null>, active: boolean) {
  useEffect(() => {
    const el = ref.current;
    const root = document.documentElement;
    if (!active || !el) return;
    const update = () => root.style.setProperty(BANNER_OFFSET_VAR, `${el.getBoundingClientRect().height}px`);
    update();
    const observer = typeof ResizeObserver === 'undefined' ? null : new ResizeObserver(update);
    observer?.observe(el);
    return () => {
      observer?.disconnect();
      root.style.removeProperty(BANNER_OFFSET_VAR);
    };
  }, [ref, active]);
}

/**
 * Persistent, high-contrast notice shown at the top of every layout while a staff member is viewing as another user
 * ("log in as"), with the only way back: Exit. It sticks to the top of the viewport (it never scrolls away; sticky
 * headers are pushed below it), is a labelled landmark region, and is announced politely when it appears. Renders
 * nothing otherwise.
 */
export function ImpersonationBanner() {
  const auth = useOptionalAuth();
  const toast = useToast();
  const [exiting, setExiting] = useState(false);
  const ref = useRef<HTMLElement>(null);
  const textId = useId();
  const user = auth?.user;
  const impersonation = auth?.impersonation;
  const active = !!(auth && user && impersonation);
  useBannerOffset(ref, active);
  if (!auth || !user || !impersonation) return null;

  // A built-in staff role first, then a custom role, then the built-in role (Participant/Client).
  const primaryBuiltIn = user.roles.find((r) => r !== 'Participant');
  const roleLabel = primaryBuiltIn
    ? roleName(primaryBuiltIn)
    : user.customRoles?.[0] ?? roleName(user.roles[0]);

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
    <section ref={ref} className="impersonation-banner" aria-label="Impersonation" data-testid="impersonation-banner">
      <Eye aria-hidden="true" className="impersonation-banner__icon" />
      <p className="impersonation-banner__text" id={textId} role="status" aria-live="polite">
        You are viewing as <strong>{user.displayName}</strong> ({roleLabel})
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
        aria-describedby={textId}
        aria-busy={exiting || undefined}
      >
        <LogOut aria-hidden="true" />
        {exiting ? 'Exiting…' : 'Exit'}
      </button>
    </section>
  );
}
