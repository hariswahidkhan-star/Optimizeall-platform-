using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Ledger;
using OptimizeAll.Domain.Campaigns;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Rewards;
using OptimizeAll.Domain.Social;
using OptimizeAll.Domain.Submissions;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Rates;

/// <summary>
/// Everything needed to resolve one person's person-level rates: their profile, verified followers per platform,
/// group memberships and the assignments (with cards, approved versions and lines) that could apply to them. Built once
/// per request by <see cref="PersonalRateService.LoadAsync"/>; <see cref="Resolve"/> is then pure and cheap, so a page
/// can price many campaign × platform × format combinations.
/// </summary>
public sealed class PersonRateData
{
    public required Guid UserId { get; init; }
    public required string CountryCode { get; init; }
    public required ParticipantTier Tier { get; init; }
    public required UserStatus Status { get; init; }
    public required bool IsTestAccount { get; init; }
    public required string DisplayName { get; init; }

    /// <summary>Highest follower count per platform over active accounts: (verified accounts only, any account).</summary>
    public required IReadOnlyDictionary<SocialPlatform, (int Verified, int Any)> Followers { get; init; }

    public required IReadOnlyList<RateAssignment> Assignments { get; init; }
    public required IReadOnlyDictionary<Guid, RateCard> Cards { get; init; }
    public required IReadOnlyDictionary<Guid, RateGroup> Groups { get; init; }

    /// <summary>Manual groups the person belongs to, with the time they were added.</summary>
    public required IReadOnlyDictionary<Guid, DateTime> Memberships { get; init; }

    public int FollowersOn(SocialPlatform platform, bool verifiedOnly) =>
        Followers.TryGetValue(platform, out var f) ? (verifiedOnly ? f.Verified : f.Any) : 0;

    /// <summary>
    /// Candidates for a post in <paramref name="campaignId"/> (null = only assignments that apply to every campaign).
    /// Group assignments are only candidates for members (manual) or, for automatic groups, carry the reason when the
    /// person doesn't match the rule on <paramref name="platform"/>.
    /// </summary>
    public IReadOnlyList<RateCandidate> Candidates(Guid? campaignId, SocialPlatform platform)
    {
        var list = new List<RateCandidate>();
        foreach (var a in Assignments)
        {
            if (a.CampaignId is { } scoped && scoped != campaignId) continue;
            if (!Cards.TryGetValue(a.RateCardId, out var card)) continue;
            RateGroup? group = null;
            string? ineligible = null;
            if (a.Target == RateAssignmentTarget.Group)
            {
                if (a.GroupId is not { } gid || !Groups.TryGetValue(gid, out group) || group.ArchivedAt is not null) continue;
                if (group.MembershipMode == RateGroupMembershipMode.Manual)
                {
                    if (!Memberships.ContainsKey(gid)) continue;
                }
                else if (!RateCardRules.AutoRuleMatches(group, Tier, FollowersOn(platform, group.AutoRequireVerified), out var why))
                {
                    ineligible = $"not in automatic group '{group.Name}': {why}";
                }
            }
            else if (a.UserId != UserId) continue;

            var level = RateAssignment.LevelFor(a.Target, a.IsCustom, a.CampaignId is not null, group?.MembershipMode);
            list.Add(new RateCandidate(level, a.Id, card, card.Versions, group, group?.Priority ?? 0, a.ValidFrom, a.EffectiveTo, ineligible));
        }
        return list;
    }

    public RateResolution Resolve(Guid? campaignId, SocialPlatform platform, ContentFormat? format, DateTime windowAtUtc, DateTime versionAtUtc) =>
        PersonalRateResolver.Resolve(Candidates(campaignId, platform), new RateSubject(platform, format, CountryCode, windowAtUtc, versionAtUtc));

    /// <summary>Formats that any candidate card prices specifically on <paramref name="platform"/>.</summary>
    public IReadOnlyList<ContentFormat> FormatsPriced(Guid? campaignId, SocialPlatform platform) =>
        Candidates(campaignId, platform)
            .SelectMany(c => c.Versions.SelectMany(v => v.Lines))
            .Where(l => l.Format.HasValue && (!l.Platform.HasValue || l.Platform == platform))
            .Select(l => l.Format!.Value).Distinct().OrderBy(f => f).ToList();
}

public interface IPersonalRateService
{
    /// <summary>
    /// Loads a person's rate data. <paramref name="campaignId"/> limits campaign-scoped assignments to that campaign;
    /// with <paramref name="allCampaigns"/> every campaign's scoped assignments are loaded (participant catalog).
    /// </summary>
    Task<PersonRateData> LoadAsync(Guid userId, Guid? campaignId, bool allCampaigns, CancellationToken ct = default);

    /// <summary>
    /// Resolves and converts the person-level rate for a new submission; null when the campaign rules price it (no
    /// person-level rate applies, or the rule set says "campaign rates only"). Throws 409 rates.fx_missing when the
    /// winning card's currency cannot be converted — a post is never silently priced at 0.
    /// </summary>
    Task<SubmissionRate?> ResolveForSubmissionAsync(Submission submission, RewardRuleSet ruleSet, CancellationToken ct = default);

