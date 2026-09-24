using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Common.Notifications;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.Billing;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Notifications;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Billing;

/// <summary>
/// Invoice lifecycle: drafts (editable), issuing (gapless number, immutable amounts), sending (public link + email),
/// voiding and writing off (sensitive, four-eyes). Payments and credit notes live in their own services.
/// </summary>
public sealed class InvoiceService(
    AppDbContext db,
    IDatabaseDialect dialect,
    IClientScope scope,
    ICurrentUser currentUser,
    IAuditLogger audit,
    INotificationService notifications,
    IEmailSender email,
    IOptions<EmailOptions> emailOptions,
    LineBuilder lineBuilder,
    DocumentNumberService numbers,
    BillingSettingsService settingsService,
    PublicLinkTokens tokens,
    TimeProvider clock,
    ILogger<InvoiceService> logger)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private DateOnly Today => BillingDates.Today(clock);

    // ------------------------------------------------------------------ Queries

    public async Task<PagedResult<InvoiceSummaryDto>> ListAsync(InvoiceQuery query, CancellationToken ct)
    {
        var invoices = await scope.ApplyAsync(db.Set<Invoice>().AsNoTracking(), i => i.ClientAccountId, ct);
        if (query.Status is { } status) invoices = invoices.Where(i => i.Status == status);
        if (query.OpenOnly == true) invoices = invoices.Where(i => Invoice.OpenStatuses.Contains(i.Status));
        if (query.ClientAccountId is { } clientId) invoices = invoices.Where(i => i.ClientAccountId == clientId);
        if (query.ContractId is { } contractId) invoices = invoices.Where(i => i.ContractId == contractId);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = PagingExtensions.LikePattern(query.Search);
            var clientIds = db.Set<ClientAccount>().Where(c => EF.Functions.Like(c.Name, pattern, "\\")).Select(c => c.Id);
            invoices = invoices.Where(i => EF.Functions.Like(i.Number!, pattern, "\\") || EF.Functions.Like(i.Reference!, pattern, "\\") ||
                                           clientIds.Contains(i.ClientAccountId));
        }
        invoices = (query.Sort ?? "created") switch
        {
            "number" => query.Desc ? invoices.OrderByDescending(i => i.Number) : invoices.OrderBy(i => i.Number),
            "dueDate" => query.Desc ? invoices.OrderByDescending(i => i.DueDate) : invoices.OrderBy(i => i.DueDate),
            "issueDate" => query.Desc ? invoices.OrderByDescending(i => i.IssueDate) : invoices.OrderBy(i => i.IssueDate),
            _ => query.Desc ? invoices.OrderByDescending(i => i.CreatedAt) : invoices.OrderBy(i => i.CreatedAt),
        };
        var total = await invoices.CountAsync(ct);
        var rows = await invoices.Skip(query.Skip).Take(query.PageSize).ToListAsync(ct);
        return new PagedResult<InvoiceSummaryDto>(await SummariesAsync(rows, ct), total, query.Page, query.PageSize);
    }

    public async Task<IReadOnlyList<InvoiceSummaryDto>> SummariesAsync(IReadOnlyList<Invoice> rows, CancellationToken ct)
    {
        var clientIds = rows.Select(r => r.ClientAccountId).Distinct().ToList();
        var names = await db.Set<ClientAccount>().AsNoTracking().Where(c => clientIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var today = Today;
        return rows.Select(i => new InvoiceSummaryDto(i.Id, i.Number, i.ClientAccountId, names.GetValueOrDefault(i.ClientAccountId, "—"),
            i.Status, i.Currency, i.IssueDate, i.DueDate, i.Total, i.AmountPaid, i.Balance, DaysOverdue(i, today), i.CreatedAt,
            i.ContractId)).ToList();
    }

    public static int DaysOverdue(Invoice i, DateOnly today) =>
        Invoice.IsOpen(i.Status) && i.DueDate is { } due && due < today ? today.DayNumber - due.DayNumber : 0;

    public async Task<InvoiceDto> GetAsync(Guid id, CancellationToken ct)
    {
        var invoice = await LoadScopedAsync(id, ct);
        return await ToDtoAsync(invoice, includeStaffDetails: true, ct);
    }

    /// <summary>Loads an invoice (with lines) the caller may see, or 404.</summary>
    public async Task<Invoice> LoadScopedAsync(Guid id, CancellationToken ct)
    {
        var query = await scope.ApplyAsync(db.Set<Invoice>().AsNoTracking().Include(i => i.Lines), i => i.ClientAccountId, ct);
        return await query.FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw DomainException.NotFound("Invoice");
    }

    public async Task<InvoiceDto> ToDtoAsync(Invoice invoice, bool includeStaffDetails, CancellationToken ct)
    {
        var client = await db.Set<ClientAccount>().AsNoTracking().Where(c => c.Id == invoice.ClientAccountId)
            .Select(c => new { c.Name, c.BillingEmail }).FirstAsync(ct);
        var payments = await db.Set<Payment>().AsNoTracking().Where(p => p.InvoiceId == invoice.Id).OrderBy(p => p.PaidOn)
            .ThenBy(p => p.CreatedAt).ToListAsync(ct);
        var recorders = await UserNamesAsync(payments.Where(p => p.RecordedByUserId.HasValue).Select(p => p.RecordedByUserId!.Value), ct);
        var credits = await (from a in db.Set<CreditNoteApplication>().AsNoTracking()
                             join n in db.Set<CreditNote>().AsNoTracking() on a.CreditNoteId equals n.Id
                             where a.InvoiceId == invoice.Id
                             orderby a.AppliedAt
                             select new CreditApplicationDto(n.Id, n.Number, a.InvoiceId, invoice.Number, a.Amount, a.AppliedAt)).ToListAsync(ct);
        var reminders = includeStaffDetails
            ? await db.Set<InvoiceReminder>().AsNoTracking().Where(r => r.InvoiceId == invoice.Id).OrderBy(r => r.SentAt)
                .Select(r => new ReminderDto(r.Kind, r.SentAt)).ToListAsync(ct)
            : new List<ReminderDto>();
        var lines = invoice.Lines.OrderBy(l => l.Position).ToList();
        var totals = LineBuilder.TotalsOf(lines, invoice.Currency);
        var publicUrl = includeStaffDetails && tokens.Reveal(invoice.PublicTokenProtected) is { } raw
            ? emailOptions.Value.AppBaseUrl.TrimEnd('/') + BillingLinks.PublicInvoice(raw)
            : null;
        return new InvoiceDto(invoice.Id, invoice.Number, invoice.ClientAccountId, client.Name, client.BillingEmail, invoice.Status,
            invoice.Currency, invoice.IssueDate, invoice.DueDate, invoice.PaymentTermsDays,
            LineBuilder.ToDto(totals), invoice.AmountPaid, invoice.AmountCredited, invoice.AmountWrittenOff,
            invoice.Balance, DaysOverdue(invoice, Today), invoice.Notes, invoice.Reference, invoice.ContractId, invoice.ProposalId,
            invoice.PeriodStart, invoice.PeriodEnd, lines.Select(LineBuilder.ToDto).ToList(),
            payments.Select(p => PaymentService.ToDto(p, invoice.Number, client.Name,
                p.RecordedByUserId is { } u ? recorders.GetValueOrDefault(u) : null)).ToList(),
            credits, reminders, publicUrl, invoice.IssuedAt, invoice.SentAt, invoice.PaidAt, invoice.VoidedAt,
            includeStaffDetails ? invoice.VoidReason : null, invoice.WrittenOffAt, includeStaffDetails ? invoice.WriteOffReason : null,
            includeStaffDetails ? invoice.IssuedByUserId : null, invoice.CreatedAt, invoice.ConcurrencyStamp);
    }

    private async Task<Dictionary<Guid, string>> UserNamesAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        if (list.Count == 0) return new Dictionary<Guid, string>();
        return await db.Set<User>().AsNoTracking().Where(u => list.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
    }

    public async Task<PreviewResponse> PreviewAsync(PreviewRequest request, bool allowRecurrence, CancellationToken ct)
    {
        var built = await lineBuilder.BuildAsync(request.Lines, request.Currency, () => new InvoiceLine(), ct, allowRecurrence);
        var lines = built.Lines.Select((l, i) => LineBuilder.ToDto(l) with { Recurrence = allowRecurrence ? request.Lines[i].Recurrence : Recurrence.OneTime })
            .ToList();
        return new PreviewResponse(lines, LineBuilder.ToDto(built.Totals), LineBuilder.ToDto(built.Recurring));
    }

    // ------------------------------------------------------------------ Drafts

    public async Task<InvoiceDto> CreateDraftAsync(InvoiceDraftRequest request, CancellationToken ct)
    {
        var clientId = request.ClientAccountId!.Value;
        await scope.EnsureAccessAsync(clientId, ct: ct);
        var client = await db.Set<ClientAccount>().AsNoTracking().FirstAsync(c => c.Id == clientId, ct);
        var settings = await settingsService.GetAsync(ct);
        var currency = LineBuilder.NormalizeCurrency(request.Currency ?? client.Currency);
        var built = await lineBuilder.BuildAsync(request.Lines, currency, () => new InvoiceLine(), ct);
        var invoice = new Invoice
        {
            ClientAccountId = clientId,
            Currency = currency,
            PaymentTermsDays = request.PaymentTermsDays ?? settings.PaymentTermsDays,
            Notes = Clean(request.Notes),
            Reference = Clean(request.Reference),
            CreatedByUserId = currentUser.IdOrNull,
            Lines = built.Lines,
        };
        LineBuilder.ApplyTotals(invoice, built.Totals);
        db.Set<Invoice>().Add(invoice);
        audit.Record("billing.invoice_created", nameof(Invoice), invoice.Id,
            after: new { invoice.ClientAccountId, invoice.Currency, invoice.Total, Lines = built.Lines.Count });
        await db.SaveChangesAsync(ct);
        return await GetAsync(invoice.Id, ct);
    }

    public async Task<InvoiceDto> UpdateDraftAsync(Guid id, InvoiceDraftRequest request, CancellationToken ct)
    {
        await LoadScopedAsync(id, ct);
        var invoice = await db.Set<Invoice>().Include(i => i.Lines).FirstAsync(i => i.Id == id, ct);
        if (invoice.Status != InvoiceStatus.Draft)
            throw DomainException.Conflict("billing.invoice_not_draft",
                "Issued invoices can't be edited. Record a payment or issue a credit note to correct it.");
        RequireStamp(invoice, request.ConcurrencyStamp);
        if (request.ClientAccountId is { } clientId && clientId != invoice.ClientAccountId)
        {
            await scope.EnsureAccessAsync(clientId, ct: ct);
            invoice.ClientAccountId = clientId;
        }
        var before = new { invoice.Total, invoice.Currency, Lines = invoice.Lines.Count };
        invoice.Currency = LineBuilder.NormalizeCurrency(request.Currency ?? invoice.Currency);
        var built = await lineBuilder.BuildAsync(request.Lines, invoice.Currency, () => new InvoiceLine { InvoiceId = id }, ct);
        db.Set<InvoiceLine>().RemoveRange(invoice.Lines);
        invoice.Lines = built.Lines;
        foreach (var line in built.Lines) db.Set<InvoiceLine>().Add(line);
        invoice.PaymentTermsDays = request.PaymentTermsDays ?? invoice.PaymentTermsDays;
        invoice.Notes = Clean(request.Notes);
        invoice.Reference = Clean(request.Reference);
        LineBuilder.ApplyTotals(invoice, built.Totals);
        audit.Record("billing.invoice_updated", nameof(Invoice), id, before, new { invoice.Total, invoice.Currency, Lines = built.Lines.Count });
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        return await GetAsync(id, ct);
    }

    /// <summary>
    /// Copies an invoice's lines, terms, notes and reference into a new draft for the same client (any status: used to
    /// re-bill after a void, or to start next month's one-off invoice). The source is never changed.
    /// </summary>
    public async Task<InvoiceDto> DuplicateAsync(Guid id, CancellationToken ct)
    {
        var source = await LoadScopedAsync(id, ct);
        var lines = await db.Set<InvoiceLine>().AsNoTracking().Where(l => l.InvoiceId == id).OrderBy(l => l.Position).ToListAsync(ct);
        var copy = await CreateDraftAsync(new InvoiceDraftRequest
        {
            ClientAccountId = source.ClientAccountId,
            Currency = source.Currency,
            PaymentTermsDays = source.PaymentTermsDays,
            Notes = source.Notes,
            Reference = source.Reference,
            Lines = lines.Select(l => new PriceLineRequest
            {
                Description = l.Description, ServiceSlug = l.ServiceSlug, Quantity = l.Quantity, UnitPrice = l.UnitPrice, DiscountType = l.DiscountType,
                DiscountValue = l.DiscountValue, TaxRateId = l.TaxRateId,
            }).ToList(),
        }, ct);
        audit.Record("billing.invoice_duplicated", nameof(Invoice), copy.Id, after: new { From = id, SourceNumber = source.Number });
        await db.SaveChangesAsync(ct);
        return copy;
    }

    public async Task DeleteDraftAsync(Guid id, CancellationToken ct)
    {
        var existing = await LoadScopedAsync(id, ct);
        var deleted = await db.Set<Invoice>().Where(i => i.Id == id && i.Status == InvoiceStatus.Draft).ExecuteDeleteAsync(ct);
        if (deleted == 0) throw DomainException.Conflict("billing.invoice_not_draft", "Only draft invoices can be deleted.");
        audit.Record("billing.invoice_deleted", nameof(Invoice), id, before: new { existing.ClientAccountId, existing.Total, existing.Currency });
        await db.SaveChangesAsync(ct);
    }

    // ------------------------------------------------------------------ Issue & send

    public async Task<InvoiceDto> IssueAsync(Guid id, IssueInvoiceRequest request, CancellationToken ct)
    {
        await LoadScopedAsync(id, ct);
        var settings = await settingsService.GetAsync(ct);
        await numbers.EnsureAsync(db, DocumentNumberService.InvoiceSeries, ct);
        await using (var tx = await dialect.BeginWriteTransactionAsync(db, ct))
        {
            if (!await dialect.LockRowAsync(db, "invoices", id, ct)) throw DomainException.NotFound("Invoice");
            var invoice = await db.Set<Invoice>().Include(i => i.Lines).FirstAsync(i => i.Id == id, ct);
            if (invoice.Status != InvoiceStatus.Draft)
                throw DomainException.Conflict("billing.invoice_not_draft", $"This invoice is already {invoice.Status}.");
            RequireStamp(invoice, request.ConcurrencyStamp);
            await IssueTrackedAsync(invoice, settings, currentUser.IdOrNull, ct);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        db.ChangeTracker.Clear();
        if (request.Send) await SendAsync(id, ct);
        return await GetAsync(id, ct);
    }

    /// <summary>
    /// Draft → Issued on a tracked invoice inside the caller's write transaction: allocates the gapless number, fixes the
    /// dates, creates the public link and audits. Used by the API, proposal acceptance and the recurring job.
    /// </summary>
    public async Task IssueTrackedAsync(Invoice invoice, BillingSettings settings, Guid? actor, CancellationToken ct)
    {
        if (invoice.Lines.Count == 0)
            throw new DomainException("billing.no_lines", "An invoice needs at least one line before it can be issued.");
        if (invoice.Total <= 0)
            throw new DomainException("billing.zero_total", "An invoice total must be greater than zero to be issued.");
        var today = Today;
        invoice.Number = await numbers.NextAsync(db, DocumentNumberService.InvoiceSeries, settings.InvoicePrefix, settings.NumberPadding, ct);
        invoice.Status = InvoiceStatus.Issued;
        invoice.IssueDate = today;
        invoice.DueDate = today.AddDays(invoice.PaymentTermsDays);
        invoice.IssuedAt = Now;
        invoice.IssuedByUserId = actor;
        invoice.RecalculateBalance(invoice.Currency);
        var (_, hash, protectedToken) = tokens.Create();
        invoice.PublicTokenHash = hash;
        invoice.PublicTokenProtected = protectedToken;
        var after = new { invoice.Number, invoice.Total, invoice.Currency, invoice.IssueDate, invoice.DueDate };
        if (actor is null) audit.RecordSystem("billing.invoice_issued", nameof(Invoice), invoice.Id, after);
        else audit.Record("billing.invoice_issued", nameof(Invoice), invoice.Id, new { Status = InvoiceStatus.Draft }, after);
    }

    /// <summary>Emails the invoice (public view link) to the client's billing contacts. Issued, open invoices only.</summary>
    public async Task<InvoiceDto> SendAsync(Guid id, CancellationToken ct)
    {
        var invoice = await LoadScopedAsync(id, ct);
        if (!Invoice.IsOpen(invoice.Status) && invoice.Status != InvoiceStatus.Paid)
            throw DomainException.Conflict("billing.invoice_not_sendable", "Only issued invoices can be sent.");
        var afterCommit = await DeliverAsync(invoice, reminderKind: null, ct);
        await db.Set<Invoice>().Where(i => i.Id == id).ExecuteUpdateAsync(s => s.SetProperty(i => i.SentAt, Now), ct);
        audit.Record("billing.invoice_sent", nameof(Invoice), id, after: new { invoice.Number });
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        await afterCommit(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>
    /// Stages in-app + email notifications (client-portal link) for every client member with the Billing or Owner duty, to be
    /// saved by the caller's SaveChanges. Returns the direct email to the client's billing address (public <c>/i/{token}</c>
    /// link, when that address is not one of the members) for the caller to run after committing.
    /// </summary>
    public async Task<Func<CancellationToken, Task>> DeliverAsync(Invoice invoice, string? reminderKind, CancellationToken ct)
    {
        var raw = tokens.Reveal(invoice.PublicTokenProtected);
        if (raw is null)
        {
            // Missing or unreadable token (e.g. rotated key ring): mint a fresh link.
            var created = tokens.Create();
            raw = created.Raw;
            await db.Set<Invoice>().Where(i => i.Id == invoice.Id).ExecuteUpdateAsync(s => s
                .SetProperty(i => i.PublicTokenHash, created.Hash).SetProperty(i => i.PublicTokenProtected, created.Protected), ct);
        }
        var client = await db.Set<ClientAccount>().AsNoTracking().FirstAsync(c => c.Id == invoice.ClientAccountId, ct);
        var settings = await settingsService.GetAsync(ct);
        var amount = $"{invoice.Balance.ToString("N" + Money.MinorUnitDigits(invoice.Currency), System.Globalization.CultureInfo.InvariantCulture)} {invoice.Currency}";
        var due = invoice.DueDate?.ToString("d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture);
        var (title, body) = reminderKind switch
        {
            null => ($"Invoice {invoice.Number} from {settings.CompanyName}",
                $"Invoice {invoice.Number} for {client.Name} is ready: {amount} due on {due}."),
            "due" => ($"Invoice {invoice.Number} is due today", $"A friendly reminder: {amount} for invoice {invoice.Number} is due today."),
            _ when reminderKind.StartsWith("before", StringComparison.Ordinal) =>
                ($"Invoice {invoice.Number} is due soon", $"A friendly reminder: {amount} for invoice {invoice.Number} is due on {due}."),
            _ => ($"Invoice {invoice.Number} is overdue",
                $"Invoice {invoice.Number} was due on {due} and {amount} is still outstanding. If you've already paid, please ignore this message."),
        };
        var recipients = await (from m in db.Set<ClientMember>().AsNoTracking()
                                join u in db.Set<User>().AsNoTracking() on m.UserId equals u.Id
                                where m.ClientAccountId == invoice.ClientAccountId &&
                                      (m.Role == ClientMemberRole.Billing || m.Role == ClientMemberRole.Owner) && u.Status == UserStatus.Active
                                select new { u.Id, u.NormalizedEmail }).ToListAsync(ct);
        var type = reminderKind is null ? BillingNotificationTypes.InvoiceIssued : BillingNotificationTypes.InvoiceReminder;
        foreach (var r in recipients)
            await notifications.StageAsync(new NotificationRequest(r.Id, type, title, body, BillingLinks.ClientInvoice(invoice.Id),
                new[] { NotificationChannel.Email }), ct);

        var billingEmail = client.BillingEmail?.Trim();
        if (string.IsNullOrWhiteSpace(billingEmail) || recipients.Any(r => r.NormalizedEmail == Normalization.Email(billingEmail)))
            return _ => Task.CompletedTask;
        var absolute = emailOptions.Value.AppBaseUrl.TrimEnd('/') + BillingLinks.PublicInvoice(raw);
        var message = new EmailMessage(billingEmail, client.Name, title,
            $"Hello {client.Name},\n\n{body}\n\nView and download the invoice: {absolute}\n\n— {settings.CompanyName}");
        return async token =>
        {
            var result = await email.SendAsync(message, token);
            if (!result.Success)
                logger.LogWarning("Invoice email for {InvoiceId} to the client billing address failed: {Error}", invoice.Id, result.Error);
        };
    }

    // ------------------------------------------------------------------ Void & write-off

    public Task<InvoiceDto> VoidAsync(Guid id, SensitiveInvoiceActionRequest request, CancellationToken ct) =>
        SensitiveTransitionAsync(id, request, "void", ct);

    public Task<InvoiceDto> WriteOffAsync(Guid id, SensitiveInvoiceActionRequest request, CancellationToken ct) =>
        SensitiveTransitionAsync(id, request, "write-off", ct);

    private async Task<InvoiceDto> SensitiveTransitionAsync(Guid id, SensitiveInvoiceActionRequest request, string action, CancellationToken ct)
    {
        if (!request.Confirm)
            throw new DomainException("request.confirm_required", "Confirm this action by sending \"confirm\": true.");
        var reason = request.Reason.Trim();
        var actor = currentUser.Id;
        await LoadScopedAsync(id, ct);
        await using (var tx = await dialect.BeginWriteTransactionAsync(db, ct))
        {
            if (!await dialect.LockRowAsync(db, "invoices", id, ct)) throw DomainException.NotFound("Invoice");
            var invoice = await db.Set<Invoice>().FirstAsync(i => i.Id == id, ct);
            RequireStamp(invoice, request.ConcurrencyStamp);
            // Four-eyes: the person who issued an invoice can't also make it go away.
            if (invoice.IssuedByUserId == actor)
                throw DomainException.Forbidden("billing.four_eyes",
                    $"You issued this invoice, so another finance user must {(action == "void" ? "void" : "write off")} it.");
            var before = new { invoice.Status, invoice.Balance, invoice.AmountPaid, invoice.AmountCredited };
            if (action == "void")
            {
                if (!Invoice.IsOpen(invoice.Status))
                    throw DomainException.Conflict("billing.invoice_not_open", $"A {invoice.Status} invoice can't be voided.");
                if (invoice.AmountPaid > 0 || invoice.AmountCredited > 0)
                    throw DomainException.Conflict("billing.invoice_has_payments",
                        "This invoice has payments or credits applied. Issue a credit note for the remaining balance instead.");
                invoice.Status = InvoiceStatus.Void;
                invoice.VoidedAt = Now;
                invoice.VoidedByUserId = actor;
                invoice.VoidReason = reason;
                invoice.Balance = 0;
            }
            else
            {
                if (!Invoice.IsOpen(invoice.Status) || invoice.Balance <= 0)
                    throw DomainException.Conflict("billing.invoice_not_open", "Only open invoices with a balance can be written off.");
                invoice.AmountWrittenOff = invoice.Balance;
                invoice.Balance = 0;
                invoice.Status = InvoiceStatus.WrittenOff;
                invoice.WrittenOffAt = Now;
                invoice.WrittenOffByUserId = actor;
                invoice.WriteOffReason = reason;
            }
            audit.Record(action == "void" ? "billing.invoice_voided" : "billing.invoice_written_off", nameof(Invoice), id, before,
                new { invoice.Status, invoice.Number, invoice.AmountWrittenOff, invoice.Currency }, reason);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        db.ChangeTracker.Clear();
        return await GetAsync(id, ct);
    }

    // ------------------------------------------------------------------ Helpers

    /// <summary>Compares the client's stamp with the tracked entity and makes SaveChanges check it too (409 on mismatch).</summary>
    public void RequireStamp<T>(T entity, Guid? stamp) where T : class, IConcurrencyStamped
    {
        if (stamp is null || stamp.Value != entity.ConcurrencyStamp)
            throw DomainException.Conflict("concurrency.conflict", "This record was changed by someone else. Reload and try again.");
        var stampProperty = db.Entry(entity).Property(e => e.ConcurrencyStamp);
        stampProperty.OriginalValue = stamp.Value;
        // Every accepted edit rotates the stamp (and is checked against it), even if no other column changes.
        stampProperty.IsModified = true;
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
