using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.Billing;
using OptimizeAll.Domain.Common;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Billing;

/// <summary>
/// Client-portal billing. Only members with the Billing or Owner duty see billing; every query is limited to their
/// organizations (another organization's invoice answers 404, a Viewer/Approver member gets 403).
/// </summary>
public sealed class ClientBillingService(
    AppDbContext db,
    IClientScope scope,
    InvoiceService invoices,
    BillingSettingsService settingsService,
    IClientPaymentGateway gateway,
    TimeProvider clock)
{
    /// <summary>Organizations where the caller holds the Billing or Owner duty (403 when there are none).</summary>
    public async Task<IReadOnlyList<Guid>> BillingClientIdsAsync(CancellationToken ct)
    {
        var result = new List<Guid>();
        foreach (var id in await scope.MemberClientIdsAsync(ct))
            if (await scope.MemberRoleAsync(id, ct) is ClientMemberRole.Billing or ClientMemberRole.Owner)
                result.Add(id);
        if (result.Count == 0)
            throw DomainException.Forbidden("client.insufficient_role",
                "Billing is only available to members with the Billing or Owner role in your organization.");
        return result;
    }

    public async Task<ClientBillingSummaryDto> SummaryAsync(CancellationToken ct)
    {
        var ids = await BillingClientIdsAsync(ct);
        var orgs = await db.Set<ClientAccount>().AsNoTracking().Where(c => ids.Contains(c.Id)).OrderBy(c => c.Name).ToListAsync(ct);
        var roles = new List<ClientOrgBillingDto>();
        foreach (var o in orgs) roles.Add(new ClientOrgBillingDto(o.Id, o.Name, o.Currency, (await scope.MemberRoleAsync(o.Id, ct))!.Value.ToString()));
        var open = await db.Set<Invoice>().AsNoTracking().Where(i => ids.Contains(i.ClientAccountId) && Invoice.OpenStatuses.Contains(i.Status)).ToListAsync(ct);
        var today = BillingDates.Today(clock);
        var awaiting = await db.Set<Domain.Crm.Proposal>().AsNoTracking().CountAsync(p => p.ClientAccountId != null &&
            ids.Contains(p.ClientAccountId.Value) && (p.Status == Domain.Crm.ProposalStatus.Sent || p.Status == Domain.Crm.ProposalStatus.Viewed), ct);
        return new ClientBillingSummaryDto(roles,
            BillingReports.ByCurrency(open.Select(i => (i.Currency, i.Balance))),
            BillingReports.ByCurrency(open.Where(i => i.DueDate < today).Select(i => (i.Currency, i.Balance))),
            open.Count, awaiting);
    }

    public async Task<PagedResult<InvoiceSummaryDto>> InvoicesAsync(InvoiceQuery query, CancellationToken ct)
    {
        var ids = await BillingClientIdsAsync(ct);
        var rows = db.Set<Invoice>().AsNoTracking().Where(i => ids.Contains(i.ClientAccountId) && i.Status != InvoiceStatus.Draft);
        if (query.ClientAccountId is { } clientId) rows = rows.Where(i => i.ClientAccountId == clientId);
        if (query.Status is { } status) rows = rows.Where(i => i.Status == status);
        if (query.OpenOnly == true) rows = rows.Where(i => Invoice.OpenStatuses.Contains(i.Status));
        var total = await rows.CountAsync(ct);
        var page = await rows.OrderByDescending(i => i.IssueDate).ThenByDescending(i => i.CreatedAt).Skip(query.Skip).Take(query.PageSize).ToListAsync(ct);
        return new PagedResult<InvoiceSummaryDto>(await invoices.SummariesAsync(page, ct), total, query.Page, query.PageSize);
    }

    public async Task<Invoice> LoadInvoiceAsync(Guid id, CancellationToken ct)
    {
        var invoice = await db.Set<Invoice>().AsNoTracking().Include(i => i.Lines)
            .FirstOrDefaultAsync(i => i.Id == id && i.Status != InvoiceStatus.Draft, ct) ?? throw DomainException.NotFound("Invoice");
        await scope.EnsureAccessAsync(invoice.ClientAccountId, ClientMemberRole.Billing, ct);
        return invoice;
    }

    public async Task<PublicInvoiceDto> InvoiceAsync(Guid id, CancellationToken ct) => await PublicViewAsync(await LoadInvoiceAsync(id, ct), ct);

    public async Task<OnlinePaymentDto> PayOnlineAsync(Guid id, CancellationToken ct)
    {
        var invoice = await LoadInvoiceAsync(id, ct);
        if (!Invoice.IsOpen(invoice.Status)) return new OnlinePaymentDto(false, null, "This invoice has nothing left to pay.");
        var link = await gateway.CreatePaymentLinkAsync(invoice, ct);
        return new OnlinePaymentDto(link.Configured && link.RedirectUrl is not null, link.RedirectUrl, link.Message);
    }

    public async Task<PublicInvoiceDto> PublicViewAsync(Invoice invoice, CancellationToken ct)
    {
        var client = await db.Set<ClientAccount>().AsNoTracking().FirstAsync(c => c.Id == invoice.ClientAccountId, ct);
        var lines = invoice.Lines.OrderBy(l => l.Position).ToList();
        return new PublicInvoiceDto(invoice.Number, client.Name, client.BillingAddress, client.TaxId, invoice.Status, invoice.Currency,
            invoice.IssueDate, invoice.DueDate, LineBuilder.ToDto(LineBuilder.TotalsOf(lines, invoice.Currency)), invoice.AmountPaid,
            invoice.AmountCredited, invoice.Balance, invoice.Notes, invoice.PeriodStart, invoice.PeriodEnd,
            lines.Select(LineBuilder.ToDto).ToList(), await InstructionsAsync(ct));
    }

    public async Task<PaymentInstructionsDto> InstructionsAsync(CancellationToken ct)
    {
        var s = await settingsService.GetAsync(ct);
        return new PaymentInstructionsDto(s.CompanyName, s.CompanyAddress, s.CompanyTaxId, s.CompanyEmail, s.BankDetails, s.PaymentLinkText,
            s.PaymentInstructions, s.InvoiceFooter, gateway.IsConfigured);
    }

    public async Task<IReadOnlyList<ContractSummaryDto>> ContractsAsync(CancellationToken ct)
    {
        var ids = await BillingClientIdsAsync(ct);
        var rows = await db.Set<Contract>().AsNoTracking().Include(c => c.Lines)
            .Where(c => ids.Contains(c.ClientAccountId) && c.Status != ContractStatus.Draft).OrderByDescending(c => c.StartDate).ToListAsync(ct);
        var names = await db.Set<ClientAccount>().AsNoTracking().Where(c => ids.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        return rows.Select(c => ContractService.Summary(c, names.GetValueOrDefault(c.ClientAccountId, "—"))).ToList();
    }

    /// <summary>Statement of account for one organization and currency: invoices (debits), payments/credits/write-offs (credits).</summary>
    public async Task<StatementDto> StatementAsync(Guid clientAccountId, string? currency, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var ids = await BillingClientIdsAsync(ct);
        if (!ids.Contains(clientAccountId)) await scope.EnsureAccessAsync(clientAccountId, ClientMemberRole.Billing, ct);
        return await BuildStatementAsync(clientAccountId, currency, from, to, ct);
    }

    public async Task<StatementDto> BuildStatementAsync(Guid clientAccountId, string? currency, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var client = await db.Set<ClientAccount>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == clientAccountId, ct)
                     ?? throw DomainException.NotFound("Client");
        var cur = LineBuilder.NormalizeCurrency(currency ?? client.Currency);
        var end = to ?? BillingDates.Today(clock);
        var start = from ?? end.AddMonths(-12);
        if (start > end) throw new DomainException("billing.invalid_range", "The start date must be before the end date.");

        var issued = await db.Set<Invoice>().AsNoTracking().Where(i => i.ClientAccountId == clientAccountId && i.Currency == cur &&
            i.IssueDate != null && i.IssueDate <= end && i.Status != InvoiceStatus.Draft && i.Status != InvoiceStatus.Void).ToListAsync(ct);
        var paid = await db.Set<Payment>().AsNoTracking().Where(p => p.ClientAccountId == clientAccountId && p.Currency == cur && p.PaidOn <= end)
            .ToListAsync(ct);
        var credits = await (from a in db.Set<CreditNoteApplication>().AsNoTracking()
                             join n in db.Set<CreditNote>().AsNoTracking() on a.CreditNoteId equals n.Id
                             join i in db.Set<Invoice>().AsNoTracking() on a.InvoiceId equals i.Id
                             where n.ClientAccountId == clientAccountId && n.Currency == cur
                             select new { a.AppliedAt, n.Number, InvoiceNumber = i.Number, a.Amount }).ToListAsync(ct);
        var numbers = issued.ToDictionary(i => i.Id, i => i.Number);

        var entries = new List<(DateOnly Date, int Order, string Type, string Reference, string Description, decimal Debit, decimal Credit)>();
        foreach (var i in issued)
        {
            entries.Add((i.IssueDate!.Value, 0, "Invoice", i.Number ?? "—", i.Notes ?? "Invoice", i.Total, 0));
            if (i.Status == InvoiceStatus.WrittenOff && i.WrittenOffAt is { } w)
                entries.Add((DateOnly.FromDateTime(w), 3, "Write-off", i.Number ?? "—", "Balance written off", 0, i.AmountWrittenOff));
        }
        foreach (var p in paid)
            entries.Add((p.PaidOn, 1, "Payment", p.Reference, $"Payment for {numbers.GetValueOrDefault(p.InvoiceId) ?? "invoice"} ({p.Method})", 0, p.Amount));
        foreach (var c in credits)
            entries.Add((DateOnly.FromDateTime(c.AppliedAt), 2, "Credit note", c.Number, $"Credit applied to {c.InvoiceNumber}", 0, c.Amount));

        var ordered = entries.Where(e => e.Date <= end).OrderBy(e => e.Date).ThenBy(e => e.Order).ToList();
        var opening = ordered.Where(e => e.Date < start).Sum(e => e.Debit - e.Credit);
        var balance = opening;
        var lines = new List<StatementLineDto>();
        foreach (var e in ordered.Where(e => e.Date >= start))
        {
            balance += e.Debit - e.Credit;
            lines.Add(new StatementLineDto(e.Date, e.Type, e.Reference, e.Description, e.Debit, e.Credit, balance));
        }
        return new StatementDto(clientAccountId, client.Name, cur, start, end, opening, lines, balance);
    }
}

