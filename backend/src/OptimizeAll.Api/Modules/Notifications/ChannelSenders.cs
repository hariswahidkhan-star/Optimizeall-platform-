using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using OptimizeAll.Api.Common.Notifications;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Notifications;

namespace OptimizeAll.Api.Modules.Notifications;

public enum ChannelSendStatus
{
    Sent,
    Failed,
    /// <summary>Not attempted by design (channel not configured, no address, recipient opted out). Never retried.</summary>
    Skipped,
}

public sealed record ChannelSendResult(ChannelSendStatus Status, string? ProviderMessageId = null, string? Error = null)
{
    public static ChannelSendResult Sent(string? providerMessageId) => new(ChannelSendStatus.Sent, providerMessageId);
    public static ChannelSendResult Failed(string error) => new(ChannelSendStatus.Failed, null, error);
    public static ChannelSendResult Skipped(string reason) => new(ChannelSendStatus.Skipped, null, reason);
}

/// <summary>
/// Adapter for one external notification channel. The dispatch job picks the last registered sender for each
/// channel, so a deployment (or test) can replace a provider by registering another implementation.
/// </summary>
public interface INotificationChannelSender
{
    NotificationChannel Channel { get; }
    Task<ChannelSendResult> SendAsync(NotificationDelivery delivery, Notification notification, User user, CancellationToken ct);
}

internal static class RecipientRules
{
    /// <summary>Suspended/deactivated users only receive essential (transactional) messages on external channels.</summary>
    public static string? SkipReason(Notification notification, User user) =>
        user.Status != UserStatus.Active && !NotificationTypes.Essential.Contains(notification.Type)
            ? "Recipient account is not active; only essential messages are sent."
            : null;
}

/// <summary>Sends notifications as plain-text + simple HTML email through <see cref="IEmailSender"/>.</summary>
public sealed class EmailChannelSender(IEmailSender email, IOptions<EmailOptions> options) : INotificationChannelSender
{
    public NotificationChannel Channel => NotificationChannel.Email;

    public async Task<ChannelSendResult> SendAsync(NotificationDelivery delivery, Notification notification, User user, CancellationToken ct)
    {
        if (RecipientRules.SkipReason(notification, user) is { } reason) return ChannelSendResult.Skipped(reason);
        if (string.IsNullOrWhiteSpace(user.Email)) return ChannelSendResult.Skipped("Recipient has no email address.");

        var message = Compose(notification, user, options.Value.AppBaseUrl);
        var result = await email.SendAsync(message, ct);
        return result.Success
            ? ChannelSendResult.Sent(result.ProviderMessageId)
            : ChannelSendResult.Failed(result.Error ?? "Email provider reported a failure.");
    }

    /// <summary>Builds the email. All user-controlled text is HTML-encoded in the HTML part.</summary>
    public static EmailMessage Compose(Notification notification, User user, string appBaseUrl)
    {
        var link = BuildLink(appBaseUrl, notification.LinkUrl);
        var preferences = BuildLink(appBaseUrl, "/app/settings/notifications")!;
        var text = $"Hi {user.DisplayName},\n\n{notification.Body}\n\n" +
                   (link is null ? string.Empty : $"Open in Optimize All: {link}\n\n") +
                   $"— Optimize All\nManage your notification settings: {preferences}\n";

        var enc = HtmlEncoder.Default;
        var html = "<!DOCTYPE html><html><body style=\"font-family:Arial,Helvetica,sans-serif;color:#1f2937;line-height:1.5\">" +
                   $"<p>Hi {enc.Encode(user.DisplayName)},</p>" +
                   $"<h2 style=\"font-size:18px\">{enc.Encode(notification.Title)}</h2>" +
                   $"<p>{enc.Encode(notification.Body).Replace("&#xA;", "<br>")}</p>" +
                   (link is null ? string.Empty
                       : $"<p><a href=\"{enc.Encode(link)}\" style=\"background:#2563eb;color:#ffffff;padding:10px 16px;border-radius:6px;text-decoration:none\">Open in Optimize All</a></p>") +
                   $"<p style=\"font-size:12px;color:#6b7280\">Optimize All · <a href=\"{enc.Encode(preferences)}\">Notification settings</a></p>" +
                   "</body></html>";

        return new EmailMessage(user.Email, user.DisplayName, notification.Title, text, html);
    }

    /// <summary>App-relative links are prefixed with the app base URL; absolute https links are kept; anything else is dropped.</summary>
    public static string? BuildLink(string appBaseUrl, string? linkUrl)
    {
        if (string.IsNullOrWhiteSpace(linkUrl)) return null;
        if (linkUrl.StartsWith('/') && !linkUrl.StartsWith("//", StringComparison.Ordinal))
            return appBaseUrl.TrimEnd('/') + linkUrl;
        return Uri.TryCreate(linkUrl, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps ? linkUrl : null;
    }
}

/// <summary>WhatsApp Business Cloud API configuration (section "WhatsApp"). Secrets come from secret configuration.</summary>
public sealed class WhatsAppOptions
{
    public const string Section = "WhatsApp";

