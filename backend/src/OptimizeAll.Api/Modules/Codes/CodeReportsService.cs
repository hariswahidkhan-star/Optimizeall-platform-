using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Domain.Codes;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Ledger;
using OptimizeAll.Domain.Marketing;
using OptimizeAll.Domain.Rewards;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Codes;

public interface ICodeReportsService
{
    Task<CodeReportDto> ReportAsync(CodeReportQuery query, CancellationToken ct);
    Task<(string FileName, IReadOnlyList<string> Header, IReadOnlyList<object?[]> Rows)> ExportAsync(CodeReportQuery query, CancellationToken ct);
}

/// <summary>
/// Uses, gross/net sales, discount given and commissions (pending estimate, approved unpaid, paid, reversed) per program,
/// code, person or rate group; clicks and conversion from the participants' tracking links when the program is linked to a
/// campaign. Money is in the program currency.
/// </summary>
public sealed class CodeReportsService(AppDbContext db, IOptions<ExportOptions> exports, TimeProvider clock) : ICodeReportsService
{
    private const int MaxSales = 200_000;

    private sealed record SaleRow(Guid Id, Guid ProgramId, Guid CodeId, Guid UserId, Guid? GroupId, CodeSaleStatus Status, decimal Net, decimal Discount,
        decimal? Estimate);

    private sealed record EarningRow(Guid SaleId, EarningType Type, EarningStatus Status, decimal Amount, bool Reversed);

