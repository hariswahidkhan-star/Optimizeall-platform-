using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Modules.Projects;
using OptimizeAll.Api.Modules.Seed;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.Audit;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Notifications;
using OptimizeAll.Domain.Projects;
using OptimizeAll.IntegrationTests.Auth;
using OptimizeAll.IntegrationTests.Clients;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.Projects;

/// <summary>
/// Internal (staff-only) threads, messaging duties of client members, onboarding steps done on the client's behalf, and
/// removal of brand assets and task attachments (with file clean-up).
/// </summary>
public sealed class DeliveryGapsTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private const string ImpersonationForbidden = "auth.impersonation_forbidden_action";

    private static async Task<JsonElement> UploadAsync(HttpClient staff, Guid clientId, byte[] bytes, string name)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(bytes), "file", name);
        return await (await staff.PostAsync($"/api/v1/agency/clients/{clientId}/files", form)).ReadJsonAsync();
    }

    private Task<bool> AuditedAsync(string action, Guid entityId) =>
        api.WithDbAsync(db => db.Set<AuditLog>().AnyAsync(a => a.Action == action && a.EntityId == entityId.ToString()));

    private Task<bool> FileExistsAsync(Guid fileId) => api.WithDbAsync(db => db.Set<DeliveryFile>().AnyAsync(f => f.Id == fileId));

    // ------------------------------------------------------------------ internal threads

    [Fact]
    public async Task Internal_threads_never_reach_client_users_in_lists_by_id_notifications_or_files()
    {
        var am = await api.StaffAsync();
        var orgA = await api.CreateOrgAsync(am.Client, "Internal A");
        var orgB = await api.CreateOrgAsync(am.Client, "Internal B");
        var designer = await api.StaffAsync(Role.Designer);
        (await am.Client.PostAsJsonAsync($"/api/v1/agency/clients/{orgA}/team", new { userId = designer.User.Id, serviceRole = "Design" }))
            .EnsureSuccessStatusCode();
        var owner = await api.ClientUserAsync(orgA, ClientMemberRole.Owner);
        var viewer = await api.ClientUserAsync(orgA, ClientMemberRole.Viewer);
        var ownerB = await api.ClientUserAsync(orgB, ClientMemberRole.Owner);

        // A public thread (control) and an internal one with a file attached only there.
        var shared = await (await am.Client.PostAsJsonAsync($"/api/v1/agency/clients/{orgA}/threads", new { subject = "Kickoff", body = "Welcome!" }))
            .ReadJsonAsync();
        Assert.False(shared.GetProperty("isInternal").GetBoolean());
        var file = await UploadAsync(am.Client, orgA, DemoPng.Creative(30, 30, 3), "margins.png");
        var fileId = file.GetProperty("id").GetGuid();
        var created = await am.Client.PostAsJsonAsync($"/api/v1/agency/clients/{orgA}/threads",
            new { subject = "Pricing strategy", body = "Client is price sensitive.", isInternal = true, attachmentFileIds = new[] { fileId } });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var internalThread = await created.ReadJsonAsync();
        var internalId = internalThread.GetProperty("id").GetGuid();
        Assert.True(internalThread.GetProperty("isInternal").GetBoolean());
        Assert.True(await AuditedAsync("message.internal_thread_created", internalId));

        // Staff see both, flagged; the team (not the client) is notified.
        var staffList = await (await am.Client.GetAsync($"/api/v1/agency/clients/{orgA}/threads")).ReadJsonAsync();
        Assert.Contains(staffList.EnumerateArray(), t => t.GetProperty("id").GetGuid() == internalId && t.GetProperty("isInternal").GetBoolean());
        Assert.True(await api.WithDbAsync(db => db.Set<Notification>().AnyAsync(n => n.UserId == designer.User.Id && n.Title.Contains("Pricing strategy"))));
        (await am.Client.PostAsJsonAsync($"/api/v1/agency/clients/{orgA}/threads/{internalId}/messages", new { body = "Agreed, hold the discount." }))
            .EnsureSuccessStatusCode();
        foreach (var member in new[] { owner.User.Id, viewer.User.Id })
            Assert.False(await api.WithDbAsync(db => db.Set<Notification>().AnyAsync(n => n.UserId == member && n.Title.Contains("Pricing strategy"))));

        // Client users of the organization: not listed, 404 by id (read, reply), not on the home page, file not served.
        foreach (var member in new[] { owner, viewer })
        {
            var list = await (await member.Client.GetAsync($"/api/v1/client/orgs/{orgA}/threads")).ReadJsonAsync();
            Assert.Contains(list.EnumerateArray(), t => t.GetProperty("subject").GetString() == "Kickoff");
            Assert.DoesNotContain(list.EnumerateArray(), t => t.GetProperty("id").GetGuid() == internalId);
            await (await member.Client.GetAsync($"/api/v1/client/orgs/{orgA}/threads/{internalId}")).ShouldFailAsync(404);
            await (await member.Client.PostAsJsonAsync($"/api/v1/client/orgs/{orgA}/threads/{internalId}/messages", new { body = "hi" })).ShouldFailAsync(404);
            var home = await (await member.Client.GetAsync($"/api/v1/client/orgs/{orgA}/home")).ReadJsonAsync();
            Assert.DoesNotContain(home.GetProperty("threads").EnumerateArray(), t => t.GetProperty("id").GetGuid() == internalId);
            Assert.DoesNotContain("Pricing strategy", home.GetRawText());
            await (await member.Client.GetAsync($"/api/v1/client/orgs/{orgA}/files/{fileId}")).ShouldFailAsync(404);
        }
        // Another organization's client: 404 both ways.
        await (await ownerB.Client.GetAsync($"/api/v1/client/orgs/{orgA}/threads/{internalId}")).ShouldFailAsync(404);
        await (await ownerB.Client.GetAsync($"/api/v1/client/orgs/{orgB}/threads/{internalId}")).ShouldFailAsync(404);
        Assert.Equal(0, (await (await ownerB.Client.GetAsync($"/api/v1/client/orgs/{orgB}/threads")).ReadJsonAsync()).GetArrayLength());
        // Staff can't address the thread through another client either.
        await (await am.Client.GetAsync($"/api/v1/agency/clients/{orgB}/threads/{internalId}")).ShouldFailAsync(404);

        // A client user can't start an internal thread, nor re-share the internal file.
        await (await owner.Client.PostAsJsonAsync($"/api/v1/client/orgs/{orgA}/threads", new { subject = "Sneaky", body = "x", isInternal = true }))
            .ShouldFailAsync(400, "message.internal_not_allowed");
        await (await owner.Client.PostAsJsonAsync($"/api/v1/client/orgs/{orgA}/threads", new { subject = "Sneaky", body = "x", attachmentFileIds = new[] { fileId } }))
            .ShouldFailAsync(400, "file.invalid_attachment");

        // Global search is staff-only (and has no thread results), so it can't surface the thread to a client user.
        await (await owner.Client.GetAsync("/api/v1/search?q=Pricing")).ShouldFailAsync(403);

        // Sharing the file in the client-visible thread is a deliberate staff act; then the client may download it.
        (await am.Client.PostAsJsonAsync($"/api/v1/agency/clients/{orgA}/threads/{shared.GetProperty("id").GetGuid()}/messages",
            new { body = "Here you go", attachmentFileIds = new[] { fileId } })).EnsureSuccessStatusCode();
        (await owner.Client.GetAsync($"/api/v1/client/orgs/{orgA}/files/{fileId}")).EnsureSuccessStatusCode();
    }

    // ------------------------------------------------------------------ messaging duties

    [Fact]
    public async Task Viewer_and_Billing_members_read_messages_but_cannot_post()
    {
        var am = await api.StaffAsync();
        var org = await api.CreateOrgAsync(am.Client, "Duties");
        var thread = await (await am.Client.PostAsJsonAsync($"/api/v1/agency/clients/{org}/threads", new { subject = "Monthly check-in", body = "How are we doing?" }))
            .ReadJsonAsync();
        var threadId = thread.GetProperty("id").GetGuid();
        Assert.True(thread.GetProperty("canReply").GetBoolean());

        foreach (var duty in new[] { ClientMemberRole.Viewer, ClientMemberRole.Billing })
        {
            var member = await api.ClientUserAsync(org, duty);
            var read = await (await member.Client.GetAsync($"/api/v1/client/orgs/{org}/threads/{threadId}")).ReadJsonAsync();
            Assert.False(read.GetProperty("canReply").GetBoolean());
            await (await member.Client.PostAsJsonAsync($"/api/v1/client/orgs/{org}/threads/{threadId}/messages", new { body = "Fine" }))
                .ShouldFailAsync(403, "client.insufficient_role");
            await (await member.Client.PostAsJsonAsync($"/api/v1/client/orgs/{org}/threads", new { subject = "Question", body = "Hi" }))
                .ShouldFailAsync(403, "client.insufficient_role");
        }
        foreach (var duty in new[] { ClientMemberRole.Approver, ClientMemberRole.Owner })
        {
            var member = await api.ClientUserAsync(org, duty);
            var reply = await (await member.Client.PostAsJsonAsync($"/api/v1/client/orgs/{org}/threads/{threadId}/messages", new { body = $"{duty} here" }))
                .ReadJsonAsync();
            Assert.True(reply.GetProperty("canReply").GetBoolean());
        }
        var final = await (await am.Client.GetAsync($"/api/v1/agency/clients/{org}/threads/{threadId}")).ReadJsonAsync();
        Assert.Equal(3, final.GetProperty("messages").GetArrayLength());
    }

    // ------------------------------------------------------------------ onboarding on the client's behalf

    [Fact]
    public async Task Staff_tick_and_untick_client_steps_on_the_clients_behalf_audited_and_visible_to_the_client()
    {
        var am = await api.StaffAsync();
        var org = await api.CreateOrgAsync(am.Client, "On behalf");
        var otherOrg = await api.CreateOrgAsync(am.Client, "On behalf other");
        var owner = await api.ClientUserAsync(org, ClientMemberRole.Owner);
        var items = (await (await am.Client.GetAsync($"/api/v1/agency/clients/{org}/onboarding")).ReadJsonAsync()).GetProperty("items").EnumerateArray().ToList();
        var ga4 = items.First(i => i.GetProperty("key").GetString() == "ga4-access").GetProperty("id").GetGuid();
        var kickoff = items.First(i => i.GetProperty("key").GetString() == "kickoff-call").GetProperty("id").GetGuid();
        var url = $"/api/v1/agency/clients/{org}/onboarding/{ga4}/on-behalf";
        var amName = await api.WithDbAsync(db => db.Set<User>().Where(u => u.Id == am.User.Id).Select(u => u.DisplayName).SingleAsync());

        // The plain status update refuses to tick a client step for them; the on-behalf action is the way.
        await (await am.Client.PutAsJsonAsync($"/api/v1/agency/clients/{org}/onboarding/{ga4}", new { status = "Done" }))
            .ShouldFailAsync(400, "onboarding.client_item");
        // Agency steps aren't "on behalf"; other clients' ids and missing permissions are refused.
        await (await am.Client.PostAsJsonAsync($"/api/v1/agency/clients/{org}/onboarding/{kickoff}/on-behalf", new { done = true }))
            .ShouldFailAsync(400, "onboarding.not_client_item");
        await (await am.Client.PostAsJsonAsync($"/api/v1/agency/clients/{otherOrg}/onboarding/{ga4}/on-behalf", new { done = true })).ShouldFailAsync(404);
        var strategist = await api.StaffAsync(Role.Strategist); // clients.view, not clients.manage
        await (await strategist.Client.PostAsJsonAsync(url, new { done = true })).ShouldFailAsync(403);
        await (await owner.Client.PostAsJsonAsync(url, new { done = true })).ShouldFailAsync(403);
        await (await am.Client.PostAsJsonAsync(url, new { })).ShouldFailAsync(400);

        var done = await (await am.Client.PostAsJsonAsync(url, new { done = true, note = "Confirmed on the kickoff call" })).ReadJsonAsync();
        var item = done.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("id").GetGuid() == ga4);
        Assert.Equal("Done", item.GetProperty("status").GetString());
        Assert.True(item.GetProperty("completedOnBehalfOfClient").GetBoolean());
        Assert.Equal(amName, item.GetProperty("completedBy").GetString());
        Assert.Equal("Confirmed on the kickoff call", item.GetProperty("note").GetString());
        var audit = await api.WithDbAsync(db => db.Set<AuditLog>().AsNoTracking()
            .SingleAsync(a => a.Action == "client.onboarding_item_completed_on_behalf" && a.EntityId == ga4.ToString()));
        Assert.Equal(am.User.Id, audit.ActorUserId);
        Assert.Equal("Completed by staff on behalf of client", audit.Reason);
        // Repeating is a no-op (no second audit row).
        (await am.Client.PostAsJsonAsync(url, new { done = true })).EnsureSuccessStatusCode();
        Assert.Equal(1, await api.WithDbAsync(db => db.Set<AuditLog>().CountAsync(a => a.Action == "client.onboarding_item_completed_on_behalf" && a.EntityId == ga4.ToString())));

        // The client sees who completed it, flagged as done on their behalf (portal checklist and home).
        var portal = await (await owner.Client.GetAsync($"/api/v1/client/orgs/{org}/onboarding")).ReadJsonAsync();
        var seen = portal.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("id").GetGuid() == ga4);
        Assert.True(seen.GetProperty("completedOnBehalfOfClient").GetBoolean());
        Assert.Equal(amName, seen.GetProperty("completedBy").GetString());
        var home = await (await owner.Client.GetAsync($"/api/v1/client/orgs/{org}/home")).ReadJsonAsync();
        Assert.Contains(home.GetProperty("onboarding").GetProperty("items").EnumerateArray(),
            i => i.GetProperty("id").GetGuid() == ga4 && i.GetProperty("completedOnBehalfOfClient").GetBoolean());

        // Un-tick on their behalf: back to Pending, flag cleared, audited.
        var undone = await (await am.Client.PostAsJsonAsync(url, new { done = false })).ReadJsonAsync();
        var reopened = undone.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("id").GetGuid() == ga4);
        Assert.Equal("Pending", reopened.GetProperty("status").GetString());
        Assert.False(reopened.GetProperty("completedOnBehalfOfClient").GetBoolean());
        Assert.True(await AuditedAsync("client.onboarding_item_reopened_on_behalf", ga4));

        // When the client ticks it themselves, it is not "on behalf".
        var own = await (await owner.Client.PutAsJsonAsync($"/api/v1/client/orgs/{org}/onboarding/{ga4}", new { status = "Done" })).ReadJsonAsync();
        Assert.False(own.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("id").GetGuid() == ga4).GetProperty("completedOnBehalfOfClient").GetBoolean());
    }

    // ------------------------------------------------------------------ removing brand assets and task attachments

    [Fact]
    public async Task Removing_a_brand_asset_is_permission_gated_audited_and_deletes_the_unused_file()
    {
        var am = await api.StaffAsync();
        var org = await api.CreateOrgAsync(am.Client, "Brand removal");
        var otherOrg = await api.CreateOrgAsync(am.Client, "Brand removal other");
        var owner = await api.ClientUserAsync(org, ClientMemberRole.Owner);

        async Task<(Guid AssetId, Guid FileId)> AddAssetAsync(string name)
        {
            using var form = new MultipartFormDataContent
            {
                { new ByteArrayContent(DemoPng.Creative(20, 20, name.Length)), "file", name }, { new StringContent("Logo"), "kind" },
            };
            var kit = await (await am.Client.PostAsync($"/api/v1/agency/clients/{org}/brand-kit/assets", form)).ReadJsonAsync();
            var asset = kit.GetProperty("assets").EnumerateArray().Single(a => a.GetProperty("fileName").GetString() == name);
            return (asset.GetProperty("id").GetGuid(), asset.GetProperty("fileId").GetGuid());
        }

        var (assetId, fileId) = await AddAssetAsync("logo-a.png");
        (await owner.Client.GetAsync($"/api/v1/client/orgs/{org}/files/{fileId}")).EnsureSuccessStatusCode();

        var designer = await api.StaffAsync(Role.Designer); // deliverables.submit, not clients.manage
        await (await designer.Client.DeleteAsync($"/api/v1/agency/clients/{org}/brand-kit/assets/{assetId}")).ShouldFailAsync(403);
        await (await am.Client.DeleteAsync($"/api/v1/agency/clients/{otherOrg}/brand-kit/assets/{assetId}")).ShouldFailAsync(404);
        await (await owner.Client.DeleteAsync($"/api/v1/agency/clients/{org}/brand-kit/assets/{assetId}")).ShouldFailAsync(403);

        var after = await (await am.Client.DeleteAsync($"/api/v1/agency/clients/{org}/brand-kit/assets/{assetId}")).ReadJsonAsync();
        Assert.DoesNotContain(after.GetProperty("assets").EnumerateArray(), a => a.GetProperty("id").GetGuid() == assetId);
        Assert.True(await AuditedAsync("client.brand_asset_removed", assetId));
        Assert.False(await FileExistsAsync(fileId));
        await (await owner.Client.GetAsync($"/api/v1/client/orgs/{org}/files/{fileId}")).ShouldFailAsync(404);
        await (await am.Client.GetAsync($"/api/v1/agency/files/{fileId}")).ShouldFailAsync(404);
        // Removing it again is a 404.
        await (await am.Client.DeleteAsync($"/api/v1/agency/clients/{org}/brand-kit/assets/{assetId}")).ShouldFailAsync(404);

        // A file that is also shared in a message is kept (only the brand-kit entry goes).
        var (keptAsset, keptFile) = await AddAssetAsync("logo-shared.png");
        (await am.Client.PostAsJsonAsync($"/api/v1/agency/clients/{org}/threads", new { subject = "Logo", body = "Attached", attachmentFileIds = new[] { keptFile } }))
            .EnsureSuccessStatusCode();
        (await am.Client.DeleteAsync($"/api/v1/agency/clients/{org}/brand-kit/assets/{keptAsset}")).EnsureSuccessStatusCode();
        Assert.True(await FileExistsAsync(keptFile));
        (await owner.Client.GetAsync($"/api/v1/client/orgs/{org}/files/{keptFile}")).EnsureSuccessStatusCode();

        // Concurrent removals of the same asset: exactly one succeeds, the other is a 404.
        var (raced, racedFile) = await AddAssetAsync("logo-race.png");
        var am2 = await api.LoginAsync(am.User);
        var results = await Task.WhenAll(
            am.Client.DeleteAsync($"/api/v1/agency/clients/{org}/brand-kit/assets/{raced}"),
            am2.DeleteAsync($"/api/v1/agency/clients/{org}/brand-kit/assets/{raced}"));
        Assert.Equal(new[] { HttpStatusCode.OK, HttpStatusCode.NotFound }, results.Select(r => r.StatusCode).OrderBy(s => (int)s).ToArray());
        Assert.False(await FileExistsAsync(racedFile));
    }

    [Fact]
    public async Task Removing_a_task_attachment_is_tenant_scoped_audited_and_deletes_the_unused_file()
    {
        var am = await api.StaffAsync();
        var org = await api.CreateOrgAsync(am.Client, "Attachment removal");
        var otherOrg = await api.CreateOrgAsync(am.Client, "Attachment removal other");
        var projectId = (await am.Client.CreateProjectAsync(org)).GetProperty("id").GetGuid();
        var otherProject = (await am.Client.CreateProjectAsync(otherOrg)).GetProperty("id").GetGuid();
        var task = await (await am.Client.PostAsJsonAsync($"/api/v1/agency/projects/{projectId}/tasks", new { title = "Keyword research" })).ReadJsonAsync();
        var taskId = task.GetProperty("task").GetProperty("id").GetGuid();
        var otherTask = await (await am.Client.PostAsJsonAsync($"/api/v1/agency/projects/{otherProject}/tasks", new { title = "Other" })).ReadJsonAsync();
        var otherTaskId = otherTask.GetProperty("task").GetProperty("id").GetGuid();

        var fileId = (await UploadAsync(am.Client, org, DemoPng.Creative(25, 25, 7), "gap.png")).GetProperty("id").GetGuid();
        var attached = await (await am.Client.PostAsJsonAsync($"/api/v1/agency/tasks/{taskId}/attachments", new { fileId })).ReadJsonAsync();
        var attachmentId = attached.GetProperty("attachments")[0].GetProperty("id").GetGuid();
        Assert.True(await AuditedAsync("task.attachment_added", taskId));

        var client = await api.ClientUserAsync(org, ClientMemberRole.Owner);
        await (await client.Client.DeleteAsync($"/api/v1/agency/tasks/{taskId}/attachments/{attachmentId}")).ShouldFailAsync(403);
        await (await am.Client.DeleteAsync($"/api/v1/agency/tasks/{otherTaskId}/attachments/{attachmentId}")).ShouldFailAsync(404);

        var designer = await api.StaffAsync(Role.Designer); // deliverables.submit
        var after = await (await designer.Client.DeleteAsync($"/api/v1/agency/tasks/{taskId}/attachments/{attachmentId}")).ReadJsonAsync();
        Assert.Equal(0, after.GetProperty("attachments").GetArrayLength());
        Assert.True(await api.WithDbAsync(db => db.Set<AuditLog>().AnyAsync(a =>
            a.Action == "task.attachment_removed" && a.EntityId == taskId.ToString() && a.ActorUserId == designer.User.Id)));
        Assert.False(await FileExistsAsync(fileId));
        await (await am.Client.DeleteAsync($"/api/v1/agency/tasks/{taskId}/attachments/{attachmentId}")).ShouldFailAsync(404);

        // A file attached to two tasks survives the first removal.
        var shared = (await UploadAsync(am.Client, org, DemoPng.Creative(26, 26, 8), "shared.png")).GetProperty("id").GetGuid();
        var first = await (await am.Client.PostAsJsonAsync($"/api/v1/agency/tasks/{taskId}/attachments", new { fileId = shared })).ReadJsonAsync();
        var task2 = await (await am.Client.PostAsJsonAsync($"/api/v1/agency/projects/{projectId}/tasks", new { title = "Second" })).ReadJsonAsync();
        (await am.Client.PostAsJsonAsync($"/api/v1/agency/tasks/{task2.GetProperty("task").GetProperty("id").GetGuid()}/attachments", new { fileId = shared }))
            .EnsureSuccessStatusCode();
        (await am.Client.DeleteAsync($"/api/v1/agency/tasks/{taskId}/attachments/{first.GetProperty("attachments")[0].GetProperty("id").GetGuid()}"))
            .EnsureSuccessStatusCode();
        Assert.True(await FileExistsAsync(shared));
    }

    // ------------------------------------------------------------------ impersonation

    [Fact]
    public async Task Impersonating_an_account_manager_cannot_act_for_the_client_or_delete_files()
    {
        var (_, admin) = await api.CreateClientAsync(Role.Admin);
        var am = await api.StaffAsync();
        var org = await api.CreateOrgAsync(am.Client, "Impersonated");
        var items = (await (await am.Client.GetAsync($"/api/v1/agency/clients/{org}/onboarding")).ReadJsonAsync()).GetProperty("items").EnumerateArray().ToList();
        var ga4 = items.First(i => i.GetProperty("key").GetString() == "ga4-access").GetProperty("id").GetGuid();
        var projectId = (await am.Client.CreateProjectAsync(org)).GetProperty("id").GetGuid();
        var task = await (await am.Client.PostAsJsonAsync($"/api/v1/agency/projects/{projectId}/tasks", new { title = "Task T" })).ReadJsonAsync();
        var taskId = task.GetProperty("task").GetProperty("id").GetGuid();
        var fileId = (await UploadAsync(am.Client, org, DemoPng.Creative(21, 21, 9), "a.png")).GetProperty("id").GetGuid();
        var attached = await (await am.Client.PostAsJsonAsync($"/api/v1/agency/tasks/{taskId}/attachments", new { fileId })).ReadJsonAsync();
        var attachmentId = attached.GetProperty("attachments")[0].GetProperty("id").GetGuid();
        using var form = new MultipartFormDataContent { { new ByteArrayContent(DemoPng.Creative(22, 22, 5)), "file", "logo.png" }, { new StringContent("Logo"), "kind" } };
        var kit = await (await am.Client.PostAsync($"/api/v1/agency/clients/{org}/brand-kit/assets", form)).ReadJsonAsync();
        var assetId = kit.GetProperty("assets")[0].GetProperty("id").GetGuid();

        var token = await Impersonating.TokenAsync(admin, am.User.Id);
        await (await Impersonating.SendAsync(admin, HttpMethod.Post, $"/api/v1/agency/clients/{org}/onboarding/{ga4}/on-behalf", token, new { done = true }))
            .ShouldFailAsync(403, ImpersonationForbidden);
        await (await Impersonating.SendAsync(admin, HttpMethod.Delete, $"/api/v1/agency/clients/{org}/brand-kit/assets/{assetId}", token))
            .ShouldFailAsync(403, ImpersonationForbidden);
        await (await Impersonating.SendAsync(admin, HttpMethod.Delete, $"/api/v1/agency/tasks/{taskId}/attachments/{attachmentId}", token))
            .ShouldFailAsync(403, ImpersonationForbidden);
        // Reads stay available; nothing changed.
        (await Impersonating.SendAsync(admin, HttpMethod.Get, $"/api/v1/agency/clients/{org}/onboarding", token)).EnsureSuccessStatusCode();
        Assert.True(await FileExistsAsync(fileId));
        Assert.True(await api.WithDbAsync(db => db.Set<BrandAsset>().AnyAsync(a => a.Id == assetId)));
        Assert.Equal(OnboardingItemStatus.Pending, await api.WithDbAsync(db => db.Set<ClientOnboardingItem>().Where(i => i.Id == ga4).Select(i => i.Status).SingleAsync()));
    }
}
