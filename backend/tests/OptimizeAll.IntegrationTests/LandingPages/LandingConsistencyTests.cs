using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.LandingPages;
using OptimizeAll.Domain.Website;
using OptimizeAll.IntegrationTests.Infrastructure;
using OptimizeAll.IntegrationTests.Seo;

namespace OptimizeAll.IntegrationTests.LandingPages;

/// <summary>
/// Landing pages and forms behave like the website CMS and forms: a published rename redirects (301) the old address,
/// updates without a concurrency stamp are refused as stale, and a form's render token can be spent only once.
/// </summary>
public sealed class LandingConsistencyTests(LandingPagesFixture fx) : IClassFixture<LandingPagesFixture>
{
    private static StringContent JsonBody(JsonNode node) => new(node.ToJsonString(), Encoding.UTF8, "application/json");

    private async Task<(string ClientSlug, JsonElement Page, Guid FormId)> PublishedPageAsync(HttpClient staff)
    {
        var client = await fx.Api.CreateClientAccountAsync();
        var page = await (await staff.PostAsJsonAsync("/api/v1/agency/pages/landing-pages", new
        {
            clientAccountId = client.Id, name = "Spring Offer", templateKey = "lead-generation",
        })).ReadJsonAsync();
        var formId = page.GetProperty("variants")[0].GetProperty("blocks").EnumerateArray()
            .Single(b => b.GetProperty("type").GetString() == "form").GetProperty("props").GetProperty("formId").GetGuid();
        page = await (await staff.PostAsync($"/api/v1/agency/pages/landing-pages/{page.GetProperty("id").GetGuid()}/publish", null)).ReadJsonAsync();
        return (client.Slug, page, formId);
    }

    private static JsonObject Draft(JsonElement page, string? slug = null, bool withStamp = true)
    {
        var draft = new JsonObject
        {
            ["name"] = page.GetProperty("name").GetString(), ["slug"] = slug ?? page.GetProperty("slug").GetString(),
            ["metaTitle"] = "Spring offer", ["metaDescription"] = "Save on spring plans.", ["noIndex"] = false, ["experimentEnabled"] = false,
            ["variants"] = JsonNode.Parse(page.GetProperty("variants").GetRawText()),
        };
        if (withStamp) draft["concurrencyStamp"] = page.GetProperty("concurrencyStamp").GetGuid().ToString();
        return draft;
    }

    private async Task<string?> LookupAsync(string path)
    {
        var response = await fx.Anonymous().GetAsync("/api/v1/public/redirects?path=" + Uri.EscapeDataString(path));
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        return (await response.ReadJsonAsync()).GetProperty("location").GetString();
    }

    [Fact]
    public async Task Publishing_a_renamed_landing_page_redirects_its_old_address()
    {
        var staff = await fx.StaffAsync();
        var (clientSlug, page, _) = await PublishedPageAsync(staff);
        var pageId = page.GetProperty("id").GetGuid();
        var oldSlug = page.GetProperty("slug").GetString()!;
        var newSlug = oldSlug + "-2027";
        var (oldPath, newPath) = ($"/lp/{clientSlug}/{oldSlug}", $"/lp/{clientSlug}/{newSlug}");

        // A rename saved in the draft is not live, so nothing is redirected yet.
        page = await (await staff.PutAsync($"/api/v1/agency/pages/landing-pages/{pageId}", JsonBody(Draft(page, newSlug)))).ReadJsonAsync();
        Assert.Null(await LookupAsync(oldPath));

        await (await staff.PostAsync($"/api/v1/agency/pages/landing-pages/{pageId}/publish", null)).ReadJsonAsync();
        Assert.Equal(newPath, await LookupAsync(oldPath));
        // A full page load of the old address (nginx @document / Vite seoShell → /_document) is a real 301.
        var web = fx.Host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await web.GetAsync("/_document" + oldPath + "?utm_source=newsletter");
        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal(newPath + "?utm_source=newsletter", response.Headers.Location!.OriginalString);
        var row = await fx.WithDbAsync(db => db.Set<SiteRedirect>().AsNoTracking().SingleAsync(r => r.FromPath == oldPath));
        Assert.Equal(("landing-page", pageId), (row.ContentType, row.ContentId!.Value));

        // Another page published at the old address takes it over.
        var client = await fx.WithDbAsync(db => db.Set<OptimizeAll.Domain.Agency.ClientAccount>().AsNoTracking().SingleAsync(c => c.Slug == clientSlug));
        var reuse = await (await staff.PostAsJsonAsync("/api/v1/agency/pages/landing-pages", new { clientAccountId = client.Id, name = "Reuse", slug = oldSlug, templateKey = "lead-generation" }))
            .ReadJsonAsync();
        Assert.Equal(newPath, await LookupAsync(oldPath)); // a draft does not
        (await staff.PostAsync($"/api/v1/agency/pages/landing-pages/{reuse.GetProperty("id").GetGuid()}/publish", null)).EnsureSuccessStatusCode();
        Assert.Null(await LookupAsync(oldPath));
        Assert.False(await fx.WithDbAsync(db => db.Set<SiteRedirect>().AnyAsync(r => r.FromPath == oldPath)));
    }

