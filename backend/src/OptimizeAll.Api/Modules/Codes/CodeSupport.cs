using System.Globalization;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Ledger;
using OptimizeAll.Api.Modules.Rewards;
using OptimizeAll.Domain.Codes;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Ledger;
using OptimizeAll.Domain.Rewards;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Codes;

/// <summary>
/// Concurrency for discount-code programs: every write that changes a program's codes, assignments, sales or money takes
/// the program's named lock <b>before</b> its write transaction, then locks the program row inside it, so caps, budget,
/// code assignment and order-reference checks are evaluated serially per program (the unique indexes are the final guard).
/// </summary>
public static class CodeLocks
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);
    public const string ProgramsTable = "code_programs";

    public static string Program(Guid programId) => $"codes:program:{programId:N}";
}

/// <summary>The rate that applies to a person in a program (override or program/tier rules).</summary>
public sealed record ResolvedCodeOverride(CodeRate Rate, CodeRateSource Source, string Label, Guid OverrideId, Guid? GroupId);

/// <summary>A priced approval: the calculator result plus how the rate was chosen (for the ledger's rate-source fields).</summary>
public sealed record PricedCodeSale(CodePayoutResult Result, ResolvedCodeOverride? Override, int PayoutVersion)
{
    public EarningRateSource RateSource(CodeProgram program)
    {
        var level = Result.RateSource switch
        {
            CodeRateSource.PersonOverride => RateSourceLevel.CodePersonOverride,
            CodeRateSource.GroupOverride => RateSourceLevel.CodeGroupOverride,
            CodeRateSource.Tier => RateSourceLevel.CodeProgramTier,
            _ => RateSourceLevel.CodeProgramRules,
        };
        return new EarningRateSource(level, $"{program.Name} v{PayoutVersion} · {Result.RateLabel}", RateGroupId: Override?.GroupId);
    }
}

public interface ICodePayoutService
{
    Task<ResolvedCodeOverride?> ResolveOverrideAsync(Guid programId, Guid userId, CancellationToken ct);

    /// <summary>Prices an approval with the person's real counts, caps and the program budget (call under the program lock).</summary>
    Task<PricedCodeSale> PriceAsync(CodeProgram program, CodeSale sale, CancellationToken ct);

    /// <summary>The informational estimate for a sale (no caps).</summary>
    Task<decimal> EstimateAsync(CodeProgram program, Guid userId, decimal programNetAmount, Guid? excludeSaleId, CancellationToken ct);

    /// <summary>What the person earns per sale ("10% of net", "5.00 USD per sale").</summary>
    Task<string> DescribeRateForAsync(CodeProgram program, Guid userId, CancellationToken ct);

    /// <summary>Live (not reversed/declined) commission and tier-bonus earnings of a program.</summary>
    IQueryable<EarningEntry> LiveEarnings(Guid programId);
}

public sealed class CodePayoutService(AppDbContext db) : ICodePayoutService
{
    public static readonly EarningType[] CodeEarningTypes = { EarningType.SaleCommission, EarningType.SaleTierBonus };

    public static string CommissionKey(Guid saleId) => $"codesale:{saleId:N}:commission";

    public static string TierBonusKeyPrefix(Guid programId, Guid userId) => $"codetier:{programId:N}:{userId:N}:";

    public static string TierBonusKey(Guid programId, Guid userId, int threshold) => $"{TierBonusKeyPrefix(programId, userId)}{threshold}";

    public IQueryable<EarningEntry> LiveEarnings(Guid programId) =>
        db.Set<EarningEntry>().AsNoTracking().Where(e => e.CodeProgramId == programId &&
            (e.Type == EarningType.SaleCommission || e.Type == EarningType.SaleTierBonus) &&
            e.ReversedByEntryId == null && e.Status != EarningStatus.Reversed && e.Status != EarningStatus.Declined);

