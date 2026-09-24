using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Modules.Billing;
using OptimizeAll.Domain.Billing;
using OptimizeAll.Domain.Common;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.PaymentsHub;

/// <summary>
/// Payments hub for finance and admins: one list of incoming (client invoice) and outgoing (participant payout) money
/// with KPIs, CSV export and manual actions. Reading needs <c>billing.view</c> (incoming, client-scoped) and/or
/// <c>payouts.view</c> (outgoing); each section is shown only with its permission. Writes need <c>billing.manage</c>
/// (invoice payments, claims, reminders) or <c>payouts.record_payment</c> (payouts) and go through the Billing and
/// Payouts services.
/// </summary>
[ApiController]
[Route("api/v1/admin/payments")]
[DeniedWhileImpersonating(WritesOnly = true)]
public sealed class PaymentsHubController(
    PaymentsHubQueries queries,
    PaymentsHubActions actions,
    PaymentClaimService claims,
    PaymentReminderService reminders,
    PaymentProofService proofs,
    PaymentService payments,
    IClientScope scope,
    ICurrentUser currentUser,
    AppDbContext db) : ControllerBase
{
    [HttpGet]
    [RequireAnyPermission(Permissions.BillingView, Permissions.PayoutsView)]
    public Task<PagedResult<PaymentRecordDto>> List([FromQuery] PaymentHubQuery query, CancellationToken ct) => queries.ListAsync(query, ct);

    [HttpGet("summary")]
    [RequireAnyPermission(Permissions.BillingView, Permissions.PayoutsView)]
    public Task<PaymentsSummaryDto> Summary(CancellationToken ct) => queries.SummaryAsync(ct);

    [HttpGet("records/{kind}/{id:guid}")]
    [RequireAnyPermission(Permissions.BillingView, Permissions.PayoutsView)]
    public Task<PaymentRecordDetailDto> Detail(PaymentRecordKind kind, Guid id, CancellationToken ct) => queries.DetailAsync(kind, id, ct);

    /// <summary>CSV of every record matching the filters (formula-safe; at most 20,000 rows).</summary>
    [HttpGet("export.csv")]
    [RequireAnyPermission(Permissions.BillingView, Permissions.PayoutsView)]
    public async Task<FileContentResult> Export([FromQuery] PaymentHubQuery query, CancellationToken ct)
    {
        var rows = await queries.ExportAsync(query, ct);
        return Csv.File($"payments-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv",
            new[]
            {
                "Direction", "Type", "Status", "Source status", "Date", "Due date", "Paid at (UTC)", "Party", "Party email", "Amount", "Currency",
                "Method", "Reference", "Invoice", "Batch", "Recorded by", "Notes", "Reversal / failure reason", "Id",
            },
            rows.Select(r => new object?[]
            {
                r.Direction.ToString(), r.Kind.ToString(), r.Status.ToString(), r.SourceStatus, r.Date, r.DueDate, r.PaidAt, r.Party.Name,
                r.Party.Email, r.Amount, r.Currency, r.Method, r.Reference, r.InvoiceNumber, r.BatchReference, r.RecordedBy, r.Notes,
                r.ReversalReason, r.Id,
            }));
    }

    // ---------------- Incoming: invoices

    /// <summary>Records a manual incoming payment. Idempotent by <c>requestId</c> (201 new, 200 replay).</summary>
    [HttpPost("invoices/{invoiceId:guid}/payments")]
    [HasPermission(Permissions.BillingManage)]
    public async Task<IActionResult> RecordInvoicePayment(Guid invoiceId, RecordInvoicePaymentRequest request, CancellationToken ct)
    {
        var result = await actions.RecordInvoicePaymentAsync(invoiceId, request, ct);
        return result.Replayed ? Ok(result) : StatusCode(StatusCodes.Status201Created, result);
    }

    /// <summary>Records the remaining balance as one payment (idempotent by <c>requestId</c>).</summary>
    [HttpPost("invoices/{invoiceId:guid}/mark-paid")]
    [HasPermission(Permissions.BillingManage)]
    public async Task<IActionResult> MarkInvoicePaid(Guid invoiceId, MarkInvoicePaidRequest request, CancellationToken ct)
    {
        var result = await actions.MarkInvoicePaidAsync(invoiceId, request, ct);
        return result.Replayed ? Ok(result) : StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPost("invoices/{invoiceId:guid}/reminders")]
    [HasPermission(Permissions.BillingManage)]
    public async Task<IActionResult> SendReminder(Guid invoiceId, SendReminderRequest request, CancellationToken ct)
    {
        var result = await reminders.SendNowAsync(invoiceId, request, ct);
        return result.Replayed ? Ok(result) : StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpGet("invoices/{invoiceId:guid}/reminders")]
    [HasPermission(Permissions.BillingView)]
    public Task<IReadOnlyList<ReminderHistoryDto>> ReminderHistory(Guid invoiceId, CancellationToken ct) => reminders.HistoryAsync(invoiceId, ct);

    /// <summary>Edits reference, date, method or notes of a payment (never the amount). Needs a reason; audited.</summary>
    [HttpPatch("invoice-payments/{paymentId:guid}")]
    [HasPermission(Permissions.BillingManage)]
    public Task<PaymentDto> EditPayment(Guid paymentId, UpdatePaymentDetailsRequest request, CancellationToken ct) =>
        actions.EditInvoicePaymentAsync(paymentId, request, ct);

    /// <summary>Sensitive: reverses (Error) or refunds (Refund) a payment; the invoice balance is restored.</summary>
    [HttpPost("invoice-payments/{paymentId:guid}/reverse")]
    [HasPermission(Permissions.BillingManage)]
    public async Task<IActionResult> ReversePayment(Guid paymentId, ReversePaymentRequest request, CancellationToken ct)
    {
        var result = await actions.ReverseInvoicePaymentAsync(paymentId, request, ct);
        return result.Replayed ? Ok(result) : StatusCode(StatusCodes.Status201Created, result);
    }

    /// <summary>Attaches a proof file (PDF, PNG, JPEG, WebP; max 10 MB) to a payment.</summary>
    [HttpPost("invoice-payments/{paymentId:guid}/proofs")]
    [HasPermission(Permissions.BillingManage)]
    [RequestSizeLimit(12 * 1024 * 1024)]
    public async Task<IActionResult> UploadPaymentProof(Guid paymentId, [FromForm] ProofUploadForm form, CancellationToken ct)
    {
        var payment = await payments.LoadScopedAsync(paymentId, ct);
        if (payment.IsReversal) throw DomainException.Conflict("billing.payment_is_reversal", "Attach proof to the original payment.");
        if (await db.Set<PaymentProof>().CountAsync(f => f.PaymentId == paymentId, ct) >= 5)
            throw DomainException.Conflict("payments.too_many_proofs", "A payment can have at most 5 proof files.");
        var dto = await proofs.SaveAsync(form.File, payment.ClientAccountId, payment.InvoiceId, paymentId, null, ct);
        return StatusCode(StatusCodes.Status201Created, dto);
    }

    [HttpGet("proofs/{proofId:guid}")]
    [HasPermission(Permissions.BillingView)]
    public async Task<IActionResult> Proof(Guid proofId, CancellationToken ct)
    {
        var proof = await proofs.FindAsync(proofId, ct);
        if (proof is null || !await (await scope.ApplyAsync(db.Set<Invoice>().AsNoTracking(), i => i.ClientAccountId, ct)).AnyAsync(i => i.Id == proof.InvoiceId, ct))
            throw DomainException.NotFound("File");
        return proofs.Stream(this, proof);
    }

    // ---------------- Incoming: client payment reports ("I've paid")

    [HttpGet("claims")]
    [HasPermission(Permissions.BillingView)]
    public Task<PagedResult<PaymentClaimDto>> Claims([FromQuery] PaymentClaimQuery query, CancellationToken ct) => claims.ListAsync(query, ct);

    /// <summary>Confirms a client's report: records the payment (idempotent by claim id) and closes the claim.</summary>
    [HttpPost("claims/{claimId:guid}/confirm")]
    [HasPermission(Permissions.BillingManage)]
    public Task<ClaimReviewedDto> ConfirmClaim(Guid claimId, ConfirmPaymentClaimRequest request, CancellationToken ct) =>
        claims.ConfirmAsync(claimId, request, ct);

    [HttpPost("claims/{claimId:guid}/reject")]
    [HasPermission(Permissions.BillingManage)]
    public Task<ClaimReviewedDto> RejectClaim(Guid claimId, RejectPaymentClaimRequest request, CancellationToken ct) =>
        claims.RejectAsync(claimId, request, ct);

    // ---------------- Reminder schedules

    [HttpGet("reminders/preview")]
    [HasPermission(Permissions.BillingView)]
    public Task<IReadOnlyList<ReminderPreviewRowDto>> ReminderPreview(CancellationToken ct) => reminders.PreviewAsync(ct);

    [HttpGet("reminder-policies/{clientAccountId:guid}")]
    [HasPermission(Permissions.BillingView)]
    public Task<ReminderPolicyDto> ReminderPolicy(Guid clientAccountId, CancellationToken ct) => reminders.GetPolicyAsync(clientAccountId, ct);

    /// <summary>Sensitive: per-client reminder schedule (billing.settings, reason, audited).</summary>
    [HttpPut("reminder-policies/{clientAccountId:guid}")]
    [HasPermission(Permissions.BillingSettings)]
    public Task<ReminderPolicyDto> UpdateReminderPolicy(Guid clientAccountId, UpdateReminderPolicyRequest request, CancellationToken ct) =>
        reminders.UpdatePolicyAsync(clientAccountId, request, ct);

    // ---------------- Outgoing: payouts (through the Payouts flow; four-eyes and holds apply)

    [HttpPost("payouts/{itemId:guid}/mark-paid")]
    [HasPermission(Permissions.PayoutsRecordPayment)]
    public Task<PayoutActionResultDto> MarkPayoutPaid(Guid itemId, MarkPayoutPaidRequest request, CancellationToken ct) =>
        actions.MarkPayoutPaidAsync(itemId, request, ct);

    [HttpPost("payouts/{itemId:guid}/mark-failed")]
    [HasPermission(Permissions.PayoutsRecordPayment)]
    public Task<PayoutActionResultDto> MarkPayoutFailed(Guid itemId, MarkPayoutFailedRequest request, CancellationToken ct) =>
        actions.MarkPayoutFailedAsync(itemId, request, ct);

    [HttpPost("payout-batches/{batchId:guid}/mark-paid")]
    [HasPermission(Permissions.PayoutsRecordPayment)]
    public Task<BatchPaidResultDto> MarkBatchPaid(Guid batchId, MarkBatchPaidRequest request, CancellationToken ct) =>
        actions.MarkBatchPaidAsync(batchId, request, ct);

    /// <summary>Who is calling and what they may do (drives the page's sections and buttons).</summary>
    [HttpGet("capabilities")]
    [RequireAnyPermission(Permissions.BillingView, Permissions.PayoutsView)]
    public object Capabilities()
    {
        queries.RequireAny();
        return new
        {
            incoming = currentUser.HasPermission(Permissions.BillingView),
            outgoing = currentUser.HasPermission(Permissions.PayoutsView),
            manageIncoming = currentUser.HasPermission(Permissions.BillingManage),
            recordPayouts = currentUser.HasPermission(Permissions.PayoutsRecordPayment),
            reminderSettings = currentUser.HasPermission(Permissions.BillingSettings),
        };
    }
}

