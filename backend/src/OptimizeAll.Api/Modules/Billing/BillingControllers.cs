using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Common.Jobs;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.Billing;
using OptimizeAll.Domain.Common;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Billing;

internal static class HtmlDownload
{
    public static ContentResult Html(ControllerBase controller, string html, string fileName, bool attachment)
    {
        controller.Response.Headers.ContentDisposition = $"{(attachment ? "attachment" : "inline")}; filename=\"{fileName}\"";
        return new ContentResult { Content = html, ContentType = "text/html; charset=utf-8", StatusCode = 200 };
    }

    public static string SafeFileName(string? number) =>
        "invoice-" + new string((number ?? "draft").Where(c => char.IsAsciiLetterOrDigit(c) || c == '-').ToArray()) + ".html";
}

[ApiController]
[Route("api/v1/agency/billing/invoices")]
[HasPermission(Permissions.BillingView)]
public sealed class AgencyInvoicesController(InvoiceService invoices, PaymentService payments, ClientBillingService clientBilling) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<InvoiceSummaryDto>> List([FromQuery] InvoiceQuery query, CancellationToken ct) => invoices.ListAsync(query, ct);

    [HttpGet("{id:guid}")]
    public Task<InvoiceDto> Get(Guid id, CancellationToken ct) => invoices.GetAsync(id, ct);

    /// <summary>Printable HTML invoice (download).</summary>
    [HttpGet("{id:guid}/document")]
    public async Task<ContentResult> Document(Guid id, CancellationToken ct)
    {
        var invoice = await invoices.LoadScopedAsync(id, ct);
        return HtmlDownload.Html(this, InvoiceDocument.Render(await clientBilling.PublicViewAsync(invoice, ct)),
            HtmlDownload.SafeFileName(invoice.Number), attachment: true);
    }

    [HttpPost("preview")]
    [HasPermission(Permissions.BillingManage)]
    public Task<PreviewResponse> Preview(PreviewRequest request, CancellationToken ct) => invoices.PreviewAsync(request, allowRecurrence: false, ct);

    [HttpPost]
    [HasPermission(Permissions.BillingManage)]
    [ProducesResponseType(typeof(InvoiceDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(InvoiceDraftRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await invoices.CreateDraftAsync(request, ct));

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.BillingManage)]
    public Task<InvoiceDto> Update(Guid id, InvoiceDraftRequest request, CancellationToken ct) => invoices.UpdateDraftAsync(id, request, ct);

    [HttpDelete("{id:guid}")]
    [HasPermission(Permissions.BillingManage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await invoices.DeleteDraftAsync(id, ct);
        return NoContent();
    }

    /// <summary>Copies an invoice (any status) into a new draft for the same client.</summary>
    [HttpPost("{id:guid}/duplicate")]
    [HasPermission(Permissions.BillingManage)]
    [ProducesResponseType(typeof(InvoiceDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> Duplicate(Guid id, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await invoices.DuplicateAsync(id, ct));

    [HttpPost("{id:guid}/issue")]
    [HasPermission(Permissions.BillingManage)]
    public Task<InvoiceDto> Issue(Guid id, IssueInvoiceRequest request, CancellationToken ct) => invoices.IssueAsync(id, request, ct);

    [HttpPost("{id:guid}/send")]
    [HasPermission(Permissions.BillingManage)]
    public Task<InvoiceDto> Send(Guid id, CancellationToken ct) => invoices.SendAsync(id, ct);

    /// <summary>Sensitive: void an issued invoice without payments (billing.manage, reason, confirm, not the issuer).</summary>
    [HttpPost("{id:guid}/void")]
    [HasPermission(Permissions.BillingManage)]
    public Task<InvoiceDto> Void(Guid id, SensitiveInvoiceActionRequest request, CancellationToken ct) => invoices.VoidAsync(id, request, ct);

    /// <summary>Sensitive: write off the remaining balance (billing.manage, reason, confirm, not the issuer).</summary>
    [HttpPost("{id:guid}/write-off")]
    [HasPermission(Permissions.BillingManage)]
    public Task<InvoiceDto> WriteOff(Guid id, SensitiveInvoiceActionRequest request, CancellationToken ct) => invoices.WriteOffAsync(id, request, ct);

    /// <summary>Records a manual payment. Idempotent by <c>requestId</c>; 409 on overpayment or a stale invoice stamp.</summary>
    [HttpPost("{id:guid}/payments")]
    [HasPermission(Permissions.BillingManage)]
    [DeniedWhileImpersonating]
    public async Task<IActionResult> RecordPayment(Guid id, RecordPaymentRequest request, CancellationToken ct)
    {
        var result = await payments.RecordAsync(id, request, ct);
        return result.Replayed ? Ok(result) : StatusCode(StatusCodes.Status201Created, result);
    }
}

[ApiController]
[Route("api/v1/agency/billing/payments")]
[HasPermission(Permissions.BillingView)]
[DeniedWhileImpersonating(WritesOnly = true)]
public sealed class AgencyPaymentsController(PaymentService payments) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<PaymentDto>> List([FromQuery] PaymentQuery query, CancellationToken ct) => payments.ListAsync(query, ct);
}

[ApiController]
[Route("api/v1/agency/billing/credit-notes")]
[HasPermission(Permissions.BillingView)]
[DeniedWhileImpersonating(WritesOnly = true)]
public sealed class AgencyCreditNotesController(CreditNoteService creditNotes) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<CreditNoteDto>> List([FromQuery] CreditNoteQuery query, CancellationToken ct) => creditNotes.ListAsync(query, ct);

    [HttpGet("{id:guid}")]
    public Task<CreditNoteDto> Get(Guid id, CancellationToken ct) => creditNotes.GetAsync(id, ct);

    [HttpPost]
    [HasPermission(Permissions.BillingManage)]
    [ProducesResponseType(typeof(CreditNoteDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(CreateCreditNoteRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await creditNotes.CreateAsync(request, ct));

    [HttpPost("{id:guid}/apply")]
    [HasPermission(Permissions.BillingManage)]
    public Task<CreditNoteDto> Apply(Guid id, ApplyCreditNoteRequest request, CancellationToken ct) => creditNotes.ApplyAsync(id, request, ct);
}

[ApiController]
[Route("api/v1/agency/billing")]
[HasPermission(Permissions.BillingView)]
public sealed class AgencyBillingController(
    AppDbContext db,
    BillingReports reports,
    BillingSettingsService settings,
    ClientBillingService clientBilling,
    IAuditLogger audit,
    ICurrentUser currentUser,
    IClientScope scope,
    JobRunner jobs) : ControllerBase
{
    [HttpGet("overview")]
    public Task<BillingOverviewDto> Overview(CancellationToken ct) => reports.OverviewAsync(ct);

    /// <summary>Client accounts for pickers (id, name, currency, billing email).</summary>
    [HttpGet("clients")]
    public async Task<IReadOnlyList<ClientOptionDto>> Clients([FromQuery] string? search, CancellationToken ct)
    {
        var clients = await scope.ApplyAsync(db.Set<ClientAccount>().AsNoTracking(), c => c.Id, ct);
        if (!string.IsNullOrWhiteSpace(search)) clients = clients.Where(c => EF.Functions.Like(c.Name, PagingExtensions.LikePattern(search), "\\"));
        return await clients.OrderBy(c => c.Name).Take(200)
            .Select(c => new ClientOptionDto(c.Id, c.Name, c.Slug, c.Currency, c.CountryCode, c.Status.ToString(), c.BillingEmail)).ToListAsync(ct);
    }

    [HttpGet("clients/{clientAccountId:guid}/statement")]
    public async Task<StatementDto> Statement(Guid clientAccountId, [FromQuery] string? currency, [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to, CancellationToken ct)
    {
        await scope.EnsureAccessAsync(clientAccountId, ct: ct);
        return await clientBilling.BuildStatementAsync(clientAccountId, currency, from, to, ct);
    }

    // ---------------- Reports

    [HttpGet("reports/aging")]
    public Task<AgingReportDto> Aging([FromQuery] DateOnly? asOf, CancellationToken ct) => reports.AgingAsync(asOf, ct);

    [HttpGet("reports/aging.csv")]
    public async Task<FileContentResult> AgingCsv([FromQuery] DateOnly? asOf, CancellationToken ct)
    {
        var r = await reports.AgingAsync(asOf, ct);
        return Csv.File($"ar-aging-{r.AsOf:yyyy-MM-dd}.csv",
            new[] { "Client", "Currency", "Current", "1-30", "31-60", "61-90", "90+", "Total" },
            r.Rows.Concat(r.Totals).Select(x => new object?[] { x.ClientName, x.Currency, x.Current, x.Days1To30, x.Days31To60, x.Days61To90, x.Over90, x.Total }));
    }

    [HttpGet("reports/revenue")]
    public Task<RevenueReportDto> Revenue([FromQuery] string? groupBy, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct) =>
        reports.RevenueAsync(groupBy ?? "month", from, to, ct);

    [HttpGet("reports/revenue.csv")]
    public async Task<FileContentResult> RevenueCsv([FromQuery] string? groupBy, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
    {
        var r = await reports.RevenueAsync(groupBy ?? "month", from, to, ct);
        return Csv.File($"revenue-by-{r.GroupBy}-{r.From:yyyy-MM-dd}-{r.To:yyyy-MM-dd}.csv",
            new[] { r.GroupBy == "month" ? "Month" : r.GroupBy == "service" ? "Service" : "Client", "Currency", "Invoiced (net)", "Collected" },
            r.Rows.Select(x => new object?[] { x.Label, x.Currency, x.Invoiced, x.Collected }));
    }

    [HttpGet("reports/mrr")]
    public Task<MrrReportDto> Mrr(CancellationToken ct) => reports.MrrAsync(ct);

    [HttpGet("reports/mrr.csv")]
    public async Task<FileContentResult> MrrCsv(CancellationToken ct)
    {
        var r = await reports.MrrAsync(ct);
        return Csv.File("mrr-arr.csv", new[] { "Client", "Currency", "Active contracts", "MRR", "ARR" },
            r.Rows.Select(x => new object?[] { x.ClientName, x.Currency, x.ActiveContracts, x.Mrr, x.Arr }));
    }

    [HttpGet("reports/collections")]
    public Task<CollectionsReportDto> Collections([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct) =>
        reports.CollectionsAsync(from, to, ct);

    [HttpGet("reports/collections.csv")]
    public async Task<FileContentResult> CollectionsCsv([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
    {
        var r = await reports.CollectionsAsync(from, to, ct);
        return Csv.File($"collections-{r.From:yyyy-MM-dd}-{r.To:yyyy-MM-dd}.csv", new[] { "Month", "Currency", "Method", "Payments", "Amount" },
            r.Rows.Select(x => new object?[] { x.Month, x.Currency, x.Method.ToString(), x.Payments, x.Amount }));
    }

    // ---------------- Settings & tax rates

    [HttpGet("settings")]
    public Task<BillingSettings> GetSettings(CancellationToken ct) => settings.GetAsync(ct);

    /// <summary>Sensitive: numbering, terms, reminders and payment instructions (billing.settings, reason, confirm).</summary>
    [HttpPut("settings")]
    [HasPermission(Permissions.BillingSettings)]
    public async Task<BillingSettings> UpdateSettings(UpdateBillingSettingsRequest request, CancellationToken ct)
    {
        if (!request.Confirm) throw new DomainException("request.confirm_required", "Confirm this change by sending \"confirm\": true.");
        var before = await settings.GetAsync(ct);
        var value = request.Settings!;
        if (value.DefaultTaxRateId is { } rateId && !await db.Set<TaxRate>().AnyAsync(t => t.Id == rateId && t.IsActive, ct))
            throw new DomainException("billing.invalid_tax_rate", "The default tax rate doesn't exist or is inactive.");
        await settings.SaveAsync(value, currentUser.Id, ct);
        audit.Record("billing.settings_updated", "BillingSettings", BillingSettingsService.Key, before, value, request.Reason.Trim());
        await db.SaveChangesAsync(ct);
        return await settings.GetAsync(ct);
    }

    [HttpGet("tax-rates")]
    public async Task<IReadOnlyList<TaxRateDto>> TaxRates([FromQuery] bool includeInactive, CancellationToken ct)
    {
        var rates = db.Set<TaxRate>().AsNoTracking();
        if (!includeInactive) rates = rates.Where(t => t.IsActive);
        return (await rates.ToListAsync(ct)).OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).Select(ToDto).ToList();
    }

    [HttpPost("tax-rates")]
    [HasPermission(Permissions.BillingSettings)]
    [ProducesResponseType(typeof(TaxRateDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateTaxRate(TaxRateRequest request, CancellationToken ct)
    {
        var rate = new TaxRate();
        Apply(rate, request);
        db.Set<TaxRate>().Add(rate);
        audit.Record("billing.tax_rate_created", nameof(TaxRate), rate.Id, after: ToDto(rate));
        await db.SaveChangesAsync(ct);
        return StatusCode(StatusCodes.Status201Created, ToDto(rate));
    }

    /// <summary>Updating a rate never changes existing documents (lines keep their tax snapshot).</summary>
    [HttpPut("tax-rates/{id:guid}")]
    [HasPermission(Permissions.BillingSettings)]
    public async Task<TaxRateDto> UpdateTaxRate(Guid id, TaxRateRequest request, CancellationToken ct)
    {
        var rate = await db.Set<TaxRate>().FirstOrDefaultAsync(t => t.Id == id, ct) ?? throw DomainException.NotFound("TaxRate");
        if (request.ConcurrencyStamp is null || request.ConcurrencyStamp != rate.ConcurrencyStamp)
            throw DomainException.Conflict("concurrency.conflict", "This tax rate was changed by someone else. Reload and try again.");
        db.Entry(rate).Property(r => r.ConcurrencyStamp).OriginalValue = request.ConcurrencyStamp.Value;
        var before = ToDto(rate);
        Apply(rate, request);
        audit.Record("billing.tax_rate_updated", nameof(TaxRate), id, before, ToDto(rate));
        await db.SaveChangesAsync(ct);
        return ToDto(rate);
    }

    /// <summary>
    /// Deletes a tax rate that no document, catalog item, template or setting uses. A rate in use is deactivated instead
    /// (issued documents keep their snapshot either way).
    /// </summary>
    [HttpDelete("tax-rates/{id:guid}")]
    [HasPermission(Permissions.BillingSettings)]
    public async Task<IActionResult> DeleteTaxRate(Guid id, CancellationToken ct)
    {
        var rate = await db.Set<TaxRate>().FirstOrDefaultAsync(t => t.Id == id, ct) ?? throw DomainException.NotFound("TaxRate");
        var inUse = await db.Set<InvoiceLine>().AnyAsync(l => l.TaxRateId == id, ct) || await db.Set<ContractLine>().AnyAsync(l => l.TaxRateId == id, ct) ||
                    await db.Set<Domain.Crm.ProposalLine>().AnyAsync(l => l.TaxRateId == id, ct) ||
                    await db.Set<ServiceCatalogItem>().AnyAsync(i => i.TaxRateId == id, ct) ||
                    (await settings.GetAsync(ct)).DefaultTaxRateId == id ||
                    (await db.Set<Domain.Crm.ProposalTemplate>().AsNoTracking().Select(t => t.Lines).ToListAsync(ct)).Any(ls => ls.Any(l => l.TaxRateId == id));
        if (inUse)
            throw DomainException.Conflict("billing.tax_rate_in_use",
                "This tax rate is used by documents, catalog items, templates or the default setting. Deactivate it instead.");
        db.Remove(rate);
        audit.Record("billing.tax_rate_deleted", nameof(TaxRate), id, before: ToDto(rate));
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Runs the recurring-invoice job now (same idempotent logic as the schedule).</summary>
    [HttpPost("jobs/recurring-invoices/run")]
    [HasPermission(Permissions.BillingManage)]
    public async Task<IActionResult> RunRecurring(CancellationToken ct)
    {
        var run = await jobs.RunAsync<RecurringInvoiceJob>(ct);
        return Ok(new { ran = run is not null, status = run?.Status.ToString(), summary = run?.Summary ?? "Another instance is running the job." });
    }

    private static void Apply(TaxRate rate, TaxRateRequest request)
    {
        if (decimal.Round(request.RatePercent, 4) != request.RatePercent)
            throw new DomainException("billing.invalid_tax", "Use at most 4 decimals for the rate.");
        rate.Name = request.Name.Trim();
        rate.RatePercent = request.RatePercent;
        rate.Inclusive = request.Inclusive;
        rate.CountryCode = string.IsNullOrWhiteSpace(request.CountryCode) ? null : request.CountryCode.Trim().ToUpperInvariant();
        rate.IsActive = request.IsActive;
        rate.NeedsReview = request.NeedsReview;
        rate.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
    }

    private static TaxRateDto ToDto(TaxRate t) =>
        new(t.Id, t.Name, t.RatePercent, t.Inclusive, t.CountryCode, t.IsActive, t.NeedsReview, t.Notes, t.ConcurrencyStamp);
}

[ApiController]
[Route("api/v1/agency/contracts")]
[HasPermission(Permissions.ContractsManage)]
public sealed class AgencyContractsController(ContractService contracts, RecurringBillingService recurring) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<ContractSummaryDto>> List([FromQuery] ContractQuery query, CancellationToken ct) => contracts.ListAsync(query, ct);

    [HttpGet("{id:guid}")]
    public Task<ContractDto> Get(Guid id, CancellationToken ct) => contracts.GetAsync(id, ct);

    [HttpPost]
    [ProducesResponseType(typeof(ContractDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(ContractRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await contracts.CreateAsync(request, ct));

    [HttpPut("{id:guid}")]
    public Task<ContractDto> Update(Guid id, ContractRequest request, CancellationToken ct) => contracts.UpdateAsync(id, request, ct);

    [HttpPost("{id:guid}/activate")]
    public Task<ContractDto> Activate(Guid id, StampRequest request, CancellationToken ct) => contracts.ActivateAsync(id, request, ct);

    [HttpPost("{id:guid}/pause")]
    public Task<ContractDto> Pause(Guid id, StampRequest request, CancellationToken ct) => contracts.PauseAsync(id, request, ct);

    [HttpPost("{id:guid}/resume")]
    public Task<ContractDto> Resume(Guid id, StampRequest request, CancellationToken ct) => contracts.ResumeAsync(id, request, ct);

    [HttpPost("{id:guid}/cancel")]
    public Task<ContractDto> Cancel(Guid id, CancelContractRequest request, CancellationToken ct) => contracts.CancelAsync(id, request, ct);

    /// <summary>Deletes a draft contract that never billed (otherwise cancel it).</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, [FromQuery] Guid? concurrencyStamp, CancellationToken ct)
    {
        await contracts.DeleteDraftAsync(id, concurrencyStamp, ct);
        return NoContent();
    }

    /// <summary>Generates any invoices that are due for this contract now (idempotent per period).</summary>
    [HttpPost("{id:guid}/generate-invoices")]
    public async Task<ContractDto> Generate(Guid id, CancellationToken ct)
    {
        await contracts.GetAsync(id, ct);
        await recurring.RunAsync(ct, id);
        return await contracts.GetAsync(id, ct);
    }
}

/// <summary>Client portal billing (Billing or Owner duty).</summary>
[ApiController]
[Route("api/v1/client/billing")]
[HasPermission(Permissions.ClientPortal)]
public sealed class ClientBillingController(ClientBillingService billing) : ControllerBase
{
    [HttpGet("summary")]
    public Task<ClientBillingSummaryDto> Summary(CancellationToken ct) => billing.SummaryAsync(ct);

    [HttpGet("invoices")]
    public Task<PagedResult<InvoiceSummaryDto>> Invoices([FromQuery] InvoiceQuery query, CancellationToken ct) => billing.InvoicesAsync(query, ct);

    [HttpGet("invoices/{id:guid}")]
    public Task<PublicInvoiceDto> Invoice(Guid id, CancellationToken ct) => billing.InvoiceAsync(id, ct);

    [HttpGet("invoices/{id:guid}/document")]
    public async Task<ContentResult> Document(Guid id, CancellationToken ct)
    {
        var view = await billing.InvoiceAsync(id, ct);
        return HtmlDownload.Html(this, InvoiceDocument.Render(view), HtmlDownload.SafeFileName(view.Number), attachment: true);
    }

    /// <summary>Online payment through the configured gateway; reports "not available" when none is configured.</summary>
    [HttpPost("invoices/{id:guid}/pay")]
    [DeniedWhileImpersonating]
    public Task<OnlinePaymentDto> Pay(Guid id, CancellationToken ct) => billing.PayOnlineAsync(id, ct);

    [HttpGet("payment-instructions")]
    public async Task<PaymentInstructionsDto> Instructions(CancellationToken ct)
    {
        await billing.BillingClientIdsAsync(ct);
        return await billing.InstructionsAsync(ct);
    }

    [HttpGet("contracts")]
    public Task<IReadOnlyList<ContractSummaryDto>> Contracts(CancellationToken ct) => billing.ContractsAsync(ct);

    [HttpGet("statement")]
    public Task<StatementDto> Statement([FromQuery] Guid clientAccountId, [FromQuery] string? currency, [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to, CancellationToken ct) => billing.StatementAsync(clientAccountId, currency, from, to, ct);
}

/// <summary>Public, tokenized invoice view (<c>/i/{token}</c>). Anonymous and rate limited; the token is 256-bit random.</summary>
[ApiController]
[AllowAnonymous]
[EnableRateLimiting(RateLimitPolicies.Public)]
[Route("api/v1/public/invoices/{token}")]
public sealed class PublicInvoiceController(AppDbContext db, ClientBillingService billing) : ControllerBase
{
    [HttpGet]
    public async Task<PublicInvoiceDto> Get(string token, CancellationToken ct) => await billing.PublicViewAsync(await FindAsync(token, ct), ct);

    [HttpGet("document")]
    public async Task<ContentResult> Document(string token, CancellationToken ct)
    {
        var view = await billing.PublicViewAsync(await FindAsync(token, ct), ct);
        return HtmlDownload.Html(this, InvoiceDocument.Render(view), HtmlDownload.SafeFileName(view.Number), attachment: true);
    }

    private async Task<Invoice> FindAsync(string token, CancellationToken ct)
    {
        if (!PublicLinkTokens.IsWellFormed(token)) throw DomainException.NotFound("Invoice");
        var hash = PublicLinkTokens.Hash(token);
        return await db.Set<Invoice>().AsNoTracking().Include(i => i.Lines)
                   .FirstOrDefaultAsync(i => i.PublicTokenHash == hash && i.Status != InvoiceStatus.Draft, ct)
               ?? throw DomainException.NotFound("Invoice");
    }
}
