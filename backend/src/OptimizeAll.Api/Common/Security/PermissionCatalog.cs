namespace OptimizeAll.Api.Common.Security;

/// <summary>Human-readable metadata for one permission (the admin "Roles &amp; permissions" editor).</summary>
public sealed record PermissionInfo(string Key, string Area, string Label, string Description, bool Sensitive = false);

/// <summary>
/// Groups every permission in <see cref="Permissions"/> by area with a label and a description. Every constant must have
/// an entry (asserted by a unit test). Entries for permissions that do not exist (yet) are ignored by <see cref="Areas"/>.
/// </summary>
public static class PermissionCatalog
{
    /// <summary>Permissions only a user holding the built-in Admin role may put into a custom role or assign through one.</summary>
    public static readonly IReadOnlySet<string> AdminOnlyGrants = new HashSet<string>(StringComparer.Ordinal)
    {
        Permissions.RolesManage,
        Permissions.SettingsManage,
        // Impersonation (users.impersonate) is introduced separately; listed here so it is admin-only from day one.
        "users.impersonate",
    };

    /// <summary>Portal markers that are not staff permissions.</summary>
    public static readonly IReadOnlySet<string> NonStaff = new HashSet<string>(StringComparer.Ordinal)
    {
        Permissions.ClientPortal,
        Permissions.ParticipantPortal,
    };

    public static bool IsStaffPermission(string permission) => !NonStaff.Contains(permission);

    private const string Participants = "Participant portal";
    private const string Campaigns = "Campaigns & rewards";
    private const string Review = "Review";
    private const string Finance = "Finance & payouts";
    private const string Growth = "Marketing & analytics";
    private const string Admin = "Administration";
    private const string Website = "Website & CMS";
    private const string Sales = "CRM & sales";
    private const string Billing = "Client billing";
    private const string Delivery = "Client delivery";
    private const string Execution = "Marketing execution";
    private const string Client = "Client portal";

    /// <summary>Display order of the areas.</summary>
    public static readonly IReadOnlyList<string> AreaOrder = new[]
    {
        Admin, Sales, Delivery, Execution, Website, Billing, Campaigns, Review, Finance, Growth, Participants, Client,
    };