public sealed class ProofUploadForm
{
    public IFormFile? File { get; set; }
}

/// <summary>Client portal: payment history of an invoice and "I've paid" reports (Billing or Owner duty).</summary>
[ApiController]
[Route("api/v1/client/billing")]
[HasPermission(Permissions.ClientPortal)]
[DeniedWhileImpersonating(WritesOnly = true)]
public sealed class ClientPaymentsController(PaymentClaimService claims, PaymentProofService proofs, ClientBillingService billing, AppDbContext db) : ControllerBase
{
    [HttpGet("invoices/{invoiceId:guid}/payments")]
    public Task<ClientInvoicePaymentsDto> Payments(Guid invoiceId, CancellationToken ct) => claims.ClientInvoicePaymentsAsync(invoiceId, ct);

    /// <summary>"I've paid": reports a transfer for staff to confirm. Idempotent by <c>requestId</c> (201 new, 200 replay).</summary>
    [HttpPost("invoices/{invoiceId:guid}/payment-claims")]
    public async Task<IActionResult> Claim(Guid invoiceId, SubmitPaymentClaimRequest request, CancellationToken ct)
    {
        var (claim, replayed) = await claims.SubmitAsync(invoiceId, request, ct);
        return replayed ? Ok(claim) : StatusCode(StatusCodes.Status201Created, claim);
    }

