using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Modules.LandingPages.Templates;
using OptimizeAll.Domain.Audit;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.LandingPages;
using OptimizeAll.IntegrationTests.Infrastructure;
using OptimizeAll.IntegrationTests.Seo;

namespace OptimizeAll.IntegrationTests.LandingPages;

/// <summary>Duplicate/restore of pages and forms, submission follow-up and the editable template library.</summary>
public sealed class LandingManageTests(LandingPagesFixture fx) : IClassFixture<LandingPagesFixture>
{
    private async Task<(Guid ClientId, Guid PageId, Guid FormId, string Slug)> PageAsync(HttpClient staff)
    {
        var client = await fx.Api.CreateClientAccountAsync();
        var page = await (await staff.PostAsJsonAsync("/api/v1/agency/pages/landing-pages", new { clientAccountId = client.Id, name = "Spring Offer", templateKey = "lead-generation" }))
            .ReadJsonAsync();
        var formId = page.GetProperty("variants")[0].GetProperty("blocks").EnumerateArray()
            .Single(b => b.GetProperty("type").GetString() == "form").GetProperty("props").GetProperty("formId").GetGuid();
        return (client.Id, page.GetProperty("id").GetGuid(), formId, page.GetProperty("slug").GetString()!);
    }

    private async Task<List<Guid>> SubmissionsAsync(Guid clientId, Guid formId, int count)
    {
        var ids = new List<Guid>();
        await fx.WithDbAsync(async db =>
        {
            for (var i = 0; i < count; i++)
            {
                var s = new FormSubmission { FormId = formId, ClientAccountId = clientId, DataJson = "{\"email\":\"lead@example.com\"}", Email = $"lead{i}@example.com", SubmittedAt = DateTime.UtcNow.AddMinutes(-i) };
                db.Add(s);
                ids.Add(s.Id);
            }
            await db.SaveChangesAsync();
            return true;
        });
        return ids;
    }

    [Fact]
    public async Task Pages_and_forms_can_be_duplicated_and_forms_restored()
    {
        var staff = await fx.StaffAsync();
        var (clientId, pageId, formId, slug) = await PageAsync(staff);
        var copy = await (await staff.PostAsync($"/api/v1/agency/pages/landing-pages/{pageId}/duplicate", null)).ReadJsonAsync();
        Assert.Equal($"{slug}-copy", copy.GetProperty("slug").GetString());
        var second = await (await staff.PostAsync($"/api/v1/agency/pages/landing-pages/{pageId}/duplicate", null)).ReadJsonAsync();
        Assert.Equal($"{slug}-copy-2", second.GetProperty("slug").GetString());
        var detail = await (await staff.GetAsync($"/api/v1/agency/pages/landing-pages/{copy.GetProperty("id").GetGuid()}")).ReadJsonAsync();
        Assert.Equal("Draft", detail.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("publishedVersionId").ValueKind);

        var formCopy = await (await staff.PostAsync($"/api/v1/agency/pages/forms/{formId}/duplicate", null)).ReadJsonAsync();
        Assert.Equal("Draft", formCopy.GetProperty("status").GetString());
        await (await staff.PostAsync($"/api/v1/agency/pages/forms/{formId}/restore", null)).ShouldFailAsync(409, "forms.not_archived");
        (await staff.DeleteAsync($"/api/v1/agency/pages/forms/{formId}")).EnsureSuccessStatusCode();
        var restored = await (await staff.PostAsync($"/api/v1/agency/pages/forms/{formId}/restore", null)).ReadJsonAsync();
        Assert.Equal("Draft", restored.GetProperty("status").GetString());

        await (await staff.PostAsync($"/api/v1/agency/pages/landing-pages/{Guid.NewGuid()}/duplicate", null)).ShouldFailAsync(404);
        var seo = await fx.StaffAsync(Role.SeoSpecialist);
        await (await seo.PostAsync($"/api/v1/agency/pages/forms/{formId}/duplicate", null)).ShouldFailAsync(403);
        Assert.True(await fx.WithDbAsync(db => db.Set<AuditLog>().AnyAsync(a => a.Action == "landing.page_duplicated")));
    }