    /// <summary>Converts a resolution's winner into a snapshot in <paramref name="campaignCurrency"/> (throws rates.fx_missing).</summary>
    Task<SubmissionRate> SnapshotAsync(RateResolution resolution, string campaignCurrency, DateTime atUtc, CancellationToken ct = default);

    /// <summary>Throws 409 rates.fx_missing listing every card currency → campaign currency pair without an exchange rate.</summary>
    Task EnsureConvertibleAsync(string cardCurrency, IEnumerable<string> campaignCurrencies, DateTime atUtc, CancellationToken ct = default);

    /// <summary>Currencies of every live (scheduled, active or paused) campaign.</summary>
    Task<IReadOnlyList<string>> LiveCampaignCurrenciesAsync(CancellationToken ct = default);
}

public sealed class PersonalRateService(AppDbContext db, IExchangeRateProvider fx, TimeProvider clock) : IPersonalRateService
{
    /// <summary>Assignments that ended or expired more than this long ago can no longer match any post window.</summary>
    private static readonly TimeSpan HistoryHorizon = SubmissionTiming.MaxPostAgeAtSubmission + TimeSpan.FromDays(1);

    public async Task<PersonRateData> LoadAsync(Guid userId, Guid? campaignId, bool allCampaigns, CancellationToken ct = default)
    {
        var user = await db.Set<User>().AsNoTracking().Where(u => u.Id == userId)
            .Select(u => new { u.CountryCode, u.Tier, u.Status, u.IsTestAccount, u.DisplayName })
            .FirstOrDefaultAsync(ct) ?? throw DomainException.NotFound("User");

        var memberships = await (from m in db.Set<RateGroupMember>().AsNoTracking()
                                 join g in db.Set<RateGroup>() on m.GroupId equals g.Id
                                 where m.UserId == userId && g.ArchivedAt == null
                                 select new { m.GroupId, m.AddedAt }).ToListAsync(ct);
        var autoGroups = await db.Set<RateGroup>().AsNoTracking()
            .Where(g => g.MembershipMode == RateGroupMembershipMode.Automatic && g.ArchivedAt == null).ToListAsync(ct);
        var groupIds = memberships.Select(m => m.GroupId).Concat(autoGroups.Select(g => g.Id)).Distinct().ToList();

        var horizon = clock.GetUtcNow().UtcDateTime - HistoryHorizon;
        var query = db.Set<RateAssignment>().AsNoTracking()
            .Where(a => (a.UserId == userId || (a.GroupId != null && groupIds.Contains(a.GroupId.Value))) &&
                        (a.EndedAt == null || a.EndedAt > horizon) && (a.ValidTo == null || a.ValidTo > horizon));
        if (!allCampaigns) query = query.Where(a => a.CampaignId == null || a.CampaignId == campaignId);
        var assignments = await query.ToListAsync(ct);

        var cardIds = assignments.Select(a => a.RateCardId).Distinct().ToList();
        var cards = cardIds.Count == 0
            ? new List<RateCard>()
            : await db.Set<RateCard>().AsNoTracking().AsSplitQuery()
                .Include(c => c.Versions.Where(v => v.Status == RateCardVersionStatus.Approved)).ThenInclude(v => v.Lines)
                .Where(c => cardIds.Contains(c.Id)).ToListAsync(ct);

        var manualIds = memberships.Select(m => m.GroupId).ToList();
        var groups = autoGroups.Concat(manualIds.Count == 0
                ? new List<RateGroup>()
                : await db.Set<RateGroup>().AsNoTracking().Where(g => manualIds.Contains(g.Id)).ToListAsync(ct))
            .GroupBy(g => g.Id).ToDictionary(g => g.Key, g => g.First());

        var accounts = await db.Set<SocialAccount>().AsNoTracking().Where(a => a.UserId == userId && a.IsActive)
            .Select(a => new { a.Platform, a.FollowerCount, a.VerificationStatus }).ToListAsync(ct);
        var followers = accounts.GroupBy(a => a.Platform).ToDictionary(g => g.Key, g => (
            g.Where(a => a.VerificationStatus == SocialAccountVerificationStatus.Verified).Select(a => a.FollowerCount).DefaultIfEmpty(0).Max(),
            g.Max(a => a.FollowerCount)));

        return new PersonRateData
        {
            UserId = userId, CountryCode = user.CountryCode, Tier = user.Tier, Status = user.Status, IsTestAccount = user.IsTestAccount,
            DisplayName = user.DisplayName, Followers = followers, Assignments = assignments,
            Cards = cards.ToDictionary(c => c.Id), Groups = groups,
            Memberships = memberships.GroupBy(m => m.GroupId).ToDictionary(g => g.Key, g => g.Min(m => m.AddedAt)),
        };
    }

