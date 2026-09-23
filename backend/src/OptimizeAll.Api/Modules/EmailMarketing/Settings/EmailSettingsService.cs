using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Notifications;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Modules.EmailMarketing.Audiences;
using OptimizeAll.Api.Modules.EmailMarketing.Campaigns;
using OptimizeAll.Api.Modules.EmailMarketing.Delivery;
using OptimizeAll.Api.Modules.EmailMarketing.Shared;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.EmailMarketing;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.EmailMarketing.Settings;

public sealed record ProviderReadiness(string Channel, bool Ready, string Detail);

public sealed record WebhookUrls(string SendGrid, string Mailgun, string TwilioInbound, string TwilioStatus, string Conversions, string Events);

public sealed record WorkspaceSettingsDto(
    Guid? ClientAccountId, string OrganizationName, string PhysicalAddress, bool RequireClientApproval, string EmailProvider, int DefaultThrottlePerMinute,
    int QuietHoursStart, int QuietHoursEnd, string DefaultTimeZone, decimal SmsCostPerSegment, decimal WhatsAppCostPerMessage, string CostCurrency,
    IReadOnlyList<ProviderReadiness> Providers, WebhookUrls Webhooks, IReadOnlyList<string> AvailableProviders, Guid ConcurrencyStamp);

public sealed class WorkspaceSettingsRequest
{
    public Guid? ClientAccountId { get; set; }
    [Required, MaxLength(200)] public string OrganizationName { get; set; } = string.Empty;
    [MaxLength(500)] public string PhysicalAddress { get; set; } = string.Empty;
    public bool RequireClientApproval { get; set; }
    [Range(1, 100_000)] public int DefaultThrottlePerMinute { get; set; } = 600;
    [Required, MaxLength(64)] public string DefaultTimeZone { get; set; } = "UTC";
    /// <summary>SMS settings (need sms.manage to change).</summary>
    [Range(0, 23)] public int QuietHoursStart { get; set; } = 21;
    [Range(0, 23)] public int QuietHoursEnd { get; set; } = 8;
    [Range(0, 10)] public decimal SmsCostPerSegment { get; set; }
    [Range(0, 10)] public decimal WhatsAppCostPerMessage { get; set; }
    [MaxLength(3)] public string? CostCurrency { get; set; }
    public Guid? ConcurrencyStamp { get; set; }
}

public sealed class ProviderChoiceRequest
{
    public Guid? ClientAccountId { get; set; }
    [Required, MaxLength(20)] public string EmailProvider { get; set; } = "smtp";
    public bool Confirm { get; set; }
}

public sealed record SenderProfileDto(Guid Id, Guid? ClientAccountId, string FromName, string FromEmail, string? ReplyTo, bool IsDefault, bool Verified,
    DateTime? VerifiedAt, DateTime? VerificationSentAt, Guid ConcurrencyStamp);

public sealed class SenderProfileRequest
{
    public Guid? ClientAccountId { get; set; }
    [Required, MaxLength(100)] public string FromName { get; set; } = string.Empty;
    [Required, MaxLength(254)] public string FromEmail { get; set; } = string.Empty;
    [MaxLength(254)] public string? ReplyTo { get; set; }
    public bool IsDefault { get; set; }
    public Guid? ConcurrencyStamp { get; set; }
}

public sealed class VerifySenderRequest
{
    [Required, RegularExpression("^[0-9]{6}$")] public string Code { get; set; } = string.Empty;
}

