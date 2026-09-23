import { Permissions } from '@/lib/auth/permissions';
import { useAuth } from '@/lib/auth/useAuth';

/** Finance permission flags for the signed-in user (UI hints only — the server enforces every rule). */
export function useCan() {
  const { hasPermission, user } = useAuth();
  return {
    userId: user?.id ?? null,
    viewLedger: hasPermission(Permissions.LedgerView),
    adjust: hasPermission(Permissions.LedgerAdjust),
    viewPayouts: hasPermission(Permissions.PayoutsView),
    prepare: hasPermission(Permissions.PayoutsPrepare),
    finalize: hasPermission(Permissions.PayoutsFinalize),
    recordPayment: hasPermission(Permissions.PayoutsRecordPayment),
    hold: hasPermission(Permissions.PayoutsHold),
    settings: hasPermission(Permissions.PayoutSettingsEdit),
    approveBonus: hasPermission(Permissions.RewardsApproveBonus),
    viewUsers: hasPermission(Permissions.UsersView),
  };
}