    public async Task<SubmissionRate?> ResolveForSubmissionAsync(Submission submission, RewardRuleSet ruleSet, CancellationToken ct = default)
    {
        if (ruleSet.PersonalRatesMode == PersonalRatesMode.CampaignRatesOnly) return null;
        var data = await LoadAsync(submission.UserId, submission.CampaignId, allCampaigns: false, ct);
        if (data.Assignments.Count == 0) return null;
        var resolution = data.Resolve(submission.CampaignId, submission.Platform, submission.Format,
            SubmissionTiming.RewardWindowTime(submission.PostedAt, submission.SubmittedAt), submission.SubmittedAt);
        if (resolution.Winner is null) return null;
        var snapshot = await SnapshotAsync(resolution, ruleSet.Currency, submission.SubmittedAt, ct);
        snapshot.SubmissionId = submission.Id;
        return snapshot;
    }

    public async Task<SubmissionRate> SnapshotAsync(RateResolution resolution, string campaignCurrency, DateTime atUtc, CancellationToken ct = default)
    {
        var w = resolution.Winner ?? throw new InvalidOperationException("No person-level rate won.");
        var version = w.Version!;
        var line = w.Line!;
        campaignCurrency = Money.Normalize(campaignCurrency);
        var rate = await RateAsync(version.Currency, campaignCurrency, atUtc, ct);
        decimal? Cap(decimal? cap) => cap is { } c ? Money.Convert(c, rate.Rate, campaignCurrency) : null;
        return new SubmissionRate
        {
            Level = w.Candidate.Level,
            RateAssignmentId = w.Candidate.AssignmentId,
            RateCardId = w.Candidate.Card.Id,
            RateCardVersionId = version.Id,
            RateCardVersion = version.Version,
            RateCardLineId = line.Id,
            RateGroupId = w.Candidate.Group?.Id,
            CardName = w.Candidate.Card.Name,
            GroupName = w.Candidate.Group?.Name,
            CardAmount = line.Amount,
            CardCurrency = version.Currency,
            ExchangeRate = rate.Rate,
            ExchangeRateId = rate.ExchangeRateId,
            Amount = Money.Convert(line.Amount, rate.Rate, campaignCurrency),
            Currency = campaignCurrency,
            DailyCap = Cap(version.DailyCapPerParticipant),
            WeeklyCap = Cap(version.WeeklyCapPerParticipant),
            CampaignCap = Cap(version.CampaignCapPerParticipant),
            StackCampaignBonuses = version.StackCampaignBonuses,
            LineLabel = line.Label,
            AssignmentValidTo = w.Candidate.ValidTo,
            ResolvedAt = atUtc,
            Explanation = resolution.Explain(),
        };
    }

    private async Task<ResolvedRate> RateAsync(string from, string to, DateTime atUtc, CancellationToken ct)
    {
        try
        {
            return await fx.GetRateAsync(from, to, atUtc, ct);
        }
        catch (DomainException ex) when (ex.Code == "fx.rate_missing")
        {
            throw FxMissing(new[] { $"{Money.Normalize(from)}→{Money.Normalize(to)}" });
        }
    }

    public async Task EnsureConvertibleAsync(string cardCurrency, IEnumerable<string> campaignCurrencies, DateTime atUtc, CancellationToken ct = default)
    {
        var missing = new List<string>();
        foreach (var currency in campaignCurrencies.Select(Money.Normalize).Distinct().OrderBy(c => c))
        {
            try
            {
                await fx.GetRateAsync(cardCurrency, currency, atUtc, ct);
            }
            catch (DomainException ex) when (ex.Code == "fx.rate_missing")
            {
                missing.Add($"{Money.Normalize(cardCurrency)}→{currency}");
            }
        }
        if (missing.Count > 0) throw FxMissing(missing);
    }

    public async Task<IReadOnlyList<string>> LiveCampaignCurrenciesAsync(CancellationToken ct = default) =>
        await db.Set<Campaign>().AsNoTracking()
            .Where(c => c.Status == CampaignStatus.Scheduled || c.Status == CampaignStatus.Active || c.Status == CampaignStatus.Paused)
            .Select(c => c.BudgetCurrency).Distinct().ToListAsync(ct);

    public static DomainException FxMissing(IReadOnlyCollection<string> pairs) =>
        new("rates.fx_missing",
            $"No exchange rate is configured for {string.Join(", ", pairs)}. Add it under Finance → Exchange rates first; " +
            "rates are never priced without a conversion.", DomainErrorKind.Conflict,
            new Dictionary<string, string[]> { ["currency"] = pairs.ToArray() });

    /// <summary>The engine input for a locked snapshot.</summary>
    public static PersonalRateInput ToInput(SubmissionRate r) =>
        new(r.Amount, r.RateCardLineId, string.IsNullOrWhiteSpace(r.LineLabel) ? "Post reward (your rate)" : r.LineLabel!,
            r.StackCampaignBonuses, r.DailyCap, r.WeeklyCap, r.CampaignCap);
}