/// <summary>Printable, self-contained HTML invoice (the "PDF-less" download). All values are HTML-encoded.</summary>
public static class InvoiceDocument
{
    public static string Render(PublicInvoiceDto invoice)
    {
        string E(string? s) => WebUtility.HtmlEncode(s ?? string.Empty);
        string M(decimal v) => v.ToString("N" + Money.MinorUnitDigits(invoice.Currency), CultureInfo.InvariantCulture) + " " + invoice.Currency;
        string D(DateOnly? d) => d?.ToString("d MMM yyyy", CultureInfo.InvariantCulture) ?? "—";
        string Multi(string? s) => E(s).Replace("\n", "<br>");
        var p = invoice.Payment;
        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\"><title>Invoice ").Append(E(invoice.Number)).Append("</title>")
          .Append("<style>body{font-family:Inter,Arial,Helvetica,sans-serif;color:#1D174C;margin:0;padding:32px;font-size:14px}")
          .Append("h1{color:#1F2659;margin:0 0 4px}table{width:100%;border-collapse:collapse;margin-top:24px}th,td{padding:8px;border-bottom:1px solid #d8dbe8;text-align:left}")
          .Append("th{background:#f3f4fa}td.n,th.n{text-align:right;font-variant-numeric:tabular-nums}.head{display:flex;justify-content:space-between;gap:24px}")
          .Append(".totals{margin-left:auto;width:320px}.totals td{border:none}.totals tr.grand td{font-weight:700;border-top:2px solid #1F2659}")
          .Append(".muted{color:#555a7a}.box{margin-top:24px;padding:12px 16px;border:1px solid #d8dbe8;border-radius:8px}")
          .Append("@media print{body{padding:0}.box{break-inside:avoid}}</style></head><body>");
        sb.Append("<div class=\"head\"><div><h1>").Append(E(p.CompanyName)).Append("</h1><div class=\"muted\">").Append(Multi(p.CompanyAddress)).Append("</div>");
        if (!string.IsNullOrWhiteSpace(p.CompanyTaxId)) sb.Append("<div class=\"muted\">Tax ID: ").Append(E(p.CompanyTaxId)).Append("</div>");
        sb.Append("</div><div><h2>Invoice ").Append(E(invoice.Number)).Append("</h2><div>Status: ").Append(E(invoice.Status.ToString())).Append("</div>")
          .Append("<div>Issued: ").Append(D(invoice.IssueDate)).Append("</div><div>Due: ").Append(D(invoice.DueDate)).Append("</div>");
        if (invoice.PeriodStart is not null)
            sb.Append("<div>Period: ").Append(D(invoice.PeriodStart)).Append(" – ").Append(D(invoice.PeriodEnd)).Append("</div>");
        sb.Append("</div></div><div class=\"box\"><strong>Bill to</strong><div>").Append(E(invoice.ClientName)).Append("</div><div class=\"muted\">")
          .Append(Multi(invoice.ClientAddress)).Append("</div>");
        if (!string.IsNullOrWhiteSpace(invoice.ClientTaxId)) sb.Append("<div class=\"muted\">Tax ID: ").Append(E(invoice.ClientTaxId)).Append("</div>");
        sb.Append("</div><table><thead><tr><th>Description</th><th class=\"n\">Qty</th><th class=\"n\">Unit price</th><th class=\"n\">Discount</th><th>Tax</th><th class=\"n\">Amount</th></tr></thead><tbody>");
        foreach (var l in invoice.Lines)
            sb.Append("<tr><td>").Append(E(l.Description)).Append("</td><td class=\"n\">").Append(l.Quantity.ToString("0.####", CultureInfo.InvariantCulture))
              .Append("</td><td class=\"n\">").Append(M(l.UnitPrice)).Append("</td><td class=\"n\">").Append(l.DiscountAmount > 0 ? M(l.DiscountAmount) : "—")
              .Append("</td><td>").Append(l.TaxPercent > 0 ? E($"{l.TaxName} {l.TaxPercent:0.##}%{(l.TaxInclusive ? " incl." : string.Empty)}") : "—")
              .Append("</td><td class=\"n\">").Append(M(l.Total)).Append("</td></tr>");
        sb.Append("</tbody></table><table class=\"totals\"><tbody>")
          .Append("<tr><td>Subtotal</td><td class=\"n\">").Append(M(invoice.Totals.Subtotal)).Append("</td></tr>");
        foreach (var t in invoice.Totals.Taxes)
            sb.Append("<tr><td>").Append(E($"{t.Name} ({t.RatePercent:0.##}%{(t.Inclusive ? ", included" : string.Empty)})")).Append("</td><td class=\"n\">")
              .Append(M(t.TaxAmount)).Append("</td></tr>");
        sb.Append("<tr class=\"grand\"><td>Total</td><td class=\"n\">").Append(M(invoice.Totals.Total)).Append("</td></tr>");
        if (invoice.AmountPaid > 0) sb.Append("<tr><td>Paid</td><td class=\"n\">−").Append(M(invoice.AmountPaid)).Append("</td></tr>");
        if (invoice.AmountCredited > 0) sb.Append("<tr><td>Credited</td><td class=\"n\">−").Append(M(invoice.AmountCredited)).Append("</td></tr>");
        sb.Append("<tr class=\"grand\"><td>Balance due</td><td class=\"n\">").Append(M(invoice.Balance)).Append("</td></tr></tbody></table>");
        if (!string.IsNullOrWhiteSpace(invoice.Notes)) sb.Append("<div class=\"box\"><strong>Notes</strong><div>").Append(Multi(invoice.Notes)).Append("</div></div>");
        if (!string.IsNullOrWhiteSpace(p.BankDetails) || !string.IsNullOrWhiteSpace(p.PaymentInstructions) || !string.IsNullOrWhiteSpace(p.PaymentLinkText))
        {
            sb.Append("<div class=\"box\"><strong>How to pay</strong>");
            if (!string.IsNullOrWhiteSpace(p.PaymentInstructions)) sb.Append("<div>").Append(Multi(p.PaymentInstructions)).Append("</div>");
            if (!string.IsNullOrWhiteSpace(p.BankDetails)) sb.Append("<div>").Append(Multi(p.BankDetails)).Append("</div>");
            if (!string.IsNullOrWhiteSpace(p.PaymentLinkText)) sb.Append("<div>").Append(Multi(p.PaymentLinkText)).Append("</div>");
            sb.Append("<div class=\"muted\">Please quote invoice ").Append(E(invoice.Number)).Append(" as the payment reference.</div></div>");
        }
        if (!string.IsNullOrWhiteSpace(p.InvoiceFooter)) sb.Append("<p class=\"muted\">").Append(Multi(p.InvoiceFooter)).Append("</p>");
        sb.Append("</body></html>");
        return sb.ToString();
    }
}
