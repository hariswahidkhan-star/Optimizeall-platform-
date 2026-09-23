using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Modules.Notifications;
using OptimizeAll.Domain.Integrations;

namespace OptimizeAll.Api.Modules.EmailMarketing.Delivery;

/// <summary>SMS adapter. Returns NotConfigured without credentials and Accepted only with the provider's message id.</summary>
public interface ISmsProvider
{
    string Key { get; }
    Task<ProviderResult> SendAsync(Guid? clientAccountId, string toE164, string body, string? statusCallbackUrl, CancellationToken ct);
}

/// <summary>
/// Twilio Programmable Messaging (REST, <c>POST /2010-04-01/Accounts/{sid}/Messages.json</c>). Vault provider "twilio":
/// settings <c>accountSid</c> and <c>fromNumber</c> or <c>messagingServiceSid</c>; secret <c>authToken</c>
/// (also used to verify webhook signatures).
/// </summary>
public sealed class TwilioSmsProvider(HttpClient http, ICredentialVault vault, ILogger<TwilioSmsProvider> logger) : ISmsProvider
{
    public const string ProviderKey = "twilio";
    public string Key => ProviderKey;

    public async Task<ProviderResult> SendAsync(Guid? clientAccountId, string toE164, string body, string? statusCallbackUrl, CancellationToken ct)
    {
        var creds = await vault.GetAsync(ProviderKey, clientAccountId, ct);
        if (creds is null || !creds.Settings.TryGetValue("accountSid", out var sid) || string.IsNullOrWhiteSpace(sid) ||
            !creds.Secrets.TryGetValue("authToken", out var token) || string.IsNullOrWhiteSpace(token))
            return ProviderResult.NotConfigured("Twilio is not configured for this workspace (integration \"twilio\" with accountSid and authToken).");
        creds.Settings.TryGetValue("fromNumber", out var from);
        creds.Settings.TryGetValue("messagingServiceSid", out var service);
        if (string.IsNullOrWhiteSpace(from) && string.IsNullOrWhiteSpace(service))
            return ProviderResult.NotConfigured("Twilio needs a fromNumber or messagingServiceSid setting.");
        var baseUrl = creds.Settings.TryGetValue("apiBaseUrl", out var b) && Uri.TryCreate(b, UriKind.Absolute, out var u) && u.Scheme == "https"
            ? b.TrimEnd('/') : "https://api.twilio.com";

        var form = new List<KeyValuePair<string, string>> { new("To", toE164), new("Body", body) };
        form.Add(string.IsNullOrWhiteSpace(service) ? new("From", from!) : new("MessagingServiceSid", service));
        if (!string.IsNullOrWhiteSpace(statusCallbackUrl)) form.Add(new("StatusCallback", statusCallbackUrl));

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/2010-04-01/Accounts/{Uri.EscapeDataString(sid)}/Messages.json")
        {
            Content = new FormUrlEncodedContent(form),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{sid}:{token}")));
        try
        {
            using var response = await http.SendAsync(request, ct);
            var text = await response.Content.ReadAsStringAsync(ct);
            if (response.IsSuccessStatusCode)
            {
                string? messageSid = null;
                try
                {
                    using var doc = JsonDocument.Parse(text);
                    if (doc.RootElement.TryGetProperty("sid", out var s)) messageSid = s.GetString();
                }
                catch (JsonException) { }
                return string.IsNullOrWhiteSpace(messageSid)
                    ? ProviderResult.Transient("Twilio answered without a message sid; treating the outcome as unknown.")
                    : ProviderResult.Accepted(messageSid);
            }
            var result = ProviderResult.FromHttpStatus(response.StatusCode, text, "Twilio");
            if (result.Outcome == ProviderOutcome.NotConfigured) await vault.MarkStatusAsync(creds.ConnectionId, IntegrationStatus.Error, result.Error, ct);
            return result;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Twilio request failed");
            return ProviderResult.Transient($"Twilio request failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Twilio request validation: Base64(HMAC-SHA1(authToken, url + concatenated sorted form key/value pairs)) compared
    /// in constant time with the <c>X-Twilio-Signature</c> header.
    /// </summary>
    public static bool IsValidSignature(string authToken, string url, IEnumerable<KeyValuePair<string, string>> form, string? signature)
    {
        if (string.IsNullOrWhiteSpace(signature) || string.IsNullOrEmpty(authToken)) return false;
        var data = new StringBuilder(url);
        foreach (var pair in form.OrderBy(p => p.Key, StringComparer.Ordinal)) data.Append(pair.Key).Append(pair.Value);
        var expected = HMACSHA1.HashData(Encoding.UTF8.GetBytes(authToken), Encoding.UTF8.GetBytes(data.ToString()));
        byte[] provided;
        try { provided = Convert.FromBase64String(signature.Trim()); }
        catch (FormatException) { return false; }
        return CryptographicOperations.FixedTimeEquals(provided, expected);
    }

    public static string ComputeSignature(string authToken, string url, IEnumerable<KeyValuePair<string, string>> form)
    {
        var data = new StringBuilder(url);
        foreach (var pair in form.OrderBy(p => p.Key, StringComparer.Ordinal)) data.Append(pair.Key).Append(pair.Value);
        return Convert.ToBase64String(HMACSHA1.HashData(Encoding.UTF8.GetBytes(authToken), Encoding.UTF8.GetBytes(data.ToString())));
    }
}

/// <summary>WhatsApp Business template message sender.</summary>
public interface IWhatsAppTemplateProvider
{
    Task<ProviderResult> SendTemplateAsync(Guid? clientAccountId, string toE164, string templateName, string language,
        IReadOnlyList<string> bodyParameters, CancellationToken ct);
}

/// <summary>
/// WhatsApp Business Cloud API template messages, following the notification module's WhatsApp adapter
/// (same payload shape and parameter rules) but with per-workspace credentials from the vault: provider "whatsapp",
/// settings <c>phoneNumberId</c> (and optional <c>apiBaseUrl</c>), secret <c>accessToken</c>.
/// </summary>
public sealed class WhatsAppCloudTemplateProvider(HttpClient http, ICredentialVault vault, ILogger<WhatsAppCloudTemplateProvider> logger)
    : IWhatsAppTemplateProvider
{
    public const string ProviderKey = "whatsapp";

    public async Task<ProviderResult> SendTemplateAsync(Guid? clientAccountId, string toE164, string templateName, string language,
        IReadOnlyList<string> bodyParameters, CancellationToken ct)
    {
        var creds = await vault.GetAsync(ProviderKey, clientAccountId, ct);
        if (creds is null || !creds.Settings.TryGetValue("phoneNumberId", out var phoneId) || string.IsNullOrWhiteSpace(phoneId) ||
            !creds.Secrets.TryGetValue("accessToken", out var accessToken) || string.IsNullOrWhiteSpace(accessToken))
            return ProviderResult.NotConfigured(WhatsAppChannelSender.NotConfiguredError + " for this workspace (integration \"whatsapp\").");
        var baseUrl = creds.Settings.TryGetValue("apiBaseUrl", out var b) && Uri.TryCreate(b, UriKind.Absolute, out var u) && u.Scheme == "https"
            ? b.TrimEnd('/') : "https://graph.facebook.com/v20.0";

        var payload = new Dictionary<string, object>
        {
            ["messaging_product"] = "whatsapp",
            ["to"] = toE164.TrimStart('+'),
            ["type"] = "template",
            ["template"] = new Dictionary<string, object>
            {
                ["name"] = templateName,
                ["language"] = new Dictionary<string, string> { ["code"] = language },
                ["components"] = new object[]
                {
                    new Dictionary<string, object>
                    {
                        ["type"] = "body",
                        ["parameters"] = bodyParameters
                            .Select(p => new Dictionary<string, string> { ["type"] = "text", ["text"] = WhatsAppChannelSender.TemplateText(p, WhatsAppChannelSender.MaxBodyParameterLength) })
                            .ToArray(),
                    },
                },
            },
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/{Uri.EscapeDataString(phoneId)}/messages") { Content = JsonContent.Create(payload) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
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
                    if (doc.RootElement.TryGetProperty("messages", out var m) && m.ValueKind == JsonValueKind.Array && m.GetArrayLength() > 0 &&
                        m[0].TryGetProperty("id", out var idProp)) id = idProp.GetString();
                }
                catch (JsonException) { }
                return string.IsNullOrWhiteSpace(id)
                    ? ProviderResult.Transient("WhatsApp answered without a message id; treating the outcome as unknown.")
                    : ProviderResult.Accepted(id);
            }
            var result = ProviderResult.FromHttpStatus(response.StatusCode, body, "WhatsApp");
            if (result.Outcome == ProviderOutcome.NotConfigured) await vault.MarkStatusAsync(creds.ConnectionId, IntegrationStatus.Error, result.Error, ct);
            return result;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "WhatsApp request failed");
            return ProviderResult.Transient($"WhatsApp request failed: {ex.Message}");
        }
    }
}
