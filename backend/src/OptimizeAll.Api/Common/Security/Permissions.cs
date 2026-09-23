using OptimizeAll.Domain.Identity;

namespace OptimizeAll.Api.Common.Security;

/// <summary>
/// Fine-grained permissions. Endpoints authorize against permissions (never roles directly) using
/// <see cref="HasPermissionAttribute"/>; roles are bundles of permissions defined in <see cref="RolePermissions"/>.
/// </summary>
public static class Permissions
{
    // Participant self-service
    public const string ParticipantPortal = "participant.portal";

    // Campaigns
    public const string CampaignsView = "campaigns.view";
    public const string CampaignsManage = "campaigns.manage";
    public const string CampaignsPublish = "campaigns.publish";
    /// <summary>Sensitive: changing reward rates, bonuses and caps (creates a new rule version).</summary>
    public const string RewardsEdit = "rewards.edit";
    /// <summary>Approve/decline bonuses that need separate approval (quality, referral).</summary>
    public const string RewardsApproveBonus = "rewards.approve_bonus";

    // Review
    public const string SubmissionsReview = "submissions.review";
    public const string SubmissionsReverse = "submissions.reverse";
    public const string AppealsResolve = "appeals.resolve";
    public const string ReviewAssign = "review.assign";
    public const string SocialAccountsVerify = "social.verify";

    // Finance
    public const string LedgerView = "ledger.view";
    public const string LedgerAdjust = "ledger.adjust";
    public const string PayoutsView = "payouts.view";
    public const string PayoutsPrepare = "payouts.prepare";
    /// <summary>Sensitive: freeze a batch for payment.</summary>
    public const string PayoutsFinalize = "payouts.finalize";
    public const string PayoutsRecordPayment = "payouts.record_payment";
    public const string PayoutsHold = "payouts.hold";
    /// <summary>Sensitive: payout schedule, minimum threshold, settlement currency, exchange rates.</summary>
    public const string PayoutSettingsEdit = "payouts.settings";

    // Marketing & growth
    public const string MarketingManage = "marketing.manage";
    public const string AnalyticsView = "analytics.view";

    // Administration
    public const string UsersView = "users.view";
    public const string UsersManage = "users.manage";
    public const string UsersSuspend = "users.suspend";
    public const string RolesAssign = "roles.assign";
    public const string ContentManage = "content.manage";
    public const string SettingsManage = "settings.manage";
    public const string SupportManage = "support.manage";
    public const string AuditView = "audit.view";
    public const string JobsView = "jobs.view";

    public static readonly IReadOnlyList<string> All = typeof(Permissions)
        .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
        .Where(f => f.IsLiteral && f.FieldType == typeof(string))
        .Select(f => (string)f.GetRawConstantValue()!)
        .ToArray();
}

public static class RolePermissions
{
    private static readonly Dictionary<Role, string[]> Map = new()
    {
        [Role.Participant] = new[]
        {
            Permissions.ParticipantPortal,
        },
        [Role.Reviewer] = new[]
        {
            Permissions.CampaignsView, Permissions.SubmissionsReview, Permissions.AppealsResolve,
            Permissions.SocialAccountsVerify, Permissions.UsersView, Permissions.SupportManage,
        },
        [Role.CampaignManager] = new[]
        {
            Permissions.CampaignsView, Permissions.CampaignsManage, Permissions.CampaignsPublish, Permissions.RewardsEdit,
            Permissions.RewardsApproveBonus, Permissions.MarketingManage, Permissions.AnalyticsView, Permissions.ReviewAssign,
            Permissions.UsersView,
        },
        [Role.Finance] = new[]
        {
            Permissions.CampaignsView, Permissions.LedgerView, Permissions.LedgerAdjust, Permissions.PayoutsView,
            Permissions.PayoutsPrepare, Permissions.PayoutsFinalize, Permissions.PayoutsRecordPayment, Permissions.PayoutsHold,
            Permissions.PayoutSettingsEdit, Permissions.RewardsApproveBonus, Permissions.SubmissionsReverse,
            Permissions.AnalyticsView, Permissions.UsersView, Permissions.AuditView,
        },
        [Role.Admin] = Permissions.All.ToArray(),
    };

    public static IReadOnlySet<string> For(IEnumerable<Role> roles) =>
        roles.SelectMany(r => Map.TryGetValue(r, out var p) ? p : Array.Empty<string>()).ToHashSet();

    public static IReadOnlyCollection<string> For(Role role) => Map[role];
}
