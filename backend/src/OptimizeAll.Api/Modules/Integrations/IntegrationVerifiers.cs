using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Jobs;
using OptimizeAll.Api.Common.Notifications;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Domain.Audit;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Integrations;
using OptimizeAll.Domain.Notifications;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Integrations;

public sealed record VerificationResult(IntegrationStatus Status, string Message);

/// <summary>
/// Checks credentials against the provider with a harmless read-only call. Providers without a verifier are saved as
/// Unverified with an explanatory message (never reported as connected).
/// </summary>
public sealed class IntegrationVerifier(IHttpClientFactory httpFactory, IConfiguration configuration, ILogger<IntegrationVerifier> logger)
{
    public const string HttpClientName = "integrations";

    private static readonly HashSet<string> Supported = new() { "dataforseo", "sendgrid", "mailgun", "stripe", "twilio", "google-search-console" };

    public static bool CanVerify(string provider) => Supported.Contains(provider);

    public async Task<VerificationResult> VerifyAsync(string provider, IReadOnlyDictionary<string, string> settings,
        IReadOnlyDictionary<string, string> secrets, CancellationToken ct)
    {
        var descriptor = ProviderRegistry.Find(provider);
        if (descriptor is null || !CanVerify(provider))
            return new VerificationResult(IntegrationStatus.Unverified,
                $"Automatic verification is not available for {descriptor?.Name ?? provider} yet. The credentials are saved and will be checked on first use.");

        string Get(IReadOnlyDictionary<string, string> d, string k) => d.TryGetValue(k, out var v) ? v : string.Empty;
        string Base(string key, string fallback) => (configuration[$"Integrations:{key}:BaseUrl"] is { Length: > 0 } b ? b : fallback).TrimEnd('/');

        using var request = provider switch
        {
            "dataforseo" => Basic(HttpMethod.Get, $"{Base("DataForSeo", "https://api.dataforseo.com")}/v3/appendix/user_data", Get(secrets, "login"), Get(secrets, "password")),
            "sendgrid" => Bearer($"{Base("SendGrid", "https://api.sendgrid.com")}/v3/scopes", Get(secrets, "apiKey")),
            "mailgun" => Basic(HttpMethod.Get,
                $"{Base("Mailgun", Get(settings, "region") == "eu" ? "https://api.eu.mailgun.net" : "https://api.mailgun.net")}/v3/domains/{Uri.EscapeDataString(Get(settings, "domain"))}",
                "api", Get(secrets, "apiKey")),
            "stripe" => Bearer($"{Base("Stripe", "https://api.stripe.com")}/v1/balance", Get(secrets, "secretKey")),
            "twilio" => Basic(HttpMethod.Get, $"{Base("Twilio", "https://api.twilio.com")}/2010-04-01/Accounts/{Uri.EscapeDataString(Get(settings, "accountSid"))}.json",
                Get(settings, "accountSid"), Get(secrets, "authToken")),
            "google-search-console" => Bearer($"{Base("SearchConsole", "https://www.googleapis.com")}/webmasters/v3/sites", Get(secrets, "accessToken")),
            _ => throw new InvalidOperationException(),
        };

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            using var response = await httpFactory.CreateClient(HttpClientName).SendAsync(request, timeout.Token);
            var status = (int)response.StatusCode;
            if (status is 401 or 403)
                return new VerificationResult(IntegrationStatus.Error, $"{descriptor.Name} rejected the credentials ({status}). Check them and try again.");
            if (!response.IsSuccessStatusCode)
                return new VerificationResult(IntegrationStatus.Error, $"{descriptor.Name} answered {status}; the connection could not be verified.");
            if (provider == "dataforseo")
            {
                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
                if (doc.RootElement.TryGetProperty("status_code", out var code) && code.GetInt32() != 20000)
                    return new VerificationResult(IntegrationStatus.Error, $"DataForSEO error {code.GetInt32()}.");
            }
            return new VerificationResult(IntegrationStatus.Connected, $"Verified with {descriptor.Name}.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(ex, "Verification of {Provider} failed", provider);
            return new VerificationResult(IntegrationStatus.Error, $"{descriptor.Name} could not be reached: {ex.Message}");
        }
    }

    private static HttpRequestMessage Basic(HttpMethod method, string url, string user, string password)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{password}")));
        return request;
    }

    private static HttpRequestMessage Bearer(string url, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }
}

/// <summary>
/// Daily: warns integration managers about tokens expiring within 14 days (once per expiry date; idempotent via the audit
/// log) and marks already-expired connections as Error.
/// </summary>
public sealed class IntegrationExpiryJob(
    AppDbContext db, INotificationService notifications, IAuditLogger audit, IPermissionDirectory directory, TimeProvider clock) : IJob
{
    public const string WarningAction = "integration.expiry_warning";
    public static readonly TimeSpan WarnWithin = TimeSpan.FromDays(14);
    public string Name => "integrations.expiry";

    public async Task<string> ExecuteAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var expired = await db.Set<IntegrationConnection>()
            .Where(c => c.ExpiresAt != null && c.ExpiresAt <= now && (c.Status == IntegrationStatus.Connected || c.Status == IntegrationStatus.Unverified))
            .ToListAsync(ct);
        foreach (var c in expired)
        {
            c.Status = IntegrationStatus.Error;
            c.StatusMessage = $"The access token expired on {c.ExpiresAt:yyyy-MM-dd}. Reconnect to restore the integration.";
            audit.RecordSystem("integration.expired", nameof(IntegrationConnection), c.Id, new { c.Provider, c.ClientAccountId, c.ExpiresAt });
        }
        await db.SaveChangesAsync(ct);

        var horizon = now + WarnWithin;
        var expiring = await db.Set<IntegrationConnection>().AsNoTracking()
            .Where(c => c.ExpiresAt != null && c.ExpiresAt > now && c.ExpiresAt <= horizon && c.Status != IntegrationStatus.Disconnected)
            .ToListAsync(ct);
        // Built-in or custom-role holders of integrations.manage (custom roles cannot mix client.portal with staff permissions).
        var managers = await (await directory.UsersWithPermissionAsync(Permissions.IntegrationsManage, ct))
            .Where(u => u.Status == UserStatus.Active).Select(u => u.Id).ToListAsync(ct);
        var warned = 0;
        foreach (var c in expiring)
        {
            var reason = $"expires:{c.ExpiresAt:yyyy-MM-ddTHH:mm:ssZ}";
            var id = c.Id.ToString();
            if (await db.Set<AuditLog>().AnyAsync(a => a.EntityType == nameof(IntegrationConnection) && a.EntityId == id && a.Action == WarningAction && a.Reason == reason, ct)) continue;
            var name = ProviderRegistry.Find(c.Provider)?.Name ?? c.Provider;
            foreach (var userId in managers)
                await notifications.StageAsync(new NotificationRequest(userId, "integrations.expiring",
                    $"{name} connection expires soon",
                    $"“{c.DisplayName}” ({name}) expires on {c.ExpiresAt:d MMM yyyy}. Reconnect it before then to avoid interruptions.",
                    "/agency/integrations", new[] { NotificationChannel.Email }), ct);
            audit.RecordSystem(WarningAction, nameof(IntegrationConnection), c.Id, new { c.Provider, c.ExpiresAt, Recipients = managers.Count }, reason);
            await db.SaveChangesAsync(ct);
            warned++;
        }
        return $"{expired.Count} expired, {warned} warning(s) sent";
    }
}
