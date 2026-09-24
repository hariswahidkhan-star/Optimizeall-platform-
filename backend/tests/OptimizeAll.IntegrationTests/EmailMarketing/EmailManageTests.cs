using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Domain.Audit;
using OptimizeAll.Domain.EmailMarketing;
using OptimizeAll.Domain.Identity;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.EmailMarketing;

/// <summary>Restore/duplicate/delete of lists, templates and journeys, and workspace tag &amp; field management.</summary>
[Collection(EmailCollection.Name)]
public sealed class EmailManageTests(EmailFixture fx)
{
    [Fact]
    public async Task Archived_lists_and_templates_can_be_listed_and_restored()
    {
        var staff = await fx.StaffAsync();
        var ws = await fx.CreateWorkspaceAsync();
        Assert.Equal(HttpStatusCode.NoContent, (await staff.DeleteAsync($"/api/v1/agency/email/lists/{ws.ListId}")).StatusCode);
        var restored = await (await staff.PostAsync($"/api/v1/agency/email/lists/{ws.ListId}/restore", null)).ReadJsonAsync();
        Assert.False(restored.GetProperty("isArchived").GetBoolean());
        await (await staff.PostAsync($"/api/v1/agency/email/lists/{Guid.NewGuid()}/restore", null)).ShouldFailAsync(404);

        var template = await (await staff.PostAsJsonAsync("/api/v1/agency/email/templates", new
        {
            clientAccountId = ws.ClientId, name = "Promo", subject = "Deals", design = EmailFixture.Design(),
        })).ReadJsonAsync();
        var templateId = template.GetProperty("id").GetGuid();
        (await staff.DeleteAsync($"/api/v1/agency/email/templates/{templateId}")).EnsureSuccessStatusCode();
        var visible = await (await staff.GetAsync($"/api/v1/agency/email/templates?clientId={ws.ClientId}")).ReadJsonAsync();
        Assert.DoesNotContain(visible.EnumerateArray(), t => t.GetProperty("id").GetGuid() == templateId);
        var all = await (await staff.GetAsync($"/api/v1/agency/email/templates/all?clientId={ws.ClientId}&includeGlobal=false")).ReadJsonAsync();
        Assert.True(all.EnumerateArray().Single(t => t.GetProperty("id").GetGuid() == templateId).GetProperty("isArchived").GetBoolean());
        var back = await (await staff.PostAsync($"/api/v1/agency/email/templates/{templateId}/restore", null)).ReadJsonAsync();
        Assert.False(back.GetProperty("isArchived").GetBoolean());

        var (_, client) = await fx.ClientUserAsync(ws.ClientId, OptimizeAll.Domain.Agency.ClientMemberRole.Owner);
        await (await client.PostAsync($"/api/v1/agency/email/templates/{templateId}/restore", null)).ShouldFailAsync(403);
    }

