using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Api.Common.Settings;
using OptimizeAll.Domain.Billing;
using OptimizeAll.Domain.Common;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Billing;

/// <summary>Agency billing configuration (setting <see cref="BillingSettingsService.Key"/>). Edited under <c>billing.settings</c>.</summary>
public sealed class BillingSettings
{
    public string InvoicePrefix { get; set; } = "OA";
    public string CreditNotePrefix { get; set; } = "CN";
    public string ContractPrefix { get; set; } = "CT";
    public string ProposalPrefix { get; set; } = "PR";
    public int NumberPadding { get; set; } = 4;

    /// <summary>Default payment terms (days from issue to due date).</summary>
    public int PaymentTermsDays { get; set; } = 14;
    public string DefaultCurrency { get; set; } = "USD";

    /// <summary>When a proposal is accepted, create the first invoice (one-time lines + first period of recurring lines).</summary>
    public bool InvoiceOnAcceptance { get; set; } = true;

    /// <summary>Issue invoices created on acceptance / by the recurring job immediately (otherwise they stay drafts).</summary>
    public bool AutoIssueInvoices { get; set; }

    public bool RemindersEnabled { get; set; } = true;

    /// <summary>Reminder schedule in days relative to the due date (negative = before).</summary>
    public List<int> ReminderOffsetsDays { get; set; } = new() { -3, 0, 7, 14 };

    public string CompanyName { get; set; } = "Optimize All";
    public string? CompanyAddress { get; set; }
    public string? CompanyTaxId { get; set; }
    public string? CompanyEmail { get; set; }

    /// <summary>Bank transfer details shown on invoices and in the client portal.</summary>
    public string? BankDetails { get; set; }

    /// <summary>Text for an external payment link (e.g. "Pay by card at https://pay.example.com/optimizeall").</summary>
    public string? PaymentLinkText { get; set; }
    public string? PaymentInstructions { get; set; }
    public string? InvoiceFooter { get; set; }
    public Guid? DefaultTaxRateId { get; set; }

    /// <summary>Payment terms (days) offered in the invoice and contract editors.</summary>
    public List<int> PaymentTermsOptions { get; set; } = new() { 0, 7, 14, 30, 45, 60 };
}

public sealed class BillingSettingsService(ISettingsService settings)
{
    public const string Key = "billing.settings";

    public Task<BillingSettings> GetAsync(CancellationToken ct = default) => settings.GetAsync(Key, new BillingSettings(), ct);

    /// <summary>Validates and stages the new settings (the caller audits and saves).</summary>
    public async Task SaveAsync(BillingSettings value, Guid? actor, CancellationToken ct)
    {
        Validate(value);
        await settings.SetAsync(Key, value, actor, "Agency billing settings (numbering, terms, reminders, payment instructions).", ct);
    }