    public bool Enabled { get; set; }
    public string? PhoneNumberId { get; set; }
    public string? AccessToken { get; set; }
    public string ApiBaseUrl { get; set; } = "https://graph.facebook.com/v20.0";

    /// <summary>Approved message template with two body parameters: {{1}} title, {{2}} body.</summary>
    public string? TemplateName { get; set; }
    public string TemplateLanguage { get; set; } = "en";

    public bool IsConfigured =>
        Enabled && !string.IsNullOrWhiteSpace(PhoneNumberId) && !string.IsNullOrWhiteSpace(AccessToken) &&
        !string.IsNullOrWhiteSpace(TemplateName) && !string.IsNullOrWhiteSpace(ApiBaseUrl);
}

/// <summary>
/// WhatsApp Business Cloud API adapter: sends an approved template message. Never pretends to send: when the
/// integration is not configured the delivery is Skipped with an explicit reason.
/// </summary>
public sealed partial class WhatsAppChannelSender(HttpClient http, IOptions<WhatsAppOptions> options, ILogger<WhatsAppChannelSender> logger)
    : INotificationChannelSender
{
    public const string NotConfiguredError = "WhatsApp Business credentials are not configured";
    public const int MaxBodyParameterLength = 1024;

    public NotificationChannel Channel => NotificationChannel.WhatsApp;

    public async Task<ChannelSendResult> SendAsync(NotificationDelivery delivery, Notification notification, User user, CancellationToken ct)
    {
        var o = options.Value;
        if (!o.IsConfigured) return ChannelSendResult.Skipped(NotConfiguredError);
        if (RecipientRules.SkipReason(notification, user) is { } reason) return ChannelSendResult.Skipped(reason);
        if (!user.WhatsAppOptIn || string.IsNullOrWhiteSpace(user.WhatsAppNumber))
            return ChannelSendResult.Skipped("Recipient has not opted in to WhatsApp messages.");

        var payload = BuildPayload(o, user.WhatsAppNumber, notification);
        var url = $"{o.ApiBaseUrl.TrimEnd('/')}/{Uri.EscapeDataString(o.PhoneNumberId!)}/messages";
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(payload) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", o.AccessToken);

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "WhatsApp send for delivery {DeliveryId} failed", delivery.Id);
            return ChannelSendResult.Failed($"WhatsApp API request failed: {ex.Message}");
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            if (response.IsSuccessStatusCode && TryReadMessageId(body) is { } id)
                return ChannelSendResult.Sent(id);

            var error = response.IsSuccessStatusCode
                ? $"WhatsApp API returned {(int)response.StatusCode} without a message id: {Truncate(body, 500)}"
                : $"WhatsApp API returned {(int)response.StatusCode}: {Truncate(body, 500)}";
            return ChannelSendResult.Failed(error);
        }
    }

    /// <summary>Template message payload. The number is sent without '+' as the Cloud API expects.</summary>
    public static object BuildPayload(WhatsAppOptions o, string e164Number, Notification notification) => new Dictionary<string, object>
    {
        ["messaging_product"] = "whatsapp",
        ["to"] = e164Number.TrimStart('+'),
        ["type"] = "template",
        ["template"] = new Dictionary<string, object>
        {
            ["name"] = o.TemplateName!,
            ["language"] = new Dictionary<string, string> { ["code"] = o.TemplateLanguage },
            ["components"] = new object[]
            {
                new Dictionary<string, object>
                {
                    ["type"] = "body",
                    ["parameters"] = new object[]
                    {
                        new Dictionary<string, string> { ["type"] = "text", ["text"] = TemplateText(notification.Title, 200) },
                        new Dictionary<string, string> { ["type"] = "text", ["text"] = TemplateText(notification.Body, MaxBodyParameterLength) },
                    },
                },
            },
        },
    };

    /// <summary>Template parameters may not contain new lines, tabs or more than four consecutive spaces.</summary>
    public static string TemplateText(string value, int max)
    {
        var flat = WhitespaceRegex().Replace(value.Replace('\n', ' ').Replace('\r', ' ').Replace('\t', ' '), " ").Trim();
        return flat.Length <= max ? flat : flat[..(max - 1)] + "…";
    }

    private static string? TryReadMessageId(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array &&
                messages.GetArrayLength() > 0 && messages[0].TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(id.GetString()))
                return id.GetString();
        }
        catch (JsonException)
        {
        }
        return null;
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";

    [GeneratedRegex(" {2,}")]
    private static partial Regex WhitespaceRegex();
}