    [Fact]
    public async Task Journeys_can_be_duplicated_restored_and_deleted_only_without_history()
    {
        var staff = await fx.StaffAsync();
        var ws = await fx.CreateWorkspaceAsync();
        var journey = await (await staff.PostAsJsonAsync("/api/v1/agency/email/automations", new
        {
            clientAccountId = ws.ClientId, name = "Tagged", trigger = "TagAdded", triggerConfig = new { tag = "vip" }, reentry = "Never",
            steps = new object[] { new { key = "s1", type = "Wait", config = new { days = 1 } } },
        })).ReadJsonAsync();
        var id = journey.GetProperty("id").GetGuid();

        var copy = await (await staff.PostAsync($"/api/v1/agency/email/automations/{id}/duplicate", null)).ReadJsonAsync();
        Assert.Equal("Tagged (copy)", copy.GetProperty("name").GetString());
        Assert.Equal("Draft", copy.GetProperty("status").GetString());
        Assert.Single(copy.GetProperty("steps").EnumerateArray());

        await (await staff.PostAsync($"/api/v1/agency/email/automations/{id}/restore", null)).ShouldFailAsync(409, "email.automation_not_archived");
        (await staff.PostAsync($"/api/v1/agency/email/automations/{id}/archive", null)).EnsureSuccessStatusCode();
        var archivedList = await (await staff.GetAsync($"/api/v1/agency/email/automations/all?clientId={ws.ClientId}")).ReadJsonAsync();
        Assert.Contains(archivedList.EnumerateArray(), a => a.GetProperty("id").GetGuid() == id && a.GetProperty("status").GetString() == "Archived");
        var restored = await (await staff.PostAsync($"/api/v1/agency/email/automations/{id}/restore", null)).ReadJsonAsync();
        Assert.Equal("Paused", restored.GetProperty("status").GetString());

        // A journey someone entered keeps its history.
        var subscriber = (await fx.AddSubscribersAsync(ws, 1))[0];
        await fx.Db(async db =>
        {
            db.Add(new AutomationEnrollment { AutomationId = id, ClientAccountId = ws.ClientId, SubscriberId = subscriber, Status = EnrollmentStatus.Exited, EnteredAt = fx.Now, NextRunAt = fx.Now });
            await db.SaveChangesAsync();
        });
        await (await staff.DeleteAsync($"/api/v1/agency/email/automations/{id}")).ShouldFailAsync(409, "email.automation_has_history");
        var copyId = copy.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent, (await staff.DeleteAsync($"/api/v1/agency/email/automations/{copyId}")).StatusCode);
        await (await staff.GetAsync($"/api/v1/agency/email/automations/{copyId}")).ShouldFailAsync(404);
        Assert.True(await fx.Db(db => db.Set<AuditLog>().AnyAsync(a => a.Action == "email.automation.deleted" && a.EntityId == copyId.ToString())));
    }

    [Fact]
    public async Task Tags_and_fields_can_be_renamed_merged_and_deleted_per_workspace()
    {
        var staff = await fx.StaffAsync();
        var ws = await fx.CreateWorkspaceAsync();
        var other = await fx.CreateWorkspaceAsync();
        var ids = await fx.AddSubscribersAsync(ws, 3);
        var otherIds = await fx.AddSubscribersAsync(other, 1);
        await fx.Db(async db =>
        {
            db.Add(new SubscriberTag { SubscriberId = ids[0], Tag = "vip", AddedAt = fx.Now });
            db.Add(new SubscriberTag { SubscriberId = ids[1], Tag = "vip", AddedAt = fx.Now });
            db.Add(new SubscriberTag { SubscriberId = ids[1], Tag = "gold", AddedAt = fx.Now });
            db.Add(new SubscriberTag { SubscriberId = otherIds[0], Tag = "vip", AddedAt = fx.Now });
            db.Add(new SubscriberField { SubscriberId = ids[2], Key = "plan", Value = "pro" });
            await db.SaveChangesAsync();
        });

        var tags = await (await staff.GetAsync($"/api/v1/agency/email/tags?clientId={ws.ClientId}")).ReadJsonAsync();
        Assert.Equal(2, tags.EnumerateArray().Single(t => t.GetProperty("key").GetString() == "vip").GetProperty("contacts").GetInt32());

        await (await staff.PostAsJsonAsync("/api/v1/agency/email/tags/rename", new { clientAccountId = ws.ClientId, from = "vip", to = "!!" })).ShouldFailAsync(400);
        await (await staff.PostAsJsonAsync("/api/v1/agency/email/tags/rename", new { clientAccountId = ws.ClientId, from = "missing", to = "x" })).ShouldFailAsync(404);
        var renamed = await (await staff.PostAsJsonAsync("/api/v1/agency/email/tags/rename", new { clientAccountId = ws.ClientId, from = "vip", to = "gold" })).ReadJsonAsync();
        Assert.Equal(1, renamed.GetProperty("changed").GetInt32());
        Assert.Equal(1, renamed.GetProperty("merged").GetInt32());
        tags = await (await staff.GetAsync($"/api/v1/agency/email/tags?clientId={ws.ClientId}")).ReadJsonAsync();
        Assert.Equal(new[] { "gold" }, tags.EnumerateArray().Select(t => t.GetProperty("key").GetString()));
        // The other workspace is untouched.
        Assert.True(await fx.Db(db => db.Set<SubscriberTag>().AnyAsync(t => t.SubscriberId == otherIds[0] && t.Tag == "vip")));

        var removed = await (await staff.DeleteAsync($"/api/v1/agency/email/tags?clientId={ws.ClientId}&tag=gold")).ReadJsonAsync();
        Assert.Equal(2, removed.GetProperty("removed").GetInt32());
        await (await staff.DeleteAsync($"/api/v1/agency/email/tags?clientId={ws.ClientId}&tag=gold")).ShouldFailAsync(404);

        var field = await (await staff.PostAsJsonAsync("/api/v1/agency/email/fields/rename", new { clientAccountId = ws.ClientId, from = "plan", to = "tier" })).ReadJsonAsync();
        Assert.Equal(1, field.GetProperty("changed").GetInt32());
        await (await staff.PostAsJsonAsync("/api/v1/agency/email/fields/rename", new { clientAccountId = ws.ClientId, from = "tier", to = "email" })).ShouldFailAsync(400);
        var fields = await (await staff.GetAsync($"/api/v1/agency/email/fields?clientId={ws.ClientId}")).ReadJsonAsync();
        Assert.Equal("tier", fields[0].GetProperty("key").GetString());
        (await staff.DeleteAsync($"/api/v1/agency/email/fields?clientId={ws.ClientId}&key=tier")).EnsureSuccessStatusCode();
        await (await staff.GetAsync($"/api/v1/agency/email/tags?clientId={Guid.NewGuid()}")).ShouldFailAsync(404);

        var designer = await fx.StaffAsync(Role.Designer);
        await (await designer.GetAsync($"/api/v1/agency/email/tags?clientId={ws.ClientId}")).ShouldFailAsync(403);
    }
}
