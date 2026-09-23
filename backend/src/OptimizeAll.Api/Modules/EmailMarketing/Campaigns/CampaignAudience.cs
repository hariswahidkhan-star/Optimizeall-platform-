using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Modules.EmailMarketing.Delivery;
using OptimizeAll.Api.Modules.EmailMarketing.Segments;
using OptimizeAll.Domain.EmailMarketing;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.EmailMarketing.Campaigns;

/// <summary>
/// Who may receive a message on a channel. Used for the audience count, for campaign expansion and again right before
/// each send (a contact who unsubscribed, bounced or withdrew consent after expansion is skipped).
/// </summary>
public sealed class CampaignAudience(AppDbContext db, SegmentQueryBuilder segments)
{
    /// <summary>Contacts of the campaign's list/segment who can legally receive it (consent granted, not suppressed).</summary>
    public async Task<IQueryable<Subscriber>> EligibleAsync(EmailCampaign c, CancellationToken ct)
    {
        var q = Reachable(db.Set<Subscriber>().AsNoTracking().Where(s => s.ScopeKey == c.ScopeKey), c.Channel, c.ScopeKey);
        if (c.ListId is { } listId)
            q = q.Where(s => db.Set<ListMembership>().Any(m => m.SubscriberId == s.Id && m.ListId == listId && m.Status == MembershipStatus.Subscribed));
        if (c.SegmentId is { } segmentId)
        {
            var json = await db.Set<Segment>().AsNoTracking().Where(x => x.Id == segmentId).Select(x => x.DefinitionJson).FirstOrDefaultAsync(ct);
            if (json is null) return q.Where(_ => false);
            q = segments.Apply(q, SegmentDefinition.Parse(json));
        }
        return q;
    }

    /// <summary>Channel rules: address present, consent granted, deliverable status, not on the suppression list.</summary>
    public IQueryable<Subscriber> Reachable(IQueryable<Subscriber> q, MessageChannel channel, string scopeKey)
    {
        var suppressions = db.Set<Suppression>();
        return channel switch
        {
            MessageChannel.Sms => q.Where(s => s.Phone != null && s.SmsConsent == ConsentStatus.Granted &&
                                               !suppressions.Any(x => x.ScopeKey == scopeKey && x.Channel == MessageChannel.Sms && x.Value == s.Phone)),
            MessageChannel.WhatsApp => q.Where(s => s.Phone != null && s.WhatsAppConsent == ConsentStatus.Granted &&
                                                    !suppressions.Any(x => x.ScopeKey == scopeKey && x.Channel == MessageChannel.WhatsApp && x.Value == s.Phone)),
            _ => q.Where(s => s.NormalizedEmail != null && s.Status == SubscriberStatus.Subscribed && s.EmailConsent == ConsentStatus.Granted &&
                              !suppressions.Any(x => x.ScopeKey == scopeKey && x.Channel == MessageChannel.Email && x.Value == s.NormalizedEmail)),
        };
    }

    /// <summary>Final per-message gate. Returns a skip reason, or null when the message may be sent.</summary>
    public async Task<string?> BlockReasonAsync(Subscriber? s, MessageChannel channel, string address, CancellationToken ct)
    {
        if (s is null) return "The contact no longer exists.";
        switch (channel)
        {
            case MessageChannel.Email:
                if (s.NormalizedEmail != address) return "The contact's email address changed after the campaign started.";
                if (s.Status != SubscriberStatus.Subscribed) return $"The contact's email status is {s.Status}.";
                if (s.EmailConsent != ConsentStatus.Granted) return "No email marketing consent.";
                break;
            case MessageChannel.Sms:
                if (s.Phone != address) return "The contact's phone number changed.";
                if (s.SmsConsent != ConsentStatus.Granted) return "No SMS consent.";
                break;
            case MessageChannel.WhatsApp:
                if (s.Phone != address) return "The contact's phone number changed.";
                if (s.WhatsAppConsent != ConsentStatus.Granted) return "No WhatsApp consent.";
                break;
        }
        var suppressed = await db.Set<Suppression>().AsNoTracking()
            .Where(x => x.ScopeKey == s.ScopeKey && x.Channel == channel && x.Value == address).Select(x => (SuppressionReason?)x.Reason).FirstOrDefaultAsync(ct);
        return suppressed is { } reason ? $"Suppressed ({reason})." : null;
    }
}

public enum CheckStatus { Pass, Warning, Fail, Info }

public sealed record ChecklistItem(string Id, string Label, CheckStatus Status, string? Detail);

public sealed record Checklist(IReadOnlyList<ChecklistItem> Items, bool CanSend, int AudienceCount, int? SmsSegments, string? SmsEncoding,
    decimal? EstimatedCost, string? CostCurrency, bool RequiresClientApproval);

/// <summary>Provider readiness (credentials present) for the checklist and settings page.</summary>
public sealed class ProviderStatus(ICredentialVault vault, EmailProviderResolver resolver)
{
    public async Task<(bool Ready, string Detail)> EmailAsync(EmailWorkspaceSettings settings, CancellationToken ct)
    {
        switch (settings.EmailProvider)
        {
            case "smtp":
                return (true, "Platform SMTP relay (Email settings of the server).");
            case "sendgrid":
            case "mailgun":
                var creds = await vault.GetAsync(settings.EmailProvider, settings.ClientAccountId, ct);
                return creds is null || creds.Status == Domain.Integrations.IntegrationStatus.Error
                    ? (false, $"{settings.EmailProvider} is selected but not connected (Integrations).")
                    : (true, $"{settings.EmailProvider} connected ({creds.Status}).");
            default:
                return resolver.ByKey(settings.EmailProvider) is UnavailableEmailProvider
                    ? (false, $"The {settings.EmailProvider} adapter is not available in this release; choose SMTP, SendGrid or Mailgun.")
                    : (true, settings.EmailProvider);
        }
    }

    public async Task<(bool Ready, string Detail)> SmsAsync(Guid? clientId, MessageChannel channel, CancellationToken ct)
    {
        var provider = channel == MessageChannel.WhatsApp ? WhatsAppCloudTemplateProvider.ProviderKey : TwilioSmsProvider.ProviderKey;
        var creds = await vault.GetAsync(provider, clientId, ct);
        return creds is null || creds.Status == Domain.Integrations.IntegrationStatus.Error
            ? (false, $"{(channel == MessageChannel.WhatsApp ? "WhatsApp Business" : "Twilio")} is not connected for this workspace (Integrations).")
            : (true, $"{provider} connected ({creds.Status}).");
    }
}
