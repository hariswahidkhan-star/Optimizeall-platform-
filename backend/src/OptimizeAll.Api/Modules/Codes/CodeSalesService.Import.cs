using System.Data;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Domain.Codes;
using OptimizeAll.Domain.Common;

namespace OptimizeAll.Api.Modules.Codes;

// Reconciliation with the brand's sales report (order id, code, amount, date, status): matches reported sales, flags
// differences, creates sales nobody claimed (attributed to the code's personal holder) and applies refunds/cancellations.
public sealed partial class CodeSalesService
{
    private const int ImportChunk = 200;

    private static readonly Dictionary<string, string> SalesColumns = new()
    {
        ["orderid"] = "order", ["order"] = "order", ["ordernumber"] = "order", ["orderno"] = "order", ["orderreference"] = "order",
        ["orderref"] = "order", ["reference"] = "order", ["transactionid"] = "order",
        ["code"] = "code", ["discountcode"] = "code", ["couponcode"] = "code", ["coupon"] = "code", ["promocode"] = "code",
        ["amount"] = "amount", ["netamount"] = "amount", ["net"] = "amount", ["total"] = "amount", ["ordertotal"] = "amount",
        ["ordervalue"] = "amount", ["revenue"] = "amount", ["subtotal"] = "amount",
        ["date"] = "date", ["orderdate"] = "date", ["createdat"] = "date", ["ordereddate"] = "date", ["purchasedate"] = "date",
        ["status"] = "status", ["orderstatus"] = "status", ["state"] = "status",
        ["currency"] = "currency", ["discount"] = "discount", ["discountamount"] = "discount",
    };

    private enum ReportStatus { Valid, Refunded, Cancelled }

    private sealed record ReportRow(int Line, string Reference, string Key, string Code, decimal Amount, decimal Discount, string Currency, DateTime Date,
        ReportStatus Status);

    private sealed record Plan(ReportRow Row, string Outcome, string Message, Guid? SaleId = null, Guid? CodeId = null, Guid? UserId = null,
        Guid? AssignmentId = null, CodeSaleVerification? Verification = null);

