using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Modules.Projects;
using OptimizeAll.Api.Modules.Seed;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.Audit;
using OptimizeAll.Domain.Notifications;
using OptimizeAll.Domain.Projects;
using OptimizeAll.IntegrationTests.Clients;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.Projects;

public sealed class DeliverableWorkflowTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Project_template_generates_milestones_tasks_and_recurring_rules()
    {
        var am = await api.StaffAsync();
        var org = await api.CreateOrgAsync(am.Client);
        var project = await am.Client.CreateProjectAsync(org, "seo-monthly-retainer", "SEO retainer");
        var id = project.GetProperty("id").GetGuid();
        Assert.Equal(3, project.GetProperty("milestones").GetArrayLength());
        var tasks = await (await am.Client.GetAsync($"/api/v1/agency/projects/{id}/tasks")).ReadJsonAsync();
        var template = DeliveryBaselineSeeder.ProjectTemplates().Single(t => t.Key == "seo-monthly-retainer");
        Assert.Equal(template.Tasks.Count, tasks.GetArrayLength());
        Assert.Contains(tasks.EnumerateArray(), t => t.GetProperty("title").GetString() == "Compile the monthly SEO report" && t.GetProperty("clientVisible").GetBoolean());
        Assert.All(tasks.EnumerateArray(), t => Assert.NotEqual(JsonValueKind.Null, t.GetProperty("dueDate").ValueKind));
        var recurring = await (await am.Client.GetAsync($"/api/v1/agency/projects/{id}/recurring-tasks")).ReadJsonAsync();
        Assert.Equal(template.Recurring.Count, recurring.GetArrayLength());
    }

    [Fact]
    public async Task Full_approval_state_machine_with_versions_history_and_one_event()
    {
        await using var host = api.WithEventRecorder();
        var am = await api.StaffAsync();
        var amHost = await host.LoginOnAsync(am.User);
        var org = await api.CreateOrgAsync(am.Client);
        var projectId = (await am.Client.CreateProjectAsync(org)).GetProperty("id").GetGuid();
        var approver = await api.ClientUserAsync(org, ClientMemberRole.Approver);
        var approverHost = await host.LoginOnAsync(approver.User);
        var viewer = await api.ClientUserAsync(org, ClientMemberRole.Viewer);

        var d = await amHost.CreateDeliverableAsync(projectId);
        var id = d.GetProperty("deliverable").GetProperty("id").GetGuid();
        Assert.Equal("Draft", d.Status());
        // Can't submit without a version; the client can't see drafts.
        await (await amHost.PostAsJsonAsync($"/api/v1/agency/deliverables/{id}/submit", new { version = 1 })).ShouldFailAsync(409);
        await (await approver.Client.GetAsync($"/api/v1/client/orgs/{org}/deliverables/{id}")).ShouldFailAsync(404);

        var v1 = await amHost.AddLinkVersionAsync(id, "https://example.com/v1");
        Assert.Equal(1, v1.Version());
        (await amHost.PostAsJsonAsync($"/api/v1/agency/deliverables/{id}/submit", new { version = 1 })).EnsureSuccessStatusCode();
        // Internal changes requested → back to Draft.
        var back = await (await amHost.PostAsJsonAsync($"/api/v1/agency/deliverables/{id}/internal-request-changes", new { version = 1, comment = "Tighten the headline" })).ReadJsonAsync();
        Assert.Equal("Draft", back.Status());
        (await amHost.PostAsJsonAsync($"/api/v1/agency/deliverables/{id}/submit", new { version = 1 })).EnsureSuccessStatusCode();
        var sent = await (await amHost.PostAsJsonAsync($"/api/v1/agency/deliverables/{id}/internal-approve", new { version = 1 })).ReadJsonAsync();
        Assert.Equal("ClientReview", sent.Status());
        Assert.NotEqual(JsonValueKind.Null, sent.GetProperty("deliverable").GetProperty("clientDueAt").ValueKind);
        Assert.True(await api.WithDbAsync(db => db.Set<Notification>().AnyAsync(n => n.UserId == approver.User.Id && n.Type == DeliveryNotificationTypes.DeliverableAwaitingClient)));

        // Client sees v1 without internal comments; a Viewer can't act.
        var clientView = await (await approver.Client.GetAsync($"/api/v1/client/orgs/{org}/deliverables/{id}")).ReadJsonAsync();
        Assert.DoesNotContain("Tighten the headline", clientView.GetRawText());
        Assert.Contains("approve", clientView.GetProperty("allowedActions").EnumerateArray().Select(a => a.GetString()));
        var viewerView = await (await viewer.Client.GetAsync($"/api/v1/client/orgs/{org}/deliverables/{id}")).ReadJsonAsync();
        Assert.Empty(viewerView.GetProperty("allowedActions").EnumerateArray());
        await (await viewer.Client.PostAsJsonAsync($"/api/v1/client/orgs/{org}/deliverables/{id}/approve", new { version = 1 }))
            .ShouldFailAsync(403, "client.insufficient_role");

        // Client requests changes (comment required) → new version v2 → Draft → review → client.
        await (await approver.Client.PostAsJsonAsync($"/api/v1/client/orgs/{org}/deliverables/{id}/request-changes", new { version = 1 }))
            .ShouldFailAsync(400, "deliverable.comment_required");
        var changes = await (await approver.Client.PostAsJsonAsync($"/api/v1/client/orgs/{org}/deliverables/{id}/request-changes",
            new { version = 1, comment = "Use the lighter logo" })).ReadJsonAsync();
        Assert.Equal("ChangesRequested", changes.Status());
        var v2 = await amHost.AddLinkVersionAsync(id, "https://example.com/v2");
        Assert.Equal("Draft", v2.Status());
        // The client can't see v2 yet.
        var hidden = await (await approver.Client.GetAsync($"/api/v1/client/orgs/{org}/deliverables/{id}")).ReadJsonAsync();
        Assert.Single(hidden.GetProperty("versions").EnumerateArray());
        (await amHost.PostAsJsonAsync($"/api/v1/agency/deliverables/{id}/submit", new { version = 2 })).EnsureSuccessStatusCode();
        // Approving an outdated version is impossible, internally and for the client.
        await (await amHost.PostAsJsonAsync($"/api/v1/agency/deliverables/{id}/internal-approve", new { version = 1 }))
            .ShouldFailAsync(409, "deliverable.stale_version");
        (await amHost.PostAsJsonAsync($"/api/v1/agency/deliverables/{id}/internal-approve", new { version = 2 })).EnsureSuccessStatusCode();
        await (await approverHost.PostAsJsonAsync($"/api/v1/client/orgs/{org}/deliverables/{id}/approve", new { version = 1 }))
            .ShouldFailAsync(409, "deliverable.stale_version");

        var approved = await (await approverHost.PostAsJsonAsync($"/api/v1/client/orgs/{org}/deliverables/{id}/approve", new { version = 2, comment = "Lovely" }))
            .ReadJsonAsync();
        Assert.Equal("Approved", approved.Status());
        Assert.Equal(2, approved.GetProperty("approvedVersion").GetInt32());
        Assert.Contains("rate", approved.GetProperty("allowedActions").EnumerateArray().Select(a => a.GetString()));
        // Approval record: name, timestamp, version.
        var record = approved.GetProperty("history").EnumerateArray().First(h => h.GetProperty("decision").GetString() == "Approved");
        Assert.Equal(2, record.GetProperty("versionNumber").GetInt32());
        Assert.False(string.IsNullOrEmpty(record.GetProperty("userName").GetString()));
        // Approving again is rejected; DeliverableApproved was published exactly once.
        await (await approverHost.PostAsJsonAsync($"/api/v1/client/orgs/{org}/deliverables/{id}/approve", new { version = 2 }))
            .ShouldFailAsync(409, "deliverable.not_awaiting_approval");
        Assert.Single(ApprovedEventRecorder.Events, e => e.DeliverableId == id);

        // CSAT once; then staff publish.
        (await approver.Client.PostAsJsonAsync($"/api/v1/client/orgs/{org}/deliverables/{id}/csat", new { score = 5 })).EnsureSuccessStatusCode();
        await (await approver.Client.PostAsJsonAsync($"/api/v1/client/orgs/{org}/deliverables/{id}/csat", new { score = 4 }))
            .ShouldFailAsync(409, "feedback.already_submitted");
        var published = await (await amHost.PostAsJsonAsync($"/api/v1/agency/deliverables/{id}/publish", new { version = 2 })).ReadJsonAsync();
        Assert.Equal("Published", published.Status());
        await (await amHost.AddLinkVersionAsyncRaw(id)).ShouldFailAsync(409, "deliverable.locked");
        Assert.True(await api.WithDbAsync(db => db.Set<AuditLog>().AnyAsync(a => a.Action == "deliverable.client_approved" && a.EntityId == id.ToString())));
    }

    [Fact]
    public async Task Concurrent_approve_and_request_changes_have_exactly_one_winner()
    {
        await using var host = api.WithEventRecorder();
        var am = await api.StaffAsync();
        var org = await api.CreateOrgAsync(am.Client);
        var projectId = (await am.Client.CreateProjectAsync(org)).GetProperty("id").GetGuid();
        var a = await host.LoginOnAsync((await api.ClientUserAsync(org, ClientMemberRole.Approver)).User);
        var b = await host.LoginOnAsync((await api.ClientUserAsync(org, ClientMemberRole.Owner)).User);

        for (var round = 0; round < 3; round++)
        {
            var id = await am.Client.DeliverableAwaitingClientAsync(projectId, $"Race {round}");
            var approve = a.PostAsJsonAsync($"/api/v1/client/orgs/{org}/deliverables/{id}/approve", new { version = 1 });
            var reject = b.PostAsJsonAsync($"/api/v1/client/orgs/{org}/deliverables/{id}/request-changes", new { version = 1, comment = "No" });
            var results = await Task.WhenAll(approve, reject);
            Assert.Equal(1, results.Count(r => r.IsSuccessStatusCode));
            Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.Conflict));
            var decisions = await api.WithDbAsync(db => db.Set<DeliverableReview>().CountAsync(r => r.DeliverableId == id && r.Stage == ReviewStage.Client));
            Assert.Equal(1, decisions);
            var approvedWon = results[0].IsSuccessStatusCode;
            Assert.Equal(approvedWon ? 1 : 0, ApprovedEventRecorder.Events.Count(e => e.DeliverableId == id));
        }
    }

    [Fact]
    public async Task Sla_reminders_are_sent_once_and_auto_approve_is_off_by_default()
    {
        var am = await api.StaffAsync();
        var org = await api.CreateOrgAsync(am.Client);
        var projectId = (await am.Client.CreateProjectAsync(org)).GetProperty("id").GetGuid();
        var approver = await api.ClientUserAsync(org, ClientMemberRole.Approver);
        var id = await am.Client.DeliverableAwaitingClientAsync(projectId);
        var now = api.Clock.GetUtcNow().UtcDateTime;
        await api.WithDbAsync(db => db.Set<Deliverable>().Where(d => d.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.SentToClientAt, now.AddDays(-30)).SetProperty(d => d.ClientDueAt, now.AddDays(-1))));

        await api.RunJobAsync<DeliverableSlaJob>();
        await api.RunJobAsync<DeliverableSlaJob>();
        var reminders = await api.WithDbAsync(db => db.Set<Notification>()
            .CountAsync(n => n.UserId == approver.User.Id && n.Type == DeliveryNotificationTypes.DeliverableReminder));
        Assert.Equal(1, reminders);
        // Off by default: still waiting for the client after 30 days.
        Assert.Equal(DeliverableStatus.ClientReview, await api.WithDbAsync(db => db.Set<Deliverable>().Where(d => d.Id == id).Select(d => d.Status).SingleAsync()));

        // Enabled for the client: auto-approved once, audited as a system action.
        await api.WithDbAsync(db => db.Set<ClientAccount>().Where(c => c.Id == org).ExecuteUpdateAsync(s => s.SetProperty(c => c.AutoApproveAfterDays, 5)));
        await api.RunJobAsync<DeliverableSlaJob>();
        await api.RunJobAsync<DeliverableSlaJob>();
        var d = await api.WithDbAsync(db => db.Set<Deliverable>().SingleAsync(x => x.Id == id));
        Assert.Equal(DeliverableStatus.Approved, d.Status);
        Assert.True(d.AutoApproved);
        Assert.Equal(1, await api.WithDbAsync(db => db.Set<AuditLog>().CountAsync(a => a.Action == "deliverable.auto_approved" && a.EntityId == id.ToString() && a.ActorType == "system")));
        Assert.Equal(1, await api.WithDbAsync(db => db.Set<DeliverableReview>().CountAsync(r => r.DeliverableId == id && r.Decision == ReviewDecision.AutoApproved)));
    }

    [Fact]
    public async Task Tenant_isolation_for_projects_deliverables_reports_messages_and_files()
    {
        var am = await api.StaffAsync();
        var orgA = await api.CreateOrgAsync(am.Client, "Tenant A");
        var orgB = await api.CreateOrgAsync(am.Client, "Tenant B");
        var projectB = (await am.Client.CreateProjectAsync(orgB)).GetProperty("id").GetGuid();
        var deliverableB = await am.Client.DeliverableAwaitingClientAsync(projectB);
        var clientA = await api.ClientUserAsync(orgA, ClientMemberRole.Owner);
        var clientB = await api.ClientUserAsync(orgB, ClientMemberRole.Owner);

        var report = await (await am.Client.PostAsJsonAsync("/api/v1/agency/reports", new { clientId = orgB, period = "2026-08" })).ReadJsonAsync();
        var reportB = report.GetProperty("id").GetGuid();
        (await am.Client.PostAsJsonAsync($"/api/v1/agency/reports/{reportB}/publish", new { concurrencyStamp = report.GetProperty("concurrencyStamp").GetGuid() }))
            .EnsureSuccessStatusCode();
        var thread = await (await clientB.Client.PostAsJsonAsync($"/api/v1/client/orgs/{orgB}/threads", new { subject = "Hello", body = "Private to B" })).ReadJsonAsync();
        var threadB = thread.GetProperty("id").GetGuid();
        var file = await UploadAsync(am.Client, orgB, DemoPng.Creative(40, 40, 1), "b.png");
        var fileB = file.GetProperty("id").GetGuid();

        // Client B sees their own records.
        (await clientB.Client.GetAsync($"/api/v1/client/orgs/{orgB}/projects/{projectB}")).EnsureSuccessStatusCode();
        (await clientB.Client.GetAsync($"/api/v1/client/orgs/{orgB}/deliverables/{deliverableB}")).EnsureSuccessStatusCode();
        (await clientB.Client.GetAsync($"/api/v1/client/orgs/{orgB}/reports/{reportB}")).EnsureSuccessStatusCode();
        (await clientB.Client.GetAsync($"/api/v1/client/orgs/{orgB}/threads/{threadB}")).EnsureSuccessStatusCode();
        (await clientB.Client.GetAsync($"/api/v1/client/orgs/{orgB}/files/{fileB}")).EnsureSuccessStatusCode();

        // Client A gets 404 for B's records, whether addressing B's org or their own org with B's ids.
        foreach (var path in new[]
                 {
                     $"/api/v1/client/orgs/{orgB}/projects", $"/api/v1/client/orgs/{orgB}/projects/{projectB}", $"/api/v1/client/orgs/{orgA}/projects/{projectB}",
                     $"/api/v1/client/orgs/{orgB}/deliverables/{deliverableB}", $"/api/v1/client/orgs/{orgA}/deliverables/{deliverableB}",
                     $"/api/v1/client/orgs/{orgB}/reports/{reportB}", $"/api/v1/client/orgs/{orgA}/reports/{reportB}",
                     $"/api/v1/client/orgs/{orgB}/threads/{threadB}", $"/api/v1/client/orgs/{orgA}/threads/{threadB}",
                     $"/api/v1/client/orgs/{orgB}/files/{fileB}", $"/api/v1/client/orgs/{orgA}/files/{fileB}",
                     $"/api/v1/client/orgs/{orgB}/home", $"/api/v1/client/orgs/{orgB}/brand-kit", $"/api/v1/client/orgs/{orgB}/meetings",
                 })
            await (await clientA.Client.GetAsync(path)).ShouldFailAsync(404);
        await (await clientA.Client.PostAsJsonAsync($"/api/v1/client/orgs/{orgB}/deliverables/{deliverableB}/approve", new { version = 1 })).ShouldFailAsync(404);
        await (await clientA.Client.PostAsJsonAsync($"/api/v1/client/orgs/{orgA}/deliverables/{deliverableB}/approve", new { version = 1 })).ShouldFailAsync(404);
        await (await clientA.Client.PostAsJsonAsync($"/api/v1/client/orgs/{orgB}/threads/{threadB}/messages", new { body = "hi" })).ShouldFailAsync(404);
        // Attachments must belong to the same tenant.
        await (await clientA.Client.PostAsJsonAsync($"/api/v1/client/orgs/{orgA}/threads", new { subject = "Sneaky", body = "y", attachmentFileIds = new[] { fileB } }))
            .ShouldFailAsync(400, "file.invalid_attachment");

        // Anonymous callers get 401 for delivery files.
        var anonymous = api.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/v1/agency/files/{fileB}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/v1/client/orgs/{orgB}/files/{fileB}")).StatusCode);
        // Delivery files are not served by the public Files endpoint.
        await (await anonymous.GetAsync($"/api/v1/files/{fileB}")).ShouldFailAsync(404);
    }

    [Fact]
    public async Task Deliverable_files_are_validated_by_content_and_stored_privately()
    {
        var am = await api.StaffAsync();
        var org = await api.CreateOrgAsync(am.Client);
        var projectId = (await am.Client.CreateProjectAsync(org)).GetProperty("id").GetGuid();
        var d = await am.Client.CreateDeliverableAsync(projectId);
        var id = d.GetProperty("deliverable").GetProperty("id").GetGuid();

        async Task<HttpResponseMessage> Upload(byte[] bytes, string name)
        {
            using var form = new MultipartFormDataContent();
            var content = new ByteArrayContent(bytes);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png"); // client MIME is never trusted
            form.Add(content, "file", name);
            return await am.Client.PostAsync($"/api/v1/agency/deliverables/{id}/versions", form);
        }

        await (await Upload("MZ\u0090\0 not an image"u8.ToArray(), "evil.png")).ShouldFailAsync(400, "file.unsupported_type");
        await (await Upload("<svg onload=alert(1)>"u8.ToArray(), "x.svg")).ShouldFailAsync(400, "file.unsupported_type");
        var pdf = await (await Upload("%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF"u8.ToArray(), "brief.png")).ReadJsonAsync();
        var file = pdf.GetProperty("versions")[0].GetProperty("file");
        Assert.Equal("application/pdf", file.GetProperty("contentType").GetString());
        Assert.EndsWith(".pdf", file.GetProperty("fileName").GetString());
        var mp4 = new byte[64];
        "\0\0\0\u0018ftypisom"u8.ToArray().CopyTo(mp4, 0);
        var video = await (await Upload(mp4, "clip.mp4")).ReadJsonAsync();
        Assert.Equal("video/mp4", video.GetProperty("versions")[0].GetProperty("file").GetProperty("contentType").GetString());
        var png = await (await Upload(DemoPng.Creative(20, 20, 2), "img.png")).ReadJsonAsync();
        var fileId = png.GetProperty("versions")[0].GetProperty("file").GetProperty("id").GetGuid();

        var download = await am.Client.GetAsync($"/api/v1/agency/files/{fileId}");
        download.EnsureSuccessStatusCode();
        Assert.Equal("image/png", download.Content.Headers.ContentType!.MediaType);
        Assert.Equal("nosniff", download.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Contains("no-store", download.Headers.CacheControl!.ToString());
        // A participant (no clients.view, no membership) can't read it.
        var (_, participant) = await api.CreateClientAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await participant.GetAsync($"/api/v1/agency/files/{fileId}")).StatusCode);
    }

    private static async Task<JsonElement> UploadAsync(HttpClient staff, Guid clientId, byte[] bytes, string name)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(bytes), "file", name);
        return await (await staff.PostAsync($"/api/v1/agency/clients/{clientId}/files", form)).ReadJsonAsync();
    }
}

internal static class DeliverableTestExtensions
{
    public static async Task<HttpResponseMessage> AddLinkVersionAsyncRaw(this HttpClient staff, Guid deliverableId)
    {
        using var form = new MultipartFormDataContent { { new StringContent("https://example.com/late"), "linkUrl" } };
        return await staff.PostAsync($"/api/v1/agency/deliverables/{deliverableId}/versions", form);
    }
}