    [Fact]
    public async Task Submissions_can_be_triaged_noted_bulk_updated_and_deleted()
    {
        var staff = await fx.StaffAsync();
        var (clientId, _, formId, _) = await PageAsync(staff);
        var ids = await SubmissionsAsync(clientId, formId, 3);

        var done = await (await staff.PatchAsJsonAsync($"/api/v1/agency/pages/forms/{formId}/submissions/{ids[0]}", new { status = "Done", note = "Called back" })).ReadJsonAsync();
        Assert.Equal("Done", done.GetProperty("status").GetString());
        Assert.Equal("Called back", done.GetProperty("note").GetString());
        var filtered = await (await staff.GetAsync($"/api/v1/agency/pages/forms/{formId}/submissions?status=Done")).ReadJsonAsync();
        Assert.Equal(1, filtered.GetProperty("total").GetInt32());
        Assert.Equal("Called back", filtered.GetProperty("items")[0].GetProperty("note").GetString());

        var bulk = await (await staff.PostAsJsonAsync($"/api/v1/agency/pages/forms/{formId}/submissions/bulk", new { ids = new[] { ids[1], ids[2] }, status = "Spam" })).ReadJsonAsync();
        Assert.Equal(2, bulk.GetProperty("updated").GetInt32());
        var visible = await (await staff.GetAsync($"/api/v1/agency/pages/forms/{formId}/submissions")).ReadJsonAsync();
        Assert.Equal(1, visible.GetProperty("total").GetInt32()); // spam is hidden by default
        await (await staff.PostAsJsonAsync($"/api/v1/agency/pages/forms/{formId}/submissions/bulk", new { ids = new[] { ids[1] } })).ShouldFailAsync(400);

        var deleted = await (await staff.PostAsJsonAsync($"/api/v1/agency/pages/forms/{formId}/submissions/bulk", new { ids = new[] { ids[1], ids[2] }, delete = true })).ReadJsonAsync();
        Assert.Equal(2, deleted.GetProperty("deleted").GetInt32());
        Assert.Equal(HttpStatusCode.NoContent, (await staff.DeleteAsync($"/api/v1/agency/pages/forms/{formId}/submissions/{ids[0]}")).StatusCode);
        await (await staff.DeleteAsync($"/api/v1/agency/pages/forms/{formId}/submissions/{ids[0]}")).ShouldFailAsync(404);
        await (await staff.PatchAsJsonAsync($"/api/v1/agency/pages/forms/{Guid.NewGuid()}/submissions/{ids[0]}", new { status = "Done" })).ShouldFailAsync(404);
        Assert.False(await fx.WithDbAsync(db => db.Set<FormSubmission>().AnyAsync(s => s.FormId == formId)));
    }

