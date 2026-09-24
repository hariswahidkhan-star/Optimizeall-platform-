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
    /// <summary>Sensitive: create/edit/delete custom roles and assign them to users (grant requires the built-in Admin role).</summary>
    public const string RolesManage = "roles.manage";
    /// <summary>Sensitive: "log in as" another user (time-boxed, audited, high-risk actions blocked). Admin only by default.</summary>
    public const string UsersImpersonate = "users.impersonate";
    public const string ContentManage = "content.manage";
    public const string SettingsManage = "settings.manage";
    public const string SupportManage = "support.manage";
    public const string AuditView = "audit.view";
    public const string JobsView = "jobs.view";

    // Agency website & CMS
    /// <summary>Services, packages, industries, case studies, testimonials, team, pages, navigation, legal.</summary>
    public const string SiteManage = "site.manage";
    public const string BlogWrite = "blog.write";
    public const string BlogPublish = "blog.publish";
    public const string CareersManage = "careers.manage";

    // CRM & sales
    public const string CrmView = "crm.view";
    public const string CrmManage = "crm.manage";
    public const string ProposalsManage = "proposals.manage";
    public const string ContractsManage = "contracts.manage";

    // Client billing
    public const string BillingView = "billing.view";
    public const string BillingManage = "billing.manage";
    /// <summary>Sensitive: tax rates, invoice numbering, payment terms.</summary>
    public const string BillingSettings = "billing.settings";

    // Client delivery
    public const string ClientsView = "clients.view";
    public const string ClientsManage = "clients.manage";
    public const string ProjectsView = "projects.view";
    public const string ProjectsManage = "projects.manage";
    public const string DeliverablesSubmit = "deliverables.submit";
    public const string TimeTrack = "time.track";
    public const string TimeViewAll = "time.view_all";
    public const string ReportsManage = "reports.manage";

    // Marketing execution
    public const string EmailManage = "email.manage";
    /// <summary>Sensitive: sending an email/SMS campaign to an audience.</summary>
    public const string EmailSend = "email.send";
    public const string SmsManage = "sms.manage";
    public const string SocialManage = "social.manage";
    public const string SocialPublish = "social.publish";
    public const string AdsManage = "ads.manage";
    public const string SeoManage = "seo.manage";
    /// <summary>Landing pages and form builder.</summary>
    public const string FormsManage = "forms.manage";
    /// <summary>Sensitive: third-party integration credentials (social, ads, SMS, SEO data providers).</summary>
    public const string IntegrationsManage = "integrations.manage";

    // Client portal
    public const string ClientPortal = "client.portal";

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
            Permissions.BillingView, Permissions.BillingManage, Permissions.BillingSettings, Permissions.ClientsView,
            Permissions.TimeViewAll,
        },
        [Role.Admin] = Permissions.All.Where(p => p != Permissions.ClientPortal && p != Permissions.ParticipantPortal)
            .Append(Permissions.ParticipantPortal).ToArray(),
        [Role.AccountManager] = new[]
        {
            Permissions.CrmView, Permissions.CrmManage, Permissions.ProposalsManage, Permissions.ContractsManage,
            Permissions.ClientsView, Permissions.ClientsManage, Permissions.ProjectsView, Permissions.ProjectsManage,
            Permissions.DeliverablesSubmit, Permissions.ReportsManage, Permissions.TimeTrack, Permissions.TimeViewAll,
            Permissions.BillingView, Permissions.SocialManage, Permissions.EmailManage, Permissions.EmailSend,
            Permissions.SmsManage, Permissions.AnalyticsView, Permissions.CampaignsView, Permissions.UsersView,
        },
        [Role.Strategist] = new[]
        {
            Permissions.CrmView, Permissions.ClientsView, Permissions.ProjectsView, Permissions.ProjectsManage,
            Permissions.DeliverablesSubmit, Permissions.ReportsManage, Permissions.TimeTrack, Permissions.SeoManage,
            Permissions.AdsManage, Permissions.AnalyticsView, Permissions.CampaignsView,
        },
        [Role.ContentCreator] = new[]
        {
            Permissions.ClientsView, Permissions.ProjectsView, Permissions.DeliverablesSubmit, Permissions.TimeTrack,
            Permissions.BlogWrite, Permissions.SocialManage, Permissions.EmailManage,
        },
        [Role.Designer] = new[]
        {
            Permissions.ClientsView, Permissions.ProjectsView, Permissions.DeliverablesSubmit, Permissions.TimeTrack,
            Permissions.FormsManage,
        },
        [Role.SeoSpecialist] = new[]
        {
            Permissions.ClientsView, Permissions.ProjectsView, Permissions.DeliverablesSubmit, Permissions.TimeTrack,
            Permissions.SeoManage, Permissions.BlogWrite, Permissions.ReportsManage,
        },
        [Role.AdsSpecialist] = new[]
        {
            Permissions.ClientsView, Permissions.ProjectsView, Permissions.DeliverablesSubmit, Permissions.TimeTrack,
            Permissions.AdsManage, Permissions.ReportsManage, Permissions.AnalyticsView,
        },
        [Role.SocialMediaManager] = new[]
        {
            Permissions.ClientsView, Permissions.ProjectsView, Permissions.DeliverablesSubmit, Permissions.TimeTrack,
            Permissions.SocialManage, Permissions.SocialPublish, Permissions.ReportsManage,
        },
        [Role.SalesRep] = new[]
        {
            Permissions.CrmView, Permissions.CrmManage, Permissions.ProposalsManage, Permissions.ClientsView,
            Permissions.BillingView,
        },
        [Role.Client] = new[]
        {
            Permissions.ClientPortal,
        },
    };

    public static IReadOnlySet<string> For(IEnumerable<Role> roles) =>
        roles.SelectMany(r => Map.TryGetValue(r, out var p) ? p : Array.Empty<string>()).ToHashSet();

    public static IReadOnlyCollection<string> For(Role role) => Map[role];
}
