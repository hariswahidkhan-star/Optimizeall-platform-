using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Errors;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Modules.Campaigns;
using OptimizeAll.Domain.Campaigns;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Rewards;
using OptimizeAll.Domain.Submissions;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Rewards;

public static class RewardRuleSetFactory
{
    /// <summary>Builds an (unsaved) rule set from client input, normalizing currency, country codes and dates.</summary>
    public static RewardRuleSet Build(RewardRuleSetInput input, Guid campaignId, int version, DateTime nowUtc, Guid createdBy, string reason)
    {
        var set = new RewardRuleSet
        {
            CampaignId = campaignId,
            Version = version,
            Currency = Money.Normalize(input.Currency),
            DailyCapPerParticipant = input.DailyCapPerParticipant,
            WeeklyCapPerParticipant = input.WeeklyCapPerParticipant,
            CampaignCapPerParticipant = input.CampaignCapPerParticipant,
            EffectiveFrom = nowUtc,
            CreatedAt = nowUtc,
            CreatedByUserId = createdBy,
            ChangeReason = reason.Trim(),
        };
        foreach (var r in input.Rules)
        {
            set.Rules.Add(new RewardRule
            {
                RuleSetId = set.Id,
                Type = r.Type,
                Amount = r.Amount,
                Platform = r.Platform,
                CountryCode = string.IsNullOrWhiteSpace(r.CountryCode) ? null : r.CountryCode.Trim().ToUpperInvariant(),
                Tier = r.Tier,
                ValidFrom = Utc(r.ValidFrom),
                ValidTo = Utc(r.ValidTo),
                ApprovalMode = r.ApprovalMode,
                Priority = r.Priority,
                Label = string.IsNullOrWhiteSpace(r.Label) ? null : r.Label.Trim(),
            });
        }
        return set;
    }

    /// <summary>Copies a saved version as a new (unsaved) version with fresh ids.</summary>
    public static RewardRuleSet Copy(RewardRuleSet source, Guid campaignId, int version, DateTime nowUtc, Guid createdBy, string reason)
    {
        var set = new RewardRuleSet
        {
            CampaignId = campaignId, Version = version, Currency = source.Currency,
            DailyCapPerParticipant = source.DailyCapPerParticipant,
            WeeklyCapPerParticipant = source.WeeklyCapPerParticipant,
            CampaignCapPerParticipant = source.CampaignCapPerParticipant,
            EffectiveFrom = nowUtc, CreatedAt = nowUtc, CreatedByUserId = createdBy, ChangeReason = reason,
        };
        foreach (var r in source.Rules)
        {
            set.Rules.Add(new RewardRule
            {
                RuleSetId = set.Id, Type = r.Type, Amount = r.Amount, Platform = r.Platform, CountryCode = r.CountryCode,
                Tier = r.Tier, ValidFrom = r.ValidFrom, ValidTo = r.ValidTo, ApprovalMode = r.ApprovalMode,
                Priority = r.Priority, Label = r.Label,
            });
        }
        return set;
    }

    public static DateTime? Utc(DateTime? value) => value is null ? null : Utc(value.Value);

    public static DateTime Utc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };

    public static object Snapshot(RewardRuleSet s) => new
    {
        s.Version, s.Currency, s.DailyCapPerParticipant, s.WeeklyCapPerParticipant, s.CampaignCapPerParticipant,
        Summary = RewardEngine.Summarize(s),
        Rules = s.Rules.Select(r => new { r.Type, r.Amount, r.Platform, r.CountryCode, r.Tier, r.ValidFrom, r.ValidTo, r.ApprovalMode, r.Priority, r.Label }),
    };
}

public interface IRewardRulesService
{
    Task<IReadOnlyList<RewardRuleSetDto>> ListAsync(Guid campaignId, CancellationToken ct);
    Task<RewardRuleSetDto> CreateVersionAsync(Guid campaignId, CreateRewardRuleSetRequest request, CancellationToken ct);
    Task<RewardQuoteDto> PreviewAsync(Guid campaignId, RewardPreviewRequest request, CancellationToken ct);

