using System.Data;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Common.Ledger;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Modules.Accounts;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.Campaigns;
using OptimizeAll.Domain.Codes;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Ledger;
using OptimizeAll.Domain.Marketing;
using OptimizeAll.Domain.Rewards;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Codes;

public interface ICodeProgramsService
{
    Task<PagedResult<CodeProgramListItemDto>> ListAsync(CodeProgramQuery query, CancellationToken ct);
    Task<CodeProgramDto> GetAsync(Guid id, CancellationToken ct);
    Task<CodeProgramDto> CreateAsync(CreateCodeProgramRequest request, CancellationToken ct);
    Task<CodeProgramDto> UpdateAsync(Guid id, UpdateCodeProgramRequest request, CancellationToken ct);
    Task<CodeProgramDto> UpdatePayoutAsync(Guid id, UpdatePayoutRulesRequest request, CancellationToken ct);
    Task<CodeProgramDto> ChangeStatusAsync(Guid id, ChangeProgramStatusRequest request, CancellationToken ct);
    Task<CodeProgramDto> CreateOverrideAsync(Guid id, CreatePayoutOverrideRequest request, CancellationToken ct);
    Task<CodeProgramDto> EndOverrideAsync(Guid id, Guid overrideId, EndPayoutOverrideRequest request, CancellationToken ct);
}