/// <summary>Workspace compliance/sending settings and verified sender identities.</summary>
public sealed class EmailSettingsService(
    AppDbContext db,
    EmailAccess access,
    IAuditLogger audit,
    ICurrentUser currentUser,
    EmailSettingsStore store,
    ProviderStatus providerStatus,
    EmailProviderResolver providers,
    EmailMarketingUrls urls,
    IOptions<EmailOptions> emailOptions,
    TimeProvider clock)
{
    public const int MaxVerificationAttempts = 5;

    public async Task<WorkspaceSettingsDto> GetAsync(Guid? clientId, CancellationToken ct)
    {
        await access.EnsureStaffWorkspaceAsync(clientId, ct);
        return await ToDtoAsync(await store.GetAsync(clientId, ct), ct);
    }

    public async Task<WorkspaceSettingsDto> UpdateAsync(WorkspaceSettingsRequest r, CancellationToken ct)
    {
        await access.EnsureStaffWorkspaceAsync(r.ClientAccountId, ct);
        await store.GetAsync(r.ClientAccountId, ct);
        var key = Workspace.Key(r.ClientAccountId);
        var s = await db.Set<EmailWorkspaceSettings>().FirstAsync(x => x.ScopeKey == key, ct);
        AudienceService.ExpectStamp(s.ConcurrencyStamp, r.ConcurrencyStamp);
        if (!SendTiming.IsValidZone(r.DefaultTimeZone)) throw new DomainException("email.settings_invalid", "Choose a valid IANA time zone.");
        var smsChanged = s.QuietHoursStart != r.QuietHoursStart || s.QuietHoursEnd != r.QuietHoursEnd || s.SmsCostPerSegment != r.SmsCostPerSegment ||
                         s.WhatsAppCostPerMessage != r.WhatsAppCostPerMessage || (r.CostCurrency is not null && r.CostCurrency.ToUpperInvariant() != s.CostCurrency);
        if (smsChanged) currentUser.Require(Permissions.SmsManage);
        if (r.CostCurrency is not null && !Money.IsSupported(r.CostCurrency)) throw new DomainException("email.settings_invalid", "Unsupported currency.");
        var before = Snapshot(s);
        s.OrganizationName = r.OrganizationName.Trim();
        s.PhysicalAddress = r.PhysicalAddress.Trim();
        s.RequireClientApproval = r.ClientAccountId is not null && r.RequireClientApproval;
        s.DefaultThrottlePerMinute = r.DefaultThrottlePerMinute;
        s.DefaultTimeZone = r.DefaultTimeZone;
        s.QuietHoursStart = r.QuietHoursStart;
        s.QuietHoursEnd = r.QuietHoursEnd;
        s.SmsCostPerSegment = r.SmsCostPerSegment;
        s.WhatsAppCostPerMessage = r.WhatsAppCostPerMessage;
        if (r.CostCurrency is not null) s.CostCurrency = Money.Normalize(r.CostCurrency);
        audit.Record("email.settings.updated", nameof(EmailWorkspaceSettings), s.Id, before, Snapshot(s));
        await db.SaveChangesAsync(ct);
        return await ToDtoAsync(s, ct);
    }

    /// <summary>Selects the email service provider (sensitive: integrations.manage + confirm).</summary>
    public async Task<WorkspaceSettingsDto> ChooseProviderAsync(ProviderChoiceRequest r, CancellationToken ct)
    {
        await access.EnsureStaffWorkspaceAsync(r.ClientAccountId, ct);
        if (!r.Confirm) throw new DomainException("email.confirm_required", "Confirm the provider change (confirm: true).");
        if (!EmailProviderResolver.Keys.Contains(r.EmailProvider)) throw new DomainException("email.provider_unknown", $"Choose one of {string.Join(", ", EmailProviderResolver.Keys)}.");
        await store.GetAsync(r.ClientAccountId, ct);
        var key = Workspace.Key(r.ClientAccountId);
        var s = await db.Set<EmailWorkspaceSettings>().FirstAsync(x => x.ScopeKey == key, ct);
        var before = s.EmailProvider;
        s.EmailProvider = r.EmailProvider;
        audit.Record("email.settings.provider_changed", nameof(EmailWorkspaceSettings), s.Id, new { EmailProvider = before }, new { s.EmailProvider });
        await db.SaveChangesAsync(ct);
        return await ToDtoAsync(s, ct);
    }

    private async Task<WorkspaceSettingsDto> ToDtoAsync(EmailWorkspaceSettings s, CancellationToken ct)
    {
        var email = await providerStatus.EmailAsync(s, ct);
        var sms = await providerStatus.SmsAsync(s.ClientAccountId, MessageChannel.Sms, ct);
        var whatsapp = await providerStatus.SmsAsync(s.ClientAccountId, MessageChannel.WhatsApp, ct);
        var ws = s.ScopeKey;
        var b = urls.PublicBaseUrl;
        return new WorkspaceSettingsDto(s.ClientAccountId, s.OrganizationName, s.PhysicalAddress, s.RequireClientApproval, s.EmailProvider,
            s.DefaultThrottlePerMinute, s.QuietHoursStart, s.QuietHoursEnd, s.DefaultTimeZone, s.SmsCostPerSegment, s.WhatsAppCostPerMessage, s.CostCurrency,
            new[] { new ProviderReadiness("Email", email.Ready, email.Detail), new ProviderReadiness("Sms", sms.Ready, sms.Detail), new ProviderReadiness("WhatsApp", whatsapp.Ready, whatsapp.Detail) },
            new WebhookUrls($"{b}/api/v1/public/email/webhooks/sendgrid/{ws}", $"{b}/api/v1/public/email/webhooks/mailgun/{ws}",
                $"{b}/api/v1/public/sms/webhooks/twilio/{ws}/inbound", $"{b}/api/v1/public/sms/webhooks/twilio/{ws}/status",
                $"{b}/api/v1/public/email/conversions", $"{b}/api/v1/public/email/events"),
            EmailProviderResolver.Keys, s.ConcurrencyStamp);
    }

    private static object Snapshot(EmailWorkspaceSettings s) => new
    {
        s.OrganizationName, s.PhysicalAddress, s.RequireClientApproval, s.DefaultThrottlePerMinute, s.DefaultTimeZone, s.QuietHoursStart, s.QuietHoursEnd,
        s.SmsCostPerSegment, s.WhatsAppCostPerMessage, s.CostCurrency,
    };

    // ---------- Sender profiles ----------

    public async Task<IReadOnlyList<SenderProfileDto>> SendersAsync(Guid? clientId, CancellationToken ct)
    {
        await access.EnsureStaffWorkspaceAsync(clientId, ct);
        var key = Workspace.Key(clientId);
        return (await db.Set<SenderProfile>().AsNoTracking().Where(p => p.ScopeKey == key).OrderByDescending(p => p.IsDefault).ThenBy(p => p.FromEmail).ToListAsync(ct))
            .Select(ToDto).ToList();
    }

    public async Task<SenderProfileDto> CreateSenderAsync(SenderProfileRequest r, CancellationToken ct)
    {
        await access.EnsureStaffWorkspaceAsync(r.ClientAccountId, ct);
        var p = new SenderProfile { ClientAccountId = r.ClientAccountId, ScopeKey = Workspace.Key(r.ClientAccountId) };
        Apply(p, r);
        if (r.IsDefault) await ClearDefaultAsync(p.ScopeKey, ct);
        db.Set<SenderProfile>().Add(p);
        audit.Record("email.sender.created", nameof(SenderProfile), p.Id, after: new { p.FromName, p.FromEmail, p.ReplyTo });
        await db.SaveChangesAsync(ct);
        return ToDto(p);
    }

    public async Task<SenderProfileDto> UpdateSenderAsync(Guid id, SenderProfileRequest r, CancellationToken ct)
    {
        var p = await LoadSenderAsync(id, ct);
        AudienceService.ExpectStamp(p.ConcurrencyStamp, r.ConcurrencyStamp);
        var before = new { p.FromName, p.FromEmail, p.ReplyTo };
        var previousEmail = p.FromEmail;
        Apply(p, r);
        if (!string.Equals(previousEmail, p.FromEmail, StringComparison.OrdinalIgnoreCase))
        {
            // A new address must be verified again.
            p.VerifiedAt = null;
            p.VerifiedByUserId = null;
            p.VerificationCodeHash = null;
        }
        if (r.IsDefault) await ClearDefaultAsync(p.ScopeKey, ct);
        audit.Record("email.sender.updated", nameof(SenderProfile), p.Id, before, new { p.FromName, p.FromEmail, p.ReplyTo });
        await db.SaveChangesAsync(ct);
        return ToDto(p);
    }

    public async Task DeleteSenderAsync(Guid id, CancellationToken ct)
    {
        var p = await LoadSenderAsync(id, ct);
        if (await db.Set<EmailCampaign>().AnyAsync(c => c.SenderProfileId == id && (c.Status == CampaignStatus.Scheduled || c.Status == CampaignStatus.Sending || c.Status == CampaignStatus.Paused), ct))
            throw DomainException.Conflict("email.sender_in_use", "A scheduled or sending campaign uses this sender.");
        audit.Record("email.sender.deleted", nameof(SenderProfile), p.Id, before: new { p.FromName, p.FromEmail });
        db.Set<SenderProfile>().Remove(p);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Emails a 6-digit code to the sender address; entering it proves the agency controls the mailbox.</summary>
    public async Task<SenderProfileDto> SendVerificationAsync(Guid id, CancellationToken ct)
    {
        var p = await LoadSenderAsync(id, ct);
        var now = clock.GetUtcNow().UtcDateTime;
        if (p.VerificationSentAt is { } sent && sent > now.AddMinutes(-1)) throw new DomainException("email.verification_throttled", "Wait a minute before requesting another code.", DomainErrorKind.TooManyRequests);
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        var provider = await providers.ForWorkspaceAsync(p.ClientAccountId, ct);
        var text = $"Your Optimize All sender verification code is {code}.\n\nEnter it in Email settings to confirm that {p.FromEmail} may send campaigns. The code expires in 24 hours. If you did not expect this email, ignore it.";
        var result = await provider.SendAsync(new OutboundEmail(p.ClientAccountId, p.FromEmail, p.FromName, emailOptions.Value.FromAddress, emailOptions.Value.FromName, null,
            "Verify your sender address", $"<p>Your Optimize All sender verification code is <strong>{code}</strong>.</p><p>Enter it in Email settings to confirm that {System.Net.WebUtility.HtmlEncode(p.FromEmail)} may send campaigns. The code expires in 24 hours.</p>",
            text, new Dictionary<string, string>(), new Dictionary<string, string> { ["oa_kind"] = "sender-verification" }), ct);
        if (result.Outcome != ProviderOutcome.Accepted)
            throw new DomainException("email.verification_not_sent", "The verification email could not be sent: " + result.Error, DomainErrorKind.Conflict);
        p.VerificationCodeHash = Normalization.Sha256Hex($"{p.Id:N}:{code}");
        p.VerificationSentAt = now;
        p.VerificationAttempts = 0;
        audit.Record("email.sender.verification_sent", nameof(SenderProfile), p.Id, after: new { p.FromEmail });
        await db.SaveChangesAsync(ct);
        return ToDto(p);
    }

    public async Task<SenderProfileDto> VerifyAsync(Guid id, VerifySenderRequest r, CancellationToken ct)
    {
        var p = await LoadSenderAsync(id, ct);
        var now = clock.GetUtcNow().UtcDateTime;
        if (p.VerificationCodeHash is null || p.VerificationSentAt is null || p.VerificationSentAt < now.AddHours(-24))
            throw new DomainException("email.verification_expired", "Request a new verification code.");
        if (p.VerificationAttempts >= MaxVerificationAttempts) throw new DomainException("email.verification_locked", "Too many attempts. Request a new code.");
        p.VerificationAttempts++;
        var expected = Normalization.Sha256Hex($"{p.Id:N}:{r.Code}");
        if (!CryptographicOperations.FixedTimeEquals(System.Text.Encoding.ASCII.GetBytes(expected), System.Text.Encoding.ASCII.GetBytes(p.VerificationCodeHash)))
        {
            await db.SaveChangesAsync(ct);
            throw new DomainException("email.verification_wrong_code", "That code is not correct.");
        }
        p.VerifiedAt = now;
        p.VerifiedByUserId = currentUser.IdOrNull;
        p.VerificationCodeHash = null;
        audit.Record("email.sender.verified", nameof(SenderProfile), p.Id, after: new { p.FromEmail });
        await db.SaveChangesAsync(ct);
        return ToDto(p);
    }

    private Task<SenderProfile> LoadSenderAsync(Guid id, CancellationToken ct) =>
        access.LoadAsync(db.Set<SenderProfile>().Where(p => p.Id == id), p => p.ClientAccountId, "Sender", ct);

    private static void Apply(SenderProfile p, SenderProfileRequest r)
    {
        var from = ContactRules.NormalizeEmail(r.FromEmail) ?? throw new DomainException("email.sender_invalid", "Enter a valid from address.");
        string? replyTo = null;
        if (!string.IsNullOrWhiteSpace(r.ReplyTo)) replyTo = ContactRules.NormalizeEmail(r.ReplyTo) ?? throw new DomainException("email.sender_invalid", "Enter a valid reply-to address.");
        if (r.FromName.Any(char.IsControl) || r.FromName.Contains('"') || r.FromName.Contains('<')) throw new DomainException("email.sender_invalid", "The from name contains invalid characters.");
        p.FromName = r.FromName.Trim();
        p.FromEmail = from;
        p.ReplyTo = replyTo;
        p.IsDefault = r.IsDefault;
    }

    private async Task ClearDefaultAsync(string scopeKey, CancellationToken ct) =>
        await db.Set<SenderProfile>().Where(p => p.ScopeKey == scopeKey && p.IsDefault).ExecuteUpdateAsync(u => u.SetProperty(p => p.IsDefault, false), ct);

    private static SenderProfileDto ToDto(SenderProfile p) => new(p.Id, p.ClientAccountId, p.FromName, p.FromEmail, p.ReplyTo, p.IsDefault, p.VerifiedAt is not null,
        p.VerifiedAt, p.VerificationSentAt, p.ConcurrencyStamp);
}