    /// <summary>Maps saved versions to DTOs (creator names, usage counts, which one is current).</summary>
    Task<IReadOnlyList<RewardRuleSetDto>> ToDtosAsync(IReadOnlyList<RewardRuleSet> sets, CancellationToken ct);
}

public sealed class RewardRulesService(
    AppDbContext db,
    IRewardQuoteService quotes,
    IAuditLogger audit,
    ICurrentUser currentUser,
    TimeProvider clock) : IRewardRulesService
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<IReadOnlyList<RewardRuleSetDto>> ListAsync(Guid campaignId, CancellationToken ct)
    {
        if (!await db.Set<Campaign>().AnyAsync(c => c.Id == campaignId, ct)) throw DomainException.NotFound("Campaign");
        var sets = await db.Set<RewardRuleSet>().AsNoTracking().Include(s => s.Rules)
            .Where(s => s.CampaignId == campaignId).OrderByDescending(s => s.Version).ToListAsync(ct);
        return await ToDtosAsync(sets, ct);
    }

    public async Task<IReadOnlyList<RewardRuleSetDto>> ToDtosAsync(IReadOnlyList<RewardRuleSet> sets, CancellationToken ct)
    {
        var ids = sets.Select(s => s.Id).ToList();
        var usage = await db.Set<Submission>().Where(s => ids.Contains(s.RewardRuleSetId))
            .GroupBy(s => s.RewardRuleSetId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var userIds = sets.Select(s => s.CreatedByUserId).Distinct().ToList();
        var users = await db.Set<User>().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
        var now = Now;
        var current = sets.Where(s => s.EffectiveFrom <= now).OrderByDescending(s => s.Version).FirstOrDefault()?.Id;
        return sets.Select(s => ToDto(s, usage.GetValueOrDefault(s.Id), users.TryGetValue(s.CreatedByUserId, out var n) ? new UserRefDto(s.CreatedByUserId, n) : null, s.Id == current)).ToList();
    }

    public static RewardRuleSetDto ToDto(RewardRuleSet s, int inUse, UserRefDto? createdBy, bool isCurrent) => new(
        s.Id, s.Version, s.Currency, s.DailyCapPerParticipant, s.WeeklyCapPerParticipant, s.CampaignCapPerParticipant,
        s.EffectiveFrom, s.CreatedAt, createdBy, s.ChangeReason, RewardEngine.Summarize(s), isCurrent, inUse,
        s.Rules.OrderBy(r => r.Type).ThenBy(r => r.Id).Select(RewardRuleDto.From).ToList());

    public async Task<RewardRuleSetDto> CreateVersionAsync(Guid campaignId, CreateRewardRuleSetRequest request, CancellationToken ct)
    {
        if (!request.Confirm)
            throw new DomainException("confirmation.required", "Confirm the reward change by sending \"confirm\": true.");

        await using var tx = await db.Dialect().BeginWriteTransactionAsync(db, ct, System.Data.IsolationLevel.ReadCommitted);
        await CampaignLock.LockAsync(db, campaignId, ct);
        var campaign = await db.Set<Campaign>().FirstOrDefaultAsync(c => c.Id == campaignId, ct)
            ?? throw DomainException.NotFound("Campaign");
        if (campaign.Status == CampaignStatus.Archived)
            throw DomainException.Conflict("campaign.archived", "Archived campaigns cannot be changed.");

        var previous = await db.Set<RewardRuleSet>().AsNoTracking().Include(s => s.Rules)
            .Where(s => s.CampaignId == campaignId).OrderByDescending(s => s.Version).FirstOrDefaultAsync(ct);
        // Checked under the campaign lock, so of two editors who opened the same version only the first one saves.
        if (request.BaseVersion is { } baseVersion && baseVersion != (previous?.Version ?? 0))
            throw DomainException.Conflict("reward.version_conflict",
                $"Someone else saved new reward rules (version {previous?.Version ?? 0}) after you opened version {baseVersion}. " +
                "Reload to see them, then make your change again.");
        var set = RewardRuleSetFactory.Build(request, campaignId, (previous?.Version ?? 0) + 1, Now, currentUser.Id, request.Reason);
        RewardEngine.EnsureValid(set);

        if (previous is not null && previous.Currency != set.Currency &&
            await db.Set<Submission>().AnyAsync(s => s.CampaignId == campaignId, ct))
            throw DomainException.Conflict("reward.currency_locked",
                $"This campaign already has submissions priced in {previous.Currency}; its reward currency cannot change.");
        if (campaign.BudgetAmount.HasValue && campaign.BudgetCurrency != set.Currency)
            throw new DomainException("campaign.budget_currency_mismatch",
                $"The campaign budget is in {campaign.BudgetCurrency}; reward rules must use the same currency.");
        if (!campaign.BudgetAmount.HasValue) campaign.BudgetCurrency = set.Currency;

        db.Set<RewardRuleSet>().Add(set);
        audit.Record("campaign.reward_rules_changed", nameof(Campaign), campaignId,
            before: previous is null ? null : RewardRuleSetFactory.Snapshot(previous),
            after: RewardRuleSetFactory.Snapshot(set), reason: request.Reason);
        try
        {
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (ProblemExceptionHandler.IsUniqueViolation(ex))
        {
            throw DomainException.Conflict("reward.version_conflict",
                "Someone else saved new reward rules at the same time. Reload and try again.");
        }

        var name = await db.Set<User>().Where(u => u.Id == set.CreatedByUserId).Select(u => u.DisplayName).FirstAsync(ct);
        return ToDto(set, 0, new UserRefDto(set.CreatedByUserId, name), true);
    }

    public async Task<RewardQuoteDto> PreviewAsync(Guid campaignId, RewardPreviewRequest request, CancellationToken ct)
    {
        var campaign = await db.Set<Campaign>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == campaignId, ct)
            ?? throw DomainException.NotFound("Campaign");

        RewardRuleSet set;
        Guid? setId = null;
        int? version = null;
        if (request.Draft is not null)
        {
            set = RewardRuleSetFactory.Build(request.Draft, campaignId, 0, Now, currentUser.Id, "preview");
        }
        else
        {
            var query = db.Set<RewardRuleSet>().AsNoTracking().Include(s => s.Rules).Where(s => s.CampaignId == campaignId);
            set = (request.RuleSetVersion is { } v
                      ? await query.FirstOrDefaultAsync(s => s.Version == v, ct)
                      : await quotes.GetCurrentRuleSetAsync(campaignId, Now, ct))
                  ?? throw new DomainException("reward.version_not_found", "That reward rule version does not exist.", DomainErrorKind.NotFound);
            setId = set.Id;
            version = set.Version;
        }

        var budget = request.CampaignBudgetRemaining;
        if (budget is null && campaign.BudgetAmount.HasValue && campaign.BudgetCurrency == Money.Normalize(set.Currency))
            budget = await quotes.BudgetRemainingAsync(campaign, ct);

        var context = new RewardContext
        {
            Platform = request.Platform,
            CountryCode = request.CountryCode.ToUpperInvariant(),
            Tier = request.Tier,
            PostedAtUtc = RewardRuleSetFactory.Utc(request.PostedAt) ?? Now,
            IsFirstApprovedPostInCampaign = request.IsFirstApprovedPost,
            EarnedTodayInCampaign = request.EarnedToday,
            EarnedThisWeekInCampaign = request.EarnedThisWeek,
            EarnedInCampaignTotal = request.EarnedInCampaign,
            CampaignBudgetRemaining = budget,
            QualityBonusRequested = request.QualityBonusRequested,
        };
        return RewardQuoteDto.From(RewardEngine.Quote(set, context), setId, version);
    }
}
