using OptimizeAll.Domain.Campaigns;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Eligibility;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Social;
using Xunit;

namespace OptimizeAll.UnitTests.Foundation;

public sealed class EligibilityEvaluatorTests
{
    private static readonly DateTime Now = new(2026, 9, 23, 12, 0, 0, DateTimeKind.Utc);

    private static ParticipantProfile Participant(
        bool verified = true, UserStatus status = UserStatus.Active, string country = "PK", string language = "en",
        ParticipantTier tier = ParticipantTier.Standard, params string[] interests) =>
        new(Guid.NewGuid(), status, verified, country, language, tier, interests);

    private static SocialAccount Account(
        int ageDays = 400, int followers = 5000, SocialPlatform platform = SocialPlatform.Instagram,
        SocialAccountVerificationStatus verification = SocialAccountVerificationStatus.Unverified, bool active = true,
        string? language = null) => new()
    {
        Platform = platform,
        Handle = "creator",
        NormalizedHandle = "creator",
        AccountCreatedAt = Now.AddDays(-ageDays),
        FollowerCount = followers,
        VerificationStatus = verification,
        IsActive = active,
        PrimaryLanguage = language,
    };

    private static EligibilityCriteria Criteria(
        int minAge = 90, int minFollowers = 0, bool requireVerified = false, SocialPlatform[]? platforms = null,
        string[]? countries = null, string[]? languages = null, string[]? interests = null, ParticipantTier[]? tiers = null) =>
        new(minAge, minFollowers, requireVerified, platforms ?? Array.Empty<SocialPlatform>(), countries ?? Array.Empty<string>(),
            languages ?? Array.Empty<string>(), interests ?? Array.Empty<string>(), tiers ?? Array.Empty<ParticipantTier>());

    [Fact]
    public void Established_account_meeting_all_rules_is_eligible()
    {
        var result = EligibilityEvaluator.Evaluate(Criteria(), Participant(), new[] { Account() }, Now);
        Assert.True(result.IsEligible);
        Assert.Empty(result.ParticipantReasons);
        Assert.Single(result.EligibleAccounts);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(30)]
    [InlineData(89)]
    public void Newly_created_accounts_are_ineligible_until_minimum_age(int ageDays)
    {
        var account = Account(ageDays: ageDays);
        var result = EligibilityEvaluator.Evaluate(Criteria(minAge: 90), Participant(), new[] { account }, Now);

        Assert.False(result.IsEligible);
        var accountResult = Assert.Single(result.Accounts);
        Assert.Contains(accountResult.Reasons, r => r.Code == IneligibilityCodes.AccountTooNew);
        Assert.Equal(account.AccountCreatedAt.AddDays(90), accountResult.EligibleFrom);
        Assert.Contains(result.ParticipantReasons, r => r.Code == IneligibilityCodes.NoQualifyingAccount);
    }

    [Fact]
    public void Account_exactly_at_minimum_age_is_eligible()
    {
        var result = EligibilityEvaluator.Evaluate(Criteria(minAge: 90), Participant(), new[] { Account(ageDays: 90) }, Now);
        Assert.True(result.IsEligible);
    }

    [Fact]
    public void Minimum_age_is_configurable()
    {
        var account = Account(ageDays: 45);
        Assert.False(EligibilityEvaluator.Evaluate(Criteria(minAge: 60), Participant(), new[] { account }, Now).IsEligible);
        Assert.True(EligibilityEvaluator.Evaluate(Criteria(minAge: 30), Participant(), new[] { account }, Now).IsEligible);
    }

    [Fact]
    public void EligibleFrom_is_only_reported_when_age_is_the_sole_blocker()
    {
        var result = EligibilityEvaluator.Evaluate(Criteria(minAge: 90, minFollowers: 10_000), Participant(),
            new[] { Account(ageDays: 10, followers: 50) }, Now);
        Assert.Null(result.Accounts[0].EligibleFrom);
        Assert.Equal(2, result.Accounts[0].Reasons.Count);
    }

    [Fact]
    public void Unverified_email_and_suspension_block_participation()
    {
        var unverified = EligibilityEvaluator.Evaluate(Criteria(), Participant(verified: false), new[] { Account() }, Now);
        Assert.Contains(unverified.ParticipantReasons, r => r.Code == IneligibilityCodes.EmailUnverified);

        var suspended = EligibilityEvaluator.Evaluate(Criteria(), Participant(status: UserStatus.Suspended), new[] { Account() }, Now);
        Assert.Contains(suspended.ParticipantReasons, r => r.Code == IneligibilityCodes.AccountSuspended);
        Assert.False(suspended.IsEligible);
    }

