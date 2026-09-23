using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MimeKit;
using OptimizeAll.Api.Common.Notifications;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Domain.EmailMarketing;
using OptimizeAll.Domain.Integrations;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.EmailMarketing.Delivery;

public enum ProviderOutcome
{
    /// <summary>The provider acknowledged the message (it has a provider message id). Only this counts as "sent".</summary>
    Accepted,
    /// <summary>Temporary failure (timeout, 429, 5xx): retried with backoff.</summary>
    TransientFailure,
    /// <summary>The provider rejected the message: not retried.</summary>
    PermanentFailure,
    /// <summary>No credentials/configuration: nothing was attempted.</summary>
    NotConfigured,
}

public sealed record ProviderResult(ProviderOutcome Outcome, string? MessageId = null, string? Error = null)
{
    public static ProviderResult Accepted(string messageId) => new(ProviderOutcome.Accepted, messageId);
    public static ProviderResult Transient(string error) => new(ProviderOutcome.TransientFailure, null, error);
    public static ProviderResult Permanent(string error) => new(ProviderOutcome.PermanentFailure, null, error);
    public static ProviderResult NotConfigured(string error) => new(ProviderOutcome.NotConfigured, null, error);

    public static ProviderResult FromHttpStatus(HttpStatusCode status, string body, string provider)
    {
        var code = (int)status;
        var message = $"{provider} returned {code}: {Shorten(body)}";
        return code == 429 || code >= 500 ? Transient(message)
            : code is 401 or 403 ? NotConfigured($"{provider} rejected the credentials ({code}). Check the integration: {Shorten(body)}")
            : Permanent(message);
    }

    public static string Shorten(string? s, int max = 400) => s is null ? string.Empty : s.Length <= max ? s : s[..max] + "…";
}

/// <summary>A rendered marketing email ready for a provider.</summary>
public sealed record OutboundEmail(
    Guid? ClientAccountId,
    string To,
    string? ToName,
    string FromEmail,
    string FromName,
    string? ReplyTo,
    string Subject,
    string Html,
    string Text,
    IReadOnlyDictionary<string, string> Headers,
    IReadOnlyDictionary<string, string> Metadata); // Metadata: correlation values echoed back by provider webhooks.

/// <summary>
/// Email service provider adapter. Implementations must return <see cref="ProviderOutcome.Accepted"/> only with the
/// provider's acknowledgement (message id) and <see cref="ProviderOutcome.NotConfigured"/> when credentials are missing.
/// </summary>
public interface IEmailMarketingProvider
{
    /// <summary>smtp, sendgrid, mailgun, ses, postmark.</summary>
    string Key { get; }
    Task<ProviderResult> SendAsync(OutboundEmail email, CancellationToken ct);
}

/// <summary>Picks the provider configured for a workspace (last registration per key wins, so tests can substitute one).</summary>
public sealed class EmailProviderResolver(IEnumerable<IEmailMarketingProvider> providers, AppDbContext db)
{
    public static readonly string[] Keys = { "smtp", "sendgrid", "mailgun", "ses", "postmark" };

    public async Task<IEmailMarketingProvider> ForWorkspaceAsync(Guid? clientAccountId, CancellationToken ct)
    {
        var key = Workspace.Key(clientAccountId);
        var configured = await db.Set<EmailWorkspaceSettings>().AsNoTracking().Where(s => s.ScopeKey == key)
            .Select(s => s.EmailProvider).FirstOrDefaultAsync(ct) ?? "smtp";
        return ByKey(configured);
    }

    public IEmailMarketingProvider ByKey(string key)
    {
        IEmailMarketingProvider? match = null;
        foreach (var p in providers) if (string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase)) match = p;
        return match ?? new UnavailableEmailProvider(key, $"No adapter is registered for email provider '{key}'.");
    }
}