    [Fact]
    public async Task Template_library_is_editable_by_admins_and_survives_the_seeder()
    {
        var staff = await fx.StaffAsync();
        var admin = await fx.StaffAsync(Role.Admin);
        var (_, pageId, formId, _) = await PageAsync(staff);
        var templates = await (await staff.GetAsync("/api/v1/agency/pages/admin/templates")).ReadJsonAsync();
        var leadGen = templates.EnumerateArray().Single(t => t.GetProperty("key").GetString() == "lead-generation");
        var body = new
        {
            name = "Lead generation (agency)", category = leadGen.GetProperty("category").GetString(), description = "Our house style",
            metaTitle = "Get a quote", metaDescription = "Tell us about your project.", formTemplateKey = leadGen.GetProperty("formTemplateKey").GetString(),
            sortOrder = 1, isActive = true, concurrencyStamp = leadGen.GetProperty("concurrencyStamp").GetGuid(),
        };
        await (await staff.PutAsJsonAsync("/api/v1/agency/pages/admin/templates/lead-generation", body)).ShouldFailAsync(403);
        var updated = await (await admin.PutAsJsonAsync("/api/v1/agency/pages/admin/templates/lead-generation", body)).ReadJsonAsync();
        Assert.True(updated.GetProperty("isCustomized").GetBoolean());
        await (await admin.PutAsJsonAsync("/api/v1/agency/pages/admin/templates/lead-generation", body)).ShouldFailAsync(409, "concurrency.conflict");
        await (await admin.PutAsJsonAsync("/api/v1/agency/pages/admin/templates/lead-generation", body with { concurrencyStamp = updated.GetProperty("concurrencyStamp").GetGuid(), formTemplateKey = "nope" }))
            .ShouldFailAsync(400);
        await (await admin.PutAsJsonAsync("/api/v1/agency/pages/admin/templates/unknown", body)).ShouldFailAsync(404);

        // The baseline seeder no longer overwrites the edited copy.
        await fx.WithDbAsync(async db =>
        {
            await new OptimizeAll.Api.Modules.LandingPages.LandingPagesBaselineSeeder().SeedAsync(db, CancellationToken.None);
            return true;
        });
        var live = await (await staff.GetAsync("/api/v1/agency/pages/templates")).ReadJsonAsync();
        Assert.Equal("Lead generation (agency)", live.EnumerateArray().Single(t => t.GetProperty("key").GetString() == "lead-generation").GetProperty("name").GetString());

        // Save a page as a template: the form block becomes the placeholder again.
        var saved = await (await admin.PostAsJsonAsync($"/api/v1/agency/pages/landing-pages/{pageId}/save-as-template", new { name = "Spring layout" })).ReadJsonAsync();
        var key = saved.GetProperty("key").GetString()!;
        Assert.True(saved.GetProperty("isCustom").GetBoolean());
        var stored = await fx.WithDbAsync(db => db.Set<LandingPageTemplate>().AsNoTracking().FirstAsync(t => t.Key == key));
        Assert.Contains(TemplateCatalog.FormPlaceholder, stored.BlocksJson);
        Assert.DoesNotContain(formId.ToString(), stored.BlocksJson);
        var client = await fx.Api.CreateClientAccountAsync();
        (await staff.PostAsJsonAsync("/api/v1/agency/pages/landing-pages", new { clientAccountId = client.Id, name = "From saved", templateKey = key })).EnsureSuccessStatusCode();

        // Built-in templates hide; agency templates delete; reset restores the catalog copy.
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync("/api/v1/agency/pages/admin/templates/lead-generation")).StatusCode);
        live = await (await staff.GetAsync("/api/v1/agency/pages/templates")).ReadJsonAsync();
        Assert.DoesNotContain(live.EnumerateArray(), t => t.GetProperty("key").GetString() == "lead-generation");
        var reset = await (await admin.PostAsync("/api/v1/agency/pages/admin/templates/lead-generation/reset", null)).ReadJsonAsync();
        Assert.True(reset.GetProperty("isActive").GetBoolean());
        Assert.False(reset.GetProperty("isCustomized").GetBoolean());
        await (await admin.PostAsync($"/api/v1/agency/pages/admin/templates/{key}/reset", null)).ShouldFailAsync(409, "landing.template_custom");
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/v1/agency/pages/admin/templates/{key}")).StatusCode);
        await (await admin.DeleteAsync($"/api/v1/agency/pages/admin/templates/{key}")).ShouldFailAsync(404);
    }

    [Fact]
    public async Task Form_templates_are_validated_and_can_be_saved_from_a_form()
    {
        var staff = await fx.StaffAsync();
        var admin = await fx.StaffAsync(Role.Admin);
        var (_, _, formId, _) = await PageAsync(staff);
        var list = await (await staff.GetAsync("/api/v1/agency/pages/admin/form-templates")).ReadJsonAsync();
        var contact = list.EnumerateArray().Single(t => t.GetProperty("key").GetString() == "contact");
        var schema = contact.GetProperty("schema");
        await (await admin.PutAsJsonAsync("/api/v1/agency/pages/admin/form-templates/contact", new
        {
            name = "Contact", description = "d", schema = new { steps = new[] { new { id = "s1", fields = new[] { new { key = "Bad Key!", type = "nope", label = "x" } } } } },
            submitLabel = "Send", successMessage = "Thanks", concurrencyStamp = contact.GetProperty("concurrencyStamp").GetGuid(),
        })).ShouldFailAsync(400);
        var updated = await (await admin.PutAsJsonAsync("/api/v1/agency/pages/admin/form-templates/contact", new
        {
            name = "Contact us (agency)", description = "House style", schema, submitLabel = "Send it", successMessage = "We will call you back.",
            sortOrder = 1, isActive = true, concurrencyStamp = contact.GetProperty("concurrencyStamp").GetGuid(),
        })).ReadJsonAsync();
        Assert.Equal("Send it", updated.GetProperty("submitLabel").GetString());
        Assert.True(updated.GetProperty("isCustomized").GetBoolean());

        var client = await fx.Api.CreateClientAccountAsync();
        var form = await (await staff.PostAsJsonAsync("/api/v1/agency/pages/forms", new { clientAccountId = client.Id, name = "Contact", templateKey = "contact" })).ReadJsonAsync();
        Assert.Equal("Send it", form.GetProperty("submitLabel").GetString());

        var saved = await (await admin.PostAsJsonAsync($"/api/v1/agency/pages/forms/{formId}/save-as-template", new { name = "Quote (agency)" })).ReadJsonAsync();
        Assert.True(saved.GetProperty("isCustom").GetBoolean());
        await (await staff.PostAsJsonAsync($"/api/v1/agency/pages/forms/{formId}/save-as-template", new { name = "Nope" })).ShouldFailAsync(403);
        (await admin.PostAsync("/api/v1/agency/pages/admin/form-templates/contact/reset", null)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/v1/agency/pages/admin/form-templates/{saved.GetProperty("key").GetString()}")).StatusCode);
    }
}