    public async Task<SalesImportResultDto> ImportReportAsync(Guid programId, IFormFile file, bool dryRun, CancellationToken ct)
    {
        var program = await LoadProgramAsync(programId, ct);
        if (program.Status == CodeProgramStatus.Archived) throw CodeProgramsService.Archived();
        var table = CodeCsv.Parse(await CodeCsv.ReadAsync(file, ct), SalesColumns, "order", firstColumnFallback: false, CodeLimits.MaxSaleRows);
        foreach (var required in new[] { "code", "amount", "date" })
            if (!table.Columns.ContainsKey(required))
                throw new DomainException("csv.missing_column", $"The sales report needs a '{required}' column (order id, code, amount, date; optional status, currency, discount).");

        var issues = new List<SalesImportIssueDto>();
        var rows = new List<ReportRow>();
        var seen = new HashSet<string>();
        foreach (var r in table.Rows)
        {
            var reference = table.Get(r, "order");
            var code = table.Get(r, "code");
            if (reference is null || code is null) { issues.Add(new(r.Line, reference, code, "rejected", "The row needs an order id and a code.")); continue; }
            if (reference.Length > CodeSale.MaxOrderReference) { issues.Add(new(r.Line, reference, code, "rejected", "The order id is too long.")); continue; }
            var key = CodeSale.NormalizeOrderReference(reference);
            if (!seen.Add(key)) { issues.Add(new(r.Line, reference, code, "rejected", "This order appears earlier in the file.")); continue; }
            if (!CodeCsv.TryAmount(table.Get(r, "amount"), out var amount) || amount <= 0)
            { issues.Add(new(r.Line, reference, code, "rejected", $"'{table.Get(r, "amount")}' is not a positive amount.")); continue; }
            var discount = 0m;
            if (table.Get(r, "discount") is { } d && (!CodeCsv.TryAmount(d, out discount) || discount < 0))
            { issues.Add(new(r.Line, reference, code, "rejected", $"'{d}' is not a valid discount.")); continue; }
            if (!CodeCsv.TryDate(table.Get(r, "date"), out var date))
            { issues.Add(new(r.Line, reference, code, "rejected", $"'{table.Get(r, "date")}' is not a date (use YYYY-MM-DD).")); continue; }
            var currency = Money.Normalize(table.Get(r, "currency") ?? program.Currency);
            if (!Money.IsSupported(currency)) { issues.Add(new(r.Line, reference, code, "rejected", $"Currency {currency} is not supported.")); continue; }
            var statusText = (table.Get(r, "status") ?? "completed").Trim().ToLowerInvariant();
            ReportStatus? status = statusText switch
            {
                "refunded" or "refund" or "returned" or "return" or "chargeback" or "partially_refunded" => ReportStatus.Refunded,
                "cancelled" or "canceled" or "void" or "voided" or "failed" => ReportStatus.Cancelled,
                "completed" or "complete" or "paid" or "fulfilled" or "shipped" or "delivered" or "processing" or "success" or "succeeded" or "approved" or "valid" => ReportStatus.Valid,
                _ => null,
            };
            if (status is null) { issues.Add(new(r.Line, reference, code, "rejected", $"Unknown status '{statusText}' (use completed, refunded or cancelled).")); continue; }
            rows.Add(new ReportRow(r.Line, reference, key, DiscountCode.Normalize(code), Money.Round(amount, currency), Money.Round(discount, currency), currency,
                date, status.Value));
        }
        var rejected = issues.Count;

        var plans = new List<Plan>();
        foreach (var chunk in rows.Chunk(ImportChunk))
            plans.AddRange(await PlanAsync(program, chunk, ct));

        if (!dryRun && plans.Any(p => p.Outcome is "match" or "mismatch" or "create" or "refund" or "cancel"))
        {
            var batch = new CodeImportBatch
            {
                ProgramId = programId, Kind = CodeImportKind.Sales, FileName = CodeQueries.Fit(file.FileName, 200), Rows = table.Rows.Count,
                CreatedAt = Now, CreatedByUserId = currentUser.Id,
            };
            db.Set<CodeImportBatch>().Add(batch);
            await db.SaveChangesAsync(ct);
            var applied = new List<Plan>();
            foreach (var chunk in plans.Chunk(ImportChunk))
                applied.AddRange(await ApplyAsync(program.Id, batch.Id, chunk, ct));
            plans = applied;
            var tracked = await db.Set<CodeImportBatch>().FirstAsync(b => b.Id == batch.Id, ct);
            tracked.Created = plans.Count(p => p.Outcome == "create");
            tracked.Matched = plans.Count(p => p.Outcome == "match");
            tracked.Flagged = plans.Count(p => p.Outcome is "mismatch" or "flagged");
            tracked.Rejected = rejected + plans.Count(p => p.Outcome == "rejected");
            audit.Record("code_sale.report_imported", nameof(CodeProgram), programId, after: new
            {
                BatchId = batch.Id, batch.FileName, Rows = table.Rows.Count, tracked.Created, tracked.Matched, tracked.Flagged,
                Refunded = plans.Count(p => p.Outcome == "refund"), Cancelled = plans.Count(p => p.Outcome == "cancel"), tracked.Rejected,
            });
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
        }

        issues.AddRange(plans.Where(p => p.Outcome != "match" && p.Outcome != "unchanged")
            .Select(p => new SalesImportIssueDto(p.Row.Line, p.Row.Reference, p.Row.Code, p.Outcome, p.Message)));
        return new SalesImportResultDto(dryRun, table.Rows.Count,
            plans.Count(p => p.Outcome == "match"), plans.Count(p => p.Outcome == "mismatch"), plans.Count(p => p.Outcome == "create"),
            plans.Count(p => p.Outcome == "cancel"), plans.Count(p => p.Outcome == "refund"), plans.Count(p => p.Outcome is "unchanged" or "ignored"),
            rejected + plans.Count(p => p.Outcome is "rejected" or "flagged"), issues.OrderBy(i => i.Row).ToList());
    }