    public async Task<CodeReportDto> ReportAsync(CodeReportQuery query, CancellationToken ct)
    {
        CodeProgram? program = null;
        if (query.ProgramId is { } pid)
            program = await db.Set<CodeProgram>().AsNoTracking().FirstOrDefaultAsync(p => p.Id == pid, ct) ?? throw DomainException.NotFound("CodeProgram");
        else if (query.GroupBy != CodeReportGrouping.Program)
            throw new DomainException("code_report.program_required", "Choose a program (amounts are in each program's currency).");
        if (query.From is { } f && query.To is { } t && t < f)
            throw new DomainException("code_report.invalid_range", "The end date is before the start date.");

        var q = SalesOf(program, query);
        var sales = await q.OrderBy(s => s.Id).Take(MaxSales)
            .Select(s => new SaleRow(s.Id, s.ProgramId, s.CodeId, s.UserId, s.GroupId, s.Status, s.ProgramNetAmount, s.ProgramDiscountAmount, s.EstimatedCommission))
            .ToListAsync(ct);

        var saleIds = sales.Select(s => s.Id).ToList();
        var earnings = new List<EarningRow>();
        foreach (var chunk in saleIds.Chunk(1000))
        {
            var list = chunk.ToList();
            earnings.AddRange(await db.Set<EarningEntry>().AsNoTracking()
                .Where(e => e.CodeSaleId != null && list.Contains(e.CodeSaleId.Value))
                .Select(e => new EarningRow(e.CodeSaleId!.Value, e.Type, e.Status, e.Amount, e.ReversedByEntryId != null)).ToListAsync(ct));
        }
        var bySale = earnings.GroupBy(e => e.SaleId).ToDictionary(g => g.Key, g => g.ToList());

        var programs = await db.Set<CodeProgram>().AsNoTracking().Where(p => program == null || p.Id == program.Id)
            .Select(p => new { p.Id, p.Name, p.BrandName, p.Currency, p.CampaignId }).ToDictionaryAsync(p => p.Id, ct);

        // Keys and labels per grouping.
        Func<SaleRow, Guid?> key;
        Dictionary<Guid, (string Label, string? Detail)> labels;
        switch (query.GroupBy)
        {
            case CodeReportGrouping.Program:
                key = s => s.ProgramId;
                labels = programs.Values.ToDictionary(p => p.Id, p => (p.Name, (string?)$"{p.BrandName} · {p.Currency}"));
                break;
            case CodeReportGrouping.Code:
            {
                key = s => s.CodeId;
                var ids = sales.Select(s => s.CodeId).Distinct().ToList();
                labels = new();
                foreach (var chunk in ids.Chunk(1000))
                {
                    var list = chunk.ToList();
                    foreach (var c in await db.Set<DiscountCode>().AsNoTracking().Where(c => list.Contains(c.Id)).Select(c => new { c.Id, c.Code }).ToListAsync(ct))
                        labels[c.Id] = (c.Code, null);
                }
                break;
            }
            case CodeReportGrouping.Person:
            {
                key = s => s.UserId;
                var names = await db.UserNamesAsync(sales.Select(s => (Guid?)s.UserId), ct);
                labels = names.ToDictionary(n => n.Key, n => (n.Value.Name, (string?)n.Value.Email));
                break;
            }
            default:
            {
                // Shared-code sales belong to their group; personal-code sales to the person's highest-priority manual group.
                var users = sales.Where(s => s.GroupId is null).Select(s => s.UserId).Distinct().ToList();
                var primary = new Dictionary<Guid, Guid>();
                foreach (var chunk in users.Chunk(1000))
                {
                    var list = chunk.ToList();
                    var rows = await (from m in db.Set<RateGroupMember>().AsNoTracking()
                                      join g in db.Set<RateGroup>() on m.GroupId equals g.Id
                                      where list.Contains(m.UserId) && g.ArchivedAt == null
                                      select new { m.UserId, m.GroupId, g.Priority }).ToListAsync(ct);
                    foreach (var r in rows.GroupBy(r => r.UserId))
                        primary[r.Key] = r.OrderByDescending(x => x.Priority).ThenBy(x => x.GroupId).First().GroupId;
                }
                key = s => s.GroupId ?? (primary.TryGetValue(s.UserId, out var g) ? g : null);
                var groupNames = await db.GroupNamesAsync(sales.Select(s => s.GroupId).Concat(primary.Values.Select(v => (Guid?)v)), ct);
                labels = groupNames.ToDictionary(g => g.Key, g => (g.Value, (string?)null));
                break;
            }
        }

        // Clicks from the participants' tracking links of the program's campaign.
        Dictionary<Guid, int>? clicksByUser = null;
        var totalClicks = (int?)null;
        var campaignIds = programs.Values.Where(p => p.CampaignId != null).Select(p => p.CampaignId!.Value).Distinct().ToList();
        if (campaignIds.Count > 0 && query.GroupBy is CodeReportGrouping.Person or CodeReportGrouping.Program)
        {
            var clicks = await (from l in db.Set<TrackingLink>().AsNoTracking()
                                join c in db.Set<TrackingClick>() on l.Id equals c.TrackingLinkId
                                where campaignIds.Contains(l.CampaignId) && l.UserId != null && c.IsUnique && !c.IsSuspectedBot
                                group c by new { l.CampaignId, UserId = l.UserId!.Value } into g
                                select new { g.Key.CampaignId, g.Key.UserId, Count = g.Count() }).ToListAsync(ct);
            clicksByUser = clicks.GroupBy(c => c.UserId).ToDictionary(g => g.Key, g => g.Sum(x => x.Count));
            totalClicks = clicks.Sum(c => c.Count);
        }

        CodeReportRowDto Row(Guid? id, string label, string? detail, IReadOnlyList<SaleRow> rows, int? clicks)
        {
            var e = rows.SelectMany(s => bySale.GetValueOrDefault(s.Id) ?? new List<EarningRow>()).ToList();
            var credit = e.Where(x => x.Type is EarningType.SaleCommission or EarningType.SaleTierBonus).ToList();
            var live = credit.Where(x => !x.Reversed && x.Status is not (EarningStatus.Reversed or EarningStatus.Declined)).ToList();
            var uses = rows.Count(s => s.Status is CodeSaleStatus.Pending or CodeSaleStatus.NeedsInfo or CodeSaleStatus.Approved or CodeSaleStatus.Refunded);
            var counted = rows.Where(s => s.Status is CodeSaleStatus.Pending or CodeSaleStatus.NeedsInfo or CodeSaleStatus.Approved).ToList();
            return new CodeReportRowDto(id, label, detail, uses,
                rows.Count(s => s.Status is CodeSaleStatus.Pending or CodeSaleStatus.NeedsInfo),
                rows.Count(s => s.Status == CodeSaleStatus.Approved),
                rows.Count(s => s.Status == CodeSaleStatus.Rejected),
                rows.Count(s => s.Status is CodeSaleStatus.Refunded or CodeSaleStatus.Cancelled),
                counted.Sum(s => s.Net + s.Discount), counted.Sum(s => s.Discount), counted.Sum(s => s.Net),
                rows.Where(s => s.Status is CodeSaleStatus.Pending or CodeSaleStatus.NeedsInfo).Sum(s => s.Estimate ?? 0m),
                live.Where(x => x.Status != EarningStatus.Paid).Sum(x => x.Amount),
                live.Where(x => x.Status == EarningStatus.Paid).Sum(x => x.Amount),
                credit.Where(x => x.Reversed || x.Status == EarningStatus.Reversed).Sum(x => x.Amount),
                clicks, clicks is > 0 ? Math.Round((decimal)counted.Count / clicks.Value * 100m, 2) : null);
        }

        var result = sales.GroupBy(key).Select(g =>
            {
                var (label, detail) = g.Key is { } k && labels.TryGetValue(k, out var l) ? l : (query.GroupBy == CodeReportGrouping.Group ? "No group" : "Unknown", null);
                int? clicks = query.GroupBy == CodeReportGrouping.Person && clicksByUser is not null && g.Key is { } u ? clicksByUser.GetValueOrDefault(u) : null;
                return Row(g.Key, label, detail, g.ToList(), clicks);
            })
            .OrderByDescending(r => r.CommissionApproved + r.CommissionPaid).ThenByDescending(r => r.Uses).ThenBy(r => r.Label).ToList();
        var totals = Row(null, "Total", null, sales, query.GroupBy is CodeReportGrouping.Person or CodeReportGrouping.Program ? totalClicks : null);
        var currencies = programs.Values.Select(p => p.Currency).Distinct().ToList();
        return new CodeReportDto(query.GroupBy,
            program is null ? null : new ProgramRefDto(program.Id, program.Name, program.BrandName, program.Currency),
            currencies.Count == 1 ? currencies[0] : null, query.From, query.To, totals, result,
            "Uses count pending, approved and refunded sales. Gross = net + discount of pending and approved sales. Commission pending is the " +
            "estimate before caps; approved = recorded but not yet paid; reversed = clawed back after refunds." +
            (currencies.Count > 1 ? " Programs use different currencies: filter by program for comparable totals." : "") +
            (sales.Count >= MaxSales ? $" Only the first {MaxSales:N0} sales are included." : ""));
    }

