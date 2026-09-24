using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Domain.Audit;
using OptimizeAll.Domain.Identity;
using OptimizeAll.IntegrationTests.Clients;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.Projects;

/// <summary>
/// Editing flows added for delivery: task comments, deliverable deletion, timesheet reopen, brief/report/project templates,
/// the onboarding checklist template and items, and thread renames.
/// </summary>
public sealed class DeliveryEditingTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private DateOnly Today => DateOnly.FromDateTime(api.Clock.GetUtcNow().UtcDateTime);

    private static Task<bool> Audited(ApiFactory api, string action, Guid id) =>
        api.WithDbAsync(db => db.Set<AuditLog>().AnyAsync(a => a.Action == action && a.EntityId == id.ToString()));

    [Fact]
    public async Task Task_comments_are_edited_by_their_author_and_deleted_by_author_or_manager()
    {
        var am = await api.StaffAsync();
        var org = await api.CreateOrgAsync(am.Client);
        var projectId = (await am.Client.CreateProjectAsync(org)).GetProperty("id").GetGuid();
        var task = await (await am.Client.PostAsJsonAsync($"/api/v1/agency/projects/{projectId}/tasks", new { title = "Write copy" })).ReadJsonAsync();
        var taskId = task.GetProperty("task").GetProperty("id").GetGuid();
        var designer = await api.StaffAsync(Role.Designer);
        var commented = await (await designer.Client.PostAsJsonAsync($"/api/v1/agency/tasks/{taskId}/comments", new { body = "First draft" })).ReadJsonAsync();
        var commentId = commented.GetProperty("comments")[0].GetProperty("id").GetGuid();

        var edited = await (await designer.Client.PutAsJsonAsync($"/api/v1/agency/tasks/{taskId}/comments/{commentId}", new { body = "First draft, v2" }))
            .ReadJsonAsync();
        var comment = edited.GetProperty("comments")[0];
        Assert.Equal("First draft, v2", comment.GetProperty("body").GetString());
        Assert.NotEqual(JsonValueKind.Null, comment.GetProperty("editedAt").ValueKind);
        Assert.True(await Audited(api, "task.comment_edited", commentId));

        // Only the author edits; validation; unknown comment and a comment on another task answer 404.
        await (await am.Client.PutAsJsonAsync($"/api/v1/agency/tasks/{taskId}/comments/{commentId}", new { body = "Hijack" }))
            .ShouldFailAsync(403, "task.comment_not_author");
        await (await designer.Client.PutAsJsonAsync($"/api/v1/agency/tasks/{taskId}/comments/{commentId}", new { body = "" })).ShouldFailAsync(400);
        var otherTask = await (await am.Client.PostAsJsonAsync($"/api/v1/agency/projects/{projectId}/tasks", new { title = "Other" })).ReadJsonAsync();
        await (await designer.Client.PutAsJsonAsync($"/api/v1/agency/tasks/{otherTask.GetProperty("task").GetProperty("id").GetGuid()}/comments/{commentId}",
            new { body = "x" })).ShouldFailAsync(404);

        // Another delivery person can't delete it; a project manager can.
        var writer = await api.StaffAsync(Role.ContentCreator);
        await (await writer.Client.DeleteAsync($"/api/v1/agency/tasks/{taskId}/comments/{commentId}")).ShouldFailAsync(403);
        var afterDelete = await (await am.Client.DeleteAsync($"/api/v1/agency/tasks/{taskId}/comments/{commentId}")).ReadJsonAsync();
        Assert.Equal(0, afterDelete.GetProperty("comments").GetArrayLength());
        Assert.True(await Audited(api, "task.comment_deleted", commentId));
    }

    [Fact]
    public async Task Deliverables_the_client_never_saw_can_be_deleted_others_are_kept()
    {
        var am = await api.StaffAsync();
        var org = await api.CreateOrgAsync(am.Client);
        var projectId = (await am.Client.CreateProjectAsync(org)).GetProperty("id").GetGuid();
        var draft = await am.Client.CreateDeliverableAsync(projectId, "Scrapped idea");
        var draftId = draft.GetProperty("deliverable").GetProperty("id").GetGuid();
        var stamp = draft.GetProperty("deliverable").GetProperty("concurrencyStamp").GetGuid();

        await (await am.Client.DeleteAsync($"/api/v1/agency/deliverables/{draftId}?concurrencyStamp={Guid.NewGuid()}")).ShouldFailAsync(409, "concurrency.conflict");
        var designer = await api.StaffAsync(Role.Designer); // deliverables.submit, not projects.manage
        await (await designer.Client.DeleteAsync($"/api/v1/agency/deliverables/{draftId}?concurrencyStamp={stamp}")).ShouldFailAsync(403);
        Assert.Equal(HttpStatusCode.NoContent, (await am.Client.DeleteAsync($"/api/v1/agency/deliverables/{draftId}?concurrencyStamp={stamp}")).StatusCode);
        await (await am.Client.GetAsync($"/api/v1/agency/deliverables/{draftId}")).ShouldFailAsync(404);
        Assert.True(await Audited(api, "deliverable.deleted", draftId));

        var sentId = await am.Client.DeliverableAwaitingClientAsync(projectId);
        var sent = await (await am.Client.GetAsync($"/api/v1/agency/deliverables/{sentId}")).ReadJsonAsync();
        await (await am.Client.DeleteAsync(
                $"/api/v1/agency/deliverables/{sentId}?concurrencyStamp={sent.GetProperty("deliverable").GetProperty("concurrencyStamp").GetGuid()}"))
            .ShouldFailAsync(409, "deliverable.not_deletable");

        // Editing details is audited.
        var renamed = await (await am.Client.PutAsJsonAsync($"/api/v1/agency/deliverables/{sentId}", new
        {
            title = "Hero banner (final)", concurrencyStamp = sent.GetProperty("deliverable").GetProperty("concurrencyStamp").GetGuid(),
        })).ReadJsonAsync();
        Assert.Equal("Hero banner (final)", renamed.GetProperty("deliverable").GetProperty("title").GetString());
        Assert.True(await Audited(api, "deliverable.updated", sentId));
    }

    [Fact]
    public async Task Timesheets_can_be_recalled_by_the_owner_and_reopened_by_another_manager()
    {
        var am = await api.StaffAsync();
        var org = await api.CreateOrgAsync(am.Client);
        var projectId = (await am.Client.CreateProjectAsync(org)).GetProperty("id").GetGuid();
        var designer = await api.StaffAsync(Role.Designer);
        (await designer.Client.PostAsJsonAsync("/api/v1/agency/time/entries", new { projectId, date = Today, minutes = 120 })).EnsureSuccessStatusCode();
        var week = await (await designer.Client.PostAsync($"/api/v1/agency/time/timesheets/submit?date={Today:yyyy-MM-dd}", null)).ReadJsonAsync();
        var sheetId = week.GetProperty("id").GetGuid();

        // Owner recalls the submitted week, edits, resubmits.
        var recalled = await (await designer.Client.PostAsJsonAsync($"/api/v1/agency/time/timesheets/{sheetId}/reopen",
            new { concurrencyStamp = week.GetProperty("concurrencyStamp").GetGuid() })).ReadJsonAsync();
        Assert.Equal("Open", recalled.GetProperty("status").GetString());
        await (await designer.Client.PostAsJsonAsync($"/api/v1/agency/time/timesheets/{sheetId}/reopen",
            new { concurrencyStamp = recalled.GetProperty("concurrencyStamp").GetGuid() })).ShouldFailAsync(409, "time.already_open");
        await (await designer.Client.PostAsJsonAsync($"/api/v1/agency/time/timesheets/{sheetId}/reopen",
            new { concurrencyStamp = week.GetProperty("concurrencyStamp").GetGuid() })).ShouldFailAsync(409, "concurrency.conflict");
        (await designer.Client.PostAsJsonAsync("/api/v1/agency/time/entries", new { projectId, date = Today, minutes = 30 })).EnsureSuccessStatusCode();
        week = await (await designer.Client.PostAsync($"/api/v1/agency/time/timesheets/submit?date={Today:yyyy-MM-dd}", null)).ReadJsonAsync();
        var approved = await (await am.Client.PostAsJsonAsync($"/api/v1/agency/time/timesheets/{sheetId}/approve",
            new { concurrencyStamp = week.GetProperty("concurrencyStamp").GetGuid() })).ReadJsonAsync();
        var approvedStamp = approved.GetProperty("concurrencyStamp").GetGuid();

        // The owner can't reopen an approved week; a manager needs a reason; others don't see it.
        await (await designer.Client.PostAsJsonAsync($"/api/v1/agency/time/timesheets/{sheetId}/reopen", new { concurrencyStamp = approvedStamp }))
            .ShouldFailAsync(403, "time.own_timesheet");
        await (await am.Client.PostAsJsonAsync($"/api/v1/agency/time/timesheets/{sheetId}/reopen", new { concurrencyStamp = approvedStamp }))
            .ShouldFailAsync(400, "time.comment_required");
        var writer = await api.StaffAsync(Role.ContentCreator);
        await (await writer.Client.PostAsJsonAsync($"/api/v1/agency/time/timesheets/{sheetId}/reopen", new { comment = "x", concurrencyStamp = approvedStamp }))
            .ShouldFailAsync(404);
        var reopened = await (await am.Client.PostAsJsonAsync($"/api/v1/agency/time/timesheets/{sheetId}/reopen",
            new { comment = "Wrong project on Tuesday", concurrencyStamp = approvedStamp })).ReadJsonAsync();
        Assert.Equal("Open", reopened.GetProperty("status").GetString());
        Assert.True(await Audited(api, "time.timesheet_reopened", sheetId));
        // Entries are editable again.
        (await designer.Client.PostAsJsonAsync("/api/v1/agency/time/entries", new { projectId, date = Today, minutes = 15 })).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Brief_and_report_templates_are_managed_and_built_ins_are_protected()
    {
        var am = await api.StaffAsync();
        var key = "brief-" + Guid.NewGuid().ToString("N")[..8];
        var brief = await (await am.Client.PostAsJsonAsync("/api/v1/agency/templates/briefs", new
        {
            key, serviceLine = "content", name = "Podcast brief", isActive = true,
            fields = new object[]
            {
                new { key = "topic", label = "Topic", type = "Text", required = true, help = (string?)null, options = Array.Empty<string>() },
                new { key = "format", label = "Format", type = "Select", required = false, help = "Pick one", options = new[] { "Interview", "Solo" } },
            },
        })).ReadJsonAsync();
        Assert.False(brief.GetProperty("builtIn").GetBoolean());
        await (await am.Client.PostAsJsonAsync("/api/v1/agency/templates/briefs", new
        {
            key, serviceLine = "content", name = "Duplicate", fields = new[] { new { key = "a", label = "A", type = "Text", required = false, options = Array.Empty<string>() } },
        })).ShouldFailAsync(400, "template.key_taken");
        await (await am.Client.PostAsJsonAsync("/api/v1/agency/templates/briefs", new
        {
            key = key + "-x", serviceLine = "content", name = "No options",
            fields = new[] { new { key = "pick", label = "Pick", type = "Select", required = true, options = Array.Empty<string>() } },
        })).ShouldFailAsync(400, "template.invalid_field");

        var briefId = brief.GetProperty("id").GetGuid();
        var deactivated = await (await am.Client.PutAsJsonAsync($"/api/v1/agency/templates/briefs/{briefId}", new
        {
            serviceLine = "content", name = "Podcast brief", isActive = false, concurrencyStamp = brief.GetProperty("concurrencyStamp").GetGuid(),
            fields = new[] { new { key = "topic", label = "Topic", type = "Text", required = true, options = Array.Empty<string>() } },
        })).ReadJsonAsync();
        Assert.False(deactivated.GetProperty("isActive").GetBoolean());
        await (await am.Client.PutAsJsonAsync($"/api/v1/agency/templates/briefs/{briefId}", new
        {
            serviceLine = "content", name = "Stale", concurrencyStamp = brief.GetProperty("concurrencyStamp").GetGuid(),
            fields = new[] { new { key = "topic", label = "Topic", type = "Text", required = true, options = Array.Empty<string>() } },
        })).ShouldFailAsync(409, "concurrency.conflict");
        var active = await (await am.Client.GetAsync("/api/v1/agency/templates/briefs")).ReadJsonAsync();
        Assert.DoesNotContain(active.EnumerateArray(), t => t.GetProperty("id").GetGuid() == briefId);
        Assert.Equal(HttpStatusCode.NoContent, (await am.Client.DeleteAsync($"/api/v1/agency/templates/briefs/{briefId}")).StatusCode);

        var all = await (await am.Client.GetAsync("/api/v1/agency/templates/briefs?includeInactive=true")).ReadJsonAsync();
        var builtIn = all.EnumerateArray().First(t => t.GetProperty("builtIn").GetBoolean());
        await (await am.Client.DeleteAsync($"/api/v1/agency/templates/briefs/{builtIn.GetProperty("id").GetGuid()}")).ShouldFailAsync(409, "template.built_in");
        var designer = await api.StaffAsync(Role.Designer);
        await (await designer.Client.DeleteAsync($"/api/v1/agency/templates/briefs/{builtIn.GetProperty("id").GetGuid()}")).ShouldFailAsync(403);

        // Report templates: data sources must exist; the monthly template stays active.
        var report = await (await am.Client.PostAsJsonAsync("/api/v1/agency/templates/reports", new
        {
            key = "report-" + Guid.NewGuid().ToString("N")[..8], name = "Quarterly review", isActive = true,
            sections = new[] { new { key = "summary", kind = "summary", title = "Summary", providerKey = (string?)null, prompt = "Headline results" } },
        })).ReadJsonAsync();
        await (await am.Client.PostAsJsonAsync("/api/v1/agency/templates/reports", new
        {
            key = "report-bad-" + Guid.NewGuid().ToString("N")[..6], name = "Bad", sections = new[] { new { key = "x", kind = "custom", title = "X", providerKey = "no.such.provider" } },
        })).ShouldFailAsync(400, "template.invalid_section");
        var reports = await (await am.Client.GetAsync("/api/v1/agency/templates/reports?includeInactive=true")).ReadJsonAsync();
        var monthly = reports.EnumerateArray().First(t => t.GetProperty("key").GetString() == "monthly-performance");
        await (await am.Client.PutAsJsonAsync($"/api/v1/agency/templates/reports/{monthly.GetProperty("id").GetGuid()}", new
        {
            name = monthly.GetProperty("name").GetString(), isActive = false, sections = monthly.GetProperty("sections"),
            concurrencyStamp = monthly.GetProperty("concurrencyStamp").GetGuid(),
        })).ShouldFailAsync(409, "template.default_report");
        Assert.Equal(HttpStatusCode.NoContent, (await am.Client.DeleteAsync($"/api/v1/agency/templates/reports/{report.GetProperty("id").GetGuid()}")).StatusCode);
        await (await am.Client.DeleteAsync($"/api/v1/agency/templates/reports/{Guid.NewGuid()}")).ShouldFailAsync(404);

        // Project templates: custom ones can be deleted, built-ins can't.
        var projectTemplate = await (await am.Client.PostAsJsonAsync("/api/v1/agency/templates/projects", new
        {
            key = "custom-" + Guid.NewGuid().ToString("N")[..8], name = "Custom sprint", projectType = "OneOffCampaign", milestones = Array.Empty<object>(),
            tasks = new[] { new { title = "Kickoff", offsetDays = 0, labels = Array.Empty<string>(), clientVisible = true } }, recurring = Array.Empty<object>(),
            serviceLines = Array.Empty<string>(), isActive = true,
        })).ReadJsonAsync();
        Assert.False(projectTemplate.GetProperty("builtIn").GetBoolean());
        Assert.Equal(HttpStatusCode.NoContent,
            (await am.Client.DeleteAsync($"/api/v1/agency/templates/projects/{projectTemplate.GetProperty("id").GetGuid()}")).StatusCode);
        var projects = await (await am.Client.GetAsync("/api/v1/agency/templates/projects?includeInactive=true")).ReadJsonAsync();
        var builtInProject = projects.EnumerateArray().First(t => t.GetProperty("builtIn").GetBoolean());
        await (await am.Client.DeleteAsync($"/api/v1/agency/templates/projects/{builtInProject.GetProperty("id").GetGuid()}"))
            .ShouldFailAsync(409, "template.built_in");
    }

    [Fact]
    public async Task Onboarding_template_drives_new_clients_and_items_can_be_edited_or_removed()
    {
        var am = await api.StaffAsync();
        var template = await (await am.Client.GetAsync("/api/v1/agency/settings/onboarding-template")).ReadJsonAsync();
        Assert.Contains(template.GetProperty("items").EnumerateArray(), i => i.GetProperty("key").GetString() == "ga4-access");

        var saved = await (await am.Client.PutAsJsonAsync("/api/v1/agency/settings/onboarding-template", new
        {
            items = new object[]
            {
                new { title = "Sign the SOW", category = "Commercial", owner = "Client" },
                new { title = "Kickoff call", category = "Kickoff", owner = "Agency", key = "kickoff-call" },
            },
            version = template.GetProperty("version").GetString(),
        })).ReadJsonAsync();
        Assert.Equal(new[] { "sign-the-sow", "kickoff-call" }, saved.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("key").GetString()));
        await (await am.Client.PutAsJsonAsync("/api/v1/agency/settings/onboarding-template", new
        {
            items = Array.Empty<object>(), version = template.GetProperty("version").GetString(),
        })).ShouldFailAsync(409, "concurrency.conflict");
        var strategist = await api.StaffAsync(Role.Strategist); // clients.view only
        await (await strategist.Client.PutAsJsonAsync("/api/v1/agency/settings/onboarding-template", new
        {
            items = Array.Empty<object>(), version = saved.GetProperty("version").GetString(),
        })).ShouldFailAsync(403);

        // New clients start with the edited checklist.
        var org = await api.CreateOrgAsync(am.Client);
        var onboarding = await (await am.Client.GetAsync($"/api/v1/agency/clients/{org}/onboarding")).ReadJsonAsync();
        var items = onboarding.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(new[] { "Sign the SOW", "Kickoff call" }, items.Select(i => i.GetProperty("title").GetString()));

        var itemId = items[0].GetProperty("id").GetGuid();
        var edited = await (await am.Client.PutAsJsonAsync($"/api/v1/agency/clients/{org}/onboarding/{itemId}/details", new
        {
            title = "Sign the statement of work", category = "Legal", owner = "Client", description = "Both parties sign.",
        })).ReadJsonAsync();
        Assert.Equal("Sign the statement of work", edited.GetProperty("items")[0].GetProperty("title").GetString());
        await (await am.Client.PutAsJsonAsync($"/api/v1/agency/clients/{org}/onboarding/{itemId}/details", new { title = "x", owner = "Client" }))
            .ShouldFailAsync(400);

        // Another client's id in the path answers 404 (tenancy by parent).
        var otherOrg = await api.CreateOrgAsync(am.Client);
        await (await am.Client.DeleteAsync($"/api/v1/agency/clients/{otherOrg}/onboarding/{itemId}")).ShouldFailAsync(404);
        await (await strategist.Client.DeleteAsync($"/api/v1/agency/clients/{org}/onboarding/{itemId}")).ShouldFailAsync(403);
        var afterDelete = await (await am.Client.DeleteAsync($"/api/v1/agency/clients/{org}/onboarding/{itemId}")).ReadJsonAsync();
        Assert.Single(afterDelete.GetProperty("items").EnumerateArray());
        Assert.True(await Audited(api, "client.onboarding_item_deleted", itemId));

        // Restore the default checklist for other tests in this class.
        await (await am.Client.PutAsJsonAsync("/api/v1/agency/settings/onboarding-template", new
        {
            items = template.GetProperty("items"), version = saved.GetProperty("version").GetString(),
        })).ReadJsonAsync();
    }

    [Fact]
    public async Task Staff_rename_threads_within_the_client_only()
    {
        var am = await api.StaffAsync();
        var org = await api.CreateOrgAsync(am.Client);
        var thread = await (await am.Client.PostAsJsonAsync($"/api/v1/agency/clients/{org}/threads", new { subject = "Kickoff", body = "Hello" }))
            .ReadJsonAsync();
        var threadId = thread.GetProperty("id").GetGuid();
        var renamed = await (await am.Client.PutAsJsonAsync($"/api/v1/agency/clients/{org}/threads/{threadId}", new { subject = "Kickoff: agenda" }))
            .ReadJsonAsync();
        Assert.Equal("Kickoff: agenda", renamed.GetProperty("subject").GetString());
        Assert.True(await Audited(api, "message.thread_renamed", threadId));

        var otherOrg = await api.CreateOrgAsync(am.Client);
        await (await am.Client.PutAsJsonAsync($"/api/v1/agency/clients/{otherOrg}/threads/{threadId}", new { subject = "Nope" })).ShouldFailAsync(404);
        await (await am.Client.PutAsJsonAsync($"/api/v1/agency/clients/{org}/threads/{threadId}", new { subject = "" })).ShouldFailAsync(400);
        var client = await api.ClientUserAsync(org, Domain.Agency.ClientMemberRole.Owner);
        await (await client.Client.PutAsJsonAsync($"/api/v1/agency/clients/{org}/threads/{threadId}", new { subject = "Client rename" })).ShouldFailAsync(403);
    }
}