    /// <summary>Decides what each row does (no writes): the same classification for the dry run and the real import.</summary>
    private async Task<List<Plan>> PlanAsync(CodeProgram program, IReadOnlyList<ReportRow> rows, CancellationToken ct)
    {
        var codeKeys = rows.Select(r => r.Code).Distinct().ToList();
        var codes = await db.Set<DiscountCode>().AsNoTracking().Where(c => c.ProgramId == program.Id && codeKeys.Contains(c.NormalizedCode))
            .ToDictionaryAsync(c => c.NormalizedCode, ct);
        var keys = rows.Select(r => r.Key).ToList();
        var sales = await db.Set<CodeSale>().AsNoTracking().Where(s => s.ProgramId == program.Id && s.ActiveOrderKey != null && keys.Contains(s.ActiveOrderKey))
            .ToDictionaryAsync(s => s.ActiveOrderKey!, ct);
        var codeIds = codes.Values.Select(c => c.Id).ToList();
        var assignments = await db.Set<DiscountCodeAssignment>().AsNoTracking().Where(a => codeIds.Contains(a.CodeId))
            .OrderBy(a => a.ValidFrom).ThenBy(a => a.Id).ToListAsync(ct);
        var saleCodeIds = sales.Values.Select(s => s.CodeId).Distinct().ToList();
        var saleCodes = await db.Set<DiscountCode>().AsNoTracking().Where(c => saleCodeIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.NormalizedCode, ct);

        var plans = new List<Plan>();
        foreach (var r in rows)
        {
            if (!codes.TryGetValue(r.Code, out var code))
            {
                plans.Add(new Plan(r, "rejected", $"The program has no code {r.Code}."));
                continue;
            }
            if (sales.TryGetValue(r.Key, out var sale))
            {
                if (r.Status != ReportStatus.Valid)
                {
                    plans.Add(sale.Status switch
                    {
                        CodeSaleStatus.Approved => new Plan(r, "refund", $"Reported {r.Status.ToString().ToLowerInvariant()}: the approved sale's commission will be reversed.", sale.Id),
                        CodeSaleStatus.Pending or CodeSaleStatus.NeedsInfo => new Plan(r, "cancel", $"Reported {r.Status.ToString().ToLowerInvariant()}: the pending sale will be cancelled.", sale.Id),
                        _ => new Plan(r, "unchanged", $"Already {sale.Status}.", sale.Id),
                    });
                    continue;
                }
                var problems = new List<string>();
                if (saleCodes.GetValueOrDefault(sale.CodeId) != r.Code) problems.Add($"code {r.Code} in the report, {saleCodes.GetValueOrDefault(sale.CodeId)} claimed");
                var reported = await ComparableAmountAsync(program, sale, r, ct);
                if (reported is null) problems.Add($"can't compare {r.Currency} with {sale.Currency} (no exchange rate)");
                else if (Math.Abs(reported.Value - sale.NetAmount) > Math.Max(0.01m, sale.NetAmount * 0.01m))
                    problems.Add($"amount {Money.Format(reported.Value, sale.Currency)} in the report, {Money.Format(sale.NetAmount, sale.Currency)} claimed");
                if (Math.Abs((r.Date - sale.OrderDate).TotalHours) > 36) problems.Add($"date {r.Date:yyyy-MM-dd} in the report, {sale.OrderDate:yyyy-MM-dd} claimed");
                var verification = problems.Count == 0 ? CodeSaleVerification.Matched : CodeSaleVerification.Mismatch;
                if (sale.Verification == verification && sale.ReportedNetAmount == r.Amount && sale.ReportedOrderDate == r.Date)
                    plans.Add(new Plan(r, "unchanged", "Already reconciled.", sale.Id));
                else if (sale.Verification == CodeSaleVerification.ReportedByBrand)
                    plans.Add(new Plan(r, "unchanged", "Created from an earlier report.", sale.Id));
                else
                    plans.Add(new Plan(r, verification == CodeSaleVerification.Matched ? "match" : "mismatch",
                        problems.Count == 0 ? "Matches the reported sale." : "Differs: " + string.Join("; ", problems) + ".", sale.Id, Verification: verification));
                continue;
            }
            if (r.Status != ReportStatus.Valid)
            {
                plans.Add(new Plan(r, "ignored", $"Reported {r.Status.ToString().ToLowerInvariant()} and nobody claimed it: nothing to do."));
                continue;
            }
            var covering = assignments.FirstOrDefault(a => a.CodeId == code.Id && a.Covers(r.Date));
            if (covering is null)
                plans.Add(new Plan(r, "flagged", $"Code {code.Code} wasn't assigned to anyone on {r.Date:yyyy-MM-dd}; the use can't be attributed."));
            else if (covering.UserId is null)
                plans.Add(new Plan(r, "flagged", $"Code {code.Code} is shared by a group; ask the member who made the sale to report it (or add it for them)."));
            else if (!program.IsWithinWindow(r.Date))
                plans.Add(new Plan(r, "flagged", "The order date is outside the program."));
            else
                plans.Add(new Plan(r, "create", "Unclaimed use: a pending sale is created for the code's holder.", CodeId: code.Id, UserId: covering.UserId,
                    AssignmentId: covering.Id));
        }
        return plans;
    }

