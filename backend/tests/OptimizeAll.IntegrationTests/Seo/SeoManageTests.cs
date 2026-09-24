using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Modules.Seo.Audit;
using OptimizeAll.Domain.Audit;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Seo;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.Seo;

/// <summary>Edit/delete/"mark done" coverage of the SEO toolkit: rule settings, issue triage, audits, backlinks, briefs and directories.</summary>
public sealed class SeoManageTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private async Task<(HttpClient Seo, Guid SiteId, Guid ClientId)> SiteAsync(string domain = "shop.example.com")
    {
        var client = await api.CreateClientAccountAsync();
        var (_, seo) = await api.CreateClientAsync(Role.SeoSpecialist);
        var site = await (await seo.PostAsJsonAsync("/api/v1/agency/seo/sites", new
        {
            clientAccountId = client.Id, name = "Main", domain, targetCountry = "US", targetLanguage = "en",
        })).ReadJsonAsync();
        return (seo, site.GetProperty("id").GetGuid(), client.Id);
    }

    private async Task<Guid> CompletedAuditAsync(Guid siteId, Guid clientId, params (string Rule, SeoSeverity Severity)[] issues)
    {
        var audit = new SeoAudit
        {
            SiteId = siteId, ClientAccountId = clientId, Status = SeoAuditStatus.Completed, QueuedAt = DateTime.UtcNow, FinishedAt = DateTime.UtcNow, HealthScore = 80,
        };
        await api.WithDbAsync(async db =>
        {
            db.Add(audit);
            foreach (var (rule, severity) in issues)
                db.Add(new SeoAuditIssue { AuditId = audit.Id, RuleKey = rule, Severity = severity, AffectedCount = 1, AffectedUrls = new() { "https://shop.example.com/" } });
            await db.SaveChangesAsync();
        });
        return audit.Id;
    }

    [Fact]
    public async Task Issues_can_be_marked_fixed_ignored_with_a_note_and_reopened()
    {
        var (seo, siteId, clientId) = await SiteAsync();
        var auditId = await CompletedAuditAsync(siteId, clientId, (SeoAuditRules.TitleMissing, SeoSeverity.Error));
        var path = $"/api/v1/agency/seo/audits/{auditId}/issues/{SeoAuditRules.TitleMissing}/status";

        await (await seo.PostAsJsonAsync(path, new { status = "Ignored" })).ShouldFailAsync(400, "seo.issue_note_required");
        var ignored = await (await seo.PostAsJsonAsync(path, new { status = "Ignored", note = "Intentional on the landing page" })).ReadJsonAsync();
        Assert.Equal("Ignored", ignored.GetProperty("status").GetString());
        Assert.Equal("Intentional on the landing page", ignored.GetProperty("statusNote").GetString());

        var fixedIssue = await (await seo.PostAsJsonAsync(path, new { status = "Fixed" })).ReadJsonAsync();
        Assert.Equal("Fixed", fixedIssue.GetProperty("status").GetString());
        var reopened = await (await seo.PostAsJsonAsync(path, new { status = "Open", note = "ignored when reopening" })).ReadJsonAsync();
        Assert.Equal("Open", reopened.GetProperty("status").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, reopened.GetProperty("statusNote").ValueKind);

        var detail = await (await seo.GetAsync($"/api/v1/agency/seo/audits/{auditId}")).ReadJsonAsync();
        Assert.Equal("Open", detail.GetProperty("issues")[0].GetProperty("status").GetString());
        Assert.True(await api.WithDbAsync(db => db.Set<AuditLog>().AnyAsync(a => a.Action == "seo.issue_status_changed" && a.EntityId == auditId.ToString())));

        await (await seo.PostAsJsonAsync($"/api/v1/agency/seo/audits/{auditId}/issues/unknown.rule/status", new { status = "Fixed" })).ShouldFailAsync(404);
        await (await seo.PostAsJsonAsync($"/api/v1/agency/seo/audits/{Guid.NewGuid()}/issues/{SeoAuditRules.TitleMissing}/status", new { status = "Fixed" }))
            .ShouldFailAsync(404);
        var (_, designer) = await api.CreateClientAsync(Role.Designer);
        await (await designer.PostAsJsonAsync(path, new { status = "Fixed" })).ShouldFailAsync(403);
    }

    [Fact]
    public async Task Finished_audits_can_be_deleted_but_queued_ones_cannot()
    {
        var (seo, siteId, clientId) = await SiteAsync("delete.example.com");
        var done = await CompletedAuditAsync(siteId, clientId, (SeoAuditRules.H1Missing, SeoSeverity.Warning));
        var queued = (await (await seo.PostAsync($"/api/v1/agency/seo/sites/{siteId}/audits", null)).ReadJsonAsync()).GetProperty("id").GetGuid();

        await (await seo.DeleteAsync($"/api/v1/agency/seo/audits/{queued}")).ShouldFailAsync(409, "seo.audit_in_progress");
        Assert.Equal(HttpStatusCode.NoContent, (await seo.DeleteAsync($"/api/v1/agency/seo/audits/{done}")).StatusCode);
        await (await seo.GetAsync($"/api/v1/agency/seo/audits/{done}")).ShouldFailAsync(404);
        Assert.False(await api.WithDbAsync(db => db.Set<SeoAuditIssue>().AnyAsync(i => i.AuditId == done)));
        await (await seo.DeleteAsync($"/api/v1/agency/seo/audits/{Guid.NewGuid()}")).ShouldFailAsync(404);
    }

    [Fact]
    public async Task Rule_settings_are_admin_only_change_severity_and_enablement_and_reset()
    {
        var (seo, _, _) = await SiteAsync("rules.example.com");
        var (_, admin) = await api.CreateClientAsync(Role.Admin);
        var rules = await (await seo.GetAsync("/api/v1/agency/seo/rules")).ReadJsonAsync();
        var rule = rules.EnumerateArray().First(r => r.GetProperty("key").GetString() == SeoAuditRules.H1Missing);
        var body = new
        {
            title = "Page has no H1 (agency wording)", severity = "Error", whyItMatters = rule.GetProperty("whyItMatters").GetString(),
            howToFix = rule.GetProperty("howToFix").GetString(), isEnabled = false, concurrencyStamp = rule.GetProperty("concurrencyStamp").GetGuid(),
        };
        // seo.manage alone may read but not change agency-wide settings.
        await (await seo.PutAsJsonAsync($"/api/v1/agency/seo/rules/{SeoAuditRules.H1Missing}", body)).ShouldFailAsync(403);
        await (await admin.PutAsJsonAsync("/api/v1/agency/seo/rules/no.such.rule", body)).ShouldFailAsync(404);
        await (await admin.PutAsJsonAsync($"/api/v1/agency/seo/rules/{SeoAuditRules.H1Missing}", new { title = "", severity = "Error", whyItMatters = "x", howToFix = "y" }))
            .ShouldFailAsync(400);

        var updated = await (await admin.PutAsJsonAsync($"/api/v1/agency/seo/rules/{SeoAuditRules.H1Missing}", body)).ReadJsonAsync();
        Assert.Equal("Error", updated.GetProperty("severity").GetString());
        Assert.Equal("Warning", updated.GetProperty("defaultSeverity").GetString());
        Assert.False(updated.GetProperty("isEnabled").GetBoolean());
        Assert.True(updated.GetProperty("isCustomized").GetBoolean());
        // A stale stamp is a friendly 409.
        await (await admin.PutAsJsonAsync($"/api/v1/agency/seo/rules/{SeoAuditRules.H1Missing}", body)).ShouldFailAsync(409, "concurrency.conflict");

        // The audit engine honours the settings: the disabled rule is skipped, severities come from the settings.
        var stored = await api.WithDbAsync(db => db.Set<SeoAuditRule>().AsNoTracking().ToDictionaryAsync(r => r.Key));
        Assert.False(stored[SeoAuditRules.H1Missing].IsEnabled);

        var reset = await (await admin.PostAsync($"/api/v1/agency/seo/rules/{SeoAuditRules.H1Missing}/reset", null)).ReadJsonAsync();
        Assert.True(reset.GetProperty("isEnabled").GetBoolean());
        Assert.False(reset.GetProperty("isCustomized").GetBoolean());
        Assert.Equal("Warning", reset.GetProperty("severity").GetString());
    }

    [Fact]
    public async Task Audit_rule_overrides_apply_to_new_audits_and_ignored_issues_carry_over()
    {
        api.SetConfig(("Seo:Crawler:AllowLoopback", "true"), ("Seo:Crawler:DelayMilliseconds", "0"));
        await using var siteServer = await TestSite.StartAsync();
        siteServer.Text("/robots.txt", "User-agent: *\nAllow: /\n")
            .Html("/", "<html><head><title>Home</title></head><body><p>No heading here</p></body></html>");
        var client = await api.CreateClientAccountAsync();
        var (_, seo) = await api.CreateClientAsync(Role.SeoSpecialist);
        var (_, admin) = await api.CreateClientAsync(Role.Admin);
        var siteId = (await (await seo.PostAsJsonAsync("/api/v1/agency/seo/sites", new
        {
            clientAccountId = client.Id, name = "Fixture", domain = $"127.0.0.1:{siteServer.Port}", protocol = "http", targetCountry = "US", targetLanguage = "en", maxPages = 5,
        })).ReadJsonAsync()).GetProperty("id").GetGuid();

        // Title too short → Error (overridden from Warning); missing sitemap disabled.
        foreach (var (key, severity, enabled) in new[] { (SeoAuditRules.TitleTooShort, "Error", true), (SeoAuditRules.SitemapMissing, "Notice", false) })
        {
            var d = SeoAuditRules.Find(key)!;
            (await admin.PutAsJsonAsync($"/api/v1/agency/seo/rules/{key}", new { title = d.Title, severity, whyItMatters = d.WhyItMatters, howToFix = d.HowToFix, isEnabled = enabled }))
                .EnsureSuccessStatusCode();
        }

        var firstId = (await (await seo.PostAsync($"/api/v1/agency/seo/sites/{siteId}/audits", null)).ReadJsonAsync()).GetProperty("id").GetGuid();
        await api.RunJobAsync<SeoAuditJob>();
        var first = await (await seo.GetAsync($"/api/v1/agency/seo/audits/{firstId}")).ReadJsonAsync();
        var issues = first.GetProperty("issues").EnumerateArray().ToList();
        Assert.Equal("Error", issues.Single(i => i.GetProperty("ruleKey").GetString() == SeoAuditRules.TitleTooShort).GetProperty("severity").GetString());
        Assert.DoesNotContain(issues, i => i.GetProperty("ruleKey").GetString() == SeoAuditRules.SitemapMissing);

        (await seo.PostAsJsonAsync($"/api/v1/agency/seo/audits/{firstId}/issues/{SeoAuditRules.H1Missing}/status", new { status = "Ignored", note = "Design choice" }))
            .EnsureSuccessStatusCode();
        api.Clock.Advance(TimeSpan.FromMinutes(5));
        var secondId = (await (await seo.PostAsync($"/api/v1/agency/seo/sites/{siteId}/audits", null)).ReadJsonAsync()).GetProperty("id").GetGuid();
        await api.RunJobAsync<SeoAuditJob>();
        var second = await (await seo.GetAsync($"/api/v1/agency/seo/audits/{secondId}")).ReadJsonAsync();
        var h1 = second.GetProperty("issues").EnumerateArray().Single(i => i.GetProperty("ruleKey").GetString() == SeoAuditRules.H1Missing);
        Assert.Equal("Ignored", h1.GetProperty("status").GetString());
        Assert.Equal("Design choice", h1.GetProperty("statusNote").GetString());

        foreach (var key in new[] { SeoAuditRules.TitleTooShort, SeoAuditRules.SitemapMissing })
            (await admin.PostAsync($"/api/v1/agency/seo/rules/{key}/reset", null)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Backlinks_can_be_edited_with_validation_and_duplicate_detection()
    {
        var (seo, siteId, _) = await SiteAsync("links.example.com");
        async Task<Guid> Add(string source) => (await (await seo.PostAsJsonAsync($"/api/v1/agency/seo/sites/{siteId}/backlinks", new
        {
            sourceUrl = source, targetUrl = "https://links.example.com/",
        })).ReadJsonAsync()).GetProperty("id").GetGuid();
        var a = await Add("https://blog.example.org/post-a");
        await Add("https://blog.example.org/post-b");

        var edited = await (await seo.PutAsJsonAsync($"/api/v1/agency/seo/backlinks/{a}", new
        {
            sourceUrl = "https://blog.example.org/post-a", targetUrl = "https://links.example.com/pricing", anchorText = "pricing", rel = "nofollow",
        })).ReadJsonAsync();
        Assert.Equal("https://links.example.com/pricing", edited.GetProperty("targetUrl").GetString());
        Assert.Equal("pricing", edited.GetProperty("anchorText").GetString());
        Assert.Equal("Unchecked", edited.GetProperty("status").GetString());

        await (await seo.PutAsJsonAsync($"/api/v1/agency/seo/backlinks/{a}", new { sourceUrl = "https://blog.example.org/post-a", targetUrl = "https://other.example/" }))
            .ShouldFailAsync(400);
        await (await seo.PutAsJsonAsync($"/api/v1/agency/seo/backlinks/{a}", new { sourceUrl = "https://blog.example.org/post-b", targetUrl = "https://links.example.com/" }))
            .ShouldFailAsync(409, "seo.backlink_exists");
        await (await seo.PutAsJsonAsync($"/api/v1/agency/seo/backlinks/{Guid.NewGuid()}", new { sourceUrl = "https://x.example/", targetUrl = "https://links.example.com/" }))
            .ShouldFailAsync(404);
        Assert.True(await api.WithDbAsync(db => db.Set<AuditLog>().AnyAsync(l => l.Action == "seo.backlink_updated" && l.EntityId == a.ToString())));
    }

    [Fact]
    public async Task Briefs_can_be_marked_published_and_duplicated_and_outreach_changes_are_audited()
    {
        var (seo, siteId, _) = await SiteAsync("briefs.example.com");
        var brief = await (await seo.PostAsJsonAsync($"/api/v1/agency/seo/sites/{siteId}/briefs", new
        {
            title = "Guide to widgets", targetKeyword = "widgets", outline = new[] { "## Intro" }, status = "Draft",
        })).ReadJsonAsync();
        var id = brief.GetProperty("id").GetGuid();
        var done = await (await seo.PutAsJsonAsync($"/api/v1/agency/seo/briefs/{id}", new
        {
            title = "Guide to widgets", targetKeyword = "widgets", outline = new[] { "## Intro" }, status = "Published",
            concurrencyStamp = brief.GetProperty("concurrencyStamp").GetGuid(),
        })).ReadJsonAsync();
        Assert.Equal("Published", done.GetProperty("status").GetString());

        var copy = await (await seo.PostAsync($"/api/v1/agency/seo/briefs/{id}/duplicate", null)).ReadJsonAsync();
        Assert.Equal("Guide to widgets (copy)", copy.GetProperty("title").GetString());
        Assert.Equal("Draft", copy.GetProperty("status").GetString());
        Assert.Equal(new[] { "## Intro" }, copy.GetProperty("outline").EnumerateArray().Select(o => o.GetString()));
        await (await seo.PostAsync($"/api/v1/agency/seo/briefs/{Guid.NewGuid()}/duplicate", null)).ShouldFailAsync(404);

        var prospect = await (await seo.PostAsJsonAsync($"/api/v1/agency/seo/sites/{siteId}/outreach", new { prospectUrl = "https://news.example/", status = "Identified" })).ReadJsonAsync();
        var pid = prospect.GetProperty("id").GetGuid();
        (await seo.PutAsJsonAsync($"/api/v1/agency/seo/outreach/{pid}", new
        {
            prospectUrl = "https://news.example/", status = "Won", concurrencyStamp = prospect.GetProperty("concurrencyStamp").GetGuid(),
        })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NoContent, (await seo.DeleteAsync($"/api/v1/agency/seo/outreach/{pid}")).StatusCode);
        var actions = await api.WithDbAsync(db => db.Set<AuditLog>().Where(l => l.EntityId == pid.ToString()).Select(l => l.Action).ToListAsync());
        Assert.Contains("seo.outreach_updated", actions);
        Assert.Contains("seo.outreach_deleted", actions);
    }

    [Fact]
    public async Task Directories_can_be_added_edited_hidden_and_deleted_by_admins()
    {
        var (seo, siteId, _) = await SiteAsync("local.example.com");
        var (_, admin) = await api.CreateClientAsync(Role.Admin);
        var body = new { name = "Agency Local Guide", url = "https://guide.example/", category = "Local", countries = new[] { "gb" }, sortOrder = 5 };
        await (await seo.PostAsJsonAsync("/api/v1/agency/seo/citation-sources", body)).ShouldFailAsync(403);
        await (await admin.PostAsJsonAsync("/api/v1/agency/seo/citation-sources", new { name = "Bad", url = "ftp://x", category = "Local" })).ShouldFailAsync(400);
        var created = await (await admin.PostAsJsonAsync("/api/v1/agency/seo/citation-sources", body)).ReadJsonAsync();
        Assert.True(created.GetProperty("isCustom").GetBoolean());
        Assert.Equal(new[] { "GB" }, created.GetProperty("countries").EnumerateArray().Select(c => c.GetString()));
        await (await admin.PostAsJsonAsync("/api/v1/agency/seo/citation-sources", body)).ShouldFailAsync(409, "seo.directory_exists");
        var id = created.GetProperty("id").GetGuid();

        var local = await (await seo.GetAsync($"/api/v1/agency/seo/sites/{siteId}/local")).ReadJsonAsync();
        Assert.Contains(local.GetProperty("citations").EnumerateArray(), c => c.GetProperty("name").GetString() == "Agency Local Guide");

        var hidden = await (await admin.PutAsJsonAsync($"/api/v1/agency/seo/citation-sources/{id}", new
        {
            body.name, body.url, body.category, body.countries, body.sortOrder, isActive = false, concurrencyStamp = created.GetProperty("concurrencyStamp").GetGuid(),
        })).ReadJsonAsync();
        Assert.False(hidden.GetProperty("isActive").GetBoolean());
        await (await admin.PutAsJsonAsync($"/api/v1/agency/seo/citation-sources/{id}", new
        {
            body.name, body.url, body.category, isActive = true, concurrencyStamp = created.GetProperty("concurrencyStamp").GetGuid(),
        })).ShouldFailAsync(409, "concurrency.conflict");
        local = await (await seo.GetAsync($"/api/v1/agency/seo/sites/{siteId}/local")).ReadJsonAsync();
        Assert.DoesNotContain(local.GetProperty("citations").EnumerateArray(), c => c.GetProperty("name").GetString() == "Agency Local Guide");

        var seeded = (await (await seo.GetAsync("/api/v1/agency/seo/citation-sources")).ReadJsonAsync()).EnumerateArray().First(s => !s.GetProperty("isCustom").GetBoolean());
        await (await admin.DeleteAsync($"/api/v1/agency/seo/citation-sources/{seeded.GetProperty("id").GetGuid()}")).ShouldFailAsync(409, "seo.directory_seeded");
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/v1/agency/seo/citation-sources/{id}")).StatusCode);
        await (await admin.DeleteAsync($"/api/v1/agency/seo/citation-sources/{id}")).ShouldFailAsync(404);
    }
}
