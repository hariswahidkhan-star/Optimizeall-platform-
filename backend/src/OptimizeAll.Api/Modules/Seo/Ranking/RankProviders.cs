using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Domain.Integrations;
using OptimizeAll.Domain.Seo;

namespace OptimizeAll.Api.Modules.Seo.Ranking;

public enum ProviderOutcome
{
    Ok,
    NotConfigured,
    Error,
}

public sealed record SerpEntry(string Domain, int Position, string Url);

public sealed record SerpCheck(string Keyword, int? Position, string? Url, IReadOnlyList<string> Features, IReadOnlyList<SerpEntry> Organic);

public sealed record RankCheckRequest(
    Guid ClientAccountId, string SiteDomain, string CountryCode, string LanguageCode, IReadOnlyList<string> Keywords);

public sealed record RankCheckResult(ProviderOutcome Outcome, string? Message, IReadOnlyList<SerpCheck> Results)
{
    public static RankCheckResult NotConfigured(string message) => new(ProviderOutcome.NotConfigured, message, Array.Empty<SerpCheck>());
}

/// <summary>
/// Source of daily SERP positions. The default implementation (<see cref="DataForSeoRankProvider"/>) reports
/// NotConfigured unless DataForSEO credentials are saved; manual entry and CSV import always work.
/// </summary>
public interface IRankTrackingProvider
{
    string Name { get; }
    Task<RankCheckResult> CheckAsync(RankCheckRequest request, CancellationToken ct);
}

