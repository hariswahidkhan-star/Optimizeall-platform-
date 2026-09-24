using System.Net;
using System.Net.Http.Json;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.LandingPages;
using OptimizeAll.IntegrationTests.Infrastructure;
using OptimizeAll.IntegrationTests.Seo;

namespace OptimizeAll.IntegrationTests.LandingPages;

/// <summary>Hidden library templates are really out of use, and submission follow-up only accepts defined statuses.</summary>
public sealed class LandingLibraryIntegrityTests(LandingPagesFixture fx) : IClassFixture<LandingPagesFixture>
{
    [Fact]
    public async Task Hidden_page_and_form_templates_cannot_be_used_through_the_api()
    {
        var staff = await fx.StaffAsync();
        var admin = await fx.StaffAsync(Role.Admin);
        var client = await fx.Api.CreateClientAccountAsync();

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync("/api/v1/agency/pages/admin/templates/webinar")).StatusCode);
        await (await staff.PostAsJsonAsync("/api/v1/agency/pages/landing-pages", new { clientAccountId = client.Id, name = "Hidden", templateKey = "webinar" }))
            .ShouldFailAsync(400, "landing.template_not_found");

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync("/api/v1/agency/pages/admin/form-templates/newsletter")).StatusCode);
        await (await staff.PostAsJsonAsync("/api/v1/agency/pages/forms", new { clientAccountId = client.Id, name = "Hidden", templateKey = "newsletter" }))
            .ShouldFailAsync(400, "forms.template_not_found");

        // Reset brings them back into use.
        (await admin.PostAsync("/api/v1/agency/pages/admin/templates/webinar/reset", null)).EnsureSuccessStatusCode();
        (await admin.PostAsync("/api/v1/agency/pages/admin/form-templates/newsletter/reset", null)).EnsureSuccessStatusCode();
        (await staff.PostAsJsonAsync("/api/v1/agency/pages/landing-pages", new { clientAccountId = client.Id, name = "Back", templateKey = "webinar" }))
            .EnsureSuccessStatusCode();
        (await staff.PostAsJsonAsync("/api/v1/agency/pages/forms", new { clientAccountId = client.Id, name = "Back", templateKey = "newsletter" }))
            .EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Bulk_delete_with_another_forms_submission_ids_leaves_that_form_untouched()
    {
        var staff = await fx.StaffAsync();
        var own = await fx.Api.CreateClientAccountAsync();
        var foreign = await fx.Api.CreateClientAccountAsync();
        async Task<Guid> FormAsync(Guid clientId) => (await (await staff.PostAsJsonAsync("/api/v1/agency/pages/forms",
            new { clientAccountId = clientId, name = "Bulk", templateKey = "contact" })).ReadJsonAsync()).GetProperty("id").GetGuid();
        var ownForm = await FormAsync(own.Id);
        var foreignForm = await FormAsync(foreign.Id);
        var mine = new FormSubmission { FormId = ownForm, ClientAccountId = own.Id, DataJson = "{}", Email = "mine@example.com", SubmittedAt = DateTime.UtcNow };
        var theirs = new FormSubmission { FormId = foreignForm, ClientAccountId = foreign.Id, DataJson = "{}", Email = "theirs@example.com", SubmittedAt = DateTime.UtcNow };
        var outbox = new FormEmailOutbox
        {
            Key = $"autoresponder:{theirs.Id}", SubmissionId = theirs.Id, ToAddress = "theirs@example.com", Subject = "Thanks", Body = "We got it.",
            Status = OutboxEmailStatus.Pending, NextAttemptAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow,
        };
        await fx.WithDbAsync(async db =>
        {
            db.AddRange(mine, theirs, outbox);
            await db.SaveChangesAsync();
            return true;
        });

        var result = await (await staff.PostAsJsonAsync($"/api/v1/agency/pages/forms/{ownForm}/submissions/bulk",
            new { ids = new[] { mine.Id, theirs.Id }, delete = true })).ReadJsonAsync();
        Assert.Equal(1, result.GetProperty("deleted").GetInt32());

        var (theirsKept, outboxKept, mineGone) = await fx.WithDbAsync(async db => (
            await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.AnyAsync(db.Set<FormSubmission>(), s => s.Id == theirs.Id),
            await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.AnyAsync(db.Set<FormEmailOutbox>(), o => o.Id == outbox.Id),
            !await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.AnyAsync(db.Set<FormSubmission>(), s => s.Id == mine.Id)));
        Assert.True(theirsKept);
        Assert.True(outboxKept, "another form's queued email must not be deleted");
        Assert.True(mineGone);
    }

    [Fact]
    public async Task Submission_status_must_be_a_defined_value()
    {
        var staff = await fx.StaffAsync();
        var client = await fx.Api.CreateClientAccountAsync();
        var form = await (await staff.PostAsJsonAsync("/api/v1/agency/pages/forms", new { clientAccountId = client.Id, name = "Enum", templateKey = "contact" })).ReadJsonAsync();
        var formId = form.GetProperty("id").GetGuid();
        var submission = new FormSubmission { FormId = formId, ClientAccountId = client.Id, DataJson = "{}", Email = "lead@example.com", SubmittedAt = DateTime.UtcNow };
        await fx.WithDbAsync(async db =>
        {
            db.Add(submission);
            await db.SaveChangesAsync();
            return true;
        });

        await (await staff.PatchAsJsonAsync($"/api/v1/agency/pages/forms/{formId}/submissions/{submission.Id}", new { status = 77 })).ShouldFailAsync(400);
        await (await staff.PostAsJsonAsync($"/api/v1/agency/pages/forms/{formId}/submissions/bulk", new { ids = new[] { submission.Id }, status = 77 }))
            .ShouldFailAsync(400);
        var stored = await fx.WithDbAsync(db => Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleAsync(
            db.Set<FormSubmission>(), s => s.Id == submission.Id));
        Assert.True(Enum.IsDefined(stored.Status));
    }
}
