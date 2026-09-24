using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Modules.Website.Leads;
using OptimizeAll.Domain.Audit;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Website;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.Website;

/// <summary>
/// CMS completeness: page version history and scheduled publishing, reordering, newsletter subscriber management,
/// inquiry assignment filters and erasure, job application erasure.
/// </summary>
public sealed class WebsiteCmsCompletenessTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private static object PageBody(string slug, string title, string markdown, bool published, DateTime? publishAt = null, Guid? stamp = null, string? note = null) => new
    {
        slug, title, summary = "Summary", kind = "Legal", sortOrder = 0, isPublished = published, publishAt, revisionNote = note,
        blocks = new[] { new { type = "richText", data = new { markdown } } },
        seo = new { title = (string?)null, description = (string?)null, noIndex = false },
        concurrencyStamp = stamp,
    };

    [Fact]
    public async Task Pages_keep_every_version_and_restore_an_old_one_as_a_new_version()
    {
        var admin = await api.AdminAsync();
        var slug = "data-policy-" + Guid.NewGuid().ToString("N")[..6];
        var created = await admin.PostJsonAsync("/api/v1/agency/website/pages", PageBody(slug, "Data policy", "We keep data for 30 days.", true), 201);
        var id = created.GetProperty("id").GetGuid();
        Assert.Equal(1, created.GetProperty("version").GetInt32());

        var v2 = await admin.PutJsonAsync($"/api/v1/agency/website/pages/{id}",
            PageBody(slug, "Data policy", "We keep data for 90 days.", true, stamp: created.GetProperty("concurrencyStamp").GetGuid(), note: "Retention is now 90 days"));
        Assert.Equal(2, v2.GetProperty("version").GetInt32());

        var history = await admin.GetJsonAsync($"/api/v1/agency/website/pages/{id}/revisions");
        var versions = history.EnumerateArray().ToList();
        Assert.Equal(new[] { 2, 1 }, versions.Select(v => v.GetProperty("version").GetInt32()));
        Assert.Equal("Retention is now 90 days", versions[0].GetProperty("note").GetString());
        Assert.True(versions[0].GetProperty("isCurrent").GetBoolean());
        Assert.NotNull(versions[0].GetProperty("authorName").GetString());

        var old = await admin.GetJsonAsync($"/api/v1/agency/website/pages/{id}/revisions/1");
        Assert.Contains("30 days", old.GetProperty("blocks")[0].GetProperty("data").GetProperty("markdown").GetString());

        // Restoring needs the current stamp (409 otherwise) and cannot target the current version.
        await (await admin.PostAsJsonAsync($"/api/v1/agency/website/pages/{id}/revisions/1/restore", new { concurrencyStamp = Guid.NewGuid() }))
            .ShouldFailAsync(409, "concurrency.conflict");
        await (await admin.PostAsJsonAsync($"/api/v1/agency/website/pages/{id}/revisions/2/restore", new { concurrencyStamp = v2.GetProperty("concurrencyStamp").GetGuid() }))
            .ShouldFailAsync(409, "website.revision_current");
        await (await admin.GetAsync($"/api/v1/agency/website/pages/{id}/revisions/99")).ShouldFailAsync(404);

        var restored = await admin.PostJsonAsync($"/api/v1/agency/website/pages/{id}/revisions/1/restore",
            new { concurrencyStamp = v2.GetProperty("concurrencyStamp").GetGuid() }, 200);
        Assert.Equal(3, restored.GetProperty("version").GetInt32());
        var page = await api.Anonymous().GetJsonAsync($"/api/v1/public/pages/{slug}");
        Assert.Contains("30 days", page.GetRawText());
        var latest = (await admin.GetJsonAsync($"/api/v1/agency/website/pages/{id}/revisions"))[0];
        Assert.Equal("restored", latest.GetProperty("action").GetString());
        Assert.Equal("Restored version 1", latest.GetProperty("note").GetString());

        Assert.Contains("website.page_restored", await api.WithDbAsync(db => db.Set<AuditLog>().AsNoTracking()
            .Where(a => a.EntityType == nameof(SitePage)).Select(a => a.Action).ToListAsync()));

        // Seeded pages created before history existed get their original content recorded as version 0 on first edit.
        var privacy = await api.WithDbAsync(db => db.Set<SitePage>().AsNoTracking().SingleAsync(p => p.Slug == "privacy-policy"));
        var detail = await admin.GetJsonAsync($"/api/v1/agency/website/pages/{privacy.Id}");
        var body = JsonSerializer.SerializeToNode(detail)!.AsObject();
        body["title"] = "Privacy policy (updated)";
        await admin.PutJsonAsync($"/api/v1/agency/website/pages/{privacy.Id}", body);
        var privacyHistory = (await admin.GetJsonAsync($"/api/v1/agency/website/pages/{privacy.Id}/revisions")).EnumerateArray().ToList();
        Assert.Equal(new[] { 1, 0 }, privacyHistory.Select(v => v.GetProperty("version").GetInt32()));
        Assert.Equal("initial", privacyHistory[1].GetProperty("action").GetString());
    }

    [Fact]
    public async Task Scheduled_pages_stay_hidden_until_their_go_live_time()
    {
        var admin = await api.AdminAsync();
        var slug = "launch-" + Guid.NewGuid().ToString("N")[..6];
        var goLive = api.Clock.GetUtcNow().UtcDateTime.AddHours(2);
        var created = await admin.PostJsonAsync("/api/v1/agency/website/pages", PageBody(slug, "Launch", "Coming soon.", true, goLive), 201);
        Assert.NotEqual(JsonValueKind.Null, created.GetProperty("publishAt").ValueKind);

        await (await api.Anonymous().GetAsync($"/api/v1/public/pages/{slug}")).ShouldFailAsync(404);
        Assert.DoesNotContain($"/{slug}", await (await api.Anonymous().GetAsync("/api/v1/public/sitemap.xml")).Content.ReadAsStringAsync());

        api.Clock.Advance(TimeSpan.FromHours(3));
        await api.Anonymous().GetJsonAsync($"/api/v1/public/pages/{slug}");
        Assert.Contains($"/{slug}", await (await api.Anonymous().GetAsync("/api/v1/public/sitemap.xml")).Content.ReadAsStringAsync());

        // A draft never keeps a schedule. (Sign in again: the access token expired while the clock moved on.)
        admin = await api.AdminAsync();
        var draft = await admin.PostJsonAsync("/api/v1/agency/website/pages", PageBody(slug + "-d", "Draft", "x", false, goLive), 201);
        Assert.Equal(JsonValueKind.Null, draft.GetProperty("publishAt").ValueKind);
    }

    [Fact]
    public async Task Industries_case_studies_and_blog_categories_can_be_reordered()
    {
        var admin = await api.AdminAsync();
        var industries = (await admin.GetJsonAsync("/api/v1/agency/website/industries?pageSize=200")).GetProperty("items")
            .EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ToList();
        Assert.True(industries.Count >= 2);
        var reversed = Enumerable.Reverse(industries).ToList();
        var result = await admin.PostJsonAsync("/api/v1/agency/website/industries/reorder", new { ids = reversed }, 200);
        Assert.Equal(industries.Count, result.GetProperty("updated").GetInt32());
        var after = (await admin.GetJsonAsync("/api/v1/agency/website/industries?pageSize=200")).GetProperty("items")
            .EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ToList();
        Assert.Equal(reversed, after);

        await (await admin.PostAsJsonAsync("/api/v1/agency/website/industries/reorder", new { ids = new[] { reversed[0], reversed[0] } }))
            .ShouldFailAsync(400, "website.reorder_duplicates");
        await (await admin.PostAsJsonAsync("/api/v1/agency/website/case-studies/reorder", new { ids = new[] { Guid.NewGuid() } }))
            .ShouldFailAsync(400, "website.reorder_unknown");

        var categories = (await admin.GetJsonAsync("/api/v1/agency/website/blog/categories")).EnumerateArray()
            .Select(c => c.GetProperty("id").GetGuid()).ToList();
        var catReversed = Enumerable.Reverse(categories).ToList();
        await admin.PostJsonAsync("/api/v1/agency/website/blog/categories/reorder", new { ids = catReversed }, 200);
        Assert.Equal(catReversed, (await admin.GetJsonAsync("/api/v1/agency/website/blog/categories")).EnumerateArray()
            .Select(c => c.GetProperty("id").GetGuid()).ToList());

        var (_, writer) = await api.CreateClientAsync(Role.ContentCreator); // blog.write only
        await (await writer.PostAsJsonAsync("/api/v1/agency/website/blog/categories/reorder", new { ids = catReversed })).ShouldFailAsync(403);
        await (await writer.PostAsJsonAsync("/api/v1/agency/website/industries/reorder", new { ids = reversed })).ShouldFailAsync(403);
    }

    [Fact]
    public async Task Staff_unsubscribe_and_erase_newsletter_subscribers()
    {
        var admin = await api.AdminAsync();
        var subscriber = new NewsletterSubscriber
        {
            Email = $"sub-{Guid.NewGuid():N}@example.test", Status = NewsletterStatus.Confirmed, UnsubscribeTokenHash = Guid.NewGuid().ToString("N"),
            ConsentVersion = ConsentTexts.NewsletterVersion, ConsentAt = api.Clock.GetUtcNow().UtcDateTime,
        };
        subscriber.NormalizedEmail = subscriber.Email;
        await api.WithDbAsync(async db =>
        {
            db.Add(subscriber);
            await db.SaveChangesAsync();
        });

        var unsubscribed = await admin.PostJsonAsync($"/api/v1/agency/website/newsletter/subscribers/{subscriber.Id}/unsubscribe", new { }, 200);
        Assert.Equal("Unsubscribed", unsubscribed.GetProperty("status").GetString());
        await admin.PostJsonAsync($"/api/v1/agency/website/newsletter/subscribers/{subscriber.Id}/unsubscribe", new { }, 200); // idempotent

        var (_, marketer) = await api.CreateClientAsync(Role.AccountManager);
        await (await marketer.DeleteAsync($"/api/v1/agency/website/newsletter/subscribers/{subscriber.Id}")).ShouldFailAsync(403);

        Assert.Equal(204, (int)(await admin.DeleteAsync($"/api/v1/agency/website/newsletter/subscribers/{subscriber.Id}")).StatusCode);
        Assert.False(await api.WithDbAsync(db => db.Set<NewsletterSubscriber>().AnyAsync(s => s.Id == subscriber.Id)));
        await (await admin.DeleteAsync($"/api/v1/agency/website/newsletter/subscribers/{subscriber.Id}")).ShouldFailAsync(404);
        var actions = await api.WithDbAsync(db => db.Set<AuditLog>().AsNoTracking()
            .Where(a => a.EntityType == nameof(NewsletterSubscriber)).Select(a => a.Action).ToListAsync());
        Assert.Contains("website.subscriber_unsubscribed", actions);
        Assert.Contains("website.subscriber_deleted", actions);
    }

    [Fact]
    public async Task Inquiries_filter_by_assignee_and_can_be_erased()
    {
        var (adminUser, admin) = await api.CreateClientAsync(Role.Admin);
        var anon = api.Anonymous();
        var email = $"lead-{Guid.NewGuid():N}@example.test";
        await anon.PostJsonAsync("/api/v1/public/inquiries/contact", WebsiteTestKit.Form(await api.FormTokenAsync(anon), WebsiteTestKit.Contact(email)), 202);
        var inquiry = await api.WithDbAsync(db => db.Set<WebsiteInquiry>().AsNoTracking().SingleAsync(i => i.Email == email));

        var detail = await admin.GetJsonAsync($"/api/v1/agency/website/inquiries/{inquiry.Id}");
        await admin.PutJsonAsync($"/api/v1/agency/website/inquiries/{inquiry.Id}", new
        {
            status = "InProgress", assignedToUserId = adminUser.Id, staffNotes = "Called, sending proposal",
            concurrencyStamp = detail.GetProperty("concurrencyStamp").GetGuid(),
        });

        var mine = await admin.GetJsonAsync("/api/v1/agency/website/inquiries?assignedTo=me&pageSize=100");
        Assert.Contains(mine.GetProperty("items").EnumerateArray(), i => i.GetProperty("id").GetGuid() == inquiry.Id);
        var byId = await admin.GetJsonAsync($"/api/v1/agency/website/inquiries?assignedTo={adminUser.Id}&pageSize=100");
        Assert.Contains(byId.GetProperty("items").EnumerateArray(), i => i.GetProperty("id").GetGuid() == inquiry.Id);
        var unassigned = await admin.GetJsonAsync("/api/v1/agency/website/inquiries?assignedTo=unassigned&pageSize=100");
        Assert.DoesNotContain(unassigned.GetProperty("items").EnumerateArray(), i => i.GetProperty("id").GetGuid() == inquiry.Id);
        await (await admin.GetAsync("/api/v1/agency/website/inquiries?assignedTo=somebody")).ShouldFailAsync(400);

        // crm.view can read inquiries but not erase them.
        var (_, sales) = await api.CreateClientAsync(Role.SalesRep);
        await (await sales.DeleteAsync($"/api/v1/agency/website/inquiries/{inquiry.Id}")).ShouldFailAsync(403);

        Assert.Equal(204, (int)(await admin.DeleteAsync($"/api/v1/agency/website/inquiries/{inquiry.Id}")).StatusCode);
        await (await admin.GetAsync($"/api/v1/agency/website/inquiries/{inquiry.Id}")).ShouldFailAsync(404);
        Assert.Contains("website.inquiry_deleted", await api.WithDbAsync(db => db.Set<AuditLog>().AsNoTracking()
            .Where(a => a.EntityType == nameof(WebsiteInquiry)).Select(a => a.Action).ToListAsync()));
    }

    [Fact]
    public async Task Job_applications_can_be_erased_with_their_cv_and_notes()
    {
        var admin = await api.AdminAsync();
        var now = api.Clock.GetUtcNow().UtcDateTime;
        var job = new JobOpening
        {
            Slug = "role-" + Guid.NewGuid().ToString("N")[..6], Title = "SEO lead", Department = "Search", Location = "Remote",
            Summary = "Lead our SEO team.", DescriptionMarkdown = "Details", Status = JobOpeningStatus.Open, PostedAt = now,
        };
        var cv = new CareerCvFile { FileName = "cv.pdf", SizeBytes = 10, Sha256 = "x", Content = WebsiteTestKit.Pdf(), CreatedAt = now };
        var application = new JobApplication
        {
            JobOpeningId = job.Id, Name = "Grace Hopper", Email = "grace@example.test", CvFileId = cv.Id, ConsentAt = now, ConsentVersion = "careers-2026-09",
        };
        await api.WithDbAsync(async db =>
        {
            db.AddRange(job, cv, application);
            await db.SaveChangesAsync();
        });
        await admin.PostJsonAsync($"/api/v1/agency/website/careers/applications/{application.Id}/notes", new { body = "Strong portfolio" }, 200);

        Assert.Equal(204, (int)(await admin.DeleteAsync($"/api/v1/agency/website/careers/applications/{application.Id}")).StatusCode);
        Assert.False(await api.WithDbAsync(db => db.Set<JobApplication>().AnyAsync(a => a.Id == application.Id)));
        Assert.False(await api.WithDbAsync(db => db.Set<CareerCvFile>().AnyAsync(f => f.Id == cv.Id)));
        Assert.False(await api.WithDbAsync(db => db.Set<JobApplicationNote>().AnyAsync(n => n.ApplicationId == application.Id)));
        await (await admin.DeleteAsync($"/api/v1/agency/website/careers/applications/{application.Id}")).ShouldFailAsync(404);
    }

    [Theory]
    [InlineData("verify-email")]
    [InlineData("reset-password")]
    [InlineData("forgot-password")]
    [InlineData("check-email")]
    [InlineData("design-system")]
    [InlineData("lp")]
    [InlineData("f")]
    public async Task Pages_cannot_take_the_address_of_a_built_in_route(string slug)
    {
        // A published page at /verify-email or /lp would never render (the app's own route wins) while the sitemap listed
        // it; the slug is refused up front.
        var admin = await api.AdminAsync();
        await (await admin.PostAsJsonAsync("/api/v1/agency/website/pages", PageBody(slug, "Shadowed", "Never reachable.", true)))
            .ShouldFailAsync(400, "website.invalid");
        var sitemap = await (await api.Anonymous().GetAsync("/api/v1/public/sitemap.xml")).Content.ReadAsStringAsync();
        Assert.DoesNotMatch($"<loc>[^<]*/{slug}</loc>", sitemap);
    }
}