/// <summary>
/// Default adapter: the platform SMTP relay configured in <see cref="EmailOptions"/> (Email:Mode=Smtp), or .eml files in the
/// pickup directory in development (Email:Mode=File). Builds the MIME message itself so marketing headers
/// (List-Unsubscribe, List-Unsubscribe-Post, Reply-To, the workspace sender) are preserved.
/// </summary>
public sealed class SmtpEmailMarketingProvider(IOptions<EmailOptions> options, IHostEnvironment env, ILogger<SmtpEmailMarketingProvider> logger)
    : IEmailMarketingProvider
{
    public string Key => "smtp";

    public async Task<ProviderResult> SendAsync(OutboundEmail email, CancellationToken ct)
    {
        var o = options.Value;
        var mime = BuildMime(email);
        if (o.Mode != "Smtp")
        {
            var dir = Path.IsPathRooted(o.PickupDirectory) ? o.PickupDirectory : Path.Combine(env.ContentRootPath, o.PickupDirectory);
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.eml");
            await mime.WriteToAsync(path, ct);
            return ProviderResult.Accepted(mime.MessageId ?? string.Empty);
        }
        if (string.IsNullOrWhiteSpace(o.SmtpHost)) return ProviderResult.NotConfigured("SMTP host is not configured (Email:SmtpHost).");
        try
        {
            using var client = new SmtpClient();
            await client.ConnectAsync(o.SmtpHost, o.SmtpPort, o.SmtpUseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto, ct);
            if (!string.IsNullOrEmpty(o.SmtpUsername)) await client.AuthenticateAsync(o.SmtpUsername, o.SmtpPassword ?? string.Empty, ct);
            await client.SendAsync(mime, ct);
            await client.DisconnectAsync(true, ct);
            return ProviderResult.Accepted(mime.MessageId ?? string.Empty);
        }
        catch (SmtpCommandException ex) when ((int)ex.StatusCode >= 500)
        {
            return ProviderResult.Permanent($"SMTP rejected the message: {ex.Message}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Marketing SMTP send failed");
            return ProviderResult.Transient($"SMTP send failed: {ex.Message}");
        }
    }

    public static MimeMessage BuildMime(OutboundEmail email)
    {
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(email.FromName, email.FromEmail));
        mime.To.Add(new MailboxAddress(email.ToName ?? string.Empty, email.To));
        if (!string.IsNullOrWhiteSpace(email.ReplyTo)) mime.ReplyTo.Add(MailboxAddress.Parse(email.ReplyTo));
        mime.Subject = email.Subject;
        var domain = email.FromEmail.Split('@').LastOrDefault() ?? "optimizeall.local";
        mime.MessageId = MimeKit.Utils.MimeUtils.GenerateMessageId(domain);
        foreach (var (name, value) in email.Headers) mime.Headers.Add(name, value);
        foreach (var (name, value) in email.Metadata) mime.Headers.Add("X-OA-" + name, value);
        mime.Body = new BodyBuilder { TextBody = email.Text, HtmlBody = email.Html }.ToMessageBody();
        return mime;
    }
}

/// <summary>SendGrid v3 <c>mail/send</c>. Vault provider "sendgrid": secret <c>apiKey</c>; settings <c>apiBaseUrl</c> (EU: https://api.eu.sendgrid.com), <c>webhookPublicKey</c>.</summary>
public sealed class SendGridEmailProvider(HttpClient http, ICredentialVault vault, ILogger<SendGridEmailProvider> logger) : IEmailMarketingProvider
{
    public const string ProviderKey = "sendgrid";
    public string Key => ProviderKey;

    public async Task<ProviderResult> SendAsync(OutboundEmail email, CancellationToken ct)
    {
        var creds = await vault.GetAsync(ProviderKey, email.ClientAccountId, ct);
        if (creds is null || !creds.Secrets.TryGetValue("apiKey", out var apiKey) || string.IsNullOrWhiteSpace(apiKey))
            return ProviderResult.NotConfigured("SendGrid is not configured for this workspace (integration \"sendgrid\" with an apiKey).");
        var baseUrl = creds.Settings.TryGetValue("apiBaseUrl", out var b) && Uri.TryCreate(b, UriKind.Absolute, out var u) && u.Scheme == "https"
            ? b.TrimEnd('/') : "https://api.sendgrid.com";

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/v3/mail/send")
        {
            Content = new StringContent(JsonSerializer.Serialize(BuildPayload(email)), Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        try
        {
            using var response = await http.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (response.StatusCode == HttpStatusCode.Accepted || response.IsSuccessStatusCode)
            {
                var id = response.Headers.TryGetValues("X-Message-Id", out var values) ? values.FirstOrDefault() : null;
                return string.IsNullOrWhiteSpace(id)
                    ? ProviderResult.Transient("SendGrid accepted the request without an X-Message-Id; treating the outcome as unknown.")
                    : ProviderResult.Accepted(id);
            }
            var result = ProviderResult.FromHttpStatus(response.StatusCode, body, "SendGrid");
            if (result.Outcome == ProviderOutcome.NotConfigured) await vault.MarkStatusAsync(creds.ConnectionId, IntegrationStatus.Error, result.Error, ct);
            return result;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "SendGrid request failed");
            return ProviderResult.Transient($"SendGrid request failed: {ex.Message}");
        }
    }

    /// <summary>
    /// v3 payload. SendGrid's own click/open/subscription tracking is disabled because the platform tracks (and signs)
    /// links itself and adds RFC 8058 headers; our correlation id travels in <c>custom_args</c> for the event webhook.
    /// </summary>
    public static Dictionary<string, object> BuildPayload(OutboundEmail email)
    {
        var personalization = new Dictionary<string, object>
        {
            ["to"] = new[] { email.ToName is { Length: > 0 } ? new Dictionary<string, string> { ["email"] = email.To, ["name"] = email.ToName } : new Dictionary<string, string> { ["email"] = email.To } },
        };
        if (email.Metadata.Count > 0) personalization["custom_args"] = email.Metadata;
        var payload = new Dictionary<string, object>
        {
            ["personalizations"] = new[] { personalization },
            ["from"] = new Dictionary<string, string> { ["email"] = email.FromEmail, ["name"] = email.FromName },
            ["subject"] = email.Subject,
            ["content"] = new[]
            {
                new Dictionary<string, string> { ["type"] = "text/plain", ["value"] = email.Text },
                new Dictionary<string, string> { ["type"] = "text/html", ["value"] = email.Html },
            },
            ["tracking_settings"] = new Dictionary<string, object>
            {
                ["click_tracking"] = new Dictionary<string, bool> { ["enable"] = false, ["enable_text"] = false },
                ["open_tracking"] = new Dictionary<string, bool> { ["enable"] = false },
                ["subscription_tracking"] = new Dictionary<string, bool> { ["enable"] = false },
            },
        };
        if (!string.IsNullOrWhiteSpace(email.ReplyTo)) payload["reply_to"] = new Dictionary<string, string> { ["email"] = email.ReplyTo };
        if (email.Headers.Count > 0) payload["headers"] = email.Headers;
        return payload;
    }
}

/// <summary>Mailgun <c>POST /v3/{domain}/messages</c>. Vault provider "mailgun": secrets <c>apiKey</c>, <c>webhookSigningKey</c>; settings <c>domain</c>, <c>apiBaseUrl</c> (EU: https://api.eu.mailgun.net).</summary>
public sealed class MailgunEmailProvider(HttpClient http, ICredentialVault vault, ILogger<MailgunEmailProvider> logger) : IEmailMarketingProvider
{
    public const string ProviderKey = "mailgun";
    public string Key => ProviderKey;

