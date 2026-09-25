using System.Data;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Ledger;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Modules.Rewards;
using OptimizeAll.Domain.Campaigns;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Rewards;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Rates;

public interface IPersonRatesService
{
    Task<PersonRatesDto> PersonRatesAsync(Guid userId, Guid? campaignId, CancellationToken ct);
    Task<RateExplanationDto> ExplainAsync(Guid userId, ExplainRateQuery query, CancellationToken ct);
    Task<RateExplanationDto> SimulateAsync(Guid campaignId, SimulateRateRequest request, CancellationToken ct);
    Task<CampaignRatesDto> CampaignRatesAsync(Guid campaignId, CancellationToken ct);
    Task<RateAssignmentDto> CreateCustomRateAsync(Guid userId, CreateCustomRateRequest request, CancellationToken ct);

    /// <summary>The participant's own rate in each campaign (null entries = campaign rates apply). Never names cards or groups.</summary>
    Task<IReadOnlyDictionary<Guid, YourRateDto>> YourRatesAsync(Guid userId, IReadOnlyList<(Campaign Campaign, RewardRuleSet? RuleSet)> campaigns,
        CancellationToken ct);

    /// <summary>Card currency → campaign currency pairs without an exchange rate among the rates that can apply in a campaign.</summary>
    Task<IReadOnlyList<string>> CampaignFxProblemsAsync(Guid campaignId, string currency, CancellationToken ct);
}