    [Fact]
    public void No_social_accounts_is_reported()
    {
        var result = EligibilityEvaluator.Evaluate(Criteria(), Participant(), Array.Empty<SocialAccount>(), Now);
        Assert.Contains(result.ParticipantReasons, r => r.Code == IneligibilityCodes.NoSocialAccounts);
    }

    [Fact]
    public void Platform_followers_verification_and_active_rules_apply_per_account()
    {
        var criteria = Criteria(minFollowers: 1000, requireVerified: true, platforms: new[] { SocialPlatform.TikTok });
        var accounts = new[]
        {
            Account(platform: SocialPlatform.Instagram, verification: SocialAccountVerificationStatus.Verified),
            Account(platform: SocialPlatform.TikTok, followers: 10, verification: SocialAccountVerificationStatus.Verified),
            Account(platform: SocialPlatform.TikTok, verification: SocialAccountVerificationStatus.Unverified),
            Account(platform: SocialPlatform.TikTok, verification: SocialAccountVerificationStatus.Verified, active: false),
            Account(platform: SocialPlatform.TikTok, verification: SocialAccountVerificationStatus.Verified),
        };

        var result = EligibilityEvaluator.Evaluate(criteria, Participant(), accounts, Now);

        Assert.True(result.IsEligible);
        Assert.Single(result.EligibleAccounts);
        var codes = result.Accounts.Where(a => !a.IsEligible).SelectMany(a => a.Reasons.Select(r => r.Code)).ToList();
        Assert.Contains(IneligibilityCodes.PlatformNotAllowed, codes);
        Assert.Contains(IneligibilityCodes.FollowersBelowMinimum, codes);
        Assert.Contains(IneligibilityCodes.AccountNotVerified, codes);
        Assert.Contains(IneligibilityCodes.AccountInactive, codes);
    }

    [Fact]
    public void Rejected_verification_always_disqualifies()
    {
        var result = EligibilityEvaluator.Evaluate(Criteria(), Participant(),
            new[] { Account(verification: SocialAccountVerificationStatus.Rejected) }, Now);
        Assert.Contains(result.Accounts[0].Reasons, r => r.Code == IneligibilityCodes.AccountRejected);
    }

    [Fact]
    public void Segmentation_by_country_tier_and_interests()
    {
        var criteria = Criteria(countries: new[] { "AE", "SA" }, tiers: new[] { ParticipantTier.Gold }, interests: new[] { "fitness" });

        var wrong = EligibilityEvaluator.Evaluate(criteria, Participant(country: "PK", interests: "travel"), new[] { Account() }, Now);
        Assert.Contains(wrong.ParticipantReasons, r => r.Code == IneligibilityCodes.CountryNotTargeted);
        Assert.Contains(wrong.ParticipantReasons, r => r.Code == IneligibilityCodes.TierNotTargeted);
        Assert.Contains(wrong.ParticipantReasons, r => r.Code == IneligibilityCodes.InterestsNotMatched);

        var right = EligibilityEvaluator.Evaluate(criteria,
            Participant(country: "ae", tier: ParticipantTier.Gold, interests: new[] { "Fitness", "food" }), new[] { Account() }, Now);
        Assert.True(right.IsEligible);
    }

    [Fact]
    public void Language_targeting_matches_participant_or_account_audience_language()
    {
        var criteria = Criteria(languages: new[] { "ar" });
        Assert.False(EligibilityEvaluator.Evaluate(criteria, Participant(language: "en"), new[] { Account() }, Now).IsEligible);
        Assert.True(EligibilityEvaluator.Evaluate(criteria, Participant(language: "ar"), new[] { Account() }, Now).IsEligible);
        Assert.True(EligibilityEvaluator.Evaluate(criteria, Participant(language: "en"), new[] { Account(language: "ar") }, Now).IsEligible);
    }

    [Fact]
    public void Campaign_criteria_fall_back_to_global_defaults()
    {
        var campaign = new Campaign { Eligibility = new CampaignEligibility { MinAccountAgeDays = null, MinFollowers = 100 } };
        campaign.Platforms.Add(new CampaignPlatform { Platform = SocialPlatform.X });

        var criteria = EligibilityCriteria.ForCampaign(campaign, globalMinAccountAgeDays: 120, globalMinFollowers: 500);

        Assert.Equal(120, criteria.MinAccountAgeDays);
        Assert.Equal(500, criteria.MinFollowers);
        Assert.Equal(new[] { SocialPlatform.X }, criteria.AllowedPlatforms);

        campaign.Eligibility.MinAccountAgeDays = 30;
        Assert.Equal(30, EligibilityCriteria.ForCampaign(campaign, 120, 0).MinAccountAgeDays);
    }
}
