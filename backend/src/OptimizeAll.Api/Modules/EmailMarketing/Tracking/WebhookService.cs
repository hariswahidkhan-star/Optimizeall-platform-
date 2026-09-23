using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Modules.EmailMarketing.Audiences;
using OptimizeAll.Api.Modules.EmailMarketing.Delivery;
using OptimizeAll.Api.Modules.EmailMarketing.Shared;
using OptimizeAll.Domain.EmailMarketing;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.EmailMarketing.Tracking;

public enum ProviderEventKind { Delivered, HardBounce, SoftBounce, Complaint, Unsubscribe, Dropped, Ignored }

/// <summary>A normalized delivery event from an ESP webhook.</summary>
public sealed record ProviderEvent(string Provider, string? EventId, ProviderEventKind Kind, string? Email, string? Reference, string? ProviderMessageId,
    DateTime OccurredAt, string? Detail);

public enum WebhookVerdict { Ok, NotConfigured, InvalidSignature }

/// <summary>
/// Bounce/complaint/delivery webhooks. Signatures are verified where the provider supports it (SendGrid signed event
/// webhook: ECDSA P-256 over timestamp + body; Mailgun: HMAC-SHA256 over timestamp + token; Twilio: HMAC-SHA1 over URL
/// + sorted form). Events are idempotent by provider event id, and hard bounces and complaints always reach the
/// workspace suppression list.
/// </summary>
public sealed class WebhookService(
    AppDbContext db, ICredentialVault vault, AudienceService audiences, IDatabaseDialect dialect, TimeProvider clock, ILogger<WebhookService> logger)
{
    public static readonly TimeSpan MailgunTolerance = TimeSpan.FromMinutes(15);
    /// <summary>Replay window for SendGrid's signed timestamp (each delivery attempt is signed when it is sent).</summary>
    public static readonly TimeSpan SendGridTolerance = TimeSpan.FromMinutes(15);

    // ---------- SendGrid ----------

    public async Task<WebhookVerdict> VerifySendGridAsync(Guid? clientId, string? signature, string? timestamp, byte[] body, CancellationToken ct)
    {
        var creds = await vault.GetAsync(SendGridEmailProvider.ProviderKey, clientId, ct);
        if (creds is null || !creds.Settings.TryGetValue("webhookPublicKey", out var key) || string.IsNullOrWhiteSpace(key)) return WebhookVerdict.NotConfigured;
        return VerifySendGridSignature(key, signature, timestamp, body) && IsFreshTimestamp(timestamp, clock.GetUtcNow().UtcDateTime, SendGridTolerance)
            ? WebhookVerdict.Ok : WebhookVerdict.InvalidSignature;
    }

    /// <summary>True when <paramref name="timestamp"/> (unix seconds) is within <paramref name="tolerance"/> of now (replay protection).</summary>
    public static bool IsFreshTimestamp(string? timestamp, DateTime nowUtc, TimeSpan tolerance)
    {
        if (!long.TryParse(timestamp, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var seconds)) return false;
        if (seconds > 253_402_300_799) return false; // beyond DateTimeOffset's range (would throw)
        return (nowUtc - DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime).Duration() <= tolerance;
    }

    /// <summary>SendGrid "Signed Event Webhook": ECDSA (P-256, SHA-256, DER signature, base64) over UTF-8(timestamp) + raw body.</summary>
    public static bool VerifySendGridSignature(string publicKey, string? signature, string? timestamp, byte[] body)
    {
        if (string.IsNullOrWhiteSpace(signature) || string.IsNullOrWhiteSpace(timestamp)) return false;
        try
        {
            var der = Convert.FromBase64String(publicKey.Replace("-----BEGIN PUBLIC KEY-----", "").Replace("-----END PUBLIC KEY-----", "").Replace("\n", "").Replace("\r", "").Trim());
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(der, out _);
            var payload = Encoding.UTF8.GetBytes(timestamp).Concat(body).ToArray();
            return ecdsa.VerifyData(payload, Convert.FromBase64String(signature.Trim()), HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            return false;
        }
    }

    public static IReadOnlyList<ProviderEvent> ParseSendGrid(byte[] body)
    {
        var events = new List<ProviderEvent>();
        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return events;
        foreach (var e in doc.RootElement.EnumerateArray())
        {
            string? Str(string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            var type = Str("event");
            var kind = type switch
            {
                "delivered" => ProviderEventKind.Delivered,
                "bounce" => Str("type") == "blocked" ? ProviderEventKind.SoftBounce : ProviderEventKind.HardBounce,
                "blocked" => ProviderEventKind.SoftBounce,
                "spamreport" => ProviderEventKind.Complaint,
                "unsubscribe" or "group_unsubscribe" => ProviderEventKind.Unsubscribe,
                "dropped" => (Str("reason") ?? string.Empty).Contains("Spam", StringComparison.OrdinalIgnoreCase) ? ProviderEventKind.Complaint
                    : (Str("reason") ?? string.Empty).Contains("Bounce", StringComparison.OrdinalIgnoreCase) ? ProviderEventKind.HardBounce : ProviderEventKind.Dropped,
                _ => ProviderEventKind.Ignored,
            };
            var ts = e.TryGetProperty("timestamp", out var t) && t.TryGetInt64(out var seconds) ? DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime : DateTime.UtcNow;
            var messageId = Str("sg_message_id");
            events.Add(new ProviderEvent("sendgrid", Str("sg_event_id"), kind, Str("email"), Str("oa_ref"), messageId?.Split('.')[0], ts, Str("reason") ?? Str("response")));
        }
        return events;
    }

    // ---------- Mailgun ----------

    public async Task<(WebhookVerdict Verdict, ProviderEvent? Event)> VerifyAndParseMailgunAsync(Guid? clientId, byte[] body, CancellationToken ct)
    {
        var creds = await vault.GetAsync(MailgunEmailProvider.ProviderKey, clientId, ct);
        if (creds is null || !creds.Secrets.TryGetValue("webhookSigningKey", out var key) || string.IsNullOrWhiteSpace(key)) return (WebhookVerdict.NotConfigured, null);
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        if (!root.TryGetProperty("signature", out var sig) || !IsValidMailgunSignature(key, Get(sig, "timestamp"), Get(sig, "token"), Get(sig, "signature"), clock.GetUtcNow().UtcDateTime))
            return (WebhookVerdict.InvalidSignature, null);
        if (!root.TryGetProperty("event-data", out var data)) return (WebhookVerdict.Ok, null);
        var type = Get(data, "event");
        var kind = type switch
        {
            "delivered" => ProviderEventKind.Delivered,
            "failed" => Get(data, "severity") == "permanent" ? ProviderEventKind.HardBounce : ProviderEventKind.SoftBounce,
            "complained" => ProviderEventKind.Complaint,
            "unsubscribed" => ProviderEventKind.Unsubscribe,
            _ => ProviderEventKind.Ignored,
        };
        string? reference = null;
        if (data.TryGetProperty("user-variables", out var vars)) reference = Get(vars, "oa_ref");
        string? messageId = null;
        if (data.TryGetProperty("message", out var message) && message.TryGetProperty("headers", out var headers)) messageId = Get(headers, "message-id");
        var ts = data.TryGetProperty("timestamp", out var tsProp) && tsProp.TryGetDouble(out var seconds)
            ? DateTimeOffset.FromUnixTimeMilliseconds((long)(seconds * 1000)).UtcDateTime : clock.GetUtcNow().UtcDateTime;
        string? detail = null;
        if (data.TryGetProperty("delivery-status", out var status)) detail = Get(status, "description") ?? Get(status, "message");
        return (WebhookVerdict.Ok, new ProviderEvent("mailgun", Get(data, "id"), kind, Get(data, "recipient"), reference, messageId, ts, detail));
    }

    /// <summary>Mailgun: hex HMAC-SHA256(signing key, timestamp + token), and a fresh timestamp (replay protection).</summary>
    public static bool IsValidMailgunSignature(string signingKey, string? timestamp, string? token, string? signature, DateTime nowUtc)
    {
        if (string.IsNullOrEmpty(timestamp) || string.IsNullOrEmpty(token) || string.IsNullOrEmpty(signature)) return false;
        if (!IsFreshTimestamp(timestamp, nowUtc, MailgunTolerance)) return false;
        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(signingKey), Encoding.UTF8.GetBytes(timestamp + token));
        byte[] provided;
        try { provided = Convert.FromHexString(signature); }
        catch (FormatException) { return false; }
        return CryptographicOperations.FixedTimeEquals(provided, expected);
    }

    private static string? Get(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) ? v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString() : null;

    // ---------- Applying events ----------

    public async Task<int> ApplyAsync(Guid? clientId, IEnumerable<ProviderEvent> events, CancellationToken ct)
    {
        var applied = 0;
        foreach (var e in events)
        {
            if (e.Kind == ProviderEventKind.Ignored) continue;
            try
            {
                if (await ApplyOneAsync(clientId, e, ct)) applied++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Could not apply {Provider} event {EventId}", e.Provider, e.EventId);
                db.ChangeTracker.Clear();
            }
        }
        return applied;
    }

    private async Task<bool> ApplyOneAsync(Guid? clientId, ProviderEvent e, CancellationToken ct)
    {
        var key = Workspace.Key(clientId);
        CampaignRecipient? recipient = null;
        if (e.Reference is { Length: > 2 } reference && reference.StartsWith("c:", StringComparison.Ordinal) && Guid.TryParse(reference[2..], out var rid))
            recipient = await db.Set<CampaignRecipient>().AsNoTracking().FirstOrDefaultAsync(r => r.Id == rid, ct);
        if (recipient is null && !string.IsNullOrEmpty(e.ProviderMessageId))
            recipient = await db.Set<CampaignRecipient>().AsNoTracking().FirstOrDefaultAsync(r => r.ProviderMessageId == e.ProviderMessageId, ct);
        // Only act on this workspace's messages.
        if (recipient is not null && recipient.ClientAccountId != clientId) recipient = null;

        var email = ContactRules.NormalizeEmail(e.Email) ?? recipient?.Address;
        var subscriber = email is null ? null : await db.Set<Subscriber>().AsNoTracking().FirstOrDefaultAsync(s => s.ScopeKey == key && s.NormalizedEmail == email, ct);

        // Idempotency by provider event id.
        if (subscriber is not null && e.EventId is not null)
        {
            var type = e.Kind switch
            {
                ProviderEventKind.Delivered => EngagementType.Delivered,
                ProviderEventKind.Complaint => EngagementType.Complaint,
                ProviderEventKind.Unsubscribe => EngagementType.Unsubscribe,
                _ => EngagementType.Bounce,
            };
            db.Set<EngagementEvent>().Add(new EngagementEvent
            {
                ClientAccountId = clientId, SubscriberId = subscriber.Id, CampaignId = recipient?.CampaignId, RecipientId = recipient?.Id, Type = type,
                OccurredAt = e.OccurredAt, Provider = e.Provider, DedupKey = Text.Truncate($"{e.Provider}:{e.EventId}", 200),
                Detail = Text.Truncate(e.Kind switch { ProviderEventKind.HardBounce => "hard: ", ProviderEventKind.SoftBounce => "soft: ", _ => "" } + e.Detail, 2000),
            });
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (dialect.IsUniqueViolation(ex))
            {
                db.ChangeTracker.Clear();
                return false; // already processed
            }
        }

        var now = clock.GetUtcNow().UtcDateTime;
        if (recipient is not null)
        {
            var q = db.Set<CampaignRecipient>().Where(r => r.Id == recipient.Id);
            switch (e.Kind)
            {
                case ProviderEventKind.Delivered:
                    await q.ExecuteUpdateAsync(u => u.SetProperty(r => r.DeliveredAt, r => r.DeliveredAt ?? e.OccurredAt), ct); break;
                case ProviderEventKind.HardBounce:
                    await q.ExecuteUpdateAsync(u => u.SetProperty(r => r.BounceType, BounceType.Hard).SetProperty(r => r.BouncedAt, now), ct); break;
                case ProviderEventKind.SoftBounce:
                    await q.Where(r => r.BounceType == null).ExecuteUpdateAsync(u => u.SetProperty(r => r.BounceType, BounceType.Soft).SetProperty(r => r.BouncedAt, now), ct); break;
                case ProviderEventKind.Complaint:
                    await q.ExecuteUpdateAsync(u => u.SetProperty(r => r.ComplainedAt, r => r.ComplainedAt ?? now), ct); break;
                case ProviderEventKind.Dropped:
                    await q.ExecuteUpdateAsync(u => u.SetProperty(r => r.Error, Text.Truncate("Dropped by the provider: " + e.Detail, 1000)), ct); break;
            }
        }
        if (email is null) return true;
        switch (e.Kind)
        {
            case ProviderEventKind.HardBounce:
                await audiences.ApplyDeliveryFailureAsync(clientId, email, BounceType.Hard, false, e.Provider + "-webhook", ct); break;
            case ProviderEventKind.SoftBounce:
                await audiences.ApplyDeliveryFailureAsync(clientId, email, BounceType.Soft, false, e.Provider + "-webhook", ct); break;
            case ProviderEventKind.Complaint:
                await audiences.ApplyDeliveryFailureAsync(clientId, email, null, true, e.Provider + "-webhook", ct); break;
            case ProviderEventKind.Unsubscribe when subscriber is not null:
                var tracked = await db.Set<Subscriber>().FirstAsync(s => s.Id == subscriber.Id, ct);
                await audiences.UnsubscribeAsync(tracked, null, e.Provider + "-webhook", null, recipient, ct);
                break;
        }
        return true;
    }

    // ---------- Twilio ----------

    public async Task<(WebhookVerdict Verdict, string? AuthToken)> VerifyTwilioAsync(Guid? clientId, string url, IEnumerable<KeyValuePair<string, string>> form,
        string? signature, CancellationToken ct)
    {
        var creds = await vault.GetAsync(TwilioSmsProvider.ProviderKey, clientId, ct);
        if (creds is null || !creds.Secrets.TryGetValue("authToken", out var token) || string.IsNullOrWhiteSpace(token)) return (WebhookVerdict.NotConfigured, null);
        return TwilioSmsProvider.IsValidSignature(token, url, form, signature) ? (WebhookVerdict.Ok, token) : (WebhookVerdict.InvalidSignature, null);
    }

    /// <summary>Inbound SMS: STOP-family keywords opt the number out (suppression + consent withdrawn); START opts back in.</summary>
    public async Task<string> HandleInboundSmsAsync(Guid? clientId, string? from, string? body, CancellationToken ct)
    {
        var phone = ContactRules.NormalizePhone(from);
        if (phone is null) return "ignored";
        var key = Workspace.Key(clientId);
        var contacts = await db.Set<Subscriber>().Where(s => s.ScopeKey == key && s.Phone == phone).ToListAsync(ct);
        if (SmsSegments.IsStop(body))
        {
            foreach (var s in contacts.Where(s => s.SmsConsent != ConsentStatus.Withdrawn))
                await audiences.SetConsentAsync(s, MessageChannel.Sms, ConsentStatus.Withdrawn, "sms-stop", null, null, null, $"Replied \"{Text.Truncate(body, 20)}\"", ct);
            await db.SaveChangesAsync(ct);
            await audiences.AddSuppressionAsync(clientId, MessageChannel.Sms, phone, SuppressionReason.StopKeyword, "sms-stop", null, null, ct);
            return "opted-out";
        }
        if (SmsSegments.IsStart(body))
        {
            await db.Set<Suppression>().Where(x => x.ScopeKey == key && x.Channel == MessageChannel.Sms && x.Value == phone && x.Reason == SuppressionReason.StopKeyword)
                .ExecuteDeleteAsync(ct);
            foreach (var s in contacts.Where(s => s.SmsConsent != ConsentStatus.Granted))
                await audiences.SetConsentAsync(s, MessageChannel.Sms, ConsentStatus.Granted, "sms-start", null, null, null, "Replied START", ct);
            await db.SaveChangesAsync(ct);
            return "opted-in";
        }
        return "ignored";
    }

    /// <summary>Twilio status callback: delivered / undelivered / failed. Error 21610 (recipient opted out at the carrier) suppresses the number.</summary>
    public async Task HandleSmsStatusAsync(Guid? clientId, string? messageSid, string? status, string? errorCode, string? to, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(messageSid)) return;
        var q = db.Set<CampaignRecipient>().Where(r => r.ProviderMessageId == messageSid && r.ClientAccountId == clientId);
        var now = clock.GetUtcNow().UtcDateTime;
        switch (status)
        {
            case "delivered":
                await q.ExecuteUpdateAsync(u => u.SetProperty(r => r.DeliveredAt, r => r.DeliveredAt ?? now), ct); break;
            case "undelivered" or "failed":
                var error = Text.Truncate($"Twilio {status} (error {errorCode})", 1000);
                await q.ExecuteUpdateAsync(u => u.SetProperty(r => r.BounceType, BounceType.Soft).SetProperty(r => r.BouncedAt, now).SetProperty(r => r.Error, error), ct);
                if (errorCode == "21610" && ContactRules.NormalizePhone(to) is { } phone)
                    await audiences.AddSuppressionAsync(clientId, MessageChannel.Sms, phone, SuppressionReason.StopKeyword, "twilio-21610", null, null, ct);
                break;
        }
    }
}
