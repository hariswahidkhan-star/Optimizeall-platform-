using OptimizeAll.Api.Common.Ledger;
using OptimizeAll.Api.Modules.Rewards;
using OptimizeAll.Domain.Ledger;
using OptimizeAll.Domain.Rewards;

namespace OptimizeAll.Api.Modules.Rates;

/// <summary>Maps a priced reward line to the rate source recorded on its ledger entry.</summary>
public static class RateSourceMapping
{
    /// <summary>
    /// Post rewards carry their source (campaign rules, or the locked person-level rate); bonuses carry none (they are
    /// always campaign rules, identified by RewardRuleId).
    /// </summary>
    public static EarningRateSource? ForLine(RewardLine line, PricedReward priced)
    {
        if (line.Type != EarningType.PostReward) return null;
        if (line.FromPersonalRate && priced.Rate is { } r)
            return new EarningRateSource(r.Level, r.SourceLabel, r.RateCardId, r.RateCardVersion, r.RateGroupId, r.RateAssignmentId);
        return new EarningRateSource(RateSourceLevel.CampaignRules, $"Campaign rules v{priced.RuleSet.Version}");
    }
}
