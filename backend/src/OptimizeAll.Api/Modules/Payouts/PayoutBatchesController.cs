using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Common.Settings;
using OptimizeAll.Api.Modules.Ledger;
using OptimizeAll.Api.Modules.Payouts.Providers;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Payouts;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Payouts;

[ApiController]
[Route("api/v1/finance/payout-batches")]
[DeniedWhileImpersonating(WritesOnly = true)]
public sealed class PayoutBatchesController(
    AppDbContext db,
    PayoutBatchService batches,
    PayoutPaymentService payments,
    PaymentDispatcher dispatcher,
    ISettingsService settings,
    IAuditLogger audit,
    IDataProtectionProvider dataProtection,
    ICurrentUser currentUser,
    TimeProvider clock) : ControllerBase
{
    /// <summary>Data Protection purpose used by the Accounts module to encrypt payout destinations (shared contract).</summary>
    public const string DestinationProtectorPurpose = "OptimizeAll.PayoutProfile.Destination.v1";

    /// <summary>Prepares (or returns the existing) draft batch for a completed period. 201 when created, 200 when it already existed.</summary>
    [HttpPost("prepare")]
    [HasPermission(Permissions.PayoutsPrepare)]
    public async Task<IActionResult> Prepare(PreparePayoutBatchRequest? request, CancellationToken ct)
    {
        var period = await batches.ResolvePeriodAsync(request?.PeriodKey, ct);
        var outcome = await batches.PrepareAsync(period, currentUser.Id, request?.Note, ct);
        var summary = await PayoutReadModels.SummaryAsync(db, outcome.BatchId, ct);
        var users = await PayoutReadModels.PayoutUsersAsync(db, outcome.Exclusions.Select(e => e.UserId), ct);
        var response = new PrepareBatchResponse(outcome.Created, summary,
            outcome.Exclusions.Select(e => new PayoutExclusionDto(
                users.GetValueOrDefault(e.UserId) ?? new PayoutUserDto(e.UserId, string.Empty, string.Empty, string.Empty),
                e.Reason, e.Amount, e.EarningCount)).ToList());
        return outcome.Created ? StatusCode(StatusCodes.Status201Created, response) : Ok(response);
    }

    [HttpGet]
    [HasPermission(Permissions.PayoutsView)]
    public async Task<PagedResult<PayoutBatchSummaryDto>> List([FromQuery] BatchListQuery query, CancellationToken ct)
    {
        var q = db.Set<PayoutBatch>().AsNoTracking();
        if (query.Status is { } status) q = q.Where(b => b.Status == status);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var like = PagingExtensions.LikePattern(query.Search);
            q = q.Where(b => EF.Functions.Like(b.Reference, like, "\\") || EF.Functions.Like(b.PeriodKey, like, "\\"));
        }
        var total = await q.CountAsync(ct);
        var items = await PayoutReadModels.SummariesAsync(db,
            q.OrderByDescending(b => b.CutoffAt).ThenByDescending(b => b.CreatedAt).ThenByDescending(b => b.Id).Skip(query.Skip).Take(query.PageSize), ct);
        return new PagedResult<PayoutBatchSummaryDto>(items, total, query.Page, query.PageSize);
    }

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.PayoutsView)]
    public Task<PayoutBatchDetailDto> Get(Guid id, [FromQuery] BatchItemsQuery query, CancellationToken ct) =>
        PayoutReadModels.DetailAsync(db, settings, id, query, ct);

    [HttpGet("{id:guid}/items/{itemId:guid}")]
    [HasPermission(Permissions.PayoutsView)]
    public Task<PayoutItemDetailDto> GetItem(Guid id, Guid itemId, CancellationToken ct) =>
        PayoutReadModels.ItemDetailAsync(db, id, itemId, ct);

    [HttpPost("{id:guid}/items/{itemId:guid}/hold")]
    [HasPermission(Permissions.PayoutsPrepare)]
    public async Task<PayoutItemDto> HoldItem(Guid id, Guid itemId, HoldItemRequest request, CancellationToken ct)
    {
        await batches.HoldItemAsync(id, itemId, request.Reason.Trim(), ct);
        return await PayoutReadModels.ItemAsync(db, itemId, ct);
    }

    [HttpPost("{id:guid}/items/{itemId:guid}/unhold")]
    [HasPermission(Permissions.PayoutsPrepare)]
    public async Task<PayoutItemDto> UnholdItem(Guid id, Guid itemId, UnholdItemRequest? request, CancellationToken ct)
    {
        await batches.UnholdItemAsync(id, itemId, request?.Note, ct);
        return await PayoutReadModels.ItemAsync(db, itemId, ct);
    }

    [HttpPost("{id:guid}/regenerate")]
    [HasPermission(Permissions.PayoutsPrepare)]
    public async Task<PayoutBatchSummaryDto> Regenerate(Guid id, RegenerateBatchRequest request, CancellationToken ct)
    {
        await batches.RegenerateAsync(id, request.Reason.Trim(), currentUser.Id, ct);
        return await PayoutReadModels.SummaryAsync(db, id, ct);
    }

    [HttpPost("{id:guid}/cancel")]
    [HasPermission(Permissions.PayoutsPrepare)]
    public async Task<PayoutBatchSummaryDto> Cancel(Guid id, CancelBatchRequest request, CancellationToken ct)
    {
        FinanceGuards.RequireConfirm(request.Confirm);
        await batches.CancelAsync(id, request.Reason.Trim(), currentUser.Id, ct);
        return await PayoutReadModels.SummaryAsync(db, id, ct);
    }

    /// <summary>Freezes a draft batch for payment (four-eyes). Items are then dispatched to the payment provider.</summary>
    [HttpPost("{id:guid}/finalize")]
    [HasPermission(Permissions.PayoutsFinalize)]
    public async Task<FinalizeBatchResponse> Finalize(Guid id, FinalizeBatchRequest request, CancellationToken ct)
    {
        FinanceGuards.RequireConfirm(request.Confirm);
        var dispatch = await batches.FinalizeAsync(id, request.ConcurrencyStamp!.Value, request.Reason?.Trim(), currentUser.Id, ct);
        return new FinalizeBatchResponse(await PayoutReadModels.SummaryAsync(db, id, ct), dispatch);
    }

    /// <summary>Re-dispatches items awaiting payment; existing payment attempts are reused (idempotent).</summary>
    [HttpPost("{id:guid}/dispatch")]
    [HasPermission(Permissions.PayoutsRecordPayment)]
    public async Task<IReadOnlyList<DispatchResultDto>> Dispatch(Guid id, CancellationToken ct)
    {
        var batch = await db.Set<PayoutBatch>().AsNoTracking().FirstOrDefaultAsync(b => b.Id == id, ct)
                    ?? throw DomainException.NotFound("PayoutBatch");
        if (batch.Status != PayoutBatchStatus.Finalized)
            throw DomainException.Conflict("payout.batch_not_finalized", "Only finalized batches can be dispatched.");
        var itemIds = await db.Set<PayoutItem>().AsNoTracking()
            .Where(i => i.BatchId == id && i.Status == PayoutItemStatus.AwaitingPayment).Select(i => i.Id).ToListAsync(ct);
        return await dispatcher.DispatchAsync(itemIds, ct);
    }

    [HttpPost("{id:guid}/items/{itemId:guid}/record-payment")]
    [HasPermission(Permissions.PayoutsRecordPayment)]
    public Task<PaymentRecordedDto> RecordPayment(Guid id, Guid itemId, RecordPaymentRequest request, CancellationToken ct) =>
        payments.RecordPaymentAsync(id, itemId, request.PaymentReference, request.PaidAt!.Value, request.Note?.Trim(), currentUser.Id, ct,
            request.OverrideReason);

    [HttpPost("{id:guid}/items/{itemId:guid}/mark-failed")]
    [HasPermission(Permissions.PayoutsRecordPayment)]
    public Task<PaymentRecordedDto> MarkFailed(Guid id, Guid itemId, MarkFailedRequest request, CancellationToken ct) =>
        payments.MarkFailedAsync(id, itemId, request.Reason.Trim(), currentUser.Id, ct);

    [HttpPost("{id:guid}/record-payments")]
    [HasPermission(Permissions.PayoutsRecordPayment)]
    public async Task<IReadOnlyList<BulkPaymentResultDto>> RecordPayments(Guid id, List<BulkPaymentLine> lines, CancellationToken ct)
    {
        if (lines.Count is 0 or > 1000)
            throw new DomainException("payout.bulk_size", "Send between 1 and 1000 payment lines.");
        if (!await db.Set<PayoutBatch>().AnyAsync(b => b.Id == id, ct)) throw DomainException.NotFound("PayoutBatch");
        return await payments.RecordBulkAsync(id, lines, currentUser.Id, ct);
    }

    [HttpGet("{id:guid}/export.csv")]
    [HasPermission(Permissions.PayoutsView)]
    public async Task<IActionResult> Export(Guid id, CancellationToken ct)
    {
        var (batch, rows) = await PayoutReadModels.ExportRowsAsync(db, id, null, ct);
        return Csv.File($"payout-batch-{batch.Reference}.csv", PayoutReadModels.ExportHeader,
            rows.Select(r => PayoutReadModels.ExportCells(batch, r)));
    }

    /// <summary>Status cell of a companion row for an item that must not be paid now (see <see cref="PaymentInstructions"/>).</summary>
    public const string ExcludedMarker = "EXCLUDED";

    /// <summary>
    /// Sensitive: CSV of the items awaiting payment with the DECRYPTED payout destination, for paying manually.
    /// Requires <c>?confirm=true</c>; every export is audited and the first one sets <c>InstructionsExportedAt</c>
    /// (after which the batch can no longer be cancelled).
    /// Items whose participant has an active payout hold or an inactive account are NOT payable: they appear after the
    /// payable rows as companion rows whose Status is <c>EXCLUDED</c>, whose destination is withheld and replaced by the
    /// reason ("EXCLUDED — do not pay: …").
    /// </summary>
    [HttpGet("{id:guid}/payment-instructions.csv")]
    [DeniedWhileImpersonating] // decrypts payout destinations
    [HasPermission(Permissions.PayoutsRecordPayment)]
    public async Task<IActionResult> PaymentInstructions(Guid id, [FromQuery] bool confirm, CancellationToken ct)
    {
        FinanceGuards.RequireConfirm(confirm);
        var (batch, rows) = await PayoutReadModels.ExportRowsAsync(db, id,
            q => q.Where(i => i.Status == PayoutItemStatus.AwaitingPayment), ct);
        if (batch.Status is not (PayoutBatchStatus.Finalized or PayoutBatchStatus.Completed))
            throw DomainException.Conflict("payout.batch_not_finalized", "Payment instructions are only available for finalized batches.");

        var blockers = await PayoutStore.PayoutBlockersAsync(db, rows.Select(r => r.Item.UserId).Distinct().ToList(), ct);
        var payable = rows.Where(r => !blockers.ContainsKey(r.Item.UserId)).ToList();
        var excluded = rows.Where(r => blockers.ContainsKey(r.Item.UserId)).ToList();

        var protector = dataProtection.CreateProtector(DestinationProtectorPurpose);
        var header = PayoutReadModels.ExportHeader.Append("Destination (confidential)").ToArray();
        var statusColumn = Array.IndexOf(PayoutReadModels.ExportHeader, "Status");
        var cells = payable.Select(r => PayoutReadModels.ExportCells(batch, r)
            .Append(Decrypt(protector, r.Profile?.EncryptedDestination)).ToArray()).ToList();
        foreach (var r in excluded)
        {
            var row = PayoutReadModels.ExportCells(batch, r);
            row[statusColumn] = ExcludedMarker;
            cells.Add(row.Append($"EXCLUDED — do not pay: {blockers[r.Item.UserId]}").ToArray());
        }

        var now = clock.GetUtcNow().UtcDateTime;
        await db.Set<PayoutBatch>().Where(b => b.Id == batch.Id && b.InstructionsExportedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.InstructionsExportedAt, now), ct);
        audit.Record("payout.payment_instructions_exported", nameof(PayoutBatch), batch.Id,
            after: new
            {
                batch.Reference, Items = payable.Count, ItemIds = payable.Select(r => r.Item.Id).ToList(),
                Excluded = excluded.Count, ExcludedItemIds = excluded.Select(r => r.Item.Id).ToList(),
                FirstExport = batch.InstructionsExportedAt is null,
            },
            reason: "Sensitive export: decrypted payout destinations");
        await db.SaveChangesAsync(ct);
        return Csv.File($"payment-instructions-{batch.Reference}-{clock.GetUtcNow():yyyyMMddHHmmss}.csv", header, cells);
    }

    private static string Decrypt(IDataProtector protector, string? encrypted)
    {
        if (string.IsNullOrEmpty(encrypted)) return "MISSING: no payout details on file";
        try
        {
            return protector.Unprotect(encrypted);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return "UNREADABLE: ask the participant to re-enter payout details";
        }
    }

    [HttpGet("{id:guid}/reconciliation")]
    [HasPermission(Permissions.PayoutsView)]
    public Task<ReconciliationDto> Reconciliation(Guid id, CancellationToken ct) => PayoutReadModels.ReconcileAsync(db, id, ct);

    [HttpGet("{id:guid}/reconciliation.csv")]
    [HasPermission(Permissions.PayoutsView)]
    public async Task<IActionResult> ReconciliationCsv(Guid id, CancellationToken ct)
    {
        var r = await PayoutReadModels.ReconcileAsync(db, id, ct);
        var byItem = r.Discrepancies.Where(d => d.ItemId != null).GroupBy(d => d.ItemId!.Value)
            .ToDictionary(g => g.Key, g => string.Join(" | ", g.Select(d => $"{d.Type}: {d.Message}")));
        var rows = r.Items.Select(i => new object?[]
        {
            r.Reference, r.PeriodKey, i.ItemId, i.User.DisplayName, i.User.Email, i.Status, i.Amount, i.EarningsTotal,
            i.EarningCount, i.LinkedEarningCount, i.PaymentReference, i.PaidAt, i.Ok ? "OK" : "DISCREPANCY",
            byItem.GetValueOrDefault(i.ItemId),
        }).ToList();
        foreach (var d in r.Discrepancies.Where(d => d.ItemId == null))
            rows.Add(new object?[] { r.Reference, r.PeriodKey, null, null, null, null, null, null, null, null, null, null, "DISCREPANCY", $"{d.Type}: {d.Message}" });
        return Csv.File($"reconciliation-{r.Reference}.csv",
            new[]
            {
                "Batch reference", "Period", "Item ID", "Participant name", "Email", "Item status", "Item amount",
                "Sum of earnings", "Earning count", "Linked earnings", "Payment reference", "Paid at (UTC)", "Result", "Details",
            },
            rows);
    }
}