    private static readonly PermissionInfo[] Entries =
    {
        new(Permissions.ParticipantPortal, Participants, "Participant portal", "Use the participant app: join campaigns, submit posts and receive payouts."),

        new(Permissions.CampaignsView, Campaigns, "View campaigns", "Read campaigns, their content and reward rules."),
        new(Permissions.CampaignsManage, Campaigns, "Manage campaigns", "Create and edit campaigns, assets, invitations and categories."),
        new(Permissions.CampaignsPublish, Campaigns, "Publish campaigns", "Publish, pause and close campaigns."),
        new(Permissions.RewardsEdit, Campaigns, "Edit reward rules", "Change reward rates, bonuses and caps (creates a new rule version).", Sensitive: true),
        new(Permissions.RewardsApproveBonus, Campaigns, "Approve bonuses", "Approve or decline quality and referral bonuses."),

        new(Permissions.SubmissionsReview, Review, "Review submissions", "Claim and decide participant submissions."),
        new(Permissions.SubmissionsReverse, Review, "Reverse submissions", "Reverse approved submissions and their earnings."),
        new(Permissions.AppealsResolve, Review, "Resolve appeals", "Decide participant appeals."),
        new(Permissions.ReviewAssign, Review, "Assign reviews", "Assign submissions to reviewers."),
        new(Permissions.SocialAccountsVerify, Review, "Verify social accounts", "Verify or reject participants' social accounts."),

        new(Permissions.LedgerView, Finance, "View ledger", "Read earnings and balances."),
        new(Permissions.LedgerAdjust, Finance, "Adjust ledger", "Record manual earning adjustments."),
        new(Permissions.PayoutsView, Finance, "View payouts", "Read payout batches and payments."),
        new(Permissions.PayoutsPrepare, Finance, "Prepare payouts", "Create and prepare payout batches."),
        new(Permissions.PayoutsFinalize, Finance, "Finalize payouts", "Freeze a payout batch for payment.", Sensitive: true),
        new(Permissions.PayoutsRecordPayment, Finance, "Record payments", "Mark payout items as paid or failed."),
        new(Permissions.PayoutsHold, Finance, "Payout holds", "Place and release holds on participant payouts."),
        new(Permissions.PayoutSettingsEdit, Finance, "Payout settings", "Payout schedule, minimum threshold, settlement currency and exchange rates.", Sensitive: true),

        new(Permissions.MarketingManage, Growth, "Growth marketing", "Referral programs, banners, landing campaigns and tracking links."),
        new(Permissions.AnalyticsView, Growth, "View analytics", "Platform and marketing analytics dashboards."),

        new(Permissions.UsersView, Admin, "View users", "Read the user directory (profile, roles, status, activity)."),
        new(Permissions.UsersManage, Admin, "Manage users", "Edit user attributes such as participant tier."),
        new(Permissions.UsersSuspend, Admin, "Suspend users", "Suspend and reactivate accounts.", Sensitive: true),
        new(Permissions.RolesAssign, Admin, "Assign built-in roles", "Change users' built-in roles and invite staff.", Sensitive: true),
        new(Permissions.RolesManage, Admin, "Manage custom roles", "Create, edit and delete custom roles and assign them to users. Only admins can grant this.", Sensitive: true),
        new("users.impersonate", Admin, "Impersonate users", "Sign in as another user for support. Only admins can grant this.", Sensitive: true),
        new(Permissions.ContentManage, Admin, "Manage content", "Banners, announcements, FAQs and onboarding copy."),
        new(Permissions.SettingsManage, Admin, "Platform settings", "Edit platform settings and policies. Only admins can grant this.", Sensitive: true),
        new(Permissions.SupportManage, Admin, "Support tickets", "Handle support tickets; can be assigned tickets."),
        new(Permissions.AuditView, Admin, "View audit log", "Read the append-only audit log."),
        new(Permissions.JobsView, Admin, "Jobs & notifications", "Background jobs and the notification outbox."),

        new(Permissions.SiteManage, Website, "Manage website", "Services, packages, case studies, team, pages, navigation and legal pages."),
        new(Permissions.BlogWrite, Website, "Write blog posts", "Draft and edit blog posts."),
        new(Permissions.BlogPublish, Website, "Publish blog posts", "Publish and unpublish blog posts."),
        new(Permissions.CareersManage, Website, "Manage careers", "Job openings and applications."),

        new(Permissions.CrmView, Sales, "View CRM", "Contacts, companies, deals and tasks; can own and be assigned CRM records."),
        new(Permissions.CrmManage, Sales, "Manage CRM", "Create and edit contacts, companies and deals; receives inbound leads."),
        new(Permissions.ProposalsManage, Sales, "Manage proposals", "Build, send and track proposals."),
        new(Permissions.ContractsManage, Sales, "Manage contracts", "Draft, send and track contracts."),

        new(Permissions.BillingView, Billing, "View billing", "Invoices, payments and subscriptions."),
        new(Permissions.BillingManage, Billing, "Manage billing", "Create and send invoices, record payments."),
        new(Permissions.BillingSettings, Billing, "Billing settings", "Tax rates, invoice numbering and payment terms.", Sensitive: true),

        new(Permissions.ClientsView, Delivery, "View clients", "Every client account and its data (agency-wide access)."),
        new(Permissions.ClientsManage, Delivery, "Manage clients", "Create and edit client accounts, teams and onboarding."),
        new(Permissions.ProjectsView, Delivery, "View projects", "Projects, tasks and the delivery dashboard."),
        new(Permissions.ProjectsManage, Delivery, "Manage projects", "Create and edit projects; review deliverables."),
        new(Permissions.DeliverablesSubmit, Delivery, "Submit deliverables", "Upload deliverables for review."),
        new(Permissions.TimeTrack, Delivery, "Track time", "Log your own time."),
        new(Permissions.TimeViewAll, Delivery, "View all time", "Read everyone's timesheets and utilization."),
        new(Permissions.ReportsManage, Delivery, "Manage reports", "Build and send client reports."),

        new(Permissions.EmailManage, Execution, "Manage email & lists", "Audiences, templates and email campaigns."),
        new(Permissions.EmailSend, Execution, "Send email/SMS campaigns", "Send a campaign to an audience.", Sensitive: true),
        new(Permissions.SmsManage, Execution, "Manage SMS", "SMS and WhatsApp campaigns."),
        new(Permissions.SocialManage, Execution, "Manage social", "Social calendars, posts and the engagement inbox."),
        new(Permissions.SocialPublish, Execution, "Publish social posts", "Approve and schedule social posts."),
        new(Permissions.AdsManage, Execution, "Manage ads", "Ad accounts, campaigns and budgets."),
        new(Permissions.SeoManage, Execution, "Manage SEO", "Keywords, audits and SEO reports."),
        new(Permissions.FormsManage, Execution, "Landing pages & forms", "Landing pages and the form builder; receives form notifications."),
        new(Permissions.IntegrationsManage, Execution, "Manage integrations", "Third-party credentials (social, ads, SMS, SEO providers).", Sensitive: true),

        new(Permissions.ClientPortal, Client, "Client portal", "Client users only: their organization's projects, approvals, reports and invoices. Cannot be combined with staff permissions."),
    };

    private static readonly Dictionary<string, PermissionInfo> ByKey = Entries.ToDictionary(e => e.Key, StringComparer.Ordinal);

    public static PermissionInfo? Find(string permission) => ByKey.GetValueOrDefault(permission);

    /// <summary>Catalog entries of the permissions that exist, grouped by area in display order.</summary>
    public static IReadOnlyList<(string Area, IReadOnlyList<PermissionInfo> Permissions)> Areas()
    {
        var existing = Permissions.All.ToHashSet(StringComparer.Ordinal);
        return Entries.Where(e => existing.Contains(e.Key))
            .GroupBy(e => e.Area)
            .OrderBy(g => AreaIndex(g.Key))
            .Select(g => (g.Key, (IReadOnlyList<PermissionInfo>)g.ToList()))
            .ToList();
    }

    private static int AreaIndex(string area)
    {
        for (var i = 0; i < AreaOrder.Count; i++)
            if (AreaOrder[i] == area) return i;
        return int.MaxValue;
    }
}