    public static void Validate(BillingSettings s)
    {
        var errors = new Dictionary<string, string[]>();
        void Add(string field, string message) => errors[field] = new[] { message };
        foreach (var (field, prefix) in new[] { ("invoicePrefix", s.InvoicePrefix), ("creditNotePrefix", s.CreditNotePrefix),
                     ("contractPrefix", s.ContractPrefix), ("proposalPrefix", s.ProposalPrefix) })
            if (string.IsNullOrWhiteSpace(prefix) || prefix.Length > 10 || !prefix.All(c => char.IsAsciiLetterOrDigit(c)))
                Add(field, "Use 1–10 letters or digits.");
        if (s.NumberPadding is < 3 or > 8) Add("numberPadding", "Use between 3 and 8 digits.");
        if (s.PaymentTermsDays is < 0 or > 365) Add("paymentTermsDays", "Payment terms must be between 0 and 365 days.");
        if (!Money.IsSupported(s.DefaultCurrency)) Add("defaultCurrency", "Choose a supported currency.");
        if (s.ReminderOffsetsDays.Count > 8 || s.ReminderOffsetsDays.Any(d => d is < -30 or > 180) ||
            s.ReminderOffsetsDays.Distinct().Count() != s.ReminderOffsetsDays.Count)
            Add("reminderOffsetsDays", "Up to 8 distinct offsets between -30 and 180 days.");
        s.PaymentTermsOptions ??= new List<int>();
        if (s.PaymentTermsOptions.Count > 12 || s.PaymentTermsOptions.Any(d => d is < 0 or > 365))
            Add("paymentTermsOptions", "Up to 12 payment terms between 0 and 365 days.");
        if (string.IsNullOrWhiteSpace(s.CompanyName) || s.CompanyName.Length > 200) Add("companyName", "Enter the company name (up to 200 characters).");
        foreach (var (field, text, max) in new[] { ("companyAddress", s.CompanyAddress, 1000), ("companyTaxId", s.CompanyTaxId, 64),
                     ("companyEmail", s.CompanyEmail, 254), ("bankDetails", s.BankDetails, 2000), ("paymentLinkText", s.PaymentLinkText, 500),
                     ("paymentInstructions", s.PaymentInstructions, 2000), ("invoiceFooter", s.InvoiceFooter, 1000) })
            if (text is { Length: var len } && len > max) Add(field, $"At most {max} characters.");
        if (errors.Count > 0)
            throw new DomainException("billing.invalid_settings", "Some billing settings need attention.", errors: errors);
        s.DefaultCurrency = Money.Normalize(s.DefaultCurrency);
        s.ReminderOffsetsDays = s.ReminderOffsetsDays.OrderBy(d => d).ToList();
        s.PaymentTermsOptions = s.PaymentTermsOptions.Append(s.PaymentTermsDays).Distinct().OrderBy(d => d).ToList();
        s.InvoicePrefix = s.InvoicePrefix.ToUpperInvariant();
        s.CreditNotePrefix = s.CreditNotePrefix.ToUpperInvariant();
        s.ContractPrefix = s.ContractPrefix.ToUpperInvariant();
        s.ProposalPrefix = s.ProposalPrefix.ToUpperInvariant();
    }
}

/// <summary>
/// Gapless document numbers (invoices, credit notes, contracts, proposals). <see cref="NextAsync"/> must run inside the
/// caller's write transaction: it row-locks the series counter (<see cref="IDatabaseDialect.LockRowAsync"/>) and
/// increments it in that transaction, so concurrent issuers are serialized and a rollback returns the number.
/// </summary>
public sealed class DocumentNumberService(IDatabaseDialect dialect, TimeProvider clock)
{
    public const string InvoiceSeries = "invoice";
    public const string CreditNoteSeries = "credit";
    public const string ContractSeries = "contract";
    public const string ProposalSeries = "proposal";

    public static string SequenceKey(string series, int year) => $"{series}:{year}";

    public int CurrentYear => clock.GetUtcNow().UtcDateTime.Year;

    /// <summary>Creates the counter row if missing. Call before opening the transaction that allocates numbers.</summary>
    public async Task EnsureAsync(AppDbContext db, string series, CancellationToken ct, int? year = null)
    {
        var key = SequenceKey(series, year ?? CurrentYear);
        if (await db.Set<NumberSequence>().AsNoTracking().AnyAsync(s => s.Key == key, ct)) return;
        var row = new NumberSequence { Key = key, LastValue = 0, UpdatedAt = clock.GetUtcNow().UtcDateTime };
        db.Set<NumberSequence>().Add(row);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (dialect.IsUniqueViolation(ex))
        {
            // Another request created it first.
        }
        finally
        {
            db.Entry(row).State = EntityState.Detached;
        }
    }