/// <summary>Discount-code programs: brand, window, terms and payout rules (docs/DISCOUNT_CODES.md).</summary>
public sealed class CodeProgramsService(
    AppDbContext db,
    IAuditLogger audit,
    ICurrentUser currentUser,
    ICodePayoutService payouts,
    IExchangeRateProvider fx,
    TimeProvider clock) : ICodeProgramsService
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<PagedResult<CodeProgramListItemDto>> ListAsync(CodeProgramQuery query, CancellationToken ct)
    {
        var q = db.Set<CodeProgram>().AsNoTracking().Include(p => p.Tiers).AsQueryable();
        if (query.Status is { } status) q = q.Where(p => p.Status == status);
        else q = q.Where(p => p.Status != CodeProgramStatus.Archived);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var like = PagingExtensions.LikePattern(query.Search);
            q = q.Where(p => EF.Functions.Like(p.Name, like, "\\") || EF.Functions.Like(p.BrandName, like, "\\"));
        }
        var total = await q.CountAsync(ct);
        var page = await q.OrderByDescending(p => p.CreatedAt).ThenBy(p => p.Id).Skip(query.Skip).Take(query.PageSize).ToListAsync(ct);
        var ids = page.Select(p => p.Id).ToList();
        var codes = await db.Set<DiscountCode>().AsNoTracking().Where(c => ids.Contains(c.ProgramId))
            .GroupBy(c => new { c.ProgramId, c.Status }).Select(g => new { g.Key.ProgramId, g.Key.Status, Count = g.Count() }).ToListAsync(ct);
        var sales = await db.Set<CodeSale>().AsNoTracking().Where(s => ids.Contains(s.ProgramId))
            .GroupBy(s => new { s.ProgramId, s.Status }).Select(g => new { g.Key.ProgramId, g.Key.Status, Count = g.Count() }).ToListAsync(ct);
        var approved = await db.Set<EarningEntry>().AsNoTracking()
            .Where(e => e.CodeProgramId != null && ids.Contains(e.CodeProgramId.Value) &&
                        (e.Type == EarningType.SaleCommission || e.Type == EarningType.SaleTierBonus) &&
                        e.ReversedByEntryId == null && e.Status != EarningStatus.Reversed && e.Status != EarningStatus.Declined)
            .GroupBy(e => e.CodeProgramId!.Value).Select(g => new { g.Key, Sum = g.Sum(e => e.Amount) }).ToListAsync(ct);
        return new PagedResult<CodeProgramListItemDto>(page.Select(p => new CodeProgramListItemDto(
            p.Id, p.Name, p.BrandName, p.Status, p.Currency, p.StartsAt, p.EndsAt, CodePayoutService.Summary(p),
            codes.Where(c => c.ProgramId == p.Id).Sum(c => c.Count),
            codes.Where(c => c.ProgramId == p.Id && c.Status == DiscountCodeStatus.Assigned).Sum(c => c.Count),
            sales.Where(s => s.ProgramId == p.Id && (s.Status == CodeSaleStatus.Pending || s.Status == CodeSaleStatus.NeedsInfo)).Sum(s => s.Count),
            sales.Where(s => s.ProgramId == p.Id && s.Status == CodeSaleStatus.Approved).Sum(s => s.Count),
            approved.FirstOrDefault(a => a.Key == p.Id)?.Sum ?? 0m, p.UpdatedAt)).ToList(), total, query.Page, query.PageSize);
    }

    public async Task<CodeProgramDto> GetAsync(Guid id, CancellationToken ct)
    {
        var p = await db.Set<CodeProgram>().AsNoTracking().Include(x => x.Tiers).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw NotFound();
        var now = Now;
        var client = p.ClientAccountId is { } cid
            ? await db.Set<ClientAccount>().AsNoTracking().Where(c => c.Id == cid).Select(c => new NamedRefDto(c.Id, c.Name)).FirstOrDefaultAsync(ct)
            : null;
        var campaign = p.CampaignId is { } campId
            ? await db.Set<Campaign>().AsNoTracking().Where(c => c.Id == campId).Select(c => new NamedRefDto(c.Id, c.Title)).FirstOrDefaultAsync(ct)
            : null;
        var overrides = await db.Set<CodePayoutOverride>().AsNoTracking().Where(o => o.ProgramId == id)
            .OrderBy(o => o.EndedAt != null).ThenByDescending(o => o.CreatedAt).ThenBy(o => o.Id).Take(500).ToListAsync(ct);
        var names = await db.UserNamesAsync(overrides.Select(o => o.UserId).Concat(overrides.Select(o => (Guid?)o.CreatedByUserId)), ct);
        var groups = await db.GroupNamesAsync(overrides.Select(o => o.GroupId), ct);

        var codeCounts = await db.Set<DiscountCode>().AsNoTracking().Where(c => c.ProgramId == id)
            .GroupBy(c => c.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        var expired = await db.Set<DiscountCode>().AsNoTracking().CountAsync(c => c.ProgramId == id && c.Status != DiscountCodeStatus.Retired &&
            ((c.ValidTo != null && c.ValidTo < now) || (c.ValidTo == null && p.EndsAt != null && p.EndsAt < now)), ct);
        var saleCounts = await db.Set<CodeSale>().AsNoTracking().Where(s => s.ProgramId == id)
            .GroupBy(s => s.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        var live = payouts.LiveEarnings(id);
        var approved = await live.SumAsync(e => (decimal?)e.Amount, ct) ?? 0m;
        var paid = await live.Where(e => e.Status == EarningStatus.Paid).SumAsync(e => (decimal?)e.Amount, ct) ?? 0m;
        var stats = new CodeProgramStatsDto(
            codeCounts.Sum(c => c.Count),
            Math.Max(0, codeCounts.Where(c => c.Key == DiscountCodeStatus.Available).Sum(c => c.Count) - expired),
            codeCounts.Where(c => c.Key == DiscountCodeStatus.Assigned).Sum(c => c.Count),
            saleCounts.Where(s => s.Key is CodeSaleStatus.Pending or CodeSaleStatus.NeedsInfo).Sum(s => s.Count),
            saleCounts.Where(s => s.Key == CodeSaleStatus.Approved).Sum(s => s.Count),
            approved, paid, p.BudgetAmount is { } b ? Math.Max(0m, b - approved) : null);

        return new CodeProgramDto(p.Id, p.Name, p.BrandName, client, campaign, p.Description, p.Terms, p.StoreUrl, p.DiscountLabel, p.Currency,
            p.StartsAt, p.EndsAt, p.Status, p.PayoutType, p.FlatAmount, p.Percent,
            p.Tiers.OrderBy(t => t.ThresholdSales).Select(t => new CodeTierDto(t.ThresholdSales, t.FlatAmount, t.Percent, t.BonusAmount)).ToList(),
            p.DailyCapPerPerson, p.ProgramCapPerPerson, p.BudgetAmount, p.MaxOrderAgeDays, p.RequireProof, p.PayoutVersion, CodePayoutService.Summary(p),
            overrides.Select(o => ToDto(o, p.Currency, names, groups)).ToList(), stats, p.CreatedAt, p.UpdatedAt, p.ConcurrencyStamp);
    }

    public async Task<CodeProgramDto> CreateAsync(CreateCodeProgramRequest request, CancellationToken ct)
    {
        var program = new CodeProgram { CreatedByUserId = currentUser.Id };
        await ApplyAsync(program, request, hasSales: false, ct);
        ApplyPayout(program, request.Payout!);
        program.Status = request.Activate ? CodeProgramStatus.Active : CodeProgramStatus.Draft;
        await EnsureFxAsync(program.Currency, ct);
        db.Set<CodeProgram>().Add(program);
        audit.Record("code_program.created", nameof(CodeProgram), program.Id, after: Snapshot(program));
        await db.SaveChangesAsync(ct);
        return await GetAsync(program.Id, ct);
    }

    public async Task<CodeProgramDto> UpdateAsync(Guid id, UpdateCodeProgramRequest request, CancellationToken ct)
    {
        await using (await db.Dialect().AcquireNamedLockAsync(db, CodeLocks.Program(id), CodeLocks.Timeout, ct))
        await using (var tx = await db.Dialect().BeginWriteTransactionAsync(db, ct, IsolationLevel.ReadCommitted))
        {
            await db.Dialect().LockRowAsync(db, CodeLocks.ProgramsTable, id, ct);
            var program = await db.Set<CodeProgram>().Include(p => p.Tiers).FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw NotFound();
            ConcurrencyGuard.Apply(db, program, request.ConcurrencyStamp!.Value);
            if (program.Status == CodeProgramStatus.Archived) throw Archived();
            var before = Snapshot(program);
            var hasSales = await db.Set<CodeSale>().AnyAsync(s => s.ProgramId == id, ct);
            await ApplyAsync(program, request, hasSales, ct);
            await EnsureFxAsync(program.Currency, ct);
            audit.Record("code_program.updated", nameof(CodeProgram), id, before, Snapshot(program));
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        return await GetAsync(id, ct);
    }

    public async Task<CodeProgramDto> UpdatePayoutAsync(Guid id, UpdatePayoutRulesRequest request, CancellationToken ct)
    {
        if (!request.Confirm) throw new DomainException("confirmation.required", "Confirm the payout change by sending \"confirm\": true.");
        await using (await db.Dialect().AcquireNamedLockAsync(db, CodeLocks.Program(id), CodeLocks.Timeout, ct))
        await using (var tx = await db.Dialect().BeginWriteTransactionAsync(db, ct, IsolationLevel.ReadCommitted))
        {
            await db.Dialect().LockRowAsync(db, CodeLocks.ProgramsTable, id, ct);
            var program = await db.Set<CodeProgram>().Include(p => p.Tiers).FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw NotFound();
            ConcurrencyGuard.Apply(db, program, request.ConcurrencyStamp!.Value);
            if (program.Status == CodeProgramStatus.Archived) throw Archived();
            var before = PayoutSnapshot(program);
            db.Set<CodeProgramTier>().RemoveRange(program.Tiers);
            program.Tiers.Clear();
            ApplyPayout(program, request);
            program.PayoutVersion++;
            audit.Record("code_program.payout_changed", nameof(CodeProgram), id, before, PayoutSnapshot(program), request.Reason.Trim());
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        return await GetAsync(id, ct);
    }

    public async Task<CodeProgramDto> ChangeStatusAsync(Guid id, ChangeProgramStatusRequest request, CancellationToken ct)
    {
        var target = request.Status!.Value;
        if (target == CodeProgramStatus.Draft)
            throw new DomainException("code_program.invalid_status", "A program can't go back to draft; pause it instead.");
        await using (await db.Dialect().AcquireNamedLockAsync(db, CodeLocks.Program(id), CodeLocks.Timeout, ct))
        await using (var tx = await db.Dialect().BeginWriteTransactionAsync(db, ct, IsolationLevel.ReadCommitted))
        {
            await db.Dialect().LockRowAsync(db, CodeLocks.ProgramsTable, id, ct);
            var program = await db.Set<CodeProgram>().FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw NotFound();
            ConcurrencyGuard.Apply(db, program, request.ConcurrencyStamp!.Value);
            if (program.Status == CodeProgramStatus.Archived) throw Archived();
            if (program.Status == target) throw DomainException.Conflict("code_program.status_unchanged", $"The program is already {target}.");
            if (target == CodeProgramStatus.Archived && await db.Set<CodeSale>().AnyAsync(s => s.ProgramId == id &&
                    (s.Status == CodeSaleStatus.Pending || s.Status == CodeSaleStatus.NeedsInfo), ct))
                throw DomainException.Conflict("code_program.has_pending_sales", "Decide the program's pending sales before archiving it.");
            var from = program.Status;
            program.Status = target;
            if (target == CodeProgramStatus.Archived) program.ArchivedAt = Now;
            audit.Record("code_program.status_changed", nameof(CodeProgram), id, new { Status = from.ToString() }, new { Status = target.ToString() },
                CodeQueries.Trimmed(request.Reason));
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        return await GetAsync(id, ct);
    }

    public async Task<CodeProgramDto> CreateOverrideAsync(Guid id, CreatePayoutOverrideRequest request, CancellationToken ct)
    {
        var target = request.Target!.Value;
        if ((target == CodeAssignmentTarget.Person) != (request.UserId is not null) || (target == CodeAssignmentTarget.Group) != (request.GroupId is not null))
            throw new DomainException("code_override.invalid_target", "A person override needs userId; a group override needs groupId.");
        var errors = CodePayoutCalculator.ValidateRate(request.PayoutType!.Value, request.FlatAmount, request.Percent);
        if (errors.Count > 0) throw new DomainException("code_override.invalid", "Check the payout.", errors: errors);
        await using (await db.Dialect().AcquireNamedLockAsync(db, CodeLocks.Program(id), CodeLocks.Timeout, ct))
        await using (var tx = await db.Dialect().BeginWriteTransactionAsync(db, ct, IsolationLevel.ReadCommitted))
        {
            await db.Dialect().LockRowAsync(db, CodeLocks.ProgramsTable, id, ct);
            var program = await db.Set<CodeProgram>().FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw NotFound();
            if (program.Status == CodeProgramStatus.Archived) throw Archived();
            if (request.UserId is { } uid)
                await CodeTargets.EnsureParticipantAsync(db, uid, ct);
            if (request.GroupId is { } gid)
                await CodeTargets.EnsureManualGroupAsync(db, gid, ct);
            if (await db.Set<CodePayoutOverride>().AnyAsync(o => o.ProgramId == id && o.EndedAt == null &&
                    ((request.UserId != null && o.UserId == request.UserId) || (request.GroupId != null && o.GroupId == request.GroupId)), ct))
                throw DomainException.Conflict("code_override.duplicate", "This person or group already has a payout override in the program. End it first.");
            var o = new CodePayoutOverride
            {
                ProgramId = id, Target = target, UserId = request.UserId, GroupId = request.GroupId, PayoutType = request.PayoutType!.Value,
                FlatAmount = request.FlatAmount, Percent = request.Percent, Reason = request.Reason.Trim(), CreatedAt = Now, CreatedByUserId = currentUser.Id,
            };
            db.Set<CodePayoutOverride>().Add(o);
            ConcurrencyGuard.Touch(db, program);
            audit.Record("code_program.override_created", nameof(CodeProgram), id, after: new
            {
                OverrideId = o.Id, Target = target.ToString(), o.UserId, o.GroupId, PayoutType = o.PayoutType.ToString(), o.FlatAmount, o.Percent,
            }, reason: o.Reason);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        return await GetAsync(id, ct);
    }

    public async Task<CodeProgramDto> EndOverrideAsync(Guid id, Guid overrideId, EndPayoutOverrideRequest request, CancellationToken ct)
    {
        await using (await db.Dialect().AcquireNamedLockAsync(db, CodeLocks.Program(id), CodeLocks.Timeout, ct))
        await using (var tx = await db.Dialect().BeginWriteTransactionAsync(db, ct, IsolationLevel.ReadCommitted))
        {
            await db.Dialect().LockRowAsync(db, CodeLocks.ProgramsTable, id, ct);
            var program = await db.Set<CodeProgram>().FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw NotFound();
            var o = await db.Set<CodePayoutOverride>().FirstOrDefaultAsync(x => x.Id == overrideId && x.ProgramId == id, ct)
                    ?? throw DomainException.NotFound("CodePayoutOverride");
            if (o.EndedAt is not null) throw DomainException.Conflict("code_override.ended", "This override has already ended.");
            o.EndedAt = Now;
            o.EndedByUserId = currentUser.Id;
            o.EndReason = request.Reason.Trim();
            ConcurrencyGuard.Touch(db, program);
            audit.Record("code_program.override_ended", nameof(CodeProgram), id, new { OverrideId = o.Id, Active = true }, new { OverrideId = o.Id, Active = false }, o.EndReason);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        return await GetAsync(id, ct);
    }

    // ------------------------------------------------------------------ helpers

    private async Task ApplyAsync(CodeProgram p, CodeProgramInput input, bool hasSales, CancellationToken ct)
    {
        var currency = Money.Normalize(input.Currency);
        if (!Money.IsSupported(currency))
            throw new DomainException("code_program.currency_unsupported", $"Currency {currency} is not supported.",
                errors: new Dictionary<string, string[]> { ["currency"] = new[] { "Choose a supported currency." } });
        if (hasSales && currency != p.Currency)
            throw DomainException.Conflict("code_program.currency_locked", "The program already has sales, so its currency can't change.");
        var starts = DateTime.SpecifyKind(input.StartsAt!.Value.ToUniversalTime(), DateTimeKind.Utc);
        DateTime? ends = input.EndsAt is { } e ? DateTime.SpecifyKind(e.ToUniversalTime(), DateTimeKind.Utc) : null;
        if (ends is { } end && end <= starts)
            throw new DomainException("code_program.invalid_window", "The end must be after the start.",
                errors: new Dictionary<string, string[]> { ["endsAt"] = new[] { "Must be after the start." } });
        var store = CodeQueries.Trimmed(input.StoreUrl);
        if (store is not null && !TrackingUrl.IsValidDestination(store))
            throw new DomainException("code_program.invalid_store_url", "The store URL must be an absolute http(s) address.",
                errors: new Dictionary<string, string[]> { ["storeUrl"] = new[] { "Use an https:// address." } });
        if (input.CampaignId is { } campaignId && !await db.Set<Campaign>().AnyAsync(c => c.Id == campaignId, ct))
            throw new DomainException("code_program.campaign_not_found", "The campaign was not found.",
                errors: new Dictionary<string, string[]> { ["campaignId"] = new[] { "Unknown campaign." } });
        if (input.ClientAccountId is { } clientId && !await db.Set<ClientAccount>().AnyAsync(c => c.Id == clientId, ct))
            throw new DomainException("code_program.client_not_found", "The client was not found.",
                errors: new Dictionary<string, string[]> { ["clientAccountId"] = new[] { "Unknown client." } });
        p.Name = input.Name.Trim();
        p.BrandName = input.BrandName.Trim();
        p.ClientAccountId = input.ClientAccountId;
        p.CampaignId = input.CampaignId;
        p.Description = CodeQueries.Trimmed(input.Description);
        p.Terms = CodeQueries.Trimmed(input.Terms);
        p.StoreUrl = store;
        p.DiscountLabel = CodeQueries.Trimmed(input.DiscountLabel);
        p.Currency = currency;
        p.StartsAt = starts;
        p.EndsAt = ends;
        p.MaxOrderAgeDays = input.MaxOrderAgeDays;
        p.RequireProof = input.RequireProof;
    }

    private static void ApplyPayout(CodeProgram p, CodePayoutRulesInput input)
    {
        var type = input.PayoutType!.Value;
        var errors = CodePayoutCalculator.ValidateRate(type, input.FlatAmount, input.Percent);
        var tiers = input.Tiers.Select(t => new CodeTierRule(t.ThresholdSales, t.FlatAmount, t.Percent, t.BonusAmount)).ToList();
        foreach (var (k, v) in CodePayoutCalculator.ValidateTiers(tiers)) errors[k] = v;
        if (errors.Count > 0) throw new DomainException("code_program.invalid_payout", "Check the payout rules.", errors: errors);
        p.PayoutType = type;
        p.FlatAmount = type == CodePayoutType.FlatPerSale ? Money.Round(input.FlatAmount!.Value, p.Currency) : null;
        p.Percent = type == CodePayoutType.PercentOfNet ? input.Percent : null;
        p.DailyCapPerPerson = input.DailyCapPerPerson is { } d ? Money.Round(d, p.Currency) : null;
        p.ProgramCapPerPerson = input.ProgramCapPerPerson is { } c ? Money.Round(c, p.Currency) : null;
        p.BudgetAmount = input.BudgetAmount is { } b ? Money.Round(b, p.Currency) : null;
        foreach (var t in tiers.OrderBy(t => t.ThresholdSales))
            p.Tiers.Add(new CodeProgramTier
            {
                ProgramId = p.Id, ThresholdSales = t.ThresholdSales,
                FlatAmount = t.FlatAmount is { } f ? Money.Round(f, p.Currency) : null, Percent = t.Percent,
                BonusAmount = t.BonusAmount is { } bo ? Money.Round(bo, p.Currency) : null,
            });
    }

    /// <summary>Commissions are converted to the settlement currency by the ledger: refuse a program nobody could be paid for.</summary>
    private async Task EnsureFxAsync(string currency, CancellationToken ct)
    {
        try
        {
            var schedule = await new PayoutScheduleProvider(db).GetActiveAsync(Now, ct);
            await fx.GetRateAsync(currency, schedule.SettlementCurrency, Now, ct);
        }
        catch (DomainException ex) when (ex.Code == "fx.rate_missing")
        {
            throw DomainException.Conflict("fx.rate_missing", $"{ex.Message} Commissions in {currency} could not be paid out without it.");
        }
    }

    private static object Snapshot(CodeProgram p) => new
    {
        p.Name, p.BrandName, p.ClientAccountId, p.CampaignId, p.StoreUrl, p.Currency, p.StartsAt, p.EndsAt, Status = p.Status.ToString(),
        p.MaxOrderAgeDays, p.RequireProof, Payout = PayoutSnapshot(p),
    };

    private static object PayoutSnapshot(CodeProgram p) => new
    {
        PayoutType = p.PayoutType.ToString(), p.FlatAmount, p.Percent, p.DailyCapPerPerson, p.ProgramCapPerPerson, p.BudgetAmount, p.PayoutVersion,
        Tiers = p.Tiers.OrderBy(t => t.ThresholdSales).Select(t => new { t.ThresholdSales, t.FlatAmount, t.Percent, t.BonusAmount }),
    };

    internal static CodePayoutOverrideDto ToDto(CodePayoutOverride o, string currency, Dictionary<Guid, (string Name, string Email)> names,
        Dictionary<Guid, string> groups) => new(
        o.Id, o.Target, names.Ref(o.UserId), o.GroupId is { } g ? new NamedRefDto(g, groups.GetValueOrDefault(g, "Unknown group")) : null,
        o.PayoutType, o.FlatAmount, o.Percent, new CodeRate(o.PayoutType, o.FlatAmount, o.Percent).Describe(currency), o.Reason, o.CreatedAt,
        names.Ref(o.CreatedByUserId), o.EndedAt, o.EndReason, o.EndedAt is null);

    private static DomainException NotFound() => DomainException.NotFound("CodeProgram");

    internal static DomainException Archived() => DomainException.Conflict("code_program.archived", "This program is archived.");
}

/// <summary>Validation of assignment / override targets (participants and manual rate groups).</summary>
internal static class CodeTargets
{
    public static async Task EnsureParticipantAsync(AppDbContext db, Guid userId, CancellationToken ct)
    {
        var u = await db.Set<User>().AsNoTracking().Where(x => x.Id == userId)
            .Select(x => new { x.Status, IsParticipant = x.Roles.Any(r => r.Role == Role.Participant) }).FirstOrDefaultAsync(ct);
        if (u is null) throw new DomainException("user.not_found", "No account with this id.", DomainErrorKind.NotFound);
        if (!u.IsParticipant) throw new DomainException("codes.not_participant", "Only participants can hold discount codes.");
        if (u.Status == UserStatus.Deactivated) throw DomainException.Conflict("codes.user_deactivated", "The account is deactivated.");
    }

    public static async Task<RateGroup> EnsureManualGroupAsync(AppDbContext db, Guid groupId, CancellationToken ct)
    {
        var g = await db.Set<RateGroup>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == groupId, ct)
                ?? throw DomainException.NotFound("RateGroup");
        if (g.ArchivedAt is not null) throw DomainException.Conflict("rate_group.archived", "This rate group is archived.");
        if (g.MembershipMode != RateGroupMembershipMode.Manual)
            throw DomainException.Conflict("codes.group_automatic", "Codes and overrides need a manual rate group (automatic groups have no fixed members).");
        return g;
    }
}