/// <summary>
/// DataForSEO SERP API (Google organic, live/advanced; one task per keyword, depth 100). Credentials come from the vault
/// (provider "dataforseo": secrets <c>login</c> + <c>password</c>; client connection wins over the agency one).
/// Base URL: <c>Seo:DataForSeo:BaseUrl</c> (default https://api.dataforseo.com).
/// </summary>
public sealed class DataForSeoRankProvider(HttpClient http, ICredentialVault vault, IConfiguration configuration, ILogger<DataForSeoRankProvider> logger)
    : IRankTrackingProvider
{
    public const string Provider = "dataforseo";

    /// <summary>DataForSEO/Google Ads location codes of common markets (country → code).</summary>
    public static readonly IReadOnlyDictionary<string, int> LocationCodes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["US"] = 2840, ["GB"] = 2826, ["AE"] = 2784, ["PK"] = 2586, ["CA"] = 2124, ["AU"] = 2036, ["IN"] = 2356, ["SA"] = 2682,
        ["DE"] = 2276, ["FR"] = 2250, ["ES"] = 2724, ["IT"] = 2380, ["NL"] = 2528, ["IE"] = 2372, ["NZ"] = 2554, ["SG"] = 2702,
        ["QA"] = 2634, ["KW"] = 2414, ["BH"] = 2048, ["OM"] = 2512, ["EG"] = 2818, ["TR"] = 2792, ["ZA"] = 2710, ["NG"] = 2566,
        ["BR"] = 2076, ["MX"] = 2484, ["JP"] = 2392,
    };

    public string Name => "DataForSEO";

    private string BaseUrl => (configuration["Seo:DataForSeo:BaseUrl"] is { Length: > 0 } b ? b : "https://api.dataforseo.com").TrimEnd('/');

    public async Task<RankCheckResult> CheckAsync(RankCheckRequest request, CancellationToken ct)
    {
        var credentials = await vault.GetAsync(Provider, request.ClientAccountId, ct);
        if (credentials is null || !credentials.Secrets.TryGetValue("login", out var login) || !credentials.Secrets.TryGetValue("password", out var password) ||
            string.IsNullOrWhiteSpace(login) || string.IsNullOrWhiteSpace(password))
            return RankCheckResult.NotConfigured("DataForSEO is not configured. Add DataForSEO credentials under Integrations, or enter ranks manually / import a CSV.");

        var location = LocationCodes.TryGetValue(request.CountryCode, out var code) ? code : 2840;
        var siteHost = RankMath.BareHost(request.SiteDomain);
        var results = new List<SerpCheck>();
        foreach (var keyword in request.Keywords)
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/v3/serp/google/organic/live/advanced");
            message.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{login}:{password}")));
            message.Content = new StringContent(JsonSerializer.Serialize(new[]
            {
                new { keyword, location_code = location, language_code = request.LanguageCode, depth = 100 },
            }), Encoding.UTF8, "application/json");

            HttpResponseMessage response;
            try
            {
                response = await http.SendAsync(message, ct);
            }
            catch (HttpRequestException ex)
            {
                logger.LogWarning(ex, "DataForSEO request failed");
                return new RankCheckResult(ProviderOutcome.Error, "DataForSEO could not be reached: " + ex.Message, results);
            }
            using (response)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                if ((int)response.StatusCode is 401 or 403)
                {
                    await vault.MarkStatusAsync(credentials.ConnectionId, IntegrationStatus.Error, "DataForSEO rejected the credentials.", ct);
                    return new RankCheckResult(ProviderOutcome.Error, "DataForSEO rejected the credentials (401).", results);
                }
                if (!response.IsSuccessStatusCode)
                    return new RankCheckResult(ProviderOutcome.Error, $"DataForSEO answered {(int)response.StatusCode}.", results);
                var parsed = Parse(body, keyword, siteHost, out var error);
                if (parsed is null) return new RankCheckResult(ProviderOutcome.Error, error, results);
                results.Add(parsed);
            }
        }
        return new RankCheckResult(ProviderOutcome.Ok, null, results);
    }

    /// <summary>Parses a live/advanced response for one keyword (null + error when the task failed).</summary>
    public static SerpCheck? Parse(string json, string keyword, string siteHost, out string? error)
    {
        error = null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("status_code", out var sc) && sc.GetInt32() != 20000)
            {
                error = $"DataForSEO error {sc.GetInt32()}: {(root.TryGetProperty("status_message", out var sm) ? sm.GetString() : "unknown")}";
                return null;
            }
            var task = root.GetProperty("tasks").EnumerateArray().FirstOrDefault();
            if (task.ValueKind != JsonValueKind.Object) { error = "DataForSEO returned no task."; return null; }
            if (task.TryGetProperty("status_code", out var ts) && ts.GetInt32() != 20000)
            {
                error = $"DataForSEO task error {ts.GetInt32()}: {(task.TryGetProperty("status_message", out var tm) ? tm.GetString() : "unknown")}";
                return null;
            }
            var organic = new List<SerpEntry>();
            var features = new HashSet<string>(StringComparer.Ordinal);
            if (task.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.Array)
            {
                foreach (var r in result.EnumerateArray())
                {
                    if (!r.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array) continue;
                    foreach (var item in items.EnumerateArray())
                    {
                        var type = item.TryGetProperty("type", out var t) ? t.GetString() ?? string.Empty : string.Empty;
                        if (type != "organic")
                        {
                            if (type.Length > 0) features.Add(type);
                            continue;
                        }
                        var domain = item.TryGetProperty("domain", out var d) ? d.GetString() ?? string.Empty : string.Empty;
                        var url = item.TryGetProperty("url", out var u) ? u.GetString() ?? string.Empty : string.Empty;
                        var position = item.TryGetProperty("rank_group", out var rg) && rg.ValueKind == JsonValueKind.Number ? rg.GetInt32() : 0;
                        if (position > 0) organic.Add(new SerpEntry(RankMath.BareHost(domain.Length > 0 ? domain : url), position, url));
                    }
                }
            }
            var own = organic.Where(o => o.Domain == siteHost || o.Domain.EndsWith("." + siteHost, StringComparison.Ordinal)).OrderBy(o => o.Position).FirstOrDefault();
            return new SerpCheck(keyword, own?.Position, own?.Url, features.OrderBy(f => f, StringComparer.Ordinal).ToList(), organic);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            error = "DataForSEO returned an unexpected response.";
            return null;
        }
    }
}

public sealed record SearchConsoleRow(DateOnly Date, string Query, string Page, int Clicks, int Impressions, double Ctr, double Position);

