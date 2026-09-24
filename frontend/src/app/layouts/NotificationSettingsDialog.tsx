import { Dialog } from '@/components/ui/Dialog';
import { useNotificationPreferences } from '@/features/participant/api/queries';
import { QueryState } from '@/features/participant/components/QueryState';
import { PreferencesMatrix } from '@/features/participant/profile/NotificationPreferencesPage';

/**
 * Notification settings for users without the participant profile (staff and client users), opened from the account
 * menu. Lists only the kinds the user can receive (the server filters by permission).
 */
export function NotificationSettingsDialog({ open, onClose }: { open: boolean; onClose: () => void }) {
  return (
    <Dialog
      open={open}
      onClose={onClose}
      size="lg"
      title="Notification settings"
      description="Choose how each kind of notification reaches you."
    >
      {open && <NotificationSettingsBody />}
    </Dialog>
  );
}

function NotificationSettingsBody() {
  const query = useNotificationPreferences();
  return (
    <QueryState query={query} errorTitle="Your preferences couldn’t be loaded" headingLevel={3}>
      {(prefs) => <PreferencesMatrix prefs={prefs} profileLink={null} />}
    </QueryState>
  );
}