    [Fact]
    public async Task Page_form_and_template_updates_without_a_stamp_are_refused_as_stale()
    {
        var staff = await fx.StaffAsync();
        var admin = await fx.StaffAsync(Role.Admin);
        var (_, page, formId) = await PublishedPageAsync(staff);
        var pageId = page.GetProperty("id").GetGuid();

        await (await staff.PutAsync($"/api/v1/agency/pages/landing-pages/{pageId}", JsonBody(Draft(page, withStamp: false))))
            .ShouldFailAsync(409, "concurrency.conflict");
        (await staff.PutAsync($"/api/v1/agency/pages/landing-pages/{pageId}", JsonBody(Draft(page)))).EnsureSuccessStatusCode();

        var form = JsonNode.Parse((await (await staff.GetAsync($"/api/v1/agency/pages/forms/{formId}")).ReadJsonAsync()).GetRawText())!.AsObject();
        var stamp = form["concurrencyStamp"]!.GetValue<string>();
        form.Remove("concurrencyStamp");
        form["name"] = "No stamp";
        await (await staff.PutAsync($"/api/v1/agency/pages/forms/{formId}", JsonBody(form))).ShouldFailAsync(409, "concurrency.conflict");
        form["concurrencyStamp"] = stamp;
        (await staff.PutAsync($"/api/v1/agency/pages/forms/{formId}", JsonBody(form))).EnsureSuccessStatusCode();

        var template = (await (await admin.GetAsync("/api/v1/agency/pages/admin/templates")).ReadJsonAsync()).EnumerateArray()
            .Single(t => t.GetProperty("key").GetString() == "webinar");
        var templateBody = new
        {
            name = template.GetProperty("name").GetString(), category = template.GetProperty("category").GetString(),
            description = template.GetProperty("description").GetString(), metaTitle = template.GetProperty("metaTitle").GetString(),
            metaDescription = template.GetProperty("metaDescription").GetString(), sortOrder = 1, isActive = true,
        };
        await (await admin.PutAsJsonAsync("/api/v1/agency/pages/admin/templates/webinar", templateBody)).ShouldFailAsync(409, "concurrency.conflict");

        var formTemplate = (await (await admin.GetAsync("/api/v1/agency/pages/admin/form-templates")).ReadJsonAsync()).EnumerateArray()
            .Single(t => t.GetProperty("key").GetString() == "newsletter");
        await (await admin.PutAsJsonAsync("/api/v1/agency/pages/admin/form-templates/newsletter", new
        {
            name = "Newsletter", description = "d", schema = formTemplate.GetProperty("schema"), submitLabel = "Subscribe", successMessage = "Thanks",
        })).ShouldFailAsync(409, "concurrency.conflict");
        Assert.Equal(formTemplate.GetProperty("concurrencyStamp").GetGuid(), await fx.WithDbAsync(db =>
            db.Set<FormTemplate>().Where(t => t.Key == "newsletter").Select(t => t.ConcurrencyStamp).SingleAsync()));
    }

    [Fact]
    public async Task A_render_token_can_be_spent_only_once_even_concurrently()
    {
        var staff = await fx.StaffAsync();
        var client = await fx.Api.CreateClientAccountAsync();
        var detail = await (await staff.PostAsJsonAsync("/api/v1/agency/pages/forms", new { clientAccountId = client.Id, name = "Contact", templateKey = "contact" })).ReadJsonAsync();
        var formId = detail.GetProperty("id").GetGuid();
        var update = JsonNode.Parse(detail.GetRawText())!.AsObject();
        update["minFillSeconds"] = 0;
        update["autoresponderEnabled"] = false;
        (await staff.PutAsync($"/api/v1/agency/pages/forms/{formId}", JsonBody(update))).EnsureSuccessStatusCode();
        var path = $"/api/v1/public/forms/{formId}/submissions";
        var values = new { name = "Ada", email = "ada@example.com", topic = "support", message = "Hello there", consent = "on" };

        async Task<string> TokenAsync(HttpClient visitor) =>
            (await (await visitor.GetAsync($"/api/v1/public/forms/{formId}")).ReadJsonAsync()).GetProperty("token").GetString()!;
        Task<int> CountAsync() => fx.WithDbAsync(db => db.Set<FormSubmission>().CountAsync(s => s.FormId == formId));

        // Every render gets its own token.
        var visitor = fx.Anonymous(ip: "198.51.100.90");
        var token = await TokenAsync(visitor);
        Assert.NotEqual(token, await TokenAsync(visitor));

        // A honeypot hit or a failed validation does not spend the token; a success does, and a replay is refused.
        Assert.Equal(HttpStatusCode.Created, (await visitor.PostAsJsonAsync(path, new { token, hp = "bot", values })).StatusCode);
        await (await visitor.PostAsJsonAsync(path, new { token, values = new { name = "Ada", email = "nope", topic = "support", message = "Hi", consent = "on" } }))
            .ShouldFailAsync(400, "forms.invalid_submission");
        Assert.Equal(HttpStatusCode.Created, (await visitor.PostAsJsonAsync(path, new { token, values })).StatusCode);
        await (await visitor.PostAsJsonAsync(path, new { token, values })).ShouldFailAsync(409, "forms.already_submitted");
        // …also from another network (the per-IP rate limit is not what stops it).
        await (await fx.Anonymous(ip: "198.51.100.91").PostAsJsonAsync(path, new { token, values })).ShouldFailAsync(409, "forms.already_submitted");
        Assert.Equal(1, await CountAsync());

        // Concurrent replays of one token from different networks: exactly one submission.
        var burst = await TokenAsync(visitor);
        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(i =>
            fx.Anonymous(ip: $"198.51.100.{100 + i}").PostAsJsonAsync(path, new { token = burst, values })));
        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.All(responses.Where(r => r.StatusCode != HttpStatusCode.Created), r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
        Assert.Equal(2, await CountAsync());
    }
}
