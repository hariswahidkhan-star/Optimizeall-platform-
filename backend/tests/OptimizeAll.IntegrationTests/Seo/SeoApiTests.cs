using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Api.Modules.Seo.Audit;
using OptimizeAll.Api.Modules.Seo.Demo;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.LandingPages;
using OptimizeAll.Domain.Seo;
using OptimizeAll.Infrastructure.Persistence;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.Seo;

public sealed class SeoApiTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private static readonly string Id = Guid.NewGuid().ToString();

    public static TheoryData<string, string> StaffEndpoints() => new()
    {
        { "GET", "/api/v1/agency/seo/sites" },
        { "POST", "/api/v1/agency/seo/sites" },
        { "GET", $"/api/v1/agency/seo/sites/{Id}" },
        { "POST", $"/api/v1/agency/seo/sites/{Id}/audits" },
        { "GET", $"/api/v1/agency/seo/audits/{Id}" },
        { "GET", $"/api/v1/agency/seo/audits/{Id}/diff" },
        { "POST", "/api/v1/agency/seo/analyze" },
        { "GET", $"/api/v1/agency/seo/sites/{Id}/keywords" },
        { "GET", $"/api/v1/agency/seo/sites/{Id}/rankings" },
        { "GET", $"/api/v1/agency/seo/sites/{Id}/backlinks" },
        { "GET", $"/api/v1/agency/seo/sites/{Id}/local" },
        { "GET", $"/api/v1/agency/seo/sites/{Id}/briefs" },
    };

    private static HttpRequestMessage Request(string method, string path) => new(new HttpMethod(method), path)
    {
        Content = method is "POST" or "PUT" ? JsonContent.Create(new { }) : null,
    };

    [Theory]
    [MemberData(nameof(StaffEndpoints))]
    public async Task Staff_endpoints_need_seo_manage(string method, string path)
    {
        var (_, designer) = await api.CreateClientAsync(Role.Designer);
        var (_, client) = await api.CreateClientAsync(Role.Client);
        var anonymous = api.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        Assert.Equal(HttpStatusCode.Forbidden, (await designer.SendAsync(Request(method, path))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(Request(method, path))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.SendAsync(Request(method, path))).StatusCode);
    }

    [Fact]
    public async Task Kpis_accept_seo_or_reports_permission_and_client_portal_is_scoped_to_membership()
    {
        var clientA = await api.CreateClientAccountAsync("Alpha Co");
        var clientB = await api.CreateClientAccountAsync("Beta Co");
        var (_, seo) = await api.CreateClientAsync(Role.SeoSpecialist);
        (await seo.PostAsJsonAsync("/api/v1/agency/seo/sites", new
        {
            clientAccountId = clientA.Id, name = "Alpha", domain = "https://www.alpha.example/", targetCountry = "GB", targetLanguage = "en",
        })).EnsureSuccessStatusCode();
        (await seo.PostAsJsonAsync("/api/v1/agency/seo/sites", new
        {
            clientAccountId = clientB.Id, name = "Beta", domain = "beta.example", targetCountry = "GB", targetLanguage = "en",
        })).EnsureSuccessStatusCode();

        var (_, accountManager) = await api.CreateClientAsync(Role.AccountManager); // reports.manage, no seo.manage
        var kpis = await (await accountManager.GetAsync($"/api/v1/agency/seo/clients/{clientA.Id}/kpis")).ReadJsonAsync();
        Assert.Equal("Alpha", kpis.GetProperty("sites")[0].GetProperty("name").GetString());
        var (_, designer) = await api.CreateClientAsync(Role.Designer);
        await (await designer.GetAsync($"/api/v1/agency/seo/clients/{clientA.Id}/kpis")).ShouldFailAsync(403);
        await (await seo.GetAsync($"/api/v1/agency/seo/clients/{Guid.NewGuid()}/kpis")).ShouldFailAsync(404);

        var (_, portalA) = await api.CreateClientUserAsync(clientA.Id);
        var overview = await (await portalA.GetAsync("/api/v1/client/seo/overview")).ReadJsonAsync();
        var orgs = overview.GetProperty("organizations").EnumerateArray().ToList();
        Assert.Single(orgs);
        Assert.Equal(clientA.Id, orgs[0].GetProperty("clientAccountId").GetGuid());
        Assert.Equal("www.alpha.example", orgs[0].GetProperty("kpis").GetProperty("sites")[0].GetProperty("domain").GetString());

        // Client users cannot reach the agency API, and staff without client.portal cannot use the portal endpoint.
        await (await portalA.GetAsync($"/api/v1/agency/seo/clients/{clientB.Id}/kpis")).ShouldFailAsync(403);
        await (await portalA.GetAsync("/api/v1/agency/seo/sites")).ShouldFailAsync(403);
        await (await seo.GetAsync("/api/v1/client/seo/overview")).ShouldFailAsync(403);
    }

    [Fact]
    public async Task Sites_validate_domains_and_reject_duplicates_with_concurrency_stamps()
    {
        var client = await api.CreateClientAccountAsync();
        var (_, seo) = await api.CreateClientAsync(Role.SeoSpecialist);
        await (await seo.PostAsJsonAsync("/api/v1/agency/seo/sites", new { clientAccountId = client.Id, name = "Bad", domain = "not a domain!", targetCountry = "US", targetLanguage = "en" }))
            .ShouldFailAsync(400);
        var site = await (await seo.PostAsJsonAsync("/api/v1/agency/seo/sites", new
        {
            clientAccountId = client.Id, name = "Main", domain = "HTTPS://Shop.Example.com/path", targetCountry = "us", targetLanguage = "en",
            competitors = new[] { "https://www.rival.example/", "other.example" },
        })).ReadJsonAsync();
        Assert.Equal("shop.example.com", site.GetProperty("domain").GetString());
        Assert.Equal("https://shop.example.com/", site.GetProperty("baseUrl").GetString());
        Assert.Equal(new[] { "rival.example", "other.example" }, site.GetProperty("competitors").EnumerateArray().Select(c => c.GetString()));
        await (await seo.PostAsJsonAsync("/api/v1/agency/seo/sites", new { clientAccountId = client.Id, name = "Dup", domain = "shop.example.com", targetCountry = "US", targetLanguage = "en" }))
            .ShouldFailAsync(409, "seo.site_exists");

        var id = site.GetProperty("id").GetGuid();
        var stamp = site.GetProperty("concurrencyStamp").GetGuid();
        var update = new { clientAccountId = client.Id, name = "Renamed", domain = "shop.example.com", targetCountry = "US", targetLanguage = "en", concurrencyStamp = stamp };
        (await seo.PutAsJsonAsync($"/api/v1/agency/seo/sites/{id}", update)).EnsureSuccessStatusCode();
        await (await seo.PutAsJsonAsync($"/api/v1/agency/seo/sites/{id}", update)).ShouldFailAsync(409, "concurrency.conflict");
        Assert.True(await api.WithDbAsync(db => db.Set<OptimizeAll.Domain.Audit.AuditLog>().AnyAsync(a => a.Action == "seo.site_updated" && a.EntityId == id.ToString())));
    }

    [Fact]
    public async Task Audit_runs_through_the_job_and_diffs_against_the_previous_audit()
    {
        api.SetConfig(("Seo:Crawler:AllowLoopback", "true"), ("Seo:Crawler:DelayMilliseconds", "0"));
        await using var siteServer = await TestSite.StartAsync();
        siteServer.Text("/robots.txt", "User-agent: *\nAllow: /\n")
            .Html("/", "<html><head><title>Home</title></head><body><h1>Home</h1><a href=\"/about\">About</a><a href=\"/gone\">Old</a></body></html>")
            .Html("/about", "<html><head><title>About us and our story at the fixture company</title></head><body><h1>About</h1></body></html>");

        var client = await api.CreateClientAccountAsync();
        var (_, seo) = await api.CreateClientAsync(Role.SeoSpecialist);
        var site = await (await seo.PostAsJsonAsync("/api/v1/agency/seo/sites", new
        {
            clientAccountId = client.Id, name = "Fixture", domain = $"127.0.0.1:{siteServer.Port}", protocol = "http", targetCountry = "US", targetLanguage = "en",
            maxPages = 20,
        })).ReadJsonAsync();
        var siteId = site.GetProperty("id").GetGuid();

        var queued = await seo.PostAsync($"/api/v1/agency/seo/sites/{siteId}/audits", null);
        Assert.Equal(HttpStatusCode.Accepted, queued.StatusCode);
        var firstId = (await queued.ReadJsonAsync()).GetProperty("id").GetGuid();
        await (await seo.PostAsync($"/api/v1/agency/seo/sites/{siteId}/audits", null)).ShouldFailAsync(409, "seo.audit_in_progress");

        await api.RunJobAsync<SeoAuditJob>();
        var first = await (await seo.GetAsync($"/api/v1/agency/seo/audits/{firstId}")).ReadJsonAsync();
        Assert.Equal("Completed", first.GetProperty("audit").GetProperty("status").GetString());
        Assert.Equal(3, first.GetProperty("audit").GetProperty("pagesCrawled").GetInt32());
        var issues = first.GetProperty("issues").EnumerateArray().ToList();
        var broken = issues.Single(i => i.GetProperty("ruleKey").GetString() == SeoAuditRules.BrokenInternalLink);
        Assert.Equal("Error", broken.GetProperty("severity").GetString());
        Assert.False(string.IsNullOrEmpty(broken.GetProperty("howToFix").GetString()));
        Assert.Contains(issues, i => i.GetProperty("ruleKey").GetString() == SeoAuditRules.TitleTooShort);
        // Severity ordering: errors first.
        Assert.Equal("Error", issues[0].GetProperty("severity").GetString());

        var csv = await (await seo.GetAsync($"/api/v1/agency/seo/audits/{firstId}/export.csv")).Content.ReadAsStringAsync();
        Assert.Contains("severity,rule,issue,category,url,detail,how_to_fix", csv);

        // Fix the broken link, break something else, re-audit, and diff.
        siteServer.Html("/", "<html><head><title>Home</title></head><body><h1>Home</h1><h1>Twice</h1><a href=\"/about\">About</a></body></html>");
        api.Clock.Advance(TimeSpan.FromMinutes(5));
        var secondId = (await (await seo.PostAsync($"/api/v1/agency/seo/sites/{siteId}/audits", null)).ReadJsonAsync()).GetProperty("id").GetGuid();
        await api.RunJobAsync<SeoAuditJob>();
        var diff = await (await seo.GetAsync($"/api/v1/agency/seo/audits/{secondId}/diff")).ReadJsonAsync();
        Assert.Equal(firstId, diff.GetProperty("againstAuditId").GetGuid());
        Assert.Contains(diff.GetProperty("fixedIssues").EnumerateArray(), i => i.GetProperty("ruleKey").GetString() == SeoAuditRules.BrokenInternalLink);
        Assert.Contains(diff.GetProperty("newIssues").EnumerateArray(), i => i.GetProperty("ruleKey").GetString() == SeoAuditRules.H1Multiple);

        var history = await (await seo.GetAsync($"/api/v1/agency/seo/sites/{siteId}/audits")).ReadJsonAsync();
        Assert.Equal(2, history.GetProperty("total").GetInt32());
        var pages = await (await seo.GetAsync($"/api/v1/agency/seo/audits/{secondId}/pages?pageSize=50")).ReadJsonAsync();
        Assert.Equal(2, pages.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task A_worker_whose_stale_audit_was_reclaimed_does_not_overwrite_the_new_results()
    {
        api.SetConfig(("Seo:Crawler:AllowLoopback", "true"), ("Seo:Crawler:DelayMilliseconds", "0"));
        await using var siteServer = await TestSite.StartAsync();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstRequestSeen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var homeCalls = 0;
        siteServer.Text("/robots.txt", "User-agent: *\nAllow: /\n").Route("/", async ctx =>
        {
            var call = Interlocked.Increment(ref homeCalls);
            if (call == 1)
            {
                firstRequestSeen.TrySetResult();
                await gate.Task;
            }
            ctx.Response.ContentType = "text/html; charset=utf-8";
            await Microsoft.AspNetCore.Http.HttpResponseWritingExtensions.WriteAsync(ctx.Response, $"<html><head><title>{(call == 1 ? "Slow first run" : "Second run")}</title></head><body><h1>Home</h1></body></html>");
        });

        var client = await api.CreateClientAccountAsync();
        var (_, seo) = await api.CreateClientAsync(Role.SeoSpecialist);
        var siteId = (await (await seo.PostAsJsonAsync("/api/v1/agency/seo/sites", new
        {
            clientAccountId = client.Id, name = "Reclaim", domain = $"127.0.0.1:{siteServer.Port}", protocol = "http", targetCountry = "US", targetLanguage = "en",
        })).ReadJsonAsync()).GetProperty("id").GetGuid();
        var auditId = (await (await seo.PostAsync($"/api/v1/agency/seo/sites/{siteId}/audits", null)).ReadJsonAsync()).GetProperty("id").GetGuid();

        // Worker A claims the audit and hangs on the home page …
        using var scopeA = api.Services.CreateScope();
        var slowRun = scopeA.ServiceProvider.GetRequiredService<SeoAuditRunner>().RunAsync(auditId, CancellationToken.None);
        await firstRequestSeen.Task.WaitAsync(TimeSpan.FromSeconds(30));

        // … long enough to be considered dead: worker B re-queues, claims and completes it.
        api.Clock.Advance(SeoAuditRunner.StaleAfter + TimeSpan.FromMinutes(1));
        using (var scopeB = api.Services.CreateScope())
            await scopeB.ServiceProvider.GetRequiredService<SeoAuditRunner>().RunQueuedAsync(10, CancellationToken.None);
        Assert.Equal(SeoAuditStatus.Completed, await api.WithDbAsync(db => db.Set<SeoAudit>().Where(a => a.Id == auditId).Select(a => a.Status).SingleAsync()));

        // Worker A finally finishes: it lost its claim, so it must not replace B's results.
        gate.SetResult();
        Assert.Null(await slowRun);
        var pages = await api.WithDbAsync(db => db.Set<SeoAuditPage>().Where(p => p.AuditId == auditId).ToListAsync());
        var home = Assert.Single(pages);
        Assert.Equal("Second run", home.Title);
    }

    [Fact]
    public async Task Audits_of_private_addresses_fail_safely()
    {
        api.SetConfig(("Seo:Crawler:AllowLoopback", "true"), ("Seo:Crawler:DelayMilliseconds", "0"));
        var client = await api.CreateClientAccountAsync();
        var (_, seo) = await api.CreateClientAsync(Role.SeoSpecialist);
        var site = await (await seo.PostAsJsonAsync("/api/v1/agency/seo/sites", new
        {
            clientAccountId = client.Id, name = "Metadata", domain = "169.254.169.254", protocol = "http", targetCountry = "US", targetLanguage = "en",
        })).ReadJsonAsync();
        var auditId = (await (await seo.PostAsync($"/api/v1/agency/seo/sites/{site.GetProperty("id").GetGuid()}/audits", null)).ReadJsonAsync()).GetProperty("id").GetGuid();
        await api.RunJobAsync<SeoAuditJob>();
        var audit = await (await seo.GetAsync($"/api/v1/agency/seo/audits/{auditId}")).ReadJsonAsync();
        Assert.Equal("Completed", audit.GetProperty("audit").GetProperty("status").GetString());
        var page = (await (await seo.GetAsync($"/api/v1/agency/seo/audits/{auditId}/pages")).ReadJsonAsync()).GetProperty("items")[0];
        Assert.Contains("non-public", page.GetProperty("fetchError").GetString());

        await (await seo.PostAsJsonAsync("/api/v1/agency/seo/analyze", new { url = "http://10.0.0.1/admin", keyword = "x" })).ShouldFailAsync(400, "seo.url_blocked");
    }

    [Fact]
    public async Task Long_text_columns_of_seo_and_landing_page_entities_are_unbounded()
    {
        var modules = new[] { typeof(SeoSite).Namespace, typeof(LandingPage).Namespace, typeof(OptimizeAll.Domain.Integrations.IntegrationConnection).Namespace };
        var offenders = await api.WithDbAsync(db => Task.FromResult(db.Model.GetEntityTypes()
            .Where(t => modules.Contains(t.ClrType.Namespace))
            .SelectMany(t => t.GetProperties().Where(p => p.GetMaxLength() >= 4000).Select(p => $"{t.ClrType.Name}.{p.Name} ({p.GetMaxLength()})"))
            .ToList()));
        Assert.Empty(offenders);
    }

    [Fact]
    public async Task Csv_imports_reject_files_with_too_many_rows()
    {
        var client = await api.CreateClientAccountAsync();
        var (_, seo) = await api.CreateClientAsync(Role.SeoSpecialist);
        var siteId = (await (await seo.PostAsJsonAsync("/api/v1/agency/seo/sites", new
        {
            clientAccountId = client.Id, name = "Bulk", domain = "bulk.example", targetCountry = "US", targetLanguage = "en",
        })).ReadJsonAsync()).GetProperty("id").GetGuid();
        var rows = string.Concat(Enumerable.Repeat("x,not-a-date,3,,\n", OptimizeAll.Api.Modules.Seo.Ranking.CsvReader.MaxImportRows + 1));

        var imports = new[]
        {
            ("ranks/import", "keyword,date,position,url,domain\n"),
            ("search-console/import", "query,date,clicks,impressions,page\n"),
            ("backlinks/import", "source_url,first_seen,x,y,z\n"),
        };
        foreach (var (path, header) in imports)
            await (await seo.PostAsync($"/api/v1/agency/seo/sites/{siteId}/{path}", AgencyTestData.CsvUpload(header + rows)))
                .ShouldFailAsync(400, "seo.import_too_many_rows");
        Assert.False(await api.WithDbAsync(db => db.Set<SeoKeyword>().AnyAsync(k => k.SiteId == siteId)));
    }

    [Fact]
    public async Task Rank_csv_import_is_idempotent_and_feeds_history_and_overview()
    {
        var client = await api.CreateClientAccountAsync();
        var (_, seo) = await api.CreateClientAsync(Role.SeoSpecialist);
        var site = await (await seo.PostAsJsonAsync("/api/v1/agency/seo/sites", new
        {
            clientAccountId = client.Id, name = "Ranks", domain = "ranks.example", targetCountry = "US", targetLanguage = "en", competitors = new[] { "rival.example" },
        })).ReadJsonAsync();
        var siteId = site.GetProperty("id").GetGuid();
        var today = DateOnly.FromDateTime(api.Clock.GetUtcNow().UtcDateTime);
        var csv = "keyword,date,position,url,domain,serp_features,search_volume\n" +
                  $"running shoes,{today.AddDays(-7):yyyy-MM-dd},12,https://ranks.example/shoes,,people_also_ask,5400\n" +
                  $"running shoes,{today:yyyy-MM-dd},4,https://ranks.example/shoes,,featured_snippet;people_also_ask,5400\n" +
                  $"running shoes,{today:yyyy-MM-dd},2,https://rival.example/x,rival.example,,5400\n" +
                  $"trail shoes,{today:yyyy-MM-dd},-,,,,\n" +
                  $"\"shoes, waterproof\",{today:yyyy-MM-dd},35,,,,\n" +
                  "bad row,not-a-date,3,,,,\n";

        var first = await (await seo.PostAsync($"/api/v1/agency/seo/sites/{siteId}/ranks/import", AgencyTestData.CsvUpload(csv))).ReadJsonAsync();
        Assert.Equal(6, first.GetProperty("rowsRead").GetInt32());
        Assert.Equal(5, first.GetProperty("created").GetInt32());
        Assert.Equal(3, first.GetProperty("keywordsCreated").GetInt32());
        Assert.Single(first.GetProperty("errors").EnumerateArray());

        var second = await (await seo.PostAsync($"/api/v1/agency/seo/sites/{siteId}/ranks/import", AgencyTestData.CsvUpload(csv))).ReadJsonAsync();
        Assert.Equal(0, second.GetProperty("created").GetInt32());
        Assert.Equal(0, second.GetProperty("updated").GetInt32());
        Assert.Equal(5, second.GetProperty("unchanged").GetInt32());
        Assert.Equal(0, second.GetProperty("keywordsCreated").GetInt32());
        Assert.Equal(5, await api.WithDbAsync(db => db.Set<SeoRankSnapshot>().CountAsync(s => s.SiteId == siteId)));

        var keywords = (await (await seo.GetAsync($"/api/v1/agency/seo/sites/{siteId}/keywords")).ReadJsonAsync()).EnumerateArray().ToList();
        var shoes = keywords.Single(k => k.GetProperty("keyword").GetString() == "running shoes");
        Assert.Equal(4, shoes.GetProperty("position").GetInt32());
        Assert.Equal(8, shoes.GetProperty("change").GetInt32());
        Assert.Equal(5400, shoes.GetProperty("searchVolume").GetInt32());

        var history = await (await seo.GetAsync($"/api/v1/agency/seo/keywords/{shoes.GetProperty("id").GetGuid()}/history")).ReadJsonAsync();
        Assert.Equal(3, history.GetArrayLength());

        var overview = await (await seo.GetAsync($"/api/v1/agency/seo/sites/{siteId}/rankings")).ReadJsonAsync();
        Assert.Equal(1, overview.GetProperty("distribution").GetProperty("top10").GetInt32());
        Assert.Equal(1, overview.GetProperty("distribution").GetProperty("notRanking").GetInt32()); // "trail shoes"
        var sov = overview.GetProperty("shareOfVoice").EnumerateArray().ToList();
        Assert.Equal("rival.example", sov[0].GetProperty("domain").GetString());
        Assert.Equal(1, overview.GetProperty("serpFeatures").GetProperty("featured_snippet").GetInt32());

        // Manual entry updates the same (keyword, date, domain) row instead of duplicating it.
        (await seo.PostAsJsonAsync($"/api/v1/agency/seo/keywords/{shoes.GetProperty("id").GetGuid()}/ranks", new { date = today, position = 3 })).EnsureSuccessStatusCode();
        Assert.Equal(5, await api.WithDbAsync(db => db.Set<SeoRankSnapshot>().CountAsync(s => s.SiteId == siteId)));

        // The provider is not configured by default: refreshing reports it instead of pretending.
        var added = await (await seo.PostAsJsonAsync($"/api/v1/agency/seo/sites/{siteId}/keywords", new { keywords = new[] { "barefoot shoes", "Running Shoes" }, intent = "Commercial" }))
            .ReadJsonAsync();
        Assert.Equal("barefoot shoes", Assert.Single(added.EnumerateArray()).GetProperty("keyword").GetString()); // duplicates are skipped
        var refresh = await (await seo.PostAsync($"/api/v1/agency/seo/sites/{siteId}/ranks/refresh", null)).ReadJsonAsync();
        Assert.Equal("NotConfigured", refresh.GetProperty("outcome").GetString());
        var sync = await (await seo.PostAsync($"/api/v1/agency/seo/sites/{siteId}/search-console/sync", null)).ReadJsonAsync();
        Assert.Equal("NotConfigured", sync.GetProperty("outcome").GetString());

        var gsc = "Top queries,Clicks,Impressions,CTR,Position\nrunning shoes,120,2400,5%,4.2\ntrail shoes,8,900,0.89%,18.1\n";
        var imported = await (await seo.PostAsync($"/api/v1/agency/seo/sites/{siteId}/search-console/import", AgencyTestData.CsvUpload(gsc, date: today.AddDays(-1).ToString("yyyy-MM-dd")))).ReadJsonAsync();
        Assert.Equal(2, imported.GetProperty("created").GetInt32());
        var again = await (await seo.PostAsync($"/api/v1/agency/seo/sites/{siteId}/search-console/import", AgencyTestData.CsvUpload(gsc, date: today.AddDays(-1).ToString("yyyy-MM-dd")))).ReadJsonAsync();
        Assert.Equal(2, again.GetProperty("unchanged").GetInt32());
        var perf = await (await seo.GetAsync($"/api/v1/agency/seo/sites/{siteId}/search-console")).ReadJsonAsync();
        Assert.Equal(128, perf.GetProperty("clicks").GetInt32());
        Assert.Equal(3300, perf.GetProperty("impressions").GetInt32());
    }

    [Fact]
    public async Task Backlinks_local_seo_and_briefs_round_trip()
    {
        api.SetConfig(("Seo:Crawler:AllowLoopback", "true"), ("Seo:Crawler:DelayMilliseconds", "0"));
        await using var referrer = await TestSite.StartAsync();
        referrer.Html("/post", "<p>Read <a href=\"https://links.example/\">Links Example</a> today.</p>");
        var client = await api.CreateClientAccountAsync();
        var (_, seo) = await api.CreateClientAsync(Role.SeoSpecialist);
        var siteId = (await (await seo.PostAsJsonAsync("/api/v1/agency/seo/sites", new
        {
            clientAccountId = client.Id, name = "Links", domain = "links.example", targetCountry = "PK", targetLanguage = "en",
        })).ReadJsonAsync()).GetProperty("id").GetGuid();

        var csv = $"source_url,target_url,anchor,rel\n{referrer.Url("/post")},https://links.example/,Links Example,\nhttps://elsewhere.example/a,https://not-our-site.example/,x,\n";
        var result = await (await seo.PostAsync($"/api/v1/agency/seo/sites/{siteId}/backlinks/import", AgencyTestData.CsvUpload(csv))).ReadJsonAsync();
        Assert.Equal(1, result.GetProperty("created").GetInt32());
        Assert.Single(result.GetProperty("errors").EnumerateArray());
        var repeat = await (await seo.PostAsync($"/api/v1/agency/seo/sites/{siteId}/backlinks/import", AgencyTestData.CsvUpload(csv))).ReadJsonAsync();
        Assert.Equal(0, repeat.GetProperty("created").GetInt32());

        (await seo.PostAsync($"/api/v1/agency/seo/sites/{siteId}/backlinks/check", null)).EnsureSuccessStatusCode();
        var list = await (await seo.GetAsync($"/api/v1/agency/seo/sites/{siteId}/backlinks")).ReadJsonAsync();
        Assert.Equal("Live", list.GetProperty("page").GetProperty("items")[0].GetProperty("status").GetString());
        Assert.Equal(1, list.GetProperty("summary").GetProperty("live").GetInt32());

        var local = await (await seo.PutAsJsonAsync($"/api/v1/agency/seo/sites/{siteId}/local/profile", new
        {
            businessName = "Links & Co", address = "12 Main Street, Karachi", phone = "+92 21 1234 5678", completedChecklist = new[] { "gbp.claimed", "gbp.hours" },
        })).ReadJsonAsync();
        Assert.Equal(30, local.GetProperty("citations").GetArrayLength());
        Assert.Equal(2, local.GetProperty("checklistDone").GetInt32());
        var source = local.GetProperty("citations")[1].GetProperty("sourceId").GetGuid();
        var citation = await (await seo.PutAsJsonAsync($"/api/v1/agency/seo/sites/{siteId}/local/citations/{source}", new
        {
            status = "Live", listingUrl = "https://maps.example/links", listedName = "Links and Co", listedAddress = "12 Main St, Karachi", listedPhone = "021 1234 9999",
        })).ReadJsonAsync();
        Assert.True(citation.GetProperty("nameMatches").GetBoolean());
        Assert.True(citation.GetProperty("addressMatches").GetBoolean());
        Assert.False(citation.GetProperty("phoneMatches").GetBoolean());
        Assert.False(citation.GetProperty("consistent").GetBoolean());

        (await seo.PostAsJsonAsync($"/api/v1/agency/seo/sites/{siteId}/reviews", new { platform = "Google", rating = 4, text = "Good", reviewedAt = DateTime.UtcNow, responseText = "Thanks!" }))
            .EnsureSuccessStatusCode();
        var reviews = await (await seo.GetAsync($"/api/v1/agency/seo/sites/{siteId}/local")).ReadJsonAsync();
        Assert.Equal(1.0, reviews.GetProperty("reviews").GetProperty("responseRate").GetDouble());

        var brief = await (await seo.PostAsJsonAsync($"/api/v1/agency/seo/sites/{siteId}/briefs", new
        {
            title = "Backlink guide", targetKeyword = "backlinks", relatedKeywords = new[] { "link building" }, questions = new[] { "What is a backlink?" },
            outline = new[] { "## Basics", "## Strategy" }, wordCountTarget = 1500, competitorUrls = new[] { "https://rival.example/guide" },
        })).ReadJsonAsync();
        var handoff = await (await seo.PostAsync($"/api/v1/agency/seo/briefs/{brief.GetProperty("id").GetGuid()}/handoff", null)).ReadJsonAsync();
        Assert.Equal("HandedOff", handoff.GetProperty("brief").GetProperty("status").GetString());
        Assert.False(handoff.GetProperty("taskCreated").GetBoolean());
        Assert.Contains("# Backlink guide", handoff.GetProperty("markdown").GetString());
    }

    [Fact]
    public async Task Demo_seed_is_idempotent_and_uses_the_canonical_clients()
    {
        async Task Seed()
        {
            using var scope = api.Services.CreateScope();
            var seeder = scope.ServiceProvider.GetServices<ISeeder>().OfType<AgencyToolkitDemoSeeder>().Single();
            await seeder.SeedAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(), CancellationToken.None);
        }

        await Seed();
        var counts = await api.WithDbAsync(async db => (
            Sites: await db.Set<SeoSite>().CountAsync(), Snapshots: await db.Set<SeoRankSnapshot>().CountAsync(),
            Pages: await db.Set<LandingPage>().CountAsync(p => p.Status == LandingPageStatus.Published),
            Submissions: await db.Set<FormSubmission>().CountAsync(), Audits: await db.Set<SeoAudit>().CountAsync(a => a.Status == SeoAuditStatus.Completed)));
        await Seed();
        var again = await api.WithDbAsync(async db => (
            Sites: await db.Set<SeoSite>().CountAsync(), Snapshots: await db.Set<SeoRankSnapshot>().CountAsync(),
            Pages: await db.Set<LandingPage>().CountAsync(p => p.Status == LandingPageStatus.Published),
            Submissions: await db.Set<FormSubmission>().CountAsync(), Audits: await db.Set<SeoAudit>().CountAsync(a => a.Status == SeoAuditStatus.Completed)));
        Assert.Equal(counts, again);
        Assert.True(counts.Pages >= 4 && counts.Submissions > 0 && counts.Audits >= 8 && counts.Snapshots > 1000);

        var clients = await api.WithDbAsync(db => db.Set<ClientAccount>().Where(c => c.Slug == "nimbus-fitness" || c.Slug == "karachi-eats").ToListAsync());
        foreach (var canonical in new[] { OptimizeAll.Api.Modules.Clients.DeliveryDemoData.Nimbus, OptimizeAll.Api.Modules.Clients.DeliveryDemoData.KarachiEats })
        {
            var row = clients.Single(c => c.Slug == canonical.Slug);
            Assert.Equal((canonical.Name, canonical.Industry, canonical.CountryCode, canonical.Currency, canonical.Website, canonical.Status),
                (row.Name, row.Industry, row.CountryCode, row.Currency, row.Website, row.Status));
        }

        // The SEO and designer demo staff are the canonical delivery demo staff (same email, display name and role).
        foreach (var staff in new[] { OptimizeAll.Api.Modules.Clients.DeliveryDemoData.Seo, OptimizeAll.Api.Modules.Clients.DeliveryDemoData.Designer })
        {
            var normalized = OptimizeAll.Domain.Common.Normalization.Email(staff.Email);
            var user = await api.WithDbAsync(db => db.Set<OptimizeAll.Domain.Identity.User>().Include(u => u.Roles)
                .SingleAsync(u => u.NormalizedEmail == normalized));
            Assert.Equal(staff.DisplayName, user.DisplayName);
            Assert.Contains(user.Roles, r => r.Role == staff.Role);
        }

        var seoUser = await api.LoginAsync(new TestUser(Guid.Empty, AgencyToolkitDemoSeeder.SeoEmail, AgencyToolkitDemoSeeder.DemoPassword));
        var sites = await (await seoUser.GetAsync("/api/v1/agency/seo/sites?pageSize=50")).ReadJsonAsync();
        Assert.True(sites.GetProperty("total").GetInt32() >= 4);
        var designer = await api.LoginAsync(new TestUser(Guid.Empty, AgencyToolkitDemoSeeder.DesignerEmail, AgencyToolkitDemoSeeder.DemoPassword));
        (await designer.GetAsync("/api/v1/agency/pages/landing-pages")).EnsureSuccessStatusCode();

        // A seeded page is live on its public path and the audit diff has content.
        var page = await api.CreateClient().GetAsync("/api/v1/public/lp/nimbus-fitness/free-trial");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var nimbusSite = await api.WithDbAsync(db => db.Set<SeoSite>().FirstAsync(s => s.Domain == "nimbusfitness.app"));
        var latest = await api.WithDbAsync(db => db.Set<SeoAudit>().Where(a => a.SiteId == nimbusSite.Id).OrderByDescending(a => a.QueuedAt).FirstAsync());
        var diff = await (await seoUser.GetAsync($"/api/v1/agency/seo/audits/{latest.Id}/diff")).ReadJsonAsync();
        Assert.True(diff.GetProperty("fixedCount").GetInt32() > 0);
    }
}
