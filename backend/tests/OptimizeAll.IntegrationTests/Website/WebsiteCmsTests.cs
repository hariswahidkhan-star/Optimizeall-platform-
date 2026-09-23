using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OptimizeAll.Api.Modules.Website;
using OptimizeAll.Api.Modules.Website.Blog;
using OptimizeAll.Domain.Audit;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Website;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.Website;

/// <summary>Website CMS: publishing rules, slugs, blog workflow and scheduling, SEO endpoints and the permission matrix.</summary>
public sealed class WebsiteCmsTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private async Task<Guid> CategoryIdAsync(string slug) =>
        await api.WithDbAsync(db => db.Set<ServiceCategory>().Where(c => c.Slug == slug).Select(c => c.Id).FirstAsync());

    private static object Service(Guid categoryId, string slug, bool published, Guid? stamp = null) => new
    {
        categoryId, slug, name = "Hidden service " + slug, tagline = "Only visible when published", isPublished = published,
        overviewMarkdown = "An overview.", deliverables = new[] { "A deliverable" }, concurrencyStamp = stamp,
    };

    [Fact]
    public async Task Baseline_catalog_is_public_with_packages_faq_and_json_ld()
    {
        var anon = api.Anonymous();
        var groups = (await anon.GetJsonAsync("/api/v1/public/services")).EnumerateArray().ToList();
        Assert.Equal(9, groups.Count);
        Assert.True(groups.Sum(g => g.GetProperty("services").GetArrayLength()) == 33);
        Assert.Contains(groups, g => g.GetProperty("services").EnumerateArray().Any(s => s.GetProperty("slug").GetString() == "influencer-ugc-marketing"));

        var seo = await anon.GetJsonAsync("/api/v1/public/services/seo");
        Assert.Equal(3, seo.GetProperty("packages").GetArrayLength());
        Assert.Contains(seo.GetProperty("packages").EnumerateArray(), p => p.GetProperty("isMostPopular").GetBoolean());
        Assert.Contains(seo.GetProperty("packages").EnumerateArray(), p => p.GetProperty("isCustomQuote").GetBoolean());
        var types = seo.GetProperty("jsonLd").EnumerateArray().Select(j => j.GetProperty("@type").GetString()).ToList();
        Assert.Contains("Service", types);
        Assert.Contains("BreadcrumbList", types);
        Assert.Contains("FAQPage", types);

        var influencer = await anon.GetJsonAsync("/api/v1/public/services/influencer-ugc-marketing");
        Assert.Equal("/creators", influencer.GetProperty("ctaUrl").GetString());

        // Other modules read packages by id through IServiceCatalog.
        var packageId = seo.GetProperty("packages")[0].GetProperty("id").GetGuid();
        using var scope = api.Services.CreateScope();
        var info = await scope.ServiceProvider.GetRequiredService<IServiceCatalog>().GetPackageAsync(packageId);
        Assert.NotNull(info);
        Assert.Equal("seo", info!.ServiceSlug);
        Assert.Equal("USD", info.Currency);

        var legal = await anon.GetJsonAsync("/api/v1/public/pages/privacy-policy");
        Assert.Equal("Legal", legal.GetProperty("kind").GetString());
        Assert.Contains("review with legal counsel", legal.GetProperty("blocks")[0].GetProperty("data").GetProperty("markdown").GetString());
    }

    [Fact]
    public async Task Public_endpoints_hide_unpublished_content()
    {
        var admin = await api.AdminAsync();
        var anon = api.Anonymous();
        var search = await CategoryIdAsync("search");

        var draft = await admin.PostJsonAsync("/api/v1/agency/website/services", Service(search, "draft-service-x", false), 201);
        await anon.GetJsonAsync("/api/v1/public/services/draft-service-x", 404);
        Assert.DoesNotContain("draft-service-x", await (await anon.GetAsync("/api/v1/public/services")).Content.ReadAsStringAsync());

        // Publishing makes it visible; unpublishing its category hides it again.
        var published = await admin.PutJsonAsync($"/api/v1/agency/website/services/{draft.GetProperty("id").GetGuid()}",
            Service(search, "draft-service-x", true, draft.GetProperty("concurrencyStamp").GetGuid()));
        await anon.GetJsonAsync("/api/v1/public/services/draft-service-x");
        var category = await admin.PostJsonAsync("/api/v1/agency/website/service-categories",
            new { slug = "hidden-line", name = "Hidden line", isPublished = false }, 201);
        await admin.PutJsonAsync($"/api/v1/agency/website/services/{published.GetProperty("id").GetGuid()}",
            Service(category.GetProperty("id").GetGuid(), "draft-service-x", true, published.GetProperty("concurrencyStamp").GetGuid()));
        await anon.GetJsonAsync("/api/v1/public/services/draft-service-x", 404);

        // Case studies, pages and industries.
        var cs = await admin.PostJsonAsync("/api/v1/agency/website/case-studies", new
        {
            slug = "secret-case", title = "Secret case", clientName = "Acme", summary = "Not yet approved by the client.", isPublished = false,
            metrics = new[] { new { label = "Leads", value = "+50%", measurement = "Measured" } },
        }, 201);
        await anon.GetJsonAsync("/api/v1/public/case-studies/secret-case", 404);
        Assert.DoesNotContain("secret-case", await (await anon.GetAsync("/api/v1/public/case-studies")).Content.ReadAsStringAsync());
        await admin.PostJsonAsync("/api/v1/agency/website/pages", new
        {
            slug = "coming-soon", title = "Coming soon", kind = "Standard", isPublished = false,
            blocks = new[] { new { type = "richText", data = new { markdown = "Soon." } } },
        }, 201);
        await anon.GetJsonAsync("/api/v1/public/pages/coming-soon", 404);
        await admin.PostJsonAsync("/api/v1/agency/website/industries", new { slug = "space", name = "Space", summary = "Rockets.", isPublished = false }, 201);
        await anon.GetJsonAsync("/api/v1/public/industries/space", 404);

        // Sitemap lists published items only.
        var sitemap = await (await anon.GetAsync("/api/v1/public/sitemap.xml")).Content.ReadAsStringAsync();
        Assert.Contains("<loc>http://app.test/services/seo</loc>", sitemap);
        Assert.Contains("<loc>http://app.test/privacy-policy</loc>", sitemap);
        Assert.Contains("<loc>http://app.test/industries/ecommerce</loc>", sitemap);
        foreach (var hidden in new[] { "draft-service-x", "secret-case", "coming-soon", "/industries/space" })
            Assert.DoesNotContain(hidden, sitemap);
        Assert.NotEqual(Guid.Empty, cs.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Case_study_metrics_must_say_measured_or_estimated()
    {
        var admin = await api.AdminAsync();
        var problem = await admin.PostJsonAsync("/api/v1/agency/website/case-studies", new
        {
            slug = "no-measurement", title = "No measurement", clientName = "Acme", summary = "Missing kind.",
            metrics = new[] { new { label = "Revenue", value = "+20%" } },
        }, 400);
        Assert.True(problem.GetProperty("errors").TryGetProperty("metrics[0].measurement", out _));
    }

    [Fact]
    public async Task Slugs_are_unique_per_content_type()
    {
        var admin = await api.AdminAsync();
        var search = await CategoryIdAsync("search");
        var problem = await admin.PostJsonAsync("/api/v1/agency/website/services", Service(search, "seo", true), 409);
        Assert.Equal("website.slug_taken", problem.Code());
        Assert.Equal("website.slug_taken", (await admin.PostJsonAsync("/api/v1/agency/website/industries",
            new { slug = "ecommerce", name = "Dup", summary = "Dup" }, 409)).Code());
        Assert.Equal("website.invalid", (await admin.PostJsonAsync("/api/v1/agency/website/services",
            Service(search, "Not A Slug", true), 400)).Code());
        // A page may not shadow a built-in route.
        await admin.PostJsonAsync("/api/v1/agency/website/pages", new
        {
            slug = "blog", title = "Blog", kind = "Standard", blocks = new[] { new { type = "richText", data = new { markdown = "x" } } },
        }, 400);
    }

    [Fact]
    public async Task Page_blocks_are_validated_per_type()
    {
        var admin = await api.AdminAsync();
        var problem = await admin.PostJsonAsync("/api/v1/agency/website/pages/preview", new object[]
        {
            new { type = "hero", data = new { title = "", primaryCta = new { label = "Go", url = "javascript:alert(1)" } } },
            new { type = "marquee", data = new { } },
            new { type = "richText", data = new { markdown = "Hi <script>alert(1)</script> [x](javascript:alert(2))" } },
        }, 400);
        var errors = problem.GetProperty("errors");
        Assert.True(errors.TryGetProperty("blocks[0].data.title", out _));
        Assert.True(errors.TryGetProperty("blocks[0].data.primaryCta.url", out _));
        Assert.True(errors.TryGetProperty("blocks[1].type", out _));

        var ok = await admin.PostJsonAsync("/api/v1/agency/website/pages/preview", new object[]
        {
            new { type = "richText", data = new { markdown = "Hi <script>alert(1)</script> [x](javascript:alert(2)) <img src=x onerror=alert(3)>" } },
        }, 200);
        var markdown = ok[0].GetProperty("data").GetProperty("markdown").GetString()!;
        Assert.DoesNotContain("script", markdown);
        Assert.DoesNotContain("javascript:", markdown);
        Assert.DoesNotContain("onerror", markdown);
    }

    [Fact]
    public async Task Writers_draft_and_submit_but_only_publishers_publish()
    {
        var (writerUser, writer) = await api.CreateClientAsync(Role.ContentCreator);
        var admin = await api.AdminAsync();
        var anon = api.Anonymous();
        var body = string.Join("\n\n", Enumerable.Repeat("A practical paragraph about measuring marketing properly, with enough words to count.", 5)) +
                   "\n\n<script>alert('x')</script>";

        var post = await writer.PostJsonAsync("/api/v1/agency/website/blog/posts", new
        {
            slug = "measuring-marketing", title = "Measuring marketing", excerpt = "How to measure what matters.", bodyMarkdown = body, tags = new[] { "Analytics" },
        }, 201);
        var id = post.GetProperty("id").GetGuid();
        Assert.Equal("Draft", post.GetProperty("status").GetString());
        Assert.DoesNotContain("<script", post.GetProperty("bodyMarkdown").GetString());
        Assert.True(post.GetProperty("readingMinutes").GetInt32() >= 1);
        Assert.False(post.GetProperty("can").GetProperty("publish").GetBoolean());

        // Writers cannot publish or schedule (endpoint permission), and drafts are invisible publicly.
        Assert.Equal(HttpStatusCode.Forbidden, (await writer.PostAsJsonAsync($"/api/v1/agency/website/blog/posts/{id}/publish",
            new { concurrencyStamp = post.GetProperty("concurrencyStamp").GetGuid() })).StatusCode);
        await anon.GetJsonAsync("/api/v1/public/blog/measuring-marketing", 404);

        var submitted = await writer.PostJsonAsync($"/api/v1/agency/website/blog/posts/{id}/submit",
            new { concurrencyStamp = post.GetProperty("concurrencyStamp").GetGuid() }, 200);
        Assert.Equal("InReview", submitted.GetProperty("status").GetString());

        var published = await admin.PostJsonAsync($"/api/v1/agency/website/blog/posts/{id}/publish",
            new { concurrencyStamp = submitted.GetProperty("concurrencyStamp").GetGuid() }, 200);
        Assert.Equal("Published", published.GetProperty("status").GetString());
        var live = await anon.GetJsonAsync("/api/v1/public/blog/measuring-marketing");
        Assert.Contains(live.GetProperty("jsonLd").EnumerateArray(), j => j.GetProperty("@type").GetString() == "BlogPosting");

        // A writer can no longer edit the live post.
        var edit = await writer.PutAsJsonAsync($"/api/v1/agency/website/blog/posts/{id}", new
        {
            slug = "measuring-marketing", title = "Changed", excerpt = "x", bodyMarkdown = body, concurrencyStamp = published.GetProperty("concurrencyStamp").GetGuid(),
        });
        await edit.ShouldFailAsync(403, "blog.publish_required");

        Assert.True(await api.WithDbAsync(db => db.Set<AuditLog>().AnyAsync(a => a.Action == "blog.post_published" && a.EntityId == id.ToString())));
        var rss = await (await anon.GetAsync("/api/v1/public/blog/rss.xml")).Content.ReadAsStringAsync();
        Assert.Contains("<title>Measuring marketing</title>", rss);
        Assert.Contains("http://app.test/blog/measuring-marketing", rss);
        Assert.NotEqual(Guid.Empty, writerUser.Id);
    }

    [Fact]
    public async Task Scheduled_posts_are_published_by_the_job_when_due()
    {
        var admin = await api.AdminAsync();
        var anon = api.Anonymous();
        var body = string.Join(" ", Enumerable.Repeat("Scheduling content ahead of time keeps a steady publishing cadence.", 6));
        var post = await admin.PostJsonAsync("/api/v1/agency/website/blog/posts", new
        {
            slug = "scheduled-post", title = "Scheduled post", excerpt = "Goes live later.", bodyMarkdown = body,
        }, 201);
        var at = api.Clock.GetUtcNow().UtcDateTime.AddHours(2);
        var scheduled = await admin.PostJsonAsync($"/api/v1/agency/website/blog/posts/{post.GetProperty("id").GetGuid()}/schedule",
            new { concurrencyStamp = post.GetProperty("concurrencyStamp").GetGuid(), publishAt = at }, 200);
        Assert.Equal("Scheduled", scheduled.GetProperty("status").GetString());

        await api.RunJobAsync<BlogSchedulerJob>();
        await anon.GetJsonAsync("/api/v1/public/blog/scheduled-post", 404);
        Assert.DoesNotContain("scheduled-post", await (await anon.GetAsync("/api/v1/public/sitemap.xml")).Content.ReadAsStringAsync());

        api.Clock.Advance(TimeSpan.FromHours(3));
        await api.RunJobAsync<BlogSchedulerJob>();
        var live = await anon.GetJsonAsync("/api/v1/public/blog/scheduled-post");
        Assert.Equal(at, live.GetProperty("publishedAt").GetDateTime().ToUniversalTime(), TimeSpan.FromSeconds(1));
        Assert.Contains("scheduled-post", await (await anon.GetAsync("/api/v1/public/sitemap.xml")).Content.ReadAsStringAsync());

        // Running again changes nothing (idempotent).
        await api.RunJobAsync<BlogSchedulerJob>();
        Assert.Equal(1, await api.WithDbAsync(db => db.Set<AuditLog>().CountAsync(a => a.Action == "blog.post_published" && a.EntityId == post.GetProperty("id").GetGuid().ToString())));
    }

    [Fact]
    public async Task Robots_txt_blocks_portals_and_links_the_sitemap()
    {
        var response = await api.Anonymous().GetAsync("/robots.txt");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType!.MediaType);
        var text = await response.Content.ReadAsStringAsync();
        foreach (var path in new[] { "/app", "/agency", "/client", "/admin", "/finance", "/review", "/manage", "/api" })
            Assert.Contains($"Disallow: {path}\n", text);
        Assert.Contains("Sitemap: http://app.test/api/v1/public/sitemap.xml", text);
    }

    [Fact]
    public async Task Search_finds_published_services_posts_and_case_studies()
    {
        var result = await api.Anonymous().GetJsonAsync("/api/v1/public/search?q=seo");
        Assert.Contains(result.GetProperty("services").EnumerateArray(), s => s.GetProperty("slug").GetString() == "seo");
        Assert.Equal(0, (await api.Anonymous().GetJsonAsync("/api/v1/public/search?q=a")).GetProperty("services").GetArrayLength());
    }

    public static IEnumerable<object[]> AdminEndpoints() => new[]
    {
        new object[] { "GET", "/api/v1/agency/website/services" },
        new object[] { "POST", "/api/v1/agency/website/services" },
        new object[] { "GET", "/api/v1/agency/website/service-categories" },
        new object[] { "GET", "/api/v1/agency/website/industries" },
        new object[] { "GET", "/api/v1/agency/website/case-studies" },
        new object[] { "GET", "/api/v1/agency/website/testimonials" },
        new object[] { "GET", "/api/v1/agency/website/team" },
        new object[] { "GET", "/api/v1/agency/website/pages" },
        new object[] { "GET", "/api/v1/agency/website/settings" },
        new object[] { "GET", "/api/v1/agency/website/inquiries" },
        new object[] { "GET", "/api/v1/agency/website/overview" },
        new object[] { "GET", "/api/v1/agency/website/bookings" },
        new object[] { "GET", "/api/v1/agency/website/bookings/settings" },
        new object[] { "GET", "/api/v1/agency/website/newsletter/subscribers" },
        new object[] { "GET", "/api/v1/agency/website/newsletter/subscribers/export.csv" },
        new object[] { "GET", "/api/v1/agency/website/blog/posts" },
        new object[] { "POST", "/api/v1/agency/website/blog/posts" },
        new object[] { "GET", "/api/v1/agency/website/careers/jobs" },
        new object[] { "GET", "/api/v1/agency/website/careers/applications" },
        new object[] { "GET", "/api/v1/agency/website/catalog" },
        new object[] { "POST", "/api/v1/agency/website/images" },
    };

    [Theory]
    [MemberData(nameof(AdminEndpoints))]
    public async Task Staff_endpoints_reject_anonymous_and_participants(string method, string path)
    {
        HttpRequestMessage Request() => new(new HttpMethod(method), path)
        {
            Content = method != "POST" ? null
                : path.EndsWith("/images", StringComparison.Ordinal) ? new MultipartFormDataContent { { new ByteArrayContent(new byte[] { 1 }), "file", "x.png" } }
                : JsonContent.Create(new { }),
        };
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.Anonymous().SendAsync(Request())).StatusCode);
        var (_, participant) = await api.CreateClientAsync(Role.Participant);
        Assert.Equal(HttpStatusCode.Forbidden, (await participant.SendAsync(Request())).StatusCode);
    }

    [Fact]
    public async Task Writers_cannot_manage_site_content_and_crm_viewers_only_read_inquiries()
    {
        var (_, writer) = await api.CreateClientAsync(Role.ContentCreator);
        Assert.Equal(HttpStatusCode.Forbidden, (await writer.GetAsync("/api/v1/agency/website/services")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await writer.GetAsync("/api/v1/agency/website/inquiries")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await writer.GetAsync("/api/v1/agency/website/blog/posts")).StatusCode);

        var (_, sales) = await api.CreateClientAsync(Role.SalesRep);
        Assert.Equal(HttpStatusCode.OK, (await sales.GetAsync("/api/v1/agency/website/inquiries")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await sales.GetAsync("/api/v1/agency/website/catalog")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await sales.PutAsJsonAsync($"/api/v1/agency/website/inquiries/{Guid.NewGuid()}",
            new { status = "Closed", concurrencyStamp = Guid.NewGuid() })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await sales.GetAsync("/api/v1/agency/website/careers/jobs")).StatusCode);
    }

    [Fact]
    public async Task Site_settings_validate_links_and_analytics_ids()
    {
        var admin = await api.AdminAsync();
        var current = await admin.GetJsonAsync("/api/v1/agency/website/settings");
        var settings = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(current.GetProperty("settings").GetRawText())!;
        var bad = new Dictionary<string, object?>(settings.ToDictionary(kv => kv.Key, kv => (object?)kv.Value))
        {
            ["analytics"] = new { ga4MeasurementId = "UA-123", gtmContainerId = "GTM-ABC1234", metaPixelId = "12345678901" },
            ["announcement"] = new { enabled = true, text = "Hi", linkLabel = "Go", linkUrl = "javascript:alert(1)" },
        };
        var problem = await admin.PutJsonAsync("/api/v1/agency/website/settings",
            new { settings = bad, concurrencyStamp = current.GetProperty("concurrencyStamp").GetGuid() }, 400);
        Assert.True(problem.GetProperty("errors").TryGetProperty("analytics.ga4MeasurementId", out _));
        Assert.True(problem.GetProperty("errors").TryGetProperty("announcement.linkUrl", out _));

        bad["analytics"] = new { ga4MeasurementId = "G-ABC123XYZ9", gtmContainerId = (string?)null, metaPixelId = (string?)null };
        bad["announcement"] = new { enabled = true, text = "New: free audits", linkLabel = "Get one", linkUrl = "/free-audit" };
        await admin.PutJsonAsync("/api/v1/agency/website/settings", new { settings = bad, concurrencyStamp = current.GetProperty("concurrencyStamp").GetGuid() });
        var site = await api.Anonymous().GetJsonAsync("/api/v1/public/site");
        Assert.Equal("G-ABC123XYZ9", site.GetProperty("analytics").GetProperty("ga4MeasurementId").GetString());
        Assert.True(site.GetProperty("announcement").GetProperty("enabled").GetBoolean());
        Assert.True(site.GetProperty("serviceMenu").GetArrayLength() >= 9);
        Assert.True(await api.WithDbAsync(db => db.Set<AuditLog>().AnyAsync(a => a.Action == "website.settings_updated")));
    }
}