    public async Task<ResolvedCodeOverride?> ResolveOverrideAsync(Guid programId, Guid userId, CancellationToken ct)
    {
        var personal = await db.Set<CodePayoutOverride>().AsNoTracking()
            .Where(o => o.ProgramId == programId && o.UserId == userId && o.EndedAt == null)
            .OrderBy(o => o.CreatedAt).ThenBy(o => o.Id).FirstOrDefaultAsync(ct);
        if (personal is not null)
            return new ResolvedCodeOverride(new CodeRate(personal.PayoutType, personal.FlatAmount, personal.Percent), CodeRateSource.PersonOverride,
                "Personal rate", personal.Id, null);

        // Group overrides of the manual rate groups the person is in: the higher group priority wins, then the older override.
        var group = await (from o in db.Set<CodePayoutOverride>().AsNoTracking()
                           join g in db.Set<RateGroup>() on o.GroupId equals g.Id
                           join m in db.Set<RateGroupMember>() on g.Id equals m.GroupId
                           where o.ProgramId == programId && o.EndedAt == null && m.UserId == userId && g.ArchivedAt == null
                           orderby g.Priority descending, o.CreatedAt, o.Id
                           select new { o, g.Name }).FirstOrDefaultAsync(ct);
        return group is null
            ? null
            : new ResolvedCodeOverride(new CodeRate(group.o.PayoutType, group.o.FlatAmount, group.o.Percent), CodeRateSource.GroupOverride,
                $"Group '{group.Name}'", group.o.Id, group.o.GroupId);
    }

    public async Task<PricedCodeSale> PriceAsync(CodeProgram program, CodeSale sale, CancellationToken ct)
    {
        var over = await ResolveOverrideAsync(program.Id, sale.UserId, ct);
        var prior = await db.Set<CodeSale>().AsNoTracking()
            .CountAsync(s => s.ProgramId == program.Id && s.UserId == sale.UserId && s.Status == CodeSaleStatus.Approved && s.Id != sale.Id, ct);
        var earned = await TierBonusesEarnedAsync(program.Id, sale.UserId, ct);

        var day = sale.SubmittedAt.Date;
        var next = day.AddDays(1);
        var mine = from e in LiveEarnings(program.Id)
                   where e.UserId == sale.UserId
                   select e;
        var today = await (from e in mine
                           join s in db.Set<CodeSale>() on e.CodeSaleId equals s.Id
                           where s.SubmittedAt >= day && s.SubmittedAt < next
                           select e).SumAsync(e => (decimal?)e.Amount, ct) ?? 0m;
        var inProgram = await mine.SumAsync(e => (decimal?)e.Amount, ct) ?? 0m;
        var spent = program.BudgetAmount is null ? 0m : await LiveEarnings(program.Id).SumAsync(e => (decimal?)e.Amount, ct) ?? 0m;

        var result = CodePayoutCalculator.Calculate(new CodePayoutInput(
            program.Currency, sale.ProgramNetAmount, ProgramRate(program), Tiers(program), over?.Rate, over?.Source, over?.Label, prior, earned,
            program.DailyCapPerPerson, today, program.ProgramCapPerPerson, inProgram, program.BudgetAmount, spent));
        return new PricedCodeSale(result, over, program.PayoutVersion);
    }

    public async Task<decimal> EstimateAsync(CodeProgram program, Guid userId, decimal programNetAmount, Guid? excludeSaleId, CancellationToken ct)
    {
        var over = await ResolveOverrideAsync(program.Id, userId, ct);
        var prior = await db.Set<CodeSale>().AsNoTracking()
            .CountAsync(s => s.ProgramId == program.Id && s.UserId == userId && s.Status == CodeSaleStatus.Approved && s.Id != excludeSaleId, ct);
        var earned = await TierBonusesEarnedAsync(program.Id, userId, ct);
        return CodePayoutCalculator.Estimate(new CodePayoutInput(program.Currency, programNetAmount, ProgramRate(program), Tiers(program), over?.Rate,
            over?.Source, over?.Label, prior, earned, null, 0m, null, 0m, null, 0m));
    }

    public async Task<string> DescribeRateForAsync(CodeProgram program, Guid userId, CancellationToken ct)
    {
        var over = await ResolveOverrideAsync(program.Id, userId, ct);
        return (over?.Rate ?? ProgramRate(program)).Describe(program.Currency);
    }