public sealed class PersonRatesService(
    AppDbContext db,
    IPersonalRateService rates,
    IRateAssignmentsService assignments,
    IRateCardsService cards,
    IRewardQuoteService quotes,
    IExchangeRateProvider fx,
    ICurrentUser currentUser,
    TimeProvider clock) : IPersonRatesService
{
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(30);
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public static IReadOnlyList<PrecedenceLevelDto> Precedence { get; } = Enum.GetValues<RateSourceLevel>()
        .Where(l => l <= RateSourceLevel.CampaignRules) // discount-code payout sources are not part of post pricing
        .OrderBy(l => (int)l).Select(l => new PrecedenceLevelDto((int)l, l, RateSources.Describe(l))).ToList();

    public async Task<PersonRatesDto> PersonRatesAsync(Guid userId, Guid? campaignId, CancellationToken ct)
    {
        var data = await rates.LoadAsync(userId, campaignId, allCampaigns: campaignId is null, ct);
        Campaign? campaign = null;
        RewardRuleSet? set = null;
        var now = Now;
        if (campaignId is { } cid)
        {
            campaign = await db.Set<Campaign>().AsNoTracking().Include(c => c.Platforms).FirstOrDefaultAsync(c => c.Id == cid, ct)
                ?? throw DomainException.NotFound("Campaign");
            set = await quotes.GetCurrentRuleSetAsync(cid, now, ct);
        }

        // Groups: manual memberships plus the automatic groups the person matches on at least one platform.
        var groups = new List<PersonGroupDto>();
        foreach (var g in data.Groups.Values.OrderByDescending(g => g.Priority).ThenBy(g => g.Name))
        {
            if (g.MembershipMode == RateGroupMembershipMode.Manual)
            {
                if (data.Memberships.TryGetValue(g.Id, out var added))
                    groups.Add(new PersonGroupDto(g.Id, g.Name, g.MembershipMode, g.Priority, added, Array.Empty<SocialPlatform>()));
                continue;
            }
            var matched = Enum.GetValues<SocialPlatform>()
                .Where(p => RateCardRules.AutoRuleMatches(g, data.Tier, data.FollowersOn(p, g.AutoRequireVerified), out _)).ToList();
            if (matched.Count > 0) groups.Add(new PersonGroupDto(g.Id, g.Name, g.MembershipMode, g.Priority, null, matched));
        }

        var own = await db.Set<RateAssignment>().AsNoTracking().Where(a => a.UserId == userId)
            .OrderByDescending(a => a.CreatedAt).Take(200).ToListAsync(ct);
        var groupIds = groups.Select(g => g.Id).ToList();
        var viaGroups = await db.Set<RateAssignment>().AsNoTracking()
            .Where(a => a.GroupId != null && groupIds.Contains(a.GroupId.Value) && a.EndedAt == null && (a.ValidTo == null || a.ValidTo > now))
            .ToListAsync(ct);
        var assignmentDtos = await assignments.ToDtosAsync(own.Concat(viaGroups)
            .Where(a => campaignId is null || a.CampaignId is null || a.CampaignId == campaignId).ToList(), ct);

        // Effective rate per platform (and per format a card prices specifically).
        var platforms = campaign?.Platforms.Select(p => p.Platform).OrderBy(p => p).ToList() ?? Enum.GetValues<SocialPlatform>().ToList();
        var effective = new List<EffectiveRateDto>();
        foreach (var platform in platforms)
        {
            foreach (var format in new ContentFormat?[] { null }.Concat(data.FormatsPriced(campaignId, platform).Select(f => (ContentFormat?)f)))
            {
                var resolution = set?.PersonalRatesMode == PersonalRatesMode.CampaignRatesOnly
                    ? new RateResolution(null, Array.Empty<RateCandidateResult>())
                    : data.Resolve(campaignId, platform, format, now, now);
                if (resolution.Winner is { } w)
                {
                    effective.Add(new EffectiveRateDto(platform, format, w.Candidate.Level, RateSources.Describe(w.Candidate.Level),
                        RateSources.Label(w.Candidate.Level, w.Candidate.Card.Name, w.Version!.Version, w.Candidate.Group?.Name),
                        w.Line!.Amount, w.Version.Currency, w.Candidate.AssignmentId, w.Candidate.Card.Id, w.Version.Version, w.Candidate.ValidTo));
                }
                else if (set is not null && format is null)
                {
                    var rule = RewardEngine.SelectRateRule(set, new RewardContext { Platform = platform, CountryCode = data.CountryCode, Tier = data.Tier, PostedAtUtc = now })
                               ?? set.Rules.First(r => r.Type == RewardRuleType.BaseRate);
                    effective.Add(new EffectiveRateDto(platform, null, RateSourceLevel.CampaignRules, RateSources.Describe(RateSourceLevel.CampaignRules),
                        $"Campaign rules v{set.Version}", rule.Amount, set.Currency, null, null, null, null));
                }
            }
        }

        return new PersonRatesDto(new UserRefDto(userId, data.DisplayName), data.CountryCode, data.Tier, data.Status, data.IsTestAccount,
            campaign is null ? null : new RateCampaignRefDto(campaign.Id, campaign.Title), groups, assignmentDtos, effective, Precedence);
    }

    public async Task<RateExplanationDto> ExplainAsync(Guid userId, ExplainRateQuery query, CancellationToken ct)
    {
        var at = query.At is { } a ? RateCardFactory.Utc(a) : Now;
        return await ExplainCoreAsync(userId, query.CampaignId, query.Platform!.Value, query.Format, at, false, ct);
    }

    public Task<RateExplanationDto> SimulateAsync(Guid campaignId, SimulateRateRequest request, CancellationToken ct)
    {
        var at = request.PostedAt is { } p ? RateCardFactory.Utc(p) : Now;
        return ExplainCoreAsync(request.UserId!.Value, campaignId, request.Platform!.Value, request.Format, at, request.IsFirstApprovedPost, ct);
    }

    private async Task<RateExplanationDto> ExplainCoreAsync(Guid userId, Guid? campaignId, SocialPlatform platform, ContentFormat? format,
        DateTime at, bool firstPost, CancellationToken ct)
    {
        var data = await rates.LoadAsync(userId, campaignId, allCampaigns: false, ct);
        Campaign? campaign = null;
        RewardRuleSet? set = null;
        if (campaignId is { } cid)
        {
            campaign = await db.Set<Campaign>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == cid, ct) ?? throw DomainException.NotFound("Campaign");
            set = await quotes.GetCurrentRuleSetAsync(cid, Now, ct);
        }
        var resolution = data.Resolve(campaignId, platform, format, at, Now);
        var candidates = resolution.Trace.Select(r => new ExplainCandidateDto(
            r.Candidate.Level, RateSources.Describe(r.Candidate.Level), r.Outcome, r.Reason, r.Candidate.AssignmentId, r.Candidate.Card.Id,
            r.Candidate.Card.Name, r.Version?.Version, r.Candidate.Group?.Id, r.Candidate.Group?.Name, r.Candidate.Priority, r.Line?.Id,
            r.Line is null ? null : Conditions(r.Line), r.Line?.Amount, r.Version?.Currency, r.Candidate.ValidFrom, r.Candidate.ValidTo)).ToList();

        RateConversionDto? conversion = null;
        string? conversionError = null;
        RewardQuoteDto? quote = null;
        var policyIgnores = set?.PersonalRatesMode == PersonalRatesMode.CampaignRatesOnly;
        string summary;
        if (resolution.Winner is { } w && set is not null && !policyIgnores)
        {
            try
            {
                var snapshot = await rates.SnapshotAsync(resolution, set.Currency, Now, ct);
                conversion = new RateConversionDto(snapshot.CardCurrency, snapshot.Currency, snapshot.ExchangeRate, snapshot.ExchangeRateId,
                    snapshot.CardAmount, snapshot.Amount);
                var context = await quotes.BuildContextAsync(campaign!, set.Currency, userId, platform, at, at, null, null, ct);
                context = context with
                {
                    PersonalRate = PersonalRateService.ToInput(snapshot),
                    IsFirstApprovedPostInCampaign = firstPost || context.IsFirstApprovedPostInCampaign,
                };
                quote = RewardQuoteDto.From(RewardEngine.Quote(set, context), set.Id, set.Version, snapshot);
            }
            catch (DomainException ex) when (ex.Code == "rates.fx_missing")
            {
                conversionError = ex.Message;
            }
            summary = $"{RateSources.Describe(w.Candidate.Level)} wins: {w.Line!.Amount} {w.Version!.Currency} ({w.Reason}).";
        }
        else if (resolution.Winner is { } w2)
        {
            summary = policyIgnores
                ? $"This campaign uses campaign rates only, so {RateSources.Describe(w2.Candidate.Level).ToLowerInvariant()} is ignored."
                : $"{RateSources.Describe(w2.Candidate.Level)} wins: {w2.Line!.Amount} {w2.Version!.Currency} ({w2.Reason}).";
            if (set is not null) quote = await CampaignQuoteAsync(campaign!, set, userId, platform, at, firstPost, ct);
        }
        else
        {
            summary = "No person-level rate applies, so the campaign's own rules price this post.";
            if (set is not null) quote = await CampaignQuoteAsync(campaign!, set, userId, platform, at, firstPost, ct);
        }

        var winner = resolution.Winner is { } win && !policyIgnores ? win.Candidate.Level : RateSourceLevel.CampaignRules;
        return new RateExplanationDto(platform, format, data.CountryCode, data.Tier, data.FollowersOn(platform, true), at,
            campaign is null ? null : new RateCampaignRefDto(campaign.Id, campaign.Title), set?.PersonalRatesMode, set?.PersonalRateMaxMultiplier,
            winner, summary, candidates, conversion, conversionError, quote, Precedence);
    }

    private async Task<RewardQuoteDto> CampaignQuoteAsync(Campaign campaign, RewardRuleSet set, Guid userId, SocialPlatform platform, DateTime at,
        bool firstPost, CancellationToken ct)
    {
        var context = await quotes.BuildContextAsync(campaign, set.Currency, userId, platform, at, at, null, null, ct);
        if (firstPost) context = context with { IsFirstApprovedPostInCampaign = true };
        return RewardQuoteDto.From(RewardEngine.Quote(set, context), set.Id, set.Version);
    }

    public async Task<CampaignRatesDto> CampaignRatesAsync(Guid campaignId, CancellationToken ct)
    {
        var campaign = await db.Set<Campaign>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == campaignId, ct) ?? throw DomainException.NotFound("Campaign");
        var set = await db.Set<RewardRuleSet>().AsNoTracking().Where(s => s.CampaignId == campaignId).OrderByDescending(s => s.Version).FirstOrDefaultAsync(ct);
        var now = Now;
        var scoped = await db.Set<RateAssignment>().AsNoTracking().Where(a => a.CampaignId == campaignId)
            .OrderByDescending(a => a.CreatedAt).Take(500).ToListAsync(ct);
        var global = await db.Set<RateAssignment>().AsNoTracking()
            .Where(a => a.CampaignId == null && a.EndedAt == null && (a.ValidTo == null || a.ValidTo > now))
            .OrderByDescending(a => a.CreatedAt).Take(500).ToListAsync(ct);
        var people = await db.Set<RateAssignment>().AsNoTracking()
            .Where(a => a.UserId != null && (a.CampaignId == null || a.CampaignId == campaignId) && a.EndedAt == null && (a.ValidTo == null || a.ValidTo > now))
            .Select(a => a.UserId).Distinct().CountAsync(ct);
        var currency = set?.Currency ?? campaign.BudgetCurrency;
        return new CampaignRatesDto(campaignId, currency, set?.PersonalRatesMode ?? PersonalRatesMode.Allowed, set?.PersonalRateMaxMultiplier,
            await assignments.ToDtosAsync(scoped, ct), await assignments.ToDtosAsync(global, ct),
            await CampaignFxProblemsAsync(campaignId, currency, ct), people);
    }

    public async Task<IReadOnlyList<string>> CampaignFxProblemsAsync(Guid campaignId, string currency, CancellationToken ct)
    {
        var now = Now;
        var cardCurrencies = await (from a in db.Set<RateAssignment>().AsNoTracking()
                                    join c in db.Set<RateCard>() on a.RateCardId equals c.Id
                                    where (a.CampaignId == null || a.CampaignId == campaignId) && a.EndedAt == null &&
                                          (a.ValidTo == null || a.ValidTo > now) && c.Status == RateCardStatus.Active
                                    select c.Currency).Distinct().ToListAsync(ct);
        var problems = new List<string>();
        foreach (var from in cardCurrencies)
        {
            try
            {
                await fx.GetRateAsync(from, currency, now, ct);
            }
            catch (DomainException ex) when (ex.Code == "fx.rate_missing")
            {
                problems.Add($"{from}→{Money.Normalize(currency)}");
            }
        }
        return problems;
    }

    public async Task<RateAssignmentDto> CreateCustomRateAsync(Guid userId, CreateCustomRateRequest request, CancellationToken ct)
    {
        var owner = await db.Set<User>().AsNoTracking().Where(u => u.Id == userId).Select(u => new { u.DisplayName }).FirstOrDefaultAsync(ct)
            ?? throw DomainException.NotFound("User");
        if (userId == currentUser.Id)
            throw DomainException.Forbidden("rates.self_assignment", "You cannot set your own rate.");
        RateAssignment assignment;
        await using (await db.Dialect().AcquireNamedLockAsync(db, RateAssignmentsService.TargetLockName(userId), LockTimeout, ct))
        await using (var tx = await db.Dialect().BeginWriteTransactionAsync(db, ct, IsolationLevel.ReadCommitted))
        {
            var card = cards.CreateCustomCard(userId, owner.DisplayName, request, request.Name, request.Reason);
            assignment = await assignments.StageAsync(card, RateAssignmentTarget.Person, userId, null, request.CampaignId,
                request.ValidFrom, request.ValidTo, request.Reason, ct);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        return await assignments.GetAsync(assignment.Id, ct);
    }

    public async Task<IReadOnlyDictionary<Guid, YourRateDto>> YourRatesAsync(Guid userId,
        IReadOnlyList<(Campaign Campaign, RewardRuleSet? RuleSet)> campaigns, CancellationToken ct)
    {
        var result = new Dictionary<Guid, YourRateDto>();
        if (campaigns.Count == 0) return result;
        var data = await rates.LoadAsync(userId, null, allCampaigns: true, ct);
        if (data.Assignments.Count == 0) return result;
        var now = Now;
        var fxCache = new Dictionary<(string, string), ResolvedRate?>();
        foreach (var (campaign, set) in campaigns)
        {
            if (set is null || set.PersonalRatesMode == PersonalRatesMode.CampaignRatesOnly) continue;
            var entries = new List<YourRateEntryDto>();
            var personal = false;
            DateTime? validTo = null;
            foreach (var platform in campaign.Platforms.Select(p => p.Platform).OrderBy(p => p))
            {
                decimal? generic = null;
                foreach (var format in new ContentFormat?[] { null }.Concat(data.FormatsPriced(campaign.Id, platform).Select(f => (ContentFormat?)f)))
                {
                    var w = data.Resolve(campaign.Id, platform, format, now, now).Winner;
                    if (w is null) continue;
                    var key = (w.Version!.Currency, Money.Normalize(set.Currency));
                    if (!fxCache.TryGetValue(key, out var rate))
                    {
                        try { rate = await fx.GetRateAsync(key.Item1, key.Item2, now, ct); }
                        catch (DomainException ex) when (ex.Code == "fx.rate_missing") { rate = null; }
                        fxCache[key] = rate;
                    }
                    if (rate is null) continue; // never show an amount we can't price
                    var converted = Money.Convert(w.Line!.Amount, rate.Rate, set.Currency);
                    // What the engine would pay per post (the campaign's multiplier ceiling applies), before caps.
                    var quote = RewardEngine.Quote(set, new RewardContext
                    {
                        Platform = platform, CountryCode = data.CountryCode, Tier = data.Tier, PostedAtUtc = now,
                        PersonalRate = new PersonalRateInput(converted, w.Line.Id, "Post reward"),
                    });
                    var amount = quote.Lines.FirstOrDefault(l => l.Type == Domain.Ledger.EarningType.PostReward)?.UncappedAmount ?? converted;
                    if (format is null) generic = amount;
                    else if (generic == amount) continue;
                    entries.Add(new YourRateEntryDto(platform, format, amount));
                    personal |= RateSources.IsPersonal(w.Candidate.Level);
                    if (w.Candidate.ValidTo is { } to && (validTo is null || to < validTo)) validTo = to;
                }
            }
            if (entries.Count == 0) continue;
            result[campaign.Id] = new YourRateDto(set.Currency, entries.Min(e => e.Amount), entries.Max(e => e.Amount),
                personal ? "Personal" : "Special", validTo, entries);
        }
        return result;
    }

    private static string Conditions(RateCardLine l)
    {
        var parts = new List<string>();
        if (l.Platform is { } p) parts.Add(p.ToString());
        if (l.Format is { } f) parts.Add(f.ToString());
        if (l.CountryCode is { } c) parts.Add(c);
        return parts.Count == 0 ? "Any post" : string.Join(" · ", parts);
    }
}
