using System.Text;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Api.Common.Settings;
using OptimizeAll.Domain.Campaigns;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Eligibility;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Rewards;
using OptimizeAll.Domain.Social;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Campaigns;

/// <summary>Row lock that serializes reward-affecting writes per campaign (approvals, rule versions).</summary>
public static class CampaignLock
{
    /// <summary>
    /// Must run inside a write transaction; holds an exclusive lock on the campaign row until commit/rollback
    /// (MySQL row lock; on SQLite the write transaction already serializes all writers).
    /// </summary>
    public static async Task LockAsync(AppDbContext db, Guid campaignId, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Campaign locks require an open transaction.");
        await db.Dialect().LockRowAsync(db, "campaigns", campaignId, ct);
    }
}

/// <summary>A participant with their social accounts and the platform defaults, loaded once per request.</summary>
public sealed record ParticipantSnapshot(User User, IReadOnlyList<SocialAccount> Accounts, int GlobalMinAccountAgeDays, int GlobalMinFollowers, DateTime NowUtc)
{
    public ParticipantProfile Profile => ParticipantProfile.From(User);
}

public interface IParticipantEligibility
{
    Task<ParticipantSnapshot> LoadAsync(Guid userId, CancellationToken ct);
    EligibilityResult Evaluate(ParticipantSnapshot participant, Campaign campaign);
}

public sealed class ParticipantEligibility(AppDbContext db, ISettingsService settings, TimeProvider clock) : IParticipantEligibility
{
    public async Task<ParticipantSnapshot> LoadAsync(Guid userId, CancellationToken ct)
    {
        var user = await db.Set<User>().AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw DomainException.NotFound("User");
        var accounts = await db.Set<SocialAccount>().AsNoTracking().Where(a => a.UserId == userId).ToListAsync(ct);
        return new ParticipantSnapshot(user, accounts, await settings.MinAccountAgeDaysAsync(ct), await settings.MinFollowersAsync(ct),
            clock.GetUtcNow().UtcDateTime);
    }

    public EligibilityResult Evaluate(ParticipantSnapshot participant, Campaign campaign) =>
        EligibilityEvaluator.Evaluate(
            EligibilityCriteria.ForCampaign(campaign, participant.GlobalMinAccountAgeDays, participant.GlobalMinFollowers),
            participant.Profile, participant.Accounts.ToList(), participant.NowUtc);
}

public static class CampaignText
{
    /// <summary>URL slug from a title: lower-case ASCII letters/digits separated by single hyphens.</summary>
    public static string Slugify(string title)
    {
        var normalized = title.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        var dash = false;
        foreach (var ch in normalized)
        {
            if (char.IsAsciiLetterOrDigit(ch))
            {
                sb.Append(char.ToLowerInvariant(ch));
                dash = false;
            }
            else if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) == System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                // drop accents
            }
            else if (!dash && sb.Length > 0)
            {
                sb.Append('-');
                dash = true;
            }
        }
        var slug = sb.ToString().Trim('-');
        if (slug.Length > 90) slug = slug[..90].Trim('-');
        return slug.Length == 0 ? "campaign" : slug;
    }

    public static List<string> Tags(IEnumerable<string>? values) =>
        (values ?? Enumerable.Empty<string>())
            .Select(v => v.Trim().ToLowerInvariant()).Where(v => v.Length > 0 && v.Length <= 40).Distinct().ToList();

    public static List<string> Upper(IEnumerable<string>? values) =>
        (values ?? Enumerable.Empty<string>())
            .Select(v => v.Trim().ToUpperInvariant()).Where(v => v.Length > 0).Distinct().ToList();

    public static List<string> Lower(IEnumerable<string>? values) =>
        (values ?? Enumerable.Empty<string>())
            .Select(v => v.Trim().ToLowerInvariant()).Where(v => v.Length > 0).Distinct().ToList();

    /// <summary>Most specific disclosure for a platform and country: platform+country, platform, country, then the default.</summary>
    public static string ResolveDisclosure(Campaign campaign, SocialPlatform platform, string? countryCode)
    {
        var country = countryCode?.ToUpperInvariant();
        var d = campaign.Disclosures;
        return d.FirstOrDefault(x => x.Platform == platform && x.CountryCode != null && x.CountryCode == country)?.Text
               ?? d.FirstOrDefault(x => x.Platform == platform && x.CountryCode == null)?.Text
               ?? d.FirstOrDefault(x => x.Platform == null && x.CountryCode != null && x.CountryCode == country)?.Text
               ?? campaign.DefaultDisclosureText;
    }

    public static CardRewardDto? Reward(RewardRuleSet? set, DateTime nowUtc)
    {
        var baseRate = set?.Rules.FirstOrDefault(r => r.Type == RewardRuleType.BaseRate);
        if (set is null || baseRate is null) return null;
        var max = set.Rules.Where(r => r.Type == RewardRuleType.RateOverride && (r.ValidTo is null || r.ValidTo > nowUtc))
            .Select(r => r.Amount).Append(baseRate.Amount).Max();
        var hasBonuses = set.Rules.Any(r =>
            r.Type is RewardRuleType.FirstPostBonus or RewardRuleType.QualityBonus ||
            (r.Type == RewardRuleType.TimeLimitedBonus && (r.ValidTo is null || r.ValidTo > nowUtc)));
        return new CardRewardDto(set.Currency, baseRate.Amount, max, hasBonuses);
    }
}
