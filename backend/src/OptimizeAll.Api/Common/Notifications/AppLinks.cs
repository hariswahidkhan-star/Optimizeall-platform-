namespace OptimizeAll.Api.Common.Notifications;

/// <summary>
/// The single source of truth for web-app paths the backend puts into notifications, emails, onboarding steps and
/// seed data. Every path must match a route in the frontend router (<c>frontend/src/features/*/routes.tsx</c> and
/// <c>frontend/src/app/router.tsx</c>). The route patterns are listed in <c>frontend/src/app/appLinks.fixture.json</c>;
/// a backend unit test keeps this class and the fixture in sync, and a frontend test checks each pattern resolves to
/// a real route. App-relative paths are turned into absolute links for email by <c>EmailChannelSender.BuildLink</c>.
/// </summary>
public static class AppLinks
{
    // ---------- Public ----------
    public const string Login = "/login";

    /// <summary>Registration; the referral link adds <c>?ref={code}</c>.</summary>
    public const string Register = "/register";
    public const string ForgotPassword = "/forgot-password";

    /// <summary>Email verification page; the emailed link adds <c>?token=…</c>.</summary>
    public const string VerifyEmail = "/verify-email";

    /// <summary>Password reset page; the emailed link adds <c>?token=…</c>.</summary>
    public const string ResetPassword = "/reset-password";

    /// <summary>"Sign in with Google" redirect URI path (registered in Google Cloud as <c>https://&lt;host&gt;/auth/google/callback</c>).</summary>
    public const string GoogleCallback = "/auth/google/callback";

    /// <summary>Invitation landing page (<c>/join/:code</c>).</summary>
    public static string Invitation(string code) => $"/join/{Uri.EscapeDataString(code)}";

    /// <summary>Public campaign landing page (<c>/c/:slug</c>).</summary>
    public static string PublicCampaign(string slug) => $"/c/{Uri.EscapeDataString(slug)}";

    // ---------- Participant portal (/app) ----------
    public const string ParticipantHome = "/app";
    public const string Campaigns = "/app/campaigns";
    public static string Campaign(string slug) => $"/app/campaigns/{Uri.EscapeDataString(slug)}";
    public static string Submission(Guid id) => $"/app/submissions/{id}";
    public const string Earnings = "/app/earnings";
    public const string Payouts = "/app/payouts";
    public static string Payout(Guid payoutItemId) => $"/app/payouts/{payoutItemId}";
    public const string Profile = "/app/profile";
    public const string PayoutDetails = "/app/profile/payout-details";
    public const string NotificationPreferences = "/app/profile/notification-preferences";
    public const string SocialAccounts = "/app/social-accounts";
    public const string Achievements = "/app/achievements";
    public const string Referrals = "/app/referrals";
    public static string SupportTicket(Guid ticketId) => $"/app/support/{ticketId}";

    // ---------- Reviewer portal (/review) ----------
    public static string ReviewSubmission(Guid submissionId) => $"/review/queue/{submissionId}";
    public const string ReviewLiveChecks = "/review/live-checks";

    // ---------- Finance portal (/finance) ----------
    public static string FinanceBatch(Guid batchId) => $"/finance/batches/{batchId}";
}