    /// <summary>Allocates the next number of a series (inside the caller's write transaction).</summary>
    public async Task<string> NextAsync(AppDbContext db, string series, string prefix, int padding, CancellationToken ct, int? year = null)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Document numbers must be allocated inside a write transaction.");
        var y = year ?? CurrentYear;
        var key = SequenceKey(series, y);
        var id = await db.Set<NumberSequence>().AsNoTracking().Where(s => s.Key == key).Select(s => (Guid?)s.Id).FirstOrDefaultAsync(ct)
                 ?? throw new InvalidOperationException($"Number sequence '{key}' does not exist; call EnsureAsync first.");
        await dialect.LockRowAsync(db, "billing_number_sequences", id, ct);
        // Increment in the database (reads the latest committed value under the row lock) and read our own write back:
        // a plain read could return a snapshot taken before the lock was granted.
        var updated = await db.Set<NumberSequence>().Where(s => s.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastValue, x => x.LastValue + 1).SetProperty(x => x.UpdatedAt, clock.GetUtcNow().UtcDateTime), ct);
        if (updated != 1) throw new InvalidOperationException($"Number sequence '{key}' disappeared.");
        var next = await db.Set<NumberSequence>().AsNoTracking().Where(s => s.Id == id).Select(s => s.LastValue).FirstAsync(ct);
        return DocumentNumbers.Format(prefix, y, next, padding);
    }
}

/// <summary>
/// Unguessable public links (<c>/p/{token}</c>, <c>/i/{token}</c>): 256-bit random tokens. Only the SHA-256 is used for
/// lookup; the raw token is kept encrypted with Data Protection so staff can copy/re-send the link.
/// </summary>
public sealed class PublicLinkTokens(IDataProtectionProvider protection)
{
    private readonly IDataProtector _protector = protection.CreateProtector("OptimizeAll.Billing.PublicLinks.v1");

    public (string Raw, string Hash, string Protected) Create()
    {
        var raw = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        return (raw, Hash(raw), _protector.Protect(raw));
    }

    public static string Hash(string raw) => Normalization.Sha256Hex(raw);

    public string? Reveal(string? protectedToken)
    {
        if (string.IsNullOrEmpty(protectedToken)) return null;
        try
        {
            return _protector.Unprotect(protectedToken);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    /// <summary>Shape check before any database lookup (43 base64url characters).</summary>
    public static bool IsWellFormed(string? token) =>
        token is { Length: 43 } && token.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}

/// <summary>Web-app paths used in billing/CRM emails and notifications (mirror of the frontend routes).</summary>
public static class BillingLinks
{
    public static string PublicInvoice(string token) => $"/i/{Uri.EscapeDataString(token)}";
    public static string PublicProposal(string token) => $"/p/{Uri.EscapeDataString(token)}";
    public static string AgencyInvoice(Guid id) => $"/agency/billing/invoices/{id}";
    public static string AgencyContract(Guid id) => $"/agency/contracts/{id}";
    public static string AgencyProposal(Guid id) => $"/agency/proposals/{id}";
    public static string AgencyDeal(Guid id) => $"/agency/crm/deals/{id}";
    public const string AgencyTasks = "/agency/crm/tasks";
    public static string ClientInvoice(Guid id) => $"/client/billing/invoices/{id}";
    public static string ClientProposal(Guid id) => $"/client/billing/proposals/{id}";
}

/// <summary>Notification kinds raised by the CRM and billing modules.</summary>
public static class BillingNotificationTypes
{
    public const string LeadAssigned = "crm.lead_assigned";
    public const string TaskOverdue = "crm.task_overdue";
    public const string TaskReminder = "crm.task_reminder";
    public const string ProposalViewed = "crm.proposal_viewed";
    public const string ProposalAccepted = "crm.proposal_accepted";
    public const string ProposalDeclined = "crm.proposal_declined";
    public const string InvoiceIssued = "billing.invoice_issued";
    public const string InvoiceReminder = "billing.invoice_reminder";
    public const string InvoicePaid = "billing.invoice_paid";
    public const string ProposalReceived = "billing.proposal_received";
}

internal static class BillingDates
{
    public static DateOnly Today(TimeProvider clock) => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
}
