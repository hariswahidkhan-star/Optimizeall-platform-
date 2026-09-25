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
  RatesView: 'rates.view',
  RatesManage: 'rates.manage',
  RatesAssign: 'rates.assign',
  CodesView: 'codes.view',
  CodesManage: 'codes.manage',
  CodesAssign: 'codes.assign',
  SalesReview: 'sales.review',
  SalesReverse: 'sales.reverse',

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
  RolesManage: 'roles.manage',
  UsersImpersonate: 'users.impersonate',
  ContentManage: 'content.manage',
  SettingsManage: 'settings.manage',
  SupportManage: 'support.manage',
  AuditView: 'audit.view',
  JobsView: 'jobs.view',

  SiteManage: 'site.manage',
  BlogWrite: 'blog.write',
  BlogPublish: 'blog.publish',
  CareersManage: 'careers.manage',

  CrmView: 'crm.view',
  CrmManage: 'crm.manage',
  ProposalsManage: 'proposals.manage',
  ContractsManage: 'contracts.manage',

  BillingView: 'billing.view',
  BillingManage: 'billing.manage',
  BillingSettings: 'billing.settings',

  ClientsView: 'clients.view',
  ClientsManage: 'clients.manage',
  ProjectsView: 'projects.view',
  ProjectsManage: 'projects.manage',
  DeliverablesSubmit: 'deliverables.submit',
  TimeTrack: 'time.track',
  TimeViewAll: 'time.view_all',
  ReportsManage: 'reports.manage',

  EmailManage: 'email.manage',
  EmailSend: 'email.send',
  SmsManage: 'sms.manage',
  SocialManage: 'social.manage',
  SocialPublish: 'social.publish',
  AdsManage: 'ads.manage',
  SeoManage: 'seo.manage',
  FormsManage: 'forms.manage',
  IntegrationsManage: 'integrations.manage',

  ClientPortal: 'client.portal',
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
