using System.Globalization;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.Billing;
using OptimizeAll.Domain.Common;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Billing;

/// <summary>
/// Billing reports. Every amount is grouped by currency (currencies are never mixed or converted here). Aggregation runs
/// in memory over the rows of the requested window so the queries stay portable across database providers.
/// </summary>
public sealed class BillingReports(AppDbContext db, IClientScope scope, InvoiceService invoices, PaymentService payments, TimeProvider clock)
{
    private static readonly InvoiceStatus[] Invoiced =
    {
        InvoiceStatus.Issued, InvoiceStatus.PartiallyPaid, InvoiceStatus.Paid, InvoiceStatus.Overdue, InvoiceStatus.WrittenOff,
    };

    private async Task<IQueryable<Invoice>> InvoicesAsync(CancellationToken ct) =>
        await scope.ApplyAsync(db.Set<Invoice>().AsNoTracking(), i => i.ClientAccountId, ct);

    private async Task<Dictionary<Guid, string>> ClientNamesAsync(CancellationToken ct) =>
        await db.Set<ClientAccount>().AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Name, ct);

    public async Task<AgingReportDto> AgingAsync(DateOnly? asOf, CancellationToken ct)
    {
        var date = asOf ?? BillingDates.Today(clock);
        var open = await (await InvoicesAsync(ct)).Where(i => Invoice.OpenStatuses.Contains(i.Status) && i.IssueDate <= date).ToListAsync(ct);
        var names = await ClientNamesAsync(ct);
        var rows = open.GroupBy(i => (i.ClientAccountId, i.Currency)).Select(g =>
        {
            var buckets = new decimal[5];
            foreach (var i in g) buckets[(int)Aging.BucketFor(i.DueDate ?? date, date)] += i.Balance;
            return new AgingRowDto(g.Key.ClientAccountId, names.GetValueOrDefault(g.Key.ClientAccountId, "—"), g.Key.Currency,
                buckets[0], buckets[1], buckets[2], buckets[3], buckets[4], buckets.Sum());
        }).OrderBy(r => r.Currency).ThenBy(r => r.ClientName, StringComparer.OrdinalIgnoreCase).ToList();
        var totals = rows.GroupBy(r => r.Currency).Select(g => new AgingRowDto(Guid.Empty, "Total", g.Key, g.Sum(r => r.Current),
            g.Sum(r => r.Days1To30), g.Sum(r => r.Days31To60), g.Sum(r => r.Days61To90), g.Sum(r => r.Over90), g.Sum(r => r.Total))).ToList();
        return new AgingReportDto(date, rows, totals);
    }

    /// <summary>Invoiced (net of tax, by issue date) and collected (payments, by payment date) grouped by month, service or client.</summary>
    public async Task<RevenueReportDto> RevenueAsync(string groupBy, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        groupBy = groupBy is "service" or "client" ? groupBy : "month";
        var end = to ?? BillingDates.Today(clock);
        var start = from ?? new DateOnly(end.Year, end.Month, 1).AddMonths(-11);
        if (start > end) throw new DomainException("billing.invalid_range", "The start date must be before the end date.");
        if (end.DayNumber - start.DayNumber > 3660) throw new DomainException("billing.invalid_range", "Choose a range of at most 10 years.");

        var issued = await (await InvoicesAsync(ct)).Include(i => i.Lines)
            .Where(i => Invoiced.Contains(i.Status) && i.IssueDate >= start && i.IssueDate <= end).ToListAsync(ct);
        var paymentRows = await (await scope.ApplyAsync(db.Set<Payment>().AsNoTracking(), p => p.ClientAccountId, ct))
            .Where(p => p.PaidOn >= start && p.PaidOn <= end).ToListAsync(ct);
        var names = await ClientNamesAsync(ct);

        var invoicedEntries = new List<(string Key, string Label, string Currency, decimal Amount)>();
        var collectedEntries = new List<(string Key, string Label, string Currency, decimal Amount)>();
        switch (groupBy)
        {
            case "service":
                foreach (var i in issued)
                    foreach (var l in i.Lines)
                        invoicedEntries.Add((l.ServiceSlug ?? "other", l.ServiceSlug ?? "Other / custom", i.Currency, l.Subtotal));
                // Payments are allocated to services pro rata to the invoice's net lines.
                var byInvoice = issued.ToDictionary(i => i.Id);
                var extraIds = paymentRows.Select(p => p.InvoiceId).Where(id => !byInvoice.ContainsKey(id)).Distinct().ToList();
                foreach (var extra in await db.Set<Invoice>().AsNoTracking().Include(i => i.Lines).Where(i => extraIds.Contains(i.Id)).ToListAsync(ct))
                    byInvoice[extra.Id] = extra;
                foreach (var p in paymentRows)
                {
                    if (!byInvoice.TryGetValue(p.InvoiceId, out var inv) || inv.Total == 0) continue;
                    foreach (var l in inv.Lines)
                        collectedEntries.Add((l.ServiceSlug ?? "other", l.ServiceSlug ?? "Other / custom", p.Currency,
                            Money.Round(p.Amount * l.Total / inv.Total, p.Currency)));
                }
                break;
            case "client":
                invoicedEntries.AddRange(issued.Select(i => (i.ClientAccountId.ToString(), names.GetValueOrDefault(i.ClientAccountId, "—"), i.Currency, i.Subtotal)));
                collectedEntries.AddRange(paymentRows.Select(p => (p.ClientAccountId.ToString(), names.GetValueOrDefault(p.ClientAccountId, "—"), p.Currency, p.Amount)));
                break;
            default:
                invoicedEntries.AddRange(issued.Select(i => (Month(i.IssueDate!.Value), Month(i.IssueDate!.Value), i.Currency, i.Subtotal)));
                collectedEntries.AddRange(paymentRows.Select(p => (Month(p.PaidOn), Month(p.PaidOn), p.Currency, p.Amount)));
                break;
        }
        var keys = invoicedEntries.Select(e => (e.Key, e.Currency)).Concat(collectedEntries.Select(e => (e.Key, e.Currency))).Distinct();
        var rows = keys.Select(k =>
        {
            var label = invoicedEntries.Concat(collectedEntries).First(e => e.Key == k.Key).Label;
            return new RevenueRowDto(k.Key, label, k.Currency,
                invoicedEntries.Where(e => e.Key == k.Key && e.Currency == k.Currency).Sum(e => e.Amount),
                collectedEntries.Where(e => e.Key == k.Key && e.Currency == k.Currency).Sum(e => e.Amount));
        });
        rows = groupBy == "month"
            ? rows.OrderBy(r => r.Key, StringComparer.Ordinal).ThenBy(r => r.Currency)
            : rows.OrderBy(r => r.Currency).ThenByDescending(r => r.Invoiced);
        return new RevenueReportDto(groupBy, start, end, rows.ToList());
    }

    public async Task<MrrReportDto> MrrAsync(CancellationToken ct)
    {
        var contracts = await (await scope.ApplyAsync(db.Set<Contract>().AsNoTracking(), c => c.ClientAccountId, ct))
            .Include(c => c.Lines).Where(c => c.Status == ContractStatus.Active).ToListAsync(ct);
        var names = await ClientNamesAsync(ct);
        var rows = contracts.GroupBy(c => (c.ClientAccountId, c.Currency)).Select(g =>
        {
            var mrr = g.Sum(c => Pricing.MonthlyEquivalent(c.Lines.Sum(l => l.Total), Pricing.ToRecurrence(c.BillingFrequency), c.Currency));
            return new MrrRowDto(g.Key.ClientAccountId, names.GetValueOrDefault(g.Key.ClientAccountId, "—"), g.Key.Currency, g.Count(), mrr, mrr * 12);
        }).OrderBy(r => r.Currency).ThenByDescending(r => r.Mrr).ToList();
        return new MrrReportDto(rows,
            rows.GroupBy(r => r.Currency).Select(g => new CurrencyAmount(g.Key, g.Sum(r => r.Mrr))).ToList(),
            rows.GroupBy(r => r.Currency).Select(g => new CurrencyAmount(g.Key, g.Sum(r => r.Arr))).ToList());
    }

    public async Task<CollectionsReportDto> CollectionsAsync(DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var end = to ?? BillingDates.Today(clock);
        var start = from ?? new DateOnly(end.Year, end.Month, 1).AddMonths(-11);
        var rows = await (await scope.ApplyAsync(db.Set<Payment>().AsNoTracking(), p => p.ClientAccountId, ct))
            .Where(p => p.PaidOn >= start && p.PaidOn <= end).ToListAsync(ct);
        return new CollectionsReportDto(start, end, rows.GroupBy(p => (Month(p.PaidOn), p.Currency, p.Method))
            .Select(g => new CollectionsRowDto(g.Key.Item1, g.Key.Currency, g.Key.Method, g.Count(), g.Sum(p => p.Amount)))
            .OrderBy(r => r.Month, StringComparer.Ordinal).ThenBy(r => r.Currency).ThenBy(r => r.Method).ToList());
    }

    public async Task<BillingOverviewDto> OverviewAsync(CancellationToken ct)
    {
        var today = BillingDates.Today(clock);
        var all = await InvoicesAsync(ct);
        var open = await all.Where(i => Invoice.OpenStatuses.Contains(i.Status)).ToListAsync(ct);
        var drafts = await all.CountAsync(i => i.Status == InvoiceStatus.Draft, ct);
        var mrr = await MrrAsync(ct);
        var since = today.AddDays(-30);
        var recentPayments = await (await scope.ApplyAsync(db.Set<Payment>().AsNoTracking(), p => p.ClientAccountId, ct))
            .Where(p => p.PaidOn >= since).OrderByDescending(p => p.PaidOn).ThenByDescending(p => p.CreatedAt).ToListAsync(ct);
        var overdue = open.Where(i => i.DueDate is { } d && d < today).ToList();
        var activeContracts = await (await scope.ApplyAsync(db.Set<Contract>().AsNoTracking(), c => c.ClientAccountId, ct))
            .CountAsync(c => c.Status == ContractStatus.Active, ct);
        return new BillingOverviewDto(
            ByCurrency(open.Select(i => (i.Currency, i.Balance))),
            ByCurrency(overdue.Select(i => (i.Currency, i.Balance))),
            mrr.MrrByCurrency,
            ByCurrency(recentPayments.Select(p => (p.Currency, p.Amount))),
            drafts, overdue.Count, activeContracts,
            await invoices.SummariesAsync(overdue.OrderBy(i => i.DueDate).Take(8).ToList(), ct),
            await payments.ToDtosAsync(recentPayments.Take(8).ToList(), ct));
    }

    public static IReadOnlyList<CurrencyAmount> ByCurrency(IEnumerable<(string Currency, decimal Amount)> values) =>
        values.GroupBy(v => v.Currency).Select(g => new CurrencyAmount(g.Key, g.Sum(v => v.Amount))).OrderBy(c => c.Currency).ToList();

    private static string Month(DateOnly d) => d.ToString("yyyy-MM", CultureInfo.InvariantCulture);
}
