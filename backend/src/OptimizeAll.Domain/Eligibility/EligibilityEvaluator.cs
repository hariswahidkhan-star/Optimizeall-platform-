using OptimizeAll.Domain.Campaigns;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Social;

namespace OptimizeAll.Domain.Eligibility;

/// <summary>Stable reason codes returned to clients, with human-readable messages.</summary>
public static class IneligibilityCodes
{
    public const string EmailUnverified = "account.email_unverified";
    public const string AccountSuspended = "account.suspended";
    public const string CountryNotTargeted = "participant.country_not_targeted";
    public const string LanguageNotTargeted = "participant.language_not_targeted";
    public const string TierNotTargeted = "participant.tier_not_targeted";
    public const string InterestsNotMatched = "participant.interests_not_matched";
    public const string NoSocialAccounts = "participant.no_social_accounts";
    public const string NoQualifyingAccount = "participant.no_qualifying_account";
    public const string PlatformNotAllowed = "social.platform_not_allowed";
    public const string AccountTooNew = "social.account_too_new";
    public const string FollowersBelowMinimum = "social.followers_below_minimum";
    public const string AccountNotVerified = "social.not_verified";
    public const string AccountInactive = "social.inactive";
    public const string AccountRejected = "social.verification_rejected";
}

public sealed record EligibilityReason(string Code, string Message);

/// <summary>Participant attributes relevant to eligibility.</summary>
public sealed record ParticipantProfile(
    Guid UserId,
    UserStatus Status,
    bool EmailVerified,
    string CountryCode,
    string LanguageCode,
    ParticipantTier Tier,
    IReadOnlyCollection<string> Interests)
{
    public static ParticipantProfile From(User user) => new(
        user.Id, user.Status, user.IsEmailVerified, user.CountryCode, user.LanguageCode, user.Tier, user.Interests);
}

/// <summary>Campaign-side rules. <see cref="AllowedPlatforms"/> empty = all platforms.</summary>
public sealed record EligibilityCriteria(
    int MinAccountAgeDays,
    int MinFollowers,
    bool RequireVerifiedAccount,
    IReadOnlyCollection<SocialPlatform> AllowedPlatforms,
    IReadOnlyCollection<string> Countries,
    IReadOnlyCollection<string> Languages,
    IReadOnlyCollection<string> Interests,
    IReadOnlyCollection<ParticipantTier> Tiers)
{
    /// <summary>Platform-wide defaults used to show whether a profile qualifies in general.</summary>
    public static EligibilityCriteria Global(int minAccountAgeDays, int minFollowers) => new(
        minAccountAgeDays, minFollowers, false, Array.Empty<SocialPlatform>(), Array.Empty<string>(),
        Array.Empty<string>(), Array.Empty<string>(), Array.Empty<ParticipantTier>());

    public static EligibilityCriteria ForCampaign(Campaign campaign, int globalMinAccountAgeDays, int globalMinFollowers) => new(
        campaign.Eligibility.MinAccountAgeDays ?? globalMinAccountAgeDays,
        Math.Max(campaign.Eligibility.MinFollowers, globalMinFollowers),
        campaign.Eligibility.RequireVerifiedAccount,
        campaign.Platforms.Select(p => p.Platform).ToArray(),
        campaign.Eligibility.Countries,
        campaign.Eligibility.Languages,
        campaign.Eligibility.Interests,
        campaign.Eligibility.Tiers);
}

/// <summary>Per-profile result. <c>EligibleFrom</c> is when the profile becomes old enough (UTC), set only if age is the sole blocker.</summary>
public sealed record SocialAccountEligibility(
    Guid SocialAccountId,
    SocialPlatform Platform,
    string Handle,
    bool IsEligible,
    int AccountAgeDays,
    DateTime? EligibleFrom,
    IReadOnlyList<EligibilityReason> Reasons);

public sealed record EligibilityResult(
    bool IsEligible,
    IReadOnlyList<EligibilityReason> ParticipantReasons,
    IReadOnlyList<SocialAccountEligibility> Accounts)
{
    public IEnumerable<SocialAccountEligibility> EligibleAccounts => Accounts.Where(a => a.IsEligible);
}