    public async Task<ProviderResult> SendAsync(OutboundEmail email, CancellationToken ct)
    {
        var creds = await vault.GetAsync(ProviderKey, email.ClientAccountId, ct);
        if (creds is null || !creds.Secrets.TryGetValue("apiKey", out var apiKey) || string.IsNullOrWhiteSpace(apiKey) ||
            !creds.Settings.TryGetValue("domain", out var domain) || string.IsNullOrWhiteSpace(domain))
            return ProviderResult.NotConfigured("Mailgun is not configured for this workspace (integration \"mailgun\" with apiKey and domain).");
        var baseUrl = creds.Settings.TryGetValue("apiBaseUrl", out var b) && Uri.TryCreate(b, UriKind.Absolute, out var u) && u.Scheme == "https"
            ? b.TrimEnd('/') : "https://api.mailgun.net";

        var form = new List<KeyValuePair<string, string>>
        {
            new("from", new MailboxAddress(email.FromName, email.FromEmail).ToString()),
            new("to", email.ToName is { Length: > 0 } ? new MailboxAddress(email.ToName, email.To).ToString() : email.To),
            new("subject", email.Subject),
            new("text", email.Text),
            new("html", email.Html),
            new("o:tracking", "no"),
        };
        if (!string.IsNullOrWhiteSpace(email.ReplyTo)) form.Add(new("h:Reply-To", email.ReplyTo));
        form.AddRange(email.Headers.Select(h => new KeyValuePair<string, string>("h:" + h.Key, h.Value)));
        form.AddRange(email.Metadata.Select(m => new KeyValuePair<string, string>("v:" + m.Key, m.Value)));

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/v3/{Uri.EscapeDataString(domain)}/messages")
        {
            Content = new FormUrlEncodedContent(form),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("api:" + apiKey)));
        try
        {
            using var response = await http.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (response.IsSuccessStatusCode)
            {
                string? id = null;
                try
                {
                    using var doc = JsonDocument.Parse(body);
                    if (doc.RootElement.TryGetProperty("id", out var idProp)) id = idProp.GetString();
                }
                catch (JsonException) { }
                return string.IsNullOrWhiteSpace(id)
                    ? ProviderResult.Transient("Mailgun answered without a message id; treating the outcome as unknown.")
                    : ProviderResult.Accepted(id.Trim('<', '>'));
            }
            var result = ProviderResult.FromHttpStatus(response.StatusCode, body, "Mailgun");
            if (result.Outcome == ProviderOutcome.NotConfigured) await vault.MarkStatusAsync(creds.ConnectionId, IntegrationStatus.Error, result.Error, ct);
            return result;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Mailgun request failed");
            return ProviderResult.Transient($"Mailgun request failed: {ex.Message}");
        }
    }
}

/// <summary>Adapter slot for providers without an implementation in this release (Amazon SES, Postmark). Never sends.</summary>
public sealed class UnavailableEmailProvider(string key, string reason) : IEmailMarketingProvider
{
    public string Key => key;

    public Task<ProviderResult> SendAsync(OutboundEmail email, CancellationToken ct) => Task.FromResult(ProviderResult.NotConfigured(reason));
}
