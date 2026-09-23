/**
 * Permission strings — mirror of backend `Common/Security/Permissions.cs`.
 * The UI gates by permission (never by role), exactly like the API.
 */
export const Permissions = {
  ParticipantPortal: 'participant.portal',

  CampaignsView: 'campaigns.view',
  CampaignsManage: 'campaigns.manage',
  CampaignsPublish: 'campaigns.publish',
  RewardsEdit: 'rewards.edit',
  RewardsApproveBonus: 'rewards.approve_bonus',

  SubmissionsReview: 'submissions.review',
  SubmissionsReverse: 'submissions.reverse',
  AppealsResolve: 'appeals.resolve',
  ReviewAssign: 'review.assign',
  SocialAccountsVerify: 'social.verify',

  LedgerView: 'ledger.view',
  LedgerAdjust: 'ledger.adjust',
  PayoutsView: 'payouts.view',
  PayoutsPrepare: 'payouts.prepare',
  PayoutsFinalize: 'payouts.finalize',
  PayoutsRecordPayment: 'payouts.record_payment',
  PayoutsHold: 'payouts.hold',
  PayoutSettingsEdit: 'payouts.settings',

  MarketingManage: 'marketing.manage',
  AnalyticsView: 'analytics.view',

  UsersView: 'users.view',
  UsersManage: 'users.manage',
  UsersSuspend: 'users.suspend',
  RolesAssign: 'roles.assign',
  ContentManage: 'content.manage',
  SettingsManage: 'settings.manage',
  SupportManage: 'support.manage',
  AuditView: 'audit.view',
  JobsView: 'jobs.view',
} as const;

export type Permission = (typeof Permissions)[keyof typeof Permissions];

/** A permission requirement: every entry of `allOf` and at least one entry of `anyOf` (when given). */
export interface PermissionRequirement {
  anyOf?: readonly string[];
  allOf?: readonly string[];
}

export function hasPermission(granted: readonly string[] | undefined, permission: string): boolean {
  return !!granted?.includes(permission);
}

export function hasAnyPermission(
  granted: readonly string[] | undefined,
  required: readonly string[],
): boolean {
  return required.some((p) => hasPermission(granted, p));
}

export function hasAllPermissions(
  granted: readonly string[] | undefined,
  required: readonly string[],
): boolean {
  return required.every((p) => hasPermission(granted, p));
}

export function meetsRequirement(
  granted: readonly string[] | undefined,
  req: PermissionRequirement,
): boolean {
  if (req.allOf && !hasAllPermissions(granted, req.allOf)) return false;
  if (req.anyOf && req.anyOf.length > 0 && !hasAnyPermission(granted, req.anyOf)) return false;
  return true;
}
