using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Settings;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Content;
using OptimizeAll.Domain.Eligibility;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Settings;
using OptimizeAll.Domain.Social;
using OptimizeAll.Domain.Submissions;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Accounts;

/// <summary>Everything the server needs to evaluate homepage state, onboarding and content audiences for one user.</summary>
public sealed class ParticipantSnapshot
{
    public required User User { get; init; }
    public required DateTime Now { get; init; }
    public required int MinAccountAgeDays { get; init; }
    public required int MinFollowers { get; init; }
    public required int InactivityDays { get; init; }

    /// <summary>Global qualification of the user's active social accounts.</summary>
    public required IReadOnlyList<SocialAccountEligibility> ActiveAccounts { get; init; }
    public required IReadOnlyDictionary<SubmissionStatus, int> SubmissionCounts { get; init; }
    public required bool HasPayoutProfile { get; init; }

    public bool EmailVerified => User.IsEmailVerified;
    public bool HasSocialAccount => ActiveAccounts.Count > 0;
    public int EligibleAccountCount => ActiveAccounts.Count(a => a.IsEligible);
    public bool HasEligibleAccount => EligibleAccountCount > 0;

    /// <summary>Soonest date a not-yet-qualifying account becomes old enough (only when age is its sole blocker).</summary>
    public DateTime? SoonestEligibleFrom => ActiveAccounts.Where(a => !a.IsEligible && a.EligibleFrom.HasValue)
        .Select(a => a.EligibleFrom).Min();

    public int SubmissionTotal => SubmissionCounts.Values.Sum();
    public int Count(SubmissionStatus status) => SubmissionCounts.TryGetValue(status, out var c) ? c : 0;
    public bool HasApprovedSubmission => Count(SubmissionStatus.Approved) > 0;

    public bool ProfileCompleted =>
        !string.IsNullOrWhiteSpace(User.CountryCode) && !string.IsNullOrWhiteSpace(User.TimeZone) && User.Interests.Count > 0;

    public bool IsInactive => (User.LastActiveAt ?? User.CreatedAt) < Now.AddDays(-InactivityDays);

    public bool MatchesAudience(ContentAudience audience) => audience switch
    {
        ContentAudience.Everyone => true,
        ContentAudience.Onboarding => !EmailVerified || !HasEligibleAccount,
        ContentAudience.Eligible => HasEligibleAccount,
        ContentAudience.ActiveEarners => HasApprovedSubmission,
        ContentAudience.Inactive => IsInactive,
        _ => false,
    };

    public bool IsVisible(HomepageBanner b) =>
        b.IsActive &&
        (b.StartsAt is null || b.StartsAt <= Now) &&
        (b.EndsAt is null || b.EndsAt > Now) &&
        MatchesAudience(b.Audience) &&
        (string.IsNullOrEmpty(b.CountryCode) || string.Equals(b.CountryCode, User.CountryCode, StringComparison.OrdinalIgnoreCase)) &&
        (string.IsNullOrEmpty(b.LanguageCode) || string.Equals(PrimaryLanguage(b.LanguageCode), PrimaryLanguage(User.LanguageCode), StringComparison.OrdinalIgnoreCase));

    public bool IsVisible(Announcement a) =>
        a.IsActive && a.PublishAt <= Now && (a.ExpiresAt is null || a.ExpiresAt > Now) && MatchesAudience(a.Audience);

    private static string PrimaryLanguage(string code) => code.Split('-')[0];
}

public interface IParticipantStateService
{
    Task<ParticipantSnapshot> LoadAsync(Guid userId, CancellationToken ct);
}

public sealed class ParticipantStateService(AppDbContext db, ISettingsService settings, TimeProvider clock) : IParticipantStateService
{
    public async Task<ParticipantSnapshot> LoadAsync(Guid userId, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var user = await db.Set<User>().AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct)
                   ?? throw DomainException.NotFound("User");

        var minAge = await settings.MinAccountAgeDaysAsync(ct);
        var minFollowers = await settings.MinFollowersAsync(ct);
        var inactivityDays = await settings.GetAsync(SettingKeys.InactivityDays, 30, ct);
        var criteria = EligibilityCriteria.Global(minAge, minFollowers);
        var participant = ParticipantProfile.From(user);

        var accounts = await db.Set<SocialAccount>().AsNoTracking()
            .Where(a => a.UserId == userId && a.IsActive).ToListAsync(ct);

        var counts = await db.Set<Submission>().AsNoTracking()
            .Where(s => s.UserId == userId)
            .GroupBy(s => s.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        return new ParticipantSnapshot
        {
            User = user,
            Now = now,
            MinAccountAgeDays = minAge,
            MinFollowers = minFollowers,
            InactivityDays = inactivityDays,
            ActiveAccounts = accounts.Select(a => EligibilityEvaluator.EvaluateAccount(criteria, participant, a, now)).ToList(),
            SubmissionCounts = counts.ToDictionary(c => c.Status, c => c.Count),
            HasPayoutProfile = await db.Set<PayoutProfile>().AnyAsync(p => p.UserId == userId, ct),
        };
    }
}
