using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Jobs;
using OptimizeAll.Api.Common.Notifications;
using OptimizeAll.Api.Common.Settings;
using OptimizeAll.Api.Modules.Marketing.Shared;
using OptimizeAll.Domain.Campaigns;
using OptimizeAll.Domain.Eligibility;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Marketing;
using OptimizeAll.Domain.Notifications;
using OptimizeAll.Domain.Settings;
using OptimizeAll.Domain.Social;
using OptimizeAll.Domain.Submissions;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Retention;

/// <summary>Retention message kinds (<see cref="RetentionMessageLog.Kind"/>).</summary>
public static class RetentionKinds
{
    public const string VerifyEmail = "onboarding.verify_email";
    public const string AddSocial = "onboarding.add_social";
    public const string FirstSubmission = "onboarding.first_submission";
    public const string CampaignAlert = "campaign.alert";
    public const string Reactivation = "reactivation";

    public static readonly IReadOnlyList<string> All = new[] { VerifyEmail, AddSocial, FirstSubmission, CampaignAlert, Reactivation };
}

/// <summary>
/// Hourly retention automations: onboarding reminders, new-campaign alerts and reactivation. Each message is
/// deduplicated by a (UserId, Kind, DedupKey) row in <see cref="RetentionMessageLog"/> saved in the same
/// SaveChanges (transaction) as the notification and its outbox rows, so a crash or retry never sends twice.
/// At most <see cref="BatchSize"/> messages per kind per run. No-op when setting "retention.enabled" is false.
/// </summary>
public sealed class RetentionJob(
    AppDbContext db,
    ISettingsService settings,
    INotificationService notifications,
    TimeProvider clock,
    ILogger<RetentionJob> logger) : IJob
{
    public const int BatchSize = 500;
    private const int ScanChunk = 500;
    private const int MaxChunksPerKind = 20;

    /// <summary>Onboarding reminders are only sent to accounts registered within these windows (no backlog blasts).</summary>
    public static readonly TimeSpan OnboardingLookback = TimeSpan.FromDays(30);
    public static readonly TimeSpan FirstSubmissionLookback = TimeSpan.FromDays(60);
    public static readonly TimeSpan CampaignAlertWindow = TimeSpan.FromHours(72);
    public static readonly TimeSpan ReactivationInterval = TimeSpan.FromDays(30);
    private static readonly DateTime WindowEpoch = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public string Name => nameof(RetentionJob);

    private DateTime _now;

    public async Task<string> ExecuteAsync(CancellationToken ct)
    {
        if (!await settings.GetAsync(SettingKeys.RetentionEnabled, true, ct))
            return "disabled";

        _now = clock.GetUtcNow().UtcDateTime;
        var verify = await VerifyEmailRemindersAsync(ct);
        var social = await AddSocialRemindersAsync(ct);
        var first = await FirstSubmissionRemindersAsync(ct);
        var alerts = await CampaignAlertsAsync(ct);
        var reactivation = await ReactivationAsync(ct);
        return $"{RetentionKinds.VerifyEmail}={verify} {RetentionKinds.AddSocial}={social} {RetentionKinds.FirstSubmission}={first} " +
               $"{RetentionKinds.CampaignAlert}={alerts} {RetentionKinds.Reactivation}={reactivation}";
    }

    /// <summary>Stable 30-day window key used to message inactive participants at most once per window.</summary>
    public static string ReactivationKey(DateTime nowUtc)
    {
        var index = (int)Math.Floor((nowUtc - WindowEpoch).TotalDays / ReactivationInterval.TotalDays);
        return WindowEpoch.AddDays(index * ReactivationInterval.TotalDays).ToString("yyyy-MM-dd");
    }

    private IQueryable<User> Participants() =>
        db.Set<User>().AsNoTracking()
            .Where(u => u.Status == UserStatus.Active && u.Roles.Any(r => r.Role == Role.Participant));

    private IQueryable<User> NotLogged(IQueryable<User> users, string kind, string key) =>
        users.Where(u => !db.Set<RetentionMessageLog>().Any(l => l.UserId == u.Id && l.Kind == kind && l.DedupKey == key));

    private async Task<int> VerifyEmailRemindersAsync(CancellationToken ct)
    {
        var dueBefore = _now.AddHours(-24);
        var notBefore = _now - OnboardingLookback;
        var users = await NotLogged(Participants(), RetentionKinds.VerifyEmail, "d1")
            .Where(u => u.EmailVerifiedAt == null && u.CreatedAt <= dueBefore && u.CreatedAt >= notBefore)
            .OrderBy(u => u.CreatedAt).Select(u => u.Id).Take(BatchSize).ToListAsync(ct);

        var sent = 0;
        foreach (var userId in users)
        {
            if (await SendAsync(userId, RetentionKinds.VerifyEmail, "d1", new NotificationRequest(
                    userId, NotificationTypes.OnboardingReminder, "Confirm your email to get started",
                    "Welcome to Optimize All! Your email address isn't confirmed yet. Sign in and choose \"Resend verification\" " +
                    "to receive a new confirmation link, then you can join paid campaigns.",
                    "/login", Channels), ct))
                sent++;
        }
        return sent;
    }

    private async Task<int> AddSocialRemindersAsync(CancellationToken ct)
    {
        var dueBefore = _now.AddDays(-3);
        var notBefore = _now - OnboardingLookback;
        var users = await NotLogged(Participants(), RetentionKinds.AddSocial, "d3")
            .Where(u => u.EmailVerifiedAt != null && u.CreatedAt <= dueBefore && u.CreatedAt >= notBefore &&
                        !db.Set<SocialAccount>().Any(s => s.UserId == u.Id))
            .OrderBy(u => u.CreatedAt).Select(u => u.Id).Take(BatchSize).ToListAsync(ct);

        var sent = 0;
        foreach (var userId in users)
        {
            if (await SendAsync(userId, RetentionKinds.AddSocial, "d3", new NotificationRequest(
                    userId, NotificationTypes.OnboardingReminder, "Add the social profile you'll share from",
                    "Add your established social media profile to see which paid campaigns you qualify for.",
                    "/profile/social-accounts", Channels), ct))
                sent++;
        }
        return sent;
    }

    private async Task<int> FirstSubmissionRemindersAsync(CancellationToken ct)
    {
        var dueBefore = _now.AddDays(-7);
        var notBefore = _now - FirstSubmissionLookback;
        var criteria = EligibilityCriteria.Global(await settings.MinAccountAgeDaysAsync(ct), await settings.MinFollowersAsync(ct));
        var baseQuery = NotLogged(Participants(), RetentionKinds.FirstSubmission, "d7")
            .Where(u => u.EmailVerifiedAt != null && u.CreatedAt <= dueBefore && u.CreatedAt >= notBefore &&
                        db.Set<SocialAccount>().Any(s => s.UserId == u.Id && s.IsActive) &&
                        !db.Set<Submission>().Any(s => s.UserId == u.Id));

        var sent = 0;
        await foreach (var (user, accounts) in ScanWithAccountsAsync(baseQuery, ct))
        {
            if (!EligibilityEvaluator.Evaluate(criteria, ParticipantProfile.From(user), accounts, _now).IsEligible) continue;
            if (await SendAsync(user.Id, RetentionKinds.FirstSubmission, "d7", new NotificationRequest(
                    user.Id, NotificationTypes.OnboardingReminder, "Ready for your first paid post?",
                    "Your profile qualifies for campaigns. Pick a campaign, share the approved content and submit your post link to get paid.",
                    "/campaigns", Channels), ct))
                sent++;
            if (sent >= BatchSize) break;
        }
        return sent;
    }

    private async Task<int> CampaignAlertsAsync(CancellationToken ct)
    {
        var publishedAfter = _now - CampaignAlertWindow;
        var campaigns = await db.Set<Campaign>().AsNoTracking().Include(c => c.Platforms)
            .Where(c => c.Status == CampaignStatus.Active && c.Visibility == CampaignVisibility.Public &&
                        c.PublishedAt != null && c.PublishedAt >= publishedAfter && c.PublishedAt <= _now &&
                        c.SubmissionDeadline >= _now)
            .OrderBy(c => c.PublishedAt).ToListAsync(ct);
        if (campaigns.Count == 0) return 0;

        var minAge = await settings.MinAccountAgeDaysAsync(ct);
        var minFollowers = await settings.MinFollowersAsync(ct);
        var alertedSince = _now.AddHours(-24);
        var sent = 0;

        foreach (var campaign in campaigns)
        {
            var criteria = EligibilityCriteria.ForCampaign(campaign, minAge, minFollowers);
            var topics = campaign.Topics.Concat(campaign.Eligibility.Interests).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var platforms = campaign.Platforms.Select(p => p.Platform).ToHashSet();
            var key = campaign.Id.ToString();

            // Max one campaign alert per participant per rolling 24 hours (earlier alerts in this run are already saved).
            var baseQuery = NotLogged(Participants(), RetentionKinds.CampaignAlert, key)
                .Where(u => u.EmailVerifiedAt != null &&
                            db.Set<SocialAccount>().Any(s => s.UserId == u.Id && s.IsActive) &&
                            !db.Set<RetentionMessageLog>().Any(l =>
                                l.UserId == u.Id && l.Kind == RetentionKinds.CampaignAlert && l.SentAt >= alertedSince));

            await foreach (var (user, accounts) in ScanWithAccountsAsync(baseQuery, ct))
            {
                var result = EligibilityEvaluator.Evaluate(criteria, ParticipantProfile.From(user), accounts, _now);
                if (!result.IsEligible) continue;
                var interestMatch = user.Interests.Any(topics.Contains);
                var platformMatch = platforms.Count > 0 && result.EligibleAccounts.Any(a => platforms.Contains(a.Platform));
                if (!interestMatch && !platformMatch) continue;

                if (await SendAsync(user.Id, RetentionKinds.CampaignAlert, key, new NotificationRequest(
                        user.Id, NotificationTypes.CampaignAlert, $"New campaign: {campaign.Title}",
                        $"{campaign.Summary} You're eligible to take part.", $"/campaigns/{campaign.Slug}", Channels), ct))
                    sent++;
                if (sent >= BatchSize) return sent;
            }
        }
        return sent;
    }

    private async Task<int> ReactivationAsync(CancellationToken ct)
    {
        var inactivityDays = Math.Max(1, await settings.GetAsync(SettingKeys.InactivityDays, 30, ct));
        var inactiveBefore = _now.AddDays(-inactivityDays);
        var campaigns = await db.Set<Campaign>().AsNoTracking().Include(c => c.Platforms)
            .Where(c => c.Status == CampaignStatus.Active && c.Visibility == CampaignVisibility.Public &&
                        c.StartsAt <= _now && c.SubmissionDeadline >= _now)
            .OrderByDescending(c => c.PublishedAt).Take(200).ToListAsync(ct);
        if (campaigns.Count == 0) return 0;

        var minAge = await settings.MinAccountAgeDaysAsync(ct);
        var minFollowers = await settings.MinFollowersAsync(ct);
        var criteria = campaigns.Select(c => EligibilityCriteria.ForCampaign(c, minAge, minFollowers)).ToList();
        var key = ReactivationKey(_now);
        var lastSentAfter = _now - ReactivationInterval;

        var baseQuery = NotLogged(Participants(), RetentionKinds.Reactivation, key)
            .Where(u => u.EmailVerifiedAt != null && (u.LastActiveAt ?? u.CreatedAt) < inactiveBefore &&
                        db.Set<SocialAccount>().Any(s => s.UserId == u.Id && s.IsActive) &&
                        !db.Set<RetentionMessageLog>().Any(l =>
                            l.UserId == u.Id && l.Kind == RetentionKinds.Reactivation && l.SentAt >= lastSentAfter));

        var sent = 0;
        await foreach (var (user, accounts) in ScanWithAccountsAsync(baseQuery, ct))
        {
            var profile = ParticipantProfile.From(user);
            var eligible = criteria.Count(c => EligibilityEvaluator.Evaluate(c, profile, accounts, _now).IsEligible);
            if (eligible == 0) continue;
            if (await SendAsync(user.Id, RetentionKinds.Reactivation, key, new NotificationRequest(
                    user.Id, NotificationTypes.Reactivation, "New campaigns are waiting for you",
                    eligible == 1
                        ? "There is an active campaign you're eligible for. Share approved content and get paid for approved posts."
                        : $"There are {eligible} active campaigns you're eligible for. Share approved content and get paid for approved posts.",
                    "/campaigns", Channels), ct))
                sent++;
            if (sent >= BatchSize) break;
        }
        return sent;
    }

    private static readonly NotificationChannel[] Channels = { NotificationChannel.InApp, NotificationChannel.Email };

    /// <summary>Keyset-paginates candidate users (by id) and loads their active social accounts per chunk.</summary>
    private async IAsyncEnumerable<(User User, IReadOnlyCollection<SocialAccount> Accounts)> ScanWithAccountsAsync(
        IQueryable<User> candidates, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        Guid? after = null;
        for (var chunk = 0; chunk < MaxChunksPerKind; chunk++)
        {
            var q = candidates;
            if (after is { } a) q = q.Where(u => u.Id.CompareTo(a) > 0);
            var users = await q.OrderBy(u => u.Id).Take(ScanChunk).ToListAsync(ct);
            if (users.Count == 0) yield break;
            after = users[^1].Id;

            var ids = users.Select(u => u.Id).ToList();
            var accounts = (await db.Set<SocialAccount>().AsNoTracking()
                    .Where(s => ids.Contains(s.UserId) && s.IsActive).ToListAsync(ct))
                .ToLookup(s => s.UserId);
            foreach (var user in users)
                yield return (user, accounts[user.Id].ToList());
            if (users.Count < ScanChunk) yield break;
        }
    }

    /// <summary>Saves the dedup row, the in-app notification and its outbox rows atomically. False if already sent.</summary>
    private async Task<bool> SendAsync(Guid userId, string kind, string key, NotificationRequest request, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        db.Set<RetentionMessageLog>().Add(new RetentionMessageLog { UserId = userId, Kind = kind, DedupKey = key, SentAt = _now });
        await notifications.StageAsync(request, ct);
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            logger.LogDebug("Retention message {Kind}/{Key} for {UserId} already sent", kind, key, userId);
            db.ChangeTracker.Clear();
            return false;
        }
    }
}
