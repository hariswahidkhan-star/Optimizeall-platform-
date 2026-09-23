using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Jobs;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Domain.Ads;
using OptimizeAll.Domain.Integrations;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Ads;

/// <summary>Configuration section "Ads".</summary>
public sealed class AdsOptions
{
    public const string Section = "Ads";
    public string GoogleAdsApiBaseUrl { get; set; } = "https://googleads.googleapis.com";
    public string GoogleAdsApiVersion { get; set; } = "v21";
    public string GoogleOAuthTokenUrl { get; set; } = "https://oauth2.googleapis.com/token";
    public string MetaGraphApiBaseUrl { get; set; } = "https://graph.facebook.com/v20.0";

    /// <summary>Meta action types counted as conversions (first match wins per row).</summary>
    public string[] MetaConversionActionTypes { get; set; } = { "omni_purchase", "purchase", "offsite_conversion.fb_pixel_purchase", "lead" };
    public int BackfillDays { get; set; } = 90;
    public int RefreshDays { get; set; } = 3;
}

public enum SyncOutcome
{
    Synced,
    NotConfigured,
    AuthorizationError,
    Failed,
}

public sealed record AdsSyncResult(SyncOutcome Outcome, IReadOnlyList<AdMetricRow> Rows, string Message, Guid? ConnectionId = null);

/// <summary>Reporting adapter of one ad platform. Without credentials it reports NotConfigured and returns no rows.</summary>
public interface IAdsReportingProvider
{
    AdPlatform Platform { get; }
    Task<AdsSyncResult> FetchDailyAsync(AdAccount account, DateOnly from, DateOnly to, CancellationToken ct);
}

public sealed class NotConfiguredAdsProvider(AdPlatform platform) : IAdsReportingProvider
{
    public AdPlatform Platform => platform;

    public Task<AdsSyncResult> FetchDailyAsync(AdAccount account, DateOnly from, DateOnly to, CancellationToken ct) =>
        Task.FromResult(new AdsSyncResult(SyncOutcome.NotConfigured, Array.Empty<AdMetricRow>(),
            $"{platform} reporting sync is not configured in this installation. Import a CSV export instead."));
}

internal static class AdsHttp
{
    public static JsonElement Parse(string raw)
    {
        try
        {
            return JsonDocument.Parse(string.IsNullOrWhiteSpace(raw) ? "{}" : raw).RootElement.Clone();
        }
        catch (JsonException)
        {
            return JsonDocument.Parse("{}").RootElement.Clone();
        }
    }

