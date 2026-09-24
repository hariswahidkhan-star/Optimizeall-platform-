using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OptimizeAll.Api.Common.Jobs;
using OptimizeAll.Api.Modules.LandingPages;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.LandingPages;
using OptimizeAll.Domain.Identity;
using OptimizeAll.IntegrationTests.Infrastructure;
using OptimizeAll.IntegrationTests.Seo;

namespace OptimizeAll.IntegrationTests.LandingPages;

public sealed class LandingPagesApiTests(LandingPagesFixture fx) : IClassFixture<LandingPagesFixture>
{
    private sealed record Created(Guid ClientId, string ClientSlug, Guid PageId, string Slug, Guid FormId, JsonElement Page);

    private async Task<Created> CreatePageAsync(HttpClient staff, string template = "lead-generation", bool publish = true)
    {
        var client = await fx.Api.CreateClientAccountAsync();
        var page = await (await staff.PostAsJsonAsync("/api/v1/agency/pages/landing-pages", new
        {
            clientAccountId = client.Id, name = "Spring Offer", templateKey = template,
        })).ReadJsonAsync();
        var id = page.GetProperty("id").GetGuid();
        var formId = page.GetProperty("variants")[0].GetProperty("blocks").EnumerateArray()
            .Single(b => b.GetProperty("type").GetString() == "form").GetProperty("props").GetProperty("formId").GetGuid();
        if (publish) page = await (await staff.PostAsync($"/api/v1/agency/pages/landing-pages/{id}/publish", null)).ReadJsonAsync();
        return new Created(client.Id, client.Slug, id, page.GetProperty("slug").GetString()!, formId, page);
    }

    private static JsonNode Draft(JsonElement page, Action<JsonArray>? editVariants = null, bool experiment = false)
    {
        var variants = JsonNode.Parse(page.GetProperty("variants").GetRawText())!.AsArray();
        editVariants?.Invoke(variants);
        return new JsonObject
        {
            ["name"] = page.GetProperty("name").GetString(), ["slug"] = page.GetProperty("slug").GetString(),
            ["metaTitle"] = "Spring offer", ["metaDescription"] = "Save on spring plans.", ["noIndex"] = false,
            ["experimentEnabled"] = experiment, ["variants"] = variants,
            ["concurrencyStamp"] = page.GetProperty("concurrencyStamp").GetGuid().ToString(),
        };
    }

    private static void SetHeadline(JsonArray variants, int index, string headline) =>
        variants[index]!["blocks"]!.AsArray().First(b => b!["type"]!.GetValue<string>() == "hero")!["props"]!["headline"] = headline;

    private static StringContent JsonBody(JsonNode node) => new(node.ToJsonString(), Encoding.UTF8, "application/json");

    private static string HeroHeadline(JsonElement publicPage) =>
        publicPage.GetProperty("blocks").EnumerateArray().First(b => b.GetProperty("type").GetString() == "hero").GetProperty("props").GetProperty("headline").GetString()!;