public sealed record SearchConsoleResult(ProviderOutcome Outcome, string? Message, IReadOnlyList<SearchConsoleRow> Rows);

/// <summary>Google Search Console performance import. NotConfigured unless an OAuth access token is saved in the vault.</summary>
public interface ISearchConsoleClient
{
    Task<SearchConsoleResult> FetchAsync(Guid clientAccountId, string siteUrl, DateOnly from, DateOnly to, CancellationToken ct);
}

/// <summary>
/// Search Console API (searchanalytics.query, dimensions date/query/page). Provider "google-search-console": secret
/// <c>accessToken</c> (OAuth 2.0, scope webmasters.readonly; refreshed outside the platform), optional setting
/// <c>propertyUrl</c> (e.g. "sc-domain:example.com") overriding the site URL.
/// </summary>
public sealed class GoogleSearchConsoleClient(HttpClient http, ICredentialVault vault, IConfiguration configuration) : ISearchConsoleClient
{
    public const string Provider = "google-search-console";

    public async Task<SearchConsoleResult> FetchAsync(Guid clientAccountId, string siteUrl, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var credentials = await vault.GetAsync(Provider, clientAccountId, ct);
        if (credentials is null || !credentials.Secrets.TryGetValue("accessToken", out var token) || string.IsNullOrWhiteSpace(token))
            return new SearchConsoleResult(ProviderOutcome.NotConfigured,
                "Google Search Console is not connected. Connect it under Integrations, or import a Performance CSV export.", Array.Empty<SearchConsoleRow>());

        var property = credentials.Settings.TryGetValue("propertyUrl", out var p) && !string.IsNullOrWhiteSpace(p) ? p : siteUrl;
        var baseUrl = (configuration["Seo:SearchConsole:BaseUrl"] is { Length: > 0 } b ? b : "https://www.googleapis.com").TrimEnd('/');
        using var message = new HttpRequestMessage(HttpMethod.Post,
            $"{baseUrl}/webmasters/v3/sites/{Uri.EscapeDataString(property)}/searchAnalytics/query");
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        message.Content = new StringContent(JsonSerializer.Serialize(new
        {
            startDate = from.ToString("yyyy-MM-dd"),
            endDate = to.ToString("yyyy-MM-dd"),
            dimensions = new[] { "date", "query", "page" },
            rowLimit = 5000,
        }), Encoding.UTF8, "application/json");
        try
        {
            using var response = await http.SendAsync(message, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if ((int)response.StatusCode is 401 or 403)
            {
                await vault.MarkStatusAsync(credentials.ConnectionId, IntegrationStatus.Error, "Search Console rejected the access token (expired or revoked).", ct);
                return new SearchConsoleResult(ProviderOutcome.Error, "Search Console rejected the access token; reconnect the integration.", Array.Empty<SearchConsoleRow>());
            }
            if (!response.IsSuccessStatusCode)
                return new SearchConsoleResult(ProviderOutcome.Error, $"Search Console answered {(int)response.StatusCode}.", Array.Empty<SearchConsoleRow>());
            using var doc = JsonDocument.Parse(body);
            var rows = new List<SearchConsoleRow>();
            if (doc.RootElement.TryGetProperty("rows", out var list))
            {
                foreach (var row in list.EnumerateArray())
                {
                    var keys = row.GetProperty("keys").EnumerateArray().Select(k => k.GetString() ?? string.Empty).ToArray();
                    if (keys.Length < 3 || !DateOnly.TryParse(keys[0], System.Globalization.CultureInfo.InvariantCulture, out var date)) continue;
                    rows.Add(new SearchConsoleRow(date, keys[1], keys[2],
                        (int)row.GetProperty("clicks").GetDouble(), (int)row.GetProperty("impressions").GetDouble(),
                        row.GetProperty("ctr").GetDouble(), row.GetProperty("position").GetDouble()));
                }
            }
            return new SearchConsoleResult(ProviderOutcome.Ok, null, rows);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return new SearchConsoleResult(ProviderOutcome.Error, "Search Console could not be read: " + ex.Message, Array.Empty<SearchConsoleRow>());
        }
    }
}