    public static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v)
            ? v.ValueKind switch { JsonValueKind.String => v.GetString(), JsonValueKind.Number => v.GetRawText(), _ => null }
            : null;

    public static decimal Dec(JsonElement e, string name) =>
        decimal.TryParse(Str(e, name), NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : 0m;

    public static long Lng(JsonElement e, string name) =>
        long.TryParse(Str(e, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0L;

    public static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}

/// <summary>
/// Google Ads API reporting: exchanges the stored OAuth refresh token for an access token, then runs a GAQL query through
/// <c>POST /{version}/customers/{customerId}/googleAds:searchStream</c> (headers: developer-token, login-customer-id).
/// Credentials: vault provider "google-ads" (client-specific or agency-wide) with settings clientId, loginCustomerId (optional)
/// and secrets developerToken, clientSecret, refreshToken.
/// </summary>
public sealed class GoogleAdsReportingProvider(IHttpClientFactory factory, ICredentialVault vault, IOptions<AdsOptions> options) : IAdsReportingProvider
{
    public const string HttpClientName = "google-ads";
    public const string Provider = "google-ads";

    public AdPlatform Platform => AdPlatform.GoogleAds;

    public static string Gaql(DateOnly from, DateOnly to) =>
        "SELECT segments.date, campaign.id, campaign.name, campaign.status, customer.currency_code, metrics.cost_micros, " +
        "metrics.impressions, metrics.clicks, metrics.conversions, metrics.conversions_value, metrics.video_views " +
        $"FROM campaign WHERE segments.date BETWEEN '{from:yyyy-MM-dd}' AND '{to:yyyy-MM-dd}'";

    public async Task<AdsSyncResult> FetchDailyAsync(AdAccount account, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var creds = await vault.GetAsync(Provider, account.ClientAccountId, ct);
        if (creds is null || !creds.Secrets.TryGetValue("developerToken", out var devToken) || !creds.Secrets.TryGetValue("refreshToken", out var refresh)
            || !creds.Settings.TryGetValue("clientId", out var clientId) || !creds.Secrets.TryGetValue("clientSecret", out var clientSecret))
            return new AdsSyncResult(SyncOutcome.NotConfigured, Array.Empty<AdMetricRow>(),
                "Google Ads API credentials are not configured (developer token + OAuth client + refresh token). Import a CSV export instead.");

        var http = factory.CreateClient(HttpClientName);
        var o = options.Value;
        using var tokenResponse = await http.PostAsync(o.GoogleOAuthTokenUrl, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token", ["client_id"] = clientId, ["client_secret"] = clientSecret, ["refresh_token"] = refresh,
        }), ct);
        var tokenBody = AdsHttp.Parse(await tokenResponse.Content.ReadAsStringAsync(ct));
        var accessToken = AdsHttp.Str(tokenBody, "access_token");
        if (!tokenResponse.IsSuccessStatusCode || accessToken is null)
            return new AdsSyncResult(SyncOutcome.AuthorizationError, Array.Empty<AdMetricRow>(),
                $"Google OAuth refresh failed ({(int)tokenResponse.StatusCode}): {AdsHttp.Str(tokenBody, "error_description") ?? AdsHttp.Str(tokenBody, "error")}", creds.ConnectionId);

        var customer = new string(account.ExternalAccountId.Where(char.IsDigit).ToArray());
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"{o.GoogleAdsApiBaseUrl.TrimEnd('/')}/{o.GoogleAdsApiVersion}/customers/{customer}/googleAds:searchStream")
        {
            Content = JsonContent.Create(new { query = Gaql(from, to) }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Add("developer-token", devToken);
        if (creds.Settings.TryGetValue("loginCustomerId", out var login) && !string.IsNullOrWhiteSpace(login))
            request.Headers.Add("login-customer-id", new string(login.Where(char.IsDigit).ToArray()));
        using var response = await http.SendAsync(request, ct);
        var raw = await response.Content.ReadAsStringAsync(ct);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            return new AdsSyncResult(SyncOutcome.AuthorizationError, Array.Empty<AdMetricRow>(),
                $"Google Ads rejected the credentials ({(int)response.StatusCode}): {AdsHttp.Truncate(raw, 400)}", creds.ConnectionId);
        if (!response.IsSuccessStatusCode)
            return new AdsSyncResult(SyncOutcome.Failed, Array.Empty<AdMetricRow>(), $"Google Ads returned {(int)response.StatusCode}: {AdsHttp.Truncate(raw, 400)}");
        return new AdsSyncResult(SyncOutcome.Synced, ParseSearchStream(raw, account.Currency), "Synced from the Google Ads API.");
    }

    /// <summary>searchStream returns a JSON array of batches, each with "results" (camelCase fields; int64 as strings).</summary>
    public static IReadOnlyList<AdMetricRow> ParseSearchStream(string raw, string fallbackCurrency)
    {
        var rows = new List<AdMetricRow>();
        var doc = AdsHttp.Parse(raw);
        var batches = doc.ValueKind == JsonValueKind.Array ? doc.EnumerateArray().ToList() : new List<JsonElement> { doc };
        foreach (var batch in batches)
        {
            if (!batch.TryGetProperty("results", out var results)) continue;
            foreach (var r in results.EnumerateArray())
            {
                var campaign = r.GetProperty("campaign");
                var metrics = r.TryGetProperty("metrics", out var m) ? m : default;
                var segments = r.GetProperty("segments");
                var currency = r.TryGetProperty("customer", out var c) ? AdsHttp.Str(c, "currencyCode") ?? fallbackCurrency : fallbackCurrency;
                if (!DateOnly.TryParseExact(AdsHttp.Str(segments, "date"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) continue;
                var id = AdsHttp.Str(campaign, "id") ?? string.Empty;
                var status = AdsHttp.Str(campaign, "status") switch
                {
                    "ENABLED" => AdEntityStatus.Active,
                    "PAUSED" => AdEntityStatus.Paused,
                    "REMOVED" => AdEntityStatus.Removed,
                    _ => (AdEntityStatus?)null,
                };
                var hasMetrics = metrics.ValueKind == JsonValueKind.Object;
                rows.Add(new AdMetricRow(date, AdLevel.Campaign, id, AdsHttp.Str(campaign, "name") ?? id, null, null, null, null, currency,
                    hasMetrics ? Math.Round(AdsHttp.Dec(metrics, "costMicros") / 1_000_000m, 4) : 0m,
                    hasMetrics ? AdsHttp.Lng(metrics, "impressions") : 0, hasMetrics ? AdsHttp.Lng(metrics, "clicks") : 0,
                    hasMetrics ? AdsHttp.Dec(metrics, "conversions") : 0m, hasMetrics ? AdsHttp.Dec(metrics, "conversionsValue") : 0m,
                    null, hasMetrics ? AdsHttp.Lng(metrics, "videoViews") : null, status));
            }
        }
        return rows;
    }
}

/// <summary>
/// Meta Marketing API insights: <c>GET /act_{id}/insights?level=campaign&amp;time_increment=1</c> with spend, impressions,
/// clicks, reach, actions and action_values (paged via <c>paging.next</c>). Credentials: vault provider "meta-ads" with
/// secret accessToken (a system-user token with ads_read).
/// </summary>
public sealed class MetaAdsReportingProvider(IHttpClientFactory factory, ICredentialVault vault, IOptions<AdsOptions> options) : IAdsReportingProvider
{
    public const string HttpClientName = "meta-ads";
    public const string Provider = "meta-ads";

    public AdPlatform Platform => AdPlatform.MetaAds;

    public async Task<AdsSyncResult> FetchDailyAsync(AdAccount account, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var creds = await vault.GetAsync(Provider, account.ClientAccountId, ct);
        if (creds is null || !creds.Secrets.TryGetValue("accessToken", out var token) || string.IsNullOrWhiteSpace(token))
            return new AdsSyncResult(SyncOutcome.NotConfigured, Array.Empty<AdMetricRow>(),
                "Meta Marketing API credentials are not configured (system-user access token with ads_read). Import a CSV export instead.");

        var o = options.Value;
        var act = account.ExternalAccountId.StartsWith("act_", StringComparison.Ordinal) ? account.ExternalAccountId : "act_" + account.ExternalAccountId;
        var timeRange = JsonSerializer.Serialize(new { since = from.ToString("yyyy-MM-dd"), until = to.ToString("yyyy-MM-dd") });
        string? url = $"{o.MetaGraphApiBaseUrl.TrimEnd('/')}/{Uri.EscapeDataString(act)}/insights?level=campaign&time_increment=1" +
                      $"&fields=campaign_id,campaign_name,account_currency,spend,impressions,clicks,reach,actions,action_values,video_thruplay_watched_actions" +
                      $"&time_range={Uri.EscapeDataString(timeRange)}&limit=500";
        var rows = new List<AdMetricRow>();
        var http = factory.CreateClient(HttpClientName);
        for (var page = 0; url is not null && page < 50; page++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await http.SendAsync(request, ct);
            var body = AdsHttp.Parse(await response.Content.ReadAsStringAsync(ct));
            if (body.TryGetProperty("error", out var error))
            {
                var code = error.TryGetProperty("code", out var c) && c.TryGetInt32(out var n) ? n : 0;
                var message = AdsHttp.Str(error, "message") ?? error.ToString();
                return code == 190 || response.StatusCode == HttpStatusCode.Unauthorized
                    ? new AdsSyncResult(SyncOutcome.AuthorizationError, Array.Empty<AdMetricRow>(), $"Meta rejected the access token: {message}", creds.ConnectionId)
                    : new AdsSyncResult(SyncOutcome.Failed, Array.Empty<AdMetricRow>(), $"Meta insights failed ({(int)response.StatusCode}): {message}");
            }
            rows.AddRange(ParseInsights(body, account.Currency, o.MetaConversionActionTypes));
            url = body.TryGetProperty("paging", out var paging) ? AdsHttp.Str(paging, "next") : null;
        }
        return new AdsSyncResult(SyncOutcome.Synced, rows, "Synced from the Meta Marketing API.");
    }

    public static IReadOnlyList<AdMetricRow> ParseInsights(JsonElement body, string fallbackCurrency, IReadOnlyList<string> conversionTypes)
    {
        var rows = new List<AdMetricRow>();
        if (!body.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) return rows;
        foreach (var d in data.EnumerateArray())
        {
            if (!DateOnly.TryParseExact(AdsHttp.Str(d, "date_start"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) continue;
            var id = AdsHttp.Str(d, "campaign_id") ?? string.Empty;
            rows.Add(new AdMetricRow(date, AdLevel.Campaign, id, AdsHttp.Str(d, "campaign_name") ?? id, null, null, null, null,
                AdsHttp.Str(d, "account_currency") ?? fallbackCurrency, AdsHttp.Dec(d, "spend"), AdsHttp.Lng(d, "impressions"), AdsHttp.Lng(d, "clicks"),
                ActionValue(d, "actions", conversionTypes), ActionValue(d, "action_values", conversionTypes), AdsHttp.Lng(d, "reach"),
                (long)ActionValue(d, "video_thruplay_watched_actions", new[] { "video_view" })));
        }
        return rows;
    }

    private static decimal ActionValue(JsonElement row, string field, IReadOnlyList<string> types)
    {
        if (!row.TryGetProperty(field, out var list) || list.ValueKind != JsonValueKind.Array) return 0m;
        foreach (var type in types)
            foreach (var a in list.EnumerateArray())
                if (AdsHttp.Str(a, "action_type") == type) return AdsHttp.Dec(a, "value");
        return 0m;
    }
}

public sealed class AdsProviderRegistry(IEnumerable<IAdsReportingProvider> providers)
{
    public IAdsReportingProvider For(AdPlatform platform) =>
        providers.LastOrDefault(p => p.Platform == platform) ?? new NotConfiguredAdsProvider(platform);
}

public sealed record AccountSyncDto(Guid AdAccountId, SyncOutcome Outcome, int Inserted, int Updated, string Message);

/// <summary>Runs a reporting sync for one account and records the outcome (metrics are only written from a real provider response).</summary>
public sealed class AdsSyncService(AppDbContext db, AdsProviderRegistry providers, AdMetricWriter writer, ICredentialVault vault,
    IAuditLogger audit, IOptions<AdsOptions> options, TimeProvider clock)
{
    public async Task<AccountSyncDto> SyncAsync(Guid accountId, CancellationToken ct)
    {
        var account = await db.Set<AdAccount>().FirstAsync(a => a.Id == accountId, ct);
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var from = today.AddDays(-(account.LastSyncedAt is null ? options.Value.BackfillDays : options.Value.RefreshDays));
        var result = await providers.For(account.Platform).FetchDailyAsync(account, from, today.AddDays(-1), ct);
        var now = clock.GetUtcNow().UtcDateTime;
        UpsertResult written = new(0, 0);
        switch (result.Outcome)
        {
            case SyncOutcome.Synced:
                var wrongCurrency = result.Rows.FirstOrDefault(r => !string.Equals(r.Currency, account.Currency, StringComparison.OrdinalIgnoreCase));
                if (wrongCurrency is not null)
                {
                    account.Status = AdAccountStatus.Error;
                    account.LastSyncMessage = $"The platform reports currency {wrongCurrency.Currency} but the account is set to {account.Currency}. Fix the account currency.";
                    break;
                }
                written = await writer.UpsertAsync(account, result.Rows, AdMetricSource.Api, null, AdEntitySource.Synced, ct);
                account.Status = AdAccountStatus.Connected;
                account.StatusMessage = null;
                account.LastSyncedAt = now;
                account.LastSyncMessage = $"{result.Message} {written.Inserted} new and {written.Updated} updated rows.";
                break;
            case SyncOutcome.AuthorizationError:
                account.Status = AdAccountStatus.Error;
                account.StatusMessage = Truncate(result.Message, 1000);
                account.LastSyncMessage = account.StatusMessage;
                if (result.ConnectionId is { } connectionId) await vault.MarkStatusAsync(connectionId, IntegrationStatus.Error, result.Message, ct);
                break;
            default:
                account.LastSyncMessage = Truncate(result.Message, 1000);
                break;
        }
        audit.RecordSystem("ads.account.synced", nameof(AdAccount), account.Id, new { result.Outcome, written.Inserted, written.Updated });
        await db.SaveChangesAsync(ct);
        return new AccountSyncDto(account.Id, result.Outcome, written.Inserted, written.Updated, account.LastSyncMessage ?? result.Message);
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}

/// <summary>Daily reporting sync of every active ad account (NotConfigured accounts are skipped with a message).</summary>
public sealed class AdsSyncJob(AppDbContext db, AdsSyncService sync) : IJob
{
    public string Name => nameof(AdsSyncJob);

    public async Task<string> ExecuteAsync(CancellationToken ct)
    {
        var ids = await db.Set<AdAccount>().AsNoTracking().Where(a => a.IsActive && a.Status != AdAccountStatus.Disconnected).Select(a => a.Id).ToListAsync(ct);
        var counts = new Dictionary<SyncOutcome, int>();
        foreach (var id in ids)
        {
            var r = await sync.SyncAsync(id, ct);
            counts[r.Outcome] = counts.GetValueOrDefault(r.Outcome) + 1;
            db.ChangeTracker.Clear();
        }
        return string.Join(", ", counts.Select(kv => $"{kv.Key}: {kv.Value}")) is { Length: > 0 } s ? s : "no accounts";
    }
}