/// <summary>
/// Pure eligibility rules shared by the participant portal, campaign browsing and submission validation.
/// Newly created social accounts are ineligible until they reach the configured minimum age.
/// </summary>
public static class EligibilityEvaluator
{
    public static EligibilityResult Evaluate(
        EligibilityCriteria criteria,
        ParticipantProfile participant,
        IReadOnlyCollection<SocialAccount> accounts,
        DateTime nowUtc)
    {
        var participantReasons = new List<EligibilityReason>();

        if (participant.Status != UserStatus.Active)
            participantReasons.Add(new(IneligibilityCodes.AccountSuspended, "Your account is not active."));
        if (!participant.EmailVerified)
            participantReasons.Add(new(IneligibilityCodes.EmailUnverified, "Verify your email address to take part."));

        if (criteria.Countries.Count > 0 &&
            !criteria.Countries.Contains(participant.CountryCode, StringComparer.OrdinalIgnoreCase))
            participantReasons.Add(new(IneligibilityCodes.CountryNotTargeted, "This campaign is not available in your country."));

        if (criteria.Tiers.Count > 0 && !criteria.Tiers.Contains(participant.Tier))
            participantReasons.Add(new(IneligibilityCodes.TierNotTargeted,
                $"This campaign is for {string.Join(", ", criteria.Tiers)} tier participants."));

        if (criteria.Interests.Count > 0 &&
            !criteria.Interests.Any(i => participant.Interests.Contains(i, StringComparer.OrdinalIgnoreCase)))
            participantReasons.Add(new(IneligibilityCodes.InterestsNotMatched,
                "This campaign targets interests that are not in your profile."));

        var accountResults = accounts
            .OrderBy(a => a.Platform).ThenBy(a => a.Handle)
            .Select(a => EvaluateAccount(criteria, participant, a, nowUtc))
            .ToList();

        // Language targeting passes if the participant's language or any eligible account's audience language matches.
        if (criteria.Languages.Count > 0 &&
            !criteria.Languages.Contains(participant.LanguageCode, StringComparer.OrdinalIgnoreCase) &&
            !accounts.Any(a => a.PrimaryLanguage is not null &&
                               criteria.Languages.Contains(a.PrimaryLanguage, StringComparer.OrdinalIgnoreCase)))
            participantReasons.Add(new(IneligibilityCodes.LanguageNotTargeted,
                "This campaign targets a language that doesn't match your profile."));

        if (accounts.Count == 0)
            participantReasons.Add(new(IneligibilityCodes.NoSocialAccounts, "Add a social media profile to take part."));
        else if (!accountResults.Any(a => a.IsEligible))
            participantReasons.Add(new(IneligibilityCodes.NoQualifyingAccount,
                "None of your social profiles meet this campaign's requirements yet."));

        return new EligibilityResult(participantReasons.Count == 0, participantReasons, accountResults);
    }

    public static SocialAccountEligibility EvaluateAccount(
        EligibilityCriteria criteria, ParticipantProfile participant, SocialAccount account, DateTime nowUtc)
    {
        _ = participant;
        var reasons = new List<EligibilityReason>();
        var ageDays = account.AccountAgeDays(nowUtc);
        DateTime? eligibleFrom = null;

        if (!account.IsActive)
            reasons.Add(new(IneligibilityCodes.AccountInactive, "This profile is deactivated."));

        if (account.VerificationStatus == SocialAccountVerificationStatus.Rejected)
            reasons.Add(new(IneligibilityCodes.AccountRejected, "This profile failed verification."));
        else if (criteria.RequireVerifiedAccount && account.VerificationStatus != SocialAccountVerificationStatus.Verified)
            reasons.Add(new(IneligibilityCodes.AccountNotVerified, "This campaign requires a verified profile."));

        if (criteria.AllowedPlatforms.Count > 0 && !criteria.AllowedPlatforms.Contains(account.Platform))
            reasons.Add(new(IneligibilityCodes.PlatformNotAllowed, $"{account.Platform} is not part of this campaign."));

        if (ageDays < criteria.MinAccountAgeDays)
        {
            eligibleFrom = account.AccountCreatedAt.AddDays(criteria.MinAccountAgeDays);
            var remaining = criteria.MinAccountAgeDays - ageDays;
            reasons.Add(new(IneligibilityCodes.AccountTooNew,
                $"Profiles must be at least {criteria.MinAccountAgeDays} days old. This one qualifies in {remaining} day{(remaining == 1 ? "" : "s")}."));
        }

        if (account.FollowerCount < criteria.MinFollowers)
            reasons.Add(new(IneligibilityCodes.FollowersBelowMinimum,
                $"Profiles need at least {criteria.MinFollowers:N0} followers."));

        return new SocialAccountEligibility(account.Id, account.Platform, account.Handle, reasons.Count == 0,
            Math.Max(ageDays, 0), reasons.Count == 1 && reasons[0].Code == IneligibilityCodes.AccountTooNew ? eligibleFrom : null,
            reasons);
    }
}