    private async Task<decimal?> ComparableAmountAsync(CodeProgram program, CodeSale sale, ReportRow r, CancellationToken ct)
    {
        if (r.Currency == sale.Currency) return r.Amount;
        try
        {
            var rate = await fx.GetRateAsync(r.Currency, sale.Currency, r.Date, ct);
            return Money.Convert(r.Amount, rate.Rate, sale.Currency);
        }
        catch (DomainException ex) when (ex.Code == "fx.rate_missing")
        {
            return null;
        }
    }

    /// <summary>Applies one chunk under the program lock, re-planning against the current rows so concurrent changes are respected.</summary>
    private async Task<List<Plan>> ApplyAsync(Guid programId, Guid batchId, IReadOnlyList<Plan> planned, CancellationToken ct)
    {
        var result = new List<Plan>();
        var now = Now;
        await using (await db.Dialect().AcquireNamedLockAsync(db, CodeLocks.Program(programId), CodeLocks.Timeout, ct))
        await using (var tx = await db.Dialect().BeginWriteTransactionAsync(db, ct, IsolationLevel.ReadCommitted))
        {
            await db.Dialect().LockRowAsync(db, CodeLocks.ProgramsTable, programId, ct);
            var program = await LoadProgramAsync(programId, ct);
            var fresh = await PlanAsync(program, planned.Select(p => p.Row).ToList(), ct);
            foreach (var p in fresh)
            {
                switch (p.Outcome)
                {
                    case "match":
                    case "mismatch":
                    {
                        var sale = await db.Set<CodeSale>().FirstAsync(s => s.Id == p.SaleId, ct);
                        sale.Verification = p.Verification!.Value;
                        sale.VerificationNote = CodeQueries.Fit(p.Message, 500);
                        sale.ReportedNetAmount = p.Row.Amount;
                        sale.ReportedOrderDate = p.Row.Date;
                        sale.ImportBatchId = batchId;
                        AddEvent(sale.Id, sale.Status, sale.Status, p.Outcome == "match" ? "verified" : "mismatch_flagged", p.Message, now);
                        result.Add(p);
                        break;
                    }
                    case "refund":
                    case "cancel":
                    {
                        var sale = await db.Set<CodeSale>().FirstAsync(s => s.Id == p.SaleId, ct);
                        try
                        {
                            await RefundCoreAsync(program, sale, $"Brand report: order {p.Row.Status.ToString().ToLowerInvariant()}", "refunded_by_report", ct);
                            sale.ImportBatchId = batchId;
                            result.Add(p);
                        }
                        catch (DomainException ex)
                        {
                            result.Add(p with { Outcome = "flagged", Message = $"Could not refund automatically: {ex.Message}" });
                        }
                        break;
                    }
                    case "create":
                    {
                        Money3 money;
                        try
                        {
                            money = await PriceOrderAsync(program, p.Row.Amount, p.Row.Discount, p.Row.Currency, p.Row.Date, ct);
                        }
                        catch (DomainException ex)
                        {
                            result.Add(p with { Outcome = "flagged", Message = ex.Message });
                            break;
                        }
                        var sale = new CodeSale
                        {
                            ProgramId = programId, CodeId = p.CodeId!.Value, UserId = p.UserId!.Value, AssignmentId = p.AssignmentId, OrderReference = p.Row.Reference,
                            NormalizedOrderReference = p.Row.Key, ActiveOrderKey = p.Row.Key, OrderDate = p.Row.Date, NetAmount = money.Net,
                            DiscountAmount = money.Discount, Currency = money.Currency, ExchangeRate = money.Rate, ExchangeRateId = money.RateId,
                            ProgramNetAmount = money.ProgramNet, ProgramDiscountAmount = money.ProgramDiscount, Status = CodeSaleStatus.Pending,
                            Source = CodeSaleSource.Import, CreatedByUserId = currentUser.Id, SubmittedAt = now, Verification = CodeSaleVerification.ReportedByBrand,
                            VerificationNote = "Created from the brand's sales report (not claimed by the participant).", ReportedNetAmount = p.Row.Amount,
                            ReportedOrderDate = p.Row.Date, ImportBatchId = batchId,
                        };
                        sale.EstimatedCommission = await payouts.EstimateAsync(program, sale.UserId, money.ProgramNet, null, ct);
                        db.Set<CodeSale>().Add(sale);
                        AddEvent(sale.Id, null, CodeSaleStatus.Pending, "imported", "Unclaimed use from the brand's sales report", now);
                        result.Add(p with { SaleId = sale.Id });
                        break;
                    }
                    default:
                        result.Add(p);
                        break;
                }
            }
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        db.ChangeTracker.Clear();
        return result;
    }
}