    private async Task<IReadOnlySet<int>> TierBonusesEarnedAsync(Guid programId, Guid userId, CancellationToken ct)
    {
        var prefix = TierBonusKeyPrefix(programId, userId);
        var keys = await db.Set<EarningEntry>().AsNoTracking()
            .Where(e => e.CodeProgramId == programId && e.UserId == userId && e.Type == EarningType.SaleTierBonus && e.IdempotencyKey.StartsWith(prefix))
            .Select(e => e.IdempotencyKey).ToListAsync(ct);
        return keys.Select(k => int.TryParse(k[prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : -1)
            .Where(n => n > 0).ToHashSet();
    }

    public static CodeRate ProgramRate(CodeProgram p) => new(p.PayoutType, p.FlatAmount, p.Percent);

    public static IReadOnlyList<CodeTierRule> Tiers(CodeProgram p) =>
        p.Tiers.OrderBy(t => t.ThresholdSales).Select(t => new CodeTierRule(t.ThresholdSales, t.FlatAmount, t.Percent, t.BonusAmount)).ToList();

    /// <summary>"10% of net · after 10 sales: 12% of net, bonus 50.00 USD · daily cap 100.00 USD · budget 5,000.00 USD".</summary>
    public static string Summary(CodeProgram p)
    {
        var parts = new List<string> { ProgramRate(p).Describe(p.Currency) };
        foreach (var t in p.Tiers.OrderBy(t => t.ThresholdSales))
            parts.Add($"from {t.ThresholdSales} sales: {TierText(t, p.Currency)}");
        if (p.DailyCapPerPerson is { } d) parts.Add($"daily cap {Money.Format(d, p.Currency)}");
        if (p.ProgramCapPerPerson is { } c) parts.Add($"per-person cap {Money.Format(c, p.Currency)}");
        if (p.BudgetAmount is { } b) parts.Add($"budget {Money.Format(b, p.Currency)}");
        return string.Join(" · ", parts);
    }

    public static string TierText(CodeProgramTier t, string currency)
    {
        var bits = new List<string>();
        if (t.FlatAmount is { } f) bits.Add($"{Money.Format(f, currency)} per further sale");
        if (t.Percent is { } p) bits.Add($"{p.ToString("0.##", CultureInfo.InvariantCulture)}% of net on further sales");
        if (t.BonusAmount is { } b) bits.Add($"bonus {Money.Format(b, currency)}");
        return string.Join(", ", bits);
    }
}

internal static class CodeQueries
{
    public static async Task<Dictionary<Guid, (string Name, string Email)>> UserNamesAsync(this AppDbContext db, IEnumerable<Guid?> ids, CancellationToken ct)
    {
        var list = ids.Where(i => i.HasValue).Select(i => i!.Value).Distinct().ToList();
        if (list.Count == 0) return new();
        var result = new Dictionary<Guid, (string, string)>();
        foreach (var chunk in list.Chunk(500))
        {
            var c = chunk.ToList();
            foreach (var u in await db.Set<User>().AsNoTracking().Where(u => c.Contains(u.Id)).Select(u => new { u.Id, u.DisplayName, u.Email }).ToListAsync(ct))
                result[u.Id] = (u.DisplayName, u.Email);
        }
        return result;
    }

    public static UserRefDto? Ref(this Dictionary<Guid, (string Name, string Email)> names, Guid? id) =>
        id is { } v ? new UserRefDto(v, names.TryGetValue(v, out var n) ? n.Name : "Unknown user") : null;

    public static async Task<Dictionary<Guid, string>> GroupNamesAsync(this AppDbContext db, IEnumerable<Guid?> ids, CancellationToken ct)
    {
        var list = ids.Where(i => i.HasValue).Select(i => i!.Value).Distinct().ToList();
        if (list.Count == 0) return new();
        return await db.Set<RateGroup>().AsNoTracking().Where(g => list.Contains(g.Id)).ToDictionaryAsync(g => g.Id, g => g.Name, ct);
    }

    public static string? Trimmed(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    public static string Fit(string s, int max) => s.Length > max ? s[..max] : s;
}
