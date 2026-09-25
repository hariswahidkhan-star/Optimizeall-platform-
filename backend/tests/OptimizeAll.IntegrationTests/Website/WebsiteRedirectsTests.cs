using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Domain.Audit;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Website;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.Website;

/// <summary>
/// Redirects of old public addresses: recorded automatically when the slug of live content changes (pages, posts, services,
/// service lines, case studies, industries), chains collapsed, loops impossible, a redirect dropped when live content
/// claims its address again, the sitemap listing only the new address, manual redirects (site.manage, audited) and the
/// public lookup and the server-rendered pages (real 301 from /_document).
/// </summary>
public sealed class WebsiteRedirectsTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private const string Pages = "/api/v1/agency/website/pages";
    private const string Redirects = "/api/v1/agency/website/redirects";

    private static object Page(string slug, bool published, Guid? stamp = null) => new
    {
        slug, title = "Page " + slug, kind = "Standard", isPublished = published,
        blocks = new[] { new { type = "richText", data = new { markdown = "Content of " + slug } } },
        concurrencyStamp = stamp,
    };

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid().ToString("N")[..6]}";

    /// <summary>The public lookup: the Location a moved address redirects to, or null (404).</summary>
    private async Task<string?> LookupAsync(string path)
    {
        var response = await api.Anonymous().GetAsync("/api/v1/public/redirects?path=" + Uri.EscapeDataString(path));
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(301, body.GetProperty("statusCode").GetInt32());
        return body.GetProperty("location").GetString();
    }

    /// <summary>
    /// What the web server gets for a full page load of a request target: nginx (@document) and the Vite dev/preview server
    /// (seoShell) render every public page through <c>/_document{target}</c>, which answers a moved address with a real
    /// 301 (status, Location).
    /// </summary>
    private async Task<(HttpStatusCode Status, string? Location)> DocumentAsync(string requestTarget, HttpMethod? method = null)
    {
        var client = api.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var request = new HttpRequestMessage(method ?? HttpMethod.Get, "/_document" + requestTarget);
        var response = await client.SendAsync(request);
        return (response.StatusCode, response.Headers.Location?.OriginalString);
    }

    /// <summary>
    /// Every sitemap the crawlers get: the sitemap index (/sitemap.xml) and each sitemap it lists (/sitemaps/*.xml),
    /// concatenated, so an address is "in the sitemap" when any of them lists it.
    /// </summary>
    private async Task<string> SitemapAsync()
    {
        var anonymous = api.Anonymous();
        var index = await anonymous.GetStringAsync("/sitemap.xml");
        var files = System.Text.RegularExpressions.Regex.Matches(index, "<loc>([^<]+)</loc>").Select(m => new Uri(m.Groups[1].Value).AbsolutePath).ToList();
        Assert.NotEmpty(files);
        Assert.All(files, f => Assert.StartsWith("/sitemaps/", f));
        var all = new System.Text.StringBuilder();
        foreach (var file in files) all.Append(await anonymous.GetStringAsync(file));
        return all.ToString();
    }

    private async Task<JsonElement> RenamePageAsync(HttpClient admin, JsonElement page, string slug, bool? published = null) =>
        await admin.PutJsonAsync($"{Pages}/{page.GetProperty("id").GetGuid()}",
            Page(slug, published ?? page.GetProperty("isPublished").GetBoolean(), page.GetProperty("concurrencyStamp").GetGuid()));

    [Fact]
    public async Task Renaming_a_live_page_redirects_the_old_address_collapses_chains_and_never_loops()
    {
        var admin = await api.AdminAsync();
        var (a, b, c) = (Unique("about-us"), Unique("who-we-are"), Unique("our-team-story"));
        var page = await admin.PostJsonAsync(Pages, Page(a, true), 201);

        page = await RenamePageAsync(admin, page, b);
        Assert.Equal("/" + b, await LookupAsync("/" + a));
        await api.Anonymous().GetJsonAsync($"/api/v1/public/pages/{a}", 404);
        await api.Anonymous().GetJsonAsync($"/api/v1/public/pages/{b}");
        // The web server gets a real 301 for full page loads; UTM tags and other query parameters are carried over.
        Assert.Equal((HttpStatusCode.MovedPermanently, $"/{b}?utm_source=news&utm_medium=email"),
            await DocumentAsync($"/{a}?utm_source=news&utm_medium=email"));
        Assert.Equal((HttpStatusCode.MovedPermanently, "/" + b), await DocumentAsync($"/{a.ToUpperInvariant()}/", HttpMethod.Head));
        // The sitemap lists only the new address.
        var sitemap = await SitemapAsync();
        Assert.Contains($"/{b}</loc>", sitemap);
        Assert.DoesNotContain($"/{a}</loc>", sitemap);

        // A → B → C: every old address points straight at the final one (no chains).
        page = await RenamePageAsync(admin, page, c);
        Assert.Equal("/" + c, await LookupAsync("/" + a));
        Assert.Equal("/" + c, await LookupAsync("/" + b));

        // Back to A: the address is live again, so its redirect is dropped and nothing loops.
        page = await RenamePageAsync(admin, page, a);
        Assert.Null(await LookupAsync("/" + a));
        Assert.Equal((HttpStatusCode.OK, null), await DocumentAsync("/" + a)); // served, not redirected
        Assert.Equal("/" + a, await LookupAsync("/" + b));
        Assert.Equal("/" + a, await LookupAsync("/" + c));
        var rows = await api.WithDbAsync(db => db.Set<SiteRedirect>().AsNoTracking()
            .Where(r => r.FromPath == "/" + a || r.FromPath == "/" + b || r.FromPath == "/" + c).ToListAsync());
        Assert.Equal(new[] { "/" + b, "/" + c }.Order(), rows.Select(r => r.FromPath).Order());
        Assert.All(rows, r => Assert.Equal(("/" + a, SiteRedirectSource.Automatic, "page", page.GetProperty("id").GetGuid()),
            (r.ToPath, r.Source, r.ContentType, r.ContentId!.Value)));
        Assert.DoesNotContain(rows, r => r.FromPath == r.ToPath);

        // Listed for staff and audited.
        var list = await admin.GetJsonAsync($"{Redirects}?search={b}");
        Assert.Equal("Automatic", list.GetProperty("items")[0].GetProperty("source").GetString());
        var actions = await api.WithDbAsync(db => db.Set<AuditLog>().AsNoTracking().Where(l => l.EntityType == nameof(SiteRedirect)).Select(l => l.Action).ToListAsync());
        Assert.Contains("website.redirect_created", actions);
        Assert.Contains("website.redirect_updated", actions);
        Assert.Contains("website.redirect_removed", actions);
    }

    [Fact]
    public async Task Drafts_and_unpublished_content_record_no_redirect_and_new_content_claims_a_redirected_address()
    {
        var admin = await api.AdminAsync();
        var (draftA, draftB) = (Unique("draft-a"), Unique("draft-b"));
        var draft = await admin.PostJsonAsync(Pages, Page(draftA, false), 201);
        await RenamePageAsync(admin, draft, draftB);
        Assert.Null(await LookupAsync("/" + draftA));

        // Renaming while unpublishing: the old address was live but the new one is not, so there is nothing to send visitors to.
        var (liveA, liveB) = (Unique("live-a"), Unique("live-b"));
        var live = await admin.PostJsonAsync(Pages, Page(liveA, true), 201);
        await RenamePageAsync(admin, live, liveB, published: false);
        Assert.Null(await LookupAsync("/" + liveA));

        // A new page published at a redirected address takes it over: the redirect is removed.
        var (oldSlug, newSlug) = (Unique("pricing-guide"), Unique("pricing-explained"));
        var moved = await admin.PostJsonAsync(Pages, Page(oldSlug, true), 201);
        await RenamePageAsync(admin, moved, newSlug);
        Assert.Equal("/" + newSlug, await LookupAsync("/" + oldSlug));
        await admin.PostJsonAsync(Pages, Page(oldSlug, true), 201);
        Assert.Null(await LookupAsync("/" + oldSlug));
        Assert.False(await api.WithDbAsync(db => db.Set<SiteRedirect>().AnyAsync(r => r.FromPath == "/" + oldSlug)));
        await api.Anonymous().GetJsonAsync($"/api/v1/public/pages/{oldSlug}");
    }

    [Fact]
    public async Task Renamed_posts_services_service_lines_case_studies_and_industries_redirect()
    {
        var admin = await api.AdminAsync();

        // Blog post: renaming a live post.
        var body = string.Join(" ", Enumerable.Repeat("Redirects keep old links and search rankings working after a rename.", 4));
        var (postA, postB) = (Unique("post-a"), Unique("post-b"));
        var post = await admin.PostJsonAsync("/api/v1/agency/website/blog/posts", new { slug = postA, title = "Post", excerpt = "Excerpt.", bodyMarkdown = body }, 201);
        post = await admin.PostJsonAsync($"/api/v1/agency/website/blog/posts/{post.GetProperty("id").GetGuid()}/publish",
            new { concurrencyStamp = post.GetProperty("concurrencyStamp").GetGuid() }, 200);
        await admin.PutJsonAsync($"/api/v1/agency/website/blog/posts/{post.GetProperty("id").GetGuid()}",
            new { slug = postB, title = "Post", excerpt = "Excerpt.", bodyMarkdown = body, concurrencyStamp = post.GetProperty("concurrencyStamp").GetGuid() });
        Assert.Equal($"/blog/{postB}", await LookupAsync($"/blog/{postA}"));
        var sitemap = await SitemapAsync();
        Assert.Contains($"/blog/{postB}</loc>", sitemap);
        Assert.DoesNotContain($"/blog/{postA}</loc>", sitemap);

        // Service line and service.
        var (lineA, lineB) = (Unique("line-a"), Unique("line-b"));
        var line = await admin.PostJsonAsync("/api/v1/agency/website/service-categories", new { slug = lineA, name = "Line", isPublished = true }, 201);
        var (svcA, svcB) = (Unique("svc-a"), Unique("svc-b"));
        object Service(string slug, Guid? stamp) => new
        {
            categoryId = line.GetProperty("id").GetGuid(), slug, name = "Service " + slug, tagline = "Tagline", isPublished = true,
            overviewMarkdown = "An overview.", deliverables = new[] { "A deliverable" }, concurrencyStamp = stamp,
        };
        var service = await admin.PostJsonAsync("/api/v1/agency/website/services", Service(svcA, null), 201);
        await admin.PutJsonAsync($"/api/v1/agency/website/services/{service.GetProperty("id").GetGuid()}", Service(svcB, service.GetProperty("concurrencyStamp").GetGuid()));
        Assert.Equal($"/services/{svcB}", await LookupAsync($"/services/{svcA}"));
        await admin.PutJsonAsync($"/api/v1/agency/website/service-categories/{line.GetProperty("id").GetGuid()}",
            new { slug = lineB, name = "Line", isPublished = true, concurrencyStamp = line.GetProperty("concurrencyStamp").GetGuid() });
        Assert.Equal($"/services?category={lineB}", await LookupAsync($"/services?category={lineA}"));
        Assert.Equal((HttpStatusCode.MovedPermanently, $"/services?category={lineB}&utm_source=ads"),
            await DocumentAsync($"/services?utm_source=ads&category={lineA}"));
        // The services overview itself is a built-in page and never redirected.
        Assert.Equal((HttpStatusCode.OK, null), await DocumentAsync("/services"));

        // Case study and industry.
        var (caseA, caseB) = (Unique("case-a"), Unique("case-b"));
        object CaseStudy(string slug, Guid? stamp) => new
        {
            slug, title = "Case", clientName = "Acme", summary = "Results.", isPublished = true, concurrencyStamp = stamp,
            metrics = new[] { new { label = "Leads", value = "+50%", measurement = "Measured" } },
        };
        var cs = await admin.PostJsonAsync("/api/v1/agency/website/case-studies", CaseStudy(caseA, null), 201);
        await admin.PutJsonAsync($"/api/v1/agency/website/case-studies/{cs.GetProperty("id").GetGuid()}", CaseStudy(caseB, cs.GetProperty("concurrencyStamp").GetGuid()));
        Assert.Equal($"/case-studies/{caseB}", await LookupAsync($"/case-studies/{caseA}"));

        var (indA, indB) = (Unique("ind-a"), Unique("ind-b"));
        var industry = await admin.PostJsonAsync("/api/v1/agency/website/industries", new { slug = indA, name = "Industry", summary = "Summary.", isPublished = true }, 201);
        await admin.PutJsonAsync($"/api/v1/agency/website/industries/{industry.GetProperty("id").GetGuid()}",
            new { slug = indB, name = "Industry", summary = "Summary.", isPublished = true, concurrencyStamp = industry.GetProperty("concurrencyStamp").GetGuid() });
        Assert.Equal($"/industries/{indB}", await LookupAsync($"/industries/{indA}"));

        var types = await api.WithDbAsync(db => db.Set<SiteRedirect>().AsNoTracking().Select(r => r.ContentType).Distinct().ToListAsync());
        Assert.Superset(new HashSet<string?> { "post", "service", "service-line", "case-study", "industry" }, types.ToHashSet());
    }

    [Fact]
    public async Task A_post_unpublished_renamed_and_published_again_redirects_its_old_live_address()
    {
        var admin = await api.AdminAsync();
        const string posts = "/api/v1/agency/website/blog/posts";
        var body = string.Join(" ", Enumerable.Repeat("An unpublished rename must not break the links to the address that was live.", 3));
        object Post(string slug, Guid? stamp = null) => new { slug, title = "Post", excerpt = "Excerpt.", bodyMarkdown = body, concurrencyStamp = stamp };
        async Task<JsonElement> ActAsync(JsonElement p, string action, object? extra = null) =>
            await admin.PostJsonAsync($"{posts}/{p.GetProperty("id").GetGuid()}/{action}",
                extra ?? new { concurrencyStamp = p.GetProperty("concurrencyStamp").GetGuid() }, 200);
        async Task<JsonElement> RenameAsync(JsonElement p, string slug) =>
            await admin.PutJsonAsync($"{posts}/{p.GetProperty("id").GetGuid()}", Post(slug, p.GetProperty("concurrencyStamp").GetGuid()));

        // Live at A → unpublished → renamed to B (nothing live to redirect to yet) → published again at B: A redirects to B.
        var (a, b, c) = (Unique("post-live"), Unique("post-renamed"), Unique("post-again"));
        var post = await admin.PostJsonAsync(posts, Post(a), 201);
        post = await ActAsync(post, "publish");
        // An older address that already redirected to A follows the post to its new address (no chain).
        var older = Unique("post-older");
        await admin.PostJsonAsync(Redirects, new { fromPath = $"/blog/{older}", toPath = $"/blog/{a}" }, 201);
        post = await ActAsync(post, "unpublish");
        post = await RenameAsync(post, b);
        Assert.Null(await LookupAsync($"/blog/{a}"));
        post = await ActAsync(post, "publish");
        Assert.Equal($"/blog/{b}", await LookupAsync($"/blog/{a}"));
        Assert.Equal($"/blog/{b}", await LookupAsync($"/blog/{older}"));
        Assert.Equal((HttpStatusCode.MovedPermanently, $"/blog/{b}"), await DocumentAsync($"/blog/{a}"));
        var sitemap = await SitemapAsync();
        Assert.Contains($"/blog/{b}</loc>", sitemap);
        Assert.DoesNotContain($"/blog/{a}</loc>", sitemap);

        // The same through a return to draft and the scheduler: the job publishing the post records the redirect too.
        post = await ActAsync(post, "return-to-draft");
        post = await RenameAsync(post, c);
        post = await ActAsync(post, "schedule", new { concurrencyStamp = post.GetProperty("concurrencyStamp").GetGuid(), publishAt = api.Clock.GetUtcNow().UtcDateTime.AddHours(1) });
        Assert.Null(await LookupAsync($"/blog/{c}"));
        api.Clock.Advance(TimeSpan.FromHours(2));
        await api.RunJobAsync<OptimizeAll.Api.Modules.Website.Blog.BlogSchedulerJob>();
        Assert.Equal($"/blog/{c}", await LookupAsync($"/blog/{b}"));
        Assert.Equal($"/blog/{c}", await LookupAsync($"/blog/{a}"));
        Assert.Equal($"/blog/{c}", await LookupAsync($"/blog/{older}"));
        await api.Anonymous().GetJsonAsync($"/api/v1/public/blog/{c}");
        admin = await api.AdminAsync(); // the clock moved past the access token's lifetime

        // An old address another post has taken since is left to that post.
        var (x, y) = (Unique("post-x"), Unique("post-y"));
        var first = await ActAsync(await admin.PostJsonAsync(posts, Post(x), 201), "publish");
        first = await ActAsync(first, "unpublish");
        first = await RenameAsync(first, y);
        var newcomer = await ActAsync(await admin.PostJsonAsync(posts, Post(x), 201), "publish");
        await ActAsync(first, "publish");
        Assert.Null(await LookupAsync($"/blog/{x}"));
        Assert.False(await api.WithDbAsync(db => db.Set<SiteRedirect>().AnyAsync(r => r.FromPath == $"/blog/{x}")));
        Assert.Equal(x, newcomer.GetProperty("slug").GetString());
    }

    [Fact]
    public async Task Staff_add_and_delete_manual_redirects_with_validation_collapse_and_loop_prevention()
    {
        var admin = await api.AdminAsync();
        var (old, target) = ("/" + Unique("old-offer"), "/" + Unique("current-offer"));

        // Built-in pages, the portals and other sites cannot be used; addresses are normalized.
        foreach (var (from, to, field) in new[]
                 {
                     ("/services", "/x", "fromPath"), ("/agency/website", "/x", "fromPath"), ("/", "/x", "fromPath"),
                     ("https://evil.example/x", "/x", "fromPath"), (old, "https://evil.example/", "toPath"), (old, "//evil.example/", "toPath"),
                 })
        {
            var problem = await admin.PostJsonAsync(Redirects, new { fromPath = from, toPath = to }, 400);
            Assert.True(problem.GetProperty("errors").TryGetProperty(field, out _), $"{from} → {to}");
        }

        var created = await admin.PostJsonAsync(Redirects, new { fromPath = old.ToUpperInvariant() + "/", toPath = target }, 201);
        Assert.Equal((old, target, "Manual"), (created.GetProperty("fromPath").GetString(), created.GetProperty("toPath").GetString(),
            created.GetProperty("source").GetString()));
        Assert.Equal(target, await LookupAsync(old));
        Assert.Equal("website.redirect_exists", (await admin.PostJsonAsync(Redirects, new { fromPath = old, toPath = "/elsewhere" }, 409)).Code());

        // A → B exists; B → A would loop.
        Assert.Equal("website.redirect_loop", (await admin.PostJsonAsync(Redirects, new { fromPath = target, toPath = old }, 409)).Code());
        // X → A is collapsed to X → B (A is itself redirected).
        var viaOld = "/" + Unique("older-offer");
        var collapsed = await admin.PostJsonAsync(Redirects, new { fromPath = viaOld, toPath = old }, 201);
        Assert.Equal(target, collapsed.GetProperty("toPath").GetString());
        // Adding B → C re-points everything that led to B.
        var final = "/" + Unique("final-offer");
        await admin.PostJsonAsync(Redirects, new { fromPath = target, toPath = final }, 201);
        Assert.Equal(final, await LookupAsync(old));
        Assert.Equal(final, await LookupAsync(viaOld));

        // An address that shows published content cannot be redirected.
        var liveSlug = Unique("live-page");
        await admin.PostJsonAsync(Pages, Page(liveSlug, true), 201);
        Assert.Equal("website.redirect_source_live", (await admin.PostJsonAsync(Redirects, new { fromPath = "/" + liveSlug, toPath = "/x" }, 409)).Code());

        // Delete.
        var response = await admin.DeleteAsync($"{Redirects}/{created.GetProperty("id").GetGuid()}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Null(await LookupAsync(old));
        await (await admin.DeleteAsync($"{Redirects}/{created.GetProperty("id").GetGuid()}")).ShouldFailAsync(404);
        var actions = await api.WithDbAsync(db => db.Set<AuditLog>().AsNoTracking()
            .Where(l => l.EntityType == nameof(SiteRedirect) && l.EntityId == created.GetProperty("id").GetGuid().ToString()).Select(l => l.Action).ToListAsync());
        // Created, re-pointed when its target was redirected (collapse), deleted.
        Assert.Equal(new[] { "website.redirect_created", "website.redirect_deleted", "website.redirect_updated" }, actions.Order());
    }

    [Fact]
    public async Task Portal_and_unknown_addresses_are_never_redirected_and_redirects_need_site_manage()
    {
        Assert.Equal((HttpStatusCode.OK, null), await DocumentAsync("/agency/website/pages")); // the portal shell (noindex), never a redirect
        Assert.Equal((HttpStatusCode.NotFound, null), await DocumentAsync("/no-such-page-" + Guid.NewGuid().ToString("N")));
        Assert.Equal((HttpStatusCode.OK, null), await DocumentAsync("/")); // the home page
        Assert.Null(await LookupAsync("//evil.example"));

        var (_, writer) = await api.CreateClientAsync(Role.ContentCreator);
        await (await writer.GetAsync(Redirects)).ShouldFailAsync(403);
        await (await writer.PostAsJsonAsync(Redirects, new { fromPath = "/a", toPath = "/b" })).ShouldFailAsync(403);
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.Anonymous().GetAsync(Redirects)).StatusCode);
    }
}