    [Fact]
    public async Task Staff_api_needs_forms_manage()
    {
        var seo = await fx.StaffAsync(Role.SeoSpecialist);
        var client = await fx.StaffAsync(Role.Client);
        var anonymous = fx.Host.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        foreach (var path in new[] { "/api/v1/agency/pages/landing-pages", "/api/v1/agency/pages/forms", "/api/v1/agency/pages/templates", "/api/v1/agency/pages/form-templates" })
        {
            await (await seo.GetAsync(path)).ShouldFailAsync(403);
            await (await client.GetAsync(path)).ShouldFailAsync(403);
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(path)).StatusCode);
        }
        var designer = await fx.StaffAsync();
        var templates = await (await designer.GetAsync("/api/v1/agency/pages/templates")).ReadJsonAsync();
        Assert.Equal(6, templates.GetArrayLength());
        Assert.Equal(4, (await (await designer.GetAsync("/api/v1/agency/pages/form-templates")).ReadJsonAsync()).GetArrayLength());
    }

    [Fact]
    public async Task Publish_snapshots_are_immutable_and_drafts_stay_private()
    {
        var staff = await fx.StaffAsync();
        var created = await CreatePageAsync(staff, publish: false);
        var visitor = fx.Anonymous(visitorId: "visitor-snap");
        await (await visitor.GetAsync($"/api/v1/public/lp/{created.ClientSlug}/{created.Slug}")).ShouldFailAsync(404);

        var v1Page = await (await staff.PostAsync($"/api/v1/agency/pages/landing-pages/{created.PageId}/publish", null)).ReadJsonAsync();
        var original = HeroHeadline(await (await visitor.GetAsync($"/api/v1/public/lp/{created.ClientSlug}/{created.Slug}")).ReadJsonAsync());

        var edited = await staff.PutAsync($"/api/v1/agency/pages/landing-pages/{created.PageId}",
            JsonBody(Draft(v1Page, v => SetHeadline(v, 0, "A brand new headline"))));
        var draft = await edited.ReadJsonAsync();
        Assert.True(draft.GetProperty("hasUnpublishedChanges").GetBoolean());
        // Visitors still see version 1.
        Assert.Equal(original, HeroHeadline(await (await visitor.GetAsync($"/api/v1/public/lp/{created.ClientSlug}/{created.Slug}")).ReadJsonAsync()));

        var v1 = await fx.WithDbAsync(db => db.Set<LandingPageVersion>().AsNoTracking().SingleAsync(v => v.PageId == created.PageId));
        (await staff.PostAsync($"/api/v1/agency/pages/landing-pages/{created.PageId}/publish", null)).EnsureSuccessStatusCode();
        Assert.Equal("A brand new headline", HeroHeadline(await (await visitor.GetAsync($"/api/v1/public/lp/{created.ClientSlug}/{created.Slug}")).ReadJsonAsync()));

        var versions = await (await staff.GetAsync($"/api/v1/agency/pages/landing-pages/{created.PageId}/versions")).ReadJsonAsync();
        Assert.Equal(new[] { 2, 1 }, versions.EnumerateArray().Select(v => v.GetProperty("version").GetInt32()));
        var v1Again = await fx.WithDbAsync(db => db.Set<LandingPageVersion>().AsNoTracking().SingleAsync(v => v.Id == v1.Id));
        Assert.Equal(v1.SnapshotJson, v1Again.SnapshotJson);
        Assert.Equal(Normalization.Sha256Hex(v1Again.SnapshotJson), v1Again.ContentHash);
        var v1Detail = await (await staff.GetAsync($"/api/v1/agency/pages/landing-pages/{created.PageId}/versions/{v1.Id}")).ReadJsonAsync();
        Assert.Contains(original, v1Detail.GetProperty("snapshot").GetProperty("variants").GetRawText());
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await staff.PutAsJsonAsync($"/api/v1/agency/pages/landing-pages/{created.PageId}/versions/{v1.Id}", new { })).StatusCode);

        // Unpublishing takes the page offline; archived pages 404 too.
        (await staff.PostAsync($"/api/v1/agency/pages/landing-pages/{created.PageId}/unpublish", null)).EnsureSuccessStatusCode();
        await (await visitor.GetAsync($"/api/v1/public/lp/{created.ClientSlug}/{created.Slug}")).ShouldFailAsync(404);
    }

    [Fact]
    public async Task A_slug_rename_is_part_of_the_draft_and_goes_live_only_when_published()
    {
        var staff = await fx.StaffAsync();
        var created = await CreatePageAsync(staff);
        var visitor = fx.Anonymous(visitorId: "visitor-rename");
        var oldPath = $"/api/v1/public/lp/{created.ClientSlug}/{created.Slug}";
        var newSlug = created.Slug + "-renamed";
        var newPath = $"/api/v1/public/lp/{created.ClientSlug}/{newSlug}";

        var draft = Draft(created.Page);
        draft["slug"] = newSlug;
        var saved = await (await staff.PutAsync($"/api/v1/agency/pages/landing-pages/{created.PageId}", JsonBody(draft))).ReadJsonAsync();
        Assert.Equal(newSlug, saved.GetProperty("slug").GetString());
        // Visitors (and "View live") still use the published address; the new one is not live yet.
        (await visitor.GetAsync(oldPath)).EnsureSuccessStatusCode();
        await (await visitor.GetAsync(newPath)).ShouldFailAsync(404);
        Assert.Equal($"/lp/{created.ClientSlug}/{created.Slug}", saved.GetProperty("publicPath").GetString());
        var list = await (await staff.GetAsync($"/api/v1/agency/pages/landing-pages?clientId={created.ClientId}")).ReadJsonAsync();
        Assert.Equal($"/lp/{created.ClientSlug}/{created.Slug}", list.GetProperty("items")[0].GetProperty("publicPath").GetString());
        // The live address stays reserved while the rename is pending.
        await (await staff.PostAsJsonAsync("/api/v1/agency/pages/landing-pages", new { clientAccountId = created.ClientId, name = "Squatter", slug = created.Slug }))
            .ShouldFailAsync(409, "landing.slug_taken");

        var published = await (await staff.PostAsync($"/api/v1/agency/pages/landing-pages/{created.PageId}/publish", null)).ReadJsonAsync();
        Assert.Equal($"/lp/{created.ClientSlug}/{newSlug}", published.GetProperty("publicPath").GetString());
        (await visitor.GetAsync(newPath)).EnsureSuccessStatusCode();
        await (await visitor.GetAsync(oldPath)).ShouldFailAsync(404);
        // Once the rename is live, the old address is free again.
        (await staff.PostAsJsonAsync("/api/v1/agency/pages/landing-pages", new { clientAccountId = created.ClientId, name = "Reuse", slug = created.Slug }))
            .EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Unsafe_block_content_is_rejected_on_save()
    {
        var staff = await fx.StaffAsync();
        var created = await CreatePageAsync(staff, publish: false);
        var script = await staff.PutAsync($"/api/v1/agency/pages/landing-pages/{created.PageId}",
            JsonBody(Draft(created.Page, v => SetHeadline(v, 0, "<script>alert(document.cookie)</script>"))));
        await script.ShouldFailAsync(400, "landing.invalid_content");
        var body = await script.Content.ReadAsStringAsync();
        Assert.Contains("variants[0].blocks[0].props.headline", body);

        var jsLink = await staff.PutAsync($"/api/v1/agency/pages/landing-pages/{created.PageId}", JsonBody(Draft(created.Page, v =>
            v[0]!["blocks"]!.AsArray().Add(new JsonObject
            {
                ["id"] = "evil", ["type"] = "cta",
                ["props"] = new JsonObject { ["heading"] = "Click", ["buttonLabel"] = "Go", ["buttonHref"] = "javascript:alert(1)" },
            }))));
        await jsLink.ShouldFailAsync(400, "landing.invalid_content");

        var frame = await staff.PutAsync($"/api/v1/agency/pages/landing-pages/{created.PageId}", JsonBody(Draft(created.Page, v =>
            v[0]!["blocks"]!.AsArray().Add(new JsonObject
            {
                ["id"] = "vid", ["type"] = "video",
                ["props"] = new JsonObject { ["url"] = "https://evil.example/embed/1", ["title"] = "Video" },
            }))));
        await frame.ShouldFailAsync(400, "landing.invalid_content");

        await (await staff.PutAsync($"/api/v1/agency/pages/landing-pages/{created.PageId}", JsonBody(Draft(created.Page, experiment: true))))
            .ShouldFailAsync(400, "landing.experiment_needs_variants");
    }

    [Fact]
    public async Task Ab_assignment_is_sticky_per_visitor_and_bots_are_not_counted()
    {
        var staff = await fx.StaffAsync();
        var created = await CreatePageAsync(staff);
        var withB = Draft(created.Page, v =>
        {
            var b = JsonNode.Parse(v[0]!.ToJsonString())!.AsObject();
            b["key"] = "B";
            b["name"] = "Urgency";
            v.Add(b);
            SetHeadline(v, 1, "Only 3 days left to claim your plan");
        }, experiment: true);
        (await staff.PutAsync($"/api/v1/agency/pages/landing-pages/{created.PageId}", JsonBody(withB))).EnsureSuccessStatusCode();
        (await staff.PostAsync($"/api/v1/agency/pages/landing-pages/{created.PageId}/publish", null)).EnsureSuccessStatusCode();

        var path = $"/api/v1/public/lp/{created.ClientSlug}/{created.Slug}";
        var seen = new Dictionary<string, string>();
        for (var i = 0; i < 40; i++)
        {
            var visitor = fx.Anonymous(visitorId: $"visitor-{i}");
            var first = (await (await visitor.GetAsync(path)).ReadJsonAsync()).GetProperty("variantKey").GetString()!;
            for (var repeat = 0; repeat < 2; repeat++)
                Assert.Equal(first, (await (await visitor.GetAsync(path)).ReadJsonAsync()).GetProperty("variantKey").GetString());
            seen[$"visitor-{i}"] = first;
        }
        Assert.Contains("A", seen.Values);
        Assert.Contains("B", seen.Values);
        var bVisitor = seen.First(s => s.Value == "B").Key;
        Assert.Equal("Only 3 days left to claim your plan", HeroHeadline(await (await fx.Anonymous(visitorId: bVisitor).GetAsync(path)).ReadJsonAsync()));

        var assignments = await fx.WithDbAsync(db => db.Set<LandingPageAssignment>().CountAsync(a => a.PageId == created.PageId));
        Assert.Equal(40, assignments);
        var views = await fx.WithDbAsync(db => db.Set<LandingPageView>().CountAsync(v => v.PageId == created.PageId));
        Assert.Equal(121, views); // 40 × 3 + the B re-check

        var bot = fx.Anonymous(visitorId: "crawler-1", userAgent: "Mozilla/5.0 (compatible; Googlebot/2.1; +http://www.google.com/bot.html)");
        Assert.Equal("A", (await (await bot.GetAsync(path)).ReadJsonAsync()).GetProperty("variantKey").GetString());
        Assert.Equal(121, await fx.WithDbAsync(db => db.Set<LandingPageView>().CountAsync(v => v.PageId == created.PageId)));
        Assert.Equal(40, await fx.WithDbAsync(db => db.Set<LandingPageAssignment>().CountAsync(a => a.PageId == created.PageId)));

        var analytics = await (await staff.GetAsync($"/api/v1/agency/pages/landing-pages/{created.PageId}/analytics")).ReadJsonAsync();
        Assert.True(analytics.GetProperty("experimentEnabled").GetBoolean());
        var variants = analytics.GetProperty("variants").EnumerateArray().ToList();
        Assert.Equal(40, variants.Sum(v => v.GetProperty("uniqueVisitors").GetInt32()));
        Assert.Equal("Not enough data for a reliable conclusion", variants[1].GetProperty("note").GetString());
    }

    [Fact]
    public async Task Form_submission_pipeline_enforces_spam_rules_validates_and_publishes_once()
    {
        var staff = await fx.StaffAsync();
        var created = await CreatePageAsync(staff);
        var visitor = fx.Anonymous(ip: "198.51.100.20", visitorId: "lead-1");
        var page = await (await visitor.GetAsync($"/api/v1/public/lp/{created.ClientSlug}/{created.Slug}")).ReadJsonAsync();
        var form = page.GetProperty("forms")[0];
        var token = form.GetProperty("token").GetString();
        Assert.Contains("privacy policy", form.GetProperty("consentText").GetString()!, StringComparison.OrdinalIgnoreCase);

        object Submission(Dictionary<string, object> values, string? hp = null, string? tok = null) => new
        {
            values, hp, token = tok ?? token, landingPageId = created.PageId, variantKey = "A", utmSource = "newsletter", utmMedium = "email",
            utmCampaign = "spring", referrer = "https://news.example/post",
        };
        var valid = new Dictionary<string, object>
        {
            ["name"] = "Ada Lovelace", ["email"] = "ada@example.com", ["topic"] = "sales-enquiry", ["message"] = "Interested in the team plan.", ["consent"] = true,
        };
        var submitPath = $"/api/v1/public/forms/{created.FormId}/submissions";

        // Too fast (minimum fill time, measured from the signed render token).
        await (await visitor.PostAsJsonAsync(submitPath, Submission(valid))).ShouldFailAsync(400, "forms.too_fast");
        fx.Api.Clock.Advance(TimeSpan.FromSeconds(5));
        // Forged or missing token.
        await (await visitor.PostAsJsonAsync(submitPath, Submission(valid, tok: "forged"))).ShouldFailAsync(400, "forms.token_invalid");

        // Honeypot: accepted silently, nothing stored.
        var before = await fx.WithDbAsync(db => db.Set<FormSubmission>().CountAsync(s => s.FormId == created.FormId));
        Assert.Equal(HttpStatusCode.Created, (await visitor.PostAsJsonAsync(submitPath, Submission(valid, hp: "http://spam.example"))).StatusCode);
        Assert.Equal(before, await fx.WithDbAsync(db => db.Set<FormSubmission>().CountAsync(s => s.FormId == created.FormId)));

        // Server-side validation and conditional logic ("something else" requires topic_other).
        var invalid = new Dictionary<string, object>(valid) { ["email"] = "nope", ["topic"] = "something-else" };
        invalid.Remove("consent");
        var rejected = await visitor.PostAsJsonAsync(submitPath, Submission(invalid));
        await rejected.ShouldFailAsync(400, "forms.invalid_submission");
        var errors = (await rejected.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");
        Assert.True(errors.TryGetProperty("email", out _));
        Assert.True(errors.TryGetProperty("topic_other", out _));
        Assert.True(errors.TryGetProperty("consent", out _));

        var accepted = await visitor.PostAsJsonAsync(submitPath, Submission(valid));
        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
        var submission = await fx.WithDbAsync(db => db.Set<FormSubmission>().AsNoTracking().Where(s => s.FormId == created.FormId).OrderByDescending(s => s.SubmittedAt).FirstAsync());
        Assert.Equal(("ada@example.com", "Ada Lovelace", "newsletter", "spring"), (submission.Email, submission.Name, submission.UtmSource, submission.UtmCampaign));
        Assert.Equal(created.PageId, submission.LandingPageId);
        Assert.Equal("https://news.example/post", submission.Referrer);
        Assert.True(submission.ConsentGiven);
        Assert.Equal(1, submission.ConsentVersion);
        Assert.NotNull(submission.IpHash);
        Assert.DoesNotContain("198.51.100.20", submission.IpHash);
        Assert.NotNull(submission.EventPublishedAt);

        // FormSubmitted was published exactly once, and cannot be published again.
        Assert.Single(FormSubmittedRecorder.Events, e => e.SubmissionId == submission.Id);
        var evt = FormSubmittedRecorder.Events.Single(e => e.SubmissionId == submission.Id);
        Assert.Equal((created.FormId, created.ClientId, "ada@example.com", "Ada Lovelace"), (evt.FormId, evt.ClientAccountId!.Value, evt.Email, evt.Name));
        Assert.Equal("Interested in the team plan.", evt.Fields["message"]);
        using (var scope = fx.Host.Services.CreateScope())
            Assert.False(await scope.ServiceProvider.GetRequiredService<FormSubmissionService>().PublishOnceAsync(submission.Id, CancellationToken.None));
        await fx.Host.Services.GetRequiredService<JobRunner>().RunAsync<FormEventRetryJob>();
        Assert.Single(FormSubmittedRecorder.Events, e => e.SubmissionId == submission.Id);

        // Autoresponder goes through the outbox and the dispatch job.
        var outbox = await fx.WithDbAsync(db => db.Set<FormEmailOutbox>().AsNoTracking().SingleAsync(o => o.SubmissionId == submission.Id));
        Assert.Equal(OutboxEmailStatus.Pending, outbox.Status);
        Assert.Contains("Ada Lovelace", outbox.Body);
        await fx.Host.Services.GetRequiredService<JobRunner>().RunAsync<FormEmailDispatchJob>();
        Assert.Equal(OutboxEmailStatus.Sent, await fx.WithDbAsync(db => db.Set<FormEmailOutbox>().Where(o => o.Id == outbox.Id).Select(o => o.Status).SingleAsync()));

        // Render tokens are single use: sending the accepted submission again is refused.
        await (await visitor.PostAsJsonAsync(submitPath, Submission(valid))).ShouldFailAsync(409, "forms.already_submitted");

        // Per-IP rate limit (5 per form per 10 minutes), each submission with a freshly rendered form.
        async Task<string> FreshTokenAsync() =>
            (await (await visitor.GetAsync($"/api/v1/public/forms/{created.FormId}")).ReadJsonAsync()).GetProperty("token").GetString()!;
        var fresh = new List<string>();
        for (var i = 0; i < 6; i++) fresh.Add(await FreshTokenAsync());
        fx.Api.Clock.Advance(TimeSpan.FromSeconds(5));
        for (var i = 0; i < 4; i++)
            Assert.Equal(HttpStatusCode.Created, (await visitor.PostAsJsonAsync(submitPath, Submission(valid, tok: fresh[i]))).StatusCode);
        await (await visitor.PostAsJsonAsync(submitPath, Submission(valid, tok: fresh[4]))).ShouldFailAsync(429, "forms.rate_limited");
        // The rate-limited token was not spent: it still works from another network.
        Assert.Equal(HttpStatusCode.Created, (await fx.Anonymous(ip: "198.51.100.21").PostAsJsonAsync(submitPath, Submission(valid, tok: fresh[4]))).StatusCode);

        // Staff see the submission, can export it, and the client portal counts the lead for its own organization only.
        var list = await (await staff.GetAsync($"/api/v1/agency/pages/forms/{created.FormId}/submissions")).ReadJsonAsync();
        Assert.Equal(6, list.GetProperty("total").GetInt32());
        Assert.Equal("Spring Offer", list.GetProperty("items")[0].GetProperty("landingPageName").GetString());
        var csv = await (await staff.GetAsync($"/api/v1/agency/pages/forms/{created.FormId}/submissions/export.csv")).Content.ReadAsStringAsync();
        Assert.Contains("submitted_at,name,email,phone,name,email", csv);
        Assert.Contains("ada@example.com", csv);

        var member = await fx.Host.LoginAsync(await PortalUserAsync(created.ClientId));
        var overview = await (await member.GetAsync("/api/v1/client/seo/overview")).ReadJsonAsync();
        Assert.Equal(6, overview.GetProperty("organizations")[0].GetProperty("kpis").GetProperty("leads").GetProperty("submissions").GetInt32());
        var outsider = await fx.Host.LoginAsync(await fx.Api.CreateUserAsync(new[] { Role.Client }));
        Assert.Empty((await (await outsider.GetAsync("/api/v1/client/seo/overview")).ReadJsonAsync()).GetProperty("organizations").EnumerateArray());
    }

    private async Task<TestUser> PortalUserAsync(Guid clientId)
    {
        var user = await fx.Api.CreateUserAsync(new[] { Role.Client });
        await fx.WithDbAsync(async db =>
        {
            db.Add(new OptimizeAll.Domain.Agency.ClientMember { ClientAccountId = clientId, UserId = user.Id, AddedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
            return true;
        });
        return user;
    }

    [Fact]
    public async Task File_fields_accept_validated_uploads_that_only_staff_can_download()
    {
        var staff = await fx.StaffAsync();
        var client = await fx.Api.CreateClientAccountAsync();
        var formId = (await (await staff.PostAsJsonAsync("/api/v1/agency/pages/forms", new { clientAccountId = client.Id, name = "Quote", templateKey = "quote" }))
            .ReadJsonAsync()).GetProperty("id").GetGuid();
        var visitor = fx.Anonymous(ip: "198.51.100.40");
        var definition = await (await visitor.GetAsync($"/api/v1/public/forms/{formId}")).ReadJsonAsync();
        var fields = definition.GetProperty("schema").GetProperty("steps").EnumerateArray().SelectMany(s => s.GetProperty("fields").EnumerateArray()).ToList();
        string FirstOption(string key) => fields.Single(f => f.GetProperty("key").GetString() == key).GetProperty("options")[0].GetProperty("value").GetString()!;
        fx.Api.Clock.Advance(TimeSpan.FromSeconds(10));

        MultipartFormDataContent Body(byte[] file, string fileName)
        {
            var payload = new
            {
                token = definition.GetProperty("token").GetString(),
                values = new Dictionary<string, object>
                {
                    ["services"] = new[] { FirstOption("services") }, ["budget"] = FirstOption("budget"), ["timeline"] = FirstOption("timeline"),
                    ["name"] = "Grace Hopper", ["email"] = "grace@example.com", ["phone"] = "+44 20 7946 0000", ["company"] = "Navy", ["consent"] = "yes",
                },
            };
            var content = new MultipartFormDataContent { { new StringContent(JsonSerializer.Serialize(payload)), "payload" } };
            var part = new ByteArrayContent(file);
            part.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            content.Add(part, "brief", fileName);
            return content;
        }

        var exe = Encoding.ASCII.GetBytes("MZ\u0090\0 not really a pdf");
        var bad = await visitor.PostAsync($"/api/v1/public/forms/{formId}/submissions", Body(exe, "brief.pdf"));
        await bad.ShouldFailAsync(400, "forms.invalid_submission");
        Assert.Contains("brief", await bad.Content.ReadAsStringAsync());

        var pdf = Encoding.ASCII.GetBytes("%PDF-1.4\n% fixture\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF");
        Assert.Equal(HttpStatusCode.Created, (await visitor.PostAsync($"/api/v1/public/forms/{formId}/submissions", Body(pdf, "../Our brief.pdf"))).StatusCode);

        var submission = (await (await staff.GetAsync($"/api/v1/agency/pages/forms/{formId}/submissions")).ReadJsonAsync()).GetProperty("items")[0];
        var file = submission.GetProperty("files")[0];
        Assert.Equal("Our-brief.pdf", file.GetProperty("fileName").GetString());
        var download = await staff.GetAsync($"/api/v1/agency/pages/forms/{formId}/submissions/{submission.GetProperty("id").GetGuid()}/files/{file.GetProperty("id").GetGuid()}");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(pdf, await download.Content.ReadAsByteArrayAsync());
        Assert.Equal("attachment", download.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Equal("nosniff", download.Headers.GetValues("X-Content-Type-Options").Single());
        await (await (await fx.StaffAsync(Role.SeoSpecialist)).GetAsync(
            $"/api/v1/agency/pages/forms/{formId}/submissions/{submission.GetProperty("id").GetGuid()}/files/{file.GetProperty("id").GetGuid()}")).ShouldFailAsync(403);
    }

    [Fact]
    public async Task Embedded_forms_only_work_on_allowed_origins()
    {
        var staff = await fx.StaffAsync();
        var client = await fx.Api.CreateClientAccountAsync();
        var detail = await (await staff.PostAsJsonAsync("/api/v1/agency/pages/forms", new { clientAccountId = client.Id, name = "Newsletter", templateKey = "newsletter" })).ReadJsonAsync();
        var formId = detail.GetProperty("id").GetGuid();

        var update = JsonNode.Parse(detail.GetRawText())!.AsObject();
        update["allowedOrigins"] = new JsonArray("https://www.client-site.example/", "not an origin");
        await (await staff.PutAsync($"/api/v1/agency/pages/forms/{formId}", JsonBody(update))).ShouldFailAsync(400, "forms.invalid");
        update["allowedOrigins"] = new JsonArray("https://www.client-site.example/");
        var saved = await (await staff.PutAsync($"/api/v1/agency/pages/forms/{formId}", JsonBody(update))).ReadJsonAsync();
        Assert.Equal("https://www.client-site.example", saved.GetProperty("allowedOrigins")[0].GetString());

        var embed = await (await staff.GetAsync($"/api/v1/agency/pages/forms/{formId}/embed")).ReadJsonAsync();
        Assert.Contains($"/f/{formId}", embed.GetProperty("iframeSnippet").GetString());
        Assert.Contains("e.origin !== 'http://app.test'", embed.GetProperty("iframeSnippet").GetString());
        Assert.Equal("frame-ancestors https://www.client-site.example", embed.GetProperty("frameAncestors").GetString());

        HttpClient Embedded(string origin)
        {
            var c = fx.Anonymous(ip: "198.51.100.60");
            c.DefaultRequestHeaders.Add("X-Embed-Origin", origin);
            return c;
        }
        await (await Embedded("https://evil.example").GetAsync($"/api/v1/public/forms/{formId}")).ShouldFailAsync(403, "forms.origin_not_allowed");
        var allowed = await (await Embedded("https://www.client-site.example").GetAsync($"/api/v1/public/forms/{formId}")).ReadJsonAsync();
        fx.Api.Clock.Advance(TimeSpan.FromSeconds(10));
        var body = new { token = allowed.GetProperty("token").GetString(), values = new { email = "reader@example.com", consent = true } };

        await (await Embedded("https://evil.example").PostAsJsonAsync($"/api/v1/public/forms/{formId}/submissions", body)).ShouldFailAsync(403, "forms.origin_not_allowed");
        var crossSite = fx.Anonymous(ip: "198.51.100.61");
        crossSite.DefaultRequestHeaders.Add("Origin", "https://evil.example");
        await (await crossSite.PostAsJsonAsync($"/api/v1/public/forms/{formId}/submissions", body)).ShouldFailAsync(403, "forms.origin_not_allowed");

        Assert.Equal(HttpStatusCode.Created, (await Embedded("https://www.client-site.example").PostAsJsonAsync($"/api/v1/public/forms/{formId}/submissions", body)).StatusCode);
        var sameSite = fx.Anonymous(ip: "198.51.100.62");
        sameSite.DefaultRequestHeaders.Add("Origin", "http://app.test");
        // A second submission needs a freshly rendered form (render tokens are single use).
        var rendered = await (await sameSite.GetAsync($"/api/v1/public/forms/{formId}")).ReadJsonAsync();
        fx.Api.Clock.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(HttpStatusCode.Created, (await sameSite.PostAsJsonAsync($"/api/v1/public/forms/{formId}/submissions",
            body with { token = rendered.GetProperty("token").GetString() })).StatusCode);
        var stored = await fx.WithDbAsync(db => db.Set<FormSubmission>().Where(s => s.FormId == formId).Select(s => s.EmbedOrigin).ToListAsync());
        Assert.Contains("https://www.client-site.example", stored);

        // Archived forms stop accepting anything.
        (await staff.DeleteAsync($"/api/v1/agency/pages/forms/{formId}")).EnsureSuccessStatusCode();
        await (await Embedded("https://www.client-site.example").GetAsync($"/api/v1/public/forms/{formId}")).ShouldFailAsync(404);
    }

    [Fact]
    public async Task Per_ip_rate_limit_holds_under_concurrent_submissions()
    {
        var staff = await fx.StaffAsync();
        var client = await fx.Api.CreateClientAccountAsync();
        var detail = await (await staff.PostAsJsonAsync("/api/v1/agency/pages/forms", new { clientAccountId = client.Id, name = "Contact", templateKey = "contact" })).ReadJsonAsync();
        var formId = detail.GetProperty("id").GetGuid();
        var update = JsonNode.Parse(detail.GetRawText())!.AsObject();
        update["minFillSeconds"] = 0;
        update["autoresponderEnabled"] = false;
        (await staff.PutAsync($"/api/v1/agency/pages/forms/{formId}", JsonBody(update))).EnsureSuccessStatusCode();

        var visitor = fx.Anonymous(ip: "198.51.100.77");
        // One rendered form per submission (render tokens are single use).
        var tokens = new List<string>();
        for (var i = 0; i < 12; i++)
            tokens.Add((await (await visitor.GetAsync($"/api/v1/public/forms/{formId}")).ReadJsonAsync()).GetProperty("token").GetString()!);
        var path = $"/api/v1/public/forms/{formId}/submissions";
        object Body(string token) => new { token, values = new { name = "Burst", email = "burst@example.com", topic = "support", message = "Hello there", consent = "on" } };

        var responses = await Task.WhenAll(tokens.Select(t => visitor.PostAsJsonAsync(path, Body(t))));

        Assert.Equal(FormSubmissionService.MaxPerFormPerIp, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.All(responses.Where(r => r.StatusCode != HttpStatusCode.Created), r => Assert.Equal(HttpStatusCode.TooManyRequests, r.StatusCode));
        Assert.Equal(FormSubmissionService.MaxPerFormPerIp, await fx.WithDbAsync(db => db.Set<FormSubmission>().CountAsync(s => s.FormId == formId)));
    }

    [Fact]
    public async Task Consent_is_recorded_against_the_version_the_visitor_was_shown()
    {
        var staff = await fx.StaffAsync();
        var client = await fx.Api.CreateClientAccountAsync();
        var detail = await (await staff.PostAsJsonAsync("/api/v1/agency/pages/forms", new { clientAccountId = client.Id, name = "Contact", templateKey = "contact" })).ReadJsonAsync();
        var formId = detail.GetProperty("id").GetGuid();
        var update = JsonNode.Parse(detail.GetRawText())!.AsObject();
        update["minFillSeconds"] = 0;
        var saved = await (await staff.PutAsync($"/api/v1/agency/pages/forms/{formId}", JsonBody(update))).ReadJsonAsync();
        Assert.Equal(1, saved.GetProperty("consentVersion").GetInt32());

        // The visitor loads the form while consent v1 is live …
        var visitor = fx.Anonymous(ip: "198.51.100.90");
        var shown = await (await visitor.GetAsync($"/api/v1/public/forms/{formId}")).ReadJsonAsync();
        Assert.Equal(1, shown.GetProperty("consentVersion").GetInt32());
        var staleToken = shown.GetProperty("token").GetString();

        // … staff change the wording (v2) before the visitor submits.
        update = JsonNode.Parse(saved.GetRawText())!.AsObject();
        update["consentText"] = "I agree that my data may be shared with partners (v2).";
        Assert.Equal(2, (await (await staff.PutAsync($"/api/v1/agency/pages/forms/{formId}", JsonBody(update))).ReadJsonAsync()).GetProperty("consentVersion").GetInt32());

        var path = $"/api/v1/public/forms/{formId}/submissions";
        var values = new { name = "Lin", email = "lin@example.com", topic = "support", message = "Help please", consent = "on" };
        await (await visitor.PostAsJsonAsync(path, new { token = staleToken, values })).ShouldFailAsync(409, "forms.consent_changed");
        Assert.False(await fx.WithDbAsync(db => db.Set<FormSubmission>().AnyAsync(s => s.FormId == formId)));

        // After a reload (v2 shown) the submission is accepted and records v2.
        var fresh = (await (await visitor.GetAsync($"/api/v1/public/forms/{formId}")).ReadJsonAsync()).GetProperty("token").GetString();
        Assert.Equal(HttpStatusCode.Created, (await visitor.PostAsJsonAsync(path, new { token = fresh, values })).StatusCode);
        Assert.Equal(2, await fx.WithDbAsync(db => db.Set<FormSubmission>().Where(s => s.FormId == formId).Select(s => s.ConsentVersion).SingleAsync()));
    }

    [Fact]
    public async Task Consent_text_changes_create_new_versions_and_notify_staff()
    {
        var staff = await fx.StaffAsync();
        var designer = await fx.Api.CreateUserAsync(new[] { Role.Designer });
        var client = await fx.Api.CreateClientAccountAsync();
        var detail = await (await staff.PostAsJsonAsync("/api/v1/agency/pages/forms", new { clientAccountId = client.Id, name = "Contact", templateKey = "contact" })).ReadJsonAsync();
        var formId = detail.GetProperty("id").GetGuid();
        Assert.Equal(1, detail.GetProperty("consentVersion").GetInt32());

        var update = JsonNode.Parse(detail.GetRawText())!.AsObject();
        update["consentText"] = "I agree to the updated privacy policy (v2).";
        update["notifyUserIds"] = new JsonArray(designer.Id.ToString());
        update["minFillSeconds"] = 0;
        var saved = await (await staff.PutAsync($"/api/v1/agency/pages/forms/{formId}", JsonBody(update))).ReadJsonAsync();
        Assert.Equal(2, saved.GetProperty("consentVersion").GetInt32());
        update["notifyUserIds"] = new JsonArray(Guid.NewGuid().ToString());
        update["concurrencyStamp"] = saved.GetProperty("concurrencyStamp").GetGuid().ToString();
        await (await staff.PutAsync($"/api/v1/agency/pages/forms/{formId}", JsonBody(update))).ShouldFailAsync(400, "forms.invalid");

        var visitor = fx.Anonymous(ip: "198.51.100.80");
        var token = (await (await visitor.GetAsync($"/api/v1/public/forms/{formId}")).ReadJsonAsync()).GetProperty("token").GetString();
        Assert.Equal(HttpStatusCode.Created, (await visitor.PostAsJsonAsync($"/api/v1/public/forms/{formId}/submissions", new
        {
            token, values = new { name = "Lin", email = "lin@example.com", topic = "support", message = "Help please", consent = "on" },
        })).StatusCode);
        var submission = (await (await staff.GetAsync($"/api/v1/agency/pages/forms/{formId}/submissions")).ReadJsonAsync()).GetProperty("items")[0];
        Assert.Equal(2, submission.GetProperty("consentVersion").GetInt32());
        Assert.Equal("I agree to the updated privacy policy (v2).", submission.GetProperty("consentText").GetString());
        Assert.True(await fx.WithDbAsync(db => db.Set<OptimizeAll.Domain.Notifications.Notification>().AnyAsync(n => n.UserId == designer.Id && n.Type == "forms.submission")));
    }
}
