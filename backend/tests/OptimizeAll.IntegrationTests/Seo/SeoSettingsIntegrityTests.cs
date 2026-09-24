using System.Net.Http.Json;
using OptimizeAll.Api.Modules.Seo.Audit;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Seo;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.Seo;

/// <summary>Hidden directories stay out of new tracking, and triage/rule inputs only accept defined values.</summary>
public sealed class SeoSettingsIntegrityTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task A_hidden_directory_keeps_existing_citations_but_accepts_no_new_ones()
    {
        var client = await api.CreateClientAccountAsync();
        var (_, seo) = await api.CreateClientAsync(Role.SeoSpecialist);
        var (_, admin) = await api.CreateClientAsync(Role.Admin);
        async Task<Guid> SiteAsync(string domain) => (await (await seo.PostAsJsonAsync("/api/v1/agency/seo/sites", new
        {
            clientAccountId = client.Id, name = domain, domain, targetCountry = "US", targetLanguage = "en",
        })).ReadJsonAsync()).GetProperty("id").GetGuid();
        var tracked = await SiteAsync("tracked.example.com");
        var fresh = await SiteAsync("fresh.example.com");

        var body = new { name = "Hidden Later Guide", url = "https://hidden-guide.example/", category = "Local", countries = Array.Empty<string>(), sortOrder = 1 };
        var created = await (await admin.PostAsJsonAsync("/api/v1/agency/seo/citation-sources", body)).ReadJsonAsync();
        var sourceId = created.GetProperty("id").GetGuid();
        (await seo.PutAsJsonAsync($"/api/v1/agency/seo/sites/{tracked}/local/citations/{sourceId}", new { status = "Submitted" })).EnsureSuccessStatusCode();

        (await admin.PutAsJsonAsync($"/api/v1/agency/seo/citation-sources/{sourceId}", new
        {
            body.name, body.url, body.category, body.countries, body.sortOrder, isActive = false, concurrencyStamp = created.GetProperty("concurrencyStamp").GetGuid(),
        })).EnsureSuccessStatusCode();

        // The site that already tracks it can keep updating it; another site can't start tracking it.
        (await seo.PutAsJsonAsync($"/api/v1/agency/seo/sites/{tracked}/local/citations/{sourceId}", new { status = "Live" })).EnsureSuccessStatusCode();
        await (await seo.PutAsJsonAsync($"/api/v1/agency/seo/sites/{fresh}/local/citations/{sourceId}", new { status = "Submitted" }))
            .ShouldFailAsync(409, "seo.directory_hidden");
        var local = await (await seo.GetAsync($"/api/v1/agency/seo/sites/{fresh}/local")).ReadJsonAsync();
        Assert.DoesNotContain(local.GetProperty("citations").EnumerateArray(), c => c.GetProperty("sourceId").GetGuid() == sourceId);
    }

    [Fact]
    public async Task Undefined_numeric_enum_values_are_rejected()
    {
        var client = await api.CreateClientAccountAsync();
        var (_, seo) = await api.CreateClientAsync(Role.SeoSpecialist);
        var (_, admin) = await api.CreateClientAsync(Role.Admin);
        var site = await (await seo.PostAsJsonAsync("/api/v1/agency/seo/sites", new
        {
            clientAccountId = client.Id, name = "Enum", domain = "enum.example.com", targetCountry = "US", targetLanguage = "en",
        })).ReadJsonAsync();
        var audit = new SeoAudit
        {
            SiteId = site.GetProperty("id").GetGuid(), ClientAccountId = client.Id, Status = SeoAuditStatus.Completed, QueuedAt = DateTime.UtcNow,
            FinishedAt = DateTime.UtcNow, HealthScore = 90,
        };
        await api.WithDbAsync(async db =>
        {
            db.Add(audit);
            db.Add(new SeoAuditIssue { AuditId = audit.Id, RuleKey = SeoAuditRules.TitleMissing, Severity = SeoSeverity.Error, AffectedCount = 1, AffectedUrls = new() { "https://enum.example.com/" } });
            await db.SaveChangesAsync();
            return true;
        });

        await (await seo.PostAsJsonAsync($"/api/v1/agency/seo/audits/{audit.Id}/issues/{SeoAuditRules.TitleMissing}/status", new { status = 99, note = "x" }))
            .ShouldFailAsync(400);
        var rule = (await (await seo.GetAsync("/api/v1/agency/seo/rules")).ReadJsonAsync()).EnumerateArray().First();
        await (await admin.PutAsJsonAsync($"/api/v1/agency/seo/rules/{rule.GetProperty("key").GetString()}", new
        {
            title = "T", severity = 42, whyItMatters = "W", howToFix = "H", isEnabled = true,
        })).ShouldFailAsync(400);
    }
}
