using System.Net.Http.Json;
using System.Text.Json;
using OptimizeAll.Api.Modules.Website.Leads;
using OptimizeAll.Domain.Identity;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.Website;

internal static class WebsiteTestKit
{
    public static async Task<HttpClient> AdminAsync(this ApiFactory api) => (await api.CreateClientAsync(Role.Admin)).Client;

    public static HttpClient Anonymous(this ApiFactory api)
    {
        var client = api.CreateClient();
        client.DefaultRequestHeaders.Add("X-Requested-With", "tests");
        return client;
    }

    /// <summary>A form token that has "been open" long enough (the test clock is advanced past the minimum fill time).</summary>
    public static async Task<string> FormTokenAsync(this ApiFactory api, HttpClient client, bool wait = true)
    {
        var token = (await (await client.GetAsync("/api/v1/public/forms/token")).ReadJsonAsync()).GetProperty("token").GetString()!;
        if (wait) api.Clock.Advance(TimeSpan.FromSeconds(5));
        return token;
    }

    public static Dictionary<string, object?> Form(string token, Dictionary<string, object?> fields, string consentVersion = ConsentTexts.FormVersion)
    {
        var form = new Dictionary<string, object?>(fields)
        {
            ["formToken"] = token,
            ["consent"] = true,
            ["consentVersion"] = consentVersion,
            ["utm"] = new { source = "google", medium = "cpc", campaign = "autumn" },
            ["referrer"] = "https://www.google.com/",
            ["landingPath"] = "/services/seo",
        };
        return form;
    }

    public static Dictionary<string, object?> Contact(string email, string? name = "Ada Lovelace") => new()
    {
        ["name"] = name,
        ["email"] = email,
        ["company"] = "Analytical Engines Ltd",
        ["message"] = "We'd like help growing organic traffic next quarter.",
        ["serviceSlugs"] = new[] { "seo" },
    };

    public static async Task<JsonElement> PostJsonAsync(this HttpClient client, string path, object body, int expectedStatus)
    {
        var response = await client.PostAsJsonAsync(path, body);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True((int)response.StatusCode == expectedStatus, $"POST {path}: expected {expectedStatus}, got {(int)response.StatusCode}: {text}");
        return string.IsNullOrEmpty(text) ? default : JsonSerializer.Deserialize<JsonElement>(text);
    }

    public static async Task<JsonElement> PutJsonAsync(this HttpClient client, string path, object body, int expectedStatus = 200)
    {
        var response = await client.PutAsJsonAsync(path, body);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True((int)response.StatusCode == expectedStatus, $"PUT {path}: expected {expectedStatus}, got {(int)response.StatusCode}: {text}");
        return string.IsNullOrEmpty(text) ? default : JsonSerializer.Deserialize<JsonElement>(text);
    }

    public static async Task<JsonElement> GetJsonAsync(this HttpClient client, string path, int expectedStatus = 200)
    {
        var response = await client.GetAsync(path);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True((int)response.StatusCode == expectedStatus, $"GET {path}: expected {expectedStatus}, got {(int)response.StatusCode}: {text}");
        return string.IsNullOrEmpty(text) || expectedStatus >= 300 ? default : JsonSerializer.Deserialize<JsonElement>(text);
    }

    public static string Code(this JsonElement problem) => problem.GetProperty("code").GetString()!;

    /// <summary>A minimal valid PDF.</summary>
    public static byte[] Pdf() => System.Text.Encoding.ASCII.GetBytes(
        "%PDF-1.4\n1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj\n2 0 obj<</Type/Pages/Kids[]/Count 0>>endobj\ntrailer<</Root 1 0 R>>\n%%EOF\n");

    /// <summary>A PNG header (what a renamed image would look like).</summary>
    public static byte[] Png() => new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52, 0, 0, 1, 0, 0, 0, 1, 0, 8, 2, 0, 0, 0 };
}