    [HttpPost("payment-claims/{claimId:guid}/proofs")]
    [RequestSizeLimit(12 * 1024 * 1024)]
    public async Task<IActionResult> UploadProof(Guid claimId, [FromForm] ProofUploadForm form, CancellationToken ct)
    {
        var claim = await claims.LoadForClientAsync(claimId, ct);
        if (claim.Status != PaymentClaimStatus.Pending)
            throw DomainException.Conflict("payments.claim_state", "Proof can only be added while the report waits for confirmation.");
        if (await db.Set<PaymentProof>().CountAsync(f => f.PaymentClaimId == claimId, ct) >= 3)
            throw DomainException.Conflict("payments.too_many_proofs", "A payment report can have at most 3 files.");
        var dto = await proofs.SaveAsync(form.File, claim.ClientAccountId, claim.InvoiceId, null, claimId, ct);
        return StatusCode(StatusCodes.Status201Created, dto with { Url = $"/api/v1/client/billing/payment-proofs/{dto.Id}" });
    }

    [HttpGet("payment-proofs/{proofId:guid}")]
    public async Task<IActionResult> Proof(Guid proofId, CancellationToken ct)
    {
        var ids = await billing.BillingClientIdsAsync(ct);
        var proof = await proofs.FindAsync(proofId, ct);
        if (proof is null || proof.PaymentClaimId is null || !ids.Contains(proof.ClientAccountId)) throw DomainException.NotFound("File");
        return proofs.Stream(this, proof);
    }
}