    /// <summary>The sales a report aggregates (withdrawn ones never count).</summary>
    private IQueryable<CodeSale> SalesOf(CodeProgram? program, CodeReportQuery query)
    {
        var q = db.Set<CodeSale>().AsNoTracking().Where(s => s.Status != CodeSaleStatus.Withdrawn);
        if (program is not null) q = q.Where(s => s.ProgramId == program.Id);
        if (query.From is { } from) q = q.Where(s => s.OrderDate >= from);
        if (query.To is { } to) q = q.Where(s => s.OrderDate <= to);
        return q;
    }

    public async Task<(string FileName, IReadOnlyList<string> Header, IReadOnlyList<object?[]> Rows)> ExportAsync(CodeReportQuery query, CancellationToken ct)
    {
        var report = await ReportAsync(query, ct);
        // An export over more sales than the report can aggregate is a 422 export.too_large, not a partial file.
        var program = query.ProgramId is { } pid ? await db.Set<CodeProgram>().AsNoTracking().FirstOrDefaultAsync(p => p.Id == pid, ct) : null;
        await ExportLimit.EnsureAsync(SalesOf(program, query), Math.Min(exports.Value.CodeReportSales, MaxSales), ct);
        var header = new[]
        {
            "id", "name", "detail", "uses", "pending", "approved", "rejected", "refunded", "grossSales", "discountGiven", "netSales",
            "commissionPending", "commissionApproved", "commissionPaid", "commissionReversed", "clicks", "conversionRatePercent", "currency",
        };
        var rows = report.Rows.Append(report.Totals).Select(r => new object?[]
        {
            r.Id, r.Label, r.Detail, r.Uses, r.Pending, r.Approved, r.Rejected, r.Refunded, r.GrossSales, r.DiscountGiven, r.NetSales,
            r.CommissionPending, r.CommissionApproved, r.CommissionPaid, r.CommissionReversed, r.Clicks, r.ConversionRate, report.Currency,
        }).ToList();
        var name = report.Program is { } p ? new string(p.Name.ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-').ToArray()).Trim('-') : "all";
        return ($"code-report-{name}-{query.GroupBy.ToString().ToLowerInvariant()}-{clock.GetUtcNow():yyyyMMdd}.csv", header, rows);
    }
}
